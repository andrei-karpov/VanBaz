using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;

namespace VanBaz;

public static class RestartPlan
{
    public static ProcessStartInfo Create(string executable, string dataFolder, string mode, bool lockMode)
    {
        if (mode is not ("Пользователь" or "Специалист" or "Разработчик")) throw new ArgumentException("Неизвестный режим интерфейса.");
        var info = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))! };
        foreach (var arg in new[] { "--data-dir", Path.GetFullPath(dataFolder), "--mode", mode }) info.ArgumentList.Add(arg);
        if (lockMode) info.ArgumentList.Add("--lock-mode");
        return info;
    }
}

public partial class MainWindow
{
    private Window CreateSystemMenuWindow()
    {
        var window = new Window { Owner = this, Title = "Системное меню ВанБаз", Width = 340, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Style = (Style)FindResource(typeof(Window));
        var root = new StackPanel { Margin = new Thickness(6) };
        var frame = new Border { BorderThickness = new Thickness(1), Child = root }; frame.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder"); window.Content = frame;
        void Select(string action) { window.Tag = action; window.DialogResult = true; }
        var grid = new Grid(); root.Children.Add(grid);
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new());
        for (int i = 0; i < 2; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tiles = new[] { ("update", "Проверить\nобновления", "↻", "edit"), ("version", "Восстановить\nверсию", "▤", "edit"), ("restart", "Перезапустить\nВанБаз", "process", "idle"), ("lock", "Временная\nблокировка", "lock", "idle"), ("settings", "Настройки", "settings", "edit"), ("folder", "Каталог\nнастроек", "folder-open", "idle") };
        for (int i = 0; i < tiles.Length; i++)
        {
            var tile = tiles[i]; var content = new StackPanel(); content.Children.Add(UiIcons.Icon(tile.Item3)); content.Children.Add(new TextBlock { Text = tile.Item2, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
            var button = new Button { Content = content, MinHeight = 67, IsEnabled = Enabled(tile.Item4), ToolTip = tile.Item2.Replace('\n', ' ') }; System.Windows.Automation.AutomationProperties.SetName(button, tile.Item2.Replace('\n', ' '));
            string action = tile.Item1; button.Click += (_, _) => Select(action); Grid.SetRow(button, i / 3); Grid.SetColumn(button, i % 3); grid.Children.Add(button);
        }
        var portable = new Button { Content = "Создать переносимую копию…", IsEnabled = CanEdit, MinHeight = 32 }; portable.Click += (_, _) => Select("portable"); root.Children.Add(portable);
        var monitors = new WrapPanel { Margin = new Thickness(2, 7, 2, 5) }; monitors.Children.Add(new TextBlock { Text = "Монитор:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) }); root.Children.Add(monitors);
        string current = System.Windows.Forms.Screen.FromHandle(DisplayHandle).DeviceName; int number = 0;
        foreach (var area in DisplayAreas())
        {
            var button = new Button { Content = (++number).ToString(), MinWidth = 32, ToolTip = area.Device, IsEnabled = !locked && area.Device != current };
            string device = area.Device; button.Click += (_, _) => Select("monitor:" + device); monitors.Children.Add(button);
        }
        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center }; root.Children.Add(footer);
        foreach (var item in new[] { ("links", "Перейти"), ("help", "Справка"), ("about", "О программе"), ("close", "Закрыть") })
        { string action = item.Item1; var button = new Button { Content = item.Item2 }; button.Click += (_, _) => Select(action); footer.Children.Add(button); }
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
        window.Deactivated += (_, _) => { if (window.IsVisible) window.Close(); };
        window.Loaded += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle; var anchor = SystemMenuButton.PointToScreen(new Point(SystemMenuButton.ActualWidth, SystemMenuButton.ActualHeight));
            if (!GetWindowRect(handle, out var bounds)) return;
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.X, (int)anchor.Y)).WorkingArea;
            int width = Math.Min(bounds.Right - bounds.Left, screen.Width), height = Math.Min(bounds.Bottom - bounds.Top, screen.Height);
            SetWindowPos(handle, IntPtr.Zero, Math.Clamp((int)anchor.X - width, screen.Left, screen.Right - width), Math.Clamp((int)anchor.Y, screen.Top, screen.Bottom - height), width, height, 0x14);
        };
        return window;
    }
    private void SystemMenu_Click(object sender, RoutedEventArgs e)
    {
        if (locked) return;
        var window = CreateSystemMenuWindow(); if (window.ShowDialog() != true || window.Tag is not string action) return;
        if (action.StartsWith("monitor:", StringComparison.Ordinal)) { MoveToMonitor(action[8..]); return; }
        switch (action)
        {
            case "update": OnlineUpdate_Click(sender, e); break;
            case "version": VersionHistory_Click(sender, e); break;
            case "restart": Restart_Click(sender, e); break;
            case "lock": Lock_Click(sender, e); break;
            case "settings": Settings_Click(sender, e); break;
            case "folder": DataFolder_Click(sender, e); break;
            case "portable": PortableCopy_Click(sender, e); break;
            case "help": Help_Click(sender, e); break;
            case "about": About_Click(sender, e); break;
            case "links": ProjectLinks_Click(sender, e); break;
        }
    }
    private void ProjectLinks_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu();
        foreach (var pair in new[] { ("ВанБаз на GitHub", "https://github.com/andrei-karpov/VanBaz"), ("Сообщить об ошибке", "https://github.com/andrei-karpov/VanBaz/issues/new"), ("StartManager на Инфостарт", "https://infostart.ru/1c/tools/274485/") })
            menu.Items.Add(Item(pair.Item1, (_, _) => OpenProductLink(pair.Item2)));
        OpenMenu(SystemMenuButton, menu);
    }
    private void Feedback_Click(object sender, RoutedEventArgs e) => OpenProductLink("https://github.com/andrei-karpov/VanBaz/issues/new");
    private void OpenProductLink(string url)
    {
        if (!Enabled("idle") || isolatedSession) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Error("Открытие ссылки", ex); }
    }
    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (!Enabled("idle") || isolatedSession || Application.Current is not App app) return;
        try
        {
            string executable = Path.Combine(AppContext.BaseDirectory, "VanBaz.exe");
            if (!File.Exists(executable)) throw new IOException("Не найден VanBaz.exe для перезапуска.");
            app.RestartStartInfo = RestartPlan.Create(executable, store.Folder, InterfaceMode, modeLocked);
            bool closed = false, previousExit = exitRequested; EventHandler closedHandler = (_, _) => closed = true;
            Closed += closedHandler;
            try { exitRequested = true; Close(); }
            finally { Closed -= closedHandler; if (!closed) { app.RestartStartInfo = null; exitRequested = previousExit; } }
        }
        catch (Exception ex) { app.RestartStartInfo = null; Error("Перезапуск ВанБаз", ex); }
    }
    private void SwitchMonitor()
    {
        var areas = DisplayAreas();
        if (areas.Count < 2) { StatusText.Text = "Доступен только один монитор."; return; }
        int index = areas.FindIndex(s => s.Device == System.Windows.Forms.Screen.FromHandle(DisplayHandle).DeviceName);
        MoveToMonitor(areas[(index + 1) % areas.Count].Device);
    }
    private void MoveToMonitor(string device)
    {
        if (locked) return;
        var area = DisplayAreas().FirstOrDefault(s => s.Device == device); if (area == null) { StatusText.Text = "Выбранный монитор недоступен."; return; }
        ShowFromTray(); CaptureNormalPlacement();
        if (!WindowGeometry.Valid(preferredPlacement)) return;
        var state = WindowState;
        preferredPlacement = WindowGeometry.MoveTo(preferredPlacement!, area); PlaceNormalWindow(); WindowState = state;
        knownDisplays = DisplayAreas(); SaveWindowPlacement(); StatusText.Text = "Окно перемещено: " + area.Device;
    }
}
