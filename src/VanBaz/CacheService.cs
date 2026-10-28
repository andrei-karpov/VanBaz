using System.Diagnostics;
using System.IO;

namespace VanBaz;

public record CacheFolder(string Root, string Path, string BaseId, string Label)
{
    public override string ToString() => Label + " · " + Path;
}
public static class CacheService
{
    public static IEnumerable<string> Roots()
    {
        foreach (var special in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
            foreach (var version in new[] { "1cv8", "1cv82" })
                yield return Path.Combine(Environment.GetFolderPath(special), "1C", version);
    }
    public static string[] ProfileRoots(string folder, bool local = true, bool roaming = true, string version = "Все")
    {
        string root = Path.GetFullPath(folder); EnsureOrdinaryPath(root);
        if (!Directory.Exists(Path.Combine(root, "AppData"))) throw new IOException("Выберите профиль Windows, содержащий папку AppData.");
        var versions = new[] { "1cv8", "1cv81", "1cv82" }; if (version != "Все" && !versions.Contains(version)) throw new ArgumentException("Неизвестный каталог версии кэша.");
        return new[] { local ? "Local" : "", roaming ? "Roaming" : "" }.Where(p => p.Length > 0).SelectMany(part => versions.Where(v => version == "Все" || v == version).Select(v => Path.Combine(root, "AppData", part, "1C", v))).ToArray();
    }
    public static List<CacheFolder> Find(IEnumerable<string> roots, IEnumerable<BaseEntry> bases, bool quarantined = false, List<string>? errors = null)
    {
        var names = bases.Where(b => Guid.TryParse(b.Id, out _)).GroupBy(b => Guid.Parse(b.Id)).ToDictionary(g => g.Key, g => g.First().Name);
        var result = new List<CacheFolder>();
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                // Older archives remain discoverable by their storage shape.
                var scans = quarantined ? Directory.EnumerateDirectories(root, "*.Quarantine") : new[] { root };
                foreach (string scan in scans)
                {
                if ((File.GetAttributes(scan) & FileAttributes.Directory) == 0) continue;
                EnsureOrdinaryPath(scan);
                foreach (string path in Directory.EnumerateDirectories(scan))
                {
                    if (!Guid.TryParse(Path.GetFileName(path), out var id) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    result.Add(new(root, path, id.ToString(), names.GetValueOrDefault(id, "Нет в текущем каталоге")));
                }
                }
            }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors?.Add(root + ": " + ex.Message); }
        }
        return result;
    }
    public static void EnsureOrdinaryPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Операция с файлом или каталогом-ссылкой запрещена: " + current); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
    }
    public static void Move(CacheFolder folder, bool restore)
    {
        if (restore && CacheCleanup.IsSelective(folder)) { CacheCleanup.RestoreSelective(folder); return; }
        string root = Path.GetFullPath(folder.Root);
        string quarantine = Path.Combine(root, "VanBaz.Quarantine");
        string name = Path.GetFileName(folder.Path);
        if (!Guid.TryParse(name, out _)) throw new IOException("Ожидался каталог с GUID базы.");
        string sourceParent = restore ? Path.GetDirectoryName(Path.GetFullPath(folder.Path))! : root, targetParent = restore ? root : quarantine;
        if (restore && (!string.Equals(Path.GetDirectoryName(sourceParent), root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(sourceParent).EndsWith(".Quarantine", StringComparison.Ordinal)))
            throw new IOException("Карантин находится вне выбранного корня кэша.");
        string source = Path.GetFullPath(folder.Path);
        if (!Path.GetDirectoryName(source)!.Equals(sourceParent, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Каталог находится вне выбранного корня кэша.");
        EnsureOrdinaryPath(source); EnsureOrdinaryPath(targetParent);
        string destination = Path.Combine(targetParent, name);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Каталог назначения уже существует: " + destination + ". Сначала разберите сохранённый кэш.");
        Directory.CreateDirectory(targetParent);
        Directory.Move(source, destination);
    }
    public static bool ClientsRunning()
    {
        // Process details can be inaccessible for elevated clients; do not treat them as absent.
        foreach (var name in new[] { "1cv8", "1cv8c", "1cv7", "1cv7s", "1cv7l" })
        {
            var processes = Process.GetProcessesByName(name);
            bool found = processes.Length > 0; foreach (var process in processes) process.Dispose();
            if (found) return true;
        }
        return false;
    }
}

public record ClientProcess(int Id, DateTime Started, string Name, string Title)
{
    public override string ToString() => $"{Id} · {Name} · {Title}";
}
public static class ClientProcesses
{
    public static List<ClientProcess> List()
    {
        var result = new List<ClientProcess>();
        int session = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcesses())
            using (process)
                try
                {
                    if (process.SessionId == session && process.ProcessName.ToLowerInvariant() is "1cv8" or "1cv8c" or "1cv7" or "1cv7s" or "1cv7l")
                        result.Add(new(process.Id, process.StartTime, process.ProcessName, process.MainWindowTitle));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return result;
    }
    public static void Close(ClientProcess item, bool force)
    {
        using var process = Process.GetProcessById(item.Id);
        if (process.StartTime != item.Started || process.ProcessName != item.Name || process.SessionId != Process.GetCurrentProcess().SessionId)
            throw new InvalidOperationException("Процесс уже сменился. Обновите список.");
        if (force) process.Kill();
        else if (!process.CloseMainWindow()) throw new InvalidOperationException("У процесса нет доступного главного окна. Запрос закрытия не отправлен.");
    }
}
