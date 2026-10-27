using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VanBaz;

internal static class V12Ui
{
    public static void Run(string root)
    {
        var catalog = new Catalog { Bases = new()
        {
            new() { Id = "bound", Name = "Before", Group = "Before", Location = @"D:\Synthetic Bound", Executable = Path.Combine(AppContext.BaseDirectory, "1cv8.exe") },
            new() { Id = "other", Name = "Other", Location = @"D:\Do Not Launch", Version = "8.2" }
        } };
        catalog.Settings.CloseToTray = catalog.Settings.MinimizeAfterLaunch = false;
        catalog.Settings.HotkeysEnabled = false; catalog.Settings.BaseKeys["1"] = "BOUND";
        var store = new CatalogStore(Path.Combine(root, "key-launch")); store.Save(catalog);
        var window = new MainWindow(store, catalog, false);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, args);
        T Control<T>(string name) => (T)window.FindName(name);
        void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        void Pump(Func<bool> complete)
        {
            var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(6);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { if (complete() || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Require(complete(), "Timed out waiting for synthetic launch");
        }
        string capture = Path.Combine(root, "key-launch-args.json");
        string? previous = Environment.GetEnvironmentVariable("VANBAZ_CAPTURE"); Environment.SetEnvironmentVariable("VANBAZ_CAPTURE", capture);
        try
        {
            catalog.Bases[0].Name = "Renamed"; catalog.Bases[0].Group = "Moved/Nested";
            store.Save(catalog);
            Call("SelectBaseInList", "other"); Control<TextBox>("SearchBox").Text = "Other"; Control<ComboBox>("VersionFilter").SelectedItem = "8.2";
            // Model an immediate launch from a shortcut while initial discovery is still pending.
            catalog.Bases[0].Executable = "";
            var delayed = new TaskCompletionSource<PlatformScan>();
            var inventory = new PlatformInventory(scanner: (_, _) => delayed.Task);
            typeof(MainWindow).GetField("platformInventory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, inventory);
            catalog.Settings.PlatformRoots = new() { AppContext.BaseDirectory };
            var refresh = (Task)Call("RefreshPlatformsAsync", new object?[] { null })!;
            var task = (Task)Call("RunBoundBaseAsync", "1")!;
            Require(!task.IsCompleted && !File.Exists(capture), "Launch before initial discovery must wait without launching a fallback executable");
            delayed.SetResult(new(new() { new PlatformInfo("8.3", AppContext.BaseDirectory, "x64") { ThickClient = true } }, new()));
            Pump(() => task.IsCompleted); task.GetAwaiter().GetResult();
            Pump(() => File.Exists(capture)); var received = JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
            Require(received.Contains(@"D:\Synthetic Bound") && !received.Contains(@"D:\Do Not Launch") && catalog.History.Single().BaseId == "bound", "Local key must launch its ID after rename/move, ignoring search, version filter and another selection");
            var keys = Control<MenuItem>("BaseLaunchKeysMenu"); Call("BaseLaunchKeysMenu_Opened", keys, new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, keys));
            Require(keys.Items.OfType<MenuItem>().First() is { Header: "Renamed", InputGestureText: "Ctrl+Alt+1", IsEnabled: true }, "Visible key menu must show current base name and gesture");
            Require((bool)Call("HandleBaseLaunchKey", Key.D1, ModifierKeys.Control | ModifierKeys.Alt, true)! && catalog.History.Count == 1, "Key auto-repeat must not create another launch");
            Require(!(bool)Call("HandleBaseLaunchKey", Key.D1, ModifierKeys.Control, false)!, "Other modifier combinations must not trigger a bound base");
            catalog.Settings.HotkeysEnabled = true; catalog.Settings.ShowHotkey = "Ctrl+Alt+1";
            var dialog = (FormDialog)Call("CreateBaseKeysDialog")!;
            Require(dialog.Validate!() == false, "Binding dialog must reject a collision with a global action"); dialog.Close(); catalog.Settings.HotkeysEnabled = false;
            catalog.Bases.RemoveAll(b => b.Id == "bound"); Call("BuildRows", new object?[] { null });
            ((Task)Call("RunBoundBaseAsync", "1")!).GetAwaiter().GetResult();
            Require(catalog.History.Count == 1 && Control<TextBlock>("StatusText").Text.Contains("удалена"), "Deleted bound entry must not launch the fallback selection");
            Call("BaseLaunchKeysMenu_Opened", keys, new RoutedEventArgs(MenuItem.SubmenuOpenedEvent, keys));
            Require(keys.Items.OfType<MenuItem>().First() is { Header: "База удалена", IsEnabled: false }, "Deleted binding is visibly unavailable in the menu");
            store.Save(catalog); var bytes = File.ReadAllBytes(store.FilePath);
            var pending = FileRecovery.Begin(Path.Combine(store.Folder, "RecoveryOperations"), "Synthetic pending agreement", new Dictionary<string, byte[]?> { [store.FilePath] = bytes });
            catalog.Settings.Width += 23; window.Close();
            Require(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Closing the real WPF window must preserve a pending catalog recovery byte-for-byte");
            FileRecovery.Restore(pending);
        }
        finally { Environment.SetEnvironmentVariable("VANBAZ_CAPTURE", previous); window.Close(); }
    }
}
