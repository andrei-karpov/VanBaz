using System.IO;
using System.Text;
using VanBaz;

internal static class V11Smoke
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        string root = Path.Combine(scratch, "v11"); Directory.CreateDirectory(root);
        ImportResult Parse(string connect) => V8iImporter.Parse("[Synthetic]\nID=abc-def\nConnect=" + connect + "\n");
        check(Parse("File=\"first\";File=\"second\";").Skipped == 1 && Parse("File=\"unclosed").Skipped == 1, "Import rejects duplicate or incomplete Connect instead of accepting an unusable registration");
        check(Parse("File=\"first\";Srvr=\"server\";Ref=\"other\";").Skipped == 1 && Parse("Srvr=\"server\";Ref=\"\";").Skipped == 1, "Ambiguous and incomplete endpoints cannot silently select a different base");
        var spaced = Parse("Srvr=\"server\";Ref=\" name with space \";Locale=\"ru_RU\";").Entries.Single();
        check(spaced.Location == "server/ name with space " && InfobaseConnection.Parse(V8iImporter.Connection(spaced))["Ref"] == " name with space ", "Quoted endpoint values survive import and export without trimming their contents");
        var privateRecord = new V8iRecord { Id = "synthetic", Name = "Synthetic", Fields = new() { ["Connect"] = "File=\"D:\\Synthetic\";Pwd=\"fixture-secret\";Token=\"fixture-token\";", ["AdditionalParameters"] = "/Pfixture-argument-secret", ["Password"] = "fixture-field-secret" } };
        string detail = new ReconcileRow { Local = privateRecord }.Detail;
        check(new[] { "fixture-secret", "fixture-token", "fixture-argument-secret", "fixture-field-secret" }.All(s => !detail.Contains(s)) && detail.Contains(@"D:\Synthetic") && privateRecord.Text.Contains("fixture-secret"), "Reconcile detail hides secret values while preserving the address and original record");
        string list = Path.Combine(root, "ibases.v8i"); File.WriteAllText(list, "[Synthetic]\nID=abc-def\nConnect=File=\"D:\\Synthetic\";\n", Encoding.Unicode);
        var catalog = new Catalog(); V8iImporter.Merge(catalog, V8iImporter.Read(list));
        File.WriteAllText(list, File.ReadAllText(list).Replace("ID=abc-def", "ID=ABC-DEF"), Encoding.Unicode);
        check(CatalogReconciler.Prepare(catalog, list).Rows.Count == 0, "Case-only registration ID changes do not create permanent reconcile conflicts");
        var store = new CatalogStore(Path.Combine(root, "new-catalog")); var empty = new Catalog(); bool saved = false;
        try { var result = CatalogReconciler.Apply(empty, store, CatalogReconciler.Prepare(empty, list)); saved = result.Catalog.Bases.Count == 1 && store.Load().Bases.Count == 1; } catch (InvalidOperationException) { }
        check(saved, "First reconciliation creates a new catalog without requiring a nonexistent snapshot");
        string oldRoot = Path.Combine(root, "old-platforms"), newRoot = Path.Combine(root, "new-platforms");
        var oldPlatform = new PlatformInfo("8.3.20.1", Path.Combine(oldRoot, "bin"), "x64");
        var newPlatform = new PlatformInfo("8.3.24.1", Path.Combine(newRoot, "bin"), "x64");
        var late = new TaskCompletionSource<PlatformScan>();
        var inventory = new PlatformInventory(new[] { oldPlatform }, (_, _) => late.Task);
        var oldSettings = new Preferences { PlatformRoots = new() { oldRoot } }; var running = inventory.RefreshAsync(oldSettings);
        var newSettings = new Preferences { PlatformRoots = new() { oldRoot, newRoot }, DisabledPlatformRoots = new() { oldRoot } };
        inventory.RefreshAsync(newSettings, new(new() { oldPlatform, newPlatform }, new())).GetAwaiter().GetResult();
        check(inventory.Platforms.SequenceEqual(new[] { newPlatform }), "Saving disabled roots immediately removes their cached platforms from automatic selection");
        late.SetResult(new(new() { oldPlatform }, new())); running.GetAwaiter().GetResult();
        check(inventory.Platforms.SequenceEqual(new[] { newPlatform }) && inventory.Matches(newSettings), "Late scan result cannot overwrite newer platform settings");
        var waiting = new TaskCompletionSource<PlatformScan>();
        var rescan = new PlatformInventory(new[] { oldPlatform }, (_, _) => waiting.Task);
        var refreshing = rescan.RefreshAsync(newSettings);
        check(rescan.Platforms.Count == 0, "Changing roots without a prepared scan removes excluded platforms before background search completes");
        waiting.SetResult(new(new() { newPlatform }, new())); refreshing.GetAwaiter().GetResult();
        check(rescan.Platforms.SequenceEqual(new[] { newPlatform }) && PlatformCatalog.WithinRoots(new[] { new PlatformInfo("8.3", newRoot + "-other", "x64") }, new[] { newRoot }).Count == 0, "New roots are applied after background scan and sibling paths are not mistaken for children");
        string strict = Path.Combine(root, "invalid.v8i"); File.WriteAllText(strict, "[Bad]\nID=bad\nConnect=File=\"a\";File=\"b\";\n");
        bool refused = false; try { CatalogReconciler.Prepare(catalog, strict); } catch (InvalidDataException) { refused = true; }
        check(refused, "Reconciliation shares strict Connect parsing and refuses the whole malformed source");
        var url = new V8iRecord { Name = "Web", Fields = new() { ["Connect"] = "ws=\"https://user:fixture-url-secret@example.invalid/base?token=fixture-query-secret#fixture-fragment\";" } };
        check(!url.PreviewText.Contains("fixture-") && url.PreviewText.Contains("example.invalid/base"), "Reconcile web address preview removes userinfo, query and fragment values");
        bool Reject(string input) { try { LaunchArguments.Parse(input); return false; } catch (ArgumentException) { return true; } }
        check(LaunchArguments.Parse("/Debug").Single().Key == "/Debug" && LaunchArguments.Parse("/Debug /L ru").Select(a => a.Key).SequenceEqual(new[] { "/Debug", "/L" }), "Debug without optional mode does not consume the following key");
        check(LaunchArguments.Parse("/Debug -http -attach /DebuggerURL localhost:1550").Select(a => a.Key).SequenceEqual(new[] { "/Debug", "-http", "-attach", "/DebuggerURL" }) && Reject("/Debug -tcp -attach") && Reject("/Debug -http -tcp"), "Debug protocol and attach modifiers follow documented syntax and reject contradictory modes");
        var synthetic = new BaseEntry { Executable = Path.Combine(AppContext.BaseDirectory, "1cv8.exe"), Location = @"D:\Synthetic", ExtraArguments = "/Debug -http -attach /L ru /P fixture-debug-secret" };
        var command = LaunchService.Build(synthetic, false, "Авто", Array.Empty<PlatformInfo>());
        string capture = Path.Combine(root, "debug-args.json"); command.Environment["VANBAZ_CAPTURE"] = capture;
        using (var process = LaunchService.Start(command)) { if (!process.WaitForExit(10000)) { process.Kill(); throw new Exception("Debug argument fixture timeout"); } }
        var received = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(capture))!;
        check(received.Contains("/Debug") && received.Contains("-http") && received.Contains("-attach") && received.Contains("fixture-debug-secret") && !LaunchService.Preview(command).Contains("fixture-debug-secret"), "Stub receives debug modifiers as separate arguments and preview still hides the following password");
        var pflContext = new ProfileContext { Roaming = Path.Combine(root, "Roaming"), Local = Path.Combine(root, "Local"), Edition = "1cv82", BaseId = Guid.NewGuid().ToString(), UserId = Guid.NewGuid().ToString() };
        var files = ProfileArchive.List(pflContext);
        check(files.Single(f => f.Key == "local/1cv8u.pfl").Path == Path.Combine(pflContext.Local, "1C", "1cv8", "1cv8u.pfl") && files.Single(f => f.Key == "global/1Cv8.pfl").Path.Contains(Path.Combine("1C", "1cv82")), "Local identity PFL uses 1cv8 independently of the selected Roaming edition, matching IR paths");
        check(files.Count == 15 && files.All(f => f.Description.Length > 20) && files.Single(f => f.Key == "base/def.usr").Description.Contains("имя пользователя"), "Every allowed PFL file has a purpose description for selective reset");
        var exportFile = files.Single(f => f.Key == "base/def.usr"); Directory.CreateDirectory(Path.GetDirectoryName(exportFile.Path)!); File.WriteAllText(exportFile.Path, "first");
        string archive = Path.Combine(root, "replace-profile.zip"); ProfileArchive.Export(pflContext, new[] { exportFile.Key }, archive);
        File.WriteAllText(exportFile.Path, "second"); bool preserved = false;
        try { ProfileArchive.Export(pflContext, new[] { exportFile.Key }, archive); } catch (IOException) { preserved = Encoding.UTF8.GetString(ProfileArchive.Read(archive).Data[exportFile.Key]) == "first"; }
        ProfileArchive.Export(pflContext, new[] { exportFile.Key }, archive, overwrite: true);
        check(preserved && Encoding.UTF8.GetString(ProfileArchive.Read(archive).Data[exportFile.Key]) == "second", "PFL export preserves an existing archive by default and replaces it only through the explicit overwrite path");
        var importedCatalog = store.Load(); importedCatalog.Bases.Single().Name = "Local change";
        var readOnly = CatalogReconciler.Prepare(importedCatalog, list); var sourceBefore = File.ReadAllBytes(list);
        check(!readOnly.CanWriteSource && !readOnly.AllowedChoices.Contains(CatalogReconciler.Choices[2]) && readOnly.Rows.Single().Choice == CatalogReconciler.Choices[0], "An external list defaults to read-only and never preselects a write");
        readOnly.Rows.Single().Choice = CatalogReconciler.Choices[2]; bool writeRefused = false;
        try { CatalogReconciler.Apply(importedCatalog, store, readOnly); } catch (InvalidDataException) { writeRefused = true; }
        check(writeRefused && sourceBefore.SequenceEqual(File.ReadAllBytes(list)), "Backend rejects a forged write action on a read-only source before modifying files");
        readOnly.Rows.Single().Choice = CatalogReconciler.Choices[1]; importedCatalog = CatalogReconciler.Apply(importedCatalog, store, readOnly).Catalog;
        check(sourceBefore.SequenceEqual(File.ReadAllBytes(list)) && store.Load().Bases.Single().ListSources.Contains(list), "Read-only reconciliation imports data and persists its source without writing the external list");
        string secondSource = Path.Combine(root,"second-source.v8i"); File.Copy(list,secondSource);
        V8iImporter.Merge(importedCatalog, V8iImporter.Read(secondSource)); ListSourceService.Merge(importedCatalog, V8iImporter.Read(secondSource));
        check(importedCatalog.Bases.Count == 1 && importedCatalog.Bases[0].ListSources.Count == 2, "Repeated imports track multiple sources without duplicating the registration or origin path");
        var difference = new ReconcileRow { Local = privateRecord, External = new() { Name = privateRecord.Name, Fields = new() { ["Connect"] = "File=\"D:\\Synthetic\";Pwd=\"other-fixture-secret\";" } } };
        check(difference.DifferenceFields.Contains("Connect") && !difference.Detail.Contains("other-fixture-secret"), "Masked connection changes still identify which field differs for review");
        var firstProfile = LaunchProfile.From(synthetic, false, "Обычный режим"); firstProfile.ExtraArguments = ""; firstProfile.Advanced.Language = "ru";
        var secondProfile = LaunchProfile.From(synthetic, false, "Толстый управляемый"); secondProfile.ExtraArguments = ""; secondProfile.Advanced.Language = "en";
        string unchanged = System.Text.Json.JsonSerializer.Serialize(synthetic);
        var firstCommand = LaunchService.Build(firstProfile.Apply(synthetic), false, firstProfile.Client, Array.Empty<PlatformInfo>());
        var secondCommand = LaunchService.Build(secondProfile.Apply(synthetic), false, secondProfile.Client, Array.Empty<PlatformInfo>());
        check(firstCommand.ArgumentList.Contains("ru") && secondCommand.ArgumentList.Contains("en") && firstCommand.ArgumentList.Contains("/RunModeOrdinaryApplication") && secondCommand.ArgumentList.Contains("/RunModeManagedApplication") && unchanged == System.Text.Json.JsonSerializer.Serialize(synthetic), "Two profiles produce distinct launch commands without changing their shared base registration");
        string originalProfile = System.Text.Json.JsonSerializer.Serialize(firstProfile); var oneRun = firstProfile.CopyForRun(); oneRun.Advanced.Language = "de";
        check(oneRun.Id != firstProfile.Id && originalProfile == System.Text.Json.JsonSerializer.Serialize(firstProfile) && LaunchService.Build(oneRun.Apply(synthetic), false, oneRun.Client, Array.Empty<PlatformInfo>()).ArgumentList.Contains("de"), "One-run overrides have a new identity and cannot mutate the saved profile or its nested settings");
        firstProfile.BaseId = synthetic.Id.ToUpperInvariant();
        check(firstProfile.AppliesTo(synthetic) && !firstProfile.AppliesTo(new BaseEntry { Id = synthetic.Id, Version = "7.7" }), "Profile menus match ID case-insensitively and exclude incompatible 7.7/8.x families");
        string recoveryRoot = Path.Combine(root, "completion-recovery");
        var recovery = FileRecovery.Begin(recoveryRoot, "Synthetic completion failure", new Dictionary<string, byte[]?> { [list] = File.ReadAllBytes(list) });
        bool completionFailed = false;
        using (var hold = new FileStream(Path.Combine(recovery.Folder, "operation.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            try { FileRecovery.Complete(recovery); } catch (IOException) { completionFailed = true; }
        check(completionFailed && !recovery.Completed && FileRecovery.Pending(recoveryRoot).Count == 1, "Failed journal replacement keeps both in-memory and persisted operation pending");
        importedCatalog.Bases.Single().Name = "Written despite journal error";
        var exportPlan = CatalogReconciler.Prepare(importedCatalog, list, allowSourceWrite: true);
        exportPlan.Rows.Single().Choice = CatalogReconciler.Choices[2];
        var committed = CatalogReconciler.Apply(importedCatalog, store, exportPlan, _ => throw new IOException("synthetic journal failure"));
        check(committed.Warning != null && committed.Catalog.Bases.Single().Name == store.Load().Bases.Single().Name && V8iImporter.Read(list).Entries.Single().Name == "Written despite journal error" && FileRecovery.Pending(Path.Combine(store.Folder,"RecoveryOperations")).Count == 1, "A journal-finalization failure returns the committed catalog to UI with a warning rather than stale state");
    }
}
