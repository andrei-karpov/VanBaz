using System.IO;

namespace VanBaz;

public sealed class ListSource
{
    public string Path { get; set; } = "";
    public string Platform { get; set; } = "Авто";
    public bool Enabled { get; set; } = true;
}
public static class ListSourceService
{
    public static readonly string[] Platforms = { "Авто", "8.1", "8.2", "8.3", "8.4", "8.5" };
    public static List<ListSource> Sources(Preferences settings)
    {
        var sources = settings.PlatformLists.Select(s => new ListSource { Path = s.Path, Platform = s.Platform, Enabled = s.Enabled }).ToList();
        foreach (var path in settings.AlternativeLists)
            if (!sources.Any(s => s.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) sources.Add(new() { Path = path });
        return sources;
    }
    public static void Validate(ListSource source)
    {
        if (!System.IO.Path.IsPathFullyQualified(source.Path) || !System.IO.Path.GetExtension(source.Path).Equals(".v8i", StringComparison.OrdinalIgnoreCase) || !Platforms.Contains(source.Platform))
            throw new InvalidDataException("Укажите полный путь к .v8i и платформу 8.x или «Авто».");
    }
    public static ImportResult Read(ListSource source)
    {
        Validate(source); var result = V8iImporter.Read(source.Path);
        if (source.Platform != "Авто") foreach (var entry in result.Entries) entry.Version = source.Platform;
        return result;
    }
    public static int Merge(Catalog catalog, ImportResult import)
    {
        return V8iImporter.Merge(catalog, import);
    }
}
