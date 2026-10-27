using System.IO;
using System.Text;

namespace VanBaz;

public record ImportResult(List<BaseEntry> Entries, int Skipped)
{
    public List<BaseGroup> Groups { get; init; } = new();
}

public static class V8iImporter
{
    public static ImportResult Read(string path)
    {
        path = Path.GetFullPath(path); var result = Parse(ReadText(path));
        foreach (var b in result.Entries) b.ListSources.Add(path);
        foreach (var g in result.Groups) g.ListSources.Add(path);
        return result;
    }
    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254) text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255) text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        else
        {
            try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(1251).GetString(bytes);
            }
        }
        return text;
    }
    public static ImportResult Parse(string text)
    {
        var rows = new List<BaseEntry>();
        var groups = new List<BaseGroup>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? name = null;
        int skipped = 0;
        void Flush()
        {
            if (name == null) return;
            if (string.IsNullOrWhiteSpace(name)) { skipped++; return; }
            decimal order = decimal.TryParse(fields.GetValueOrDefault("OrderInTree"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : rows.Count + groups.Count;
            string folder = fields.GetValueOrDefault("Folder", "").Trim('/', '\\').Replace('\\', '/');
            if (!fields.TryGetValue("Connect", out var connection))
            {
                if (!fields.ContainsKey("ID")) { skipped++; return; }
                groups.Add(new() { Id = fields["ID"], Path = (folder.Length > 0 ? folder + "/" : "") + name, Order = order, V8iFields = new(fields) });
                return;
            }
            string kind, location;
            try { (kind, location) = InfobaseConnection.Endpoint(InfobaseConnection.Parse(connection)); }
            catch (InvalidDataException) { skipped++; return; }
            rows.Add(new BaseEntry
            {
                Id = fields.TryGetValue("ID", out var id) && !string.IsNullOrWhiteSpace(id) ? id : new Guid(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name + "\n" + folder + "\n" + connection))[..16]).ToString(), Name = name, Kind = kind, Location = location,
                Group = folder, Order = order, V8iFields = new(fields),
                Version = fields.GetValueOrDefault("Version", "8.3").TrimEnd('*', '.'),
                WindowsAuthentication = fields.GetValueOrDefault("WA", "") switch { "0" => "Нет", "1" => "Да", _ => "Авто" },
                Client = fields.GetValueOrDefault("App", "Auto") switch { "ThinClient" => "Тонкий клиент", "ThickClient" => "Толстый клиент", _ => "Авто" }
            });
        }
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { Flush(); name = line[1..^1]; fields.Clear(); }
            else if (name != null && !line.StartsWith(';') && !line.StartsWith('#'))
            {
                int equal = line.IndexOf('=');
                if (equal > 0) fields[line[..equal].Trim()] = line[(equal + 1)..].Trim();
            }
        }
        Flush();
        return new(rows, skipped) { Groups = groups };
    }
    public static int Merge(Catalog catalog, IEnumerable<BaseEntry> entries)
    {
        int added = 0;
        foreach (var entry in entries)
        {
            if (catalog.Bases.FirstOrDefault(b => b.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)) is { } existing)
            { existing.ListSources = existing.ListSources.Concat(entry.ListSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); continue; }
            catalog.Bases.Add(entry); added++;
        }
        return added;
    }
    public static int Merge(Catalog catalog, ImportResult result)
    {
        foreach (var group in result.Groups)
            if (catalog.Groups.FirstOrDefault(g => g.Path.Equals(group.Path, StringComparison.OrdinalIgnoreCase)) is { } existing)
                existing.ListSources = existing.ListSources.Concat(group.ListSources).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            else catalog.Groups.Add(group);
        return Merge(catalog, result.Entries);
    }

    public static string Connection(BaseEntry b)
    {
        return InfobaseConnection.Format(InfobaseConnection.For(b));
    }

    public static void Export(Catalog catalog, string path)
    {
        var text = new StringBuilder();
        void Section(string name, Dictionary<string, string> fields)
        {
            if (name.IndexOfAny(new[] { '\r', '\n', ']' }) >= 0) throw new InvalidDataException("Имя базы или группы содержит недопустимые для .v8i символы.");
            text.AppendLine("[" + name + "]");
            foreach (var pair in fields)
            {
                if (pair.Value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new InvalidDataException("Многострочное значение нельзя записать в .v8i.");
                text.AppendLine(pair.Key + "=" + pair.Value);
            }
            text.AppendLine();
        }
        var groups = catalog.Groups.ToList();
        foreach (var b in catalog.Bases.Where(b => b.Version != "7.7"))
        {
            string prefix = "";
            foreach (var part in b.Group.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                prefix = prefix.Length == 0 ? part : prefix + "/" + part;
                if (!groups.Any(g => g.Path == prefix)) groups.Add(new() { Path = prefix });
            }
        }
        foreach (var g in groups.OrderBy(g => g.Order))
        {
            int slash = g.Path.LastIndexOf('/');
            var fields = new Dictionary<string, string>(g.V8iFields, StringComparer.OrdinalIgnoreCase)
            { ["ID"] = g.Id, ["Folder"] = slash < 0 ? "/" : "/" + g.Path[..slash], ["OrderInList"] = "-1", ["OrderInTree"] = g.Order.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            Section(g.Path[(slash + 1)..], fields);
        }
        foreach (var b in catalog.Bases.Where(b => b.Version != "7.7").OrderBy(b => b.Order))
        {
            string connect = Connection(b);
            var fields = new Dictionary<string, string>(b.V8iFields, StringComparer.OrdinalIgnoreCase)
            { ["Connect"] = connect, ["ID"] = b.Id, ["Folder"] = "/" + b.Group, ["Version"] = b.Version,
              ["App"] = b.Client switch { "Тонкий клиент" => "ThinClient", "Толстый клиент" or "Обычный режим" => "ThickClient", _ => "Auto" }, ["OrderInTree"] = b.Order.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (b.WindowsAuthentication != "Авто") fields["WA"] = b.WindowsAuthentication == "Да" ? "1" : "0";
            else fields.Remove("WA");
            Section(b.Name, fields);
        }
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
    }
}
