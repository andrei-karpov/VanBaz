using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace VanBaz;

public partial class MainWindow
{
    private void EditListSources()
    {
        if (!AllowTools()) return;
        var sources = ListSourceService.Sources(catalog.Settings);
        var window = new Window { Owner = SettingsOwner, Title = "Списки информационных баз по платформам", Width = 850, Height = 480, MinWidth = 680, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        DialogBehavior.FitToScreen(window);
        var root = new DockPanel { Margin = new(10) }; window.Content = root;
        var note = new TextBlock { Text = "Это дополнительные файлы для импорта. Штатные источники 1С обнаруживаются автоматически. Новые записи импортируемых файлов будут зарегистрированы в пользовательском ibases.v8i. Авто — версия из файла; существующие ID сохраняют ваши правки.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var read = new CheckBox { Content = "Прочитать включённые списки после сохранения", IsChecked = true, Margin = new(3, 8, 0, 6) }; footer.Children.Add(read);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column, FontSize = catalog.Settings.FontSize, RowHeight = Math.Max(catalog.Settings.RowHeight, catalog.Settings.FontSize + 6) };
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Вкл.", Binding = new Binding("Enabled"), Width = 50 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Платформа", Binding = new Binding("Platform"), Width = 95 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Файл списка", Binding = new Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); root.Children.Add(grid);
        void Refresh(ListSource? select = null) { grid.ItemsSource = sources.ToList(); grid.SelectedItem = select ?? sources.FirstOrDefault(); }
        void Edit(ListSource? original)
        {
            var f = new FormDialog(original == null ? "Добавить список баз" : "Свойства списка баз", 740, 420) { Owner = window }; f.Section("Источник");
            f.BrowseText("file", "Файл .v8i", original?.Path ?? "", false, "Список 1С|*.v8i");
            f.Choice("platform", "Платформа", original?.Platform ?? "Авто", ListSourceService.Platforms); f.Check("enabled", "Прочитывать этот источник", original?.Enabled ?? true);
            f.Validate = () => { var s = new ListSource { Path = f.Value("file"), Platform = f.Value("platform") }; ListSourceService.Validate(s); return !sources.Any(x => x != original && x.Path.Equals(s.Path, StringComparison.OrdinalIgnoreCase)) || f.SetError("Этот файл уже есть в списке. Измените его свойства."); };
            if (f.ShowDialog() != true) return;
            var source = original ?? new ListSource(); source.Path = Path.GetFullPath(f.Value("file")); source.Platform = f.Value("platform"); source.Enabled = f.Flag("enabled"); if (original == null) sources.Add(source); Refresh(source);
        }
        Button Add(string text, Action action) { var b = new Button { Content = text }; b.Click += (_, _) => action(); buttons.Children.Add(b); return b; }
        Add("Добавить…", () => Edit(null)); var edit = Add("Изменить…", () => { if (grid.SelectedItem is ListSource s) Edit(s); });
        var toggle = Add("Вкл. / выкл.", () => { if (grid.SelectedItem is ListSource s) { s.Enabled = !s.Enabled; Refresh(s); } });
        var remove = Add("Убрать…", () => { if (grid.SelectedItem is ListSource s && MessageBox.Show(window, "Убрать источник из настроек?\n\n" + s.Path + "\n\nФайл списка и базы сохранятся.", "Источник", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes) { sources.Remove(s); Refresh(); } });
        var save = Add("Сохранить", () => { if (Commit(() => { catalog.Settings.PlatformLists = sources; catalog.Settings.AlternativeLists = new(); })) window.DialogResult = true; }); save.IsDefault = true;
        var cancel = Add("Отмена", () => window.DialogResult = false); cancel.IsCancel = true;
        grid.SelectionChanged += (_, _) => edit.IsEnabled = toggle.IsEnabled = remove.IsEnabled = grid.SelectedItem is ListSource;
        grid.MouseDoubleClick += (_, e) => { if (FindRow(e.OriginalSource) != null && grid.SelectedItem is ListSource s) Edit(s); };
        grid.PreviewKeyDown += (_, e) => { if (e.Key == Key.Return && grid.SelectedItem is ListSource s) { Edit(s); e.Handled = true; } else if (e.Key == Key.Space && grid.SelectedItem is ListSource t) { t.Enabled = !t.Enabled; Refresh(t); e.Handled = true; } };
        DialogBehavior.Selection(grid, edit, toggle, remove);
        Refresh(); if (window.ShowDialog() != true) return;
        if (read.IsChecked == true) ReadListSources();
    }
    private async void ReadListSources()
    {
        if (!AllowTools()) return;
        var sources = ListSourceService.Sources(catalog.Settings);
        if (sources.Count == 0) { EditListSources(); return; }
        sources = sources.Where(s => s.Enabled).ToList();
        if (sources.Count == 0) { StatusText.Text = "Нет включённых источников списков."; return; }
        string operation = "lists:" + Guid.NewGuid(); operations.Add(operation); StatusText.Text = "Чтение дополнительных списков…";
        try
        {
            var parsed = await Task.Run(() => sources.Select(ListSourceService.Read).ToList());
            int added = 0; store.Snapshot();
            if (Commit(() => { foreach (var import in parsed) added += ListSourceService.Merge(catalog, import); })) StatusText.Text = "Прочитано источников: " + parsed.Count + ". Добавлено баз: " + added;
            _ = RefreshMetadata();
        }
        catch (Exception ex) { Error("Списки не применены: не удалось прочитать один из источников", ex); }
        finally { operations.Remove(operation); }
    }
}
