using System.Diagnostics;
using System.IO;
using System.Windows;

namespace VanBaz;

public static class BaseCreation
{
    public static ProcessStartInfo Build(BaseEntry entry, string template, string locale, string log, string result, IEnumerable<PlatformInfo> platforms)
    {
        if (entry.Kind != "File" || entry.Version == "7.7") throw new ArgumentException("Создание доступно для файловых баз 8.x.");
        if (!Path.IsPathFullyQualified(entry.Location)) throw new ArgumentException("Укажите полный путь новой базы.");
        if (Directory.Exists(entry.Location) && Directory.EnumerateFileSystemEntries(entry.Location).Any()) throw new IOException("Для новой базы нужен пустой каталог.");
        if (template.Length > 0 && (!File.Exists(template) || !Path.GetExtension(template).Equals(".cf", StringComparison.OrdinalIgnoreCase))) throw new IOException("Выберите существующий шаблон конфигурации .cf.");
        if (locale.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(locale, @"^[A-Za-z]{2,3}(?:[_-][A-Za-z]{2})?$")) throw new ArgumentException("Код локализации: например ru_RU.");
        var copy = entry.Copy(); copy.ExtraArguments = ""; copy.ExternalProcessor = ""; copy.UseRepository = false; copy.DesignerLogin = false; copy.Advanced = new();
        var probe = LaunchService.Build(copy, true, "Толстый клиент", platforms);
        var info = new ProcessStartInfo(probe.FileName) { UseShellExecute = false };
        info.ArgumentList.Add("CREATEINFOBASE"); info.ArgumentList.Add(entry.ConnectionDisplay);
        if (template.Length > 0) { info.ArgumentList.Add("/UseTemplate"); info.ArgumentList.Add(Path.GetFullPath(template)); }
        if (locale.Length > 0) info.ArgumentList.Add("/VL" + locale);
        info.ArgumentList.Add("/Out"); info.ArgumentList.Add(log); info.ArgumentList.Add("/DumpResult"); info.ArgumentList.Add(result);
        return info;
    }
}
public partial class MainWindow
{
    private async void CreateBase_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !IsBaseView) return;
        var form = new FormDialog("Создание новой информационной базы", 690, 480) { Owner = this }; form.Section("Новая база");
        form.Text("name", "Наименование", "Новая база"); form.BrowseText("folder", "Новый пустой каталог", catalog.Settings.DefaultFolder, true);
        form.Choice("group", "Группа", SelectedGroupPath, new[] { "" }.Concat(catalog.Groups.Select(g => g.Path)), true);
        form.Choice("version", "Платформа", catalog.Settings.DefaultVersion, platforms.Where(p => p.ThickClient).Select(p => p.Version).Distinct(), true);
        form.Choice("arch", "Разрядность", "Авто", new[] { "Авто", "x64", "x86" });
        form.BrowseText("template", "Шаблон конфигурации", "", false, "Шаблон конфигурации|*.cf"); form.Text("locale", "Локализация", "ru_RU");
        form.Note("Без шаблона создаётся пустая база. Запись появится в штатном списке после успешного создания платформой 1С."); form.SetAcceptText("Создать");
        BaseEntry Read() => new() { Name = form.Value("name"), Location = form.Value("folder"), Group = form.Value("group").Replace('\\', '/').Trim('/'), Version = form.Value("version"), Architecture = form.Value("arch"), DoubleClickAction = catalog.Settings.DefaultDoubleClickAction };
        form.Validate = () => { if (form.Value("name").Length == 0 || form.Value("name").IndexOfAny(new[] { '\r', '\n', ']' }) >= 0) return form.SetError("Укажите корректное наименование."); BaseCreation.Build(Read(), form.Value("template"), form.Value("locale"), "preview.log", "preview.result", platforms); return true; };
        if (form.ShowDialog() != true || isolatedSession) return;
        var entry = Read(); string operation = "create:" + entry.Id; operations.Add(operation);
        try
        {
            string logs = Path.Combine(store.Folder, "Logs"); Directory.CreateDirectory(logs);
            string stem = Path.Combine(logs, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-create");
            var info = BaseCreation.Build(entry, form.Value("template"), form.Value("locale"), stem + ".log", stem + ".result", platforms);
            StatusText.Text = "Создание базы: " + entry.Name;
            using var process = LaunchService.Start(info); await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !File.Exists(stem + ".result") || File.ReadAllText(stem + ".result").Trim() != "0" || !File.Exists(Path.Combine(entry.Location, "1Cv8.1CD")))
                throw new IOException("Платформа не подтвердила создание базы. Журнал: " + stem + ".log. Файлы не удалялись.");
            if (Commit(() => catalog.Bases.Add(entry))) { SelectBase(entry.Id); StatusText.Text = "База создана и зарегистрирована: " + entry.Name; }
        }
        catch (Exception ex) { Error("Создание информационной базы", ex); }
        finally { operations.Remove(operation); }
    }
}
