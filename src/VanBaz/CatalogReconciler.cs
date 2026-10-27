using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VanBaz;

public sealed class V8iRecord
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsGroup { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Display => Name + (IsGroup ? " [группа]" : "") + " · " + Id;
    public string Text => "[" + Name + "]\n" + string.Join("\n", Fields.Select(p => p.Key + "=" + p.Value));
    public string PreviewText => "[" + Name + "]\n" + string.Join("\n", Fields.Select(p => p.Key + "=" +
        (p.Key.Equals("Connect", StringComparison.OrdinalIgnoreCase) ? InfobaseConnection.Preview(p.Value) :
        p.Key.Equals("AdditionalParameters", StringComparison.OrdinalIgnoreCase) || new[] { "password", "pwd", "secret", "token" }.Any(k => p.Key.Contains(k, StringComparison.OrdinalIgnoreCase)) ? "••••••" : p.Value)));
}
public sealed class ListSyncState
{
    public string Source { get; set; } = "";
    public Dictionary<string, V8iRecord> Agreed { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class ReconcileRow
{
    public string Id { get; init; } = "";
    public V8iRecord? Local { get; init; }
    public V8iRecord? External { get; init; }
    public string Name => (Local ?? External)!.Display;
    public string Change { get; init; } = "";
    public string Choice { get; set; } = "Пропустить";
    public string DifferenceFields
    {
        get
        {
            if (Local == null || External == null) return "Запись есть только с одной стороны";
            var fields = Local.Fields.Keys.Union(External.Fields.Keys, StringComparer.OrdinalIgnoreCase).Where(key =>
                !string.Equals(Local.Fields.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value,
                    External.Fields.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value,
                    key.Equals("ID", StringComparison.OrdinalIgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToList();
            if (Local.Name != External.Name) fields.Insert(0, "Наименование");
            if (Local.IsGroup != External.IsGroup) fields.Insert(0, "Тип записи");
            return "Различаются поля: " + string.Join(", ", fields);
        }
    }
    public string Detail => DifferenceFields + "\n\nVANBAZ\n" + (Local?.PreviewText ?? "Записи нет") + "\n\nФАЙЛ 1С\n" + (External?.PreviewText ?? "Записи нет");
}
public sealed class ReconcilePlan
{
    public bool CanWriteSource { get; init; } = true;
    public string[] AllowedChoices => CanWriteSource ? CatalogReconciler.Choices : CatalogReconciler.Choices.Take(2).ToArray();
    public string Path { get; init; } = "";
    public string SourceHash { get; init; } = "";
    public string CatalogHash { get; init; } = "";
    public List<ReconcileRow> Rows { get; init; } = new();
    internal V8iDocument Document { get; init; } = null!;
}

// Retains comments, unrecognised fields, original order and encoding of the external file.
internal sealed class V8iDocument
{
    internal sealed class Section
    {
        public string Name = "";
        public List<string> Lines = new();
        public V8iRecord Record = null!;
    }
    public byte[] Bytes = Array.Empty<byte>();
    public Encoding Encoding = new UTF8Encoding(true);
    public bool Bom;
    public string Newline = "\r\n";
    public List<string> Prefix = new();
    public List<Section> Sections = new();
    public Dictionary<string, V8iRecord> Records => Sections.ToDictionary(s => s.Record.Id, s => s.Record, StringComparer.OrdinalIgnoreCase);
    public static V8iDocument Read(string path) => Parse(File.ReadAllBytes(path));
    public static V8iDocument Parse(byte[] bytes)
    {
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Список превышает 16 МБ.");
        var d = new V8iDocument { Bytes = bytes };
        int offset = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 255, 254 })) { d.Encoding = new UnicodeEncoding(false, true, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 254, 255 })) { d.Encoding = new UnicodeEncoding(true, true, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) { d.Encoding = new UTF8Encoding(true, true); offset = 3; }
        else
        {
            try { new UTF8Encoding(false, true).GetString(bytes); d.Encoding = new UTF8Encoding(false, true); }
            catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); d.Encoding = Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        }
        d.Bom = offset > 0;
        string text = d.Encoding.GetString(bytes, offset, bytes.Length - offset);
        if (text.Contains('\0')) throw new InvalidDataException("Повреждённый список: нулевые символы.");
        d.Newline = text.Contains("\r\n") ? "\r\n" : "\n";
        Section? section = null;
        foreach (var raw in text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']') || line.Length < 3) throw new InvalidDataException("Повреждён заголовок .v8i: " + line);
                section = new() { Name = line[1..^1] }; d.Sections.Add(section);
            }
            else if (section == null) d.Prefix.Add(raw);
            else section.Lines.Add(raw);
        }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in d.Sections)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in s.Lines)
            {
                string line = raw.Trim(); if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                int eq = line.IndexOf('=');
                if (eq < 1 || !fields.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim())) throw new InvalidDataException("Повторное поле или повреждённая строка в «" + s.Name + "».");
            }
            string id = fields.GetValueOrDefault("ID", "");
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new InvalidDataException("Для согласования нужны непустые уникальные ID у всех баз и групп.");
            if (fields.TryGetValue("Connect", out var connection))
            {
                InfobaseConnection.Endpoint(InfobaseConnection.Parse(connection));
            }
            var parsed = V8iImporter.Parse("[" + s.Name + "]\n" + string.Join("\n", fields.Select(x => x.Key + "=" + x.Value)));
            if (parsed.Skipped != 0 || parsed.Entries.Count + parsed.Groups.Count != 1) throw new InvalidDataException("Неподдерживаемая или повреждённая запись: " + s.Name);
            s.Record = new() { Id = id, Name = s.Name, IsGroup = !fields.ContainsKey("Connect"), Fields = fields };
        }
        return d;
    }
    public byte[] Render(Dictionary<string, V8iRecord> target)
    {
        foreach (var record in target.Values)
            if (string.IsNullOrWhiteSpace(record.Name) || record.Name.IndexOfAny(new[] { '\r', '\n', ']', '\0' }) >= 0 || record.Fields.Any(p => p.Key.IndexOfAny(new[] { '\r', '\n', '=', '\0' }) >= 0 || p.Value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0))
                throw new InvalidDataException("Имя или поле записи содержит недопустимые символы .v8i.");
        var lines = new List<string>(Prefix); var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in Sections)
        {
            if (!target.TryGetValue(s.Record.Id, out var record)) continue;
            // Keep implicit ordering stable when preceding records are deleted.
            if (!record.Fields.ContainsKey("OrderInTree")) record.Fields["OrderInTree"] = Sections.IndexOf(s).ToString();
            lines.Add("[" + record.Name + "]"); emitted.Add(record.Id);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in s.Lines)
            {
                var trimmed = raw.Trim(); int eq = trimmed.IndexOf('=');
                if (eq <= 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#')) { lines.Add(raw); continue; }
                var key = trimmed[..eq].Trim(); keys.Add(key);
                if (record.Fields.TryGetValue(key, out var value)) lines.Add(value == trimmed[(eq + 1)..].Trim() ? raw : key + "=" + value);
            }
            foreach (var pair in record.Fields.Where(x => !keys.Contains(x.Key))) lines.Add(pair.Key + "=" + pair.Value);
        }
        foreach (var record in target.Values.Where(x => !emitted.Contains(x.Id))) { lines.Add(""); lines.Add("[" + record.Name + "]"); lines.AddRange(record.Fields.Select(x => x.Key + "=" + x.Value)); }
        byte[] body = Encoding.GetBytes(string.Join(Newline, lines) + Newline);
        return Bom ? Encoding.GetPreamble().Concat(body).ToArray() : body;
    }
}

