using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VanBaz;

public partial class MainWindow
{
    private bool applyingColumns;
    private void InitializeColumns()
    {
        ApplyColumnWidths();
        foreach (var column in BasesGrid.Columns.Where(c => c.SortMemberPath.Length > 0))
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            EventHandler changed = (_, _) => { if (!applyingColumns && column.Width.IsAbsolute && double.IsFinite(column.Width.Value)) catalog.Settings.ColumnWidths[column.SortMemberPath] = column.Width.Value; };
            descriptor.AddValueChanged(column, changed); Closed += (_, _) => descriptor.RemoveValueChanged(column, changed);
        }
        BasesGrid.Sorting += (_, e) =>
        {
            e.Handled = true;
            if (locked || operations.Count > 0 || e.Column.SortMemberPath.Length == 0) return;
            string key = e.Column.SortMemberPath;
            Commit(() => { if (catalog.Settings.SortColumn != key) { catalog.Settings.SortColumn = key; catalog.Settings.SortDescending = false; } else if (!catalog.Settings.SortDescending) catalog.Settings.SortDescending = true; else { catalog.Settings.SortColumn = ""; catalog.Settings.SortDescending = false; } });
            UpdateSortArrows();
        };
    }
    private void ApplyColumnWidths()
    {
        applyingColumns = true;
        try
        {
            double Fit(string value, double minimum)
            {
                var text = new FormattedText(value, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(BasesGrid.FontFamily, BasesGrid.FontStyle, BasesGrid.FontWeight, BasesGrid.FontStretch), BasesGrid.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(BasesGrid).PixelsPerDip);
                return Math.Max(minimum, Math.Ceiling(text.WidthIncludingTrailingWhitespace + 14));
            }
            double versionWidth = catalog.Bases.Select(b => Fit(b.Version, 88)).DefaultIfEmpty(88).Max();
            foreach (var column in BasesGrid.Columns.Where(c => c.SortMemberPath.Length > 0))
            {
                if (catalog.Settings.ColumnWidths.TryGetValue(column.SortMemberPath, out double width) && double.IsFinite(width)) column.Width = Math.Clamp(width, column.MinWidth, 1000);
                else column.Width = column.SortMemberPath switch { "Name" => new DataGridLength(1, DataGridLengthUnitType.Star), "Version" => versionWidth, "Date" => Fit("30.09.99",70), _ => Fit("999,99 ГБ",65) };
            }
            UpdateSortArrows();
        }
        finally { applyingColumns = false; }
    }
    private void UpdateSortArrows()
    {
        foreach (var column in BasesGrid.Columns) column.SortDirection = column.SortMemberPath == catalog.Settings.SortColumn && column.SortMemberPath.Length > 0 ? catalog.Settings.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending : null;
    }
    private IEnumerable<BaseEntry> SortEntries(IEnumerable<BaseEntry> entries)
    {
        var p = catalog.Settings; bool desc = p.SortDescending;
        return p.SortColumn switch
        {
            "Name" => desc ? entries.OrderByDescending(b => b.Name, StringComparer.CurrentCultureIgnoreCase) : entries.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase),
            "Version" => desc ? entries.OrderByDescending(b => Version.TryParse(b.Version, out var v) ? v : new Version()) : entries.OrderBy(b => Version.TryParse(b.Version, out var v) ? v : new Version()),
            "Date" => desc ? entries.OrderByDescending(b => metadata.GetValueOrDefault(b.Id).Modified) : entries.OrderBy(b => metadata.GetValueOrDefault(b.Id).Modified),
            "Size" => desc ? entries.OrderByDescending(b => metadata.GetValueOrDefault(b.Id).Bytes) : entries.OrderBy(b => metadata.GetValueOrDefault(b.Id).Bytes),
            _ => p.Alphabetical ? entries.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase) : entries.OrderBy(b => b.Order)
        };
    }
    private static DataGridColumnHeader? HeaderAt(object source)
    {
        for (var node = source as DependencyObject; node != null; node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
            if (node is DataGridColumnHeader header) return header;
        return null;
    }
    private void ColumnsMenu_Click(object sender, RoutedEventArgs e) => OpenMenu((FrameworkElement)sender, ColumnsMenu());
    private ContextMenu ColumnsMenu()
    {
        var menu = NewMenu();
        void Column(string title, bool visible, Action<bool> change)
        {
            var item = new MenuItem { Header = title, IsEnabled = Enabled("idle"), IsCheckable = true, IsChecked = visible, StaysOpenOnClick = true };
            item.Click += (_, _) => { if (Commit(() => change(item.IsChecked))) ApplyPreferences(); }; menu.Items.Add(item);
        }
        menu.Items.Add(new MenuItem { Header = "Колонки списка", IsEnabled = false });
        Column("Платформа", catalog.Settings.ShowPlatform, b => catalog.Settings.ShowPlatform = b);
        Column("Дата изменения", catalog.Settings.ShowDate, b => catalog.Settings.ShowDate = b);
        Column("Размер файла", catalog.Settings.ShowSize, b => catalog.Settings.ShowSize = b);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Сохранить порядок элементов", SaveListOrder_Click, "edit"));
        menu.Items.Add(Item("Сбросить ширину колонок", (_, _) => { if (Commit(() => catalog.Settings.ColumnWidths.Clear())) ApplyColumnWidths(); }));
        menu.Items.Add(Item("Исходный порядок", (_, _) => { if (Commit(() => { catalog.Settings.SortColumn = ""; catalog.Settings.SortDescending = false; })) UpdateSortArrows(); }));
        menu.Items.Add(new MenuItem { Header = "Щелчок по заголовку — сортировка", IsEnabled = false });
        return menu;
    }
}
