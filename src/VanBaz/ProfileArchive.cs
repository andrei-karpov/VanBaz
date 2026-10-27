using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace VanBaz;

public sealed class ProfileContext
{
    public string Roaming { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string Local { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public string Edition { get; set; } = "1cv8";
    public string BaseId { get; set; } = "";
    public string UserId { get; set; } = "";
}
public sealed record ProfileFile(string Key, string Path, string Description)
{
    public bool Exists
    {
        get { try { return (File.GetAttributes(Path) & FileAttributes.Directory) == 0; } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; } }
    }
    public string State
    {
        get { try { return Exists ? new FileInfo(Path).Length.ToString("N0") + " байт · " + File.GetLastWriteTime(Path).ToString("dd.MM.yy HH:mm") : "Нет файла"; } catch (UnauthorizedAccessException) { return "Нет доступа"; } catch (IOException ex) { return "Недоступно: " + ex.Message; } }
    }
}
public sealed class ProfileArchiveManifest
{
    public int Version { get; set; } = 2;
    public List<string> AbsentFiles { get; set; } = new();
    public string Edition { get; set; } = "";
    public string BaseId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<ProfileArchiveItem> Files { get; set; } = new();
}
public sealed class ProfileArchiveItem
{
    public string Key { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Hash { get; set; } = "";
    public long Length { get; set; }
}
public static class ProfileArchive
{
    private const int MaximumFile = 32 * 1024 * 1024, MaximumTotal = 128 * 1024 * 1024;
    private static readonly string[] Global = { "1Cv8.pfl", "1Cv8c.pfl", "1Cv8conn.pfl", "1Cv8cmn.pfl", "1Cv8u.pfl", "1Cv8strt.pfl", "1Cv8prim.pfl" };
    private static readonly string[] Base = { "1Cv8.pfl", "1Cv8c.pfl", "def.usr" };
    private static readonly string[] User = { "1Cv8.pfl", "1Cv8c.pfl", "1Cv8ccmn.pfl", "1Cv8cmn.pfl" };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Ordinary(string path)
    {
        CacheService.EnsureOrdinaryPath(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Файл-ссылка не поддерживается: " + path);
    }
    public static List<ProfileFile> List(ProfileContext c)
    {
        if (c.Edition is not ("1cv8" or "1cv82") || !Path.IsPathFullyQualified(c.Roaming) || !Path.IsPathFullyQualified(c.Local)) throw new ArgumentException("Укажите полные каталоги профиля и издание 1cv8/1cv82.");
        if (c.BaseId.Length > 0 && !Guid.TryParse(c.BaseId, out _) || c.UserId.Length > 0 && (!Guid.TryParse(c.UserId, out _) || c.BaseId.Length == 0)) throw new ArgumentException("Для базы и пользователя 1С нужны точные GUID. Пустое поле означает только общие файлы.");
        var list = new List<ProfileFile>(); string root = Path.Combine(c.Roaming, "1C", c.Edition);
        foreach (string name in Global) list.Add(new("global/" + name, Path.Combine(root, name), "Общие настройки 1С"));
        list.Add(new("local/1cv8u.pfl", Path.Combine(c.Local, "1C", "1cv8", "1cv8u.pfl"), "Локальный профиль клиента"));
        if (c.BaseId.Length > 0)
        {
            string basePath = Path.Combine(root, Guid.Parse(c.BaseId).ToString());
            foreach (string name in Base) list.Add(new("base/" + name, Path.Combine(basePath, name), "Настройки выбранной регистрации базы"));
            if (c.UserId.Length > 0) foreach (string name in User) list.Add(new("user/" + name, Path.Combine(basePath, Guid.Parse(c.UserId).ToString(), name), "Настройки пользователя 1С по GUID"));
        }
        return list.Select(f => f with { Path = Path.GetFullPath(f.Path), Description = Describe(f.Key) }).ToList();
    }
    private static string Describe(string key) => key switch
    {
        "global/1Cv8.pfl" => "Общие настройки Конфигуратора: текстовый редактор, табло, программы сравнения и объединения.",
        "global/1Cv8c.pfl" => "Общие настройки тонкого клиента.",
        "global/1Cv8conn.pfl" => "Клиентские настройки соединений и сведения о резервных кластерах.",
        "global/1Cv8cmn.pfl" => "Оформление Конфигуратора: окна, шрифты, цвета, панели и недавно открытые файлы.",
        "global/1Cv8u.pfl" => "Данные, влияющие на идентификатор клиента 1С. Это не обычный файл оформления.",
        "global/1Cv8strt.pfl" => "Размеры и положение окна штатного стартера, настройки диалогов информационных баз.",
        "global/1Cv8prim.pfl" => "Общие настройки клиент-серверного режима.",
        "local/1cv8u.pfl" => "Локальный профиль идентификации клиента; находится в Local\\1C\\1cv8 независимо от выбранного издания Roaming.",
        "base/1Cv8.pfl" => "Настройки Конфигуратора этой базы: сравнение, поиск по конфигурации и отладка.",
        "base/1Cv8c.pfl" => "Настройки клиентского приложения этой базы, в том числе размещение заставки на мониторе.",
        "base/def.usr" => "Последнее имя пользователя, выбранное для входа в эту базу.",
        "user/1Cv8.pfl" => "Настройки Конфигуратора пользователя базы: хранилище, синтакс-помощник, табло и выражения.",
        "user/1Cv8c.pfl" => "Настройки тонкого клиента пользователя базы.",
        "user/1Cv8ccmn.pfl" => "Общие настройки клиентского приложения пользователя базы.",
        "user/1Cv8cmn.pfl" => "Оформление клиента пользователя базы: окна, шрифты, цвета, панели и недавно открытые файлы.",
        _ => "Файл профиля 1С."
    };
    private static bool Known(string key) => Global.Select(n => "global/" + n).Concat(Base.Select(n => "base/" + n)).Concat(User.Select(n => "user/" + n)).Append("local/1cv8u.pfl").Contains(key, StringComparer.Ordinal);
    private static byte[] ReadLocked(string path)
    {
        Ordinary(path); using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > MaximumFile) throw new InvalidDataException("Файл профиля превышает 32 МБ: " + path);
        using var data = new MemoryStream(); file.CopyTo(data); return data.ToArray();
    }
    private static Dictionary<string, byte[]> Capture(IEnumerable<ProfileFile> files)
    {
        var data = new Dictionary<string, byte[]>(StringComparer.Ordinal); int total = 0;
        foreach (var f in files) { if (!Known(f.Key)) throw new InvalidDataException("Неизвестный файл профиля."); var bytes = ReadLocked(f.Path); total += bytes.Length; if (total > MaximumTotal) throw new InvalidDataException("Выборка превышает 128 МБ."); data.Add(f.Key, bytes); }
        return data;
    }
    private static void Write(string path, ProfileContext c, Dictionary<string, byte[]> data, IEnumerable<string>? absent = null, bool overwrite = false)
    {
        Ordinary(path); var manifest = new ProfileArchiveManifest { Edition = c.Edition, BaseId = c.BaseId, UserId = c.UserId, AbsentFiles = absent?.ToList() ?? new() };
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                int i = 0;
                foreach (var pair in data)
                {
                    string payload = "files/" + i++ + ".bin"; using var stream = zip.CreateEntry(payload).Open(); stream.Write(pair.Value);
                    manifest.Files.Add(new() { Key = pair.Key, Payload = payload, Length = pair.Value.LongLength, Hash = Hash(pair.Value) });
                }
                using var json = zip.CreateEntry("manifest.json").Open(); JsonSerializer.Serialize(json, manifest);
            }
            if (overwrite && File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void Export(ProfileContext context, IEnumerable<string> keys, string path, bool overwrite = false)
    {
        var selected = Select(context, keys); if (selected.Count == 0) throw new ArgumentException("Выберите файлы.");
        Write(path, context, Capture(selected), overwrite: overwrite);
    }
    public static (ProfileArchiveManifest Manifest, Dictionary<string, byte[]> Data) Read(string path)
    {
        Ordinary(path); using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Это не архив профилей ВанБаз.");
        if (entry.Length > 64 * 1024 || zip.Entries.Count > 25) throw new InvalidDataException("Состав архива превышает ограничения.");
        using var json = entry.Open(); var manifest = JsonSerializer.Deserialize<ProfileArchiveManifest>(json) ?? throw new InvalidDataException("Повреждён манифест.");
        if (manifest.Version is not (1 or 2) || manifest.Edition is not ("1cv8" or "1cv82") || manifest.Files == null || manifest.AbsentFiles == null || manifest.Files.Count + manifest.AbsentFiles.Count > 20 || manifest.AbsentFiles.Any(k => !Known(k)) || manifest.AbsentFiles.Distinct().Count() != manifest.AbsentFiles.Count || manifest.Version == 1 && manifest.AbsentFiles.Count != 0) throw new InvalidDataException("Неподдерживаемый манифест.");
        var expected = new HashSet<string>(StringComparer.Ordinal) { "manifest.json" }; var data = new Dictionary<string, byte[]>(StringComparer.Ordinal); long total = 0;
        foreach (var item in manifest.Files)
        {
            if (!Known(item.Key) || item.Payload != "files/" + data.Count + ".bin" || !expected.Add(item.Payload) || item.Length < 0 || item.Length > MaximumFile || (total += item.Length) > MaximumTotal) throw new InvalidDataException("Недопустимый файл или размер в архиве.");
            var payload = zip.GetEntry(item.Payload) ?? throw new InvalidDataException("Нет файла из манифеста.");
            if (payload.Length != item.Length) throw new InvalidDataException("Размер файла не совпадает с манифестом.");
            using var stream = payload.Open(); using var memory = new MemoryStream(); var buffer = new byte[81920]; int n;
            while ((n = stream.Read(buffer)) > 0) { if (memory.Length + n > item.Length) throw new InvalidDataException("Распакованные данные превышают заявленный размер."); memory.Write(buffer, 0, n); }
            var bytes = memory.ToArray();
            if (Hash(bytes) != item.Hash) throw new InvalidDataException("Контрольная сумма профиля не совпадает."); data.Add(item.Key, bytes);
        }
        if (manifest.AbsentFiles.Any(data.ContainsKey) || zip.Entries.Count != expected.Count || zip.Entries.Any(e => !expected.Contains(e.FullName))) throw new InvalidDataException("В архиве есть лишние, противоречивые или повторные файлы.");
        return (manifest, data);
    }
    private static List<ProfileFile> Select(ProfileContext c, IEnumerable<string> keys)
    {
        var available = List(c).ToDictionary(x => x.Key, StringComparer.Ordinal); var selected = keys.Distinct().Select(k => available.TryGetValue(k, out var f) ? f : throw new InvalidDataException("Не задано назначение для " + k)).ToList();
        if (selected.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Логические файлы указывают в одно назначение. Проверьте каталоги Local и Roaming.");
        return selected;
    }
    public static string Change(ProfileContext context, IEnumerable<string> keys, string? archive, string recoveryFolder, Func<bool>? clientsRunning = null)
    {
        if ((clientsRunning ?? CacheService.ClientsRunning)()) throw new InvalidOperationException("Закройте сеансы 1С перед изменением профилей.");
        var selected = Select(context, keys); if (selected.Count == 0) throw new ArgumentException("Выберите файлы.");
        Dictionary<string, byte[]>? incoming = null;
        if (archive != null) { var loaded = Read(archive); if (loaded.Manifest.Edition != context.Edition) throw new InvalidDataException("Издание платформы архива и назначения отличается."); incoming = loaded.Data; if (selected.Any(f => !incoming.ContainsKey(f.Key) && !loaded.Manifest.AbsentFiles.Contains(f.Key))) throw new InvalidDataException("Выбранного файла нет в архиве."); }
        foreach (var f in selected) Ordinary(f.Path);
        var before = Capture(selected.Where(f => f.Exists));
        CacheService.EnsureOrdinaryPath(recoveryFolder); Directory.CreateDirectory(recoveryFolder);
        string backup = Path.Combine(recoveryFolder, "profiles-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        Write(backup, context, before, selected.Where(f => !before.ContainsKey(f.Key)).Select(f => f.Key));
        var recovery = FileRecovery.Begin(Path.Combine(recoveryFolder, "RecoveryOperations"), archive == null ? "Сброс PFL" : "Восстановление PFL", selected.ToDictionary(f => f.Path, f => incoming?.GetValueOrDefault(f.Key), StringComparer.OrdinalIgnoreCase));
        var guards = new List<FileStream>(); var changed = new List<ProfileFile>();
        try
        {
            // Complete preflight and lock all existing destinations before the first change.
            foreach (var f in selected)
            {
                Ordinary(f.Path); Directory.CreateDirectory(Path.GetDirectoryName(f.Path)!);
                if (before.TryGetValue(f.Key, out var old))
                {
                    var guard = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); guards.Add(guard);
                    using var memory = new MemoryStream(); guard.CopyTo(memory); if (Hash(memory.ToArray()) != Hash(old)) throw new IOException("Профиль изменился после чтения: " + f.Path);
                }
                else if (File.Exists(f.Path)) throw new IOException("В назначении появился новый файл: " + f.Path);
            }
            foreach (var f in selected)
            {
                if (incoming == null || !incoming.ContainsKey(f.Key)) { if (before.ContainsKey(f.Key)) { File.Delete(f.Path); changed.Add(f); } }
                else { Replace(f.Path, incoming[f.Key], before.ContainsKey(f.Key)); changed.Add(f); }
            }
            FileRecovery.Complete(recovery);
        }
        catch (Exception error)
        {
            foreach (var guard in guards) guard.Dispose(); guards.Clear(); var rollbackErrors = new List<string>();
            foreach (var f in changed.AsEnumerable().Reverse())
                try { if (before.TryGetValue(f.Key, out var bytes)) Replace(f.Path, bytes, File.Exists(f.Path)); else File.Delete(f.Path); }
                catch (Exception rollback) { rollbackErrors.Add(f.Path + ": " + rollback.Message); }
            throw new IOException("Операция не завершена: " + error.Message + "\nКопия прежних файлов: " + backup + (rollbackErrors.Count == 0 ? "\nВыполненные изменения возвращены." : "\nНужен ручной возврат:\n" + string.Join("\n", rollbackErrors)), error);
        }
        finally { foreach (var guard in guards) guard.Dispose(); }
        return backup;
    }
    private static void Replace(string path, byte[] bytes, bool exists)
    {
        Ordinary(path); string temp = path + ".vanbaz-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); } if (exists) File.Replace(temp, path, null); else File.Move(temp, path, false); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
