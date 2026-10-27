using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VanBaz;

internal static class V09Smoke
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string folder = Path.Combine(scratch, "v09"); Directory.CreateDirectory(folder);
        static bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return true; } }
        const string input = "; сохранить комментарий\r\n[Клиенты]\r\nID=group\r\nFolder=/\r\nCustomGroup=unknown\r\n[Одна база]\r\nID=one\r\nFolder=/Клиенты\r\nConnect=File=\"D:\\Synthetic\";Locale=\"ru_RU\";\r\nVersion=8.3\r\nUnknownValue=неизвестное\r\n[Другая регистрация]\r\nID=two\r\nConnect=File=\"D:\\Synthetic\";\r\nVersion=8.3\r\n";
        string file = Path.Combine(folder, "ibases.v8i"); File.WriteAllText(file, input, Encoding.GetEncoding(1251));
        var c = new Catalog(); V8iImporter.Merge(c, V8iImporter.Read(file)); V8iImporter.Merge(c, V8iImporter.Read(file));
        var missing = V8iImporter.Parse("[Без ID]\nConnect=File=\"D:\\Other\";\n"); var missingAgain = V8iImporter.Parse("[Без ID]\nConnect=File=\"D:\\Other\";\n");
        check(c.Bases.Count == 2 && missing.Entries[0].Id == missingAgain.Entries[0].Id, "Distinct registrations survive repeated import; missing ID is stable");
        var store = new CatalogStore(Path.Combine(folder, "catalog")); store.Save(c);
        c = CatalogReconciler.Apply(c, store, CatalogReconciler.Prepare(c, file, allowSourceWrite: true)).Catalog;
        c.Bases[0].Name = "Новое имя"; c.Bases[0].Description = "Private app-only note"; c.Bases[0].Executable = "pinned.exe";
        var rename = CatalogReconciler.Prepare(c, file, allowSourceWrite: true);
        check(rename.Rows.Count == 1 && rename.Rows[0].Choice == CatalogReconciler.Choices[2], "Tracked local rename proposes only one export");
        var applied = CatalogReconciler.Apply(c, store, rename); c = applied.Catalog;
        string text = V8iImporter.ReadText(file);
        check(text.Contains("; сохранить комментарий") && text.Contains("UnknownValue=неизвестное") && text.Contains("CustomGroup=unknown") && text.Contains("Locale=\"ru_RU\"") && applied.Backup != null && File.Exists(applied.Backup) && CatalogReconciler.Prepare(c, file, allowSourceWrite: true).Rows.Count == 0, "v8i preserves comments, encoding, unknown connection/group fields and is idempotent");
        File.WriteAllText(file, text.Replace("[Новое имя]", "[Внешнее имя]"), Encoding.GetEncoding(1251));
        var incoming = CatalogReconciler.Prepare(c, file, allowSourceWrite: true); c = CatalogReconciler.Apply(c, store, incoming).Catalog;
        check(c.Bases.Single(b => b.Id == "one") is { Name: "Внешнее имя", Description: "Private app-only note", Executable: "pinned.exe" }, "External edits preserve VanBaz-only properties");
        c.Bases.Single(b => b.Id == "one").Name = "Локальное имя";
        File.WriteAllText(file, V8iImporter.ReadText(file).Replace("[Внешнее имя]", "[Ещё одно имя]"), Encoding.GetEncoding(1251));
        var conflict = CatalogReconciler.Prepare(c, file, allowSourceWrite: true); byte[] before = File.ReadAllBytes(file);
        check(conflict.Rows.Single().Change.StartsWith("Конфликт") && conflict.Rows.Single().Choice == CatalogReconciler.Choices[0], "Concurrent changes require an explicit direction");
        c = CatalogReconciler.Apply(c, store, conflict).Catalog;
        check(before.SequenceEqual(File.ReadAllBytes(file)) && CatalogReconciler.Prepare(c, file, allowSourceWrite: true).Rows.Single().Change.StartsWith("Конфликт"), "Skipping a conflict does not consume its baseline");
        var stale = CatalogReconciler.Prepare(c, file, allowSourceWrite: true); File.AppendAllText(file, "; another writer\n", Encoding.GetEncoding(1251));
        byte[] changed = File.ReadAllBytes(file); check(Reject(() => CatalogReconciler.Apply(c, store, stale)) && changed.SequenceEqual(File.ReadAllBytes(file)), "Stale plans cannot overwrite another writer");
        var resolve = CatalogReconciler.Prepare(c, file, allowSourceWrite: true); resolve.Rows.Single().Choice = CatalogReconciler.Choices[1]; c = CatalogReconciler.Apply(c, store, resolve).Catalog;
        c.Bases.RemoveAll(b => b.Id == "two"); var deletion = CatalogReconciler.Prepare(c, file, allowSourceWrite: true);
        check(deletion.Rows.Single().Choice == CatalogReconciler.Choices[0], "Deletion is never preselected"); deletion.Rows.Single().Choice = CatalogReconciler.Choices[2];
        c = CatalogReconciler.Apply(c, store, deletion).Catalog;
        string invalid = Path.Combine(folder, "broken.v8i"); File.WriteAllText(invalid, "[Broken]\nID=one\nConnect=Unknown=1;\n");
        check(V8iImporter.Read(file).Entries.Count == 1 && CatalogReconciler.Prepare(c, file, allowSourceWrite: true).Rows.Count == 0 && Reject(() => CatalogReconciler.Prepare(c, invalid)), "Chosen deletion converges; malformed lists are rejected");

        string exe = Path.Combine(AppContext.BaseDirectory, "1cv8.exe"), thin = Path.Combine(AppContext.BaseDirectory, "1cv8c.exe"); File.Copy(exe, thin, true);
        try
        {
            var b = new BaseEntry { Executable = exe, Location = @"D:\Synthetic Folder\", UserName = "Пользователь", Client = "Тонкий клиент", EnterpriseSecret = SecretStore.Protect("secret") };
            check(LaunchService.Build(b, false, "Авто", Array.Empty<PlatformInfo>()).FileName == thin, "Automatic session honours saved client");
            var auto = b.Copy(); auto.Client = "Авто";
            check(LaunchService.Build(auto, false, "Авто", Array.Empty<PlatformInfo>()).ArgumentList.Contains("/AppAutoCheckMode") && !LaunchService.Build(auto, true, "Авто", Array.Empty<PlatformInfo>()).ArgumentList.Contains("/AppAutoCheckMode"), "Automatic client mode is passed to 1C only for Enterprise");
            var web = b.Copy(); web.Kind = "Web"; web.Location = "https://example.invalid/base";
            check(LaunchService.Build(web, false, "Авто", Array.Empty<PlatformInfo>()).FileName == thin, "Pinned thick executable is corrected for web thin client");
            var profile = LaunchProfile.From(b, false, "Толстый управляемый"); profile.Name = "Проверка"; profile.Advanced = new() { Language = "ru", StartupParameter = "mode one", DataArea = "-0,+1", PermissionSecret = SecretStore.Protect("permission-secret"), ClearCache = true, DisableScheduledJobs = true, Taxi = true };
            string original = JsonSerializer.Serialize(b); var launch = profile.Apply(b); var info = LaunchService.Build(launch, false, profile.Client, Array.Empty<PlatformInfo>());
            string capture = Path.Combine(folder, "profile-arguments.json"); info.Environment["VANBAZ_CAPTURE"] = capture;
            using (var process = LaunchService.Start(info)) { if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Profile stub timeout"); } }
            var arguments = JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
            check(arguments.Contains("/RunModeManagedApplication") && arguments.Contains("/L") && arguments.Contains("mode one") && arguments.Contains("permission-secret") && arguments.Contains("-Off") && arguments.Contains(b.Location) && !LaunchService.Preview(info).Contains("permission-secret") && original == JsonSerializer.Serialize(b), "Profile fields reach real stub with exact argument boundaries, redacted preview and unchanged base");
            launch.ExtraArguments = "/L en"; check(Reject(() => LaunchService.Build(launch, false, profile.Client, Array.Empty<PlatformInfo>())), "Conflicting typed and manual keys are rejected");
            var preview = new ProcessStartInfo(exe); foreach (var arg in new[] { "/UCpermission", "/Ppassword", "/WSP=websecret" }) preview.ArgumentList.Add(arg);
            check(!LaunchService.Preview(preview).Contains("permission") && !LaunchService.Preview(preview).Contains("password") && !LaunchService.Preview(preview).Contains("websecret"), "Attached secret values are hidden in preview");
            var browser = WebClientLauncher.Build(web, "Edge", exe); web.Location = "https://user:password@example.invalid/base";
            check(browser.ArgumentList.SequenceEqual(new[] { "https://example.invalid/base" }) && Reject(() => WebClientLauncher.Build(web, "Edge", exe)), "Browser launch contains only URL and rejects embedded credentials");
            var scan = PlatformCatalog.Scan(new[] { AppContext.BaseDirectory, Path.Combine(folder, "absent") }, false);
            check(scan.Platforms.Any(p => Path.GetFullPath(p.Directory).TrimEnd('\\') == Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\') && p.Architecture == PlatformCatalog.Architecture(exe)) && scan.Errors.Count == 1, "Custom platform scan reads EXE version/architecture and reports inaccessible roots");
        }
        finally { File.Delete(thin); }

        var source = new ProfileContext { Roaming = Path.Combine(folder, "source", "Roaming"), Local = Path.Combine(folder, "source", "Local"), BaseId = Guid.NewGuid().ToString(), UserId = Guid.NewGuid().ToString() };
        var target = new ProfileContext { Roaming = Path.Combine(folder, "target", "Roaming"), Local = Path.Combine(folder, "target", "Local"), BaseId = Guid.NewGuid().ToString(), UserId = Guid.NewGuid().ToString() };
        string[] keys = { "base/1Cv8.pfl", "user/1Cv8c.pfl" };
        foreach (var f in ProfileArchive.List(source).Where(f => keys.Contains(f.Key))) { Directory.CreateDirectory(Path.GetDirectoryName(f.Path)!); File.WriteAllText(f.Path, "original " + f.Key); }
        string archive = Path.Combine(folder, "profiles.zip"), recovery = Path.Combine(folder, "recovery"); ProfileArchive.Export(source, keys, archive);
        ProfileArchive.Change(target, keys, archive, recovery, () => false);
        var destinations = ProfileArchive.List(target).Where(f => keys.Contains(f.Key)).ToList();
        check(destinations.All(f => File.ReadAllText(f.Path) == "original " + f.Key) && destinations.All(f => f.Path.Contains(target.BaseId) && !f.Path.Contains(source.BaseId)), "Profile restore maps logical paths to different base and user GUIDs");
        string reset = ProfileArchive.Change(target, keys, null, recovery, () => false);
        check(destinations.All(f => !File.Exists(f.Path)), "Selective profile reset removes only selected files after backup");
        ProfileArchive.Change(target, keys, reset, recovery, () => false);
        byte[] prior = File.ReadAllBytes(destinations[0].Path);
        using (var held = new FileStream(destinations[1].Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            check(Reject(() => ProfileArchive.Change(target, keys, null, recovery, () => false)) && prior.SequenceEqual(File.ReadAllBytes(destinations[0].Path)), "Busy destination prevents partial profile reset");
        string poisoned = Path.Combine(folder, "poisoned.zip"); File.Copy(archive, poisoned); using (var zip = ZipFile.Open(poisoned, ZipArchiveMode.Update)) { using var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open()); writer.Write("untrusted"); }
        check(Reject(() => ProfileArchive.Change(target, keys, poisoned, recovery, () => false)) && prior.SequenceEqual(File.ReadAllBytes(destinations[0].Path)) && Reject(() => ProfileArchive.Change(target, keys, null, recovery, () => true)), "Unknown archive files and active clients are rejected without modifying profiles");
        var oldCatalog = new Catalog { SchemaVersion = 2, Bases = new() { new() { Id = "persistent" } } }; File.WriteAllText(store.FilePath, JsonSerializer.Serialize(oldCatalog)); var loaded = store.Load();
        loaded.Settings.BaseKeys["1"] = "persistent"; loaded.Bases[0].Name = "Renamed"; store.Save(loaded);
        check(store.Load().Settings.BaseKeys["1"] == "persistent" && store.Load().LaunchProfiles.Count == 0 && store.Load().SchemaVersion == 3 && Directory.EnumerateFiles(Path.Combine(store.Folder, "Snapshots"), "*.json").Any(p => JsonDocument.Parse(File.ReadAllText(p)).RootElement.GetProperty("SchemaVersion").GetInt32() == 2), "Old catalog is backed up before v3 migration; base key is stable across rename/save");
        check(GlobalHotkeys.Parse("Ctrl+Alt+1").Key == 0x31, "Numeric keyboard binding resolves to digit 1, not enum value 1");
    }
}
