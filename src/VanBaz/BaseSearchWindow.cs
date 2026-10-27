using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace VanBaz;

public sealed class FoundBase
{
    public bool Selected { get; set; } = true;
    public string Folder { get; init; } = "";
    public string Name { get; set; } = "";
    public long Bytes { get; init; }
    public DateTime Modified { get; init; }
    public double Megabytes => Math.Round(Bytes / 1048576d, 2);
    public bool Registered { get; init; }
    public string State => Registered ? "Уже в списке" : "Новая";
}
public sealed record BaseSearchResult(List<FoundBase> Bases, int Inaccessible, bool Stopped);
public static class BaseSearch
{
    public static BaseSearchResult Scan(IEnumerable<string> roots, IEnumerable<string> existing, long minimumBytes, DateTime? modifiedSince, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = existing.Select(p => Path.GetFullPath(p).TrimEnd('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new List<FoundBase>(); int inaccessible = 0;
        var pending = new Stack<string>(roots.Select(Path.GetFullPath));
        while (pending.Count > 0 && !token.IsCancellationRequested)
        {
            string folder = pending.Pop().TrimEnd(Path.DirectorySeparatorChar); if (folder.EndsWith(':')) folder += "\\";
            if (!seen.Add(folder)) continue;
            try
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                var file = new FileInfo(Path.Combine(folder, "1Cv8.1CD"));
                if (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0 && file.Length >= minimumBytes && (!modifiedSince.HasValue || file.LastWriteTime >= modifiedSince.Value))
                    found.Add(new() { Folder = folder, Name = new DirectoryInfo(folder).Name, Bytes = file.Length, Modified = file.LastWriteTime, Registered = known.Contains(folder.TrimEnd('\\', '/')), Selected = !known.Contains(folder.TrimEnd('\\', '/')) });
                foreach (string child in Directory.EnumerateDirectories(folder)) pending.Push(child);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { inaccessible++; }
        }
        return new(found.OrderBy(b => b.Folder, StringComparer.OrdinalIgnoreCase).ToList(), inaccessible, token.IsCancellationRequested);
    }
}
public sealed class BaseSearchWindow : Window
{
    private readonly TextBox roots = new() { AcceptsReturn = true, Height = 60, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox minimum = new() { Text = "0", Width = 65 };
    private readonly CheckBox useDate = new() { Content = "Изменены с", VerticalAlignment = VerticalAlignment.Center };
    private readonly DatePicker since = new() { SelectedDate = DateTime.Today, Width = 125, IsEnabled = false };
    private readonly TextBox prefix = new() { Width = 120 };
    private readonly CheckBox dateName = new() { Content = "Дата в имени" };
    private readonly ComboBox version = new() { Width = 95, IsEditable = true };
    private readonly ComboBox group = new() { MinWidth = 150, IsEditable = true };
    private readonly DataGrid grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Extended };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(4) };
    private readonly Button search = new() { Content = "Найти", MinWidth = 90 };
    private readonly Button stop = new() { Content = "Остановить", MinWidth = 90, IsEnabled = false };
    private readonly Button accept = new() { Content = "Добавить отмеченные", MinWidth = 155, IsEnabled = false };
    private readonly List<string> existing;
    private CancellationTokenSource? cancellation;
    private bool closed;
    public ObservableCollection<FoundBase> Results { get; } = new();
    public List<BaseEntry> Entries { get; private set; } = new();
    public BaseSearchWindow(Catalog catalog, string groupPath)
    {
        Title = "Поиск файловых баз"; Width = 850; Height = 630; MinWidth = 680; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; UseLayoutRounding = true;
        existing = catalog.Bases.Where(b => b.Kind == "File").Select(b => b.Location).ToList();
        roots.Text = catalog.Settings.DefaultFolder; dateName.IsChecked = catalog.Settings.NewBaseNameWithDate;
        version.ItemsSource = new[] { "8.1", "8.2", "8.3", "8.4", "8.5" }; version.Text = catalog.Settings.DefaultVersion;
        group.ItemsSource = new[] { "" }.Concat(catalog.Groups.Select(g => g.Path)).Concat(catalog.Bases.Select(b => b.Group)).Distinct().Order().ToList(); group.Text = groupPath;
        var root = new DockPanel { Margin = new(10) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "Каталоги поиска — каждый с новой строки", Margin = new(3) });
        var paths = new DockPanel(); var browse = new Button { Content = "Добавить папку…", Width = 130, VerticalAlignment = VerticalAlignment.Top }; DockPanel.SetDock(browse, Dock.Right); paths.Children.Add(browse); paths.Children.Add(roots); top.Children.Add(paths);
        browse.Click += (_, _) => { var f = new OpenFolderDialog { Multiselect = true, Title = "Каталоги поиска" }; if (f.ShowDialog(this) == true) roots.Text = string.Join(Environment.NewLine, roots.Text.Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).Concat(f.FolderNames).Distinct(StringComparer.OrdinalIgnoreCase)); };
        var filter = new WrapPanel { Margin = new(0, 7, 0, 5) }; top.Children.Add(filter);
        filter.Children.Add(new TextBlock { Text = "Размер от, МБ", VerticalAlignment = VerticalAlignment.Center, Margin = new(4) }); filter.Children.Add(minimum); filter.Children.Add(useDate); filter.Children.Add(since); filter.Children.Add(search); filter.Children.Add(stop);
        useDate.Checked += (_, _) => since.IsEnabled = true; useDate.Unchecked += (_, _) => since.IsEnabled = false;
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var naming = new WrapPanel { Margin = new(0, 6, 0, 0) }; bottom.Children.Add(naming);
        foreach (var pair in new (string, UIElement)[] { ("Префикс", prefix), ("Платформа", version), ("Группа", group) }) { naming.Children.Add(new TextBlock { Text = pair.Item1, VerticalAlignment = VerticalAlignment.Center, Margin = new(5) }); naming.Children.Add(pair.Item2); } naming.Children.Add(dateName);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(status); bottom.Children.Add(buttons);
        Button Action(string label, Action action) { var b = new Button { Content = label }; b.Click += (_, _) => action(); buttons.Children.Add(b); return b; }
        Action("Отметить новые", () => Select(true)); Action("Снять отметки", () => Select(false)); Action("Очистить", () => { Results.Clear(); accept.IsEnabled = false; }); buttons.Children.Add(accept);
        Action("Закрыть", Close);
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "", Binding = new Binding(nameof(FoundBase.Selected)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 30 });
        foreach (var c in new[] { ("Наименование", "Name", 140d), ("Каталог", "Folder", 260d), ("МБ", "Megabytes", 65d), ("Изменён", "Modified", 115d), ("Состояние", "State", 100d) })
            grid.Columns.Add(new DataGridTextColumn { Header = c.Item1, Binding = new Binding(c.Item2) { StringFormat = c.Item2 == "Modified" ? "dd.MM.yy HH:mm" : null }, Width = c.Item2 == "Folder" ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(c.Item3), IsReadOnly = c.Item2 != "Name" });
        grid.ItemsSource = Results; root.Children.Add(grid);
        search.Click += async (_, _) => await SearchAsync(); stop.Click += (_, _) => cancellation?.Cancel();
        accept.Click += (_, _) => AddSelected();
        Closing += (_, _) => { closed = true; cancellation?.Cancel(); };
        DialogBehavior.CloseOnEscape(this);
    }
    private void Select(bool value) { grid.CommitEdit(); foreach (var row in Results) row.Selected = value && !row.Registered; grid.Items.Refresh(); }
    private async Task SearchAsync()
    {
        if (!decimal.TryParse(minimum.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal mb) || mb < 0 || mb > long.MaxValue / 1048576m) { status.Text = "Укажите неотрицательный размер в МБ."; return; }
        string[] paths = roots.Text.Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
        if (paths.Length == 0 || paths.Any(p => !Path.IsPathFullyQualified(p))) { status.Text = "Укажите полные пути каталогов."; return; }
        if (useDate.IsChecked == true && since.SelectedDate == null) { status.Text = "Укажите дату изменения."; return; }
        search.IsEnabled = accept.IsEnabled = false; stop.IsEnabled = true; Results.Clear(); status.Text = "Поиск…";
        using var cancel = new CancellationTokenSource(); cancellation = cancel;
        try
        {
            DateTime? date = useDate.IsChecked == true ? since.SelectedDate : null;
            var result = await Task.Run(() => BaseSearch.Scan(paths, existing, (long)(mb * 1048576), date, cancel.Token));
            if (closed) return;
            foreach (var row in result.Bases) Results.Add(row);
            status.Text = $"Найдено: {Results.Count}. Уже в списке: {Results.Count(r => r.Registered)}. Недоступных каталогов: {result.Inaccessible}." + (result.Stopped ? " Поиск остановлен; частичные результаты сохранены." : "");
            accept.IsEnabled = Results.Any(r => !r.Registered);
        }
        catch (Exception ex) { if (!closed) status.Text = ex.Message; }
        finally { cancellation = null; search.IsEnabled = true; stop.IsEnabled = false; }
    }
    private void AddSelected()
    {
        grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
        if (!System.Text.RegularExpressions.Regex.IsMatch(version.Text.Trim(), @"^8\.\d+(?:\.\d+){0,2}$")) { status.Text = "Укажите версию платформы 8.x."; return; }
        var selected = Results.Where(r => r.Selected && !r.Registered).ToList();
        if (selected.Count == 0) { status.Text = "Отметьте новые базы."; return; }
        Entries = selected.Select(r => new BaseEntry { Name = prefix.Text + r.Name + (dateName.IsChecked == true ? " " + r.Modified.ToString("dd.MM.yyyy") : ""), Location = r.Folder, Group = group.Text.Trim().Replace('\\', '/').Trim('/'), Version = version.Text.Trim() }).ToList();
        if (Entries.Any(b => string.IsNullOrWhiteSpace(b.Name) || b.Name.IndexOfAny(new[] { '\r', '\n', ']' }) >= 0 || b.Group.IndexOfAny(new[] { '\r', '\n', ']' }) >= 0)) { status.Text = "Проверьте наименования и группу."; return; }
        DialogResult = true;
    }
}
