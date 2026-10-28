using System.IO;
using System.Text.Json;

namespace VanBaz;

public sealed class SnapshotInfo
{
    public string Description { get; set; } = "";
    public string Platform { get; set; } = "Все";
    public DateTime Created { get; set; } = DateTime.Now;
}
public sealed record SnapshotItem(string Path, string Description, string Platform, DateTime Created, int Bases, string Error)
{
    public string Date => Created.ToString("dd.MM.yyyy HH:mm:ss");
    public string Status => Error.Length > 0 ? Error : "Готов к восстановлению";
}
public static class SnapshotService
{
    public static bool AutomaticDue(Preferences preferences, DateTime now) => preferences.AutoSnapshot && (!preferences.LastAutoSnapshot.HasValue || preferences.LastAutoSnapshot > now.AddDays(1) || now - preferences.LastAutoSnapshot.Value >= TimeSpan.FromDays(Math.Clamp(preferences.AutoSnapshotDays, 1, 3650)));
    public static bool SaveAutomatic(CatalogStore store, Catalog catalog, DateTime now)
    {
        if (!AutomaticDue(catalog.Settings, now) || !File.Exists(store.FilePath)) return false;
        Save(store, catalog, "Автоматический снимок при запуске", "Все"); catalog.Settings.LastAutoSnapshot = now; store.Save(catalog); return true;
    }
    public static readonly string[] Platforms = { "Все", "8.1", "8.2", "8.3", "8.4", "8.5", "7.7" };
    public static bool Matches(BaseEntry entry, string platform) => platform == "Все" || entry.Version == platform || entry.Version.StartsWith(platform + ".", StringComparison.Ordinal);
    public static string Save(CatalogStore store, Catalog catalog, string description, string platform)
    {
        if (!Platforms.Contains(platform)) throw new InvalidDataException("Неизвестная платформа снимка.");
        var copy = catalog.Copy();
        copy.Snapshot = new() { Description = description.Trim(), Platform = platform };
        if (platform != "Все")
        {
            copy.Bases.RemoveAll(b => !Matches(b, platform));
            copy.Groups.RemoveAll(g => !copy.Bases.Any(b => b.Group.Equals(g.Path, StringComparison.OrdinalIgnoreCase) || b.Group.StartsWith(g.Path + "/", StringComparison.OrdinalIgnoreCase)));
        }
        string folder = System.IO.Path.Combine(store.Folder, "Snapshots"); Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, "catalog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, copy, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true);
        return path;
    }
    public static Catalog Read(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Снимок больше 64 МБ.");
        var value = CatalogStore.Read(path);
        if (value.Snapshot is { } info && (!Platforms.Contains(info.Platform) || info.Description == null)) throw new InvalidDataException("Повреждено описание снимка.");
        return value;
    }
    public static SnapshotItem Inspect(string path)
    {
        try
        {
            var value = Read(path); var info = value.Snapshot;
            return new(path, info?.Description ?? System.IO.Path.GetFileNameWithoutExtension(path), info?.Platform ?? "Все", info?.Created ?? File.GetLastWriteTime(path), value.Bases.Count, "");
        }
        catch (Exception ex) { return new(path, System.IO.Path.GetFileName(path), "—", File.GetLastWriteTime(path), 0, ex.Message); }
    }
    public static List<SnapshotItem> List(CatalogStore store) => Directory.Exists(System.IO.Path.Combine(store.Folder, "Snapshots"))
        ? Directory.EnumerateFiles(System.IO.Path.Combine(store.Folder, "Snapshots"), "*.json").Select(Inspect).OrderByDescending(i => i.Created).ToList() : new();
    public static Catalog Restore(Catalog current, Catalog snapshot)
    {
        string platform = snapshot.Snapshot?.Platform ?? "Все";
        if (!Platforms.Contains(platform)) throw new InvalidDataException("Неизвестная платформа снимка.");
        if (platform == "Все") { var full = snapshot.Copy(); full.Snapshot = null; FeatureMigration.Apply(full); return full; }
        if (snapshot.Bases.Any(b => !Matches(b, platform))) throw new InvalidDataException("Снимок содержит базы другой платформы.");
        var result = current.Copy(); result.Bases.RemoveAll(b => Matches(b, platform));
        if (result.Bases.Any(b => snapshot.Bases.Any(s => s.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("ID из снимка уже используется другой платформой. Восстановление отменено.");
        result.Bases.AddRange(snapshot.Copy().Bases);
        foreach (var group in snapshot.Copy().Groups)
            if (!result.Groups.Any(g => g.Path.Equals(group.Path, StringComparison.OrdinalIgnoreCase))) result.Groups.Add(group);
        result.Snapshot = null; return result;
    }
}
