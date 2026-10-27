using System.IO;
using System.Text;
using VanBaz;

internal static class V13Smoke
{
    public static void Run(string scratch, Action<bool, string> check)
    {
        string root = Path.Combine(scratch, "official-lists"); Directory.CreateDirectory(root);
        string personal = Path.Combine(root, "ibases.v8i"), shared = Path.Combine(root, "corporate.v8i"), cfg = Path.Combine(root, "1cestart.cfg");
        string Base(string id, string name, string folder = "/Work", string location = "D:\\Synthetic") => $"[{name}]\nID={id}\nConnect=File=\"{location}\";\nFolder={folder}\nVersion=8.3\nApp=Auto\n";
        File.WriteAllText(shared, "[Work]\nID=shared-group\nFolder=/\n" + Base("corp", "Corporate"));
        File.WriteAllText(cfg, "; ignored\nCommonInfoBases=\"corporate.v8i\"\nCommonInfoBases=corporate.v8i\n");
        File.WriteAllText(personal, "; preserve comment\n" + Base("own", "Personal") + "UnknownField=preserved\n", Encoding.Unicode);
        var initial = new Catalog { Bases = new() { new() { Id = "own", Name = "Old", Location = @"D:\Old", Favorite = true, Notes = "private-note", EnterpriseSecret = "encrypted-fixture" }, new() { Id = "local-only", Name = "Old local", Location = @"D:\Local" } } };
        var store = new CatalogStore(Path.Combine(root, "data")); store.Save(initial);
        OfficialListSession Load() => OfficialListSession.Load(personal, new[] { cfg });
        var session = Load(); var c = session.Refresh(initial); store.Save(c);
        check(session.Sources.Count == 2 && c.Bases.Count == 3 && c.Groups.Single().Path == "Work" && c.Bases.Single(b => b.Id == "own") is { Name: "Personal", Favorite: true, Notes: "private-note" }, "Official discovery, relative/repeated cfg source, groups and local-only records; favorites remain local");
        byte[] original = File.ReadAllBytes(personal), corporateBytes = File.ReadAllBytes(shared);
        var edit = c.Copy(); edit.Bases.Single(b => b.Id == "own").Favorite = false; edit.Bases.Single(b => b.Id == "own").Notes = "new-private-note";
        c = session.SaveChanges(c, edit, store);
        check(File.ReadAllBytes(personal).SequenceEqual(original) && store.Load().Bases.Single(b => b.Id == "own").Notes == "new-private-note", "Local-only edit leaves exact official bytes unchanged");
        edit = c.Copy(); edit.Groups.Add(new() { Id = "personal-group", Path = "My clients", Color = "#123456" });
        edit.Bases.Single(b => b.Id == "corp").Group = "My clients";
        c = session.SaveChanges(c, edit, store);
        var imported = V8iImporter.Read(personal);
        check(imported.Entries.Single(b => b.Id == "corp").V8iFields["External"] == "1" && imported.Groups.Any(g => g.Path == "My clients") && File.ReadAllBytes(shared).SequenceEqual(corporateBytes), "Corporate placement writes a personal External overlay, never the corporate file");
        File.WriteAllText(shared, "[Work]\nID=shared-group\nFolder=/\n" + Base("corp", "Corporate", location: "D:\\New corporate address"));
        session = Load(); c = session.Refresh(c); store.Save(c);
        check(c.Bases.Single(b => b.Id == "corp") is { Location: "D:\\New corporate address", Group: "My clients" }, "Corporate connection refresh keeps personal placement");
        edit = c.Copy(); edit.Bases.Single(b => b.Id == "own").Name = "Renamed"; edit.Bases.Single(b => b.Id == "own").Group = "My clients";
        edit.Bases.Add(new() { Id = "added", Name = "Added", Location = @"D:\New", Group = "My clients", Favorite = true, Notes = "never export", EnterpriseSecret = "private-secret" });
        c = session.SaveChanges(c, edit, store);
        string text = V8iImporter.ReadText(personal);
        check(text.Contains("[Renamed]") && text.Contains("[Added]") && text.Contains("UnknownField=preserved") && text.Contains("; preserve comment") && File.ReadAllBytes(personal).Take(2).SequenceEqual(new byte[] {255,254}) && !text.Contains("private") && !text.Contains("Favorite") && !text.Contains("never export"), "New registrations, names/groups write through; comments/encoding/unknown fields survive, local secrets do not leak");
        // The native starter changes the connection after our view was loaded.
        File.WriteAllText(personal, text.Replace("D:\\Synthetic", "D:\\NativeChanged"), Encoding.Unicode);
        edit = c.Copy(); edit.Bases.Single(b => b.Id == "own").Group = "Another group";
        c = session.SaveChanges(c, edit, store);
        check(c.Bases.Single(b => b.Id == "own") is { Location: "D:\\NativeChanged", Group: "Another group" }, "Unrelated concurrent official changes merge with the edited field");
        text = V8iImporter.ReadText(personal); File.WriteAllText(personal, text.Replace("[Renamed]", "[Native rename]"), Encoding.Unicode);
        edit = c.Copy(); edit.Bases.Single(b => b.Id == "own").Name = "Conflicting rename";
        byte[] conflictBytes = File.ReadAllBytes(personal), jsonBefore = File.ReadAllBytes(store.FilePath);
        bool rejected = false; try { session.SaveChanges(c, edit, store); } catch (IOException) { rejected = true; }
        check(rejected && File.ReadAllBytes(personal).SequenceEqual(conflictBytes) && File.ReadAllBytes(store.FilePath).SequenceEqual(jsonBefore), "Same-field concurrent edit rejects without changing either file");
        session = Load(); c = session.Refresh(c); store.Save(c);
        edit = c.Copy(); edit.Bases.Single(b => b.Id == "corp").Location = @"D:\Forbidden";
        rejected = false; try { session.SaveChanges(c, edit, store); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && File.ReadAllBytes(personal).SequenceEqual(conflictBytes), "Corporate connection is read-only while personal placement remains editable");
        edit = c.Copy(); edit.Bases.RemoveAll(b => b.Id == "added"); c = session.SaveChanges(c, edit, store);
        check(!V8iImporter.Read(personal).Entries.Any(b => b.Id == "added") && !c.Bases.Any(b => b.Id == "added"), "Deleting an owned registration removes it from the official list");
        edit = c.Copy(); edit.Groups.Single(g => g.Id == "personal-group").Path = "Renamed group";
        foreach (var b in edit.Bases.Where(b => b.Group == "My clients")) b.Group = "Renamed group";
        c = session.SaveChanges(c, edit, store); imported = V8iImporter.Read(personal);
        check(imported.Groups.Single(g => g.Id == "personal-group").Path == "Renamed group" && imported.Entries.Single(b => b.Id == "corp").Group == "Renamed group" && c.Groups.Single(g => g.Id == "personal-group").Color == "#123456", "Renaming personal group updates native group and corporate placement while retaining local color");
        edit = c.Copy(); edit.Groups.RemoveAll(g => g.Id == "personal-group");
        foreach (var b in edit.Bases.Where(b => b.Group == "Renamed group")) b.Group = "";
        c = session.SaveChanges(c, edit, store); imported = V8iImporter.Read(personal);
        check(!imported.Groups.Any(g => g.Id == "personal-group") && imported.Entries.Single(b => b.Id == "corp").Group == "", "Removing personal group retains registrations and moves them to the root in the native list");
        // Removal elsewhere must not destroy private settings, including across restart/re-add.
        string saved = V8iImporter.ReadText(personal); var doc = V8iDocument.Parse(File.ReadAllBytes(personal)); var records = doc.Records; records.Remove("own"); File.WriteAllBytes(personal, doc.Render(records));
        c = Load().Refresh(c); store.Save(c); c = store.Load();
        check(!c.Bases.Any(b => b.Id == "own") && c.OfficialLists.DetachedBases["own"].Notes == "new-private-note", "Official removal disappears from view but keeps detached personal settings");
        File.WriteAllText(personal, saved, Encoding.Unicode); session = Load(); c = session.Refresh(c); store.Save(c);
        check(c.Bases.Single(b => b.Id == "own").Notes == "new-private-note", "Reappearing official ID restores its private settings after restart");
        original = File.ReadAllBytes(personal); jsonBefore = File.ReadAllBytes(store.FilePath);
        edit = c.Copy(); edit.Bases.Single(b => b.Id == "own").Name = "Interrupted";
        rejected = false; try { session.SaveChanges(c, edit, store, () => throw new IOException("injected write failure")); } catch (IOException) { rejected = true; }
        check(rejected && File.ReadAllBytes(personal).SequenceEqual(original) && File.ReadAllBytes(store.FilePath).SequenceEqual(jsonBefore) && FileRecovery.Pending(Path.Combine(store.Folder, "RecoveryOperations")).Count == 0, "Interrupted list/JSON write restores original exact bytes");
        File.Move(shared, shared + ".away"); rejected = false; try { Load(); } catch (FileNotFoundException) { rejected = true; }
        check(rejected && File.ReadAllBytes(store.FilePath).SequenceEqual(jsonBefore), "Missing corporate source is an error, not deletion of its bases");
        string fresh = Path.Combine(root, "fresh", "ibases.v8i"); var freshSession = OfficialListSession.Load(fresh, Array.Empty<string>());
        var freshStore = new CatalogStore(Path.Combine(root, "fresh-data")); var empty = freshSession.Refresh(new()); freshStore.Save(empty);
        var created = empty.Copy(); created.Bases.Add(new() { Name = "First", Location = @"D:\First" }); freshSession.SaveChanges(empty, created, freshStore);
        check(V8iImporter.Read(fresh).Entries.Single().Name == "First", "First registration creates a missing native list atomically");
        var orphan = Base("orphan", "Removed corporate") + "External=1\n"; File.WriteAllText(fresh, orphan);
        check(OfficialListSession.Load(fresh, Array.Empty<string>()).Refresh(new()).Bases.Count == 0, "Orphan External overlay does not resurrect a removed corporate registration");
    }
}
