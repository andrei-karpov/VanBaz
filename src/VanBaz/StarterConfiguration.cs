using System.IO;

namespace VanBaz;

public static class StarterConfiguration
{
    public static string Explain(IEnumerable<string>? paths = null)
    {
        paths ??= new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "1C", "1CEStart", "1cestart.cfg"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C", "1CEStart", "1cestart.cfg") };
        var lines = new List<string> { "Настройки штатного стартера — только чтение. ВанБаз показывает DefaultVersion для сравнения; самостоятельно эти соответствия не применяет. Выбор ВанБаз: закреплённый EXE, иначе наиболее новая найденная версия с указанным префиксом, разрядностью и нужным клиентом. Платформа может сама переключить версию при подключении к серверу.", "" };
        foreach (string path in paths)
        {
            lines.Add(path);
            try
            {
                CacheService.EnsureOrdinaryPath(path);
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Файл больше 1 МБ.");
                var matches = V8iImporter.ReadText(path).Split('\n').Select(l => l.Trim()).Where(l => !l.StartsWith(';') && !l.StartsWith('#')).Where(l => l.IndexOf('=') > 0 && l[..l.IndexOf('=')].Trim().Equals("DefaultVersion", StringComparison.OrdinalIgnoreCase)).ToList();
                lines.AddRange(matches.Count == 0 ? new[] { "DefaultVersion не задан." } : matches);
            }
            catch (FileNotFoundException) { lines.Add("Файл отсутствует."); }
            catch (DirectoryNotFoundException) { lines.Add("Каталог отсутствует."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { lines.Add("Не прочитан: " + ex.Message); }
            lines.Add("");
        }
        return string.Join("\n", lines);
    }
}
