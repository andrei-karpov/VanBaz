using System.IO;
using System.Windows;

namespace VanBaz;

public partial class MainWindow
{
    private void ProfileFiles_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || isolatedSession) return;
        var c = new ProfileContext { BaseId = Selected is { } b && Guid.TryParse(b.Id, out var id) ? id.ToString() : "" };
        var f = new FormDialog("Выбор файлов профиля 1С", 900, 620) { Owner = this }; f.Section("Профиль Windows");
        f.BrowseText("roaming", "Каталог Roaming", c.Roaming, true); f.BrowseText("local", "Каталог Local", c.Local, true);
        f.Choice("edition", "Издание платформы", c.Edition, new[] { "1cv8", "1cv82" });
        f.Text("base", "GUID регистрации базы", c.BaseId); f.Text("user", "GUID пользователя 1С", "");
        f.Note("GUID пользователя 1С нельзя определить по имени Windows или логину. Укажите его явно для пользовательских PFL. Пустой GUID базы оставляет только общие файлы. Каталоги другого Windows-профиля выбираются без изменения прав.");
        ProfileContext Read() => new() { Roaming = f.Value("roaming"), Local = f.Value("local"), Edition = f.Value("edition"), BaseId = f.Value("base"), UserId = f.Value("user") };
        f.Validate = () => { ProfileArchive.List(Read()); return true; }; f.SetAcceptText("Показать файлы");
        if (f.ShowDialog() == true) new ProfileFilesWindow(Read(), Path.Combine(store.Folder, "ProfileRecovery")) { Owner = this }.ShowDialog();
    }
}
