using System.IO;
using System.Security.Cryptography;

namespace VanBaz;

public sealed class ExternalRelocationPlan
{
    public string Path { get; internal init; } = "";
    public string SourceHash { get; internal init; } = "";
    public IReadOnlyList<BasePathChange> Changes { get; internal init; } = Array.Empty<BasePathChange>();
    public string Operation { get; internal init; } = "Замена путей в отдельном .v8i";
    internal byte[] Output { get; init; } = Array.Empty<byte>();
}

public static class ExternalListRelocation
{
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static byte[] Read(string path)
    {
        CacheService.EnsureOrdinaryPath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Список превышает 16 МБ.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public static ExternalRelocationPlan Prepare(string path, string oldRoot, string newRoot)
    {
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("Выберите полный путь файла .v8i.");
        path = System.IO.Path.GetFullPath(path);
        var document = V8iDocument.Parse(Read(path)); var records = document.Records;
        var catalog = new Catalog();
        foreach (var record in records.Values.Where(r => !r.IsGroup))
        {
            var (kind, location) = InfobaseConnection.Endpoint(InfobaseConnection.Parse(record.Fields["Connect"]));
            catalog.Bases.Add(new BaseEntry { Id = record.Id, Name = record.Name, Kind = kind, Location = location });
        }
        var changes = CatalogRelocation.Preview(catalog, oldRoot, newRoot);
        foreach (var change in changes)
        {
            var fields = records[change.Id].Fields;
            var connection = InfobaseConnection.Parse(fields["Connect"]); connection["File"] = change.After;
            fields["Connect"] = InfobaseConnection.Format(connection);
        }
        var output = document.Render(records, preserveImplicitOrdering: false);
        _ = V8iDocument.Parse(output);
        return new() { Path = path, SourceHash = Hash(document.Bytes), Changes = changes.AsReadOnly(), Output = output };
    }
    public static ExternalRelocationPlan PrepareNames(string path, Func<string, string> convert, bool bases, bool groups)
    {
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("Выберите полный путь файла .v8i.");
        path = System.IO.Path.GetFullPath(path);
        var document = V8iDocument.Parse(Read(path)); var records = document.Records;
        string Folder(V8iRecord record) => record.Fields.GetValueOrDefault("Folder", "").Trim('/', '\\').Replace('\\', '/');
        string FullGroup(V8iRecord record) => (Folder(record) + "/" + record.Name).Trim('/');
        var knownGroups = records.Values.Select(r => r.IsGroup ? FullGroup(r) : Folder(r)).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var renamedGroups = knownGroups.ToDictionary(p => p, p => groups ? string.Join('/', p.Split('/').Select(convert)) : p, StringComparer.OrdinalIgnoreCase);
        bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(new[] { '/', '\\', '\r', '\n', ']', '\0' }) < 0;
        if (renamedGroups.Values.Any(p => p.Split('/').Any(part => !ValidName(part))) || renamedGroups.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != renamedGroups.Count)
            throw new InvalidDataException("Правило создаёт недопустимые или совпадающие пути групп.");
        var changes = new List<BasePathChange>();
        foreach (var record in records.Values)
        {
            string oldName = record.Name, oldFolder = Folder(record);
            string name = record.IsGroup ? groups ? convert(oldName) : oldName : bases ? convert(oldName) : oldName;
            if (record.IsGroup ? !ValidName(name) : string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '\r', '\n', ']', '\0' }) >= 0)
                throw new InvalidDataException("Правило создаёт недопустимое наименование: " + oldName);
            string folder = groups && oldFolder.Length > 0 ? string.Join('/', oldFolder.Split('/').Select(convert)) : oldFolder;
            if (name == oldName && folder == oldFolder) continue;
            changes.Add(new(record.Id, oldName + (record.IsGroup ? " [группа]" : ""), (oldFolder + "/" + oldName).Trim('/'), (folder + "/" + name).Trim('/')));
            record.Name = name;
            if (folder != oldFolder) record.Fields["Folder"] = "/" + folder;
        }
        var output = document.Render(records, preserveImplicitOrdering: false); _ = V8iDocument.Parse(output);
        return new() { Path = path, SourceHash = Hash(document.Bytes), Changes = changes.AsReadOnly(), Output = output, Operation = "Форматирование отдельного .v8i" };
    }
    public static (string Backup, string? Warning) Apply(ExternalRelocationPlan plan, CatalogStore store, bool allowWrite = false)
    {
        if (!allowWrite) throw new InvalidOperationException("Изменение отдельного списка нужно подтвердить после просмотра путей.");
        if (plan.Changes.Count == 0) throw new InvalidOperationException("Изменений нет.");
        if (store.HasPendingRecovery()) throw new IOException("Сначала завершите прерванную операцию в журнале восстановления.");
        if (Hash(Read(plan.Path)) != plan.SourceHash) throw new IOException("Файл изменился после предпросмотра. Постройте новый план; чужие правки не заменены.");
        string temp = plan.Path + ".vanbaz-" + Guid.NewGuid().ToString("N") + ".tmp";
        string backup = plan.Path + ".vanbaz-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".bak";
        RecoveryOperation? recovery = null; bool replaced = false;
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(plan.Output); output.Flush(true); }
            // Deny new writers while comparing and replacing; independent replacers still require the final hash check.
            using var guard = new FileStream(plan.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var snapshot = new MemoryStream(); guard.CopyTo(snapshot);
            if (Hash(snapshot.ToArray()) != plan.SourceHash) throw new IOException("Список изменён другим процессом. Повторите предпросмотр.");
            recovery = FileRecovery.Begin(System.IO.Path.Combine(store.Folder, "RecoveryOperations"), plan.Operation, new Dictionary<string, byte[]?> { [plan.Path] = plan.Output });
            if (Hash(Read(plan.Path)) != plan.SourceHash) throw new IOException("Список заменён другим процессом. Повторите предпросмотр.");
            File.Replace(temp, plan.Path, backup); replaced = true;
            if (Hash(Read(backup)) != plan.SourceHash)
                throw new IOException("Обнаружена параллельная замена списка во время записи. Предшествующий файл сохранён в " + backup + ". Проверьте копию и журнал восстановления; операция не отмечена завершённой.");
            string? warning = null;
            try { FileRecovery.Complete(recovery); }
            catch (Exception ex) { warning = "Пути записаны, но журнал не отмечен завершённым: " + ex.Message; }
            return (backup, warning);
        }
        catch
        {
            if (!replaced && recovery != null)
                try { if (Hash(Read(plan.Path)) == plan.SourceHash) FileRecovery.Complete(recovery); } catch { }
            throw;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
