using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VanBaz;

internal static class V16Ui
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();

    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static IEnumerable<MenuItem> Items(ItemsControl menu) => menu.Items.OfType<MenuItem>().SelectMany(m => new[] { m }.Concat(Items(m)));
    private static IEnumerable<T> Visuals<T>(DependencyObject node) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var child = VisualTreeHelper.GetChild(node, i); if (child is T value) yield return value; foreach (var nested in Visuals<T>(child)) yield return nested; }
    }
    public static void Run(MainWindow main, Catalog catalog)
    {
        int checks = 0, renders = 0;
        void Require(bool ok, string label) { if (!ok) throw new Exception("v16: " + label); checks++; }
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, args);
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            int attempts = 0; string copied = ""; bool responded = false;
            var retry = ClipboardService.WriteAsync("immutable selection", text => { attempts++; if (attempts < 4) throw new ExternalException("busy", ClipboardService.CannotOpen); copied = text; });
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => responded = !retry.IsCompleted));
            Pump(retry);
            Require(attempts == 4 && copied == "immutable selection" && responded, $"temporary contention recovers while the dispatcher remains responsive (attempts={attempts}, output={copied}, responded={responded})");
            attempts = 0; string failure = "";
            Pump(ClipboardService.CopyAsync(main, "failure", failure: error => failure = error, write: _ => { attempts++; throw new ExternalException("busy", ClipboardService.CannotOpen); }));
            Require(attempts == 13 && failure.Contains("Повторите") && !failure.Contains("0x"), "permanent contention is bounded and has actionable feedback");
            attempts = 0;
            Pump(ClipboardService.CopyAsync(main, "other error", failure: error => failure = error, write: _ => { attempts++; throw new InvalidOperationException("sentinel"); }));
            Require(attempts == 1 && failure.Contains("sentinel"), "unrelated failures are not retried or hidden");
            var old = ClipboardService.WriteAsync("old", _ => throw new ExternalException("busy", ClipboardService.CannotOpen));
            var latest = ClipboardService.WriteAsync("new", text => copied = text); Pump(Task.WhenAll(old, latest));
            Require(!old.Result && latest.Result && copied == "new", "new copy supersedes pending older copy");

            if (Environment.GetEnvironmentVariable("VANBAZ_SKIP_NATIVE_CLIPBOARD") == "1") Console.WriteLine("SKIP native clipboard contention: explicitly excluded for an unavailable interactive desktop; simulated retry checks still run.");
            else
            {
            // Exercise real Windows contention without replacing the user's clipboard data.
            using var acquired = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            bool opened = false;
            var holder = new Thread(() =>
            {
                using var owner = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("ВанБаз clipboard smoke") { Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000) });
                opened = OpenClipboard(owner.Handle); acquired.Set(); if (opened) { release.Wait(5000); CloseClipboard(); }
            });
            holder.SetApartmentState(ApartmentState.STA); holder.Start(); Require(acquired.Wait(2000) && opened, "test window holds the Windows clipboard");
            attempts = 0; responded = false;
            try
            {
                var probe = ClipboardService.WriteAsync("probe only", _ => { attempts++; if (!OpenClipboard(IntPtr.Zero)) throw new ExternalException("busy", ClipboardService.CannotOpen); CloseClipboard(); });
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => responded = !probe.IsCompleted));
                var releaseTask = Task.Run(async () => { await Task.Delay(300); release.Set(); });
                Pump(probe);
                Require(attempts > 1 && responded, $"real Windows clipboard lock releases and retry succeeds without changing contents (attempts={attempts}, responded={responded})");
            }
            finally { release.Set(); holder.Join(2000); }
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }

        var service = (MenuItem)main.FindName("ServiceMenu");
        Require(service.Items.OfType<MenuItem>().Count() <= 13, "global menu is compact");
        var captions = Items(service).Select(i => i.Header?.ToString() ?? "").ToList();
        Require(!captions.Any(c => c is "Сводная информация…" or "Цвет группы…" or "Создать ярлык базы…" || c.StartsWith("Профили запуска") || c.StartsWith("Блокировка входа")), "base-specific actions do not leak into global menu");
        var grid = (DataGrid)main.FindName("BasesGrid");
        ((RadioButton)main.FindName("BasesTab")).IsChecked = true;
        var root = (FrameworkElement)main.Content; var tree = (StackPanel)main.FindName("TreeToolbar"); var left = (StackPanel)main.FindName("BaseToolbar"); var bar = (FrameworkElement)main.FindName("ListToolbar");
        string? output = Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS");
        void Capture(FrameworkElement content, string name, int width, int height, double scale = 1)
        {
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            content.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded); content.UpdateLayout();
            if (output == null) return;
            var bmp = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var backdrop = new DrawingVisual(); using (var dc = backdrop.RenderOpen()) dc.DrawRectangle(main.Background, null, new Rect(0, 0, width, height)); bmp.Render(backdrop); bmp.Render(content);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var file = File.Create(Path.Combine(output, name + ".png")); png.Save(file); renders++;
        }
        void MenuCapture(ItemsControl menu, string name)
        {
            typeof(MainWindow).GetMethod("AddMenuIcons", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { menu });
            Capture((FrameworkElement)menu, name, 370, Math.Max(120, menu.Items.Count * 25 + 10));
        }
        foreach (string theme in new[] { "Светло-серый", "Тёмный" })
        {
            catalog.Settings.Theme = theme; Call("ApplyPreferences", false); string suffix = theme == "Тёмный" ? "dark" : "light";
            foreach (var size in new[] { (684, 551, 1d), (1920, 1000, 1d), (1280, 680, 1.5), (960, 600, 2d) })
            {
                Capture(root, $"v16-{suffix}-{size.Item1}-{size.Item3}", size.Item1, size.Item2, size.Item3);
                double x = tree.TranslatePoint(new Point(), bar).X;
                Require(Math.Abs(x + tree.ActualWidth - bar.ActualWidth) < 1, "tree tools align with right edge of list at " + size.Item1);
                Require(left.TranslatePoint(new Point(), bar).X + left.ActualWidth <= x, "edit and tree tools do not overlap at " + size.Item1);
                var filter = (FrameworkElement)main.FindName("VersionFilter");
                Require(filter.TranslatePoint(new Point(), bar).X + filter.ActualWidth <= x, "platform filter fits before tree tools");
                foreach (var row in Visuals<DataGridRow>(grid).Where(r => r.Item is ListRow { IsGroup: true }))
                {
                    var disclosure = Visuals<Button>(row).First(b => b.ToolTip?.ToString() == "Свернуть / развернуть группу");
                    var pos = disclosure.TranslatePoint(new Point(), row);
                    Require(Math.Abs(pos.Y + disclosure.ActualHeight / 2 - row.ActualHeight / 2) <= 1, "row disclosure stays vertically centered");
                    Require(pos.X < 80 && disclosure.ActualWidth == 16, "row disclosure stays before its folder icon");
                }
            }
            // Render actual menu items outside the popup, then restore their original owner.
            var global = new ContextMenu(); var entries = service.Items.Cast<object>().ToList();
            foreach (var item in entries) { service.Items.Remove(item); global.Items.Add(item); }
            MenuCapture(global, "v16-global-menu-" + suffix);
            foreach (var item in entries) { global.Items.Remove(item); service.Items.Add(item); }
            foreach (string id in new[] { "file", "web" })
            {
                grid.SelectedItem = grid.Items.Cast<ListRow>().First(r => r.Entry?.Id == id);
                var menu = (ContextMenu)Call("BaseActions")!;
                Require(Items(menu).Any(m => m.Header?.ToString() == "Профили запуска…"), "launch profiles accessible on base context");
                if (id == "web") Require(!Items(menu).Any(m => m.Header?.ToString() is "Открыть папку базы" or "Проверка файла chdbfl…" or "Блокировка входа…"), "Web context excludes filesystem maintenance");
                MenuCapture(menu, "v16-context-" + id + "-" + suffix);
            }
            var fileEntry = catalog.Bases.First(b => b.Id == "file");
            string originalKind = fileEntry.Kind;
            try
            {
                fileEntry.Kind = "Server"; grid.SelectedItem = grid.Items.Cast<ListRow>().First(r => r.Entry?.Id == "file");
                var server = (ContextMenu)Call("BaseActions")!;
                Require(!Items(server).Any(m => m.Header?.ToString() is "Открыть папку базы" or "Проверка файла chdbfl…" or "Блокировка входа…"), "server context excludes local file operations");
                Require(Items(server).Any(m => m.Header?.ToString() == "Тестирование базы…"), "server context retains supported maintenance");
                MenuCapture(server, "v16-context-server-" + suffix);
            }
            finally { fileEntry.Kind = originalKind; }
            grid.SelectedItem = grid.Items.Cast<ListRow>().First(r => r.GroupKey == "group:Clients");
            var group = (ContextMenu)Call("BaseActions")!; MenuCapture(group, "v16-context-group-" + suffix);
            Require(Items(group).Any(m => m.Header?.ToString() == "Цвет группы…") && !Items(group).Any(m => m.Header?.ToString() == "Профили запуска…"), "group context keeps group actions only");
            grid.SelectedItem = null; var empty = (ContextMenu)Call("BaseActions")!; MenuCapture(empty, "v16-context-empty-" + suffix);
            Require(empty.Items.Count == 2, "empty space only offers new entries");
        }
        Console.WriteLine($"PASS v16: {checks} checks, {renders} renders; clipboard retry checks (native contention status printed separately), latest-copy ordering, menus and anchored tree geometry.");
    }
}
