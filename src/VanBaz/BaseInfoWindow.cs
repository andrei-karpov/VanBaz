using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace VanBaz;

public sealed record InfoField(string Name, string Value, bool OpenFolder = false, bool CopyOnClick = false);

public sealed class BaseInfoWindow : Window
{
    public string Summary { get; }
    public Button CopyButton { get; }
    private readonly Action<string>? writeClipboard;
    private readonly TextBlock status;
    public BaseInfoWindow(string heading, IEnumerable<InfoField> fields, Action<string>? clipboard = null, Action<string>? openFolder = null)
    {
        writeClipboard = clipboard;
        var rows = fields.ToList();
        Summary = string.Join(Environment.NewLine, rows.Select(r => r.Name + ": " + r.Value));
        Title = "Сводная информация"; Width = 580; Height = 400; MinWidth = 474; MinHeight = 310;
        Style = (Style)Application.Current.FindResource(typeof(Window)); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        DialogBehavior.FitToScreen(this);
        var root = new DockPanel { Margin = new(20, 12, 20, 12) }; Content = root;
        var title = new TextBlock { Text = heading, FontWeight = FontWeights.Bold, FontSize = 12, Margin = new(0, 0, 0, 12) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAccent"); DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        CopyButton = new Button { Content = UiIcons.Label("copy", "Копировать всё"), MinWidth = 125, ToolTip = "Копировать все сведения (Ctrl+Shift+C)" };
        CopyButton.Click += (_, _) => Copy(Summary); buttons.Children.Add(CopyButton);
        var close = new Button { Content = "Закрыть", Width = 89, IsCancel = true }; close.Click += (_, _) => Close(); buttons.Children.Add(close);
        status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) };
        status.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted"); DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(112) }); grid.ColumnDefinitions.Add(new());
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid }; root.Children.Add(scroll);
        foreach (var row in rows)
        {
            int index = grid.RowDefinitions.Count; grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var label = new TextBlock { Text = row.Name + ":", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 7, 10, 7) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ThemeMuted"); Grid.SetRow(label, index); grid.Children.Add(label);
            FrameworkElement value;
            bool actionable = !string.IsNullOrWhiteSpace(row.Value) && (row.CopyOnClick || row.OpenFolder);
            if (actionable)
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 7, 0, 7) };
                var link = new Hyperlink(new Run(row.Value)) { Cursor = Cursors.Hand, ToolTip = row.OpenFolder ? "Открыть каталог. Правой кнопкой мыши можно скопировать путь." : "Копировать " + row.Name.ToLowerInvariant() };
                link.SetResourceReference(Hyperlink.ForegroundProperty, "ThemeAccent");
                System.Windows.Automation.AutomationProperties.SetName(link, row.Name + ": " + row.Value);
                link.Click += (_, _) => { if (row.OpenFolder) Open(row.Value); else Copy(row.Value, row.Name); };
                text.Inlines.Add(link); value = text;
            }
            else value = new TextBox { Text = row.Value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new(0), Margin = new(0, 7, 0, 7) };
            System.Windows.Automation.AutomationProperties.SetName(value, row.Name);
            Grid.SetColumn(value, 1); Grid.SetRow(value, index); grid.Children.Add(value);
            var menu = new ContextMenu();
            var copy = new MenuItem { Header = "Копировать значение", Icon = UiIcons.Icon("copy"), IsEnabled = row.Value.Length > 0 }; copy.Click += (_, _) => Copy(row.Value, row.Name); menu.Items.Add(copy);
            var all = new MenuItem { Header = "Копировать всё", Icon = UiIcons.Icon("copy") }; all.Click += (_, _) => Copy(Summary); menu.Items.Add(all);
            if (row.OpenFolder)
            {
                var folder = new MenuItem { Header = "Открыть каталог", Icon = UiIcons.Icon("folder-open"), IsEnabled = actionable };
                folder.Click += (_, _) => Open(row.Value); menu.Items.Add(folder);
            }
            value.ContextMenu = menu;
        }
        PreviewKeyDown += (_, e) => { if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { Copy(Summary); e.Handled = true; } };

        void Open(string value)
        {
            try
            {
                string path = Directory.Exists(value) ? value : File.Exists(value) ? Path.GetDirectoryName(value) ?? "" : "";
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Каталог не найден: " + value);
                if (openFolder != null) openFolder(path);
                else Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
                status.Text = "Открыт каталог: " + path;
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
    }
    private async void Copy(string text, string field = "все сведения")
    { await ClipboardService.CopyAsync(this, text, () => { status.Text = "Скопировано: " + field; CopyButton.ToolTip = "Копировать все сведения (Ctrl+Shift+C)"; }, error => status.Text = error, write: writeClipboard); }
}
