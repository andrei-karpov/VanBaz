using System.IO;

namespace VanBaz;

public record CacheCleanupPlan(CacheFolder Folder, string Destination, string[] Paths, string[] Preserved);
public static class CacheCleanup
{
    public static readonly string[] CacheNames = { "Config", "ConfigSave", "DBNameCache", "SICache" };
    public static CacheCleanupPlan Plan(CacheFolder folder)
    {
        string source = Path.GetFullPath(folder.Path), root = Path.GetFullPath(folder.Root);
        if (!Guid.TryParse(Path.GetFileName(source), out _) || !string.Equals(Path.GetDirectoryName(source), root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Ожидался каталог GUID непосредственно в корне клиентского кэша.");
        CacheService.EnsureOrdinaryPath(source);
        var entries = Directory.EnumerateFileSystemEntries(source).ToArray();
        var paths = entries.Where(p => Directory.Exists(p) && CacheNames.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)).ToArray();
        foreach (string path in paths) CacheService.EnsureOrdinaryPath(path);
        return new(folder, Path.Combine(root, "VanBaz.Selective.Quarantine", Path.GetFileName(source)), paths, entries.Except(paths, StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public static void ClearSelective(CacheFolder folder)
    {
        var plan = Plan(folder); if (plan.Paths.Length == 0) return;
        if (Directory.Exists(plan.Destination) || File.Exists(plan.Destination)) throw new IOException("Для этой базы уже сохранён выборочный кэш. Сначала восстановите или разберите его вручную.");
        CacheService.EnsureOrdinaryPath(plan.Destination); Directory.CreateDirectory(plan.Destination);
        MoveAll(plan.Paths.Select(p => (p, Path.Combine(plan.Destination, Path.GetFileName(p)))).ToArray());
    }
    public static bool IsSelective(CacheFolder folder) => Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(folder.Path))) == "VanBaz.Selective.Quarantine";
    public static void RestoreSelective(CacheFolder folder)
    {
        string root = Path.GetFullPath(folder.Root), source = Path.GetFullPath(folder.Path), name = Path.GetFileName(source);
        if (!Guid.TryParse(name, out _) || !string.Equals(Path.GetDirectoryName(source), Path.Combine(root, "VanBaz.Selective.Quarantine"), StringComparison.OrdinalIgnoreCase)) throw new IOException("Карантин находится вне выбранного корня.");
        CacheService.EnsureOrdinaryPath(source);
        string destination = Path.Combine(root, name); CacheService.EnsureOrdinaryPath(destination);
        var paths = Directory.EnumerateFileSystemEntries(source).ToArray();
        if (paths.Any(p => !Directory.Exists(p) || !CacheNames.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))) throw new IOException("В выборочном карантине обнаружены неизвестные объекты. Проверьте каталог вручную.");
        foreach (string path in paths) CacheService.EnsureOrdinaryPath(path);
        var moves = paths.Select(p => (p, Path.Combine(destination, Path.GetFileName(p)))).ToArray();
        foreach (var move in moves) if (Directory.Exists(move.Item2) || File.Exists(move.Item2)) throw new IOException("Не будет заменён новый кэш: " + move.Item2);
        Directory.CreateDirectory(destination); MoveAll(moves);
        if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source);
    }
    private static void MoveAll((string Source, string Target)[] moves)
    {
        var done = new List<(string Source, string Target)>();
        try { foreach (var move in moves) { Directory.Move(move.Source, move.Target); done.Add(move); } }
        catch (Exception failure)
        {
            var errors = new List<string> { failure.Message };
            foreach (var move in done.AsEnumerable().Reverse()) try { Directory.Move(move.Target, move.Source); } catch (Exception ex) { errors.Add("Возврат " + move.Source + ": " + ex.Message); }
            throw new IOException(string.Join(Environment.NewLine, errors), failure);
        }
    }
}