public static class CatalogReconciler
{
    public static string StandardList => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C", "1CEStart", "ibases.v8i");
    public static bool IsStandardList(string path) => System.IO.Path.GetFullPath(path).Equals(StandardList, StringComparison.OrdinalIgnoreCase);
    public static readonly string[] Choices = { "Пропустить", "Из файла в ВанБаз", "Из ВанБаз в файл" };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string CatalogHash(Catalog c) => Hash(JsonSerializer.SerializeToUtf8Bytes(c));
    internal static bool Equal(V8iRecord? a, V8iRecord? b) => a == null ? b == null : b != null && a.Name == b.Name && a.IsGroup == b.IsGroup && a.Fields.Count == b.Fields.Count && a.Fields.All(p => b.Fields.Any(q => p.Key.Equals(q.Key, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Value, q.Value, p.Key.Equals("ID", StringComparison.OrdinalIgnoreCase) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)));
    public static Dictionary<string, V8iRecord> Project(Catalog catalog)
    {
        var result = new Dictionary<string, V8iRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in catalog.Bases.Where(x => x.Version != "7.7"))
        {
            var f = new Dictionary<string, string>(b.V8iFields, StringComparer.OrdinalIgnoreCase);
            f["ID"] = b.Id; f["Connect"] = V8iImporter.Connection(b); f["Folder"] = "/" + b.Group; f["OrderInTree"] = b.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (f.GetValueOrDefault("Version", "").TrimEnd('*', '.') != b.Version) f["Version"] = b.Version;
            f["App"] = b.Client switch { "Тонкий клиент" => "ThinClient", "Толстый клиент" or "Обычный режим" => "ThickClient", _ => "Auto" };
            if (b.WindowsAuthentication == "Авто") f.Remove("WA"); else f["WA"] = b.WindowsAuthentication == "Да" ? "1" : "0";
            result.Add(b.Id, new() { Id = b.Id, Name = b.Name, Fields = f });
        }
        foreach (var g in catalog.Groups)
        {
            int slash = g.Path.LastIndexOf('/'); var f = new Dictionary<string, string>(g.V8iFields, StringComparer.OrdinalIgnoreCase);
            f["ID"] = g.Id; f["Folder"] = slash < 0 ? "/" : "/" + g.Path[..slash]; f["OrderInTree"] = g.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);
            result.Add(g.Id, new() { Id = g.Id, Name = g.Path[(slash + 1)..], IsGroup = true, Fields = f });
        }
        return result;
    }
    private static Dictionary<string, V8iRecord> Canonical(IEnumerable<V8iRecord> records)
    {
        // Normalise defaults exactly as ВанБаз represents them; unknown attributes remain part of the comparison.
        // Parse together: an omitted OrderInTree derives from the position in the complete list.
        var parsed = V8iImporter.Parse(string.Join("\n", records.Select(r => r.Text))); var c = new Catalog { Bases = parsed.Entries, Groups = parsed.Groups };
        return Project(c);
    }
    public static ReconcilePlan Prepare(Catalog catalog, string path, bool? allowSourceWrite = null)
    {
        path = Path.GetFullPath(path); CacheService.EnsureOrdinaryPath(path);
        bool canWrite = allowSourceWrite ?? IsStandardList(path);
        var document = V8iDocument.Read(path); var external = Canonical(document.Records.Values);
        var local = Project(catalog); var previous = catalog.ListSync.FirstOrDefault(s => s.Source.Equals(path, StringComparison.OrdinalIgnoreCase));
        var rows = new List<ReconcileRow>();
        foreach (string id in local.Keys.Union(external.Keys, StringComparer.OrdinalIgnoreCase))
        {
            local.TryGetValue(id, out var l); external.TryGetValue(id, out var e);
            if (Equal(l, e)) continue;
            V8iRecord? old = null; bool tracked = previous?.Agreed.TryGetValue(id, out old) == true;
            bool lc = !Equal(l, old), ec = !Equal(e, old);
            string change = !tracked ? l == null ? "Новая запись в файле" : e == null ? "Только в ВанБаз" : "Различие: исходное состояние неизвестно" : lc && ec ? "Конфликт: изменены обе стороны" : l == null ? "Удалено из ВанБаз" : e == null ? "Удалено из файла" : lc ? "Изменено в ВанБаз" : "Изменено в файле";
            string choice = l == null || e == null ? (l == null && !tracked ? Choices[1] : Choices[0]) : tracked && lc != ec ? (lc ? Choices[2] : Choices[1]) : Choices[0];
            if (!canWrite && choice == Choices[2]) choice = Choices[0];
            rows.Add(new() { Id = id, Local = l, External = e, Change = change, Choice = choice });
        }
        return new() { Path = path, CanWriteSource = canWrite, SourceHash = Hash(document.Bytes), CatalogHash = CatalogHash(catalog), Rows = rows, Document = document };
    }
    private static void ImportRecord(Catalog c, string id, V8iRecord? record)
    {
        var existing = c.Bases.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (record == null)
        {
            if (existing?.DisallowDelete == true) throw new InvalidOperationException("Удаление записи запрещено: " + existing.Name);
            c.Bases.RemoveAll(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); c.Groups.RemoveAll(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); return;
        }
        if (existing != null && record.IsGroup || c.Groups.Any(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) && !record.IsGroup) throw new InvalidDataException("ID сменил тип база/группа: " + id);
        var parsed = V8iImporter.Parse(record.Text);
        if (record.IsGroup)
        {
            var g = parsed.Groups.Single(); var old = c.Groups.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (old != null) { g.Color = old.Color; g.ListSources = old.ListSources.ToList(); c.Groups.Remove(old); } c.Groups.Add(g);
        }
        else
        {
            var fresh = parsed.Entries.Single(); var b = existing?.Copy() ?? fresh;
            b.Name = fresh.Name; b.Kind = fresh.Kind; b.Location = fresh.Location; b.Group = fresh.Group; b.Order = fresh.Order; b.Version = fresh.Version; b.Client = fresh.Client; b.WindowsAuthentication = fresh.WindowsAuthentication; b.V8iFields = fresh.V8iFields;
            if (existing != null) c.Bases.Remove(existing); c.Bases.Add(b);
        }
    }
    public static (Catalog Catalog, string? Backup, string? Warning) Apply(Catalog current, CatalogStore store, ReconcilePlan plan, Action<RecoveryOperation>? completeJournal = null)
    {
        if (store.HasPendingRecovery()) throw new IOException("Сначала завершите прежнюю операцию согласования в меню восстановления.");
        if (CatalogHash(current) != plan.CatalogHash || Hash(File.ReadAllBytes(plan.Path)) != plan.SourceHash) throw new IOException("Каталог или файл изменился после сравнения. Постройте новый план.");
        CacheService.EnsureOrdinaryPath(plan.Path);
        var candidate = current.Copy(); var external = plan.Document.Records; bool write = false;
        foreach (var row in plan.Rows)
        {
            if (!plan.AllowedChoices.Contains(row.Choice)) throw new InvalidDataException("Запись во внешний список не разрешена. Вернитесь к выбору источника и явно разрешите её.");
            if (row.Choice == Choices[1]) ImportRecord(candidate, row.Id, row.External);
            if (row.Choice == Choices[2]) { write = true; if (row.Local == null) external.Remove(row.Id); else external[row.Id] = row.Local; }
        }
        if (candidate.Groups.GroupBy(g => g.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new InvalidDataException("Группы с одинаковым путём имеют разные ID. Согласуйте их выбор вручную.");
        byte[] output = write ? plan.Document.Render(external) : plan.Document.Bytes;
        var validated = Canonical(V8iDocument.Parse(output).Records.Values);
        var projected = Project(candidate);
        var state = candidate.ListSync.FirstOrDefault(s => s.Source.Equals(plan.Path, StringComparison.OrdinalIgnoreCase));
        if (state == null) { state = new() { Source = plan.Path }; candidate.ListSync.Add(state); }
        foreach (var id in state.Agreed.Keys.Union(projected.Keys, StringComparer.OrdinalIgnoreCase).Union(validated.Keys, StringComparer.OrdinalIgnoreCase).ToList())
        {
            projected.TryGetValue(id, out var l); validated.TryGetValue(id, out var e);
            if (Equal(l, e))
            {
                if (l == null) state.Agreed.Remove(id);
                else
                {
                    state.Agreed[id] = l;
                    var sources = candidate.Bases.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.ListSources
                        ?? candidate.Groups.FirstOrDefault(g => g.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.ListSources;
                    if (sources != null && !sources.Contains(plan.Path, StringComparer.OrdinalIgnoreCase)) sources.Add(plan.Path);
                }
            }
        }
        if (File.Exists(store.FilePath)) store.Snapshot();
        RecoveryOperation? recovery = write ? FileRecovery.Begin(Path.Combine(store.Folder, "RecoveryOperations"), "Согласование ibases.v8i", new Dictionary<string, byte[]?> { [plan.Path] = output, [store.FilePath] = JsonSerializer.SerializeToUtf8Bytes(candidate, new JsonSerializerOptions { WriteIndented = true }) }) : null;
        string? backup = null; string temp = plan.Path + ".vanbaz-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (write)
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(output); stream.Flush(true); }
                backup = plan.Path + ".vanbaz-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".bak";
                // The open handle denies new writers throughout the final compare/replace. FileShare.Delete permits our atomic replacement.
                using var guard = new FileStream(plan.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                using var snapshot = new MemoryStream(); guard.CopyTo(snapshot);
                if (Hash(snapshot.ToArray()) != plan.SourceHash) throw new IOException("Файл изменён другим процессом. Повторите сравнение.");
                File.Replace(temp, plan.Path, backup);
            }
            try { store.Save(candidate, recovery); }
            catch
            {
                if (backup != null && Hash(File.ReadAllBytes(plan.Path)) == Hash(output)) File.Replace(backup, plan.Path, null);
                throw;
            }
            string? warning = null;
            // Both data files are already committed. A journal error must not leave the UI on the old catalog.
            if (recovery != null)
                try { (completeJournal ?? FileRecovery.Complete)(recovery); }
                catch (Exception ex) { warning = "Данные сохранены, но журнал не отмечен завершённым: " + ex.Message + ". Операция остаётся в меню восстановления."; }
            return (candidate, backup, warning);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
