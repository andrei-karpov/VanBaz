using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    public static readonly DependencyProperty ListTooltipsProperty = DependencyProperty.RegisterAttached("ListTooltips", typeof(bool), typeof(MainWindow), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty CellHighlightProperty = DependencyProperty.RegisterAttached("CellHighlight", typeof(bool), typeof(MainWindow), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    private Point dragStart;
    private BaseEntry? dragEntry;
    private readonly OperationSet operations = new();
    private string SelectedGroupPath => (BasesGrid.SelectedItem as ListRow) is { IsGroup: true, GroupKey: var key } && key.StartsWith("group:") ? key[6..] : Selected?.Group ?? "";
    private static string ParentGroup(string path) => path.LastIndexOf('/') is var slash && slash >= 0 ? path[..slash] : "";
    private static bool InGroup(string path, string parent) => path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(parent + "/", StringComparison.OrdinalIgnoreCase);
    private static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    private void ApplyPreferences(bool window = false)
    {
        var p = catalog.Settings;
        LegacyTab.Visibility = LegacyListMenu.Visibility = Visible(p.EnableLegacy77);
        if (!p.EnableLegacy77 && LegacyTab.IsChecked == true) BasesTab.IsChecked = true;
        Title = "ВанБаз — " + InterfaceMode;
        SessionGroup.Visibility = Visible(InterfaceMode == "Разработчик");
        BasesGrid.FontFamily = new FontFamily(p.FontName); BasesGrid.FontSize = Math.Clamp(p.FontSize, 10, 38); BasesGrid.RowHeight = Math.Max(p.RowHeight, p.FontSize + 6);
        HistoryGrid.FontFamily = BasesGrid.FontFamily; HistoryGrid.FontSize = BasesGrid.FontSize; HistoryGrid.RowHeight = BasesGrid.RowHeight;
        RowLaunchVisible = p.ShowLaunchButtonInRow; RowActionsColumn.Width = new DataGridLength(p.ShowLaunchButtonInRow ? 92 : 51);
        ApplyColumnWidths();
        PlatformColumn.Visibility = Visible(p.ShowPlatform); DateColumn.Visibility = Visible(p.ShowDate); SizeColumn.Visibility = Visible(p.ShowSize);
        HistoryTab.Visibility = Visible(p.ShowHistory); StarterPanel.Visibility = Visible(p.ShowStarter);
        BasesGrid.HeadersVisibility = p.ShowColumnHeaders ? DataGridHeadersVisibility.Column : DataGridHeadersVisibility.None;
        ToolTipService.SetIsEnabled(BasesGrid, p.ListTooltips);
        BasesGrid.SetValue(ListTooltipsProperty, p.ListTooltips); BasesGrid.SetValue(CellHighlightProperty, p.CellHighlight);
        ThemeService.Apply(p); RefreshThemeMenu(); BuildRows();
        UpdateSelection();
        if (!platformInventory.Matches(p)) _ = RefreshPlatformsAsync();
        if (HistoryTab.IsChecked == true && !p.ShowHistory) BasesTab.IsChecked = true;
        if (window)
        {
            Width = double.IsFinite(p.Width) ? Math.Max(MinWidth, p.Width) : 735; Height = double.IsFinite(p.Height) ? Math.Max(MinHeight, p.Height) : 630;

        }
    }
    private void Migrate_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        var form = new FormDialog("Перенос из StartManager", 780, 480) { Owner = this };
        form.Section("Источники"); form.BrowseText("settings", "Папка SMSettings", StartManagerMigration.DefaultSettings, true);
        form.BrowseText("list", "Список баз", StartManagerMigration.DefaultList, false, "Список 1С|*.v8i");
        form.Note("Будут добавлены записи с новыми ID. Существующие записи ВанБаз сохранят свои свойства. Оформление будет взято из StartManager.");
        if (form.ShowDialog() != true) return;
        try
        {
            var prepared = StartManagerMigration.Prepare(catalog, form.Value("settings"), form.Value("list"));
            if (MessageBox.Show(prepared.Migration!.Summary + "\n\nПрименить перенос?", "Предварительный результат", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes) return;
            if (File.Exists(store.FilePath)) store.Snapshot();
            if (Commit(() => catalog = prepared)) { ApplyPreferences(true); SaveMigrationReport(); _ = RefreshMetadata(); }
        }
        catch (Exception ex) { Error("Перенос из StartManager", ex); }
    }
    private void SaveMigrationReport()
    {
        if (catalog.Migration == null) return;
        File.WriteAllText(Path.Combine(store.Folder, "migration-report.txt"), $"Перенос StartManager → ВанБаз\nДата: {catalog.Migration.Time:dd.MM.yyyy HH:mm}\nНастройки: {catalog.Migration.SettingsFolder}\nСписок: {catalog.Migration.ListFile}\n\n{catalog.Migration.Summary}");
    }
    private void MigrationReport_Click(object sender, RoutedEventArgs e)
    {
        FormDialog.ShowText(this, "Отчёт о переносе", catalog.Migration?.Summary ?? "Перенос из StartManager ещё не выполнялся.");
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Экспорт списка баз", FileName = "VanBaz.v8i", Filter = "Список 1С|*.v8i" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(StartManagerMigration.DefaultList), StringComparison.OrdinalIgnoreCase) || catalog.Migration?.ListFile.Equals(Path.GetFullPath(dialog.FileName), StringComparison.OrdinalIgnoreCase) == true)
                throw new InvalidOperationException("Для экспорта выберите отдельный файл. Изменения в штатный список записываются при редактировании; массовые различия можно перенести через согласование.");
            V8iImporter.Export(catalog, dialog.FileName); StatusText.Text = "Список экспортирован: " + dialog.FileName;
        }
        catch (Exception ex) { Error("Экспорт списка", ex); }
    }
    private void Snapshot_Click(object sender, RoutedEventArgs e) => SaveNamedSnapshot(this);
    private void RestoreSnapshot_Click(object sender, RoutedEventArgs e) => SnapshotList_Click(sender, e);
    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        var form = GroupForm("Добавить группу", "Новая группа", SelectedGroupPath);
        if (form.ShowDialog() != true) return;
        string path = GroupPath(form);
        if (catalog.Groups.Any(g => g.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Такая группа уже есть."); return; }
        Commit(() => catalog.Groups.Add(new() { Path = path, Order = catalog.Groups.Count * 256 }));
    }
    private FormDialog GroupForm(string title, string name, string parent)
    {
        var form = new FormDialog(title, 550, 310) { Owner = this }; form.Section("Группа");
        form.Text("name", "Наименование", name); form.Choice("parent", "Каталог списка", parent, new[] { "" }.Concat(catalog.Groups.Select(g => g.Path)).Distinct().Order(), true);
        form.Note("Пустой каталог — корень списка информационных баз.");
        form.Validate = () => form.Value("name").Length > 0 && form.Value("name").IndexOfAny(new[] { '/', '\\', '\r', '\n', ']' }) < 0 && ValidGroup(GroupPath(form)) || form.SetError("Введите имя группы без разделителей пути.");
        return form;
    }
    private static string GroupPath(FormDialog form) => (form.Value("parent").Trim('/', '\\').Replace('\\', '/') + "/" + form.Value("name")).Trim('/');

    private static bool ValidGroup(string path)
    {
        if (path.Length > 0 && !path.Contains("//") && path.IndexOfAny(new[] { '\r', '\n', ']' }) < 0) return true;
        MessageBox.Show("Укажите имя группы; вложенные группы разделяются знаком /."); return false;
    }
    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        if ((BasesGrid.SelectedItem as ListRow) is not { IsGroup: true } || SelectedGroupPath.Length == 0) return;
        string old = SelectedGroupPath;
        var source = catalog.Groups.FirstOrDefault(g => g.Path.Equals(old, StringComparison.OrdinalIgnoreCase));
        if (source != null && officialLists?.IsCommon(source.Id) == true) { StatusText.Text = "Общую группу изменяют в исходном списке."; return; }
        var form = GroupForm("Свойства группы", old.Split('/').Last(), ParentGroup(old));
        if (form.ShowDialog() != true) return; string name = GroupPath(form);
        if (!ValidGroup(name) || name == old) return;
        if (InGroup(name, old) || catalog.Groups.Any(g => g.Path.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Этот путь занят или вложен в исходную группу."); return; }
        Commit(() =>
        {
            foreach (var b in catalog.Bases.Where(b => InGroup(b.Group, old))) b.Group = name + b.Group[old.Length..];
            foreach (var g in catalog.Groups.Where(g => InGroup(g.Path, old))) g.Path = name + g.Path[old.Length..];
        });
        collapsed.Clear(); BuildRows();
    }
    private void DeleteGroup()
    {
        string group = SelectedGroupPath;
        if (group.Length == 0) return;
        if (MessageBox.Show("Удалить группу «" + group + "»?\nЕё базы и вложенные группы будут перемещены на уровень выше.", "Удаление группы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        string parent = ParentGroup(group);
        string Move(string old) => (parent + old[group.Length..]).Trim('/');
        Commit(() =>
        {
            foreach (var b in catalog.Bases.Where(b => InGroup(b.Group, group))) b.Group = Move(b.Group);
            catalog.Groups.RemoveAll(g => g.Path == group);
            foreach (var g in catalog.Groups.Where(g => InGroup(g.Path, group))) g.Path = Move(g.Path);
            catalog.Groups = catalog.Groups.GroupBy(g => g.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        });
    }
    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        if (Selected is not { } selected) return;
        var b = selected.Copy(); b.Id = Guid.NewGuid().ToString(); b.Name += " — копия"; b.LastLaunch = null;
        var editor = new BaseEditor(b, platforms, catalog.Groups.Select(g => g.Path).ToList(), catalog.Configurations, enableLegacy77: catalog.Settings.EnableLegacy77, examples: CreateParameterExamples()) { Owner = this };
        if (editor.ShowDialog() == true && Commit(() => catalog.Bases.Add(editor.Result))) BuildRows(b.Id);
    }
    private void Grid_DragStart(object sender, MouseButtonEventArgs e) { dragStart = e.GetPosition(BasesGrid); dragEntry = CanEdit && !InButton(e.OriginalSource) ? (FindRow(e.OriginalSource)?.Item as ListRow)?.Entry : null; }
    private void Grid_DragMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragEntry == null) return;
        var point = e.GetPosition(BasesGrid);
        if (Math.Abs(point.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        string id = dragEntry.Id; dragEntry = null; dragging = true; try { DragDrop.DoDragDrop(BasesGrid, new DataObject("VanBaz.Base", id), DragDropEffects.Move); } finally { dragging = false; }
    }
    private void Grid_Drop(object sender, DragEventArgs e)
    {
        if (!AllowTools()) return;
        if (e.Data.GetData("VanBaz.Base") is not string id || FindRow(e.OriginalSource)?.Item is not ListRow target) return;
        var entry = catalog.Bases.FirstOrDefault(b => b.Id == id); if (entry == null) return;
        string group = target.Entry?.Group ?? (target.GroupKey.StartsWith("group:") ? target.GroupKey[6..] : "");
        if (target.GroupKey == "@favorites") Commit(() => entry.Favorite = true);
        else Commit(() =>
        {
            var siblings = SortEntries(catalog.Bases.Where(b => b.Group == group && b.Id != id)).ToList();
            int index = target.Entry == null ? siblings.Count : siblings.FindIndex(b => b.Id == target.Entry.Id);
            siblings.Insert(index < 0 ? siblings.Count : index, entry); entry.Group = group;
            for (int i = 0; i < siblings.Count; i++) siblings[i].Order = i * 256;
            catalog.Settings.Alphabetical = false; catalog.Settings.SortColumn = "";
        });
        UpdateSortArrows();
        BuildRows(id); e.Handled = true;
    }
    private void BaseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (locked || Selected is not { Kind: "File" } b) return;
        try { if (!Directory.Exists(b.Location)) throw new DirectoryNotFoundException("Каталог базы не найден."); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { b.Location }, UseShellExecute = false }); }
        catch (Exception ex) { Error("Папка базы", ex); }
    }
    private async void CopyConnection_Click(object sender, RoutedEventArgs e) { if (Selected is { } b && !locked) await CopyTextAsync(b.ConnectionDisplay); }
    private void Starter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
                .Select(p => Path.Combine(p, "1cv8", "common", "1cestart.exe")).FirstOrDefault(File.Exists);
            if (file == null) throw new FileNotFoundException("Штатный 1С:Стартер не найден.");
            StartStarter(file);
        }
        catch (Exception ex) { Error("1С:Стартер", ex); }
    }
    private FormDialog CreateCommandDialog(CommandPreset? command)
    {
        var dialog = new FormDialog("Свойства команды", 700, 490) { Owner = this }; dialog.Section("Команда");
        dialog.Text("name", "Наименование", command?.Name ?? "Новая команда");
        var type = dialog.Choice("kind", "Тип", command?.Kind ?? "Программа", new[] { "Программа", "Каталог", "Документ", "Скрипт", "Web-адрес", "Параметры 1С" });
        dialog.Text("target", "Команда / адрес", command?.Target ?? "");
        dialog.ActionButton("Выбрать команду…", () =>
        {
            if (dialog.Value("kind") == "Каталог") { var f = new OpenFolderDialog(); if (f.ShowDialog(dialog) == true) SetTarget(f.FolderName); }
            else { var f = new OpenFileDialog { Filter = dialog.Value("kind") == "Программа" ? "Программа|*.exe" : "Все файлы|*.*" }; if (f.ShowDialog(dialog) == true) SetTarget(f.FileName); }
        });
        dialog.Text("args", "Параметры", command?.Arguments ?? ""); dialog.BrowseText("work", "Рабочая папка", command?.WorkingDirectory ?? "", true);
        void SetTarget(string value) => dialog.SetText("target", value);
        void Refresh() { bool preset = dialog.Value("kind") == "Параметры 1С"; dialog.SetEnabled("target", !preset); dialog.SetEnabled("work", !preset && dialog.Value("kind") != "Web-адрес"); dialog.SetEnabled("args", dialog.Value("kind") is not ("Web-адрес" or "Каталог")); }
        type.SelectionChanged += (_, _) => Refresh(); Refresh();
        CommandPreset Read() => new() { Name = dialog.Value("name"), Kind = dialog.Value("kind"), Target = dialog.Value("target"), Arguments = dialog.Value("args"), WorkingDirectory = dialog.Value("work") };
        dialog.Validate = () => { if (dialog.Value("name").Length == 0) return dialog.SetError("Введите название."); if (dialog.Value("kind") != "Параметры 1С") StandaloneCommand.Build(Read()); return true; };
        dialog.ApplyChanges = () => Commit(() => { var value = Read(); int index = command == null ? -1 : catalog.Commands.IndexOf(command); if (index < 0) catalog.Commands.Add(value); else catalog.Commands[index] = value; command = value; });
        return dialog;
    }

    private void FindBases_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        var dialog = new BaseSearchWindow(catalog, SelectedGroupPath) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            int added = 0;
            if (Commit(() => added = V8iImporter.Merge(catalog, dialog.Entries)))
            { StatusText.Text = "Добавлено баз: " + added; _ = RefreshMetadata(); }
        }
    }
}
