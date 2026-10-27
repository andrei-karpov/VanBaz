using System.IO;

namespace VanBaz;

public static class FeatureMigration
{
    public const int CurrentVersion = 7;
    public static bool Apply(Catalog catalog)
    {
        if (catalog.FeatureMigrationVersion >= CurrentVersion) return false;
        foreach (var settings in catalog.Bases.Select(b => b.SourceSettings).Append(catalog.Settings.SourceSettings))
            foreach (string key in settings.Keys.Where(k => k.StartsWith("Backup", StringComparison.OrdinalIgnoreCase)).ToArray()) settings.Remove(key);
        var p = catalog.Settings;
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
        catalog.FeatureMigrationVersion = CurrentVersion;
        return true;
    }

}
