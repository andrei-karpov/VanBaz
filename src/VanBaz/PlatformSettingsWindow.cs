using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace VanBaz;

public sealed class PlatformRootRow
{
    public bool Enabled { get; set; } = true;
    public string Path { get; set; } = "";
}
public sealed class PlatformSettingsWindow : Window
{
    public ObservableCollection<PlatformRootRow> Roots { get; } = new();
    public string Browser => (string)browser.SelectedItem;
    public PlatformScan? Result { get; private set; }
    public Func<bool>? ApplyChanges { get; set; }
    private readonly ComboBox browser = new() { ItemsSource = new[] { "Chrome", "Edge" }, Width = 130 };
    private readonly DataGrid roots;
    private CancellationTokenSource? search;
    private string? searchedRoots;
    private string Signature() => string.Join("\n", Roots.Select(r => (r.Enabled ? "+" : "-") + r.Path.ToUpperInvariant()));
    public PlatformSettingsWindow(Preferences settings, IEnumerable<PlatformInfo> current, Func<IEnumerable<string>, CancellationToken, Task<PlatformScan>>? scanner = null)
    {
        Title = "Каталоги платформ и браузер"; Width = 1050; Height = 740; MinWidth = 640; MinHeight = 420; Style = (Style)Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DialogBehavior.FitToScreen(this);
        foreach (var path in settings.PlatformRoots) Roots.Add(new() { Path = path, Enabled = !settings.DisabledPlatformRoots.Contains(path, StringComparer.OrdinalIgnoreCase) });
        browser.SelectedItem = settings.WebBrowser is "Chrome" ? "Chrome" : "Edge";
        var root = new DockPanel { Margin = new(12) }; Content = root;
        bool closed = false;
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var save = new Button { Content = "Сохранить", IsDefault = true }; var close = new Button { Content = "Отмена", IsCancel = true }; footer.Children.Add(close); footer.Children.Add(save);
        close.Click += (_, _) => DialogResult = false;
        save.Click += (_, _) => { try { ValidateRoots(); if (searchedRoots != Signature()) Result = null; if (ApplyChanges?.Invoke() != false) DialogResult = true; } catch (Exception ex) { MessageBox.Show(this, ex.Message); } };
        var content = new Grid(); content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.Children.Add(content);
        var top = new StackPanel(); var topScroll = new ScrollViewer { Content = top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; content.Children.Add(topScroll);
        top.Children.Add(new TextBlock { Text = "Стандартные каталоги проверяются всегда. Дополнительные корни: глубина до 3 уровней, до 5000 каталогов на корень.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,8) });
        roots = new DataGrid { Height = 115, ItemsSource = Roots, AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        roots.Columns.Add(new DataGridCheckBoxColumn { Header = "Включён", Binding = new Binding("Enabled"), Width = 85 });
        roots.Columns.Add(new DataGridTextColumn { Header = "Полный путь", Binding = new Binding("Path"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 160 }); top.Children.Add(roots);
        var actions = new WrapPanel { Margin = new(0, 7, 0, 8) }; top.Children.Add(actions);
        Button Add(string label, Action action) { var b = new Button { Content = label }; b.Click += (_, _) => action(); actions.Children.Add(b); return b; }
        var add = Add("Добавить папку…", () => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog(this) == true && !Roots.Any(r => r.Path.Equals(d.FolderName, StringComparison.OrdinalIgnoreCase))) Roots.Add(new() { Path = d.FolderName }); });
        var remove = Add("Удалить корень", () => { if (roots.SelectedItem is PlatformRootRow row) Roots.Remove(row); });
        remove.IsEnabled = false; roots.SelectionChanged += (_, _) => remove.IsEnabled = search == null && roots.SelectedItem != null;
        var find = Add("Найти платформы", () => { }); var cancel = Add("Отменить поиск", () => search?.Cancel()); cancel.IsEnabled = false;
        actions.Children.Add(new TextBlock { Text = "  Веб-клиент: ", VerticalAlignment = VerticalAlignment.Center }); actions.Children.Add(browser);
        var status = new TextBlock { Text = "Нажмите «Найти платформы» для проверки изменённых корней.", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,8) }; top.Children.Add(status);
        var explanation = new Button { Content = "DefaultVersion и выбор платформы…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0,0,0,10) }; top.Children.Add(explanation);
        explanation.Click += async (_, _) => { explanation.IsEnabled = false; try { var text = await Task.Run(() => StarterConfiguration.Explain()); if (IsVisible) FormDialog.ShowText(this, "Настройки штатного стартера", text); } finally { explanation.IsEnabled = true; } };
        var found = new DataGrid { IsReadOnly = true, ItemsSource = current.ToList(), AutoGenerateColumns = false, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column };
        found.Columns.Add(new DataGridTextColumn { Header = "Версия EXE", Binding = new Binding("Version"), Width = 140 });
        found.Columns.Add(new DataGridTextColumn { Header = "Архитектура", Binding = new Binding("Architecture"), Width = 110 });
        found.Columns.Add(new DataGridCheckBoxColumn { Header = "Толстый", Binding = new Binding("ThickClient") { Mode = BindingMode.OneWay }, Width = 80 });
        found.Columns.Add(new DataGridCheckBoxColumn { Header = "Тонкий", Binding = new Binding("ThinClient") { Mode = BindingMode.OneWay }, Width = 80 });
        var tooltip = new Style(typeof(TextBlock)); tooltip.Setters.Add(new Setter(TextBlock.ToolTipProperty, new Binding("Directory")));
        found.Columns.Add(new DataGridTextColumn { Header = "Каталог", Binding = new Binding("Directory"), ElementStyle = tooltip, MinWidth = 260, Width = new DataGridLength(1, DataGridLengthUnitType.Star) }); Grid.SetRow(found, 1); content.Children.Add(found);
        find.Click += async (_, _) =>
        {
            try
            {
                ValidateRoots(); search = new(); add.IsEnabled = remove.IsEnabled = browser.IsEnabled = find.IsEnabled = save.IsEnabled = roots.IsEnabled = false; cancel.IsEnabled = true; status.Text = "Поиск платформ…";
                string signature = Signature();
                var result = await (scanner ?? PlatformCatalog.ScanAsync)(Roots.Where(r => r.Enabled).Select(r => r.Path).ToArray(), search.Token);
                if (closed) return; searchedRoots = signature; Result = result; found.ItemsSource = result.Platforms; status.Text = "Найдено: " + result.Platforms.Count + (result.Errors.Count == 0 ? "" : ". Ошибки: " + string.Join("; ", result.Errors.Take(3)));
            }
            catch (OperationCanceledException) { status.Text = "Поиск отменён, прежний результат сохранён. Незавершённый сетевой вызов Windows завершится в фоне; следующий поиск дождётся его окончания."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { search?.Dispose(); search = null; if (!closed) { add.IsEnabled = browser.IsEnabled = find.IsEnabled = save.IsEnabled = roots.IsEnabled = true; remove.IsEnabled = roots.SelectedItem != null; cancel.IsEnabled = false; } }
        };
        Closed += (_, _) => { closed = true; search?.Cancel(); };
    }
    private void ValidateRoots()
    {
        if (!roots.CommitEdit(DataGridEditingUnit.Cell, true) || !roots.CommitEdit(DataGridEditingUnit.Row, true)) throw new ArgumentException("Завершите редактирование каталога.");
        foreach (var row in Roots) { row.Path = row.Path.Trim(); if (!Path.IsPathFullyQualified(row.Path)) throw new ArgumentException("Укажите полные пути корней."); row.Path = Path.GetFullPath(row.Path); }
        if (Roots.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new ArgumentException("Один корень указан несколько раз.");
    }
}
