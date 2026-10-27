using System.Diagnostics;
using System.IO;
using System.Text.Json;
using VanBaz;

internal static class V15Smoke
{
    public static void Run(string scratch, Action<bool,string> check)
    {
        var migrated = new Catalog { FeatureMigrationVersion = 6 }; migrated.Settings.FontSize = 14; migrated.Settings.SourceSettings["FontSize"] = "14";
        migrated.Settings.SourceSettings["ActionAfterRun"] = "1";
        FeatureMigration.Apply(migrated); check(Math.Abs(migrated.Settings.FontSize-14d*96/72)<.001 && migrated.Settings.AfterLaunchAction == "Свернуть в трей", "v15: imported points and after-launch preference migrate once");
        double size = migrated.Settings.FontSize; FeatureMigration.Apply(migrated); check(size == migrated.Settings.FontSize, "v15: font migration is idempotent");
        var imported = new Catalog { FeatureMigrationVersion = 0 }; imported.Settings.AfterLaunchAction = "Закрыть ВанБаз"; FeatureMigration.Apply(imported);
        check(imported.Settings.AfterLaunchAction == "Закрыть ВанБаз", "v15: fresh migration preserves explicitly imported post-launch action");
        string root = Path.Combine(scratch,"search-v15"), one=Path.Combine(root,"one"), two=Path.Combine(root,"two"); Directory.CreateDirectory(one); Directory.CreateDirectory(two);
        File.WriteAllBytes(Path.Combine(one,"1Cv8.1CD"), new byte[200]); File.WriteAllBytes(Path.Combine(two,"1Cv8.1CD"), new byte[10]);
        var result = BaseSearch.Scan(new[]{root,one},new[]{one},0,null,default);
        check(result.Bases.Count == 2 && result.Bases.Single(b=>b.Folder==one).Registered && !result.Bases.Single(b=>b.Folder==one).Selected, "v15: overlapping search roots deduplicate and registered records remain unselected");
        check(BaseSearch.Scan(new[]{root},Array.Empty<string>(),100,null,default).Bases.Count == 1 && BaseSearch.Scan(new[]{root},Array.Empty<string>(),0,DateTime.Now.AddDays(1),default).Bases.Count == 0, "v15: search filters actually constrain size and date");
        check(BaseSearch.Scan(new[]{root},Array.Empty<string>(),0,null,new System.Threading.CancellationToken(true)).Stopped, "v15: search cancellation preserves partial-result state");
        var entry = new BaseEntry { Location=Path.Combine(scratch,"new-base-v15"), Executable=Path.Combine(AppContext.BaseDirectory,"1cv8.exe") };
        string template=Path.Combine(scratch,"fixture.cf"); File.WriteAllText(template,"fixture");
        var info = BaseCreation.Build(entry,template,"ru_RU",Path.Combine(scratch,"create.log"),Path.Combine(scratch,"create.result"),Array.Empty<PlatformInfo>());
        string capture = Path.Combine(scratch,"create-args.json"); info.Environment["VANBAZ_CAPTURE"] = capture;
        using(var process=LaunchService.Start(info)) { if(!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Create stub timeout"); } check(process.ExitCode==0,"v15: creation uses test process"); }
        var actual=JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
        check(actual[0]=="CREATEINFOBASE" && actual[1]==entry.ConnectionDisplay && actual.Contains(template) && !actual.Contains("/AddToList") && actual.Contains("/DumpResult"),"v15: creation arguments preserve Windows boundaries and leave list registration to transactional catalog save");
        entry.Location=one; bool rejected=false; try { BaseCreation.Build(entry,"","", "log","result",Array.Empty<PlatformInfo>()); } catch(IOException) { rejected=true; }
        check(rejected,"v15: existing base folder cannot be overwritten by creation");
        var web = StandaloneCommand.Build(new() { Kind="Web-адрес",Target="https://example.invalid/demo",Arguments="ignored" });
        check(web.FileName=="https://example.invalid/demo" && web.Arguments=="" && web.UseShellExecute,"v15: independent Web command uses its URL, not selected base arguments");
        rejected=false; try { StandaloneCommand.Build(new() { Kind="Web-адрес",Target="javascript:alert(1)" }); } catch(ArgumentException) { rejected=true; }
        check(rejected,"v15: Web commands accept only HTTP(S)");
        check(MainWindow.FormatName("  теСТ   ","Первая буква Заглавная",true,"","")=="Тест","v15: name formatting applies whitespace and case rules");
        string portableFolder=Path.Combine(scratch,"portable-v15");
        string application=Environment.GetEnvironmentVariable("VANBAZ_APP_FOLDER") ?? Path.GetDirectoryName(typeof(App).Assembly.Location)!;
        PortableCopy.Create(application,portableFolder,new Catalog());
        check(File.Exists(Path.Combine(portableFolder,"VanBaz.exe")) && File.Exists(Path.Combine(portableFolder,"Data","catalog.json")),"v15: portable copy contains own runtime and selected catalog");
        rejected=false; try { PortableCopy.Create(application,portableFolder,null); } catch(IOException) { rejected=true; }
        check(rejected,"v15: portable copy never overwrites a populated destination");
        Console.WriteLine("PASS v15 logic: font migration, search, creation stub, standalone commands, name formatting.");
    }
}
