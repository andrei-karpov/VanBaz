using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VanBaz;

internal static class AcceptanceUi
{
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found) yield return found;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root))
            if (item is DependencyObject child)
            {
                if (child is T found) yield return found;
                foreach (var nested in Logical<T>(child)) yield return nested;
            }
    }
    private static Catalog Fixture(string root)
    {
        string files = Path.Combine(root, "FictionalBases"); Directory.CreateDirectory(files);
        var catalog = new Catalog
        {
            Bases = new()
            {
                new() { Id = "accept-file", Name = "Учебная бухгалтерия", Kind = "File", Location = Path.Combine(files, "Accounting"), Group = "Учебные базы", Favorite = true, Description = "Вымышленная база для приёмки интерфейса" },
                new() { Id = "accept-server", Name = "Учебная торговля", Kind = "Server", Location = "training-server/trade", Group = "Учебные базы" },
                new() { Id = "accept-web", Name = "Учебная веб-публикация", Kind = "Web", Location = "https://example.invalid/training", Group = "Разработка" },
                new() { Id = "accept-77", Name = "Учебная база 7.7", Version = "7.7", Location = Path.Combine(files, "Legacy"), Favorite = true }
            },
            Groups = new() { new() { Path = "Учебные базы" }, new() { Path = "Разработка" } },
            History = new() { new() { BaseId = "accept-file", Name = "Учебная бухгалтерия", Mode = "Предприятие", Time = new DateTime(2026, 10, 28, 10, 0, 0) } }
        };
        catalog.Settings.InterfaceMode = "Разработчик"; catalog.Settings.EnableLegacy77 = true;
        catalog.Settings.AutoSnapshot = false; catalog.Settings.HotkeysEnabled = false;
        catalog.Settings.RestorePosition = false; catalog.Settings.CloseToTray = false;
        catalog.Settings.MinimizeToTray = false; catalog.Settings.EscapeToTray = false;
        catalog.Settings.AfterLaunchAction = "Ничего не делать"; catalog.Settings.Theme = "Светло-серый";
        return catalog;
    }
    private static void Layout(MainWindow window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(980, 700)); content.Arrange(new Rect(0, 0, 980, 700)); content.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
    }
    private static void Render(MainWindow window, string path)
    {
        Layout(window); var content = (FrameworkElement)window.Content;
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 980, 700));
            drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, 980, 700));
        }
        var image = new RenderTargetBitmap(1960, 1400, 192, 192, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    public static void Run(string folder, bool live)
    {
        Directory.CreateDirectory(folder); Exception? failure = null;
        var thread = new Thread(() =>
        {
            Application? app = null; MainWindow? main = null;
            var observations = new List<object>(); int failed = 0;
            void Check(string id, bool ok, string detail)
            {
                observations.Add(new { id, status = ok ? "PASS" : "FAIL", method = "real WPF controls, isolated fixture", detail });
                Console.WriteLine((ok ? "PASS " : "FAIL ") + id + ": " + detail); if (!ok) failed++;
            }
            try
            {
                app = UiRegression.CreateApplication(); var catalog = Fixture(folder);
                var store = new CatalogStore(Path.Combine(folder, "Catalog")); store.Save(catalog);
                main = new MainWindow(store, catalog, true) { Width = 1000, Height = 760 };
                if (live)
                {
                    main.Loaded += (_, _) => Call(main, "SelectBaseInList", "accept-file");
                    main.Closed += (_, _) => app.Shutdown();
                    Console.WriteLine("Isolated live fixture: official lists disabled; external launches suppressed; fictional data only.");
                    app.Run(main); return;
                }
                T Control<T>(string name) where T : FrameworkElement => (T)main.FindName(name);
                var grid = Control<DataGrid>("BasesGrid");
                void Select(string id) { Call(main, "SelectBaseInList", id); Layout(main); }
                Select("accept-file");
                Check("P01", Control<Button>("EnterpriseButton").IsEnabled && Control<Button>("DesignerButton").IsEnabled, "Файловая база: оба запуска доступны.");
                var launch = (ContextMenu)Call(main, "CreateLaunchMenu", false)!;
                Check("P02", launch.Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == "Параметры на один запуск…") && catalog.History.Count == 1, "Построение меню стрелки запуска не запускает базу и не добавляет историю.");
                Select("accept-web");
                Check("P03", Control<Button>("EnterpriseButton").IsEnabled && !Control<Button>("DesignerButton").IsEnabled && !Control<GroupBox>("SessionGroup").IsEnabled, "Веб-база: Предприятие доступно, Конфигуратор и клиент сессии отключены.");
                Select("accept-file");
                var operation = Field(main, "operations")!; operation.GetType().GetMethod("Add")!.Invoke(operation, new object[] { "acceptance-busy" });
                Check("P04", !Control<Button>("EnterpriseButton").IsEnabled && !Control<Button>("DesignerButton").IsEnabled && Control<Button>("EnterpriseButton").ToolTip?.ToString()?.Contains("Дождитесь") == true, "Занятость отключает запуск и объясняет причину подсказкой.");
                operation.GetType().GetMethod("Remove")!.Invoke(operation, new object[] { "acceptance-busy" });
                Check("P05", Control<Button>("EnterpriseButton").IsEnabled, "Кнопки восстанавливаются после окончания операции.");
                catalog.Settings.InterfaceMode = "Пользователь"; Call(main, "ApplyPreferences", false); Layout(main);
                Check("P06", !Control<Button>("DesignerButton").IsEnabled && Control<Button>("EnterpriseButton").IsEnabled, "Режим Пользователь сохраняет запуск Предприятия и отключает Конфигуратор.");
                var editButtons = Visuals<Button>(grid).Where(button => button.Tag?.ToString() == "edit" && button.DataContext is ListRow { Entry.Id: "accept-file" }).ToList();
                double EffectiveOpacity(Button button) => button.Opacity * ((FrameworkElement?)button.Template.FindName("Frame", button))?.Opacity ?? button.Opacity;
                Check("B02", editButtons.Count > 0 && editButtons.All(button => !button.IsEnabled && EffectiveOpacity(button) <= .5), "Кнопка свойств строки в режиме Пользователь отключена; opacity × Frame opacity: " + string.Join(", ", editButtons.Select(button => EffectiveOpacity(button).ToString("0.##"))) + ".");
                Render(main, Path.Combine(folder, "user-mode-light.png"));
                var entries = catalog.Bases.ToList(); catalog.Bases.Clear(); Call(main, "BuildRows", new object?[] { null }); Layout(main);
                var emptyActions = Control<StackPanel>("EmptyPanel").Children.OfType<Button>().ToArray();
                Check("B01-user", emptyActions.Length == 3 && emptyActions.All(button => !button.IsEnabled), "Все три действия пустого каталога отключены в режиме Пользователь.");
                catalog.Settings.InterfaceMode = "Разработчик"; Call(main, "ApplyPreferences", false); operation.GetType().GetMethod("Add")!.Invoke(operation, new object[] { "acceptance-busy" }); Layout(main);
                Check("B01-busy", emptyActions.All(button => !button.IsEnabled), "Все три действия пустого каталога отключены во время операции.");
                operation.GetType().GetMethod("Remove")!.Invoke(operation, new object[] { "acceptance-busy" });
                catalog.Bases.AddRange(entries); Call(main, "BuildRows", new object?[] { null }); Select("accept-file");
                var search = Control<TextBox>("SearchBox"); search.Text = "веб-публикация"; Layout(main);
                Check("P07", grid.Items.Cast<ListRow>().Where(row => row.Entry != null).All(row => row.Entry!.Id == "accept-web") && Control<Button>("ClearSearchButton").Visibility == Visibility.Visible, "Поиск отбирает запись и показывает очистку.");
                Control<Button>("ClearSearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check("P08", search.Text == "" && grid.Items.Cast<ListRow>().Any(row => row.Entry?.Id == "accept-file"), "Кнопка очистки возвращает список.");
                Control<RadioButton>("HistoryTab").IsChecked = true; Layout(main);
                Check("A01", Visuals<Button>(Control<StackPanel>("HistoryToolbar")).Any(button => button.ToolTip?.ToString()?.Contains("F5") == true), "Во вкладке истории доступна отдельная кнопка обновления.");
                Control<RadioButton>("BasesTab").IsChecked = true; Select("accept-file");
                AddedControls(main, catalog, folder, Check);
                catalog.Settings.Theme = "Тёмный"; catalog.Settings.InterfaceMode = "Пользователь";
                Call(main, "ApplyPreferences", false); Select("accept-file"); ThemeService.Apply(catalog.Settings); Render(main, Path.Combine(folder, "main-dark.png"));
                var darkEdit = Visuals<Button>(grid).Where(button => button.Tag?.ToString() == "edit" && button.DataContext is ListRow { Entry.Id: "accept-file" }).ToList();
                Check("B02-dark", darkEdit.Count > 0 && darkEdit.All(button => !button.IsEnabled && EffectiveOpacity(button) <= .5), "В тёмной теме disabled-состояние строки также приглушено; невидимые действия не появляются.");
                Console.WriteLine("Acceptance checks: " + observations.Count + "; failed: " + failed + "; real 1C launches: 0.");
                if (failed > 0) throw new Exception("Acceptance failed: " + failed);
            }
            catch (Exception ex) { failure = ex; Console.WriteLine(ex); }
            finally
            {
                File.WriteAllText(Path.Combine(folder, "wpf-results.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
                main?.Close(); app?.Shutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new InvalidOperationException("Acceptance UI", failure);
    }

    private static void AddedControls(MainWindow main, Catalog catalog, string folder, Action<string, bool, string> check)
    {
        new System.Windows.Interop.WindowInteropHelper(main).EnsureHandle();
        var system = (Window)Call(main, "CreateSystemMenuWindow")!;
        var tiles = Logical<Grid>(system).First(grid => grid.ColumnDefinitions.Count == 3 && grid.RowDefinitions.Count == 2);
        check("A02-panel", tiles.Children.OfType<Button>().Count() == 6 && Logical<Button>(system).Any(b => b.Content?.ToString() == "Создать переносимую копию…"), "Системная панель содержит шесть плиток 3×2 и отдельную кнопку переносимой копии.");
        system.Close();
        var restart = RestartPlan.Create(Path.Combine(folder, "VanBaz.exe"), Path.Combine(folder, "Data with spaces"), "Разработчик", true);
        check("A02-restart-plan", restart.ArgumentList.SequenceEqual(new[] { "--data-dir", Path.Combine(folder, "Data with spaces"), "--mode", "Разработчик", "--lock-mode" }) && !restart.UseShellExecute, "План перезапуска сохраняет отдельные аргументы каталога, режима и его фиксации. Процесс не запускается.");
        var moved = WindowGeometry.MoveTo(new(new("old", 0, 0, 1920, 1080), new(1500, 700, 900, 600)), new("target", -1280, 0, 1280, 720, 144));
        check("A02-monitor", moved.Screen.Device == "target" && moved.Bounds.Left >= -1280 && moved.Bounds.Top >= 0 && moved.Bounds.Left + moved.Bounds.Width <= 0 && moved.Bounds.Top + moved.Bounds.Height <= 720, "Перенос на меньший монитор с отрицательной координатой и другим DPI помещает всё окно в рабочую область; физический второй монитор не требуется.");
        string mmcRoot = Path.Combine(folder, "mmc-fixture"), bin = Path.Combine(mmcRoot, "1cv8", "8.3.99", "bin"), win = Path.Combine(mmcRoot, "Windows");
        Directory.CreateDirectory(bin); File.WriteAllText(Path.Combine(bin, "radmin.dll"), "fixture only");
        foreach (string systemFolder in new[] { "System32", "SysWOW64" }) { Directory.CreateDirectory(Path.Combine(win, systemFolder)); File.WriteAllText(Path.Combine(win, systemFolder, "mmc.exe"), "fixture only"); }
        foreach (string msc in new[] { "1CV8 Servers (x86-64).msc", "1CV8 Servers.msc" }) File.WriteAllText(Path.Combine(bin, msc), "<MMC><Snapin CLSID=\"{11111111-1111-1111-1111-111111111111}\" /></MMC>");
        var x64 = ServerConsole.CreatePlan(new("8.3.99", bin, "x64"), win, true, true, (_, _) => Path.Combine(bin, "radmin.dll"));
        var x86 = ServerConsole.CreatePlan(new("8.3.99", bin, "x86"), win, true, true, (_, _) => Path.Combine(bin, "radmin.dll"));
        bool Rejects(Func<string, string, string?> registered) { try { ServerConsole.CreatePlan(new("8.3.99", bin, "x64"), win, true, true, registered); return false; } catch (InvalidOperationException) { return true; } }
        check("A04-plan", x64.ArgumentList.Last() == "/64" && x86.ArgumentList.Last() == "/32" && x86.FileName.Contains("SysWOW64") && Rejects((_, _) => null) && Rejects((_, _) => Path.Combine(folder, "different.dll")), "MMC-план выбирает x86/x64 и отклоняет отсутствующую/чужую регистрацию. Файлы синтетические; реестр и MMC не изменяются и не запускаются.");

        using var tray = new System.Windows.Forms.ContextMenuStrip();
        Call(main, "PopulateTrayMenu", tray);
        var trayItems = tray.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().ToList();
        var favorite = trayItems.Single(item => item.Text == "Избранные базы");
        bool hasCommands = new[] { "Временная блокировка…", "Снимок экрана…", "Каталог настроек", "Настройки…" }.All(label => trayItems.Any(item => item.Text == label && item.Enabled));
        check("A03", hasCommands && favorite.DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().First().DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Count() == 2, "Трей содержит новые действия и два вида запуска избранной файловой базы; обработчики внешних действий не исполнялись.");
        catalog.Settings.InterfaceMode = "Пользователь"; Call(main, "ApplyPreferences", false); Call(main, "PopulateTrayMenu", tray);
        check("A03-user", tray.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(item => item.Text == "Настройки…").Enabled == false && tray.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(item => item.Text == "Избранные базы").DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().First().DropDownItems.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(item => item.Text == "Конфигуратор").Enabled == false, "В трее режим Пользователь отключает настройки и Конфигуратор.");
        catalog.Settings.InterfaceMode = "Разработчик"; Call(main, "ApplyPreferences", false);

        int metadataCalls = 0; BaseEntry? metadataInput = null;
        var editor = new BaseEditor(catalog.Bases.First().Copy(), new(), new(), metadataReader: (entry, _) => { metadataCalls++; metadataInput = entry; return Task.FromResult<BridgeResult?>(new() { Ok = true, Name = "Учебная конфигурация", Version = "1.2.3" }); });
        editor.SetText("name", "Новое несохранённое имя");
        var detect = Logical<Button>(editor).Single(button => button.Content?.ToString() == "Определить версию…");
        detect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("A05", metadataCalls == 1 && metadataInput?.Name == "Новое несохранённое имя" && editor.Value("configname") == "Учебная конфигурация" && editor.Value("configversion") == "1.2.3" && catalog.Bases.First().Name == "Учебная бухгалтерия", "Кнопка определения версии передала несохранённые поля stub и обновила редактор, не исходную запись; COM не вызывается.");
        editor.Close();

        DateTime day = new(2026, 10, 28);
        check("A09", BaseSearch.Matches(11, day.AddHours(23), BaseSizeFilter.MoreThan, 10, null, day) && !BaseSearch.Matches(10, day, BaseSizeFilter.MoreThan, 10, null, null) && BaseSearch.Matches(9, day, BaseSizeFilter.LessThan, 10, null, null) && BaseSearch.Matches(10, day, BaseSizeFilter.Equal, 10, null, null) && !BaseSearch.Matches(10, day.AddDays(1), BaseSizeFilter.Equal, 10, null, day), "Размеры >/< /= и верхняя дата включают выбранный день и исключают следующий.");

        catalog.Configurations = new() { new() { Id = "c1", Name = "Учебная бухгалтерия", Country = "Россия", Watch = true }, new() { Id = "c2", Name = "Учебная торговля", Country = "Казахстан", Watch = true } };
        var configs = (Window)Call(main, "CreateConfigurationsWindow", false)!;
        var text = Logical<TextBox>(configs).Single(control => control.Name == "ConfigurationFilter");
        var countries = Logical<ComboBox>(configs).Single(control => control.Name == "CountryFilter");
        var configGrid = Logical<DataGrid>(configs).Single();
        var update = Logical<Button>(configs).Single(button => button.Content?.ToString() == "Проверить обновления");
        text.Text = "несуществующая конфигурация";
        check("B03", configGrid.Items.Count == 0 && !update.IsEnabled, "При пустом фильтре и отсутствии предыдущего выделения кнопка проверки обновлений отключена.");
        text.Clear();
        configGrid.SelectedItem = catalog.Configurations[1]; text.Text = "торговля";
        bool selectionKept = configGrid.Items.Count == 1 && ReferenceEquals(configGrid.SelectedItem, catalog.Configurations[1]);
        countries.SelectedItem = "Россия";
        bool excluded = configGrid.Items.Count == 0;
        Logical<Button>(configs).Single(button => button.Content?.ToString() == "Очистить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check("A10", selectionKept && excluded && configGrid.Items.Count == 2 && text.Text == "" && countries.SelectedItem?.ToString() == "Все страны", "Текст и страна совместно фильтруют конфигурации, отбор сохраняет видимое выделение, очистка возвращает строки.");
        configs.Close();

        var settings = (FormDialog)Call(main, "CreateSettingsDialog")!;
        var tree = Logical<TreeView>(settings).Single(control => control.Name == "SettingsNavigation");
        var sections = (Dictionary<string, StackPanel>)typeof(FormDialog).GetField("panels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var pages = Logical<TreeViewItem>(tree).ToArray();
        var navigation = (ListBox)typeof(FormDialog).GetField("navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        var scroll = (ScrollViewer)typeof(FormDialog).GetField("scroll", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(settings)!;
        bool allPages = pages.Length == sections.Count;
        foreach (var page in pages)
        {
            page.IsSelected = true;
            allPages &= page.Tag is string name && ReferenceEquals(scroll.Content, sections[name]);
        }
        check("V17-tree", allPages && navigation.Visibility == Visibility.Collapsed && tree.Visibility == Visibility.Visible, "Все девять страниц доступны выбором настоящих TreeViewItem; скрытый ListBox не используется тестом как вход.");
        string oldVersion = catalog.Settings.DefaultVersion;
        settings.SetText("version", "8.5"); settings.Close();
        check("V17-cancel", catalog.Settings.DefaultVersion == oldVersion, "Закрытие настроек без Apply сохраняет прежние данные.");
        settings = (FormDialog)Call(main, "CreateSettingsDialog")!;
        settings.SetText("version", "8.4");
        bool applied = settings.Validate!() && settings.ApplyChanges!();
        check("V17-apply", applied && catalog.Settings.DefaultVersion == "8.4", "Apply сохраняет настройки через реальный Commit синтетического каталога.");
        settings.Close();
    }
}
