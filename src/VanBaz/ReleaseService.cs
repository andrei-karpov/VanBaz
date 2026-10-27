using System.IO;
using System.Net.Http;
using System.Text;

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
    public static async Task<ReleaseInfo> Fetch(string url, Preferences? settings = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) throw new ArgumentException("Укажите HTTPS-адрес файла UpdInfo.txt без имени пользователя и пароля.");
        using var client = ConfigurationService.Client(settings ?? new());
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var response = await ConfigurationService.Get(client, uri, timeout.Token);
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        byte[] bytes = await ConfigurationService.Bounded(stream, 1024 * 1024, timeout.Token);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text; try { text = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { text = Encoding.GetEncoding(1251).GetString(bytes); }
        return Parse(text);
    }
}
