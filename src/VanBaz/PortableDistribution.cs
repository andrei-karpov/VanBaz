using System.IO;
using System.Text.Json;

namespace VanBaz;

public static class PortableDistribution
{
    public const string Manifest = "portable-files.json";
    public static bool IncludesRuntime(string folder) => File.Exists(Path.Combine(folder, "hostfxr.dll")) && File.Exists(Path.Combine(folder, "coreclr.dll"));
    public static string[] Files(string folder)
    {
        if (!IncludesRuntime(folder)) return AppUpdateService.Files;
        string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var manifest = Path.Combine(root, Manifest);
        CacheService.EnsureOrdinaryPath(manifest);
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 1024 * 1024) throw new IOException("Не найден список файлов портативной сборки. Распакуйте полный ZIP из раздела Releases.");
        var files = JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? throw new IOException("Повреждён список файлов портативной сборки.");
        if (files.Length > 2000 || files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length || !AppUpdateService.Files.Concat(new[] { Manifest, "hostfxr.dll", "coreclr.dll" }).All(files.Contains)) throw new IOException("Неполный список файлов портативной сборки.");
        foreach (string name in files)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Split('/', '\\').Any(p => p is ".." or "." || p.Equals("Data", StringComparison.OrdinalIgnoreCase)) || name.Contains(':')) throw new IOException("Недопустимый путь в списке файлов сборки.");
            string full = Path.GetFullPath(Path.Combine(root, name));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Файл находится вне каталога сборки.");
            CacheService.EnsureOrdinaryPath(full);
            if (!File.Exists(full)) throw new IOException("Отсутствует файл портативной сборки: " + name);
        }
        return files;
    }
}
