using System.Text.RegularExpressions;

namespace VanBaz;

// Import excludes credentials; source files and their recovery copies are not rewritten here.
public static class ImportPrivacy
{
    public static bool SecretKey(string key) => key.Equals("WSP", StringComparison.OrdinalIgnoreCase)
        || key.Equals("BlockPW", StringComparison.OrdinalIgnoreCase)
        || new[] { "password", "passwd", "pwd", "secret", "token" }.Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase));
    public static string WebAddress(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length == 0) return value;
        return new UriBuilder(uri) { UserName = "", Password = "" }.Uri.AbsoluteUri;
    }
    public static string Connection(string text)
    {
        var parts = InfobaseConnection.Parse(text); bool changed = false;
        foreach (string key in parts.Keys.ToArray())
        {
            if (SecretKey(key)) { parts.Remove(key); changed = true; }
            else if (key.Equals("ws", StringComparison.OrdinalIgnoreCase))
            { string clean = WebAddress(parts[key]); if (clean != parts[key]) { parts[key] = clean; changed = true; } }
        }
        return changed ? InfobaseConnection.Format(parts) : text;
    }
    public static string Arguments(string text)
    {
        try
        {
            var parsed = LaunchArguments.Parse(text); bool changed = false;
            var clean = new List<LaunchArgument>();
            foreach (var arg in parsed)
            {
                if (arg.Key == "/IBConnectionString")
                { string value = Connection(arg.Value!); clean.Add(arg with { Value = value }); changed |= value != arg.Value; }
                else if (arg.Secret) changed = true;
                else clean.Add(arg);
            }
            return changed ? string.Join(" ", clean.SelectMany(a => a.Value == null ? new[] { a.Key } : new[] { a.Key, DesktopIntegration.Quote(a.Value) })) : text;
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.InvalidDataException)
        {
            // Unknown option grammars cannot be safely split around a credential value.
            return Regex.IsMatch(text, @"(?i)(?:^|\s)(?:/(?:P(?!roxy)|WSP|AccessToken|ConfigurationRepositoryP|IBConnectionString)|-PPwd)") ? "" : text;
        }
    }
    public static Dictionary<string, string> Fields(IDictionary<string, string> source)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source)
        {
            if (SecretKey(pair.Key)) continue;
            fields[pair.Key] = pair.Key.Equals("Connect", StringComparison.OrdinalIgnoreCase) ? Connection(pair.Value)
                : pair.Key.Equals("AdditionalParameters", StringComparison.OrdinalIgnoreCase) ? Arguments(pair.Value) : pair.Value;
        }
        return fields;
    }
    public static void CleanImportedFields(Catalog catalog)
    {
        foreach (var entry in catalog.Bases.Concat(catalog.OfficialLists.DetachedBases.Values))
        {
            entry.V8iFields = Fields(entry.V8iFields);
            if (entry.Kind == "Web") entry.Location = WebAddress(entry.Location);
        }
        foreach (var group in catalog.Groups.Concat(catalog.OfficialLists.DetachedGroups.Values)) group.V8iFields = Fields(group.V8iFields);
        foreach (var record in catalog.ListSync.SelectMany(s => s.Agreed.Values)) record.Fields = Fields(record.Fields);
    }
}
