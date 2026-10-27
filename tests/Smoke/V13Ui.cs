using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using VanBaz;

internal static class V13Ui
{
    public static void Run(string root)
    {
        string folder = Path.Combine(root, "official-ui"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "ibases.v8i");
        File.WriteAllText(path, "[Test]\nID=ui-base\nConnect=File=\"D:\\Synthetic\";\nVersion=8.3\n");
        var session = OfficialListSession.Load(path, Array.Empty<string>());
        var c = session.Refresh(new Catalog()); c.Settings.CloseToTray = c.Settings.HotkeysEnabled = false;
        var store = new CatalogStore(Path.Combine(folder, "data")); store.Save(c);
        var window = new MainWindow(store, c, false);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("useOfficialLists", flags)!.SetValue(window, true);
        typeof(MainWindow).GetField("officialLists", flags)!.SetValue(window, session);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        Catalog Current() => (Catalog)typeof(MainWindow).GetField("catalog", flags)!.GetValue(window)!;
        void Require(bool ok, string label) { if (!ok) throw new Exception(label); }
        try
        {
            Call("SelectBaseInList", "ui-base"); byte[] before = File.ReadAllBytes(path);
            Call("Favorite_Click", window, new RoutedEventArgs());
            Require(Current().Bases.Single().Favorite && before.SequenceEqual(File.ReadAllBytes(path)), "WPF favorite must remain local");
            bool committed = (bool)Call("Commit", (Action)(() => { Current().Groups.Add(new() { Id = "ui-group", Path = "Personal" }); Current().Bases.Single().Group = "Personal"; }))!;
            Require(committed && V8iImporter.Read(path).Entries.Single().Group == "Personal" && V8iImporter.Read(path).Groups.Single().Path == "Personal", "WPF mutation must write native group and placement");
            committed = (bool)Call("Commit", (Action)(() => Current().Bases.Add(new() { Id = "ui-added", Name = "New registration", Location = @"D:\New synthetic" })))!;
            Require(committed && V8iImporter.Read(path).Entries.Count == 2 && store.Load().Bases.Single(b => b.Id == "ui-base").Favorite, "WPF registration persists to both official list and local settings");
            var editor = new BaseEditor(Current().Bases.First().Copy(), new(), new()); editor.UseCommonSource();
            var fields = (Dictionary<string, Control>)typeof(FormDialog).GetField("fields", flags)!.GetValue(editor)!;
            Require(!fields["name"].IsEnabled && !fields["location"].IsEnabled && fields["group"].IsEnabled && fields["favorite"].IsEnabled, "Corporate editor locks source identity, permits local placement and favorites");
            editor.Close();
        }
        finally { window.Close(); }
    }
}
