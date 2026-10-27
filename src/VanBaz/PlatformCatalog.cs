using System.Diagnostics;
using System.IO;
using System.Threading;

namespace VanBaz;

public sealed record PlatformScan(List<PlatformInfo> Platforms, List<string> Errors);
public static class PlatformCatalog
{
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    public static IEnumerable<string> StandardRoots() => new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
        .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).SelectMany(p => new[] { Path.Combine(p, "1cv8"), Path.Combine(p, "1cv8t") });
    public static List<PlatformInfo> WithinRoots(IEnumerable<PlatformInfo> platforms, IEnumerable<string> custom)
    {
        var roots = StandardRoots().Concat(custom).Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p))).ToArray();
        return platforms.Where(p => roots.Any(root =>
        {
            string relative = Path.GetRelativePath(root, Path.GetFullPath(p.Directory));
            return relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar) && relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length <= 3;
        })).ToList();
    }
    public static IEnumerable<string> EnabledRoots(Preferences settings) => settings.PlatformRoots.Where(p => !settings.DisabledPlatformRoots.Contains(p, StringComparer.OrdinalIgnoreCase));
    public static async Task<PlatformScan> ScanAsync(IEnumerable<string> custom, CancellationToken token = default)
    {
        var roots = custom.ToArray(); await ScanGate.WaitAsync(token);
        var worker = Task.Run(() => Scan(roots, true, token));
        try { return await worker.WaitAsync(token); }
        finally
        {
            if (worker.IsCompleted) { _ = worker.Exception; ScanGate.Release(); }
            else _ = worker.ContinueWith(t => { _ = t.Exception; ScanGate.Release(); }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    public static PlatformScan Scan(IEnumerable<string>? custom = null, bool standard = true, CancellationToken token = default)
    {
        var roots = new List<string>(); var errors = new List<string>(); var found = new Dictionary<string, PlatformInfo>(StringComparer.OrdinalIgnoreCase);
        if (standard) roots.AddRange(StandardRoots().Where(Directory.Exists));
        roots.AddRange(custom ?? Array.Empty<string>());
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((root, 0)); int visited = 0;
            while (queue.Count > 0 && visited++ < 5000)
            {
                token.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                try
                {
                    if (!Path.IsPathFullyQualified(current.Path)) throw new IOException("Нужен полный путь.");
                    CacheService.EnsureOrdinaryPath(current.Path);
                    var attrs = File.GetAttributes(current.Path); if ((attrs & FileAttributes.Directory) == 0) continue;
                    string? exe = new[] { "1cv8.exe", "1cv8c.exe" }.Select(f => Path.Combine(current.Path, f)).FirstOrDefault(File.Exists);
                    if (exe != null)
                    {
                        string version = FileVersionInfo.GetVersionInfo(exe).FileVersion?.Replace(',', '.').Replace(" ", "") ?? "";
                        if (Version.TryParse(version, out _)) found[Path.GetFullPath(current.Path)] = new(version, Path.GetFullPath(current.Path), Architecture(exe))
                        { ThickClient = File.Exists(Path.Combine(current.Path, "1cv8.exe")), ThinClient = File.Exists(Path.Combine(current.Path, "1cv8c.exe")) };
                        else errors.Add(exe + ": не удалось прочитать версию файла.");
                    }
                    if (current.Depth < 3) foreach (string d in Directory.EnumerateDirectories(current.Path)) { token.ThrowIfCancellationRequested(); queue.Enqueue((d, current.Depth + 1)); }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { errors.Add(current.Path + ": " + ex.Message); }
            }
            if (queue.Count > 0) errors.Add(root + ": достигнут предел 5000 каталогов; уточните корень поиска.");
        }
        return new(found.Values.OrderByDescending(p => Version.Parse(p.Version)).ThenByDescending(p => p.Architecture).ToList(), errors);
    }
    public static string Architecture(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Файл не является Windows EXE: " + path);
        stream.Position = 0x3c; int offset = reader.ReadInt32();
        if (offset < 0 || offset > stream.Length - 6) throw new InvalidDataException("Повреждён заголовок EXE.");
        stream.Position = offset; if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("Повреждён заголовок PE.");
        return reader.ReadUInt16() switch { 0x14c => "x86", 0x8664 => "x64", _ => throw new InvalidDataException("Архитектура EXE не поддерживается.") };
    }
}
