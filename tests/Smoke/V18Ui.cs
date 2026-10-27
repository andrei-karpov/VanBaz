using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VanBaz;

internal static class V18Ui
{
    private static object? Call(object target,string name,params object?[] args) => target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    private static void Set(object target,string name,object? value) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(target,value);
    private static T Get<T>(object target,string name) => (T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Check(bool value,string message) { if(!value) throw new Exception("v18: "+message); }
    private static void Pump(int milliseconds=250)
    {
        var frame=new DispatcherFrame(); var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=(_,_)=>frame.Continue=false; timer.Start(); try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}
    }
    private static void WaitHidden(Window window)
    { var deadline=DateTime.UtcNow.AddSeconds(2); while(window.IsVisible && DateTime.UtcNow<deadline) Pump(50); }
    private static void Render(FormDialog form,string name,double scale=1)
    {
        form.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); var content=(FrameworkElement)form.Content; if(content is Panel panel) panel.Background=form.Background;
        int width=(int)form.Width-16,height=(int)form.Height-39;
        content.Measure(new(width,height));content.Arrange(new(0,0,width,height));content.UpdateLayout();
        var image=new RenderTargetBitmap((int)(width*scale),(int)(height*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(content);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
        string? output=Environment.GetEnvironmentVariable("VANBAZ_UI_ARTIFACTS");if(output!=null){using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);}
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var nested in Children<T>(child))yield return nested;} }
    public static void Run(MainWindow main,string root)
    {
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            string fractional="[Fractional]\nID=fractional\nFolder=/\nConnect=File=\"D:\\Fixture\";\nOrderInTree=341.333333333333\n\n[Folder]\nID=folder-fractional\nOrderInTree=3.5\n";
            string source=Path.Combine(root,"fractional.v8i");File.WriteAllText(source,fractional);
            var session=OfficialListSession.Load(source,Array.Empty<string>());var hydrated=session.Refresh(new Catalog());
            Check(hydrated.Bases.Single().Order==341.333333333333m && hydrated.Groups.Single().Order==3.5m,"official fractional positions survive import/canonicalization/hydration");
            var projected=CatalogReconciler.Project(hydrated);
            Check(projected["fractional"].Fields["OrderInTree"]=="341.333333333333" && projected["folder-fractional"].Fields["OrderInTree"]=="3.5","fractional ordering exports with invariant decimal separator under ru-RU");
            var decimalStore=new CatalogStore(Path.Combine(root,"decimal-order"));decimalStore.Save(hydrated);
            Check(decimalStore.Load().Bases.Single().Order==341.333333333333m,"fractional ordering survives catalog persistence");
            Check(File.ReadAllText(source)==fractional,"reading official list never rewrites fractional source");
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        Console.WriteLine("PASS v18 official ordering: fractional base/group positions retained through session hydration, invariant export and JSON persistence; source unchanged.");
        var displays=new[]{new ScreenArea("left",-2560,40,2560,1400,144),new ScreenArea("main",0,0,1920,1040,96),new ScreenArea("right",1920,-400,3840,2120,192)};
        var original=new SavedWindowPlacement(displays[0],new(-2300,160,1200,900));
        Check(WindowGeometry.Fit(original,displays).Bounds==original.Bounds,"negative monitor coordinates survive unchanged");
        var scaled=WindowGeometry.Fit(original,new[]{displays[0] with{Dpi=192}}).Bounds;
        Check(scaled.Width==1600 && scaled.Height==1200 && scaled.Left<0,"DPI preserves logical dimensions and monitor offset");
        var removed=WindowGeometry.Fit(original,displays.Skip(1).ToArray());
        Check(removed.Screen.Device=="main" && removed.Bounds.Left>=0 && removed.Bounds.Width==800,"disconnected monitor relocates with target DPI");
        var remote=WindowGeometry.Fit(new(displays[2],new(2100,-300,2400,1500)),new[]{new ScreenArea("rdp",0,0,800,560,96)});
        Check(remote.Bounds.Width==800 && remote.Bounds.Height==560 && remote.Bounds.Left==0 && remote.Bounds.Top==0,"small RDP work area clamps entire window");
        Check(WindowGeometry.Fit(original,displays).Bounds==original.Bounds,"temporary remote shrink does not mutate saved preferred size");
        Check(!WindowGeometry.Valid(new(displays[0] with{Dpi=double.NaN},original.Bounds)),"corrupt saved geometry rejected");
        var atTop=new SavedWindowPlacement(displays[1] with{Top=48,Height=992},new(100,100,735,630));
        for(int i=0;i<10;i++)Check(WindowGeometry.Fit(atTop,new[]{atTop.Screen}).Bounds==atTop.Bounds,"top taskbar causes no cumulative offset");

        var commands=new List<CommandPreset>{new(){Name="Очистка клиентского кэша",Arguments="/ClearCache"},new(){Name="Параметр конфигурации",Arguments="/C\"Значение с пробелом\""},new(){Name="Внешняя программа",Kind="Программа",Target="fixture.exe"}};
        var library=new ParameterExamples(()=>commands,(old,value)=>{if(old!=null)commands.Remove(old);if(value!=null)commands.Add(value);return true;});
        var args=new TextBox{Text="/Lru"};args.Select(args.Text.Length,0);
        var picker=library.CreateDialog(main,args);Render(picker,"v18-examples");
        var list=Children<ListBox>((DependencyObject)picker.Content).Single(x=>x.DisplayMemberPath=="Name");
        Check(list.Items.Count==2,"only parameter presets appear as launch examples");
        var fields=Get<Dictionary<string,Control>>(picker,"fields");
        ((TextBox)fields["search"]).Text="пробелом";Check(list.Items.Count==1,"search includes full argument value");
        Check(picker.Validate!() && args.Text=="/Lru /C\"Значение с пробелом\"","example inserts without destroying previous arguments");picker.Close();
        args.Text="/Lru REPLACE /ClearCache";args.Select(5,7);ParameterExamples.Insert(args,"/C\"тест\"");
        Check(args.Text=="/Lru /C\"тест\" /ClearCache","selection replacement respects quoting and whitespace");
        var entry=new BaseEntry{Name="Тестовая база",Location=@"D:\Synthetic",ExtraArguments="/Lru"};
        var editor=new BaseEditor(entry.Copy(),new(),new(),examples:library);editor.SelectSection(0);Render(editor,"v18-base-parameters",1.5);
        Check(Children<Button>((DependencyObject)editor.Content).Any(b=>b.Content?.ToString()=="Примеры параметров…"),"base launch section exposes examples");editor.Close();
        var profile=new LaunchOptionsDialog(entry,LaunchProfile.From(entry,false,"Авто"),Array.Empty<PlatformInfo>(),examples:library);profile.SelectSection(3);Render(profile,"v18-profile-parameters",2);
        Check(Children<Button>((DependencyObject)profile.Content).Any(b=>b.Content?.ToString()=="Примеры параметров…"),"launch profiles expose same example library");profile.Close();
        Check(entry.ExtraArguments=="/Lru" && commands.Count==3,"browsing and insertion never mutate base or templates");
        Check(AppBrand.Image!=null && AppBrand.TrayIcon.Handle!=IntPtr.Zero,"own window and tray logo load from packaged ICO");
        Console.WriteLine("PASS v18 geometry/examples: three monitors, negative origins, 96/144/192 DPI, missing monitor, small RDP area, top taskbar, no size drift; templates search/insert/selection and both parameter forms; own logo.");

        var catalog=new Catalog();catalog.Settings.HotkeysEnabled=false;catalog.Settings.AutoSnapshot=false;catalog.Settings.RestorePosition=true;
        var store=new CatalogStore(Path.Combine(root,"v18-placement"));store.Save(catalog);
        var window=new MainWindow(store,catalog,true);window.Show();Pump();
        try
        {
            window.Left=80;window.Top=60;window.Width=760;window.Height=550;Pump();
            var bounds=(PixelBounds)Call(window,"CurrentPixelBounds")!;
            for(int i=0;i<3;i++)
            {Call(window,"HideToTray");WaitHidden(window);Check(!window.IsVisible,"fade hides actual window");Call(window,"ShowFromTray");Pump();Check(window.WindowState==WindowState.Normal && Equals(Call(window,"CurrentPixelBounds"),bounds),"normal rectangle restored exactly, cycle "+i);}
            Call(window,"HideToTray");Call(window,"ShowFromTray");Pump(350);Check(window.IsVisible && window.Opacity==1,"rapid restore cancels pending fade");
            window.WindowState=WindowState.Maximized;Pump();
            Call(window,"HideToTray");Pump();Call(window,"ShowFromTray");Pump();Check(window.WindowState==WindowState.Maximized,"maximized state restored from tray");
            window.WindowState=WindowState.Normal;Pump();Check(Equals(Call(window,"CurrentPixelBounds"),bounds),"normal bounds preserved behind maximized state");
            catalog.Settings.MinimizeToTray=true;window.WindowState=WindowState.Maximized;Pump();window.WindowState=WindowState.Minimized;Pump();
            Check(!window.IsVisible,"minimize-to-tray hides window");Call(window,"ShowFromTray");Pump();Check(window.WindowState==WindowState.Maximized,"minimize from maximized remembers maximize");
            Call(window,"HideToTray");Pump();
            Set(window,"preferredPlacement",new SavedWindowPlacement(new("missing",-5000,0,4000,3000,192),new(-4900,100,2200,1600)));
            Set(window,"trayDisplays",null);Call(window,"RepairDisplay");Call(window,"ShowFromTray");Pump();
            Check(window.WindowState==WindowState.Maximized,"display repair preserves maximization while hidden");
            window.WindowState=WindowState.Normal;Pump();var repaired=(PixelBounds)Call(window,"CurrentPixelBounds")!;
            var available=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle).WorkingArea;
            Check(repaired.Left>=available.Left && repaired.Top>=available.Top && repaired.Left+repaired.Width<=available.Right && repaired.Top+repaired.Height<=available.Bottom,"missing monitor repair places whole normal window on real desktop");
            window.WindowState=WindowState.Maximized;Pump();Call(window,"HideToTray");Pump();Call(window,"SaveWindowPlacement");
            Check(catalog.Settings.Maximized && WindowGeometry.Valid(catalog.Settings.WindowPlacement),"closing from tray saves both normal placement and maximize state");
        }
        finally {window.Close();}
        var restarted=new MainWindow(store,store.Load(),true);restarted.Show();Pump();
        try{Check(restarted.WindowState==WindowState.Maximized,"relaunch honors saved maximized state");}finally{restarted.Close();}
        Console.WriteLine("PASS v18 native window: exact normal rectangle across 3 tray cycles, rapid fade cancellation, maximized and minimized restoration, disconnected-monitor repair, persistence and restart; no real database launch.");
    }
}
