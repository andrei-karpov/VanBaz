using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace VanBaz;

public record Distribution(string Version, string File, long Size, string Targets)
{
    public override string ToString() => Version + " · " + Path.GetFileName(File) + " · с версий: " + Targets;
}
public static class ConfigurationService
{
    public static string GeneratedUrl(ConfigurationSource source)
    {
        string Part(string value) => Uri.EscapeDataString(value.Trim().Replace(".", ""));
        if (source.ShortName.Length == 0 || source.Edition.Length == 0 || source.Platform.Length == 0) throw new ArgumentException("Заполните короткое имя, редакцию и платформу.");
        return $"https://downloads.1c.ru/ipp/ITSREPV/V8Update/Configs/{Uri.EscapeDataString(source.ShortName.Trim())}/{Part(source.Edition)}/{Part(source.Platform)}/UpdInfo.txt";
    }
    public static void ImportExistingSources(Catalog catalog)
    {
        string folder = catalog.Migration?.SettingsFolder ?? "";
        string file = Path.Combine(folder, "v8updates.lst");
        if (File.Exists(file))
        {
            foreach (var section in StartManagerMigration.ReadIni(file).Where(s => s.Key.Length > 0))
            {
                var fields = section.Value;
                if (!fields.ContainsKey("ConfigName") || catalog.Configurations.Any(c => c.Id == section.Key)) continue;
                var source = new ConfigurationSource { Id = section.Key, Name = fields.GetValueOrDefault("Descr", section.Key), Country = fields.GetValueOrDefault("Country", ""), ShortName = fields["ConfigName"],
                    Edition = fields.GetValueOrDefault("ConfigRelease", ""), Platform = fields.GetValueOrDefault("PlatformVer", "") };
                string link = fields.GetValueOrDefault("Link", "");
                if (fields.GetValueOrDefault("UseLink") == "1" && link.Length > 0)
                    source.InfoUrl = Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? link : "https://downloads.1c.ru/ipp/ITSREPV/V8Update/Configs/" + link.Trim('/') + "/UpdInfo.txt";
                else if (source.ShortName.Length > 0 && source.Edition.Length > 0 && source.Platform.Length > 0) source.InfoUrl = GeneratedUrl(source);
                catalog.Configurations.Add(source);
            }
        }
        foreach (var b in catalog.Bases)
        {
            string id = b.SourceSettings.GetValueOrDefault("ConfigId", "");
            if (b.ConfigurationId.Length == 0 && catalog.Configurations.Any(c => c.Id == id)) b.ConfigurationId = id;
            if (b.UpdateInfoUrl.Length == 0) continue;
            var source = catalog.Configurations.FirstOrDefault(c => c.InfoUrl == b.UpdateInfoUrl);
            if (source == null) { source = new() { Name = b.ConfigurationName.Length > 0 ? b.ConfigurationName : b.Name, InfoUrl = b.UpdateInfoUrl }; catalog.Configurations.Add(source); }
            if (b.ConfigurationId.Length == 0) b.ConfigurationId = source.Id;
        }
        var settings = catalog.Settings.SourceSettings;
        if (catalog.Settings.ProxyUrl.Length == 0 && settings.GetValueOrDefault("ProxyEnabled") == "1")
        {
            string server = settings.GetValueOrDefault("ProxyServer", "").Trim();
            string port = settings.GetValueOrDefault("ProxyPort", "");
            if (server.Length > 0 && !server.Contains("://")) server = "http://" + server;
            if (Uri.TryCreate(server, UriKind.Absolute, out var proxy) && proxy.Scheme is "http" or "https" && proxy.UserInfo.Length == 0)
            { var builder = new UriBuilder(proxy); if (int.TryParse(port, out int p) && p is > 0 and <= 65535) builder.Port = p; catalog.Settings.ProxyUrl = builder.Uri.AbsoluteUri; }
        }
    }
    public static Uri Https(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new ArgumentException("Нужен HTTPS-адрес без имени пользователя и пароля.");
        return uri;
    }
    public static HttpClient Client(Preferences settings, Uri? authOrigin = null, NetworkCredential? credentials = null)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (settings.ProxyMode == "Без прокси") handler.UseProxy = false;
        if ((settings.ProxyMode is "Указать прокси" or "Авто") && settings.ProxyUrl.Length > 0)
        {
            if (!Uri.TryCreate(settings.ProxyUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0) throw new ArgumentException("Проверьте адрес прокси.");
            var proxy = new WebProxy(uri) { UseDefaultCredentials = settings.ProxyWindowsCredentials };
            if (!settings.ProxyWindowsCredentials && settings.ProxyUser.Length > 0) proxy.Credentials = new NetworkCredential(settings.ProxyUser, SecretStore.Reveal(settings.ProxySecret));
            handler.Proxy = proxy;
        }
        if (credentials != null && authOrigin != null)
        {
            // Scope challenge credentials to this origin; never forward them to a redirected host.
            var cache = new CredentialCache();
            cache.Add(new Uri(authOrigin.GetLeftPart(UriPartial.Authority) + "/"), "Basic", credentials);
            handler.Credentials = cache;
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    }
    public static async Task<HttpResponseMessage> Get(HttpClient client, Uri uri, CancellationToken cancel, bool allowSameOriginRedirect = true)
    {
        var origin = uri;
        for (int i = 0; i < 6; i++)
        {
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location == null) throw new IOException("Сервер не указал адрес перенаправления.");
                var next = Https(new Uri(uri, location).AbsoluteUri);
                if (!allowSameOriginRedirect || next.Authority != origin.Authority) throw new IOException("Сервер перенаправляет на другой адрес. Откройте сайт поставщика в браузере или укажите конечный HTTPS-адрес в каталоге.");
                uri = next; continue;
            }
            if (!response.IsSuccessStatusCode) { var status = response.StatusCode; response.Dispose(); throw new IOException("Ответ сервера: " + (int)status + " " + status); }
            return response;
        }
        throw new IOException("Слишком много перенаправлений.");
    }
    public static async Task<byte[]> Bounded(Stream stream, long maximum, CancellationToken cancel)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536]; int n;
        while ((n = await stream.ReadAsync(buffer, cancel)) > 0) { if (output.Length + n > maximum) throw new IOException("Файл превышает допустимый размер."); await output.WriteAsync(buffer.AsMemory(0, n), cancel); }
        return output.ToArray();
    }
    public static List<Distribution> ParseDistributions(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8 * 1024 * 1024 });
        var document = XDocument.Load(reader); XNamespace ns = "http://v8.1c.ru/configuration-updates";
        return document.Descendants(ns + "update").Select(x => new Distribution((string?)x.Element(ns + "version") ?? "", (string?)x.Element(ns + "file") ?? "",
            long.TryParse(((string?)x.Element(ns + "size") ?? "").Replace(" ", "").Replace("\u00a0", ""), out long size) ? size : 0,
            string.Join("; ", x.Elements(ns + "target").Select(v => v.Value))))
            .Where(x => Version.TryParse(x.Version, out _) && x.File.Length > 0 && x.Size >= 0).OrderByDescending(x => new Version(x.Version)).ToList();
    }
    public static async Task<List<Distribution>> Distributions(ConfigurationSource source, Preferences settings)
    {
        var url = new Uri(Https(source.InfoUrl), "v8upd11.zip"); using var client = Client(settings);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var response = await Get(client, url, timeout.Token);
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var bytes = new MemoryStream(await Bounded(input, 4 * 1024 * 1024, timeout.Token));
        using var zip = new ZipArchive(bytes);
        var entry = zip.Entries.SingleOrDefault(e => e.Name.Equals("v8cscdsc.xml", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("В архиве нет v8cscdsc.xml.");
        if (entry.Length > 8 * 1024 * 1024) throw new IOException("Слишком большой список дистрибутивов.");
        using var xml = entry.Open(); return ParseDistributions(xml);
    }
    public static Uri DistributionUri(string root, string relative)
    {
        var basis = Https(root.TrimEnd('/') + "/");
        if (basis.Query.Length > 0 || basis.Fragment.Length > 0) throw new InvalidDataException("Корень загрузок не должен содержать запрос или фрагмент.");
        if (Uri.TryCreate(relative, UriKind.Absolute, out _) || relative.Contains('\\') || relative.Split('/').Any(p => Uri.UnescapeDataString(p) is ".." or ".")) throw new InvalidDataException("Некорректный путь дистрибутива.");
        var uri = Https(new Uri(basis, relative).AbsoluteUri);
        if (!uri.AbsoluteUri.StartsWith(basis.AbsoluteUri, StringComparison.Ordinal)) throw new InvalidDataException("Дистрибутив вне заданного каталога.");
        return uri;
    }
    public static async Task Download(Uri uri, string destination, long expected, Preferences settings, NetworkCredential? credentials, CancellationToken cancel)
    {
        Https(uri.AbsoluteUri);
        if (expected < 0 || expected > 4L * 1024 * 1024 * 1024) throw new IOException("Допустимый размер дистрибутива — до 4 ГБ.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromMinutes(30)); cancel = timeout.Token;
        string pending = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var client = Client(settings, uri, credentials); using var response = await Get(client, uri, cancel);
            string type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (type.Contains("html", StringComparison.OrdinalIgnoreCase)) throw new IOException("Сервер вернул страницу входа вместо дистрибутива.");
            using var input = await response.Content.ReadAsStreamAsync(cancel);
            long maximum = expected > 0 ? expected : 4L * 1024 * 1024 * 1024;
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var buffer = new byte[65536]; int n;
                while ((n = await input.ReadAsync(buffer, cancel)) > 0) { if (output.Length + n > maximum) throw new IOException("Размер загрузки превышает ожидаемый."); await output.WriteAsync(buffer.AsMemory(0, n), cancel); }
                if (output.Length == 0 || expected > 0 && output.Length != expected) throw new IOException("Размер скачанного файла не совпадает со списком обновлений.");
                output.Flush(true);
            }
            File.Move(pending, destination, false);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
}
