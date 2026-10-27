using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private void LegacyMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!catalog.Settings.EnableLegacy77) return;
        var menu = NewMenu();
        menu.Items.Add(Item("Прочитать список Windows…", Import77_Click, "edit"));
        menu.Items.Add(Item("Загрузить список из REG…", Import77File_Click, "edit"));
        menu.Items.Add(Item("Выгрузить список в REG…", Export77File_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Сохранить порядок элементов", SaveListOrder_Click, "edit"));
        menu.Items.Add(Item("Сортировать по возрастанию", (_, _) => SetListSort(false), "edit"));
        menu.Items.Add(Item("Сортировать по убыванию", (_, _) => SetListSort(true), "edit"));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Очистить CDX-индексы…", LegacyIndexes_Click, "legacy"));
        OpenMenu((FrameworkElement)sender, menu);
    }
    private void Import77File_Click(object sender, RoutedEventArgs e)
    {
        if (!catalog.Settings.EnableLegacy77 || !AllowTools()) return;
        var dialog = new OpenFileDialog { Title = "Список 1С 7.7", Filter = "Экспорт списка Windows|*.reg" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var entries = LegacyListFile.Read(dialog.FileName);
            var f = new FormDialog("Импорт списка 7.7", 720, 430) { Owner = this }; f.Section("Список");
            f.Note("Найдено записей: " + entries.Count + ". Будут добавлены только новые подключения. REG читается как текст; реестр Windows не изменяется.");
            f.BrowseText("exe", "Платформа 7.7", catalog.Settings.SourceSettings.GetValueOrDefault("V77App", ""), false, "Платформа 7.7|1cv7*.exe");
            f.Note("Путь к платформе можно указать позже в свойствах базы.");
            if (f.ShowDialog() != true) return;
            foreach (var entry in entries) entry.Executable = f.Value("exe");
            store.Snapshot(); int count = 0;
            if (Commit(() => { foreach (var entry in entries) if (!catalog.Bases.Any(b => b.Id == entry.Id || b.Version == "7.7" && b.Location.TrimEnd('\\').Equals(entry.Location, StringComparison.OrdinalIgnoreCase))) { catalog.Bases.Add(entry); count++; } }))
            { LegacyTab.IsChecked = true; StatusText.Text = "Добавлено баз 7.7: " + count; }
        }
        catch (Exception ex) { Error("Импорт списка 7.7", ex); }
    }
    private void Export77File_Click(object sender, RoutedEventArgs e)
    {
        if (!catalog.Settings.EnableLegacy77 || locked || operations.Count > 0) return;
        var dialog = new SaveFileDialog { Title = "Выгрузить список 7.7", FileName = "VanBaz-77.reg", Filter = "Список Windows|*.reg" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, LegacyListFile.Export(catalog.Bases), Encoding.Unicode); StatusText.Text = "Список 7.7 выгружен. Пароли не включены."; }
        catch (Exception ex) { Error("Экспорт списка 7.7", ex); }
    }
    private void SetListSort(bool descending)
    {
        if (!CanEdit || !IsBaseView) return;
        Commit(() => { catalog.Settings.SortColumn = "Name"; catalog.Settings.SortDescending = descending; }); UpdateSortArrows();
    }
    private void SaveListOrder_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !IsBaseView) return;
        // All records of this platform participate, including collapsed/filtered groups.
        var ordered = SortEntries(catalog.Bases.Where(b => (b.Version == "7.7") == (LegacyTab.IsChecked == true))).ToList();
        if (Commit(() => { for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i * 256; catalog.Settings.SortColumn = ""; catalog.Settings.SortDescending = false; catalog.Settings.Alphabetical = false; }))
        { UpdateSortArrows(); StatusText.Text = "Порядок элементов сохранён."; }
    }
    private void RenameBase_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !IsBaseView || Selected is not { } b) return;
        string? name = FormDialog.Prompt(this, "Переименовать базу", "Наименование", b.Name);
        if (name != null) Commit(() => b.Name = name);
    }
    private void LegacyIndexes_Click(object sender, RoutedEventArgs e)
    {
        if (!catalog.Settings.EnableLegacy77 || !AllowTools() || Selected is not { Version: "7.7", Kind: "File" } entry || isolatedSession) return;
        try
        {
            var files = LegacyIndexService.Files(entry); var archives = LegacyIndexService.Archives(entry);
            var f = new FormDialog("Индексы 7.7 · " + entry.Name, 780, 500) { Owner = this }; f.Section("CDX-индексы");
            f.Note("Каталог: " + entry.Location + "\nИндексов в корне: " + files.Length + ". Закройте сеансы 1С. CDX перемещаются в карантин внутри этой базы; MD, DBF и данные не изменяются. При следующем запуске 7.7 может построить индексы заново.");
            f.Choice("action", "Действие", "Очистить индексы", new[] { "Очистить индексы", "Восстановить из карантина" });
            var labels = archives.Select((a, i) => (Label: $"{i + 1}. {a.Time:dd.MM.yyyy HH:mm:ss} · {a.Count} файлов", Archive: a)).ToList();
            f.Choice("archive", "Сохранённый набор", labels.FirstOrDefault().Label ?? "", labels.Select(a => a.Label));
            var preview = f.Text("files", "Текущие файлы", string.Join("\n", files.Select(Path.GetFileName)), true); preview.IsReadOnly = true; preview.Height = 95;
            f.Note("Восстановление откажет, если индексы уже созданы заново. Старые и новые наборы не смешиваются.");
            f.SetAcceptText("Выполнить");
            f.Validate = () => f.Value("action") == "Очистить индексы" ? files.Length > 0 || f.SetError("Нет индексов для очистки.") : labels.Any(a => a.Label == f.Value("archive")) || f.SetError("Выберите сохранённый набор.");
            if (f.ShowDialog() != true) return;
            if (MessageBox.Show(this, f.Value("action") + "?\n\n" + entry.Name + "\n" + entry.Location, "Индексы 7.7", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            if (f.Value("action") == "Очистить индексы") FormDialog.ShowText(this, "Индексы перемещены в карантин", LegacyIndexService.Quarantine(entry));
            else { LegacyIndexService.Restore(entry, labels.First(a => a.Label == f.Value("archive")).Archive.Folder); StatusText.Text = "CDX-индексы восстановлены."; }
        }
        catch (Exception ex) { Error("Индексы 7.7", ex); }
    }
}
