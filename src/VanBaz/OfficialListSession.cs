using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace VanBaz;

// The JSON is a cache plus VanBaz-only settings. Managed IDs distinguish a
// removed official registration from a pre-existing, as yet unpublished local one.
public sealed class OfficialListState
{
    public string Source { get; set; } = "";
    public List<string> ManagedIds { get; set; } = new();
    public Dictionary<string, BaseEntry> DetachedBases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, BaseGroup> DetachedGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OfficialListSession
{
    private static readonly StringComparer IdComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] Placement = { "Folder", "OrderInTree", "OrderInList" };
    private readonly Dictionary<string, V8iRecord> common = new(IdComparer);
    private readonly Dictionary<string, List<string>> origins = new(IdComparer);
    private Dictionary<string, V8iRecord> effective = new(IdComparer);
    public string UserList { get; }
    public List<string> Sources { get; } = new();
    public string? Warning { get; private set; }
    private OfficialListSession(string userList) => UserList = Path.GetFullPath(userList);
    public static string[] ConfigurationPaths => new[] {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "1C", "1CEStart", "1cestart.cfg"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C", "1CEStart", "1cestart.cfg") };

    // Missing configuration/user list is normal on a fresh installation. A missing
    // *referenced* corporate list is an error, never evidence that its bases were deleted.
    public static OfficialListSession Load(string? userList = null, IEnumerable<string>? configurationPaths = null)
    {
        var session = new OfficialListSession(userList ?? CatalogReconciler.StandardList);
        var seen = new HashSet<string>(IdComparer) { session.UserList };
        foreach (var cfg in configurationPaths ?? ConfigurationPaths)
        {
            var bytes = ReadOptional(cfg, 1024 * 1024); if (bytes == null) continue;
            foreach (string raw in Decode(bytes).Split('\n'))
            {
                string line = raw.Trim(); if (line.StartsWith(';') || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0 || !line[..eq].Trim().Equals("CommonInfoBases", StringComparison.OrdinalIgnoreCase)) continue;
                string path = Environment.ExpandEnvironmentVariables(line[(eq + 1)..].Trim().Trim('"'));
                if (path.Length == 0) continue;
                if (path.Contains("://", StringComparison.Ordinal)) throw new InvalidDataException("Общий список должен быть файлом .v8i: " + cfg);
                path = Path.GetFullPath(path, Path.GetDirectoryName(Path.GetFullPath(cfg))!);
                if (!seen.Add(path)) continue;
                if (seen.Count > 64) throw new InvalidDataException("В настройках указано больше 63 общих списков.");
                var document = V8iDocument.Parse(ReadOptional(path) ?? throw new FileNotFoundException("Не найден общий список: " + path));
                session.Sources.Add(path);
                foreach (var r in Canonical(document.Records).Values)
                {
                    // Earlier corporate sources supply the connection; later lists may supply placement.
                    var prior = session.common.Values.FirstOrDefault(p => IdComparer.Equals(p.Id, r.Id) || IdComparer.Equals(p.Name, r.Name));
                    var merged = prior == null ? Clone(r) : WithPlacement(prior, r);
                    session.common[merged.Id] = merged;
                    if (!session.origins.TryGetValue(merged.Id, out var paths)) session.origins[merged.Id] = paths = new();
                    paths.Add(path);
                }
            }
        }
        session.Sources.Add(session.UserList);
        session.effective = session.Combine(ReadDocument(session.UserList).Records);
        return session;
    }
    private static string Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        return reader.ReadToEnd();
    }
    private static byte[]? ReadOptional(string path, int maximum = 16 * 1024 * 1024)
    {
        CacheService.EnsureOrdinaryPath(path);
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > maximum) throw new InvalidDataException("Слишком большой файл списка: " + path);
            using var memory = new MemoryStream(); file.CopyTo(memory); return memory.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static V8iDocument ReadDocument(string path) => V8iDocument.Parse(ReadOptional(path) ?? Array.Empty<byte>());
    private static V8iRecord Clone(V8iRecord r) => new() { Id = r.Id, Name = r.Name, IsGroup = r.IsGroup, Fields = new(r.Fields, IdComparer) };
    private static V8iRecord WithPlacement(V8iRecord source, V8iRecord personal)
    {
        if (source.IsGroup != personal.IsGroup) throw new InvalidDataException("В разных списках совпало имя или ID базы и группы: " + source.Name);
        var result = Clone(source);
        foreach (string key in Placement) if (personal.Fields.TryGetValue(key, out var value)) result.Fields[key] = value;
        result.Fields["External"] = "1"; return result;
    }
    private Dictionary<string, V8iRecord> Combine(Dictionary<string, V8iRecord> personal)
    {
        var result = common.ToDictionary(p => p.Key, p => Clone(p.Value), IdComparer);
        foreach (var r in Canonical(personal).Values)
        {
            var shared = common.Values.FirstOrDefault(p => IdComparer.Equals(p.Id, r.Id) || IdComparer.Equals(p.Name, r.Name));
            if (shared != null) result[shared.Id] = WithPlacement(shared, r);
            else if (r.Fields.GetValueOrDefault("External") != "1") result[r.Id] = Clone(r);
        }
        return result;
    }
    private static Dictionary<string, V8iRecord> Canonical(Dictionary<string, V8iRecord> records)
    {
        var parsed = V8iImporter.Parse(string.Join("\n", records.Values.Select(r => r.Text)));
        return CatalogReconciler.Project(new Catalog { Bases = parsed.Entries, Groups = parsed.Groups });
    }
    public Catalog Refresh(Catalog previous) => Hydrate(previous, effective);
    private Catalog Hydrate(Catalog previous, Dictionary<string, V8iRecord> records)
    {
        var c = previous.Copy(); var state = c.OfficialLists;
        if (state.Source.Length > 0 && !IdComparer.Equals(state.Source, UserList)) throw new InvalidOperationException("Каталог уже связан с другим штатным списком.");
        state.DetachedBases = new(state.DetachedBases, IdComparer); state.DetachedGroups = new(state.DetachedGroups, IdComparer);
        var managed = state.ManagedIds.ToHashSet(IdComparer);
        foreach (var b in c.Bases.Where(b => b.Version != "7.7" && managed.Contains(b.Id) && !records.ContainsKey(b.Id)).ToList())
        { state.DetachedBases[b.Id] = b; c.Bases.Remove(b); }
        foreach (var g in c.Groups.Where(g => managed.Contains(g.Id) && !records.ContainsKey(g.Id)).ToList())
        { state.DetachedGroups[g.Id] = g; c.Groups.Remove(g); }
        foreach (var r in Canonical(records).Values)
        {
            var parsed = V8iImporter.Parse(r.Text);
            var paths = origins.TryGetValue(r.Id, out var origin) ? origin.ToList() : new List<string> { UserList };
            if (r.Fields.GetValueOrDefault("External") == "1" && !paths.Contains(UserList, IdComparer)) paths.Add(UserList);
            if (!r.IsGroup)
            {
                var incoming = parsed.Entries.Single();
                var b = c.Bases.FirstOrDefault(b => IdComparer.Equals(b.Id, r.Id)) ?? state.DetachedBases.GetValueOrDefault(r.Id)?.Copy() ?? incoming;
                if (b.Version == "7.7") throw new InvalidDataException("ID штатного списка совпал с базой 7.7: " + r.Name);
                b.Name = incoming.Name; b.Group = incoming.Group; b.Order = incoming.Order;
                b.Kind = incoming.Kind; b.Location = incoming.Location; b.Version = incoming.Version;
                b.Client = incoming.Client; b.WindowsAuthentication = incoming.WindowsAuthentication;
                b.V8iFields = incoming.V8iFields; b.ListSources = paths;
                c.Bases.RemoveAll(x => IdComparer.Equals(x.Id, r.Id)); c.Bases.Add(b); state.DetachedBases.Remove(r.Id);
            }
            else
            {
                var incoming = parsed.Groups.Single();
                var old = c.Groups.FirstOrDefault(g => IdComparer.Equals(g.Id, r.Id)) ?? state.DetachedGroups.GetValueOrDefault(r.Id);
                incoming.Color = old?.Color ?? incoming.Color; incoming.ListSources = paths;
                c.Groups.RemoveAll(g => IdComparer.Equals(g.Id, r.Id)); c.Groups.Add(incoming); state.DetachedGroups.Remove(r.Id);
            }
        }
        // A legacy local group may already use the same path as a newly discovered official one.
        c.Groups = c.Groups.GroupBy(g => g.Path, IdComparer).Select(g => g.OrderByDescending(x => records.ContainsKey(x.Id)).First()).ToList();
        state.Source = UserList; state.ManagedIds = records.Keys.ToList(); return c;
    }

