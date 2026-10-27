using System.IO;
using System.Text.RegularExpressions;

namespace VanBaz;

public static class InfobaseConnection
{
    public static Dictionary<string, string> Parse(string text)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); int pos = 0;
        if (text.Length > 65536 || text.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0) throw new InvalidDataException("Строка соединения слишком велика или содержит недопустимые символы.");
        while (pos < text.Length)
        {
            if (string.IsNullOrWhiteSpace(text[pos..])) break;
            var m = Regex.Match(text[pos..], "^\\s*(\\w+)\\s*=\\s*(?:\"((?:\"\"|[^\"])*)\"|([^;\"]*))\\s*(?:;|$)");
            if (!m.Success || !parts.TryAdd(m.Groups[1].Value, m.Groups[2].Success ? m.Groups[2].Value.Replace("\"\"", "\"") : m.Groups[3].Value.Trim())) throw new InvalidDataException("Повреждённая строка Connect или повторное имя параметра. Проверьте исходный список баз.");
            pos += m.Length;
        }
        return parts;
    }
    public static string Format(Dictionary<string, string> parts) => string.Join("", parts.Select(p => p.Key + "=\"" + p.Value.Replace("\"", "\"\"") + "\";"));
    public static (string Kind, string Location) Endpoint(Dictionary<string, string> parts)
    {
        bool file = parts.ContainsKey("File"), server = parts.ContainsKey("Srvr") || parts.ContainsKey("Ref"), web = parts.ContainsKey("ws");
        if ((file ? 1 : 0) + (server ? 1 : 0) + (web ? 1 : 0) != 1) throw new InvalidDataException("В Connect должно быть ровно одно файловое, серверное или веб-подключение.");
        string Required(string key) => parts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidDataException("В Connect не заполнен параметр " + key + ".");
        if (file) return ("File", Required("File"));
        if (web) return ("Web", Required("ws"));
        return ("Server", Required("Srvr").TrimEnd('/', '\\') + "/" + Required("Ref"));
    }
    public static string Preview(string text)
    {
        try
        {
            var parts = Parse(text);
            foreach (var key in parts.Keys.ToArray())
            {
                if (key.Equals("ws", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(parts[key], UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                        parts[key] = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri + (uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0 ? " [параметры скрыты]" : "");
                    else parts[key] = "••••••";
                }
                else if (!new[] { "File", "Srvr", "Ref" }.Contains(key, StringComparer.OrdinalIgnoreCase)) parts[key] = "••••••";
            }
            return Format(parts);
        }
        catch (InvalidDataException) { return "•••••• (строка подключения скрыта)"; }
    }
    public static Dictionary<string, string> For(BaseEntry b)
    {
        var fields = new Dictionary<string,string>(b.V8iFields, StringComparer.OrdinalIgnoreCase);
        var parts = Parse(fields.GetValueOrDefault("Connect", ""));
        foreach (string key in new[] { "File", "Srvr", "Ref", "ws" }) parts.Remove(key);
        if (b.Kind == "File") parts["File"] = b.Location;
        else if (b.Kind == "Web") parts["ws"] = b.Location;
        else if (b.Kind == "Server")
        {
            int slash = b.Location.IndexOfAny(new[] { '/', '\\' });
            if (slash <= 0 || slash == b.Location.Length - 1) throw new InvalidDataException("Укажите сервер и имя базы через разделитель /.");
            parts["Srvr"] = b.Location[..slash]; parts["Ref"] = b.Location[(slash + 1)..];
        }
        else throw new InvalidDataException("Неизвестный тип подключения.");
        // Validate values just added from editable fields as well.
        return Parse(Format(parts));
    }
    public static bool HasAdditional(Dictionary<string,string> parts) => parts.Keys.Any(k => !new[] { "File", "Srvr", "Ref", "ws" }.Contains(k, StringComparer.OrdinalIgnoreCase));
}
