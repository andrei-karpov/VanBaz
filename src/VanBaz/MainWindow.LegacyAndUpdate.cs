using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private void Import77_Click(object sender, RoutedEventArgs e)
    {
        if (!catalog.Settings.EnableLegacy77 || !AllowTools()) return;
        try
        {
            var entries = Legacy77Service.ReadRegistered();
            if (entries.Count == 0) { MessageBox.Show("В списке 1С 7.7 текущего пользователя записей нет. Базу можно добавить вручную, выбрав версию 7.7 и 1cv7*.exe."); return; }
            var f = new FormDialog("Импорт списка 1С 7.7", 720, 420) { Owner = this }; f.Section("Перенос");
            f.Note("Записей в реестре текущего пользователя: " + entries.Count + ". Исходный список не изменяется. Пароли не переносятся.");
            f.BrowseText("exe", "Исполняемый файл 7.7", catalog.Settings.SourceSettings.GetValueOrDefault("V77App", ""), false, "Платформа 7.7|1cv7*.exe");
            f.Validate = () => File.Exists(f.Value("exe")) && Path.GetFileName(f.Value("exe")).StartsWith("1cv7", StringComparison.OrdinalIgnoreCase) || f.SetError("Выберите установленный исполняемый файл 7.7.");
            if (f.ShowDialog() != true) return;
            foreach (var entry in entries) entry.Executable = f.Value("exe");
            if (File.Exists(store.FilePath)) store.Snapshot();
            int added = 0;
            Commit(() => { foreach (var entry in entries.Where(b => !catalog.Bases.Any(existing => existing.Id == b.Id || existing.Version == "7.7" && existing.Location.TrimEnd('\\').Equals(b.Location, StringComparison.OrdinalIgnoreCase)))) { catalog.Bases.Add(entry); added++; } });
            StatusText.Text = "Добавлено баз 7.7: " + added;
        }
        catch (Exception ex) { Error("Импорт 7.7", ex); }
    }
    private async void UpdateApp_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        if (ShowPortableUpdate()) return;
        var f = new FormDialog("Обновить ВанБаз", 780, 470) { Owner = this }; f.Section("Пакет приложения");
        f.BrowseText("package", "Пакет ZIP", "", false, "Пакет ВанБаз|*.zip");
        f.Note("Подходит компактный ZIP из Releases или пакет package.ps1. Проверяются состав и версия сборки; при наличии манифеста — SHA-256. Для компактного ZIP контрольные суммы сохраняются после чтения. Используйте доверенный источник: эти проверки не удостоверяют автора.");
        f.Note("После подтверждения ВанБаз закроется, заменит четыре файла программы и откроется снова. Каталог данных сохраняется; прежние файлы программы остаются для отката. Проверка HTTPS-канала доступна в меню «Обновления ВанБаз».");
        f.SetAcceptText("Проверить пакет"); f.Validate = () => File.Exists(f.Value("package")) || f.SetError("Выберите ZIP-пакет.");
        if (f.ShowDialog() != true || isolatedSession) return;
        string packagePath = f.Value("package");
        operations.Add("@app-update");
        try
        {
            var update = await System.Threading.Tasks.Task.Run(() => AppUpdateService.Prepare(packagePath, Path.Combine(store.Folder, "Updates")));
            if (MessageBox.Show("Установить ВанБаз " + update.Version + "?\nПрограмма будет перезапущена.\nПроверенный пакет: " + update.Folder, "Обновление ВанБаз", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            store.Save(catalog); store.Snapshot();
            await AppUpdateService.StartInstaller(update, store.Folder, InterfaceMode, modeLocked);
            operations.Remove("@app-update");
            exitRequested = true; Close();
        }
        catch (Exception ex) { Error("Обновление ВанБаз", ex); }
        finally { operations.Remove("@app-update"); }
    }
}
