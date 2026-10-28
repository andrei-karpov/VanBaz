using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VanBaz;

public partial class MainWindow
{
    private Window? trayPanel;
    private void ShowTrayPanel()
    {
        if (locked) { ShowFromTray(); return; }
        if (trayPanel is { IsVisible: true }) { trayPanel.Activate(); return; }
        var popup = trayPanel = CreateTrayPanel(); popup.Closed += (_, _) => { if (trayPanel == popup) trayPanel = null; };
        popup.Show(); popup.Activate();
    }
    private Window CreateTrayPanel(Action<string, bool>? launch = null)
    {
        launch ??= LaunchFromTray;
        var window = new Window { Title = "Избранное · ВанБаз", Width = 310, Height = 300, MinWidth = 280, MinHeight = 240, ResizeMode = ResizeMode.CanResizeWithGrip, ShowInTaskbar = false, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual };
        var area = SystemParameters.WorkArea; window.Left = area.Right - window.Width - 8; window.Top = area.Bottom - window.Height - 8; DialogBehavior.FitToScreen(window);
        var root = new DockPanel { Margin = new Thickness(6) }; window.Content = root;
        var header = new WrapPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        Button Button(Panel panel, string caption, string hint, Action action, bool enabled = true)
        { var b = new Button { Content = caption, ToolTip = hint, IsEnabled = enabled }; panel.Children.Add(b); b.Click += (_, _) => action(); return b; }
        Button(header, "ВанБаз", "Открыть главное окно", () => { window.Close(); ShowFromTray(); });
        Button(header, "▣", "Временная блокировка", () => { window.Close(); ShowFromTray(); Lock_Click(this, new RoutedEventArgs()); }, Enabled("idle"));
        Button(header, "×", "Закрыть панель · Escape", window.Close);
        var footer = new WrapPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        Button(footer, "Открыть", "Открыть главное окно", () => { window.Close(); ShowFromTray(); });
        Button(footer, "Папка", "Каталог настроек", () => DataFolder_Click(this, new RoutedEventArgs()), Enabled("idle"));
        Button(footer, "Снимок", "Сохранить снимок экрана", () => { window.Close(); Screenshot_Click(this, new RoutedEventArgs()); }, Enabled("idle"));
        Button(footer, "Выход", "Завершить ВанБаз", () => { if (!Enabled("idle")) return; window.Close(); exitRequested = true; Close(); }, Enabled("idle"));
        var list = new ListBox { Name = "TrayFavorites", DisplayMemberPath = "Name", ItemsSource = catalog.Bases.Where(b => b.Favorite && (catalog.Settings.EnableLegacy77 || b.Version != "7.7")).OrderBy(b => b.Name).ToArray() }; root.Children.Add(list);
        if (list.Items.Count == 0) list.ItemsSource = new[] { new BaseEntry { Id = "", Name = "Список избранных пуст" } };
        var menu = new ContextMenu(); var enterprise = new MenuItem { Header = "1С:Предприятие" }; var designer = new MenuItem { Header = "Конфигуратор" }; menu.Items.Add(enterprise); menu.Items.Add(designer); list.ContextMenu = menu;
        void Start(bool design) { if (!Enabled("idle") || design && !CanEdit || list.SelectedItem is not BaseEntry entry || entry.Id.Length == 0 || design && entry.Kind == "Web") return; window.Close(); launch(entry.Id, design); }
        menu.Opened += (_, _) => { enterprise.IsEnabled = Enabled("idle") && list.SelectedItem is BaseEntry { Id.Length: > 0 }; designer.IsEnabled = enterprise.IsEnabled && CanEdit && list.SelectedItem is BaseEntry { Kind: not "Web" }; };
        enterprise.Click += (_, _) => Start(false); designer.Click += (_, _) => Start(true);
        window.Deactivated += (_, _) => { if (!menu.IsOpen) window.Close(); };
        menu.Closed += (_, _) => { if (!window.IsActive && window.IsVisible) window.Close(); };
        list.PreviewMouseRightButtonDown += (_, e) => { if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem item) item.IsSelected = true; };
        list.MouseDoubleClick += (_, e) => { if (DialogBehavior.OnItem(e.OriginalSource)) Start(false); };
        list.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Start(Keyboard.Modifiers == ModifierKeys.Control); } };
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
        window.Loaded += (_, _) => { if (list.Items.Count > 0) list.SelectedIndex = 0; list.Focus(); }; return window;
    }
}
