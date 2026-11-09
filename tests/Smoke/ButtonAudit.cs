using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using VanBaz;

internal static class ButtonAudit
{
    // Load the shipped compiled resources, but suppress product startup and its real list discovery.
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)typeof(FormDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child) { if (child is T typed) yield return typed; foreach (var nested in Children<T>(child)) yield return nested; }
    }
    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Require(bool value, string detail) { if (!value) throw new Exception(detail); }
    private static void Render(Window window, string path)
    {
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); png.Save(stream);
    }
    public static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new App(isolatedStartup: true); app.InitializeComponent(); app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var catalog = new Catalog();
            if (Environment.GetEnvironmentVariable("VANBAZ_SETTINGS_SOURCE") is string source)
            {
                catalog.Settings = CatalogStore.Read(source).Settings;
                catalog.Settings.ProxySecret = "";
                catalog.Settings.ProxyUser = "";
                catalog.Settings.SourceSettings.Clear();
            }
            catalog.Settings.InterfaceMode = "Разработчик";
            catalog.Settings.HotkeysEnabled = false; catalog.Settings.AutoSnapshot = false;
            catalog.Settings.CloseToTray = false; catalog.Settings.StartMinimized = false;
            catalog.Settings.RestorePosition = false; catalog.Settings.MinimizeToTray = false; catalog.Settings.EnableLegacy77 = true;
            catalog.Bases.Add(new() { Id = "fixture", Name = "Учебная база", Location = Path.Combine(folder, "FictionalBase"), Group = "Учебные", Favorite = true });
            catalog.Groups.Add(new() { Path = "Учебные" });
            var store = new CatalogStore(Path.Combine(folder, "Catalog")); store.Save(catalog);
            var main = new MainWindow(store, catalog, true);
            app.MainWindow = main;
            var results = new List<object>(); var errors = new List<Exception>(); string current = "startup";
            void Record(string id, string status, string detail) { results.Add(new { id, status, detail }); Console.WriteLine(status + " " + id + ": " + detail); }
            void Scenario(string id, Action action)
            {
                current = id; int before = errors.Count;
                try { action(); if (errors.Count == before) Record(id, "PASS", "WPF events, compiled application resources, isolated catalog"); }
                catch (Exception ex) { errors.Add(ex); Record(id, "FAIL", ex.ToString()); }
            }
            app.DispatcherUnhandledException += (_, e) => { errors.Add(e.Exception); Record(current, "FAIL", e.Exception.ToString()); e.Handled = true; foreach (var w in app.Windows.Cast<Window>().Where(w => w != main).ToArray()) w.Close(); };
            void Dialog(Action open, Action<Window>? inspect = null)
            {
                bool opened = false;
                var existing = app.Windows.Cast<Window>().Where(w => w.IsVisible).ToHashSet();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (_, _) =>
                {
                    var window = app.Windows.Cast<Window>().LastOrDefault(w => !existing.Contains(w) && w.IsVisible && w.IsActive);
                    if (window == null) window = app.Windows.Cast<Window>().LastOrDefault(w => !existing.Contains(w) && w.IsVisible);
                    if (window == null) return;
                    timer.Stop(); opened = true;
                    try { inspect?.Invoke(window); }
                    catch (Exception ex) { errors.Add(ex); Record(current, "FAIL", ex.ToString()); }
                    finally { window.Close(); }
                };
                timer.Start(); try { open(); } finally { timer.Stop(); }
                Require(opened, "Expected a visible modal window");
            }
            try
            {
                main.Show();
                Call(main, "SelectBaseInList", "fixture");
                Scenario("SettingsButton/modal/navigation", () => Dialog(() => Click((Button)main.FindName("SettingsButton")), w =>
                {
                    var form = (FormDialog)w;
                    var panels = Field<Dictionary<string, StackPanel>>(form, "panels");
                    for (int i = 0; i < panels.Count; i++) { form.SelectSection(i); form.UpdateLayout(); }
                    form.SelectSection(1); Render(form, Path.Combine(folder, "settings-colors.png"));
                    Require(form.Validate!(), "Saved settings should validate");
                    foreach (var action in new[] { "Примеры параметров 1С…", "Внешние команды…", "Каталоги установленных платформ и браузер…", "Штатные источники 1С и личные настройки…", "Дополнительные списки баз по платформам…", "Назначить клавиши отдельным базам…", "Выбрать стиль…", "Проверить обновления ВанБаз…", "Сохранённые версии и восстановление…" })
                    {
                        var button = panels.Values.SelectMany(p => Children<Button>(p)).Single(b => b.Content?.ToString() == action);
                        Scenario("Settings/" + action, () => Dialog(() => Click(button)));
                    }
                    Scenario("Color/dialog/accept/cancel/sliders/invalid", () =>
                    {
                        form.SelectSection(1); var fields = Field<Dictionary<string, Control>>(form, "fields"); var input = (TextBox)fields["favorite"];
                        string initial = input.Text; var choose = ((Grid)input.Parent).Children.OfType<Button>().Single();
                        Dialog(() => Click(choose), picker => { var color = (ColorPickerDialog)picker; color.SetText("hex", "#13579B"); Require(color.Validate!(), "Color valid"); Click(Field<Button>(color, "accept")); });
                        Require(input.Text == "#13579B", "Chosen color not returned");
                        Dialog(() => Click(choose), picker => { var color = (ColorPickerDialog)picker; color.SetText("hex", "#FF0000"); Click(Field<Button>(color, "cancel")); });
                        Require(input.Text == "#13579B", "Cancel changed color");
                        Dialog(() => Click(choose), picker =>
                        {
                            var color = (ColorPickerDialog)picker; color.SetText("hex", "bad-color"); Click(Field<Button>(color, "accept")); Require(color.IsVisible, "Invalid color must not close dialog");
                            foreach (var button in Children<Button>(color).Where(b => b.Tag is string)) Click(button);
                            color.SetText("hex", "#8013579B"); Require(color.SelectedColorText == "#8013579B", "Alpha lost");
                            foreach (var slider in Children<Slider>(color)) slider.Value = 120;
                            Require(color.SelectedColorText == "#78787878", "RGB/alpha sliders not applied");
                            Render(color, Path.Combine(folder, "color-picker.png"));
                        });
                        input.Text = initial;
                    });
                    form.SelectSection(0); Click(Field<Button>(form, "accept"));
                }));
                var handlers = new[] { "Settings_Click", "Mode_Click", "Add_Click", "CreateBase_Click", "Edit_Click", "Delete_Click", "AddGroup_Click", "Duplicate_Click", "MoveToGroup_Click", "AssignVersion_Click", "FormatNames_Click", "RelocatePaths_Click", "FindBases_Click", "OfficialLists_Click", "AlternativeLists_Click", "BaseKeys_Click", "LaunchProfiles_Click", "Platforms_Click", "PlatformRoots_Click", "ServerConsole_Click", "Configurations_Click", "ActualReleases_Click", "CheckUpdates_Click", "OnlineUpdate_Click", "VersionHistory_Click", "SnapshotList_Click", "RestoreSnapshot_Click", "Snapshot_Click", "Recovery_Click", "PortableCopy_Click", "Cache_Click", "ProfileFiles_Click", "Help_Click", "Processes_Click", "InstallUpdate_Click", "SystemMenu_Click", "Migrate_Click" };
                foreach (var handler in handlers)
                {
                    if (handler is "Recovery_Click" or "ProfileFiles_Click" or "Processes_Click") { Scenario(handler + "/isolated-guard", () => Call(main, handler, main, new RoutedEventArgs())); continue; }
                    Scenario(handler, () => Dialog(() => Call(main, handler, main, new RoutedEventArgs())));
                }
                Scenario("SystemMenu/settings-tile", () =>
                {
                    var system = (Window)Call(main, "CreateSystemMenuWindow")!;
                    Dialog(() => system.ShowDialog(), w => Click(Children<Button>(w).Single(b => b.ToolTip?.ToString() == "Настройки")));
                    Require(system.Tag?.ToString() == "settings", "System tile action mismatch");
                });
                Scenario("ServiceMenu/settings", () => Dialog(() => Children<MenuItem>(main).Single(m => m.Header?.ToString() == "Настройки…").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent))));
                Scenario("SystemMenu/settings-routing", () =>
                {
                    int opened = 0; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    timer.Tick += (_, _) =>
                    {
                        var active = app.Windows.Cast<Window>().LastOrDefault(w => w != main && w.IsVisible);
                        if (active?.Title == "Системное меню ВанБаз") { opened++; Click(Children<Button>(active).Single(b => b.ToolTip?.ToString() == "Настройки")); }
                        else if (active is FormDialog && active.Title == "Настройки ВанБаз") { opened++; timer.Stop(); active.Close(); }
                    };
                    timer.Start(); try { Click((Button)main.FindName("SystemMenuButton")); } finally { timer.Stop(); }
                    Require(opened == 2, "System menu must route to visible settings");
                });
                Scenario("Settings/error-boundary", () =>
                {
                    string proxy = catalog.Settings.ProxyUrl, mode = catalog.Settings.ProxyMode;
                    try
                    {
                        catalog.Settings.ProxyMode = "Авто"; catalog.Settings.ProxyUrl = null!;
                        Dialog(() => Click((Button)main.FindName("SettingsButton")), w => Require(w is ErrorDialog && w.Title == "Не удалось открыть настройки", "Expected local error dialog"));
                        Require(main.IsVisible && Directory.GetFiles(Path.Combine(store.Folder, "Logs"), "*-settings-error.txt").Length > 0, "Settings failure must retain main window and diagnostics");
                    }
                    finally { catalog.Settings.ProxyUrl = proxy; catalog.Settings.ProxyMode = mode; }
                });
                foreach (var handler in new[] { "Themes_Click", "ActionsMenu_Click", "ReleasesMenu_Click", "SnapshotsMenu_Click", "CopyMenu_Click", "AddMenu_Click", "StarterMenu_Click", "ColumnsMenu_Click", "LegacyMenu_Click" })
                    Scenario(handler, () => { var sender = (Button)main.FindName("SettingsButton"); Call(main, handler, sender, new RoutedEventArgs()); sender.ContextMenu?.SetCurrentValue(ContextMenu.IsOpenProperty, false); });
                Scenario("Search/filter/tabs/history/favorite/groups", () =>
                {
                    var search = (TextBox)main.FindName("SearchBox"); search.Text = "does-not-exist"; Call(main, "ClearSearch_Click", main, new RoutedEventArgs()); Require(search.Text == "", "Search not cleared");
                    foreach (var name in new[] { "HistoryTab", "LegacyTab", "BasesTab" }) ((RadioButton)main.FindName(name)).IsChecked = true;
                    foreach (var handler in new[] { "Collapse_Click", "Expand_Click", "RefreshHistory_Click" }) Call(main, handler, main, new RoutedEventArgs());
                    Call(main, "SelectBaseInList", "fixture"); bool before = catalog.Bases[0].Favorite;
                    Call(main, "Favorite_Click", main, new RoutedEventArgs()); Require(catalog.Bases[0].Favorite != before, "Favorite not toggled");
                    Call(main, "Favorite_Click", main, new RoutedEventArgs());
                });
                Scenario("Registered-buttons/hover-hints/launch-arrows", () =>
                {
                    var controls = (List<(FrameworkElement Control, string Key, string Hint)>)typeof(MainWindow).GetField("actionControls", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
                    var hint = (TextBlock)main.FindName("HoverHint");
                    foreach (var (control, _, _) in controls)
                    {
                        Require(!string.IsNullOrWhiteSpace(control.ToolTip?.ToString()), "Missing action tooltip");
                        control.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
                        Require(hint.Text == control.ToolTip?.ToString(), "Hover hint not set");
                        control.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = System.Windows.Input.Mouse.MouseLeaveEvent }); Require(hint.Text == "", "Hover hint not cleared");
                    }
                    foreach (var name in new[] { "EnterpriseMenuButton", "DesignerMenuButton", "StarterMenuButton" })
                    {
                        var button = (Button)main.FindName(name); Click(button); Require(button.ContextMenu?.IsOpen == true, name + " must open launch choices"); button.ContextMenu!.IsOpen = false;
                    }
                    Click((Button)main.FindName("EnterpriseButton")); Click((Button)main.FindName("DesignerButton")); // Product isolation guard suppresses real 1C processes.
                });
                Scenario("Dark/color-picker", () =>
                {
                    catalog.Settings.Theme = "Тёмный"; Call(main, "ApplyPreferences", false);
                    var picker = new ColorPickerDialog("#287EB0", "Цвет группы") { Owner = main };
                    Dialog(() => picker.ShowDialog(), w => Render(w, Path.Combine(folder, "color-picker-dark.png")));
                });
                Scenario("Light/color-picker", () =>
                {
                    catalog.Settings.Theme = "Светло-серый"; Call(main, "ApplyPreferences", false);
                    var picker = new ColorPickerDialog("#287EB0", "Цвет группы") { Owner = main };
                    Dialog(() => picker.ShowDialog(), w => Render(w, Path.Combine(folder, "color-picker-light.png")));
                });
                Scenario("Exit-button", () => { var exit = Children<Button>(main).Single(b => b.ToolTip?.ToString() == "Завершить ВанБаз"); Click(exit); Require(!main.IsVisible, "Exit button did not close the main window"); });
                Require(app.Windows.OfType<MainWindow>().Count() == 0, "Isolated audit must leave no additional main window");
                File.WriteAllText(Path.Combine(folder, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                if (errors.Count > 0) throw new AggregateException(errors);
            }
            catch (Exception ex) { failure = ex; Console.WriteLine("FAIL actual settings: " + ex); }
            finally { foreach (var w in app.Windows.Cast<Window>().Where(w => w != main).ToArray()) w.Close(); main.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(45000)) throw new Exception("Button audit timeout");
        if (failure != null) throw new Exception("Button audit failed", failure);
    }
}
