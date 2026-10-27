using System.IO;

namespace VanBaz;

public record BasePathChange(string Id, string Name, string Before, string After);
public static class CatalogRelocation
{
    public static List<BasePathChange> Preview(Catalog catalog, string oldRoot, string newRoot)
    {
        string Root(string value)
        {
            if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("Укажите полный путь, например D:\\Bases или \\\\server\\share.");
            return Path.GetFullPath(value).TrimEnd('\\', '/') + "\\";
        }
        string oldPrefix = Root(oldRoot), newPrefix = Root(newRoot);
        if (oldPrefix.Equals(newPrefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Старый и новый пути совпадают.");
        var changes = new List<BasePathChange>();
        foreach (var b in catalog.Bases.Where(b => b.Kind == "File" && Path.IsPathFullyQualified(b.Location)))
        {
            string location = Path.GetFullPath(b.Location).TrimEnd('\\', '/');
            bool exact = location.Equals(oldPrefix.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (exact || location.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                changes.Add(new(b.Id, b.Name, b.Location, exact ? Path.GetFullPath(newRoot) : newPrefix + location[oldPrefix.Length..]));
        }
        return changes;
    }
}
