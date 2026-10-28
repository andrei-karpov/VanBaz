using System.IO;

namespace VanBaz;

public static class FeatureMigration
{
    public const int CurrentVersion = 8;
    public static bool Apply(Catalog catalog)
    {
        if (catalog.FeatureMigrationVersion >= CurrentVersion) return false;
        foreach (var settings in catalog.Bases.Select(b => b.SourceSettings).Append(catalog.Settings.SourceSettings))
            foreach (string key in settings.Keys.Where(k => k.StartsWith("Backup", StringComparison.OrdinalIgnoreCase)).ToArray()) settings.Remove(key);
        var p = catalog.Settings;
        if (catalog.FeatureMigrationVersion < 8)
        {
            foreach (var entry in catalog.Bases)
                if (entry.SourceSettings.TryGetValue("CheckRAgent", out string? check)) entry.CheckServerAgent = check == "1" || check.Equals("true", StringComparison.OrdinalIgnoreCase);
            bool ReadFlag(string key, bool current) => p.SourceSettings.TryGetValue(key, out string? value) ? value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" : current;
            if (int.TryParse(p.SourceSettings.GetValueOrDefault("V8iAfterDays"), out int days) && days is >= 1 and <= 3650) p.AutoSnapshotDays = days;
            p.MonitorRepeatedLaunch = ReadFlag("MonitorIBRestart", p.MonitorRepeatedLaunch); p.ShowLaunchBalloon = ReadFlag("ShowBalloonOnLaunch", p.ShowLaunchBalloon);
            if (catalog.FeatureMigrationVersion == 0) { p.AutoSnapshot = ReadFlag("V8iAutoBackup", p.AutoSnapshot); p.ShowLaunchButtonInRow = ReadFlag("ShowRunButtonInRow", p.ShowLaunchButtonInRow); }
            if (p.SourceSettings.TryGetValue("StartMode", out var mode) && LaunchBehavior.ConnectionModes.Contains(mode)) p.ConnectionMode = mode;
            else if (p.SourceSettings.ContainsKey("StartMode") && catalog.Migration is { } report && !report.Notes.Any(n => n.Contains("числовой способ запуска"))) report.Notes.Add("Исходный числовой способ запуска сохранён в сведениях переноса; его соответствие ключам 1С не доказано. Выберите способ подключения в настройках ВанБаз.");
        }
        if (p.FontName == "Calibri" && p.FontSize == 14 && !p.SourceSettings.ContainsKey("FontName")) { p.FontName = "Tahoma"; p.FontSize = 11; }
        if (p.RowHeight == 29 && !p.SourceSettings.ContainsKey("DefNodeHeight")) p.RowHeight = 22;
        if (catalog.FeatureMigrationVersion < 7)
        {
            // Delphi Font.Size is in points; WPF FontSize is in 1/96-inch units.
            if (double.TryParse(p.SourceSettings.GetValueOrDefault("FontSize"), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out double points) && Math.Abs(p.FontSize - points) < .001)
                p.FontSize = Math.Clamp(points * 96 / 72, 10, 38);
            p.ListTooltips = p.SourceSettings.GetValueOrDefault("ShowToolTip", "1") is "1" or "true";
            p.ShowColumnHeaders = p.SourceSettings.GetValueOrDefault("ShowColumns", "1") is "1" or "true";
            if (p.AfterLaunchAction == "Ничего не делать")
                p.AfterLaunchAction = p.MinimizeAfterLaunch ? "Свернуть в панель задач" : p.SourceSettings.GetValueOrDefault("ActionAfterRun") switch { "0" => "Свернуть в панель задач", "1" => "Свернуть в трей", "2" => "Закрыть ВанБаз", _ => "Ничего не делать" };
        }
        ConfigurationService.ImportExistingSources(catalog);
        foreach (var entry in catalog.Bases)
            entry.EnterprisePasswordMissing = entry.DesignerPasswordMissing = entry.RepositoryPasswordMissing = false;
        foreach (string key in catalog.Settings.SourceSettings.Keys.Where(k => k.StartsWith("Snegopat", StringComparison.OrdinalIgnoreCase)).ToArray())
            catalog.Settings.SourceSettings.Remove(key);
        if (catalog.FeatureMigrationVersion < 4 && int.TryParse(catalog.Settings.SourceSettings.GetValueOrDefault("SelectionBlendFactor"), out int intensity))
            catalog.Settings.SelectionIntensity = Math.Clamp(intensity, 0, 255);
        if (catalog.Migration is { } migration)
        {
            migration.Notes.RemoveAll(n => n.Contains("Прокси обновлений не перенесены"));
            migration.Notes.RemoveAll(n => n.Contains("Пароли") || n.Contains("парол") || n.Contains("Темы .vsf"));
            migration.Notes.Add("Перенос паролей исключён по выбору пользователя. Палитры .vsf доступны через раздел «Темы».");
        }
        ImportPrivacy.CleanImportedFields(catalog);
        catalog.FeatureMigrationVersion = CurrentVersion;
        return true;
    }

}
