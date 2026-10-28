using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VanBaz;

public static class PortableCopy
{
    public static long EstimateBytes(string application, Catalog? catalog)
    {
        long total = PortableDistribution.Files(application).Sum(name => new FileInfo(Path.Combine(application, name)).Length) + 4096;
        if (catalog != null) total += System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalog, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).LongLength * 2 + 4096;
        return total;
    }
    public static void Create(string application, string destination, Catalog? catalog)
    {
        string target = Path.GetFullPath(destination);
        CacheService.EnsureOrdinaryPath(target);
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
        string runtime = PortableDistribution.IncludesRuntime(application) ? ".NET уже включён в сборку." : "Требуется .NET Desktop Runtime 8 для Windows x64: https://dotnet.microsoft.com/ru-ru/download/dotnet/8.0";
        File.WriteAllText(Path.Combine(target,"О запуске.txt"), "ВанБаз " + typeof(App).Assembly.GetName().Version?.ToString(3) + "\r\nЗапуск: VanBaz.exe\r\n" + runtime + "\r\nКаталог Data хранится рядом с приложением. Подключения к информационным базам не копируют сами базы.\r\nПароли защищены Windows и доступны только исходной учётной записи.\r\nПри запуске читаются штатные списки текущего пользователя. Локальные записи перенесённого каталога сохраняются; проверяйте пути на другом компьютере.\r\n", new UTF8Encoding(false));
    }
}
public sealed record PortableDrive(string Root, string Kind, string Label, long? FreeBytes, bool Ready)
{
    public string FreeSpace => FreeBytes is { } bytes ? (bytes / 1073741824d).ToString("N1") + " ГБ" : "Недоступно";
    public static IReadOnlyList<PortableDrive> Find()
    {
        var result = new List<PortableDrive>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Removable or DriveType.Fixed))
        {
            string kind = drive.DriveType == DriveType.Removable ? "Съёмный" : "Локальный";
            try { result.Add(drive.IsReady ? new(drive.Name, kind, drive.VolumeLabel, drive.AvailableFreeSpace, true) : new(drive.Name, kind, "Носитель не готов", null, false)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result.Add(new(drive.Name, kind, "Недоступен", null, false)); }
        }
        return result;
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
        var form = CreatePortableCopyDialog();
        if (form.ShowDialog() != true || isolatedSession) return;
        try { PortableCopy.Create(AppContext.BaseDirectory, form.Value("folder"), form.Flag("catalog") ? catalog : null); StatusText.Text = "Переносимая копия создана: " + form.Value("folder"); }
        catch (Exception ex) { Error("Переносимая копия", ex); }
    }
    private FormDialog CreatePortableCopyDialog(Func<IReadOnlyList<PortableDrive>>? readDrives = null)
    {
        readDrives ??= PortableDrive.Find;
        var form = new FormDialog("Переносимая копия ВанБаз", 740, 580) { Owner = this }; form.Section("Размещение");
        form.Note("Выберите носитель и папку для отдельной копии программы. Можно указать свой путь ниже.");
        var drives = new DataGrid { Name = "PortableDrives", AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column, Height = 135 };
        drives.Columns.Add(new DataGridTextColumn { Header = "Диск", Binding = new Binding(nameof(PortableDrive.Root)), Width = new DataGridLength(65) });
        drives.Columns.Add(new DataGridTextColumn { Header = "Тип", Binding = new Binding(nameof(PortableDrive.Kind)), Width = new DataGridLength(90) });
        drives.Columns.Add(new DataGridTextColumn { Header = "Название", Binding = new Binding(nameof(PortableDrive.Label)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        drives.Columns.Add(new DataGridTextColumn { Header = "Свободно", Binding = new Binding(nameof(PortableDrive.FreeSpace)), Width = new DataGridLength(105) });
        form.Element(drives);
        var buttons = new WrapPanel(); var refresh = new Button { Name = "RefreshPortableDrives", Content = UiIcons.Label("refresh", "Обновить"), ToolTip = "Заново прочитать подключённые диски и свободное место" };
        var select = new Button { Name = "UsePortableDrive", Content = "Выбрать диск", IsEnabled = false, ToolTip = "Создать папку VanBaz на выбранном диске" };
        buttons.Children.Add(refresh); buttons.Children.Add(select); form.Element(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 6) }; status.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted"); form.Element(status);
        form.BrowseText("folder", "Пустой каталог", "", true); form.Check("catalog", "Включить текущий список и личные настройки", true);
        form.Note("Создаётся отдельная копия программы. Файлы информационных баз не переносятся. " + (PortableDistribution.IncludesRuntime(AppContext.BaseDirectory) ? ".NET включён в копию. " : "Для запуска нужен .NET Desktop Runtime 8. ") + "Сохранённые пароли привязаны к текущей учётной записи Windows.");
        drives.SelectionChanged += (_, _) => { select.IsEnabled = drives.SelectedItem is PortableDrive { Ready: true }; ShowSpace(); };
        select.Click += (_, _) => { if (drives.SelectedItem is PortableDrive { Ready: true } drive) form.SetText("folder", Path.Combine(drive.Root, "VanBaz")); };
        refresh.Click += (_, _) => Refresh(); form.Loaded += (_, _) => Refresh();
        form.SetAcceptText("Создать копию"); form.Validate = () =>
        {
            string folder = form.Value("folder");
            if (!Path.IsPathFullyQualified(folder)) return form.SetError("Укажите полный путь каталога.");
            CacheService.EnsureOrdinaryPath(folder);
            if (File.Exists(folder) || Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) return form.SetError("Для копии нужна пустая папка. Существующие файлы не заменяются.");
            var drive = readDrives().FirstOrDefault(d => d.Root.Equals(Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase));
            if (drive is { Ready: false }) return form.SetError("Выбранный носитель не готов.");
            long required = PortableCopy.EstimateBytes(AppContext.BaseDirectory, form.Flag("catalog") ? catalog : null);
            if (drive?.FreeBytes is { } free && free < required) return form.SetError("На выбранном носителе недостаточно свободного места. Требуется примерно " + Size(required) + ".");
            return true;
        };
        return form;

        void Refresh()
        {
            try
            {
                string? previous = (drives.SelectedItem as PortableDrive)?.Root;
                drives.ItemsSource = readDrives(); drives.SelectedItem = drives.Items.Cast<PortableDrive>().FirstOrDefault(d => d.Root == previous);
                select.IsEnabled = drives.SelectedItem is PortableDrive { Ready: true };
                ShowSpace();
            }
            catch (Exception ex) { drives.ItemsSource = Array.Empty<PortableDrive>(); select.IsEnabled = false; status.Text = ex.Message; }
        }
        void ShowSpace()
        {
            string required;
            try { required = Size(PortableCopy.EstimateBytes(AppContext.BaseDirectory, catalog)); }
            catch (Exception ex) { status.Text = "Не удалось оценить размер копии: " + ex.Message; return; }
            string free = drives.SelectedItem is PortableDrive drive ? " Свободно на выбранном диске: " + drive.FreeSpace + "." : " Выберите диск или укажите путь вручную.";
            status.Text = "Требуется примерно " + required + " с текущим каталогом." + free;
        }
        static string Size(long bytes) => (bytes / 1048576d).ToString("N1") + " МБ";
    }
}
