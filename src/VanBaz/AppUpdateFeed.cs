using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace VanBaz;

public sealed class AppRelease
{
    public string Version { get; set; } = "";
    public string PackageUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public string Notes { get; set; } = "";
}
public static class AppUpdateFeed
{
    public static AppRelease Parse(string text, Uri source)
    {
        ConfigurationService.Https(source.AbsoluteUri);
        var release = JsonSerializer.Deserialize<AppRelease>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Пустой канал обновлений.");
        if (!Version.TryParse(release.Version, out var version) || version.Build < 0 || release.Sha256 == null || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit) || release.Size <= 0 || release.Size > 256L * 1024 * 1024 || string.IsNullOrWhiteSpace(release.PackageUrl)) throw new InvalidDataException("В канале нужны Version, PackageUrl, Sha256 и Size (до 256 МБ).");
        var uri = ConfigurationService.Https(new Uri(source, release.PackageUrl).AbsoluteUri);
        if (uri.Authority != source.Authority || uri.Fragment.Length > 0) throw new InvalidDataException("Пакет должен находиться на том же HTTPS-сервере, что и канал.");
        release.PackageUrl = uri.AbsoluteUri; release.Notes ??= ""; return release;
    }
    public static async Task<AppRelease> Check(string url, Preferences settings, CancellationToken cancel, HttpClient? transport = null)
    {
        var source = ConfigurationService.Https(url); using var owned = transport == null ? ConfigurationService.Client(settings) : null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await ConfigurationService.Get(transport ?? owned!, source, timeout.Token);
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        return Parse(System.Text.Encoding.UTF8.GetString(await ConfigurationService.Bounded(input, 65536, timeout.Token)), source);
    }
    public static async Task<PreparedAppUpdate> Download(AppRelease release, string sourceUrl, Preferences settings, string workFolder, IProgress<double>? progress, CancellationToken cancel, HttpClient? transport = null)
    {
        // Revalidate even when invoked directly rather than through the check dialog.
        release = Parse(JsonSerializer.Serialize(release), ConfigurationService.Https(sourceUrl));
        Directory.CreateDirectory(workFolder); string file = Path.Combine(workFolder, "download-" + Guid.NewGuid().ToString("N") + ".zip");
        using var owned = transport == null ? ConfigurationService.Client(settings) : null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel); timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await ConfigurationService.Get(transport ?? owned!, new Uri(release.PackageUrl), timeout.Token);
            if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new IOException("Размер пакета отличается от канала.");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[65536]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += count; if (total > release.Size) throw new IOException("Размер загрузки превышен.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token); progress?.Report(100.0 * total / release.Size);
                }
                if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Пакет не прошёл проверку размера и SHA-256.");
            }
            cancel.ThrowIfCancellationRequested();
            var prepared = AppUpdateService.Prepare(file, workFolder);
            if (new Version(prepared.Version) != new Version(release.Version)) throw new IOException("Версия пакета отличается от канала обновлений.");
            return prepared;
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
