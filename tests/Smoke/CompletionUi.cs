using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VanBaz;

internal static class CompletionUi
{
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target,args);
    static IEnumerable<T> Logical<T>(DependencyObject root) where T:DependencyObject
    {
        foreach(var value in LogicalTreeHelper.GetChildren(root)) if(value is DependencyObject child)
        { if(child is T typed) yield return typed; foreach(var item in Logical<T>(child)) yield return item; }
    }
    static IEnumerable<T> Visuals<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) { var child=VisualTreeHelper.GetChild(root,i); if(child is T typed)yield return typed; foreach(var item in Visuals<T>(child))yield return item; }
    }
    static T Named<T>(DependencyObject root,string name) where T:FrameworkElement => Logical<T>(root).Single(c=>c.Name==name);
    static void Click(ButtonBase button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    static bool Reject(Action action) { try {action();return false;} catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception){return true;} }
    static void Drain()=>Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
    static bool Pump(Func<bool> completed,int milliseconds=2500)
    {
        var frame=new DispatcherFrame();var until=DateTime.UtcNow.AddMilliseconds(milliseconds);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{if(completed()||DateTime.UtcNow>=until)frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);timer.Stop();return completed();
    }
    static void Render(Window window,string path,int width=740,int height=520)
    {
        window.SizeToContent=SizeToContent.Manual;window.Width=width;window.Height=height;if(!window.IsVisible)window.Show();Drain();window.UpdateLayout();
        var content=(FrameworkElement)window.Content;content.UpdateLayout();int actualWidth=(int)Math.Ceiling(window.ActualWidth),actualHeight=(int)Math.Ceiling(window.ActualHeight);
        var bitmap=new RenderTargetBitmap(actualWidth,actualHeight,96,96,PixelFormats.Pbgra32);var background=new DrawingVisual();
        using(var dc=background.RenderOpen())dc.DrawRectangle(window.Background??Brushes.White,null,new Rect(0,0,actualWidth,actualHeight));
        bitmap.Render(background);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder); var observations=new List<object>();int failed=0;
        void Check(string id,bool pass,string evidence){observations.Add(new{id,status=pass?"PASS":"FAIL",method="WPF events / synthetic filesystem / injected callback",evidence});Console.WriteLine((pass?"PASS ":"FAIL ")+id+": "+evidence);if(!pass)failed++;}
        void Scenario(string id,Action action){try{action();}catch(Exception ex){Check(id,false,ex.ToString());}}
        Exception? fatal=null;
        var thread=new Thread(()=>
        {
            Application? app=null;MainWindow? main=null;
            try
            {
                app=UiRegression.CreateApplication();
                var catalog=new Catalog{Bases=new(){new(){Id="row-a",Name="Учебная А",Location=Path.Combine(folder,"BaseA"),Favorite=true},new(){Id="row-b",Name="Учебная Б",Location=Path.Combine(folder,"BaseB")}},Groups=new(){new(){Path="Учебные/Вложенные"}}};
                catalog.Settings.InterfaceMode="Разработчик";catalog.Settings.AutoSnapshot=false;catalog.Settings.HotkeysEnabled=false;catalog.Settings.CloseToTray=false;catalog.Settings.EscapeToTray=false;catalog.Settings.RestorePosition=false;catalog.Settings.ShowLaunchButtonInRow=true;
                var store=new CatalogStore(Path.Combine(folder,"Catalog"));store.Save(catalog);main=new MainWindow(store,catalog,true);new WindowInteropHelper(main).EnsureHandle();
                Scenario("A11",()=>Versions(folder,Check));
                Scenario("A12",()=>Creation(folder,Check));
                Scenario("A15",()=>Behavior(folder,Check));
                Scenario("A14",()=>Cache(folder,Check));
                Scenario("A16",()=>AgentVersion(folder,Check));
                Scenario("A04",()=>Administration(folder,Check));
                Scenario("A04-ui",()=>AdministrationUi(main,folder,Check));
                Scenario("A13/V08",()=>ErrorAndChooser(folder,Check));
                Scenario("R06",()=>ImportedSecrets(folder,Check));
                Scenario("V07",()=>ExternalLists(folder,Check));
                Scenario("V01",()=>Summary(folder,Check));
                Scenario("V04/V10",()=>Pickers(folder,Check));
                Scenario("V06/V09",()=>PortableThemes(main,folder,Check));
                Scenario("P04",()=>OnlineUpdate(main,catalog,folder,Check));
                Scenario("V14",()=>WebOptions(main,folder,Check));
                Scenario("V16",()=>BaseReleases(folder,Check));
                Scenario("V02/V03/V11",()=>AdditionalUi(main,catalog,folder,Check));
                Scenario("UI-events",()=>Interaction(main,catalog,folder,Check));
            }
            catch(Exception ex){fatal=ex;Check("harness",false,ex.ToString());}
            finally{main?.Close();app?.Shutdown();File.WriteAllText(Path.Combine(folder,"results.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        Console.WriteLine($"Completion aggregate: {observations.Count} checks; failures={failed}; real1C/COM/services=0.");
        if(failed>0||fatal!=null)throw new Exception("Completion acceptance failed",fatal);
    }
    static void Versions(string root,Action<string,bool,string> check)
    {
        string data=Path.Combine(root,"VersionsData");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"keep.txt"),"user settings fixture");
        string source=AppContext.BaseDirectory;string saved=ProgramVersions.Archive(source,data);var versions=ProgramVersions.List(data);var prepared=ProgramVersions.Validate(saved);var stage=ProgramVersions.Stage(prepared,data);
        string zip=Path.Combine(root,"portable.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){foreach(string file in AppUpdateService.Files)archive.CreateEntryFromFile(Path.Combine(source,file),file);using var writer=new StreamWriter(archive.CreateEntry("README.txt").Open());writer.Write("Requires .NET Desktop Runtime 8");}
        var imported=AppUpdateService.Prepare(zip,Path.Combine(root,"Prepared"));
        check("A11-valid",versions.Count==1&&versions[0].Valid&&stage.Folder!=saved&&imported.Version==prepared.Version&&File.ReadAllText(Path.Combine(data,"keep.txt"))=="user settings fixture","Archive/validate/stage and public portableZIP withREADME accepted; Data sentinel preserved; installer not run.");
        File.AppendAllText(Path.Combine(saved,"VanBaz.runtimeconfig.json"),"tamper");
        check("A11-corrupt",Reject(()=>ProgramVersions.Validate(saved))&&!ProgramVersions.List(data).Single().Valid&&File.Exists(Path.Combine(stage.Folder,"VanBaz.dll")),"Changed archived bytes rejected; independent validated stage remains intact.");
    }
    static void Creation(string root,Action<string,bool,string> check)
    {
        string templates=Path.Combine(root,"Templates");Directory.CreateDirectory(templates);string cf=Path.Combine(templates,"training.cf");File.WriteAllText(cf,"synthetic CF");
        string mft=Path.Combine(templates,"training.mft");File.WriteAllText(mft,"Name=Учебная\nVendor=Тест\nVersion=1.0\nAppVersion=8.3.23.1\n[Template]\nSource=training.cf\nCatalog=Тест\n");
        var generic=ConfigurationTemplates.ReadManifest(mft,"8.3").Single();var old=ConfigurationTemplates.ReadManifest(mft,"8.3.9.1").Single();var current=ConfigurationTemplates.ReadManifest(mft,"8.3.27.1").Single();
        check("A12-template",generic.Available&&!old.Available&&current.Available&&ConfigurationTemplates.Scan(new[]{templates},"8.3",CancellationToken.None).Count==1,"Synthetic MFT/CF: generic8.3 stays eligible with minimum-build notice; old full build excluded; current full build allowed.");
        var server=new BaseEntry{Id="created",Name="Новая учебная",Kind="Server",Location="training-cluster:1541/training",Version="8.3",Executable=Path.Combine(AppContext.BaseDirectory,"1cv8.exe")};
        var options=new ServerCreationOptions("PostgreSQL","training-db","training","tester","not-a-real-secret");string result=Path.Combine(root,"create.result");
        var plan=BaseCreation.Build(server,cf,"ru_RU",Path.Combine(root,"create.log"),result,Array.Empty<PlatformInfo>(),options);
        var connection=InfobaseConnection.Parse(plan.ArgumentList[1]);File.WriteAllText(result,"1");bool failed=!BaseCreation.Succeeded(server,0,result);File.WriteAllText(result,"0");
        check("A12-server",plan.ArgumentList[0]=="CREATEINFOBASE"&&connection["DBMS"]=="PostgreSQL"&&connection["DBSrvr"]=="training-db"&&failed&&!BaseCreation.Succeeded(server,1,result)&&BaseCreation.Succeeded(server,0,result)&&!LaunchService.Preview(plan).Contains("not-a-real-secret"),"ServerCREATEINFOBASE plan preserves DBMS/host; result/exit failures rejected; preview masks synthetic secret. Command never started; catalog not registered by this scenario.");
    }
    static void Behavior(string root,Action<string,bool,string> check)
    {
        var cat=new Catalog();cat.Settings.AutoSnapshot=true;cat.Settings.AutoSnapshotDays=3;var store=new CatalogStore(Path.Combine(root,"AutoSnapshots"));store.Save(cat);DateTime now=new(2026,10,28,12,0,0);
        bool first=SnapshotService.SaveAutomatic(store,cat,now),early=SnapshotService.SaveAutomatic(store,cat,now.AddDays(2)),due=SnapshotService.SaveAutomatic(store,cat,now.AddDays(3));
        check("A15a",first&&!early&&due&&SnapshotService.List(store).Count==2&&cat.Settings.LastAutoSnapshot==now.AddDays(3),"Automatic snapshot interval is counted from last successful automatic snapshot; due only at3days boundary.");
        var broken=new CatalogStore(Path.Combine(root,"FailedSnapshot"));var failedCatalog=new Catalog();failedCatalog.Settings.AutoSnapshot=true;broken.Save(failedCatalog);File.WriteAllText(Path.Combine(broken.Folder,"Snapshots"),"blocks directory creation");
        check("A15a-failure",Reject(()=>SnapshotService.SaveAutomatic(broken,failedCatalog,now))&&failedCatalog.Settings.LastAutoSnapshot==null,"Failed snapshot write does not advance last-success timestamp; only synthetic path used.");
        var record=new LaunchRecord{BaseId="a",Mode="Предприятие",ProcessId=42,ProcessStarted=now};
        check("A15b",LaunchBehavior.Running(new[]{record},"a","Предприятие",_=>now)==record&&LaunchBehavior.Running(new[]{record},"b","Предприятие",_=>now)==null&&LaunchBehavior.Running(new[]{record},"a","Конфигуратор",_=>now)==null&&LaunchBehavior.Running(new[]{record},"a","Предприятие",_=>now.AddSeconds(1))==null,"Repeated-launch identity checks base, mode and start time; PID reuse rejected, no process activation performed.");
        var b=new BaseEntry{Name="Учебная",Location=Path.Combine(root,"Connection"),Executable=Path.Combine(AppContext.BaseDirectory,"1cv8.exe")};
        string[] Keys(string mode)=>LaunchService.Build(b,false,"Авто",Array.Empty<PlatformInfo>(),connectionMode:mode).ArgumentList.ToArray();
        bool modes=Keys("Авто").Contains("/F")&&Keys("/F /S /WS").Contains("/F")&&Keys("/IBName").Contains("/IBName")&&Keys("/IBConnectionString").Contains("/IBConnectionString");
        b.V8iFields["Connect"]="File=\""+b.Location+"\";Locale=\"ru_RU\";";
        check("A15d",modes&&Keys("Авто").Contains("/IBConnectionString")&&Reject(()=>Keys("/F /S /WS")),"All four command builders differ; additional connection fields use IBConnectionString and are not silently lost in direct mode. Official-name uniqueness belongs to caller, not asserted here.");
        string official=Path.Combine(root,"name-launch.v8i");string listing="["+b.Name+"]\nID="+b.Id+"\nConnect=File=\""+b.Location+"\";\n";File.WriteAllText(official,listing);
        var session=OfficialListSession.Load(official,Array.Empty<string>());session.ValidateLaunchName(b);
        File.AppendAllText(official,"\n["+b.Name+"]\nID=second\nConnect=File=\""+Path.Combine(root,"Other")+"\";\n");
        check("A15d-name",Reject(()=>OfficialListSession.Load(official,Array.Empty<string>()).ValidateLaunchName(b)),"Explicit temporary official-list fixture rejects duplicate names; no standard sources read or changed.");
    }
    static void ExternalLists(string root,Action<string,bool,string> check)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);var encoding=Encoding.GetEncoding(1251);string file=Path.Combine(root,"external.v8i");
        string text="; комментарий\r\n[Учебная]\r\nConnect=File=\"D:\\Old\\Base\";\r\nID=file-id\r\nFolder=/Группа\r\nUnknownField=сохранить\r\n\r\n[Сервер]\r\nConnect=Srvr=\"server\";Ref=\"db\";\r\nID=server-id\r\n";
        File.WriteAllText(file,text,encoding);byte[] before=File.ReadAllBytes(file);var store=new CatalogStore(Path.Combine(root,"ExternalJournal"));store.Save(new());
        var plan=ExternalListRelocation.Prepare(file,@"D:\Old",@"E:\New");bool guarded=Reject(()=>ExternalListRelocation.Apply(plan,store));File.AppendAllText(file,"; intervening\r\n",encoding);byte[] changed=File.ReadAllBytes(file);bool changedRejected=Reject(()=>ExternalListRelocation.Apply(plan,store,true))&&changed.SequenceEqual(File.ReadAllBytes(file));
        File.WriteAllBytes(file,before);plan=ExternalListRelocation.Prepare(file,@"D:\Old",@"E:\New");var applied=ExternalListRelocation.Apply(plan,store,true);string output=encoding.GetString(File.ReadAllBytes(file));
        check("V07-paths",guarded&&changedRejected&&output.Contains(@"E:\New\Base")&&output.Contains("Srvr=\"server\";Ref=\"db\";")&&output.Contains("; комментарий")&&output.Contains("UnknownField=сохранить")&&!output.Contains("OrderInTree")&&File.ReadAllBytes(applied.Backup).SequenceEqual(before),"External CP1251 list: explicit-write guard, concurrent-change rejection, path-only change, comments/unknown field/implicit order preserved; backup matches original bytes.");
        var names=ExternalListRelocation.PrepareNames(file,s=>"X"+s,true,true);ExternalListRelocation.Apply(names,store,true);output=encoding.GetString(File.ReadAllBytes(file));
        check("V07-names",output.Contains("[XУчебная]")&&output.Contains("Folder=/XГруппа")&&!output.Contains("OrderInTree"),"Separate-list name/group formatting applies without importing into catalog or manufacturing ordering fields.");
        byte[] named=File.ReadAllBytes(file),pendingBytes=encoding.GetBytes(output+"; pending change\r\n");var pending=FileRecovery.Begin(Path.Combine(root,"PendingRelocation"),"Synthetic interrupted list change",new Dictionary<string,byte[]?>{{file,pendingBytes}});File.WriteAllBytes(file,pendingBytes);FileRecovery.Restore(pending);
        check("V07-recovery",File.ReadAllBytes(file).SequenceEqual(named),"Common recovery journal restores exact CP1251 bytes after a synthetic interrupted external-list write; completed transactions are not silently rolled back.");
    }
    static void Cache(string root,Action<string,bool,string> check)
    {
        string profile=Path.Combine(root,"FakeProfile"),other=Path.Combine(root,"OtherProfile");Directory.CreateDirectory(Path.Combine(profile,"AppData"));Directory.CreateDirectory(Path.Combine(other,"AppData"));
        string cacheRoot=CacheService.ProfileRoots(profile,true,false,"1cv8").Single();string otherRoot=CacheService.ProfileRoots(other,true,false,"1cv8").Single();string roaming=CacheService.ProfileRoots(profile,false,true,"1cv82").Single();
        string id="11111111-1111-1111-1111-111111111111",source=Path.Combine(cacheRoot,id);Directory.CreateDirectory(Path.Combine(source,"Config"));Directory.CreateDirectory(Path.Combine(source,"SICache"));Directory.CreateDirectory(Path.Combine(otherRoot,id));Directory.CreateDirectory(Path.Combine(roaming,id));
        File.WriteAllText(Path.Combine(source,"Config","cached"),"old cache");File.WriteAllText(Path.Combine(source,"prefs.pfl"),"profile settings");File.WriteAllText(Path.Combine(source,"unknown.txt"),"unknown preserved");File.WriteAllText(Path.Combine(otherRoot,id,"sentinel"),"other profile");File.WriteAllText(Path.Combine(roaming,id,"sentinel"),"roaming");
        var entry=new BaseEntry{Id="{"+id+"}",Name="Cache fixture",Location=Path.Combine(root,"NoBase")};var folder=CacheService.Find(new[]{cacheRoot},new[]{entry}).Single();var plan=CacheCleanup.Plan(folder);CacheCleanup.ClearSelective(folder);
        var quarantine=CacheService.Find(new[]{cacheRoot},new[]{entry},true).Single();Directory.CreateDirectory(Path.Combine(source,"Config"));File.WriteAllText(Path.Combine(source,"Config","fresh"),"new cache");bool refuses=Reject(()=>CacheService.Move(quarantine,true));
        check("A14-preserve",plan.Paths.Length==2&&plan.Preserved.Length==2&&File.ReadAllText(Path.Combine(source,"prefs.pfl"))=="profile settings"&&File.ReadAllText(Path.Combine(source,"unknown.txt"))=="unknown preserved"&&refuses&&File.Exists(Path.Combine(source,"Config","fresh"))&&File.Exists(Path.Combine(otherRoot,id,"sentinel"))&&File.Exists(Path.Combine(roaming,id,"sentinel")),"Selective quarantine affects only chosen Local/version/profile; profile/unknown files and unselected roots preserved; restore refuses newly created cache.");
        Directory.Move(Path.Combine(source,"Config"),Path.Combine(source,"new-cache-saved"));CacheService.Move(quarantine,true);
        string outside=Path.Combine(root,"Outside",id);Directory.CreateDirectory(outside);
        check("A14-boundaries",File.ReadAllText(Path.Combine(source,"Config","cached"))=="old cache"&&Reject(()=>CacheCleanup.Plan(new(cacheRoot,outside,id,"outside")))&&Reject(()=>CacheService.Move(new(cacheRoot,outside,id,"outside"),false)),"Restore returns old selected cache after resolving collision; selective and rough plans reject a path outside chosen root.");
        string reparseRoot=Path.Combine(Directory.GetParent(root)!.FullName,"ReparseFixture"),reparseId="33333333-3333-3333-3333-333333333333",junction=Path.Combine(reparseRoot,reparseId);bool isJunction=(File.GetAttributes(junction)&FileAttributes.ReparsePoint)!=0;
        check("A14-reparse",isJunction&&Reject(()=>CacheCleanup.Plan(new(reparseRoot,junction,reparseId,"junction")))&&Reject(()=>CacheService.Move(new(reparseRoot,junction,reparseId,"junction"),false))&&CacheService.Find(new[]{reparseRoot},Array.Empty<BaseEntry>()).Count==0&&File.ReadAllText(Path.Combine(reparseRoot,"Target","sentinel")).Trim()=="fixture target","Prepared workspace-only junction is excluded from discovery and rejected by both move planners; its fictional target remains untouched.");
        string roughId="22222222-2222-2222-2222-222222222222",roughPath=Path.Combine(cacheRoot,roughId);Directory.CreateDirectory(roughPath);File.WriteAllText(Path.Combine(roughPath,"whole"),"whole directory");CacheService.Move(new(cacheRoot,roughPath,roughId,"rough"),false);var rough=CacheService.Find(new[]{cacheRoot},Array.Empty<BaseEntry>(),true).Single();CacheService.Move(rough,true);
        check("A14-rough",File.ReadAllText(Path.Combine(roughPath,"whole"))=="whole directory"&&CacheService.ProfileRoots(profile,true,false,"1cv8").Length==1,"Rough quarantine/restore preserves whole selected directory; explicit root filter remains singular.");
        var catalog=new Catalog{Bases=new(){entry}};catalog.Settings.InterfaceMode="Разработчик";catalog.Settings.AutoSnapshot=false;catalog.Settings.HotkeysEnabled=false;var store=new CatalogStore(Path.Combine(root,"CacheUiCatalog"));store.Save(catalog);var main=new MainWindow(store,catalog,true);new WindowInteropHelper(main).EnsureHandle();Call(main,"SelectBaseInList",entry.Id);
        var window=(Window)Call(main,"CreateCacheWindow",new[]{profile},(Func<bool>)(()=>false),(Action<CacheFolder,bool,bool>)((_,_,_)=>throw new Exception("Executor must not run")))!;
        var scope=Logical<ComboBox>(window).Single(c=>c.Items.Cast<object>().Any(i=>i.ToString()=="Только базы текущего списка"));scope.SelectedItem="Только базы текущего списка";var folders=Named<ListBox>(window,"CacheFolders");Click(Logical<Button>(window).Single(b=>b.Content?.ToString()=="Выбранная база"));
        check("R04",folders.Items.Count>0&&folders.Items.Cast<CacheFolder>().All(f=>f.BaseId==id)&&folders.SelectedItems.Count==folders.Items.Count,"Braced catalog GUID matches normalized cache folder in current-list scope and selected-base action.");window.Close();main.Close();
    }
    static void AgentVersion(string root,Action<string,bool,string> check)
    {
        var server=new BaseEntry{Kind="Server",Location="fixture-host:2541/training"};var service=new ServerService("fixture","Fixture agent","Работает","account",Path.Combine(root,"configured","ragent.exe"),"-srvc -regport 1541","x64","8.3.28.1"){RunningExecutable=Path.Combine(root,"active","ragent.exe"),RunningArguments="-srvc -regport 2541",RunningVersion="8.3.27.1"};
        var match=ServerVersionProbe.Evaluate(server,"8.3.27.1",new[]{service},"fixture-host");var mismatch=ServerVersionProbe.Evaluate(server,"8.3.26.1",new[]{service},"fixture-host");var stopped=ServerVersionProbe.Evaluate(server,"8.3.27.1",new[]{service with{State="Остановлена"}},"fixture-host");
        var ambiguous=ServerVersionProbe.Evaluate(server,"8.3.27.1",new[]{service,service with{Name="another"}},"fixture-host");server.Location="remote-host:2541/training";var remote=ServerVersionProbe.Evaluate(server,"8.3.27.1",new[]{service},"fixture-host");
        server.Location="fixture-host:2541/training";var unknown=ServerVersionProbe.Evaluate(server,"8.3.28.1",new[]{service with{RunningExecutable=null,RunningArguments=null,RunningVersion=null}},"fixture-host");
        check("A16",match.Verified&&match.Compatible&&match.Version=="8.3.27.1"&&mismatch.Verified&&!mismatch.Compatible&&!stopped.Verified&&!ambiguous.Verified&&!remote.Verified&&remote.Message.Contains("Удалённый")&&!unknown.Verified,"Injected runtime image/port/version override different configured values; absent runtime binding, stopped service and ambiguity remain unknown; remote agent not verified. No SCM/WMI call.");
    }
    static void Administration(string root,Action<string,bool,string> check)
    {
        string windows=Path.Combine(root,"FakeWindows"),bin=Path.Combine(root,"AdminPlatform");Directory.CreateDirectory(bin);foreach(string dir in new[]{"System32","SysWOW64","Sysnative"}){Directory.CreateDirectory(Path.Combine(windows,dir));foreach(string exe in new[]{"regsvr32.exe","sc.exe"})File.WriteAllText(Path.Combine(windows,dir,exe),"fixture");}foreach(string dll in new[]{"radmin.dll","comcntr.dll","ragent.exe"})File.WriteAllText(Path.Combine(bin,dll),"fixture");
        var platform=new PlatformInfo("8.3.27.1",bin,"x64");var register=ServerAdministration.Registration(platform,"radmin.dll",false,true,windows,true,false);var unregister=ServerAdministration.Registration(platform,"radmin.dll",true,true,windows,true,true,(_,_)=>true);
        int calls=0;bool registration=true;Func<System.Diagnostics.ProcessStartInfo,Task<int>> run=_=>{calls++;registration=false;return Task.FromResult(0);};ServerAdministration.Execute(unregister,run,()=>Array.Empty<ServerService>(),(_,_)=>registration).GetAwaiter().GetResult();int before=calls;
        bool staleRegistration=Reject(()=>ServerAdministration.Execute(unregister,run,()=>Array.Empty<ServerService>(),(_,_)=>false).GetAwaiter().GetResult());
        check("A04-registration",register.Process.FileName.Contains("Sysnative")&&register.Process.Verb=="runas"&&unregister.Process.ArgumentList.Contains("/u")&&staleRegistration&&calls==before,"Injected unregister validates before and after; stale registration prevents executor. x64-from-x86 plan uses Sysnative and elevation; no registry or regsvr32 execution.");
        var service=new ServerService("FixtureAgent","Test","Остановлена","test",Path.Combine(bin,"ragent.exe"),"-srvc -regport 2541 -d \"D:\\Fixture\"","x64","8.3.27.1");var config=ServerAdministration.Service(service,"config",windows,true,true,platform);bool stale=Reject(()=>ServerAdministration.Execute(config,run,()=>new[]{service with{Arguments="-srvc -regport 3541"}},(_,_)=>true).GetAwaiter().GetResult());
        var start=ServerAdministration.Service(service,"start",windows,true,true);int attempts=0;bool failed=Reject(()=>ServerAdministration.Execute(start,_=>{attempts++;return Task.FromResult(5);},()=>new[]{service},(_,_)=>true).GetAwaiter().GetResult());
        int read=0;ServerAdministration.Execute(start,_=>Task.FromResult(0),()=>new[]{service with{State=++read==1?"Остановлена":"Работает"}},(_,_)=>true).GetAwaiter().GetResult();
        check("A04-service",stale&&calls==before&&config.Process.ArgumentList.Last().Contains(service.Arguments)&&failed&&attempts==1&&read==2,"Stale service arguments reject config before executor; original agent arguments preserved; failed start throws after one stub call, successful start requires observed running state. This tests single-command executor, not live SCM/UAC.");
    }
    static void ErrorAndChooser(string root,Action<string,bool,string> check)
    {
        string copied="",link="";var error=new ErrorDialog("Fixture error",new IOException("Synthetic diagnostic"),false,false,s=>copied=s,s=>link=s);Click(Named<Button>(error,"CopyError"));Drain();Click(Named<Button>(error,"ReportError"));
        check("A13",copied.Contains("Synthetic diagnostic")&&link=="https://github.com/andrei-karpov/VanBaz/issues/new"&&!Logical<Button>(error).Any(b=>b.Name=="RestartAfterError"),"Error buttons use injected copy/open callbacks; report URL contains no diagnostic data, closing-path dialog has no restart action. Fatal process path reviewed separately.");Render(error,Path.Combine(root,"error.png"),620,300);error.Close();
        var chooser=new NewBaseSelectionWindow("File","8.3",true);Named<RadioButton>(chooser,"NewBaseServer").IsChecked=true;bool legacyDisabled=!Named<Button>(chooser,"NewBaseVersion77").IsEnabled;chooser.Dispatcher.BeginInvoke(()=>Click(Named<Button>(chooser,"NewBaseVersion83")),DispatcherPriority.ApplicationIdle);bool? result=chooser.ShowDialog();
        var cancel=new NewBaseSelectionWindow("Web","8.3",true,true);bool webExcluded=!Logical<RadioButton>(cancel).Any(b=>b.Name=="NewBaseWeb");cancel.Dispatcher.BeginInvoke(()=>Click(Named<Button>(cancel,"CancelNewBase")),DispatcherPriority.ApplicationIdle);bool? canceled=cancel.ShowDialog();
        check("V08",legacyDisabled&&result==true&&chooser.SelectedKind=="Server"&&chooser.SelectedVersion=="8.3"&&webExcluded&&canceled!=true&&cancel.SelectedVersion=="","New-base chooser disables7.7 for server; real modal click selects8.3; creation omitsWeb and Cancel yields no selected version.");
    }
    static void AdministrationUi(MainWindow main,string root,Action<string,bool,string> check)
    {
        string bin=Path.Combine(root,"AdminPlatform");var platform=new PlatformInfo("8.3.27.1",bin,"x64");var current=new ServerService("FixtureAgent","Test","Работает","test",Path.Combine(bin,"ragent.exe"),"-regport 2541","x64","8.3.27.1");var called=new List<string>();int reads=0,consoles=0;bool approve=false;
        Func<IReadOnlyList<ServerService>> read=()=>{reads++;return new[]{current};};Func<ServerAdminCommand,Task> execute=command=>{called.Add(command.Kind);if(command.Kind=="stop"){current=current with{State="Остановлена"};return Task.CompletedTask;}return Task.FromException(new IOException("Injected registration failure"));};var owner=new Window{Width=850,Height=520,Title="Administration fixture"};var panel=(FrameworkElement)Call(main,"CreateServerAdministrationPanel",owner,(Action)(()=>{}),read,execute,(Action<PlatformInfo>)(_=>consoles++),(Func<string,bool>)(_=>approve))!;owner.Content=panel;owner.Show();bool loaded=Pump(()=>Named<DataGrid>(owner,"ServerServicesGrid").Items.Count==1);var grid=Named<DataGrid>(owner,"ServerServicesGrid");grid.SelectedIndex=0;var version=Named<ComboBox>(owner,"AdministrationPlatform");version.ItemsSource=new[]{platform};version.SelectedIndex=0;
        foreach(var box in Logical<CheckBox>(owner)) if(box.Content?.ToString() is "Остановить выбранные службы" or "Зарегистрировать radmin.dll" or "Запустить выбранные службы" or "Открыть MMC")box.IsChecked=true;
        var run=Logical<Button>(owner).Single(b=>b.Content?.ToString()=="Выполнить отмеченное…");Click(run);bool canceled=called.Count==0;approve=true;Click(run);bool finished=Pump(()=>reads>=2&&run.IsEnabled);string status=Named<TextBlock>(owner,"ServerAdministrationStatus").Text;
        check("A04-chain",loaded&&canceled&&finished&&called.SequenceEqual(new[]{"stop","register"})&&consoles==0&&status.Contains("Выполнено:")&&status.Contains("FixtureAgent")&&status.Contains("Injected registration failure")&&grid.SelectedItem is ServerService{State:"Остановлена"},"Actual WPF action: declined plan invokes nothing; stop succeeds then injected registration failure stops start/MMC, reports completed service and failure, refreshes service row while retaining selection.");Render(owner,Path.Combine(root,"server-administration.png"),850,520);owner.Close();
    }
    static void OnlineUpdate(MainWindow main,Catalog catalog,string root,Action<string,bool,string> check)
    {
        catalog.Settings.AppUpdateUrl="https://example.invalid/channel.json";catalog.Settings.AutoDownloadAppUpdates=true;int downloads=0;
        string archived=ProgramVersions.Archive(AppContext.BaseDirectory,Path.Combine(root,"UpdateFixture"));var prepared=ProgramVersions.Validate(archived);
        Func<string,CancellationToken,Task<AppRelease>> feed=(_,_)=>Task.FromResult(new AppRelease{Version="99.0.0",PackageUrl="https://example.invalid/package.zip",Notes="Fixture"});
        Func<AppRelease,string,CancellationToken,Task<PreparedAppUpdate>> download=(_,_,_)=>{downloads++;return Task.FromResult(prepared);};
        Window Create()=>(Window)Call(main,"CreateOnlineUpdateWindow",false,feed,download,1)!;
        var cancel=Create();Click(Named<Button>(cancel,"CheckAppRelease"));bool shown=Named<TextBlock>(cancel,"AppUpdateCountdown").Text.Length>0;Click(Named<Button>(cancel,"CancelAutoDownload"));bool stayedZero=Pump(()=>downloads>0,1150)==false;
        check("P04-cancel",shown&&stayedZero&&Named<TextBlock>(cancel,"AppUpdateCountdown").Text=="","Cancel countdown prevents downloader callback; no network or installer.");cancel.Close();
        var auto=Create();Click(Named<Button>(auto,"CheckAppRelease"));bool downloaded=Pump(()=>downloads==1,2200);bool ready=Named<Button>(auto,"InstallAppRelease").Content?.ToString()=="Установить…";
        check("P04-download",downloaded&&ready&&main.IsVisible==false,"Countdown downloads through injected callback and only offers install; main process remains and installer never starts.");DateTime before=DateTime.UtcNow;Click(Named<Button>(auto,"PostponeAppUpdate"));check("P04-postpone",catalog.Settings.AppUpdateRemindAfterUtc>=before.AddHours(23)&&catalog.Settings.AppUpdateRemindAfterUtc<=DateTime.UtcNow.AddHours(25),"Postpone action persists next reminder approximately24hours later in synthetic catalog.");
    }
    static void ImportedSecrets(string root,Action<string,bool,string> check)
    {
        string file=Path.Combine(root,"private-fixture.v8i"),connect="ws=\"https://user:url-fixture@example.invalid/training\";Wsp=\"wsp-fixture\";Wsppwd=\"proxy-fixture\";Pwd=\"pwd-fixture\";Locale=\"ru_RU\";Custom=\"keep;quoted\";";
        string text="[Учебная Web]\nID=privacy-fixture\nConnect="+connect+"\nPassword=outer-fixture\nAdditionalParameters=/N \"Учебный\" /P args-fixture\nUnknownField=keep\n";File.WriteAllText(file,text);byte[] original=File.ReadAllBytes(file);var imported=V8iImporter.Read(file);var entry=imported.Entries.Single();var catalog=new Catalog{Bases=imported.Entries};string serialized=JsonSerializer.Serialize(catalog);string canonical=string.Join("\n",CatalogReconciler.Project(catalog).Values.Select(r=>r.Text));var connection=InfobaseConnection.Parse(entry.V8iFields["Connect"]);
        check("R06-import",new[]{"url-fixture","wsp-fixture","proxy-fixture","pwd-fixture","outer-fixture","args-fixture"}.All(s=>!serialized.Contains(s)&&!canonical.Contains(s))&&entry.Name=="Учебная Web"&&entry.Location=="https://example.invalid/training"&&connection["Locale"]=="ru_RU"&&connection["Custom"]=="keep;quoted"&&entry.V8iFields["UnknownField"]=="keep"&&entry.V8iFields["AdditionalParameters"].Contains("Учебный")&&original.SequenceEqual(File.ReadAllBytes(file)),"Imported/canonical values exclude connection, outer-field and argument secrets; login/nonsecret fields remain; source bytes unchanged.");
        var store=new CatalogStore(Path.Combine(root,"PrivacyOfficial"));var session=OfficialListSession.Load(file,Array.Empty<string>());var before=session.Refresh(new Catalog());store.Save(before);var after=before.Copy();after.Bases.Single().Name="Переименованная";session.SaveChanges(before,after,store);string renamed=File.ReadAllText(file);
        check("R06-official-rename",renamed.Contains("[Переименованная]")&&renamed.Contains("Connect="+connect)&&renamed.Contains("Password=outer-fixture"),"Rename of explicit synthetic official file changes its name without silently stripping the source Connect or unrelated secret field; sanitized local import and source writes are separate paths.");
    }
    static void WebOptions(MainWindow main,string root,Action<string,bool,string> check)
    {
        var entry=new BaseEntry{Kind="Web",Location="https://example.invalid/training",Executable=Path.Combine(root,"1cv8c.exe"),WebOptions=new(){Speed="Низкая",Authentication="Windows",User="web-user",Proxy="Указать",ProxyServer="proxy.invalid",ProxyPort=3128,ProxyUser="proxy-user"}};
        var plan=LaunchService.Build(entry,false,"Авто",Array.Empty<PlatformInfo>(),requireInstalled:false);var args=plan.ArgumentList.ToArray();entry.WebOptions.Proxy="Без прокси";entry.WebOptions.Authentication="Запрашивать";var noProxy=LaunchService.Build(entry,false,"Авто",Array.Empty<PlatformInfo>(),requireInstalled:false);
        entry.WebOptions.Speed="Выбирать при запуске";bool choiceRequired=Reject(()=>LaunchService.Build(entry,false,"Авто",Array.Empty<PlatformInfo>(),requireInstalled:false));main.Dispatcher.BeginInvoke(()=>{var form=Application.Current.Windows.OfType<FormDialog>().Last();Click(Logical<Button>(form).Single(b=>b.IsCancel));},DispatcherPriority.ApplicationIdle);bool canceled=!(bool)Call(main,"ChooseConnectionSpeed",entry)!;
        check("V14",args.Contains("/WS")&&args.Contains("Low")&&args.Contains("/WSA+")&&args.Contains("-PSrv")&&args.Contains("3128")&&args.Contains("proxy-user")&&noProxy.ArgumentList.Contains("/NoProxy")&&noProxy.ArgumentList.Contains("/WSA-")&&choiceRequired&&canceled&&entry.WebOptions.Speed=="Выбирать при запуске","Thin-client plans carry per-base speed/auth/proxy options; canceled speed prompt returns without changing entry or constructing runnable launch; no browser/client process started.");
    }
    static void AdditionalUi(MainWindow main,Catalog catalog,string root,Action<string,bool,string> check)
    {
        var action=new ExplorerActionWindow(Path.Combine(root,"fictional.1CD"),false);bool restricted=Logical<Button>(action).Where(b=>b.Content?.ToString() is "Зарегистрировать" or "Конфигуратор").All(b=>!b.IsEnabled);action.Dispatcher.BeginInvoke(()=>Click(Logical<Button>(action).Single(b=>b.IsCancel)),DispatcherPriority.ApplicationIdle);bool? selected=action.ShowDialog();check("V03",restricted&&selected==false&&action.SelectedAction=="","File-action chooser denies edit actions in User context; actual modal Cancel sets no action or registration.");
        catalog.Configurations.Add(new(){Id="config-a",Name="Конфигурация А",InfoUrl="https://example.invalid/a",Country="Россия",Watch=true});catalog.Configurations.Add(new(){Id="config-b",Name="Конфигурация Б",InfoUrl="https://example.invalid/b",Country="Казахстан"});var configs=(Window)Call(main,"CreateConfigurationsWindow",false)!;var grid=Named<DataGrid>(configs,"ConfigurationsGrid");grid.SelectedIndex=0;bool first=Named<TextBlock>(configs,"ConfigurationSummary").Text.Contains("https://example.invalid/a");grid.SelectedIndex=1;bool second=Named<TextBlock>(configs,"ConfigurationSummary").Text.Contains("https://example.invalid/b");grid.SelectedIndex=-1;Named<TextBox>(configs,"ConfigurationFilter").Text="No matches";
        check("V02",first&&second&&Named<TextBlock>(configs,"ConfigurationSummary").Text.Contains("Выберите")&&!Logical<Button>(configs).Single(b=>b.Content?.ToString()=="Проверить обновления").IsEnabled,"Configuration selection updates property pane; clearing/filtering removes stale details and disables empty check; no network request.");configs.Close();
        string launched="";bool design=false;Window Tray()=>(Window)Call(main,"CreateTrayPanel",(Action<string,bool>)((id,d)=>{launched=id;design=d;}))!;var tray=Tray();var favorites=Named<ListBox>(tray,"TrayFavorites");favorites.SelectedIndex=0;var menu=favorites.ContextMenu!;((MenuItem)menu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));bool correct=launched=="row-a"&&design;
        string mode=catalog.Settings.InterfaceMode;catalog.Settings.InterfaceMode="Пользователь";launched="";var user=Tray();var userList=Named<ListBox>(user,"TrayFavorites");userList.SelectedIndex=0;((MenuItem)userList.ContextMenu!.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));bool denied=launched=="";user.Close();catalog.Settings.InterfaceMode=mode;
        var escape=Tray();escape.Show();Drain();var key=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(escape)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};escape.RaiseEvent(key);Drain();
        check("V11",correct&&denied&&key.Handled&&!escape.IsVisible,"Tray panel menu addresses selected favorite ID; handler rejects Designer in User context even when raised directly; WPF Escape closes panel. Native outside-click not asserted.");
    }
    static void BaseReleases(string root,Action<string,bool,string> check)
    {
        string local=Path.Combine(root,"UpdInfo.txt");File.WriteAllText(local,"Version=2.0.0\nUpdateDate=28.10.2026\nFromVersions=1.0.0\n");var entry=new BaseEntry{Name="Учебная",ConfigurationVersion="1.0.0",ConfigurationId="release",LocalUpdateInfoFile=local};var source=new ConfigurationSource{Id="release",Name="Fixture",InfoUrl="https://example.invalid/UpdInfo.txt"};int reads=0,saves=0,downloads=0;
        Func<string,bool,CancellationToken,Task<ReleaseInfo>> read=(address,isLocal,token)=>{reads++;return isLocal?ReleaseService.ReadLocal(address,token):Task.FromResult(new ReleaseInfo("2.0.0","28.10.2026","1.0.0"));};var window=new BaseReleaseWindow(entry,new[]{source},read,_=>{saves++;return true;},(_,_,_)=>downloads++);window.Show();bool loaded=Pump(()=>window.SaveButton.IsEnabled);bool compatible=window.ResultText.Text.Contains("перечислена среди");Click(window.SaveButton);window.LocalSource.IsChecked=true;bool invalidated=!window.SaveButton.IsEnabled;Click(window.CheckButton);bool localDone=Pump(()=>window.SaveButton.IsEnabled);bool localNoDownload=!window.DistributionsButton.IsEnabled;window.AddressBox.Text=Path.Combine(root,"missing-updinfo.txt");Click(window.CheckButton);bool failed=Pump(()=>window.CheckButton.IsEnabled)&&!window.SaveButton.IsEnabled;
        check("V16-source",loaded&&reads==3&&saves==1&&compatible&&invalidated&&localDone&&localNoDownload&&failed&&downloads==0&&ReleaseService.Compare(new("2.0.0","",""),"1.0.0").Contains("не сообщает")&&ReleaseService.Compare(new("1.0.0","",""),"1.0.0").Contains("совпадают"),"Release window automatically checks stub source; explicit Save only; local fixture parsed, changed source invalidates, local distribution disabled, missing file reports failure; known compatibility differs from missing metadata.");Render(window,Path.Combine(root,"base-release.png"),770,580);window.Close();
        bool canceled=false;var pending=new BaseReleaseWindow(entry,new[]{source},async(_,_,token)=>{using var registration=token.Register(()=>canceled=true);await Task.Delay(Timeout.Infinite,token);return new("2.0.0","","");},_=>{saves++;return true;});pending.Show();Drain();pending.Close();Drain();check("V16-cancel",canceled&&saves==1,"Closing a pending check cancels its injected operation; no source/configuration save or download.");
    }
    static void Summary(string root,Action<string,bool,string> check)
    {
        string file=Path.Combine(root,"summary.txt");File.WriteAllText(file,"fixture");string copied="",opened="";int openCalls=0;
        var window=new BaseInfoWindow("Учебная",new[]{new InfoField("ID","fictional-id",CopyOnClick:true),new InfoField("Путь",file,OpenFolder:true),new InfoField("Нет",Path.Combine(root,"missing","absent"),OpenFolder:true),new InfoField("Пусто","",OpenFolder:true)},s=>copied=s,s=>{opened=s;openCalls++;});
        var links=Logical<TextBlock>(window).SelectMany(t=>t.Inlines.OfType<Hyperlink>()).ToArray();links[0].RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));Drain();links[1].RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));links[2].RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        check("V01",copied=="fictional-id"&&opened==root&&openCalls==1&&links.Length==3,"Real Hyperlink Click events copy ID to injected writer and open existing file parent via callback; missing and empty paths do not launch Explorer.");
        foreach(string theme in new[]{"Светло-серый","Тёмный"}){ThemeService.Apply(new Preferences{Theme=theme});Render(window,Path.Combine(root,"summary-"+(theme=="Тёмный"?"dark":"light")+".png"),560,370);}window.Close();
    }
    static void Pickers(string root,Action<string,bool,string> check)
    {
        var groups=new GroupSelectionWindow(new[]{"Один/Два","Три"},"");var nested=Logical<TreeViewItem>(groups.Groups).Single(x=>x.Tag?.ToString()=="Один/Два");nested.IsSelected=true;bool child=groups.SelectedGroup=="Один/Два";((TreeViewItem)groups.Groups.Items[0]).IsSelected=true;bool top=groups.SelectedGroup==""&&groups.SelectButton.IsEnabled;groups.PathBox.Text="Bad//Path";
        check("V04",child&&top&&!groups.SelectButton.IsEnabled,"Real group-tree selection reaches nested path and root; malformed new path disables confirmation.");groups.Close();
        var version=new PlatformVersionWindow(new[]{new PlatformInfo("8.3.9.1",root,"x86"),new PlatformInfo("8.3.10.1",root,"x64"),new PlatformInfo("8.3.11.1",root,"x86")},"8.3","Авто","Учебная");
        Click(Named<Button>(version,"VersionsAscending"));bool numeric=version.Versions.Items.Cast<PlatformInfo>().Select(p=>p.Version).SequenceEqual(new[]{"8.3.9.1","8.3.10.1","8.3.11.1"});Named<RadioButton>(version,"Onlyx64").IsChecked=true;bool filtered=version.Versions.Items.Count==1&&((PlatformInfo)version.Versions.Items[0]).Architecture=="x64";version.VersionBox.Text="8.4";version.ArchitectureBox.SelectedItem="Авто";
        check("V10",numeric&&filtered&&!version.Versions.CanUserSortColumns&&version.ApplyButton.IsEnabled&&version.SelectedVersion=="8.4"&&version.SelectedArchitecture=="Авто","Numeric ascending/filter controls work; lexical header sort disabled; manual version and automatic architecture retained.");Render(version,Path.Combine(root,"versions.png"),630,450);version.Close();
    }
    static void PortableThemes(MainWindow main,string root,Action<string,bool,string> check)
    {
        IReadOnlyList<PortableDrive> drives=new[]{new PortableDrive("R:\\","Съёмный","Учебный",100000000,true),new PortableDrive("S:\\","Съёмный","Нет носителя",null,false)};
        var form=(FormDialog)Call(main,"CreatePortableCopyDialog",(Func<IReadOnlyList<PortableDrive>>)(()=>drives))!;Click(Named<Button>(form,"RefreshPortableDrives"));var grid=Named<DataGrid>(form,"PortableDrives");grid.SelectedIndex=0;Click(Named<Button>(form,"UsePortableDrive"));bool path=form.Value("folder")=="R:\\VanBaz";grid.SelectedIndex=1;bool unavailable=!Named<Button>(form,"UsePortableDrive").IsEnabled;form.SetText("folder",Path.Combine(root,"manual-copy"));Click(Named<Button>(form,"RefreshPortableDrives"));
        check("V06",path&&unavailable&&form.Value("folder")==Path.Combine(root,"manual-copy"),"Ready-drive action chooses root/VanBaz; unready drive disabled; refresh preserves manual destination.");form.Close();
        var themes=(FormDialog)Call(main,"CreateThemesDialog",main)!;var normal=Named<Button>(themes,"ThemePreviewNormal");var status=Named<TextBlock>(themes,"ThemePreviewStatus");string before=status.Text;Click(normal);
        check("V09",!Named<Button>(themes,"ThemePreviewDisabled").IsEnabled&&status.Text!=before,"Theme sample button changes preview status only; disabled sample really disabled.");Render(themes,Path.Combine(root,"themes.png"));themes.Close();
    }
    static void Interaction(MainWindow main,Catalog catalog,string root,Action<string,bool,string> check)
    {
        main.Show();Drain();var grid=(DataGrid)main.FindName("BasesGrid");Call(main,"SelectBaseInList","row-a");main.UpdateLayout();
        var target=Visuals<Button>(grid).Single(b=>b.Tag?.ToString()=="launch"&&b.DataContext is ListRow{Entry.Id:"row-b"});Click(target);Drain();
        bool rightTarget=ReferenceEquals(grid.SelectedItem,target.DataContext);var clickedRow=grid.Items.Cast<ListRow>().Last(r=>r.Entry?.Id=="row-a");var arrow=Visuals<Button>(grid).Single(b=>b.Tag?.ToString()=="launch-menu"&&ReferenceEquals(b.DataContext,clickedRow));int history=catalog.History.Count;Click(arrow);Drain();
        check("ROW",rightTarget&&ReferenceEquals(grid.SelectedItem,clickedRow)&&arrow.ContextMenu?.IsOpen==true&&catalog.History.Count==history,"Actual row buttons address their exact DataContext despite another selection or duplicate favorite row; arrow opens launch menu without adding history (external launch disabled in fixture).");
        if(arrow.ContextMenu is {} opened)opened.IsOpen=false;Drain();
        var search=(TextBox)main.FindName("SearchBox");bool activated=main.Activate();bool focused=search.Focus();search.Text="Учебная";Drain();
        var enter=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(main)!,0,Key.Enter){RoutedEvent=Keyboard.PreviewKeyDownEvent};search.RaiseEvent(enter);
        var escape=new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(main)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent};search.RaiseEvent(escape);
        check("KEY-TEXT",Keyboard.FocusedElement==search&&!enter.Handled&&search.Text==""&&escape.Handled,$"WPF search: activated={activated}, Focus()={focused}, currentFocus={Keyboard.FocusedElement==search}, EnterHandled={enter.Handled}, EscapeHandled={escape.Handled}, textEmpty={search.Text==""}. No native key injection.");main.Hide();
    }
}
