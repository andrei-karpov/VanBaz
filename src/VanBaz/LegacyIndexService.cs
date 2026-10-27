using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace VanBaz;

public sealed record IndexArchive(string Folder, DateTime Time, int Count);
public sealed class IndexManifest
{
    public string BaseFolder { get; set; } = "";
    public DateTime Created { get; set; }
    public Dictionary<string, string> Files { get; set; } = new();
}
public static class LegacyIndexService
{
    private static string Root(BaseEntry entry)
    {
        if (entry.Version != "7.7" || entry.Kind != "File" || !Path.IsPathFullyQualified(entry.Location)) throw new IOException("Выберите файловую базу 7.7.");
        string root = Path.GetFullPath(entry.Location).TrimEnd('\\'); CacheService.EnsureOrdinaryPath(root);
        string md = Path.Combine(root, "1Cv7.MD");
        if (!File.Exists(md) || (File.GetAttributes(md) & FileAttributes.ReparsePoint) != 0) throw new IOException("Не найден обычный файл 1Cv7.MD.");
        return root;
    }
    public static string[] Files(BaseEntry entry) => Directory.GetFiles(Root(entry), "*.cdx", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    public static List<IndexArchive> Archives(BaseEntry entry)
    {
        string root = Root(entry); var result = new List<IndexArchive>();
        foreach (string path in Directory.GetDirectories(root, ".*-cdx-*"))
        {
            try { var manifest = Read(path, root); result.Add(new(path, manifest.Created, manifest.Files.Count)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result.OrderByDescending(a => a.Time).ToList();
    }
    private static IndexManifest Read(string folder, string root)
    {
        folder = Path.GetFullPath(folder); CacheService.EnsureOrdinaryPath(folder);
        if (!Path.GetDirectoryName(folder)!.Equals(root, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder), @"^\.[A-Za-z0-9]+-cdx-[a-fA-F0-9]{32}$")) throw new IOException("Карантин находится вне выбранной базы.");
        string file = Path.Combine(folder, "manifest.json");
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || new FileInfo(file).Length > 1024 * 1024) throw new IOException("Некорректный журнал индексов.");
        var m = JsonSerializer.Deserialize<IndexManifest>(File.ReadAllText(file)) ?? throw new IOException("Повреждён журнал.");
        if (!string.Equals(m.BaseFolder, root, StringComparison.OrdinalIgnoreCase) || m.Files == null || m.Files.Count == 0 || m.Files.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Files.Count || m.Files.Any(p => Path.GetFileName(p.Key) != p.Key || !p.Key.EndsWith(".cdx", StringComparison.OrdinalIgnoreCase) || p.Key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.Value?.Length != 64)) throw new IOException("Повреждён список индексов.");
        return m;
    }
    public static string Quarantine(BaseEntry entry) => Quarantine(entry, CacheService.ClientsRunning);
    internal static string Quarantine(BaseEntry entry, Func<bool> clientsRunning)
    {
        if (clientsRunning()) throw new IOException("Закройте все процессы 1С перед очисткой индексов.");
        string root = Root(entry); var files = Files(entry);
        if (files.Length == 0) throw new IOException("В корне базы нет CDX-индексов.");
        string folder = Path.Combine(root, ".vanbaz-cdx-" + Guid.NewGuid().ToString("N"));
        var m = new IndexManifest { BaseFolder = root, Created = DateTime.Now };
        var handles = new List<FileStream>(); var moved = new List<string>();
        try
        {
            foreach (string file in files)
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Индекс является ссылкой: " + file);
                var handle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Delete); handles.Add(handle);
                m.Files.Add(Path.GetFileName(file), Convert.ToHexString(SHA256.HashData(handle)));
            }
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(m));
            foreach (string file in files) { File.Move(file, Path.Combine(folder, Path.GetFileName(file)), false); moved.Add(file); }
            return folder;
        }
        catch
        {
            foreach (string file in moved.AsEnumerable().Reverse()) File.Move(Path.Combine(folder, Path.GetFileName(file)), file, false);
            throw;
        }
        finally { foreach (var h in handles) h.Dispose(); }
    }
    public static void Restore(BaseEntry entry, string folder) => Restore(entry, folder, CacheService.ClientsRunning);
    internal static void Restore(BaseEntry entry, string folder, Func<bool> clientsRunning)
    {
        if (clientsRunning()) throw new IOException("Закройте все процессы 1С перед восстановлением индексов.");
        string root = Root(entry); var m = Read(folder, root);
        var handles = new List<FileStream>(); var moved = new List<string>();
        try
        {
            foreach (var pair in m.Files)
            {
                string file = Path.Combine(folder, pair.Key);
                if (File.Exists(Path.Combine(root, pair.Key))) throw new IOException("Индекс уже создан заново: " + pair.Key + ". Замена запрещена.");
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Индекс является ссылкой.");
                var handle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Delete); handles.Add(handle);
                if (!Convert.ToHexString(SHA256.HashData(handle)).Equals(pair.Value, StringComparison.OrdinalIgnoreCase)) throw new IOException("Контрольная сумма индекса не совпадает: " + pair.Key);
            }
            foreach (string name in m.Files.Keys) { File.Move(Path.Combine(folder, name), Path.Combine(root, name), false); moved.Add(name); }
            File.Move(Path.Combine(folder, "manifest.json"), Path.Combine(folder, "restored.json"), false);
        }
        catch
        {
            foreach (string name in moved.AsEnumerable().Reverse()) File.Move(Path.Combine(root, name), Path.Combine(folder, name), false);
            throw;
        }
        finally { foreach (var h in handles) h.Dispose(); }
    }
}
