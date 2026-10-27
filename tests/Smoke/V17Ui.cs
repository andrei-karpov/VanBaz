using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VanBaz;

internal static class V17Ui
{
    private static object? Call(MainWindow w, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    private static T Control<T>(MainWindow w, string name) => (T)w.FindName(name);
    private static Dictionary<string, Control> Fields(FormDialog d) => (Dictionary<string, Control>)typeof(FormDialog).GetField("fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(d)!;
    private static void Require(bool ok, string why) { if (!ok) throw new Exception("v17: " + why); }
    private static void Pump(Func<bool> done)
    {
        var frame = new DispatcherFrame(); var end = DateTime.UtcNow.AddSeconds(10);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= end) frame.Continue = false; };
        timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Require(done(), "timed out waiting for test process");
    }
    private static void Render(FrameworkElement content, string name, int width, int height)
    {
        string? output = Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS"); if (output == null) return;
        content.Measure(new(width,height)); content.Arrange(new(0,0,width,height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output,name+".png")); png.Save(file);
    }
    public static void Run(MainWindow main, Catalog catalog)
    {
        Require(!new Preferences().EnableLegacy77 && new Preferences().AfterLaunchAction == "Свернуть в трей", "fresh defaults require explicit 7.7 opt-in and use tray after launch");
        int count = catalog.Bases.Count;
        var settings = (FormDialog)Call(main,"CreateSettingsDialog")!;
        var fields = Fields(settings);
        ((CheckBox)fields["legacy77"]).IsChecked = false;
        Require(settings.Validate!() && settings.ApplyChanges!(), "settings apply legacy-off");
        Require(Control<RadioButton>(main,"LegacyTab").Visibility == Visibility.Collapsed && Control<MenuItem>(main,"LegacyListMenu").Visibility == Visibility.Collapsed, "7.7 tab and import menu hidden");
        Require(!(bool)Call(main,"SelectBaseInList","legacy")!, "shortcut cannot select disabled 7.7 or fall back to another base");
        Control<RadioButton>(main,"LegacyTab").IsChecked = true;
        Require(Control<RadioButton>(main,"BasesTab").IsChecked == true, "hidden tab cannot be activated by a stale action");
        using (var copy = new MemoryStream())
        {
            JsonSerializer.Serialize(copy,catalog); copy.Position=0;
            Require(!JsonSerializer.Deserialize<Catalog>(copy)!.Settings.EnableLegacy77, "7.7 setting persists");
        }
        Call(main,"Import77_Click",main,new RoutedEventArgs()); Call(main,"Import77File_Click",main,new RoutedEventArgs()); Call(main,"Export77File_Click",main,new RoutedEventArgs());
        var editor = new BaseEditor(new BaseEntry { Name="Fixture", Location=@"D:\Synthetic", Executable=@"D:\1cv7.exe" },new(),new());
        var versions = (ComboBox)Fields(editor)["version"];
        Require(!versions.Items.Cast<string>().Contains("7.7"), "disabled 7.7 is absent from base properties choices");
        versions.Text="7.7"; Require(!editor.Validate!(), "typing disabled 7.7 cannot bypass the setting"); editor.Close();
        settings.SelectSection(2); Render((FrameworkElement)settings.Content,"v17-settings",764,582);
        ((CheckBox)fields["legacy77"]).IsChecked=true; Require(settings.Validate!() && settings.ApplyChanges!(),"enable 7.7 without restart");
        Require(Control<RadioButton>(main,"LegacyTab").Visibility==Visibility.Visible && Control<MenuItem>(main,"LegacyListMenu").Visibility==Visibility.Visible,"tab and import menu return together");
        Require((bool)Call(main,"SelectBaseInList","legacy")!,"existing legacy base returns after enabling");
        ((CheckBox)fields["legacy77"]).IsChecked=false; Require(settings.Validate!() && settings.ApplyChanges!(),"disable active legacy tab");
        Require(Control<RadioButton>(main,"BasesTab").IsChecked==true && catalog.Bases.Count==count,"disabling active 7.7 switches to 8.x without deleting registrations");
        Call(main,"SelectBaseInList","file");
        var inventory=(PlatformInventory)typeof(MainWindow).GetField("platformInventory",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
        var saved=inventory.Platforms.ToList();
        try
        {
            inventory.Platforms.Clear(); inventory.Platforms.AddRange(Enumerable.Range(1,1000).Select(i=>new PlatformInfo("8.3."+i,@"D:\SyntheticPlatforms", "x64")));
            foreach(bool designer in new[]{false,true})
            {
                var menu=(ContextMenu)Call(main,"CreateLaunchMenu",designer)!;
                Require(!menu.Items.OfType<MenuItem>().Any(i=>i.Header?.ToString()?.Contains("платформ") == true) && menu.Items.Count < 10,"launch menu size is independent of installed platform count");
                Render(menu,designer?"v17-designer-menu":"v17-enterprise-menu",360,250);
            }
        }
        finally { inventory.Platforms.Clear(); inventory.Platforms.AddRange(saved); }
        ((CheckBox)fields["legacy77"]).IsChecked=true; settings.ApplyChanges!(); settings.Close();
        Console.WriteLine("PASS v17 UI: 7.7 opt-in persists and guards tabs/import/shortcuts/properties; disabled registrations preserved; both launch menus stay compact with 1000 platforms.");
    }
    public static void LaunchButtons(string root)
    {
        var catalog=new Catalog { Bases=new() { new() { Id="tray-fixture", Name="Synthetic launch only", Location=@"D:\SyntheticTrayBase", Executable=Path.Combine(AppContext.BaseDirectory,"1cv8.exe") } } };
        catalog.Settings.AutoSnapshot=false; catalog.Settings.HotkeysEnabled=false;
        var store=new CatalogStore(Path.Combine(root,"tray-launch")); store.Save(catalog);
        var window=new MainWindow(store,catalog,false);
        string capture=Path.Combine(root,"tray-launch.json"); string? previous=Environment.GetEnvironmentVariable("VANBAZ_CAPTURE");
        Environment.SetEnvironmentVariable("VANBAZ_CAPTURE",capture);
        try
        {
            window.Show(); Call(window,"SelectBaseInList","tray-fixture");
            foreach(var pair in new[]{("EnterpriseButton","ENTERPRISE"),("DesignerButton","DESIGNER")})
            {
                if(File.Exists(capture)) File.Delete(capture);
                Call(window,"ShowFromTray"); Require(window.IsVisible,"tray restoration shows the window");
                Control<Button>(window,pair.Item1).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(()=>!window.IsVisible && File.Exists(capture));
                var received=JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
                var tray=(System.Windows.Forms.NotifyIcon)typeof(MainWindow).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
                Require(tray.Visible && received[0]==pair.Item2 && received.Contains(@"D:\SyntheticTrayBase"),"both real button handlers launch the stub and hide to a visible tray icon");
            }
            Call(window,"ShowFromTray");
            Require(window.IsVisible && catalog.History.Count==2,"window restores after both launches and records both modes");
        }
        finally { Environment.SetEnvironmentVariable("VANBAZ_CAPTURE",previous); window.Close(); }
        Console.WriteLine("PASS v17 launch: Enterprise and Designer buttons each start the test process, hide the actual window, expose the tray icon and restore; real 1C launches=0.");
    }
}
