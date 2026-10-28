using System.IO;
using System.Text.Json;

namespace VanBaz;

public sealed class CatalogStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public string Folder { get; }
    public string FilePath => Path.Combine(Folder, "catalog.json");
    public CatalogStore(string folder) => Folder = folder;
    public Catalog Load()
    {
        if (!File.Exists(FilePath)) return new Catalog();
        return Read(FilePath);
    }
    public static Catalog Read(string path)
    {
        var data = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("Каталог пуст или повреждён.");
        if (data.SchemaVersion is not (1 or 2 or 3)) throw new InvalidDataException("Версия каталога не поддерживается.");
        if (data.Bases == null || data.History == null || data.Groups == null || data.Settings == null || data.Commands == null || data.Bases.Any(b => b is null ||
            string.IsNullOrWhiteSpace(b.Id) || b.Name is null || b.Location is null || b.Group is null || b.Version is null))
            throw new InvalidDataException("Структура каталога повреждена. Проверьте catalog.json.bak.");
        if (data.SchemaVersion == 1) foreach (var b in data.Bases) b.DesignerUser = b.UserName;
        if (data.Configurations == null || data.Configurations.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || c.Name == null || c.InfoUrl == null) || data.Configurations.GroupBy(c => c.Id).Any(g => g.Count() > 1)) throw new InvalidDataException("Повреждён список конфигураций.");
        if (data.Groups.Any(g => g == null || string.IsNullOrEmpty(g.Path) || string.IsNullOrEmpty(g.Id)) || data.Bases.GroupBy(b => b.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Каталог содержит повреждённые группы или повторяющиеся ID баз.");
        data.Settings.AppUpdateUrl ??= "";
        data.OfficialLists ??= new();
        data.ListSync ??= new(); data.LaunchProfiles ??= new(); data.Settings.PlatformRoots ??= new(); data.Settings.BaseKeys ??= new(); data.Settings.WebBrowser ??= "Edge";
        data.Settings.DisabledPlatformRoots ??= new();
        data.Settings.TemplateRoots ??= new(); data.Settings.ConnectionMode ??= "Авто";
        foreach (var entry in data.Bases) { entry.Advanced ??= new(); entry.WebOptions ??= new(); entry.ListSources ??= new(); entry.V8iFields = new(entry.V8iFields ?? new(), StringComparer.OrdinalIgnoreCase); }
        foreach (var group in data.Groups) group.ListSources ??= new();
        if (data.ListSync.Any(s => s == null || s.Source == null || s.Agreed == null) || data.LaunchProfiles.Any(p => p == null || p.SchemaVersion != 1 || p.Advanced == null)) throw new InvalidDataException("Повреждены профили запуска или состояние согласования.");
        foreach (var state in data.ListSync) state.Agreed = new(state.Agreed, StringComparer.OrdinalIgnoreCase);
        data.Settings.ColumnWidths ??= new(); data.Settings.PlatformLists ??= new();
        if (data.Settings.PlatformLists.Any(s => s == null || s.Path == null || s.Platform == null)) throw new InvalidDataException("Повреждены источники списков.");
        data.SchemaVersion = 3;
        return data;
    }
    public bool HasPendingRecovery(RecoveryOperation? active = null) => FileRecovery.Pending(Path.Combine(Folder, "RecoveryOperations"))
        .Any(op => !string.Equals(op.Folder, active?.Folder, StringComparison.OrdinalIgnoreCase) && op.Changes.Any(c => Path.GetFullPath(c.Path).Equals(Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase)));
    public void Save(Catalog data, RecoveryOperation? activeRecovery = null)
    {
        if (HasPendingRecovery(activeRecovery)) throw new IOException("Сначала завершите восстановление прерванного согласования в меню сервиса. Каталог не перезаписан.");
        Directory.CreateDirectory(Folder);
        if (File.Exists(FilePath))
        {
            using var old = JsonDocument.Parse(File.ReadAllBytes(FilePath));
            if (old.RootElement.TryGetProperty("SchemaVersion", out var schema) && schema.GetInt32() < 3) Snapshot();
        }
        data.SchemaVersion = 3;
        var temp = Path.Combine(Folder, ".catalog-" + Guid.NewGuid() + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, Options);
                stream.Flush(true);
            }
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public string Snapshot()
    {
        if (!File.Exists(FilePath)) throw new InvalidOperationException("Сначала сохраните каталог.");
        var folder = Path.Combine(Folder, "Snapshots"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "catalog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.Copy(FilePath, path, false); return path;
    }
}
