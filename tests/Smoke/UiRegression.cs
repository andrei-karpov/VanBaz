using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using VanBaz;

internal static class UiRegression
{
    // Use the actual style source without constructing the product's App/startup/migrations.
    internal static Application CreateApplication()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var source = typeof(UiRegression).Assembly.GetManifestResourceStream("VanBazAppResourceSource.xaml")!;
        var xml = System.Xml.Linq.XDocument.Load(source);
        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new System.Xml.Linq.XElement(wpf + "ResourceDictionary",
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "local", "clr-namespace:VanBaz;assembly=VanBaz"),
            xml.Root!.Element(wpf + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        return app;
    }
    // Exercises the real window's selection/action wiring without showing a window or injecting OS input.
    public static void Run(string folder, bool settingsOnly = false)
    {
        Exception? failure = null;
        string stage = "start";
        var thread = new Thread(() =>
        {
            Application? app = null; MainWindow? window = null;
            try
            {
                stage = "resources"; app = CreateApplication(); if (settingsOnly) { V19Ui.Run(folder); return; }
                var catalog = new Catalog
                {
                    Bases = new() { new() { Id = "file", Name = "File", Location = @"D:\Demo\File", Group = "Clients" }, new() { Id = "web", Name = "Web", Kind = "Web", Location = "https://example.invalid/demo" }, new() { Id = "legacy", Name = "Legacy", Version = "7.7", Location = @"D:\Demo\77" } },
                    Groups = new() { new() { Path = "Clients" } }, History = new() { new() { BaseId = "web", Name = "Web", Time = DateTime.Now } }
                };
                catalog.Settings.EnableLegacy77 = true; catalog.Settings.InterfaceMode = "Разработчик"; catalog.Settings.HotkeysEnabled = false;
                var store = new CatalogStore(folder); store.Save(catalog);
                stage = "construct window"; window = new MainWindow(store, catalog, true);
                T Control<T>(string name) => (T)window.FindName(name);
                object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
                void Require(bool condition, string why) { if (!condition) throw new Exception(why); }
                var bases = Control<DataGrid>("BasesGrid");
                bases.SelectedItem = bases.Items.Cast<ListRow>().First(r => r.Entry?.Id == "file");
                stage = "history"; Control<RadioButton>("HistoryTab").IsChecked = true;
                Control<DataGrid>("HistoryGrid").SelectedItem = catalog.History[0];
                Require(Control<Button>("EnterpriseButton").IsEnabled && !Control<Button>("DesignerButton").IsEnabled, "History must use its selected Web base, not the hidden File base");
                stage = "history delete guard"; Call("Delete_Click", window, new RoutedEventArgs());
                Require(catalog.Bases.Count == 3 && store.Load().Bases.Count == 3, "Delete handler must not delete a hidden base from history");
                stage = "examples relocation"; Require(window.FindName("CommandsTab") == null, "Parameter examples must no longer occupy a main-window tab");
                stage = "legacy"; Control<ComboBox>("VersionFilter").SelectedItem = "8.3"; Control<RadioButton>("LegacyTab").IsChecked = true;
                Require(bases.Items.Cast<ListRow>().Count(r => r.Entry != null) == 1, "Hidden 8.x filter must not hide 7.7 entries");
                Require(bases.Items.Cast<ListRow>().Where(r => r.Entry != null).All(r => r.Entry!.Version == "7.7"), "7.7 tab must isolate its entries");
                bases.SelectedItem = bases.Items.Cast<ListRow>().Single(r => r.Entry?.Id == "legacy");
                Require(Control<Button>("LegacyToolsButton").Visibility == Visibility.Visible && ((ContextMenu)Call("BaseActions")!).Items.OfType<MenuItem>().Any(i => i.Header?.ToString()?.Contains("CDX") == true && i.IsEnabled), "Legacy toolbar and CDX command must follow current selection");
                var emptyList = new ListBox(); var dependent = new Button(); DialogBehavior.Selection(emptyList, dependent);
                Require(!dependent.IsEnabled, "Empty list must disable dependent button"); emptyList.Items.Add("row"); emptyList.SelectedIndex = 0;
                Require(dependent.IsEnabled, "Selected row must enable dependent button");
                stage = "group"; Control<RadioButton>("BasesTab").IsChecked = true;
                bases.SelectedItem = bases.Items.Cast<ListRow>().First(r => r.GroupKey == "group:Clients");
                var menu = (ContextMenu)Call("BaseActions")!;
                Require(!menu.Items.OfType<MenuItem>().Any(i => i.Header?.ToString() == "1С:Предприятие"), "Group menu must not contain base launch");
                bases.SelectedItem = bases.Items.Cast<ListRow>().First(r => r.Entry?.Id == "file");
                stage = "busy"; var operations = typeof(MainWindow).GetField("operations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                operations.GetType().GetMethod("Add")!.Invoke(operations, new object[] { "test-operation" });
                Require(!Control<Button>("EnterpriseButton").IsEnabled && !Control<Button>("DesignerButton").IsEnabled, "Busy state must immediately disable launch and designer");
                operations.GetType().GetMethod("Remove")!.Invoke(operations, new object[] { "test-operation" });
                Require(Control<Button>("EnterpriseButton").IsEnabled, "Action must recover after completion");
                stage = "columns"; catalog.Settings.ColumnWidths["Version"] = 123; Call("ApplyColumnWidths");
                Require(((DataGridColumn)window.FindName("PlatformColumn")).Width.Value == 123, "Saved platform width must apply");
                catalog.Settings.SortColumn = "Name"; catalog.Settings.SortDescending = true; Call("BuildRows", new object?[] { null });
                Require(bases.Items.Cast<ListRow>().Any(r => r.GroupKey == "group:Clients"), "Sorting must retain group rows");
                stage = "launch option availability";
                var optionsBase = catalog.Bases.First(); var optionsProfile = LaunchProfile.From(optionsBase, false, "Толстый управляемый"); optionsProfile.Advanced.Taxi = optionsProfile.Advanced.DisableScheduledJobs = true;
                var options = new LaunchOptionsDialog(optionsBase, optionsProfile, new[] { new PlatformInfo("8.2", folder, "x86"), new PlatformInfo("8.3", folder, "x64") }, true);
                var optionFields = (Dictionary<string, Control>)typeof(FormDialog).GetField("fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(options)!;
                ((ComboBox)optionFields["version"]).SelectedItem = "8.2";
                Require(!optionFields["taxi"].IsEnabled && !optionFields["jobs"].IsEnabled && !options.Read().Advanced.Taxi && !options.Read().Advanced.DisableScheduledJobs, "8.2 choice must disable and clear incompatible checkboxes");
                ((ComboBox)optionFields["version"]).SelectedItem = "8.3";
                Require(optionFields["taxi"].IsEnabled && optionFields["jobs"].IsEnabled, "Switching back to 8.3 must enable compatible file-base options");
                ((ComboBox)optionFields["client"]).SelectedItem = "Обычный режим";
                Require(!optionFields["taxi"].IsEnabled && optionFields["jobs"].IsEnabled, "Ordinary client must disable Taxi without disabling file job control"); options.Close();
                stage = "stored arguments validation";
                var editor = new BaseEditor(optionsBase.Copy(), new(), new());
                var editorFields = (Dictionary<string, Control>)typeof(FormDialog).GetField("fields", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
                ((TextBox)editorFields["args"]).Text = "/Pfixture-secret";
                Require(editor.Validate!() == false, "Base properties must refuse saving an attached plaintext password"); editor.Close();
                V14Ui.Run(window, catalog);
                V15Ui.Run(window, catalog, folder);
                V16Ui.Run(window, catalog);
                V17Ui.Run(window, catalog);
                V09Ui.Render(folder);
                V10Ui.Render(folder, window, catalog);
                Require(app.Windows.OfType<MainWindow>().Count() == 1 && !window.IsVisible, "UI harness must not invoke application startup or open another main window");
                stage = "tray launch buttons"; V17Ui.LaunchButtons(folder);
                stage = "v18 display and examples"; V18Ui.Run(window,folder);
                stage = "v19 nested settings"; V19Ui.Run(folder);
                stage = "bound key process and recovery close"; V12Ui.Run(folder);
                stage = "official lists and local settings"; V13Ui.Run(folder);
                typeof(MainWindow).GetField("modeOverride", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, "Пользователь"); Call("ApplyPreferences", false);
                Require(!Control<Button>("DesignerButton").IsEnabled && catalog.Settings.InterfaceMode == "Разработчик", "Runtime mode override must not overwrite saved mode");
            }
            catch (Exception ex) { failure = ex; Console.WriteLine("UI regression failure at " + stage + ": " + ex); }
            finally
            {
                stage = "close"; if (window != null && !double.IsFinite(window.Left)) Console.WriteLine("UI smoke: unshown window has no finite screen coordinates; closing must preserve saved geometry.");
                window?.Close(); stage = "shutdown"; app?.Shutdown();
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                // Application.Shutdown queues its cleanup; finish it before the owning STA thread exits.
                dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                dispatcher.InvokeShutdown(); stage = "done";
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(60000)) throw new Exception("UI smoke timeout at " + stage, failure);
        if (failure != null) throw new Exception("UI action regression", failure);
    }
}
