using System.IO;

namespace VanBaz;

public sealed class StartupOptions
{
    public string? DataFolder { get; private set; }
    public string? Mode { get; private set; }
    public bool LockMode { get; private set; }
    public bool Minimized { get; private set; }
    public static StartupOptions Parse(string[] args, string appFolder, string roamingFolder)
    {
        var result = new StartupOptions();
        void Folder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("После параметра каталога нужен путь.");
            string full = Path.GetFullPath(value);
            if (result.DataFolder != null && !result.DataFolder.Equals(full, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Указаны разные каталоги данных.");
            result.DataFolder = full;
        }
        void Mode(string value)
        {
            if (value is not ("Пользователь" or "Специалист" or "Разработчик")) throw new ArgumentException("Режим: Пользователь, Специалист или Разработчик.");
            if (result.Mode != null && result.Mode != value) throw new ArgumentException("Указаны разные режимы запуска.");
            result.Mode = value;
        }
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("У параметра отсутствует значение: " + arg);
            if (arg is "--data-dir" or "/sp") Folder(Next());
            else if (arg.StartsWith("/sp", StringComparison.Ordinal) && arg.Length > 3) Folder(args[i][3..]);
            else if (arg == "/asp") Folder(Path.Combine(appFolder, "Data"));
            else if (arg == "/usp") Folder(Path.Combine(roamingFolder, "VanBaz"));
            else if (arg == "/mode1") Mode("Пользователь");
            else if (arg == "/mode2") Mode("Специалист");
            else if (arg == "/mode3") Mode("Разработчик");
            else if (arg == "--mode") Mode(Next());
            else if (arg is "/selectmodeoff" or "--lock-mode") result.LockMode = true;
            else if (arg is "/min" or "--minimized") result.Minimized = true;
            else if (arg == "/second") throw new ArgumentException("Для независимого экземпляра укажите другой --data-dir. Один каталог не открывается двумя процессами.");
        }
        return result;
    }
}
