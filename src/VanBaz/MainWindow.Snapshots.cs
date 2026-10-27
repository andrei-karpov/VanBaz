using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private void SaveNamedSnapshot(Window owner, Action? saved = null)
    {
        if (locked || operations.Count > 0) return;
        var form = new FormDialog("Сохранить снимок списка баз", 690, 430) { Owner = owner };
        form.Section("Снимок");
        form.Text("name", "Описание", "Список баз — " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
        form.Choice("platform", "Платформа", LegacyTab.IsChecked == true ? "7.7" : VersionFilter.SelectedIndex > 0 ? VersionFilter.SelectedItem.ToString()! : "Все", SnapshotService.Platforms.Where(v => catalog.Settings.EnableLegacy77 || v != "7.7"));
        form.Note("«Все» сохраняет весь каталог и настройки. Отдельная платформа позволяет восстановить только её список, сохранив остальные базы и настройки. Файлы информационных баз не копируются.");
        form.Validate = () => form.Value("name").Length > 0 || form.SetError("Укажите описание снимка.");
        if (form.ShowDialog() != true) return;
        try { SnapshotService.Save(store, catalog, form.Value("name"), form.Value("platform")); StatusText.Text = "Снимок сохранён: " + form.Value("name"); saved?.Invoke(); }
        catch (Exception ex) { Error("Сохранение снимка", ex); }
    }
    private void SnapshotList_Click(object sender, RoutedEventArgs e)
    {
        if (locked || operations.Count > 0) return;
        var window = new Window { Owner = this, Title = "Сохранённые снимки списков баз", Width = 920, Height = 520, MinWidth = 690, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(10) }; window.Content = root;
        var controls = new DockPanel { Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls);
        var filter = new ComboBox { Width = 105, ItemsSource = SnapshotService.Platforms.Where(v => catalog.Settings.EnableLegacy77 || v != "7.7"), SelectedIndex = 0, ToolTip = "Отбор по платформе снимка" }; DockPanel.SetDock(filter, Dock.Right); controls.Children.Add(filter);
        var searchLabel = new TextBlock { Text = "Поиск:", Margin = new(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(searchLabel, Dock.Left); controls.Children.Add(searchLabel);
        var search = new TextBox { Margin = new(0, 0, 8, 0), ToolTip = "Поиск по описанию снимка" }; controls.Children.Add(search);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 5) }; footer.Children.Add(hint);
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single, SelectionUnit = DataGridSelectionUnit.FullRow, HeadersVisibility = DataGridHeadersVisibility.Column, FontSize = catalog.Settings.FontSize, RowHeight = Math.Max(catalog.Settings.RowHeight, catalog.Settings.FontSize + 6) };
        void Column(string title, string property, double width) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = width });
        Column("Дата сохранения", "Date", 155); Column("Платформа", "Platform", 85); Column("Баз", "Bases", 48);
        grid.Columns.Add(new DataGridTextColumn { Header = "Описание", Binding = new Binding("Description"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 180 }); root.Children.Add(grid);
        Button Button(string caption, Action action) { var b = new Button { Content = caption }; b.Click += (_, _) => action(); buttons.Children.Add(b); return b; }
        List<SnapshotItem> items = new();
        void Filter(string? selected = null)
        {
            selected ??= (grid.SelectedItem as SnapshotItem)?.Path;
            grid.ItemsSource = items.Where(i => (filter.SelectedIndex == 0 || i.Platform == (string)filter.SelectedItem) && i.Description.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToList();
            grid.SelectedItem = grid.Items.Cast<SnapshotItem>().FirstOrDefault(i => i.Path == selected) ?? grid.Items.Cast<SnapshotItem>().FirstOrDefault();
        }
        void Refresh() { items = SnapshotService.List(store); Filter(); }
        Button("Сохранить…", () => SaveNamedSnapshot(window, Refresh));
        var restore = Button("Восстановить…", () => { if (grid.SelectedItem is SnapshotItem item && RestoreSnapshotFile(item.Path, window)) window.Close(); });
        var details = Button("Сведения…", () => { if (grid.SelectedItem is SnapshotItem item) FormDialog.ShowText(window, "Сведения о снимке", item.Description + "\n" + item.Date + " · " + item.Platform + " · баз: " + item.Bases + "\n\n" + item.Status + "\n\n" + item.Path); });
        var fromFile = Button("Из файла…", () => { var picker = new OpenFileDialog { Filter = "Снимок ВанБаз|*.json", Title = "Открыть снимок" }; if (picker.ShowDialog(window) == true && RestoreSnapshotFile(picker.FileName, window)) window.Close(); });
        Button("Обновить", Refresh); var close = Button("Закрыть", window.Close); close.IsCancel = true;
        void Selection()
        {
            var item = grid.SelectedItem as SnapshotItem; details.IsEnabled = item != null;
            restore.IsEnabled = CanEdit && item is { Error.Length: 0 }; fromFile.IsEnabled = CanEdit;
            hint.Text = item == null ? "Снимки не найдены. Создайте снимок или измените отбор." : item.Error.Length > 0 ? "Снимок недоступен: " + item.Error : item.Platform == "Все" ? "Восстановление заменит каталог и настройки целиком. Текущая версия будет сохранена отдельным снимком." : "Будет заменён только список платформы " + item.Platform + ". Остальные базы, настройки и история сохранятся.";
        }
        grid.SelectionChanged += (_, _) => Selection(); search.TextChanged += (_, _) => Filter(); filter.SelectionChanged += (_, _) => Filter();
        grid.MouseDoubleClick += (_, e) => { if (FindRow(e.OriginalSource) != null && grid.SelectedItem is SnapshotItem) details.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); };
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.F5) { Refresh(); e.Handled = true; } else if (e.Key == Key.Return && grid.IsKeyboardFocusWithin && restore.IsEnabled) { restore.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); e.Handled = true; } };
        Refresh(); Selection(); window.ShowDialog();
    }
    private bool RestoreSnapshotFile(string path, Window owner)
    {
        if (!CanEdit) return false;
        try
        {
            var source = SnapshotService.Read(path); var replacement = SnapshotService.Restore(catalog, source);
            string platform = source.Snapshot?.Platform ?? "Все";
            string scope = platform == "Все" ? "весь каталог и настройки" : "список платформы " + platform;
            if (MessageBox.Show(owner, $"Восстановить {scope}?\nБаз в снимке: {source.Bases.Count}.\n\nТекущий каталог будет сохранён отдельным снимком. Файлы баз не меняются.", "Восстановление снимка", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            store.Snapshot();
            if (!Commit(() => catalog = replacement)) return false;
            collapsed.Clear(); foreach (var key in catalog.Settings.CollapsedGroups) collapsed.Add(key);
            ApplyPreferences(); ApplyDesktopSettings(); ApplyColumnWidths(); _ = RefreshMetadata(); return true;
        }
        catch (Exception ex) { Error("Восстановление снимка", ex); return false; }
    }
}