    public bool Contains(string id) => effective.ContainsKey(id);
    public void ValidateLaunchName(BaseEntry entry)
    {
        var matches = effective.Values.Where(r => r.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || !matches[0].Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Имя отсутствует или неоднозначно в штатном списке 1С. Для этой базы выберите другой способ запуска.");
        var endpoint = InfobaseConnection.Endpoint(InfobaseConnection.Parse(matches[0].Fields.GetValueOrDefault("Connect", "")));
        if (endpoint.Kind != entry.Kind || !endpoint.Location.Equals(entry.Location, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Подключение базы в штатном списке изменилось. Обновите список перед запуском по имени.");
    }
    public bool IsCommon(string id) => common.ContainsKey(id);
    public static bool HasOfficialChanges(Catalog before, Catalog after)
    {
        var a = CatalogReconciler.Project(before); var b = CatalogReconciler.Project(after);
        return a.Keys.Union(b.Keys, IdComparer).Any(id => !CatalogReconciler.Equal(a.GetValueOrDefault(id), b.GetValueOrDefault(id)));
    }
    // Only the fields actually edited by this action are applied. An unrelated
    // concurrent edit in the native starter is retained; conflicting edits are rejected.
    public Catalog SaveChanges(Catalog before, Catalog after, CatalogStore store, Action? afterListWritten = null)
    {
        Warning = null;
        var old = CatalogReconciler.Project(before); var next = CatalogReconciler.Project(after);
        var changes = old.Keys.Union(next.Keys, IdComparer).Where(id => !CatalogReconciler.Equal(old.GetValueOrDefault(id), next.GetValueOrDefault(id))).ToList();
        if (changes.Count == 0) { store.Save(after); return after; }
        if (store.HasPendingRecovery()) throw new IOException("Сначала завершите восстановление списка.");
        string mutexId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(UserList.ToUpperInvariant())));
        using var mutex = new Mutex(false, "Local\\VanBaz-Official-" + mutexId);
        bool held; try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("Другой экземпляр сохраняет список 1С. Повторите действие.");
        try
        {
            var bytes = ReadOptional(UserList); var document = V8iDocument.Parse(bytes ?? Array.Empty<byte>());
            var personal = document.Records; var latest = Canonical(Combine(personal));
            foreach (string id in changes)
            {
                old.TryGetValue(id, out var was); next.TryGetValue(id, out var desired); latest.TryGetValue(id, out var now);
                bool shared = common.ContainsKey(id);
                if (desired == null)
                {
                    if (shared) throw new InvalidOperationException("Общую запись «" + was!.Name + "» нельзя удалить из корпоративного источника. Её состав управляется общим списком.");
                    if (now != null && !CatalogReconciler.Equal(was, now)) throw Conflict(was!.Name);
                    personal.Remove(id); continue;
                }
                if (now == null)
                {
                    if (was != null && effective.ContainsKey(id)) throw Conflict(desired.Name);
                    if (personal.Values.Any(r => IdComparer.Equals(r.Name, desired.Name) && !IdComparer.Equals(r.Id, id)) || common.Values.Any(r => IdComparer.Equals(r.Name, desired.Name) && !IdComparer.Equals(r.Id, id)))
                        throw new InvalidOperationException("В списке уже есть запись с именем «" + desired.Name + "».");
                    var added = Clone(desired); added.Fields.Remove("External"); personal[id] = added; continue;
                }
                if (was == null) throw Conflict(desired.Name);
                var keys = was.Fields.Keys.Union(desired.Fields.Keys, IdComparer).Where(k => was.Fields.GetValueOrDefault(k) != desired.Fields.GetValueOrDefault(k)).ToList();
                if (was.IsGroup != desired.IsGroup) throw Conflict(desired.Name);
                if (shared && (was.Name != desired.Name || keys.Any(k => !Placement.Contains(k, IdComparer))))
                    throw new InvalidOperationException("Подключение и имя общей записи «" + was.Name + "» задаются корпоративным списком. Можно менять личную группу и порядок; для других настроек создайте отдельную регистрацию.");
                var originalPersonal = personal.Values.FirstOrDefault(r => IdComparer.Equals(r.Id, id) || shared && IdComparer.Equals(r.Name, was.Name));
                var patched = Clone(originalPersonal ?? now);
                if (was.Name != desired.Name)
                {
                    if (now.Name != was.Name && now.Name != desired.Name) throw Conflict(was.Name);
                    if (latest.Values.Any(r => !IdComparer.Equals(r.Id, id) && IdComparer.Equals(r.Name, desired.Name))) throw Conflict(desired.Name);
                    patched.Name = desired.Name;
                }
                foreach (string key in keys)
                {
                    string? initial = was.Fields.GetValueOrDefault(key), target = desired.Fields.GetValueOrDefault(key), current = now.Fields.GetValueOrDefault(key);
                    if (current != initial && current != target) throw Conflict(was.Name);
                    if (target == null) patched.Fields.Remove(key); else patched.Fields[key] = target;
                }
                if (shared) patched.Fields["External"] = "1";
                personal[patched.Id] = patched;
            }
            byte[] output = document.Render(personal);
            var newEffective = Combine(V8iDocument.Parse(output).Records); var candidate = Hydrate(after, newEffective);
            if (File.Exists(store.FilePath)) store.Snapshot();
            Directory.CreateDirectory(Path.GetDirectoryName(UserList)!);
            string temp = UserList + ".vanbaz-" + Guid.NewGuid().ToString("N") + ".tmp";
            RecoveryOperation? op = null;
            try
            {
                using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(output); f.Flush(true); }
                using var guard = bytes == null ? null : new FileStream(UserList, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                var check = ReadOptional(UserList);
                if (bytes == null ? check != null : check == null || !bytes.SequenceEqual(check)) throw new IOException("Список изменился во время сохранения. Повторите действие.");
                op = FileRecovery.Begin(Path.Combine(store.Folder, "RecoveryOperations"), "Изменение штатного списка 1С", new Dictionary<string, byte[]?> {
                    [UserList] = output, [store.FilePath] = JsonSerializer.SerializeToUtf8Bytes(candidate, new JsonSerializerOptions { WriteIndented = true }) });
                if (bytes == null) File.Move(temp, UserList);
                else File.Replace(temp, UserList, UserList + ".vanbaz-" + Guid.NewGuid().ToString("N") + ".bak");
                afterListWritten?.Invoke(); store.Save(candidate, op);
                try { FileRecovery.Complete(op); }
                catch (Exception ex) { Warning = "Изменения сохранены; завершите журнал восстановления: " + ex.Message; }
            }
            catch
            {
                if (op != null) { try { FileRecovery.Restore(op); } catch { /* Durable recovery remains available; never overwrite third-party data. */ } }
                throw;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            effective = newEffective; return candidate;
        }
        finally { mutex.ReleaseMutex(); }
    }
    private static IOException Conflict(string name) => new("Запись «" + name + "» изменена в другом приложении. Нажмите F5 и повторите изменение; чужие данные не перезаписаны.");
}
