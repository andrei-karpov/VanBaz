using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace VanBaz;

public static class StartManagerMigration
{
    public static string DefaultList => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C", "1CEStart", "ibases.v8i");
    public static string DefaultSettings => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StartManager14", "SMSettings");

    public static Dictionary<string, Dictionary<string, string>> ReadIni(string path)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); sections[""] = current;
        if (!File.Exists(path)) return sections;
        foreach (var raw in V8iImporter.ReadText(path).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            { current = new(StringComparer.OrdinalIgnoreCase); sections[line[1..^1]] = current; }
            else if (!line.StartsWith(';') && !line.StartsWith('#') && line.IndexOf('=') is var p && p > 0)
                current[line[..p].Trim()] = line[(p + 1)..].Trim();
        }
        return sections;
    }

    public static Catalog Prepare(Catalog original, string folder, string listFile)
    {
        if (!File.Exists(Path.Combine(folder, "v8config.smc"))) throw new FileNotFoundException("В папке нет v8config.smc. Выберите SMSettings действующего StartManager.");
        var result = original.Copy();
        var imported = V8iImporter.Read(listFile);
        var settings = ReadIni(Path.Combine(folder, "v8config.smc"));
        var report = new MigrationReport { SettingsFolder = Path.GetFullPath(folder), ListFile = Path.GetFullPath(listFile), SourceBases = imported.Entries.Count, SourceGroups = imported.Groups.Count };
        foreach (var b in imported.Entries)
        {
            if (settings.TryGetValue(b.Id, out var values))
            {
                report.MatchedSettings++;
                string S(string k) => values.GetValueOrDefault(k, "");
                bool B(string k) => S(k) is "1" or "true";
                b.SourceSettings = values.Where(p => !IsSecret(p.Key) && !p.Key.StartsWith("Backup", StringComparison.OrdinalIgnoreCase)).ToDictionary(p => p.Key, p => p.Value);
                b.Favorite = B("Favorite");
                if (values.ContainsKey("x64Bit")) b.Architecture = B("x64Bit") ? "x64" : "x86";
                if (B("V8UseAppFile")) b.Executable = S("V8AppFile");
                if (S("AppVer").Length > 0) b.Version = S("AppVer").TrimEnd('*', '.');
                b.Description = S("Description"); b.Notes = S("Note"); b.ConfigurationVersion = S("ConfigVersion");
                b.ExternalProcessor = S("EpfFile"); b.RunAsAdmin = B("RunAsAdmin"); b.DisallowDelete = B("DisallowDelete");
                b.CheckServerAgent = B("CheckRAgent");
                b.EnterpriseLogin = B("UserLoginEnt"); b.DesignerLogin = B("UserLoginCnf");
                b.UserName = S("EntUser"); b.DesignerUser = S("CfgUser");
                if (B("OSAuthentication")) b.WindowsAuthentication = "Да";
                b.UseRepository = B("UserStorage"); b.Repository = S("StorageDir"); b.RepositoryUser = S("StorageUser");
                if (DateTime.TryParse(S("LaunchDate"), CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var date) && date.Year > 1900) b.LastLaunch = date;
            }
            // Exact IDs preserve distinct registrations of the same connection. Existing user edits win on re-import.
            if (result.Bases.Any(x => x.Id.Equals(b.Id, StringComparison.OrdinalIgnoreCase))) { report.Existing++; continue; }
            result.Bases.Add(b); report.Added++;
        }
        foreach (var g in imported.Groups)
            if (!result.Groups.Any(x => x.Path.Equals(g.Path, StringComparison.OrdinalIgnoreCase))) result.Groups.Add(g);
        report.OrphanSettings = settings.Count(p => Guid.TryParse(p.Key, out _) && !imported.Entries.Any(b => b.Id.Equals(p.Key, StringComparison.OrdinalIgnoreCase)));
        ReadPreferences(result.Settings, folder);
        var keys = ReadIni(Path.Combine(folder, "v8keys.smc"))[""];
        foreach (var pair in keys)
        {
            string arguments = pair.Key.StartsWith('/') ? pair.Key : "/" + pair.Key;
            if (!result.Commands.Any(c => c.Arguments == arguments)) result.Commands.Add(new() { Name = pair.Value, Arguments = arguments });
        }
        foreach (var b in result.Bases.Where(b => b.LastLaunch != null))
            if (!result.History.Any(h => h.BaseId == b.Id && h.Time == b.LastLaunch))
                result.History.Add(new() { BaseId = b.Id, Name = b.Name, Mode = "StartManager", Time = b.LastLaunch!.Value });
        result.History = result.History.OrderByDescending(h => h.Time).Take(200).ToList();
        report.Notes.Add("Настройки сопоставлены по точному ID; записи без подключения не добавлены как базы.");
        report.Notes.Add("Перенос паролей исключён по выбору пользователя.");
        report.Notes.Add("Перенесены: список, группы, порядок, описания, даты запуска, версия/файл и разрядность платформы, имена пользователей, избранное, параметры оформления, шаблоны команд.");
        report.Notes.Add("Автозапуск и глобальные клавиши включаются вручную в ВанБаз. Палитры .vsf импортируются через раздел «Темы». Адрес прокси переносится без пароля; каталог конфигураций читается из v8updates.lst, если он присутствует.");
        if (imported.Skipped > 0) report.Notes.Add("Нераспознанных записей .v8i: " + imported.Skipped);
        result.Migration = report;
        if (result.Settings.SourceSettings.TryGetValue("StartMode", out var importedMode) && !LaunchBehavior.ConnectionModes.Contains(importedMode)) report.Notes.Add("Исходный способ запуска сохранён как справочное значение; числовое соответствие ключам 1С не доказано. Выберите способ в настройках ВанБаз.");
        ConfigurationService.ImportExistingSources(result);
        return result;
    }

    private static bool IsSecret(string key) => key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Contains("passwd", StringComparison.OrdinalIgnoreCase) || key.Contains("pwd", StringComparison.OrdinalIgnoreCase) || key.Equals("BlockPW", StringComparison.OrdinalIgnoreCase);

    private static void ReadPreferences(Preferences p, string folder)
    {
        var ini = ReadIni(Path.Combine(folder, "config.sme")).Values.SelectMany(x => x).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
        var xmlPath = Path.Combine(folder, "settings.cnf");
        var xml = File.Exists(xmlPath) ? XDocument.Load(xmlPath).Descendants().Where(x => !x.HasElements).GroupBy(x => x.Name.LocalName).ToDictionary(g => g.Key, g => g.Last().Value) : new Dictionary<string, string>();
        string S(string key, string fallback = "") => ini.GetValueOrDefault(key, xml.GetValueOrDefault(key, fallback));
        bool B(string key, bool fallback) => S(key, fallback ? "1" : "0") is "1" or "true";
        double N(string key, double fallback) => double.TryParse(S(key), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        p.FontName = S("FontName", p.FontName); p.FontSize = Math.Clamp(N("FontSize", p.FontSize * 72 / 96) * 96 / 72, 10, 38); p.RowHeight = Math.Clamp(N("DefNodeHeight", p.RowHeight), 20, 56);
        p.Alphabetical = B("SortByAlfa", p.Alphabetical); p.GroupsFirst = B("GroupsShowTop", p.GroupsFirst); p.ShowCounts = B("ShowChildCount", p.ShowCounts);
        p.ShowPlatform = B("ColVisible2", true); p.ShowDate = B("ColVisible3", true); p.ShowSize = B("ColVisible4", true);
        p.ShowHistory = B("ShowHistory", true); p.ShowCommands = B("ShowLinks", true); p.ShowStarter = B("ShowStarterBtn", true);
        p.CheckExistence = B("CheckBaseExistence", true); p.RestorePosition = B("RestorePosition", false);
        p.CloseToTray = B("OnClose", false); p.EscapeToTray = B("OnEscPressed", false); p.MinimizeToTray = B("OnMinimazed", false);
        p.AfterLaunchAction = S("ActionAfterRun") switch { "0" => "Свернуть в панель задач", "1" => "Свернуть в трей", "2" => "Закрыть ВанБаз", _ => "Ничего не делать" };
        p.DefaultDoubleClickAction = S("ActionOnDblClick") switch { "1" => "Предприятие с аутентификацией", "2" => "Предприятие с параметрами", "3" => "Конфигуратор", "5" => "Свойства", _ => "Предприятие" };
        p.DefaultVersion = S("V8DefVersion", "8.3"); p.DefaultFolder = S("V8DefaultFolder");
        p.AutoSnapshot = B("V8iAutoBackup", p.AutoSnapshot); p.AutoSnapshotDays = (int)Math.Clamp(N("V8iAfterDays", p.AutoSnapshotDays), 1, 3650);
        p.MonitorRepeatedLaunch = B("MonitorIBRestart", p.MonitorRepeatedLaunch); p.ShowLaunchBalloon = B("ShowBalloonOnLaunch", p.ShowLaunchBalloon); p.ShowLaunchButtonInRow = B("ShowRunButtonInRow", p.ShowLaunchButtonInRow);
        if (LaunchBehavior.ConnectionModes.Contains(S("StartMode"))) p.ConnectionMode = S("StartMode");
        p.SelectionIntensity = (int)Math.Clamp(N("SelectionBlendFactor", 128), 0, 255); p.SelectedId = S("NodeID"); p.Left = N("Left", -1); p.Top = N("Top", -1);
        p.Width = Math.Clamp(N("Right", p.Left + 850) - p.Left, 735, 1600); p.Height = Math.Clamp(N("Bottom", p.Top + 700) - p.Top, 590, 1200);
        p.UseListColors = B("UseColorList", true); p.UseFavoriteColor = B("SetFontColorFovorite", true);
        p.FavoriteColor = DelphiColor(S("FontColorFavorite"), p.FavoriteColor);
        foreach (var family in new[] { "81", "82", "83", "84" })
        {
            string version = family.Insert(1, "."); p.PlatformColors[version] = DelphiColor(S("FontColor" + family), "#141414");
            if (!B("UseFontColor" + family, true) && !p.DisabledPlatformColors.Contains(version)) p.DisabledPlatformColors.Add(version);
        }
        p.MissingColor = DelphiColor(S("FontColorNotExists"), p.MissingColor);
        p.SourceSettings = ini.Concat(xml).Where(x => !IsSecret(x.Key) && !x.Key.StartsWith("Backup", StringComparison.OrdinalIgnoreCase) && !x.Key.StartsWith("Snegopat", StringComparison.OrdinalIgnoreCase)).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Last().Value);
    }

    private static string DelphiColor(string text, string fallback)
    {
        if (text.StartsWith('$') && uint.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
            return $"#{n & 255:X2}{n >> 8 & 255:X2}{n >> 16 & 255:X2}";
        return text switch { "clBlack" or "clWindowText" => "#000000", "clWhite" => "#FFFFFF", "clRed" => "#FF0000", "clBlue" => "#0000FF", "clGreen" => "#008000", "clGray" => "#808080", "clSilver" => "#C0C0C0", "clNavy" => "#000080", "clMaroon" => "#800000", "clPurple" => "#800080", "clTeal" => "#008080", "clOlive" => "#808000", _ => fallback };
    }
}
