using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VanBaz;

public static class LegacyListFile
{
    private const string Section = @"HKEY_CURRENT_USER\Software\1C\1Cv7\7.7\Titles";
    public static string Id(string path) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).TrimEnd('\\').ToUpperInvariant()))[..16]).ToString();
    public static List<BaseEntry> Read(string file)
    {
        if (new FileInfo(file).Length > 8 * 1024 * 1024) throw new InvalidDataException("Слишком большой список 7.7.");
        return Parse(V8iImporter.ReadText(file));
    }
    public static List<BaseEntry> Parse(string text)
    {
        var result = new List<BaseEntry>(); bool selected = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('\uFEFF');
            if (line.StartsWith('[')) { selected = line.Equals("[" + Section + "]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!selected || line.Length == 0 || line.StartsWith(';')) continue;
            var match = Regex.Match(line, "^\"((?:[^\"\\\\]|\\\\[\"\\\\])*)\"\\s*=\\s*\"((?:[^\"\\\\]|\\\\[\"\\\\])*)\"$");
            if (!match.Success) throw new InvalidDataException("Поддерживаются только строки названий баз в разделе Titles; команды удаления и двоичные значения не выполняются.");
            string Decode(string s) => Regex.Replace(s, @"\\([\\"" ])", "$1");
            string path = Decode(match.Groups[1].Value), name = Decode(match.Groups[2].Value);
            if (!Path.IsPathFullyQualified(path) || name.Length == 0 || path.IndexOfAny(new[] { '\r', '\n', '"' }) >= 0) throw new InvalidDataException("Некорректный каталог или название базы 7.7.");
            path = Path.GetFullPath(path).TrimEnd('\\');
            if (result.Any(b => b.Id == Id(path))) continue;
            result.Add(new() { Id = Id(path), Name = name, Location = path, Version = "7.7", Kind = "File", Architecture = "x86", Group = "1С:Предприятие 7.7", EnterpriseLogin = false, DesignerLogin = false });
        }
        if (result.Count == 0) throw new InvalidDataException("В файле нет списка HKCU\\Software\\1C\\1Cv7\\7.7\\Titles.");
        return result;
    }
    public static string Export(IEnumerable<BaseEntry> entries)
    {
        string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var lines = new List<string> { "Windows Registry Editor Version 5.00", "", "[" + Section + "]" };
        foreach (var entry in entries.Where(b => b.Version == "7.7" && b.Kind == "File"))
        {
            if (!Path.IsPathFullyQualified(entry.Location) || (entry.Name + entry.Location).IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new InvalidDataException("Некорректная запись 7.7: " + entry.Name);
            lines.Add("\"" + Escape(entry.Location) + "\"=\"" + Escape(entry.Name) + "\"");
        }
        return string.Join("\r\n", lines) + "\r\n";
    }
}
