using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VanBaz;

internal static class V19Ui
{
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,args);
    static T Get<T>(object target,string name) => (T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    static Dictionary<string,Control> Fields(FormDialog f) => (Dictionary<string,Control>)typeof(FormDialog).GetField("fields",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f)!;
    static void Check(bool pass,string why) { if(!pass) throw new Exception("v19: "+why); }
    static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    { for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var c=VisualTreeHelper.GetChild(parent,i);if(c is T t)yield return t;foreach(var x in Descendants<T>(c))yield return x;} }
    static void Render(Window window,string name,int width=764,int height=581,double scale=1)
    {
        if(window is FormDialog) window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var c=(FrameworkElement)window.Content; c.Measure(new(width,height));c.Arrange(new(0,0,width,height));c.UpdateLayout();
        var background=new DrawingVisual();using(var d=background.RenderOpen())d.DrawRectangle(window.Background??Brushes.White,null,new Rect(0,0,width,height));
        var bmp=new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);bmp.Render(background);bmp.Render(c);
        var output=Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS");if(output!=null){Directory.CreateDirectory(output);using var stream=File.Create(Path.Combine(output,name+".png"));var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));png.Save(stream);}
    }
    public static void Run(string scratch)
    {
        var store=new CatalogStore(Path.Combine(scratch,"v19")); var catalog=new Catalog();
        catalog.Settings.AutoSnapshot=false; catalog.Settings.EnableLegacy77=false;
        catalog.Settings.StartupMonitor="Disconnected-monitor";catalog.Settings.ScreenshotMonitor="Disconnected-monitor";
        catalog.Bases.Add(new(){Id="modern",Name="Демонстрационная база",Location=@"D:\Fixture",Version="8.3"});
        catalog.Bases.Add(new(){Id="legacy",Name="Архивная база 7.7",Location=@"D:\Legacy",Version="7.7"});
        catalog.Settings.BaseKeys["1"]="legacy";catalog.Settings.BaseKeys["2"]="missing-id";store.Save(catalog);
        var main=new MainWindow(store,catalog,true);new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        try
        {
            var settings=(FormDialog)Call(main,"CreateSettingsDialog")!;var fields=Fields(settings);new System.Windows.Interop.WindowInteropHelper(settings).EnsureHandle();
            Check(settings.Value("startupmonitor")=="Disconnected-monitor" && settings.Value("screenmonitor")=="Disconnected-monitor","disconnected monitor choices survive opening");
            Check(!fields["showkey"].IsEnabled,"disabled hotkeys disable their editors");
            settings.SetChoice("proxymode","Указать прокси");settings.SetChecked("proxywin",true);
            Check(fields["proxy"].IsEnabled&&!fields["proxyuser"].IsEnabled&&!fields["proxypass"].IsEnabled,"Windows proxy auth disables explicit credentials");
            settings.SetChecked("proxywin",false);Check(fields["proxyuser"].IsEnabled,"manual credentials recover");settings.SetChoice("proxymode","Без прокси");
            settings.SetChecked("usecolors",false);Check(!fields["favorite"].IsEnabled&&!fields["color8.3"].IsEnabled,"master color switch disables dependent color controls");
            settings.SetChecked("usecolors",true);settings.SetChecked("usecolor8.3",false);Check(!fields["color8.3"].IsEnabled&&fields["color8.4"].IsEnabled,"individual platform color toggle");
            settings.SetChecked("exist",false);Check(!fields["missing"].IsEnabled&&!fields["missingstrike"].IsEnabled,"missing-base formatting follows existence check");
            string validHeight=settings.Value("height");settings.SetText("height","NaN");Check(!settings.Validate!(),"non-finite row height rejected");settings.SetText("height",validHeight);
            settings.SetText("version","invalid");Check(!settings.Validate!(),"invalid default platform rejected");settings.SetText("version","8.3");
            settings.SetChoice("doubleclick","Конфигуратор");settings.SetChecked("doubleall",true);
            Check(settings.Validate!()&&settings.ApplyChanges!(),"settings persist through real Commit and CatalogStore");
            Check(!settings.Flag("doubleall")&&catalog.Bases.All(b=>b.DoubleClickAction=="Конфигуратор"),"apply-all executes once and resets checkbox");
            catalog.Bases[0].DoubleClickAction="Свойства";
            var keys=(FormDialog)Call(main,"CreateBaseKeysDialog")!;Check(keys.Value("1").Contains("legacy")&&keys.Value("2").Contains("missing-id"),"hidden and missing bindings remain visible");
            keys.SetChoice("3","Демонстрационная база [modern]");Check(keys.Validate!()&&keys.ApplyChanges!(),"nested key assignment commits");
            Check(catalog.Settings.BaseKeys["1"]=="legacy"&&catalog.Settings.BaseKeys["2"]=="missing-id"&&catalog.Settings.BaseKeys["3"]=="modern","editing another slot preserves hidden bindings");
            Check(settings.Validate!()&&settings.ApplyChanges!(),"parent save after nested save");
            Check(catalog.Settings.BaseKeys["3"]=="modern"&&catalog.Bases[0].DoubleClickAction=="Свойства","parent save does not undo nested edits or repeat apply-all");
            var saved=store.Load();Check(saved.Settings.StartupMonitor=="Disconnected-monitor"&&saved.Settings.DisabledPlatformColors.Contains("8.3"),"choice and color settings round-trip through disk");
            keys.SetChoice("1","Не назначено");Check(keys.Validate!()&&keys.ApplyChanges!()&&!catalog.Settings.BaseKeys.ContainsKey("1"),"explicit unbind works");
            var themes=(FormDialog)Call(main,"CreateThemesDialog",settings)!;
            themes.SetChoice("theme","Тёмный");Check(themes.Validate!()&&themes.ApplyChanges!()&&catalog.Settings.Theme=="Тёмный","theme Apply persists without closing");
            themes.SetChoice("theme","Голубой");themes.Close();Check(catalog.Settings.Theme=="Тёмный","cancel restores last applied theme");
            var baseEntry=catalog.Bases[0];var profile=LaunchProfile.From(baseEntry,true,"Авто");profile.AskLogin=true;profile.UseRepository=false;
            var launch=new LaunchOptionsDialog(baseEntry,profile,Array.Empty<PlatformInfo>(),true);var lf=Fields(launch);
            Check(!lf["user"].IsEnabled&&!lf["password"].IsEnabled&&!lf["wa"].IsEnabled&&!lf["repoPath"].IsEnabled,"launch dependencies initially disabled");
            launch.SetChecked("ask",false);launch.SetChecked("repo",true);Check(lf["user"].IsEnabled&&lf["repoPath"].IsEnabled,"launch dependencies toggle back on");
            var readonlyForm=(FormDialog)Call(main,"CreateOfficialListsDialog")!;Render(readonlyForm,"v19-official");
            Check(Get<Button>(readonlyForm,"cancel").Visibility==Visibility.Collapsed,"read-only dialog has only Close");
            var local=new FormDialog("Visibility fixture");local.Section("One");local.Check("one","one",true);local.Check("two","two",true);local.SetVisible("one",false);
            Check(Fields(local)["one"].Visibility==Visibility.Collapsed&&((FrameworkElement)Fields(local)["two"].Parent).Visibility==Visibility.Visible,"hiding checkbox leaves its section intact");local.Close();
            foreach(var theme in new[]{"Светло-серый","Тёмный"})
            {
                catalog.Settings.Theme=theme;Call(main,"ApplyPreferences",false);string suffix=theme=="Тёмный"?"dark":"light";
                for(int i=0;i<9;i++){settings.SelectSection(i);Render(settings,$"v19-settings-{i}-{suffix}");}
                settings.SelectSection(2);Render(settings,$"v19-small-200-{suffix}",484,381,2);
                // A complete line must wrap, rather than extend beyond the settings content width.
                var longLabel=Descendants<TextBlock>((DependencyObject)settings.Content).First(t=>t.Text.Contains("Применить выбранное действие"));
                Check(longLabel.TextWrapping==TextWrapping.Wrap&&longLabel.ActualWidth<300,"long checkbox caption wraps on small dialog");
                var themeForm=(FormDialog)Call(main,"CreateThemesDialog",settings)!;Render(themeForm,$"v19-themes-{suffix}");themeForm.SelectSection(1);Render(themeForm,$"v19-favorites-{suffix}");themeForm.Close();
                Render(keys,$"v19-keys-{suffix}");launch.SelectSection(1);Render(launch,$"v19-login-{suffix}");launch.SelectSection(4);Render(launch,$"v19-repository-{suffix}");
                var platformSettings=new PlatformSettingsWindow(new(){PlatformRoots=new(){@"D:\Platforms"}},new[]{new PlatformInfo("8.3.27.1",@"D:\Platforms\8.3.27.1\bin","x64")});
                Render(platformSettings,$"v19-platforms-small-{suffix}",624,401,1.5);
                var grids=Descendants<DataGrid>((DependencyObject)platformSettings.Content).ToArray();Check(grids.Length==2&&grids.All(g=>g.ActualHeight>75),"both platform tables stay accessible on small desktop");platformSettings.Close();
            }
            var scanCompletion=new TaskCompletionSource<PlatformScan>();
            var scanWindow=new PlatformSettingsWindow(new(){PlatformRoots=new(){@"D:\Fixture"}},Array.Empty<PlatformInfo>(),(_,_)=>scanCompletion.Task);
            Render(scanWindow,"v19-platform-scan");var scanButtons=Descendants<Button>((DependencyObject)scanWindow.Content).ToArray();
            Button ScanButton(string name)=>scanButtons.Single(b=>b.Content?.ToString()==name);
            ScanButton("Найти платформы").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!ScanButton("Добавить папку…").IsEnabled&&!ScanButton("Удалить корень").IsEnabled&&!ScanButton("Сохранить").IsEnabled&&ScanButton("Отменить поиск").IsEnabled,"platform edit and save controls locked during scan");
            scanCompletion.SetResult(new(new(),new()));System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check(ScanButton("Добавить папку…").IsEnabled&&ScanButton("Сохранить").IsEnabled&&!ScanButton("Отменить поиск").IsEnabled&&scanWindow.Result!=null,"platform scan restores controls and publishes its result");scanWindow.Close();
            readonlyForm.Close();launch.Close();keys.Close();settings.Close();
        }
        finally{main.Close();}
        Console.WriteLine("PASS v19 nested settings: real persistence, parent/child Apply and Cancel, hidden 7.7 and missing bindings, disconnected monitors, colors, proxy/login/repository dependencies, one-shot bulk update, read-only footer, small layouts, light/dark themes, 150/200% renders.");
    }
}
