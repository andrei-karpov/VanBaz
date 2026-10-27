using System.IO;
using System.Text;
using System.Windows;

namespace VanBaz;

public static class PortableCopy
{
    public static void Create(string application, string destination, Catalog? catalog)
    {
        string target = Path.GetFullPath(destination);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Выберите пустой каталог для переносимой копии.");
        string[] files = PortableDistribution.Files(application);
        if (files.Any(name => !File.Exists(Path.Combine(application, name)))) throw new IOException("Не найден полный комплект файлов ВанБаз.");
        Directory.CreateDirectory(target);
        foreach (string name in files) { string destinationFile = Path.Combine(target, name); Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!); File.Copy(Path.Combine(application,name), destinationFile, false); }
        if (catalog != null)
        {
            // A portable catalog must not retain synchronization baselines for the original account.
            var copy = catalog.Copy(); copy.ListSync.Clear();
            new CatalogStore(Path.Combine(target,"Data")).Save(copy);
        }
        string runtime = PortableDistribution.IncludesRuntime(application) ? ".NET уже включён в сборку." : "Требуется .NET Desktop Runtime 8.";
        File.WriteAllText(Path.Combine(target,"О запуске.txt"), "ВанБаз " + typeof(App).Assembly.GetName().Version?.ToString(3) + "\r\nЗапуск: VanBaz.exe\r\n" + runtime + "\r\nКаталог Data хранится рядом с приложением. Подключения к информационным базам не копируют сами базы.\r\nПароли защищены Windows и доступны только исходной учётной записи.\r\nПри запуске читаются штатные списки текущего пользователя. Локальные записи перенесённого каталога сохраняются; проверяйте пути на другом компьютере.\r\n", new UTF8Encoding(false));
    }
}
public partial class MainWindow
{
    private bool ShowPortableUpdate()
    {
        if (!PortableDistribution.IncludesRuntime(AppContext.BaseDirectory)) return false;
        var form = new FormDialog("Обновление ВанБаз", 640, 330) { Owner = this };
        form.Section("Портативная версия");
        form.Note("Скачайте ZIP из раздела Releases. Закройте ВанБаз и распакуйте архив в папку программы, заменив её файлы. Папку Data оставьте на месте: в ней хранятся ваши базы и настройки.");
        form.ActionButton("Открыть Releases на GitHub", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/andrei-karpov/VanBaz/releases/latest") { UseShellExecute = true }));
        form.SetAcceptText("Закрыть"); form.ShowDialog(); return true;
    }
    private void PortableCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        var form = new FormDialog("Переносимая копия ВанБаз", 660, 370) { Owner = this }; form.Section("Размещение");
        form.BrowseText("folder", "Пустой каталог", "", true); form.Check("catalog", "Включить текущий список и личные настройки", true);
        form.Note("Создаётся отдельная копия программы. Файлы информационных баз не переносятся. " + (PortableDistribution.IncludesRuntime(AppContext.BaseDirectory) ? ".NET включён в копию. " : "Для запуска нужен .NET Desktop Runtime 8. ") + "Сохранённые пароли привязаны к текущей учётной записи Windows.");
        form.SetAcceptText("Создать копию"); form.Validate = () => Path.IsPathFullyQualified(form.Value("folder")) || form.SetError("Укажите полный путь каталога.");
        if (form.ShowDialog() != true || isolatedSession) return;
        try { PortableCopy.Create(AppContext.BaseDirectory, form.Value("folder"), form.Flag("catalog") ? catalog : null); StatusText.Text = "Переносимая копия создана: " + form.Value("folder"); }
        catch (Exception ex) { Error("Переносимая копия", ex); }
    }
}
