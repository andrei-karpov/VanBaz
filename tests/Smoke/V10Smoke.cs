using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VanBaz;

internal static class V10Smoke
{
    public static void Run(string scratch, Action<bool,string> check)
    {
        string root = Path.Combine(scratch, "v10"); Directory.CreateDirectory(root);
        bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or InvalidDataException or IOException or JsonException) { return true; } }
        string exe = Path.Combine(AppContext.BaseDirectory, "1cv8.exe");
        var b = new BaseEntry { Name = "Synthetic", Executable = exe, Location = @"D:\Target Folder\", Version = "8.3", UserName = "Explicit user", EnterpriseSecret = SecretStore.Protect("explicit-secret") };
        b.V8iFields["Connect"] = "File=\"D:\\Old\";Locale=\"ru_RU\";Usr=\"old-user\";Pwd=\"old-secret\";Custom=\"part;with\"\"quote\";";
        var info = LaunchService.Build(b, false, "Авто", Array.Empty<PlatformInfo>());
        string capture = Path.Combine(root, "args.json"); info.Environment["VANBAZ_CAPTURE"] = capture;
        using (var p = LaunchService.Start(info)) { if (!p.WaitForExit(10000)) { p.Kill(); throw new Exception("Full connection stub timeout"); } }
        var actual = JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
        int at = Array.IndexOf(actual, "/IBConnectionString"); var parts = InfobaseConnection.Parse(actual[at + 1]);
        check(at >= 0 && parts["File"] == b.Location && parts["Locale"] == "ru_RU" && parts["Custom"] == "part;with\"quote" && !parts.ContainsKey("Pwd") && !parts.ContainsKey("Usr") && actual.Contains("explicit-secret") && !LaunchService.Preview(info).Contains("explicit-secret"), "Full Connect reaches process intact; selected address and explicit credentials override imported values");
        b.EnterpriseLogin = false; var prompt = LaunchService.Build(b, false, "Авто", Array.Empty<PlatformInfo>());
        check(!prompt.ArgumentList.Contains("/P") && !prompt.ArgumentList.Any(v => v.Contains("old-secret")), "Prompt-login mode removes inherited user/password from full connection");
        check(Reject(() => InfobaseConnection.Parse("File=\"a\";File=\"b\";")) && Reject(() => InfobaseConnection.Parse("File=\"unclosed")), "Duplicate and malformed full connection fields fail before launch");
        var lower = b.Copy(); lower.V8iFields = new() { ["connect"] = b.V8iFields["Connect"] };
        check(LaunchService.Build(lower.Copy(), false, "Авто", Array.Empty<PlatformInfo>()).ArgumentList.Contains("/IBConnectionString"), "Connection key remains case-insensitive after JSON/profile cloning");
        check(new[] { "/FD:\\Other", "/Sserver\\other", "/WShttps://example.invalid/other", "/IBConnectionString=File=other", "/IBNameAnother" }.All(extra => { b.ExtraArguments = extra; return Reject(() => LaunchService.Build(b, false, "Авто", Array.Empty<PlatformInfo>())); }), "Attached and equals connection keys cannot override selected base");
        b.ExtraArguments = "/Lru /L=en"; check(Reject(() => LaunchService.Build(b, false, "Авто", Array.Empty<PlatformInfo>())), "Duplicate attached and equals forms are canonicalised");
        b.ExtraArguments = "/Pprivate /UC=permit /SLev 2 /C \"/P is data\"";
        var extraInfo = LaunchService.Build(b, false, "Авто", Array.Empty<PlatformInfo>());
        check(extraInfo.ArgumentList.Contains("/SLev") && extraInfo.ArgumentList.Contains("/P is data") && !LaunchService.Preview(extraInfo).Contains("private") && !LaunchService.Preview(extraInfo).Contains("permit") && LaunchArguments.Parse(b.ExtraArguments).Count(a => a.Secret) == 2, "Known long keys and value boundaries survive parsing; attached secrets are classified and masked");
        check(Reject(() => LaunchArguments.Parse("/XUnsupported")) && Reject(() => LaunchArguments.Parse("/RestoreIBx.dt")) && Reject(() => LaunchArguments.Parse("/L")), "Unknown, maintenance and missing-value arguments are rejected");
        check(Reject(() => LaunchArguments.Parse("/L /Pprivate")) && LaunchArguments.Parse("/C==literal").Single().Value == "=literal", "A following key is not swallowed as a missing value; equals in values are preserved");
        b.ExtraArguments = "/C \"/P\" /P hidden-value";
        check(!LaunchService.Preview(LaunchService.Build(b, false, "Авто", Array.Empty<PlatformInfo>())).Contains("hidden-value"), "A parameter value resembling a secret key cannot shift redaction to the next key");
        check(Reject(() => LaunchCapabilities.ValidateKnown("Server", false, "Авто", new Version(8,3,23), new[] { "/AllowExecuteScheduledJobs" })) && Reject(() => LaunchCapabilities.ValidateKnown("File", false, "Авто", new Version(8,2), new[] { "/iTaxi" })) && Reject(() => LaunchCapabilities.ValidateKnown("File", true, "Авто", new Version(8,3), new[] { "/Execute" })), "Unsupported connection/mode/version combinations are rejected");
        b.ExtraArguments = "";
        var profile = LaunchProfile.From(b, false, "Толстый управляемый"); profile.Name = "Portable demo"; profile.BaseId = "private-id"; profile.ExtraArguments = "/Psecret-in-args"; profile.Advanced.PermissionSecret = SecretStore.Protect("permission-secret"); profile.Advanced.StartupParameter = "private-startup"; profile.RepositorySecret = SecretStore.Protect("repository-secret"); profile.RunAsAdmin = true;
        string path = Path.Combine(root, "profile.splaunch.json"); LaunchProfileExchange.Export(profile, path); string exported = File.ReadAllText(path); var imported = LaunchProfileExchange.Import(path);
        check(new[] { "private-id", "secret-in-args", "private-startup", profile.UserSecret, profile.RepositorySecret, profile.Advanced.PermissionSecret }.Where(s => s.Length > 0).All(s => !exported.Contains(s)) && imported.Id != profile.Id && imported.BaseId == "" && imported.UserSecret == "" && imported.Executable == "" && !imported.RunAsAdmin && imported.AskLogin, "Portable profile has no credential blobs, bindings, raw arguments or paths and requires login");
        File.WriteAllText(path, exported.TrimEnd().TrimEnd('}') + ",\"UserSecret\":\"injected\"}"); check(Reject(() => LaunchProfileExchange.Import(path)), "Portable import refuses fields outside its allowlist");
        string cfg = Path.Combine(root, "1cestart.cfg"); File.WriteAllText(cfg, "; comment\nDefaultVersion=8.3=8.3.24.100\nUnused=private-secret\nDefaultVersion=8.2=8.2.19.10\n", Encoding.Unicode); var before = File.ReadAllBytes(cfg); string explained = StarterConfiguration.Explain(new[] { cfg });
        check(explained.Contains("8.3.24.100") && explained.Contains("8.2.19.10") && !explained.Contains("private-secret") && before.SequenceEqual(File.ReadAllBytes(cfg)), "DefaultVersion explanation reads all mappings without exposing unrelated settings or writing cfg");
        var settings = new Preferences { PlatformRoots = new() { "A", "B" }, DisabledPlatformRoots = new() { "a" } };
        using var stop = new CancellationTokenSource(); stop.Cancel(); bool cancelled = false; try { PlatformCatalog.Scan(new[] { root }, false, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && PlatformCatalog.EnabledRoots(settings).SequenceEqual(new[] { "B" }), "Disabled platform roots are excluded and cancelled scans stop before traversal");
        string first = Path.Combine(root, "catalog-synthetic.json"), second = Path.Combine(root, "ibases-synthetic.v8i"), journal = Path.Combine(root, "journal"); File.WriteAllText(first, "old catalog"); File.WriteAllText(second, "old list");
        var op = FileRecovery.Begin(journal, "Synthetic sync", new Dictionary<string,byte[]?> { [first] = Encoding.UTF8.GetBytes("new catalog"), [second] = Encoding.UTF8.GetBytes("new list") });
        File.WriteAllText(second, "new list"); // Simulated termination between the two independent replaces.
        var resumed = FileRecovery.Pending(journal).Single(); FileRecovery.Restore(resumed);
        check(File.ReadAllText(first) == "old catalog" && File.ReadAllText(second) == "old list" && FileRecovery.Pending(journal).Count == 0, "Persistent journal recovers a partially written pair after reload");
        op = FileRecovery.Begin(journal, "Synthetic conflict", new Dictionary<string,byte[]?> { [first] = Encoding.UTF8.GetBytes("new") }); File.WriteAllText(first, "third-party edit");
        check(Reject(() => FileRecovery.Restore(op)) && File.ReadAllText(first) == "third-party edit", "Recovery does not overwrite an edit made after interrupted operation");
        string created = Path.Combine(root, "new.pfl"); op = FileRecovery.Begin(journal, "Synthetic creation", new Dictionary<string,byte[]?> { [created] = Encoding.UTF8.GetBytes("new profile") }); File.WriteAllText(created, "new profile"); FileRecovery.Restore(op);
        check(!File.Exists(created), "Recovery records absence and removes only the file created by the operation");
        var context = new ProfileContext { Roaming = Path.Combine(root,"Roaming"), Local = Path.Combine(root,"Local"), BaseId = Guid.NewGuid().ToString() };
        string key = "base/1Cv8.pfl", file = ProfileArchive.List(context).Single(f => f.Key == key).Path; Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file,"payload"); string zip = Path.Combine(root,"pfl.zip"); ProfileArchive.Export(context,new[]{key},zip); File.Delete(file);
        string legacyZip = Path.Combine(root,"pfl-v1.zip"); File.Copy(zip,legacyZip);
        using (var legacy = ZipFile.Open(legacyZip, ZipArchiveMode.Update))
        {
            var entry = legacy.GetEntry("manifest.json")!; System.Text.Json.Nodes.JsonNode manifest;
            using (var json = entry.Open()) manifest = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            manifest["Version"] = 1; manifest.AsObject().Remove("AbsentFiles"); entry.Delete();
            using var jsonOut = legacy.CreateEntry("manifest.json").Open(); JsonSerializer.Serialize(jsonOut,manifest);
        }
        check(ProfileArchive.Read(legacyZip).Data.ContainsKey(key), "PFL archives from 0.9 (manifest v1) remain readable");
        string undo = ProfileArchive.Change(context,new[]{key},zip,Path.Combine(root,"pfl-recovery"),()=>false);
        check(ProfileArchive.Read(undo).Manifest.AbsentFiles.Contains(key) && File.Exists(file), "PFL backup records that a restored destination did not previously exist");
        ProfileArchive.Change(context,new[]{key},undo,Path.Combine(root,"pfl-recovery"),()=>false);
        check(!File.Exists(file), "Restoring PFL backup returns an originally absent destination to absence");
    }
}
