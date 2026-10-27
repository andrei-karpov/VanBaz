using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace VanBaz;

public sealed class RecoveryChange
{
    public string Path { get; set; } = "";
    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }
    public string? Payload { get; set; }
}
public sealed class RecoveryOperation
{
    public int Version { get; set; } = 1;
    public string Kind { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool Completed { get; set; }
    public List<RecoveryChange> Changes { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string Folder { get; set; } = "";
    public override string ToString() => CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") + " · " + Kind + " · " + Changes.Count + " файлов";
}
public static class FileRecovery
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static byte[]? Read(string path)
    {
        CacheService.EnsureOrdinaryPath(path);
        try { using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); if (f.Length > 64 * 1024 * 1024) throw new InvalidDataException("Файл превышает 64 МБ: " + path); using var m = new MemoryStream(); f.CopyTo(m); return m.ToArray(); }
        catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
    }
    private static void DurableWrite(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(bytes); file.Flush(true);
    }
    private static void Save(RecoveryOperation op)
    {
        string path = System.IO.Path.Combine(op.Folder, "operation.json"), temp = path + "." + Guid.NewGuid().ToString("N");
        try { DurableWrite(temp, JsonSerializer.SerializeToUtf8Bytes(op, Options)); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static RecoveryOperation Begin(string root, string kind, IDictionary<string, byte[]?> planned)
    {
        if (planned.Count is < 1 or > 25) throw new InvalidDataException("Неподдерживаемый размер операции восстановления.");
        CacheService.EnsureOrdinaryPath(root); Directory.CreateDirectory(root);
        var op = new RecoveryOperation { Kind = kind, Folder = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N")) }; Directory.CreateDirectory(op.Folder);
        long total = 0;
        foreach (var pair in planned)
        {
            string path = System.IO.Path.GetFullPath(pair.Key); var bytes = Read(path); total += bytes?.Length ?? 0;
            if (total > 256 * 1024 * 1024) throw new InvalidDataException("Копия операции превышает 256 МБ.");
            var change = new RecoveryChange { Path = path, BeforeHash = bytes == null ? null : Hash(bytes), AfterHash = pair.Value == null ? null : Hash(pair.Value) };
            if (bytes != null) { change.Payload = op.Changes.Count + ".bin"; DurableWrite(System.IO.Path.Combine(op.Folder, change.Payload), bytes); }
            op.Changes.Add(change);
        }
        // The manifest becomes visible only after every original byte stream has been flushed.
        Save(op); return op;
    }
    public static void Complete(RecoveryOperation op)
    {
        bool previous = op.Completed; op.Completed = true;
        try { Save(op); }
        catch { op.Completed = previous; throw; }
    }
    public static List<RecoveryOperation> Pending(string root)
    {
        if (!Directory.Exists(root)) return new(); CacheService.EnsureOrdinaryPath(root);
        var result = new List<RecoveryOperation>();
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            string path = System.IO.Path.Combine(folder, "operation.json"); if (!File.Exists(path)) continue;
            var bytes = Read(path)!; if (bytes.Length > 65536) throw new InvalidDataException("Слишком большой журнал восстановления.");
            var op = JsonSerializer.Deserialize<RecoveryOperation>(bytes) ?? throw new InvalidDataException("Пустой журнал восстановления."); op.Folder = folder;
            if (op.Version != 1 || op.Changes == null || op.Changes.Count is < 1 or > 25 || op.Changes.Any(c => c == null || !System.IO.Path.IsPathFullyQualified(c.Path)) || op.Changes.GroupBy(c => c.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Повреждён журнал восстановления.");
            if (!op.Completed) result.Add(op);
        }
        return result.OrderByDescending(x => x.CreatedUtc).ToList();
    }
    private static byte[]? Before(RecoveryOperation op, RecoveryChange c)
    {
        if (c.BeforeHash == null) { if (c.Payload != null) throw new InvalidDataException("Некорректный маркер отсутствия файла."); return null; }
        if (c.Payload != op.Changes.IndexOf(c) + ".bin") throw new InvalidDataException("Некорректный путь резервной копии.");
        var bytes = Read(System.IO.Path.Combine(op.Folder, c.Payload)); if (bytes == null || Hash(bytes) != c.BeforeHash) throw new InvalidDataException("Резервная копия повреждена."); return bytes;
    }
    private static string State(RecoveryChange c)
    {
        var bytes = Read(c.Path); string? hash = bytes == null ? null : Hash(bytes);
        if (hash == c.BeforeHash) return "Уже исходное состояние";
        if (hash == c.AfterHash) return c.BeforeHash == null ? "Убрать созданный операцией файл" : "Вернуть прежние данные";
        throw new IOException("Файл изменён после операции. Автоматический возврат запрещён: " + c.Path);
    }
    public static string Preview(RecoveryOperation op) => string.Join("\n\n", op.Changes.Select(c => { Before(op, c); return State(c) + "\n" + c.Path; }));
    public static void AcceptApplied(RecoveryOperation op)
    {
        if (op.Completed) throw new InvalidOperationException("Операция уже завершена.");
        foreach (var c in op.Changes)
        {
            Before(op, c);
            var current = Read(c.Path);
            if ((current == null ? null : Hash(current)) != c.AfterHash)
                throw new IOException("Не все файлы соответствуют завершённой операции. Сначала проверьте возврат исходного состояния: " + c.Path);
        }
        Complete(op);
    }
    public static void Restore(RecoveryOperation op)
    {
        if (op.Completed) throw new InvalidOperationException("Операция уже завершена.");
        var backups = op.Changes.ToDictionary(c => c, c => Before(op, c));
        foreach (var c in op.Changes) State(c);
        var locks = new List<FileStream>();
        try
        {
            foreach (var c in op.Changes)
            {
                CacheService.EnsureOrdinaryPath(c.Path);
                if (File.Exists(c.Path)) locks.Add(new FileStream(c.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
            }
            foreach (var c in op.Changes) State(c);
            foreach (var c in op.Changes)
            {
                if (State(c) == "Уже исходное состояние") continue;
                if (backups[c] is not { } bytes) { File.Delete(c.Path); continue; }
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(c.Path)!); string temp = c.Path + ".recover-" + Guid.NewGuid().ToString("N");
                try { DurableWrite(temp, bytes); if (File.Exists(c.Path)) File.Replace(temp, c.Path, null); else File.Move(temp, c.Path); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            Complete(op);
        }
        finally { foreach (var stream in locks) stream.Dispose(); }
    }
}
