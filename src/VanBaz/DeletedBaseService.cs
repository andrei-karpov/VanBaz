using System.IO;
using System.Text.Json;

namespace VanBaz;

public record DeletedBase(string OriginalFile, string StoredFile, string BaseName, DateTime Time);
public static class DeletedBaseService
{
    private static void PlainPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Операция с символическими ссылками и точками подключения не поддерживается.");
    }
    public static DeletedBase Quarantine(BaseEntry entry, string journalFolder)
    {
        if (entry.Kind != "File") throw new ArgumentException("Удаление файла доступно только для файловой базы.");
        if (!Path.IsPathFullyQualified(entry.Location)) throw new ArgumentException("Нужен абсолютный путь базы.");
        string folder = Path.GetFullPath(entry.Location).TrimEnd(Path.DirectorySeparatorChar);
        if (folder.Equals(Path.GetPathRoot(Path.GetFullPath(entry.Location))!.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) throw new IOException("Нельзя удалять базу из корня диска или сетевой папки.");
        string source = Path.Combine(folder, "1Cv8.1CD");
        PlainPath(source);
        string parent = Path.GetDirectoryName(folder) ?? throw new IOException("Нельзя удалять базу из корня диска.");
        string directory = Path.Combine(parent, ".VanBaz-Deleted", Guid.NewGuid().ToString("N"));
        PlainPath(directory); Directory.CreateDirectory(directory); Directory.CreateDirectory(journalFolder);
        string destination = Path.Combine(directory, "1Cv8.1CD");
        var record = new DeletedBase(source, destination, entry.Name, DateTime.Now);
        string journal = Path.Combine(journalFolder, Path.GetFileName(directory) + ".json");
        try
        {
            using var handle = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Delete);
            // Durable recovery pointer before rename. Existing clients/readers make the exclusive open fail.
            using (var journalStream = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { JsonSerializer.Serialize(journalStream, record); journalStream.Flush(true); }
            File.Move(source, destination, false);
            return record;
        }
        catch { if (!File.Exists(destination)) { if (File.Exists(journal)) File.Delete(journal); if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } throw; }
    }
    public static void Restore(DeletedBase record)
    {
        string source = Path.GetFullPath(record.StoredFile), destination = Path.GetFullPath(record.OriginalFile);
        string sourceDirectory = Path.GetDirectoryName(source)!;
        string archive = Path.GetDirectoryName(sourceDirectory)!;
        string expectedRoot = Path.GetDirectoryName(Path.GetDirectoryName(destination)!)!;
        if (Path.GetFileName(source) != "1Cv8.1CD" || Path.GetFileName(destination) != "1Cv8.1CD" ||
            !string.Equals(Path.GetDirectoryName(archive), expectedRoot, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(archive).StartsWith(".", StringComparison.Ordinal) || !Path.GetFileName(archive).EndsWith("-Deleted", StringComparison.Ordinal) || !Guid.TryParseExact(Path.GetFileName(sourceDirectory), "N", out _))
            throw new IOException("Некорректный путь сохранённой базы.");
        PlainPath(source); PlainPath(destination);
        if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new IOException("Исходный каталог отсутствует. Восстановите его перед возвратом файла.");
        using var handle = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Delete);
        File.Move(source, destination, false);
    }
}
