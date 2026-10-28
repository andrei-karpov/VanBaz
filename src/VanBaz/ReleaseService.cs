using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace VanBaz;

public record ReleaseInfo(string Version, string Date, string FromVersions);
public static class ReleaseService
{
    public static ReleaseInfo Parse(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n'))
        {
            int equals = line.IndexOf('='); if (equals <= 0) continue;
            fields[line[..equals].Trim().Trim('\uFEFF')] = line[(equals + 1)..].Trim().Trim('"');
        }
        string version = fields.GetValueOrDefault("Version", "");
        if (!System.Version.TryParse(version, out _)) throw new InvalidDataException("Источник не содержит поле Version с номером релиза.");
        return new(version, fields.GetValueOrDefault("UpdateDate", ""), fields.GetValueOrDefault("FromVersions", ""));
    }
    public static async Task<ReleaseInfo> Fetch(string url, Preferences? settings = null, CancellationToken cancel = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new ArgumentException("Укажите HTTPS-адрес файла UpdInfo.txt без имени пользователя и пароля.");
        using var client = ConfigurationService.Client(settings ?? new());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await ConfigurationService.Get(client, uri, timeout.Token);
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        byte[] bytes = await ConfigurationService.Bounded(stream, 1024 * 1024, timeout.Token);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text; try { text = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { text = Encoding.GetEncoding(1251).GetString(bytes); }
        return Parse(text);
    }
    public static async Task<ReleaseInfo> ReadLocal(string path, CancellationToken cancel = default)
    {
        path = Path.GetFullPath(path); CacheService.EnsureOrdinaryPath(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        byte[] bytes = await ConfigurationService.Bounded(file, 1024 * 1024, cancel);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text; try { text = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { text = Encoding.GetEncoding(1251).GetString(bytes); }
        return Parse(text);
    }
    public static string Compare(ReleaseInfo info, string currentVersion)
    {
        if (!System.Version.TryParse(currentVersion, out var current)) return "Текущая версия не задана: сравнение не выполнено.";
        int comparison = System.Version.Parse(info.Version).CompareTo(current);
        if (comparison == 0) return "Номера версий совпадают.";
        if (comparison < 0) return "В источнике указан более старый релиз.";
        string compatibility;
        var from = info.FromVersions.Split(new[] { ';', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (from.Length == 0) compatibility = "Источник не сообщает, с каких версий возможно обновление.";
        else if (from.Any(value => System.Version.TryParse(value, out var parsed) && parsed == current)) compatibility = "Текущая версия перечислена среди исходных версий обновления.";
        else if (from.All(value => System.Version.TryParse(value, out _))) compatibility = "Текущая версия не перечислена среди исходных: прямое обновление этим релизом не подтверждено.";
        else compatibility = "Формат исходных версий не распознан: совместимость не установлена.";
        return "Доступен более новый релиз. " + compatibility;
    }
}
