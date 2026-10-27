using System.IO;
using System.Text;
using System.Text.Json;
using VanBaz;

internal static class V12Smoke
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        string root = Path.Combine(scratch, "v12"); Directory.CreateDirectory(root);
        string platform = Path.Combine(root, "platform"); Directory.CreateDirectory(platform);
        string exe = Path.Combine(platform, "1cv8.exe"); File.Copy(Path.Combine(AppContext.BaseDirectory, "1cv8.exe"), exe);
        var result = PlatformCatalog.Scan(new[] { platform }, standard: false);
        var found = result.Platforms.Single(); File.Delete(exe);
        check(found.ThickClient && !found.ThinClient, "Platform table uses scan snapshot after files disappear; rendering does not reprobe disk");
        var absent = PlatformCatalog.Scan(new[] { Path.Combine(root, "missing") }, standard: false);
        check(absent.Platforms.Count == 0 && absent.Errors.Count == 1, "Unavailable custom root has a visible error rather than an empty successful result");
        var inventory = new PlatformInventory();
        check(!inventory.Matches(new Preferences()), "Initial standard platform discovery is scheduled even with no custom roots");
        var offline = new BaseEntry { Version = "8.3", Location = @"D:\Synthetic", Executable = @"\\offline-fixture.invalid\platform\1cv8.exe", Advanced = new() { Taxi = true } };
        string preview = LaunchService.Preview(LaunchService.Build(offline, false, "Толстый управляемый", Array.Empty<PlatformInfo>(), requireInstalled: false));
        check(preview.Contains("offline-fixture.invalid") && preview.Contains("/iTaxi"), "Offline profile preview is pure argument validation and does not require network executable access");
        var baseEntry = new BaseEntry { Executable = Path.Combine(AppContext.BaseDirectory, "1cv8.exe"), Location = @"D:\Synthetic Profile Base", ExtraArguments = "" };
        string original = JsonSerializer.Serialize(baseEntry); var delivered = new List<string[]>();
        foreach (string language in new[] { "ru", "en" })
        {
            var profile = LaunchProfile.From(baseEntry, false, "Толстый управляемый"); profile.Advanced.Language = language;
            var launch = LaunchService.Build(profile.Apply(baseEntry), false, profile.Client, Array.Empty<PlatformInfo>());
            string path = Path.Combine(root, "profile-" + language + ".json"); launch.Environment["VANBAZ_CAPTURE"] = path;
            using (var process = LaunchService.Start(launch)) { if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Profile fixture timeout"); } }
            delivered.Add(JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))!);
        }
        check(original == JsonSerializer.Serialize(baseEntry) && delivered[0].Contains("ru") && delivered[1].Contains("en") && delivered.All(a => a.Contains(baseEntry.Location) && a.Contains("/RunModeManagedApplication")), "Two named profile settings reach separate processes without modifying the base entry");

        string list = Path.Combine(root, "ibases.v8i");
        File.WriteAllText(list, "[Before]\nID=recovery-test\nConnect=File=\"D:\\Synthetic\";\n");
        var c = new Catalog(); V8iImporter.Merge(c, V8iImporter.Read(list));
        var store = new CatalogStore(Path.Combine(root, "catalog")); store.Save(c);
        c = CatalogReconciler.Apply(c, store, CatalogReconciler.Prepare(c, list, true)).Catalog;
        c.Bases.Single().Name = "After";
        var applied = CatalogReconciler.Apply(c, store, CatalogReconciler.Prepare(c, list, true), _ => throw new IOException("fixture journal failure"));
        var op = FileRecovery.Pending(Path.Combine(store.Folder, "RecoveryOperations")).Single();
        var bytes = File.ReadAllBytes(store.FilePath); applied.Catalog.Settings.SelectedId = "new-window-selection";
        bool rejected = false; try { store.Save(applied.Catalog); } catch (IOException) { rejected = true; }
        check(rejected && bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Pending catalog recovery prevents later saves from destroying exact recovery hashes");
        FileRecovery.AcceptApplied(op);
        check(!store.HasPendingRecovery() && File.ReadAllText(list).Contains("[After]") && bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Accepting an applied operation completes only its journal and leaves both data files intact");
        store.Save(applied.Catalog);
        check(store.Load().Settings.SelectedId == "new-window-selection", "Normal saves resume after explicitly accepting a complete operation");

        string first = Path.Combine(root, "first.pfl"), second = Path.Combine(root, "second.pfl"); File.WriteAllText(first, "before"); File.WriteAllText(second, "before");
        var partial = FileRecovery.Begin(Path.Combine(root, "partial"), "PFL fixture", new Dictionary<string, byte[]?> { [first] = Encoding.UTF8.GetBytes("after"), [second] = Encoding.UTF8.GetBytes("after") });
        File.WriteAllText(first, "after"); rejected = false;
        try { FileRecovery.AcceptApplied(partial); } catch (IOException) { rejected = true; }
        check(rejected && !partial.Completed, "A partial multi-file operation cannot be accepted as complete");
        File.WriteAllText(second, "third-party"); rejected = false;
        try { FileRecovery.AcceptApplied(partial); } catch (IOException) { rejected = true; }
        check(rejected && File.ReadAllText(second) == "third-party", "Accepting recovery never hides or overwrites third-party file edits");

        string capture = Path.Combine(root, "web-args.json");
        var web = new BaseEntry { Kind = "Web", Location = "https://example.invalid/app/path?x=a%20b&y=2#section", UserName = "fixture-user", EnterpriseSecret = SecretStore.Protect("fixture-secret") };
        var browser = WebClientLauncher.Build(web, "Chrome", Path.Combine(AppContext.BaseDirectory, "1cv8.exe")); browser.Environment["VANBAZ_CAPTURE"] = capture;
        using (var process = LaunchService.Start(browser)) { if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Browser argument fixture timeout"); } }
        var args = JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
        check(args.SequenceEqual(new[] { web.Location }) && !args.Any(a => a.Contains("fixture-secret")), "External-browser process receives only the exact URL, including query/fragment, without credentials or thin-client switches");
    }
}
