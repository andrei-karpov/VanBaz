using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using VanBaz;

if (args.Length == 2 && args[0] == "--screenshots") { InterfaceScreenshots.Run(Path.GetFullPath(args[1])); return; }

if (args.Contains("--clipboard-test")) { NativeClipboardSmoke.Run(false); return; }
if (args.Contains("--clipboard-read-test")) { NativeClipboardSmoke.Run(true); return; }

if (args.FirstOrDefault() is "ENTERPRISE" or "DESIGNER" or "CREATEINFOBASE" or "enterprise" or "config" || args.FirstOrDefault()?.StartsWith("https://example.invalid/", StringComparison.Ordinal) == true)
{
    string capture = Environment.GetEnvironmentVariable("VANBAZ_CAPTURE")!;
    File.WriteAllText(capture + ".writing", JsonSerializer.Serialize(args));
    File.Move(capture + ".writing", capture, true);
    return;
}

string scratch = Path.Combine(Path.GetTempPath(), "VanBaz-smoke-" + Guid.NewGuid());
Directory.CreateDirectory(scratch);
int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
try
{
    if (args.Contains("--v09-only")) { V09Smoke.Run(scratch, Check); Console.WriteLine($"PASS v0.9: {checks}"); return; }
    if (args.Contains("--v10-only")) { V10Smoke.Run(scratch, Check); Console.WriteLine($"PASS v0.10: {checks}"); return; }
    if (args.Contains("--v11-audit")) { V11Smoke.Run(scratch, (ok, label) => Console.WriteLine((ok ? "PASS: " : "FAIL: ") + label)); return; }
    if (args.Contains("--v11-only")) { V11Smoke.Run(scratch, Check); Console.WriteLine($"PASS v0.11: {checks}"); return; }
    if (args.Contains("--settings-only")) { UiRegression.Run(scratch, true); return; }
    if (args.Contains("--ui-only")) { UiRegression.Run(scratch); Console.WriteLine("PASS UI regression"); return; }
    if (args.Contains("--v12-only")) { V12Smoke.Run(scratch, Check); Console.WriteLine($"PASS v0.12: {checks}"); return; }
    if (args.Contains("--v13-only")) { V13Smoke.Run(scratch, Check); Console.WriteLine($"PASS v0.13: {checks}"); return; }
    if (args.FirstOrDefault() == "--inspect-official")
    {
        var session = OfficialListSession.Load(args[1], args.Skip(2)); var result = session.Refresh(new());
        Console.WriteLine($"READ ONLY: sources={session.Sources.Count}; bases={result.Bases.Count}; groups={result.Groups.Count}; writes=0"); return;
    }
    const string input = "[Бухгалтерия]\nConnect=File=\"D:\\Базы 1С\\Основная\";\nID=fixed-id\nFolder=/Клиенты/Тест\nVersion=8.3\n\n[Сервер]\nConnect=Srvr=\"server:1541\";Ref=\"erp\";\n\n[Web]\nConnect=ws=\"https://example.invalid/erp\";\n\n[Broken]\nConnect=Unrecognized=1;\n";
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.GetEncoding(1251) })
    {
        var file = Path.Combine(scratch, "list.v8i"); File.WriteAllText(file, input, encoding);
        var result = V8iImporter.Read(file);
        Check(result.Entries.Count == 3 && result.Skipped == 1 && result.Entries[0].Name == "Бухгалтерия" && result.Entries[0].Group == "Клиенты/Тест" && result.Entries[1].Location == "server:1541/erp", "Import encoding/structure");
    }
    var catalog = new Catalog();
    var parsed = V8iImporter.Parse(input);
    Check(V8iImporter.Merge(catalog, parsed.Entries) == 3 && V8iImporter.Merge(catalog, parsed.Entries) == 0, "Idempotent import");
    var store = new CatalogStore(scratch); store.Save(catalog); catalog.Bases[0].Notes = "Сохранено"; store.Save(catalog);
    Check(store.Load().Bases[0].Notes == "Сохранено" && File.Exists(store.FilePath + ".bak"), "Atomic save and backup");
    File.WriteAllText(store.FilePath, "broken");
    bool rejected = false; try { store.Load(); } catch (JsonException) { rejected = true; }
    Check(rejected && File.ReadAllText(store.FilePath) == "broken", "Do not overwrite damaged catalogs");
    var executable = Path.Combine(AppContext.BaseDirectory, "1cv8.exe");
    var entry = new BaseEntry { Executable = executable, Location = "D:\\Базы 1С\\Папка с пробелами\\", UserName = "Иван \"Тест\"" };
    entry.DesignerUser = entry.UserName;
    foreach (bool designer in new[] { false, true })
    {
        var info = LaunchService.Build(entry, designer, "Авто", Array.Empty<PlatformInfo>());
        string capture = Path.Combine(scratch, "args.json"); info.Environment["VANBAZ_CAPTURE"] = capture;
        using var process = LaunchService.Start(info);
        if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Stub timeout"); }
        var actual = JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
        var expected = new[] { designer ? "DESIGNER" : "ENTERPRISE", "/F", entry.Location, "/N", entry.UserName }.Concat(designer ? Array.Empty<string>() : new[] { "/AppAutoCheckMode" });
        Check(process.ExitCode == 0 && actual.SequenceEqual(expected), "Actual process argument boundaries");
    }
    entry.Executable = ""; entry.Version = "8.3.9";
    var available = new[] { new PlatformInfo("8.3.90.1", AppContext.BaseDirectory, "x64"), new PlatformInfo("8.3.9.10", AppContext.BaseDirectory, "x64") };
    Check(LaunchService.Build(entry, false, "Авто", available).FileName == executable, "Version matching");
    rejected = false; try { LaunchService.Build(entry, false, "Авто", available.Take(1)); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "No incorrect version prefix match");
    entry.Kind = "Web"; entry.Location = "https://example.invalid/base";
    rejected = false; try { LaunchService.Build(entry, true, "Авто", available); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Web Designer rejection");
    string source = Path.Combine(scratch, "SMSettings"); Directory.CreateDirectory(source);
    const string sourceId = "11111111-1111-1111-1111-111111111111";
    string sourceList = Path.Combine(scratch, "source.v8i");
    File.WriteAllText(sourceList, "[Клиенты]\nID=group-1\nFolder=/\nOrderInTree=256\n[Тест]\nID=group-2\nFolder=/Клиенты\nOrderInTree=512\n[База]\nID=" + sourceId + "\nConnect=File=\"D:\\Тест\\База\";\nFolder=/Клиенты/Тест\nVersion=8.3\nApp=ThinClient\nWA=0\n[Второе имя]\nID=22222222-2222-2222-2222-222222222222\nConnect=File=\"D:\\Тест\\База\";\n");
    File.WriteAllText(Path.Combine(source, "v8config.smc"), "[" + sourceId + "]\nx64Bit=1\nDescription=Описание\nNote=Заметка\nUserLoginCnf=1\nCfgUser=Admin\nCfgPassword=opaque-secret\nLaunchDate=19.10.2026 12:30:00\n[33333333-3333-3333-3333-333333333333]\nDescription=Старая запись\n");
    File.WriteAllText(Path.Combine(source, "config.sme"), "[Environment]\nFontName=Calibri\nFontSize=14\nDefNodeHeight=29\nGroupsShowTop=1\nSortByAlfa=1\n");
    File.WriteAllText(Path.Combine(source, "settings.cnf"), "<Settings><FontColorFavorite>$00060D8A</FontColorFavorite><ShowHistory>true</ShowHistory><DefDesignerPassword>opaque-global</DefDesignerPassword></Settings>");
    File.WriteAllText(Path.Combine(source, "v8keys.smc"), "ClearCache=Очистка кэша\n/C РежимОтладки;=Отладка\n");
    var migrated = StartManagerMigration.Prepare(new(), source, sourceList);
    Check(migrated.Bases.Count == 2 && migrated.Groups.Count == 2 && migrated.Groups[1].Path == "Клиенты/Тест" && migrated.Migration is { MatchedSettings: 1, PasswordFields: 0, OrphanSettings: 1 }, "Migration joins exact IDs, retains groups and distinct registrations");
    var mb = migrated.Bases[0];
    Check(mb.Architecture == "x64" && mb.Description == "Описание" && !mb.DesignerPasswordMissing && mb.DesignerSecret == "" && migrated.Settings.FavoriteColor == "#8A0D06" && !JsonSerializer.Serialize(migrated).Contains("opaque-"), "Migration maps known settings and excludes opaque passwords without nags");
    mb.Description = "User edit";
    var again = StartManagerMigration.Prepare(migrated, source, sourceList);
    Check(again.Bases.Count == 2 && again.Bases[0].Description == "User edit" && again.Migration is { Added: 0, Existing: 2 } && again.Commands.Count == 2, "Repeat migration preserves user edits without duplicates");
    string exported = Path.Combine(scratch, "export.v8i"); V8iImporter.Export(migrated, exported);
    var roundtrip = V8iImporter.Read(exported);
    Check(roundtrip.Groups.Count == 2 && roundtrip.Entries.Count == 2 && roundtrip.Entries[0].Id == sourceId && roundtrip.Entries[0].Group == "Клиенты/Тест" && roundtrip.Entries[0].WindowsAuthentication == "Нет", "V8i round-trip keeps groups, IDs and connection options");
    entry.Kind = "File"; entry.Location = "D:\\База\\"; entry.Executable = executable; entry.DesignerSecret = SecretStore.Protect("Тестовый пароль");
    var snapshotStore = new CatalogStore(Path.Combine(scratch, "snapshots-test")); snapshotStore.Save(migrated); string snapshot = snapshotStore.Snapshot();
    Check(CatalogStore.Read(snapshot).Groups.Count == 2 && CatalogStore.Read(snapshot).Bases.Count == 2, "Snapshot retains complete catalog");
    string cacheRoot = Path.Combine(scratch, "cache"); string cacheId = Guid.NewGuid().ToString();
    string cacheDir = Path.Combine(cacheRoot, cacheId); Directory.CreateDirectory(cacheDir); File.WriteAllText(Path.Combine(cacheDir, "state.bin"), "original-cache");
    Directory.CreateDirectory(Path.Combine(cacheRoot, "not-a-guid"));
    var caches = CacheService.Find(new[] { cacheRoot }, Array.Empty<BaseEntry>());
    CacheService.Move(caches.Single(), false);
    var quarantined = CacheService.Find(new[] { cacheRoot }, Array.Empty<BaseEntry>(), true);
    CacheService.Move(quarantined.Single(), true);
    bool outsideRejected = false;
    try { CacheService.Move(new(cacheRoot, Path.Combine(scratch, cacheId), cacheId, ""), false); } catch (IOException) { outsideRejected = true; }
    Check(caches.Count == 1 && outsideRejected && File.ReadAllText(Path.Combine(cacheDir, "state.bin")) == "original-cache", "Cache quarantine restores contents and rejects paths outside root");
    var arguments = new[] { @"C:\Каталог с пробелом\", "with\"quote", "", "a\\\\\"b" };
    Check(LaunchService.SplitArguments(string.Join(" ", arguments.Select(DesktopIntegration.Quote))).SequenceEqual(arguments), "Windows shortcut quoting round-trips trailing slashes, spaces and quotes");
    string pipeName = "VanBaz-Smoke-" + Guid.NewGuid().ToString("N"); var received = new TaskCompletionSource<string[]>();
    using (var bridge = new InstanceBridge(pipeName, values => received.TrySetResult(values)))
    {
        InstanceBridge.Send(pipeName, new[] { "--launch-id", "synthetic-id" });
        Check(received.Task.Wait(5000) && received.Task.Result.SequenceEqual(new[] { "--launch-id", "synthetic-id" }), "Second-instance request reaches current-user pipe");
    }
    Check(ReleaseService.Parse("\uFEFFVersion=3.0.1.2\nFromVersions=;3.0.1.1;\nUpdateDate=20.10.2026").Version == "3.0.1.2", "Release manifest parser");
    var checkInfo = LaunchService.BuildCheck(entry, false, true, Path.Combine(scratch, "check.log"), available);
    string checkCapture = Path.Combine(scratch, "check-args.json"); checkInfo.Environment["VANBAZ_CAPTURE"] = checkCapture;
    using (var process = LaunchService.Start(checkInfo))
    {
        if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Check stub timeout"); }
        var actual = JsonSerializer.Deserialize<string[]>(File.ReadAllText(checkCapture))!;
        Check(actual.Contains("/IBCheckAndRepair") && actual.Contains("-TestOnly") && actual.Contains("-LogAndRefsIntegrity"), "Default maintenance is test-only; arguments reach stub");
    }
    if (Environment.GetEnvironmentVariable("VANBAZ_THEME_TEST_FOLDER") is { Length: > 0 } themeFolder)
    {
        var themeSettings = new Preferences();
        int themeCount = ThemeService.ImportFolder(themeSettings, themeFolder);
        var win10 = themeSettings.Themes.Single(t => t.Name == "Win10");
        store.Save(new Catalog { Settings = themeSettings });
        bool badThemeRejected = false;
        try { ThemeService.ReadVsf(Encoding.ASCII.GetBytes("VCL_STYLE 2.0bad")); } catch (InvalidDataException) { badThemeRejected = true; }
        Check(themeCount == 5 && win10.Colors["Window"] == "#F3F3F3" && win10.Colors["Selection"] == "#FADB1F" && store.Load().Settings.Themes.Count == 5 && badThemeRejected, "Five original VSF palettes parse and persist; Win10 BGR mapping and malformed input rejection");
    }
    string updateFile = Path.Combine(scratch, "обновление конфигурации.cfu"); File.WriteAllText(updateFile, "synthetic");
    string mergeFile = Path.Combine(scratch, "настройки объединения.xml"); File.WriteAllText(mergeFile, "<synthetic/>");
    var update = LaunchService.BuildUpdate(entry, updateFile, mergeFile, true, Path.Combine(scratch, "update.log"), available);
    string updateCapture = Path.Combine(scratch, "update-args.json"); update.Environment["VANBAZ_CAPTURE"] = updateCapture;
    using (var process = LaunchService.Start(update))
    {
        if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Update stub timeout"); }
        var actual = JsonSerializer.Deserialize<string[]>(File.ReadAllText(updateCapture))!;
        Check(actual.Contains("/UpdateCfg") && actual[Array.IndexOf(actual, "/UpdateCfg") + 1] == updateFile && actual[Array.IndexOf(actual, "-Settings") + 1] == mergeFile && actual.Contains("/UpdateDBCfg") && !LaunchService.BuildUpdate(entry, updateFile, "", false, "log", available).ArgumentList.Contains("/UpdateDBCfg"), "CFU update arguments reach stub; database restructure only on explicit selection");
    }

    byte[] payload = Encoding.UTF8.GetBytes("Synthetic quarantine fixture.");
    string deleteFolder = Path.Combine(scratch, "delete-base"); Directory.CreateDirectory(deleteFolder);
    string deleteFile = Path.Combine(deleteFolder, "1Cv8.1CD"); File.WriteAllBytes(deleteFile, payload);
    var deleteEntry = new BaseEntry { Name = "Synthetic delete", Location = deleteFolder };
    bool deleteBusyRejected = false;
    using (var busy = new FileStream(deleteFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        try { DeletedBaseService.Quarantine(deleteEntry, Path.Combine(scratch, "delete-journal")); } catch (IOException) { deleteBusyRejected = true; }
    var deleted = DeletedBaseService.Quarantine(deleteEntry, Path.Combine(scratch, "delete-journal"));
    bool moved = !File.Exists(deleteFile) && File.ReadAllBytes(deleted.StoredFile).SequenceEqual(payload);
    File.WriteAllText(deleteFile, "new base"); bool collisionRejected = false;
    try { DeletedBaseService.Restore(deleted); } catch (IOException) { collisionRejected = true; }
    bool preserved = File.ReadAllText(deleteFile) == "new base" && File.Exists(deleted.StoredFile);
    File.Delete(deleteFile); DeletedBaseService.Restore(deleted);
    Check(deleteBusyRejected && moved && collisionRejected && preserved && File.ReadAllBytes(deleteFile).SequenceEqual(payload), "Busy base cannot move; quarantined data round-trips; restore cannot overwrite newer base");

    using var distributionXml = new MemoryStream(Encoding.UTF8.GetBytes("<updateList xmlns='http://v8.1c.ru/configuration-updates'><update><version>3.0.1.2</version><file>1c/Accounting/update/1cv8.cfu</file><size>1 024</size><target>3.0.1.1</target></update></updateList>"));
    var distribution = ConfigurationService.ParseDistributions(distributionXml).Single();
    bool pathRejected = false; try { ConfigurationService.DistributionUri("https://example.invalid/files/", "%2e%2e/other.cfu"); } catch (InvalidDataException) { pathRejected = true; }
    bool dtdRejected = false; try { using var malicious = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY ext SYSTEM 'file:///C:/private'>]><x>&ext;</x>")); ConfigurationService.ParseDistributions(malicious); } catch (System.Xml.XmlException) { dtdRejected = true; }
    Check(distribution.Size == 1024 && distribution.Targets == "3.0.1.1" && ConfigurationService.DistributionUri("https://example.invalid/files/", distribution.File).AbsolutePath == "/files/1c/Accounting/update/1cv8.cfu" && pathRejected && dtdRejected, "Vendor distribution list parses; traversal and external XML entities rejected");

    File.WriteAllText(Path.Combine(source, "v8updates.lst"), "[config-test]\nDescr=Test configuration\nConfigName=Accounting\nConfigRelease=30\nPlatformVer=83\n");
    var withSource = StartManagerMigration.Prepare(new(), source, sourceList); withSource.Bases[0].SourceSettings["ConfigId"] = "config-test";
    ConfigurationService.ImportExistingSources(withSource);
    Check(withSource.Configurations.Count == 1 && withSource.Bases[0].ConfigurationId == "config-test" && withSource.Configurations[0].InfoUrl.EndsWith("/Accounting/30/83/UpdInfo.txt") && PlatformBridge.Connection(entry).Contains("Pwd=\"Тестовый пароль\";"), "Configuration catalog import links exact IDs; COM connection contains separately quoted fields");
    string legacyExecutable = Path.Combine(AppContext.BaseDirectory, "1cv7-smoke-" + Guid.NewGuid().ToString("N") + ".exe");
    File.Copy(executable, legacyExecutable, false);
    try
    {
        var legacyEntry = entry.Copy(); legacyEntry.Version = "7.7"; legacyEntry.Executable = legacyExecutable;
        var legacyLaunch = LaunchService.Build(legacyEntry, true, "Авто", available);
        string legacyCapture = Path.Combine(scratch, "legacy-args.json"); legacyLaunch.Environment["VANBAZ_CAPTURE"] = legacyCapture;
        using var process = LaunchService.Start(legacyLaunch);
        if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("7.7 stub timeout"); }
        var actual = JsonSerializer.Deserialize<string[]>(File.ReadAllText(legacyCapture))!;
        bool batchRejected = false; try { LaunchService.BuildCheck(legacyEntry, false, false, "test.log", available); } catch (InvalidOperationException) { batchRejected = true; }
        Check(actual[0] == "config" && actual.Contains("/D" + legacyEntry.Location) && actual.Contains("/PТестовый пароль") && !LaunchService.Preview(legacyLaunch).Contains("Тестовый пароль") && batchRejected, "7.7 config arguments reach stub; preview redacts attached password; 8.x maintenance is rejected");
    }
    finally { File.Delete(legacyExecutable); }
    if (Environment.GetEnvironmentVariable("VANBAZ_APP_FOLDER") is { Length: > 0 } appFolder)
    {
        string packageFolder = Path.Combine(scratch, "package"); Directory.CreateDirectory(packageFolder);
        var manifest = new AppPackage { Version = System.Reflection.AssemblyName.GetAssemblyName(Path.Combine(appFolder, "VanBaz.dll")).Version!.ToString(3) };
        foreach (string file in AppUpdateService.Files) { string path = Path.Combine(packageFolder, file); File.Copy(Path.Combine(appFolder, file), path); using var inputFile = File.OpenRead(path); manifest.Files[file] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(inputFile)); }
        File.WriteAllText(Path.Combine(packageFolder, "vanbaz-package.json"), JsonSerializer.Serialize(manifest));
        string zipPath = Path.Combine(scratch, "application.zip"); System.IO.Compression.ZipFile.CreateFromDirectory(packageFolder, zipPath);
        var prepared = AppUpdateService.Prepare(zipPath, Path.Combine(scratch, "updates"));
        using (var malicious = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Update)) malicious.CreateEntry("Data/catalog.json");
        bool dataRejected = false; try { AppUpdateService.Prepare(zipPath, Path.Combine(scratch, "updates")); } catch (InvalidDataException) { dataRejected = true; }
        Check(AppUpdateService.Files.All(f => File.Exists(Path.Combine(prepared.Folder, f))) && prepared.Version == manifest.Version && dataRejected, "Own update package validates version and hashes; embedded user Data is rejected");
    }
    var relocationCatalog = new Catalog { Bases = new() { new() { Id = "inside", Location = @"D:\Bases\A" }, new() { Id = "boundary", Location = @"D:\BasesOther\A" }, new() { Id = "server", Kind = "Server", Location = "D:/Bases" } } };
    string originalRelocation = JsonSerializer.Serialize(relocationCatalog);
    var relocation = CatalogRelocation.Preview(relocationCatalog, @"D:\Bases", @"E:\Databases");
    Check(relocation.Count == 1 && relocation[0].Id == "inside" && relocation[0].After == @"E:\Databases\A" && JsonSerializer.Serialize(relocationCatalog) == originalRelocation, "Path replacement preview preserves source and excludes adjacent prefixes and server bases");
    var snapCurrent = new Catalog { Bases = new() { new() { Id = "83", Name = "Edited", Version = "8.3.25.1", Location = @"D:\Demo83" }, new() { Id = "82", Version = "8.2", Location = @"D:\Demo82" } }, History = new() { new() { BaseId = "82", Name = "Keep" } } };
    snapCurrent.Settings.Theme = "Тёмный";
    var namedSnapshotStore = new CatalogStore(Path.Combine(scratch, "snapshots"));
    string savedSnapshot = SnapshotService.Save(namedSnapshotStore, snapCurrent, "До изменений", "8.3");
    var named = SnapshotService.Read(savedSnapshot); snapCurrent.Bases[0].Name = "Later";
    string untouched = JsonSerializer.Serialize(snapCurrent);
    var restored83 = SnapshotService.Restore(snapCurrent, named);
    var conflictSnapshot = named.Copy(); conflictSnapshot.Bases[0].Id = "82";
    bool conflictRejected = false; try { SnapshotService.Restore(snapCurrent, conflictSnapshot); } catch (InvalidDataException) { conflictRejected = true; }
    Check(named.Bases.Count == 1 && restored83.Bases.Single(b => b.Id == "83").Name == "Edited" && restored83.Bases.Any(b => b.Id == "82") && restored83.Settings.Theme == "Тёмный" && restored83.History.Count == 1 && SnapshotService.List(namedSnapshotStore).Single().Description == "До изменений" && JsonSerializer.Serialize(snapCurrent) == untouched && conflictRejected, "Named platform snapshots restore only their platform, retain settings/history, reject ID conflicts and never mutate preview source");
    string sourcePath = Path.Combine(scratch, "platform-list.v8i"); File.WriteAllText(sourcePath, "[Registered]\nID=source-id\nConnect=File=\"D:\\Source\";\nVersion=8.3\n");
    var source83 = new ListSource { Path = sourcePath, Platform = "8.2" }; var parsedSource = ListSourceService.Read(source83); var listsCatalog = new Catalog();
    int firstMerge = ListSourceService.Merge(listsCatalog, parsedSource); listsCatalog.Bases[0].Location = @"E:\UserEdit";
    int secondMerge = ListSourceService.Merge(listsCatalog, ListSourceService.Read(source83));
    var oldLists = new Preferences { AlternativeLists = new() { sourcePath }, PlatformLists = new() { new() { Path = sourcePath, Platform = "8.2", Enabled = false } } };
    Check(firstMerge == 1 && secondMerge == 0 && listsCatalog.Bases[0].Version == "8.2" && listsCatalog.Bases[0].Location == @"E:\UserEdit" && ListSourceService.Sources(oldLists).Count == 1 && !ListSourceService.Sources(oldLists)[0].Enabled, "Platform sources override imported version, retain edited IDs and preserve disabled sources when reading old settings");
    var startup = StartupOptions.Parse(new[] { "/spD:\\ВанБаз Data", "/mode1", "/SelectModeOff", "/min" }, @"C:\VanBaz", @"C:\Roaming");
    bool secondRejected = false; try { StartupOptions.Parse(new[] { "/second" }, @"C:\App", @"C:\Roaming"); } catch (ArgumentException) { secondRejected = true; }
    Check(startup.DataFolder == @"D:\ВанБаз Data" && startup.Mode == "Пользователь" && startup.LockMode && startup.Minimized && StartupOptions.Parse(new[] { "/usp" }, @"C:\App", @"C:\Roaming").DataFolder == @"C:\Roaming\VanBaz" && secondRejected, "Legacy startup aliases target ВанБаз data and preserve single-writer protection");
    await V08Smoke.Run(scratch, Check);
    V09Smoke.Run(scratch, Check);
    V10Smoke.Run(scratch, Check);
    V11Smoke.Run(scratch, Check);
    V12Smoke.Run(scratch, Check);
    V13Smoke.Run(scratch, Check);
    V14Smoke.Run(Check);
    V15Smoke.Run(scratch, Check);
    UiRegression.Run(Path.Combine(scratch, "ui-catalog"));
    Check(true, "Real window selection, history deletion guard, legacy tab, group menu and busy controls");
    Console.WriteLine($"PASS: {checks} assertions in one smoke harness; no real databases opened.");
}
finally { Directory.Delete(scratch, true); }
