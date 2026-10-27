using System.IO;
using System.Threading;

namespace VanBaz;

// UI-thread owned. A late scan must never restore platforms from disabled roots.
public sealed class PlatformInventory
{
    private readonly Func<string[], CancellationToken, Task<PlatformScan>> scan;
    private CancellationTokenSource? pending;
    private long revision;
    private string? rootsKey;
    public List<PlatformInfo> Platforms { get; private set; }
    public PlatformInventory(IEnumerable<PlatformInfo>? initial = null, Func<string[], CancellationToken, Task<PlatformScan>>? scanner = null)
    {
        Platforms = initial?.ToList() ?? new(); scan = scanner ?? ((roots, token) => PlatformCatalog.ScanAsync(roots, token));
    }
    private static string Key(Preferences settings) => string.Join("\n", PlatformCatalog.EnabledRoots(settings).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)).ToUpperInvariant()).Distinct().Order());
    public bool Matches(Preferences settings) => rootsKey == Key(settings);
    public void Cancel() { revision++; pending?.Cancel(); }
    public async Task<PlatformScan?> RefreshAsync(Preferences settings, PlatformScan? prepared = null)
    {
        Cancel(); long requested = revision; var roots = PlatformCatalog.EnabledRoots(settings).ToArray(); rootsKey = Key(settings);
        Platforms = PlatformCatalog.WithinRoots(Platforms, roots);
        if (prepared != null) { Platforms = PlatformCatalog.WithinRoots(prepared.Platforms, roots); return prepared; }
        var stop = new CancellationTokenSource(); pending = stop;
        try
        {
            var result = await scan(roots, stop.Token);
            if (requested != revision || stop.IsCancellationRequested) return null;
            Platforms = PlatformCatalog.WithinRoots(result.Platforms, roots); return result;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return null; }
        finally { if (ReferenceEquals(pending, stop)) pending = null; stop.Dispose(); }
    }
}
