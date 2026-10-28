using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using VanBaz;
using Forms = System.Windows.Forms;

internal static class TrayRestoreSmoke
{
    private static object? Call(object target, string method, params object?[] args) => target.GetType()
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Pump(int milliseconds = 250)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    public static void Run(string folder)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? app = null; MainWindow? main = null;
            try
            {
                Directory.CreateDirectory(folder); app = UiRegression.CreateApplication();
                var catalog = new Catalog(); catalog.Settings.AutoSnapshot = catalog.Settings.HotkeysEnabled = catalog.Settings.CloseToTray = false;
                catalog.Settings.MinimizeToTray = true; catalog.Settings.RestorePosition = false;
                var store = new CatalogStore(Path.Combine(folder, "Data")); store.Save(catalog);
                main = new MainWindow(store, catalog, true); main.Show(); Pump();
                var tray = (Forms.NotifyIcon)Field(main, "tray")!;
                void Require(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
                void LeftClick() => Call(tray, "OnMouseClick", new Forms.MouseEventArgs(Forms.MouseButtons.Left, 1, 0, 0, 0));
                void Visible(WindowState state, string message) => Require(main.IsVisible && main.WindowState == state && main.Opacity == 1 && !tray.Visible && Field(main, "trayPanel") == null, message);
                var bounds = Call(main, "CurrentPixelBounds");
                for (int i = 0; i < 3; i++)
                {
                    Call(main, "HideToTray"); Pump(350); Require(!main.IsVisible && tray.Visible, "window hidden with tray icon, cycle " + i);
                    LeftClick(); Pump();
                    Console.WriteLine($"LEFT_CLICK: visible={main.IsVisible}, state={main.WindowState}, trayIcon={tray.Visible}, panel={Field(main, "trayPanel") is Window { IsVisible: true }}");
                    Visible(WindowState.Normal, "single NotifyIcon click restores main window, cycle " + i);
                    Require(Equals(Call(main, "CurrentPixelBounds"), bounds), "normal bounds retained, cycle " + i);
                }
                Call(main, "HideToTray"); LeftClick(); Pump(350); Visible(WindowState.Normal, "single click during fade cancels pending hide");
                main.WindowState = WindowState.Maximized; Pump(); main.WindowState = WindowState.Minimized; Pump();
                LeftClick(); Pump(); Visible(WindowState.Maximized, "single click restores maximized state after minimization");
                Call(main, "HideToTray"); Pump(350); Call(tray, "OnDoubleClick", EventArgs.Empty); LeftClick(); Pump();
                Visible(WindowState.Maximized, "double-click and trailing click remain idempotent");
                Call(main, "HideToTray"); Pump(350);
                using var menu = new Forms.ContextMenuStrip(); Call(main, "PopulateTrayMenu", menu);
                menu.Items.OfType<Forms.ToolStripMenuItem>().Single(x => x.Text == "Панель избранного").PerformClick(); Pump();
                var panel = Field(main, "trayPanel") as Window;
                Require(panel?.IsVisible == true && !main.IsVisible, "favorites panel remains accessible from context menu");
                menu.Items.OfType<Forms.ToolStripMenuItem>().Single(x => x.Text == "Открыть ВанБаз").PerformClick(); Pump();
                Visible(WindowState.Maximized, "context-menu Open restores window and closes favorites panel");
                Console.WriteLine("PASS tray regression: real WPF window + NotifyIcon event handlers; synthetic catalog; no native mouse input or real 1C launch.");
            }
            catch (Exception ex) { failure = ex; }
            finally { main?.Close(); app?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(30000)) throw new TimeoutException("Tray regression timed out");
        if (failure != null) throw failure;
    }
}
