using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VanBaz;

internal static class V10Ui
{
    public static void Render(string folder, MainWindow main, Catalog catalog)
    {
        string? output = Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS"); if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        var b = catalog.Bases.First(); var profile = LaunchProfile.From(b, false, "Толстый управляемый"); profile.Name = "Рабочий профиль"; profile.BaseId = b.Id; catalog.LaunchProfiles.Add(profile);
        string file = Path.Combine(folder, "RecoveryPreview.pfl"); File.WriteAllText(file,"before");
        var op = FileRecovery.Begin(Path.Combine(folder,"ui-recovery"), "Пример прерванного восстановления PFL", new Dictionary<string,byte[]?> { [file] = Encoding.UTF8.GetBytes("after") }); File.WriteAllText(file,"after");
        Window Invoke(string name, params object[] args) => (Window)typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, args)!;
        foreach (string theme in new[] { "Светло-серый", "Тёмный" })
        {
            ThemeService.Apply(new Preferences { Theme = theme }); string suffix = theme == "Тёмный" ? "dark" : "light";
            void Capture(Window window, string name, int width, int height)
            {
                try
                {
                    var content = (FrameworkElement)window.Content; content.Measure(new Size(width,height)); content.Arrange(new Rect(0,0,width,height)); content.UpdateLayout();
                    content.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded); content.UpdateLayout();
                    var background = new DrawingVisual(); using (var draw = background.RenderOpen()) draw.DrawRectangle(window.Background ?? Brushes.White,null,new Rect(0,0,width,height));
                    var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(background); bitmap.Render(content);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(output,name+"-"+suffix+".png")); png.Save(stream);
                }
                finally { window.Close(); }
            }
            Capture(new PlatformSettingsWindow(new Preferences { PlatformRoots = new() { @"D:\Платформы 1С", @"E:\Архив платформ" }, DisabledPlatformRoots = new() { @"E:\Архив платформ" } }, new[] { new PlatformInfo("8.3.23.1865", AppContext.BaseDirectory, "x64") { ThickClient = true, ThinClient = true } }), "v10-platforms", 1020,670);
            Capture(Invoke("CreateLaunchProfilesWindow",b), "v10-profile-manager", 780,390);
            catalog.Settings.BaseKeys["1"] = b.Id; Capture(Invoke("CreateBaseKeysDialog"), "base-keys", 820,540); catalog.Settings.BaseKeys.Clear();
            Capture(Invoke("CreateReconcileSourceDialog"), "reconcile-source", 780,420);
            Capture(Invoke("CreateOfficialListsDialog"), "official-lists", 900,620);
            Capture(Invoke("CreateRecoveryWindow",new List<RecoveryOperation>{op}), "v10-recovery", 920,590);
            var options = new LaunchOptionsDialog(b,profile,Array.Empty<PlatformInfo>(),true); options.SelectSection(3); Capture(options,"v10-arguments",820,580);
        }
        catalog.LaunchProfiles.Remove(profile);
    }
}
