using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VanBaz;

public partial class MainWindow
{
    private bool IsBaseView => BasesTab.IsChecked == true || LegacyTab.IsChecked == true;
    private bool CanEdit => !locked && operations.Count == 0 && InterfaceMode != "Пользователь";
    private bool CanUseBase => !locked && operations.Count == 0 && Selected != null && (catalog.Settings.EnableLegacy77 || Selected.Version != "7.7");
    private bool IsV8 => Selected is { Version: not "7.7", Kind: not "Web" };
    private string lastSelectedBaseId = "";
    private string sessionBaseId = "";
    private bool dragging;
    private bool actionMenuOpen;
    public static readonly DependencyProperty EditingAvailableProperty = DependencyProperty.Register(nameof(EditingAvailable), typeof(bool), typeof(MainWindow));
    public bool EditingAvailable { get => (bool)GetValue(EditingAvailableProperty); set => SetValue(EditingAvailableProperty, value); }
    public static readonly DependencyProperty InteractionAvailableProperty = DependencyProperty.Register(nameof(InteractionAvailable), typeof(bool), typeof(MainWindow));
    public bool InteractionAvailable { get => (bool)GetValue(InteractionAvailableProperty); set => SetValue(InteractionAvailableProperty, value); }
    public static readonly DependencyProperty RowLaunchVisibleProperty = DependencyProperty.Register(nameof(RowLaunchVisible), typeof(bool), typeof(MainWindow));
    public bool RowLaunchVisible { get => (bool)GetValue(RowLaunchVisibleProperty); set => SetValue(RowLaunchVisibleProperty, value); }
    private DateTime lastLaunchClick = DateTime.MinValue;
    private string lastLaunchKey = "";
    private readonly List<(FrameworkElement Control, string Key, string Hint)> actionControls = new();

    private void InitializeInteraction()
    {
        BasesGrid.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers == ModifierKeys.Control) { ZoomList(Math.Sign(e.Delta)); e.Handled = true; } };
        operations.Changed += UpdateActionStates;
        CopyButton.Content = UiIcons.Icon("copy");
        StarterButton.Content = UiIcons.Label("▶", "1С:Стартер");
        SettingsButton.Content = UiIcons.Icon("settings");
        SystemMenuButton.Content = UiIcons.Icon("system"); FeedbackButton.Content = UiIcons.Icon("feedback");
        BasesTab.Content = UiIcons.Label("file", "1С:Предприятие 8");
        LegacyTab.Content = UiIcons.Label("file", "7.7");
        HistoryTab.Content = UiIcons.Label("history", "История");
        RefreshHistoryButton.Content = UiIcons.Icon("↻");
        EnterpriseButton.Content = UiIcons.Label("▶", "1С:Предприятие");
        DesignerButton.Content = UiIcons.Label("▧", "Конфигуратор");
        Register(EnterpriseButton, "launch", "Запустить 1С:Предприятие · F3");
        Register(EnterpriseMenuButton, "launch", "Варианты запуска Предприятия: параметры, профили, запуск от администратора");
        Register(DesignerButton, "designer", "Запустить Конфигуратор · F4");
        Register(DesignerMenuButton, "designer", "Варианты запуска Конфигуратора");
        Register(CopyButton, "selected", "Копировать сводную информацию. Правый щелчок — отдельные поля");
        Register(FindHistoryButton, "history", "Найти запись истории в списке баз · Enter");
        Register(RefreshHistoryButton, "idle", "Обновить список истории · F5");
        Register(SettingsButton, "edit", "Настройки списка и программы · F6");
        Register(SystemMenuButton, "idle", "Системное меню · Alt+F11");
        Register(FeedbackButton, "idle", "Сообщить об ошибке или предложить улучшение на GitHub");
        Tool(BaseToolbar, "+", "Добавить базу · Insert", "edit", Add_Click);
        Tool(BaseToolbar, "▾", "Выбрать тип новой базы", "edit", AddMenu_Click, 17);
        Tool(BaseToolbar, "folder-add", "Добавить группу · Ctrl+Insert", "edit", AddGroup_Click);
        Tool(BaseToolbar, "×", "Удалить выбранную запись · Delete", "delete", Delete_Click);
        Tool(BaseToolbar, "edit", "Свойства базы или группы · Alt+Enter", "properties", Edit_Click);
        Tool(BaseToolbar, "▾", "Действия с выбранной базой или группой", "row", ActionsMenu_Click, 17);
        Tool(BaseToolbar, "⟳", "Проверить опубликованный релиз · F9", "v8", CheckUpdates_Click);
        Tool(BaseToolbar, "▾", "Релизы и список конфигураций · Alt+F9", "edit", ReleasesMenu_Click, 17);
        Tool(BaseToolbar, "⌫", "Кэш 1С: просмотр, карантин и восстановление", "edit", Cache_Click);
        Tool(BaseToolbar, "★", "Добавить или убрать избранное · F8", "favorite", Favorite_Click);
        Tool(TreeToolbar, "▤", "Сохранённые снимки списков · Ctrl+L", "idle", SnapshotList_Click);
        Tool(TreeToolbar, "↻", "Обновить сведения списка · F5", "refresh", Refresh_Click);
        Tool(TreeToolbar, "⊞", "Развернуть все группы · F12", "tree", Expand_Click);
        Tool(TreeToolbar, "⊟", "Свернуть все группы · F11", "tree", Collapse_Click);
        BasesGrid.ContextMenuOpening += (_, e) => { if (!IsBaseView) { e.Handled = true; return; } BasesGrid.ContextMenu = HeaderAt(e.OriginalSource) != null || HeaderAt(Mouse.DirectlyOver ?? BasesGrid) != null ? ColumnsMenu() : BaseActions(); };
        HistoryGrid.ContextMenuOpening += (_, _) => HistoryGrid.ContextMenu = HistoryActions();
        BindServiceMenu(ServiceMenu);
        ServiceMenu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler((_, e) => { if (e.OriginalSource is ItemsControl menu) AddMenuIcons(menu); }));
        UpdateActionStates();
    }
    private void Register(FrameworkElement control, string key, string hint)
    {
        actionControls.Add((control, key, hint));
        System.Windows.Automation.AutomationProperties.SetName(control, hint);
        ToolTipService.SetShowOnDisabled(control, true);
        control.ToolTip = hint;
        control.MouseEnter += (_, _) => HoverHint.Text = control.ToolTip?.ToString() ?? hint;
        control.MouseLeave += (_, _) => HoverHint.Text = "";
        control.GotKeyboardFocus += (_, _) => HoverHint.Text = control.ToolTip?.ToString() ?? hint;
        control.LostKeyboardFocus += (_, _) => HoverHint.Text = "";
    }
    private void Tool(Panel panel, string icon, string hint, string key, RoutedEventHandler handler, double width = 25)
    {
        var button = new Button { Content = icon == "▾" ? (object)icon : UiIcons.Icon(icon), Width = width, Style = (Style)FindResource("Tool") };
        button.Click += handler; panel.Children.Add(button); Register(button, key, hint);
    }
    private bool Enabled(string key) => key switch
    {
        "edit" => CanEdit,
        "mode" => !locked && !modeLocked && operations.Count == 0,
        "idle" => !locked && operations.Count == 0,
        "launch" => CanUseBase,
        "designer" => CanUseBase && CanEdit && Selected?.Kind != "Web",
        "v8" => CanUseBase && CanEdit && IsV8,
        "legacy" => CanUseBase && CanEdit && Selected is { Version: "7.7", Kind: "File" },
        "file" => CanUseBase && CanEdit && IsV8 && Selected?.Kind == "File",
        "lock" => CanUseBase && CanEdit && IsV8 && Selected?.Kind == "File" && !Selected.Version.StartsWith("8.1"),
        "selected" => Selected != null && !locked,
        "selectededit" => CanUseBase && CanEdit,
        "favorite" => CanUseBase,
        "properties" => CanEdit && IsBaseView && (Selected != null || IsRealGroup),
        "delete" => CanEdit && IsBaseView && (Selected is { DisallowDelete: false } || IsRealGroup),
        "row" => IsBaseView && BasesGrid.SelectedItem != null && !locked,
        "tree" => IsBaseView && !locked,
        "history" => HistoryTab.IsChecked == true && Selected != null,
        "unavailable" => false,
        "refresh" => !locked && !scanning && operations.Count == 0,
        _ => true
    };
    private bool IsRealGroup => IsBaseView && BasesGrid.SelectedItem is ListRow { IsGroup: true, GroupKey: var key } && key.StartsWith("group:");
    private void UpdateActionStates()
    {
        if (!ready) return;
        EditingAvailable = CanEdit; InteractionAvailable = !locked && operations.Count == 0;
        foreach (var (control, key, hint) in actionControls)
        {
            bool enabled = Enabled(key); control.IsEnabled = enabled;
            string reason = operations.Count > 0 ? "Дождитесь завершения текущей операции." : locked ? "Программа заблокирована." :
                key == "refresh" && scanning ? "Обновление уже выполняется." : !CanEdit && key is "edit" or "v8" or "file" or "lock" or "properties" or "delete" or "command" or "designer" ? "Недоступно в режиме «Пользователь»." :
                key == "command" ? "Выберите команду." : key == "v8" || key == "file" || key == "lock" ? "Выберите подходящую базу 1С 8.x." : "Выберите подходящую запись в текущем разделе.";
            control.ToolTip = enabled ? hint : hint + "\n" + reason;
        }
        BaseToolbar.Visibility = TreeToolbar.Visibility = Visible(IsBaseView); HistoryToolbar.Visibility = Visible(HistoryTab.IsChecked == true);
        VersionFilter.Visibility = Visible(BasesTab.IsChecked == true);
        LegacyToolsButton.Visibility = Visible(catalog.Settings.EnableLegacy77 && LegacyTab.IsChecked == true);
        LegacyToolsButton.IsEnabled = !locked && operations.Count == 0;
        SessionGroup.IsEnabled = CanUseBase && Selected?.Version != "7.7" && Selected?.Kind != "Web";
        BusyIndicator.Visibility = Visible(operations.Count > 0 || scanning);
        StarterPanel.Visibility = Visible(catalog.Settings.ShowStarter);
        StarterButton.IsEnabled = StarterMenuButton.IsEnabled = !locked && operations.Count == 0;
        ClearSearchButton.Visibility = Visible(SearchBox.Text.Length > 0);
        SectionTitle.Text = HistoryTab.IsChecked == true ? "История запусков" : LegacyTab.IsChecked == true ? "Информационные базы: 1С:Предприятие 7.7" : "Информационные базы: 1С:Предприятие 8";
    }
    private void BindServiceMenu(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is string key && key.StartsWith("ui:")) Register(item, key[3..], (item.Header?.ToString() ?? "").Replace("_", ""));
            item.Icon ??= UiIcons.MenuIcon(item.Header?.ToString() ?? "");
            BindServiceMenu(item);
        }
    }
    private MenuItem Item(string caption, RoutedEventHandler action, string key = "idle", string gesture = "")
    {
        var item = new MenuItem { Header = caption, Icon = UiIcons.MenuIcon(caption, action.Method.Name), InputGestureText = gesture, IsEnabled = Enabled(key) };
        item.Click += action; item.ToolTip = item.IsEnabled ? caption : "Действие недоступно для выбранной записи или режима.";
        ToolTipService.SetShowOnDisabled(item, true); return item;
    }
    private ContextMenu NewMenu()
    { var menu = new ContextMenu(); menu.Opened += (_, _) => { actionMenuOpen = true; AddMenuIcons(menu); }; menu.Closed += (_, _) => actionMenuOpen = false; return menu; }
    private void OpenMenu(FrameworkElement anchor, ContextMenu menu)
    { AddMenuIcons(menu); anchor.ContextMenu = menu; menu.PlacementTarget = anchor; menu.Placement = PlacementMode.Custom; menu.CustomPopupPlacementCallback = (size, target, offset) => MenuPlacement.Below(anchor, size, target, offset); menu.IsOpen = true; menu.Focus(); }
    private ContextMenu BaseActions()
    {
        var menu = NewMenu();
        if (Selected is { } b)
        {
            menu.Items.Add(Item("1С:Предприятие", Enterprise_Click, "launch", "F3"));
            if (b.Kind != "Web") menu.Items.Add(Item("Конфигуратор", Designer_Click, "designer", "F4"));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Свойства…", Edit_Click, "properties", "Alt+Enter"));
            menu.Items.Add(Item("Сводная информация…", Summary_Click, "selected", "Ctrl+Alt+I"));
            menu.Items.Add(Item("Профили запуска…", LaunchProfiles_Click, "selectededit"));
            menu.Items.Add(Item("Переместить в группу…", MoveToGroup_Click, "properties"));
            menu.Items.Add(Item("Назначить версию…", AssignVersion_Click, "v8", "Ctrl+Shift+F8"));
            menu.Items.Add(Item(b.Favorite ? "Убрать из избранного" : "Добавить в избранное", Favorite_Click, "favorite", "F8"));
            menu.Items.Add(Item("Переименовать…", RenameBase_Click, "properties", "F2"));
            menu.Items.Add(Item("Дублировать запись…", Duplicate_Click, "properties"));
            if (b.Version == "7.7") menu.Items.Add(Item("Очистить / восстановить CDX-индексы…", LegacyIndexes_Click, "legacy"));
            if (b.Kind == "File") menu.Items.Add(Item("Открыть папку базы", BaseFolder_Click, "selected"));
            var copy = new MenuItem { Header = "Копировать" }; FillCopy(copy.Items); menu.Items.Add(copy);
            menu.Items.Add(Item("Создать ярлык…", Shortcut_Click, "properties"));
            if (b.Version != "7.7" && b.Kind != "Web")
            {
                var tools = new MenuItem { Header = "Обслуживание" };
                tools.Items.Add(Item("Проверить релиз…", CheckUpdates_Click, "v8", "F9"));
                tools.Items.Add(Item("Определить конфигурацию…", DetectConfiguration_Click, "v8"));
                tools.Items.Add(Item("Установить CFU / CF…", InstallUpdate_Click, "v8"));
                if (b.Kind == "File" && !b.Version.StartsWith("8.1")) tools.Items.Add(Item("Блокировка входа…", SessionLock_Click, "lock"));
                tools.Items.Add(Item("Тестирование базы…", CheckBase_Click, "v8"));
                if (b.Kind == "File") tools.Items.Add(Item("Проверка файла chdbfl…", CheckFile_Click, "file"));
                menu.Items.Add(tools);
            }
            menu.Items.Add(new Separator()); menu.Items.Add(Item("Удалить из списка…", Delete_Click, "delete", "Delete"));
        }
        else if (IsRealGroup)
        {
            menu.Items.Add(Item("Переименовать / переместить группу…", RenameGroup_Click, "properties", "F2"));
            menu.Items.Add(Item("Цвет группы…", GroupColor_Click, "properties"));
            menu.Items.Add(Item("Сводная информация…", Summary_Click, "row", "Ctrl+Alt+I"));
            menu.Items.Add(Item("Свернуть / развернуть", (_, _) => ToggleSelectedGroup(), "tree"));
            menu.Items.Add(Item("Удалить группу…", Delete_Click, "delete", "Delete"));
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(Item("Добавить базу…", Add_Click, "edit", "Insert"));
        menu.Items.Add(Item("Добавить группу…", AddGroup_Click, "edit", "Ctrl+Insert"));
        return menu;
    }
    private ContextMenu HistoryActions()
    {
        var menu = NewMenu(); menu.Items.Add(Item("Найти в списке баз", FindHistory_Click, "history", "Enter"));
        menu.Items.Add(Item("Запустить Предприятие", Enterprise_Click, "launch", "F3"));
        menu.Items.Add(Item("Запустить Конфигуратор", Designer_Click, "designer", "F4")); return menu;
    }
    private void ActionsMenu_Click(object sender, RoutedEventArgs e) => OpenMenu((FrameworkElement)sender, BaseActions());
    private void ReleasesMenu_Click(object sender, RoutedEventArgs e)
    { var menu = NewMenu(); menu.Items.Add(Item("Проверить релиз базы…", CheckUpdates_Click, "v8", "F9")); menu.Items.Add(Item("Актуальные релизы…", ActualReleases_Click, "edit", "Alt+F9")); menu.Items.Add(Item("Список конфигураций…", Configurations_Click, "edit")); OpenMenu((FrameworkElement)sender, menu); }
    private void SnapshotsMenu_Click(object sender, RoutedEventArgs e)
    { var menu = NewMenu(); menu.Items.Add(Item("Сохранить снимок каталога", Snapshot_Click)); menu.Items.Add(Item("Восстановить снимок…", RestoreSnapshot_Click, "edit")); menu.Items.Add(Item("Экспортировать список .v8i…", Export_Click)); OpenMenu((FrameworkElement)sender, menu); }
    private void FillCopy(ItemCollection items)
    {
        if (Selected is not { } b) return;
        items.Add(Item("Сводную информацию", CopySummary_Click, "selected"));
        items.Add(new Separator());
        foreach (var pair in new[] { ("Наименование", b.Name), ("ID базы", b.Id), ("Строку подключения", b.ConnectionDisplay), ("Адрес / каталог", b.Location), ("Версию платформы", b.Version) })
            items.Add(Item(pair.Item1, async (_, _) => await CopyTextAsync(pair.Item2, "Скопировано: " + pair.Item1), "selected"));
    }
    private void CopyMenu_Click(object sender, RoutedEventArgs e) { var menu = NewMenu(); FillCopy(menu.Items); OpenMenu((FrameworkElement)sender, menu); }
    private void AddMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu();
        foreach (string kind in new[] { "File", "Server", "Web" })
        { string value = kind; menu.Items.Add(Item(kind == "File" ? "Файловая база…" : kind == "Server" ? "База на сервере 1С…" : "Веб-публикация…", (_, _) => AddBase(value), "edit")); }
        menu.Items.Add(Item("Создать пустую базу / из шаблона…", CreateBase_Click, "edit"));
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Импорт .v8i…", Import_Click, "edit")); if (catalog.Settings.EnableLegacy77) menu.Items.Add(Item("Импорт списка 7.7…", Import77_Click, "edit")); OpenMenu((FrameworkElement)sender, menu);
    }
    private void AddBase(string? kind = null)
    {
        if (!CanEdit || !IsBaseView) return;
        var f = new FormDialog("Регистрация информационной базы", 680, 420) { Owner = this };
        f.Section("Тип и платформа");
        f.Choice("kind", "Расположение", kind == "Server" ? "Сервер 1С" : kind == "Web" ? "Веб-публикация" : "Файловая база", new[] { "Файловая база", "Сервер 1С", "Веб-публикация" });
        f.Choice("version", "Версия", LegacyTab.IsChecked == true ? "7.7" : catalog.Settings.DefaultVersion, new[] { "8.5", "8.4", "8.3", "8.2", "8.1", "7.7" }.Where(v => catalog.Settings.EnableLegacy77 || v != "7.7"), true);
        f.Note("Будет добавлено подключение к существующей базе. Файлы новой базы здесь не создаются.");
        f.SetAcceptText("Далее →");
        f.Validate = () => f.Value("version").Length > 0 && (f.Value("version").TrimEnd('*', '.') != "7.7" || catalog.Settings.EnableLegacy77 && f.Value("kind") == "Файловая база") || f.SetError("Для 7.7 включите её поддержку в настройках и выберите файловую базу; версия обязательна.");
        if (f.ShowDialog() != true) return;
        Edit(new BaseEntry { DoubleClickAction = catalog.Settings.DefaultDoubleClickAction, Group = SelectedGroupPath, Version = f.Value("version"), Kind = f.Value("kind") == "Сервер 1С" ? "Server" : f.Value("kind") == "Веб-публикация" ? "Web" : "File", Location = f.Value("kind") == "Файловая база" ? catalog.Settings.DefaultFolder : "" });
    }
    private void LaunchMenu_Click(object sender, RoutedEventArgs e)
    {
        OpenMenu((FrameworkElement)sender, CreateLaunchMenu(sender == DesignerMenuButton));
    }
    private ContextMenu CreateLaunchMenu(bool designer)
    {
        var menu = NewMenu(); string key = designer ? "designer" : "launch";
        menu.Items.Add(Item("Запустить", (_, _) => Launch(designer), key, designer ? "F4" : "F3"));
        menu.Items.Add(Item("Параметры на один запуск…", (_, _) => LaunchOptions(designer), key, designer ? "Ctrl+F4" : "Ctrl+F3"));
        menu.Items.Add(Item("Запросить вход в окне 1С", (_, _) => Launch(designer, clearLogin: true), key, designer ? "Shift+F4" : "Shift+F3"));
        menu.Items.Add(Item("Запустить от администратора", (_, _) => Launch(designer, administrator: true), key));
        if (Selected is { } selected)
        {
            var profiles = new MenuItem { Header = "Именованные профили" };
            foreach (var profile in catalog.LaunchProfiles.Where(p => p.Designer == designer && p.AppliesTo(selected)))
            { var saved = profile; profiles.Items.Add(Item(saved.Name, (_, _) => RunProfile(saved), key)); }
            profiles.Items.Add(new Separator()); profiles.Items.Add(Item("Управление профилями…", LaunchProfiles_Click, "selected")); menu.Items.Add(profiles);
            if (!designer && selected.Kind == "Web") menu.Items.Add(Item("Открыть веб-клиент в браузере", WebClient_Click, "launch"));
        }
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Посмотреть команду запуска…", (_, _) => ShowLaunchPreview(designer), "selected"));
        return menu;
    }
    private void LaunchOptions(bool designer)
    {
        if (Selected is not { } entry || !Enabled(designer ? "designer" : "launch")) return;
        var f = new LaunchOptionsDialog(entry, LaunchProfile.From(entry, designer, CurrentClient), platforms, examples: CreateParameterExamples()) { Owner = this };
        if (f.ShowDialog() != true) return;
        var profile = f.Read();
        if (f.SaveProfile && !Commit(() => catalog.LaunchProfiles.Add(profile))) return;
        Launch(designer, temporary: profile.Apply(entry), clientOverride: profile.Client);
    }

    private async void ShowLaunchPreview(bool designer)
    {
        if (Selected is not { } b) return;
        var entry = b.Copy(); string client = CurrentClient; var available = platforms.ToList();
        try { var text = await Task.Run(() => LaunchService.Preview(LaunchService.Build(entry, designer, client, available))); if (IsVisible) FormDialog.ShowText(this, "Команда запуска", text); }
        catch (Exception ex) { Error("Команда запуска", ex); }
    }
    private void StarterMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu();
        menu.Items.Add(Item("Обычный запуск стартера", Starter_Click));
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Distinct())
        {
            string file = Path.Combine(root, "1cv8", "common", "1cestart.exe");
            if (File.Exists(file)) menu.Items.Add(Item(file, (_, _) => StartStarter(file)));
        }
        menu.Items.Add(new Separator()); menu.Items.Add(Item("Установленные платформы…", Platforms_Click)); OpenMenu((FrameworkElement)sender, menu);
    }
    private void StartStarter(string file)
    {
        if (isolatedSession) return;
        if (locked || operations.Count > 0) return;
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch (Exception ex) { Error("1С:Стартер", ex); }
    }
    private void Platforms_Click(object sender, RoutedEventArgs e) => new PlatformsWindow(platforms) { Owner = this }.ShowDialog();
    private void ClearSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }
    private void RefreshHistory_Click(object sender, RoutedEventArgs e) { if (Enabled("idle")) BuildRows(); }
    private void FindHistory_Click(object sender, RoutedEventArgs e) { if (HistoryGrid.SelectedItem is LaunchRecord r && catalog.Bases.Any(b => b.Id == r.BaseId)) SelectBase(r.BaseId); }
    private void ToggleSelectedGroup(bool? expand = null)
    {
        if (!IsBaseView || BasesGrid.SelectedItem is not ListRow { IsGroup: true } row) return;
        if (expand == true) collapsed.Remove(row.GroupKey); else if (expand == false) collapsed.Add(row.GroupKey); else if (!collapsed.Remove(row.GroupKey)) collapsed.Add(row.GroupKey);
        string key = row.GroupKey; BuildRows(); BasesGrid.SelectedItem = ((IEnumerable<ListRow>)BasesGrid.ItemsSource).FirstOrDefault(r => r.GroupKey == key);
    }
    private void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ListRow { Entry: { } b } row, Tag: string action } button) return;
        if (!InteractionAvailable || !BasesGrid.Items.Contains(row)) return;
        BasesGrid.SelectedItem = row;
        if (action == "favorite") Favorite_Click(sender, e); else if (action == "edit") Edit_Click(sender, e);
        else if (action == "launch") Launch(false);
        else if (action == "launch-menu") { var menu = CreateLaunchMenu(false); menu.Items.Insert(1, Item("Конфигуратор", Designer_Click, "designer", "F4")); OpenMenu(button, menu); }
        e.Handled = true;
    }
    private static bool InButton(object source)
    {
        for (var current = source as DependencyObject; current != null; current = current is Visual ? VisualTreeHelper.GetParent(current) : null)
            if (current is ButtonBase) return true;
        return false;
    }
    private void Grid_Disclosure_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: ListRow row } && row.IsGroup) { BasesGrid.SelectedItem = row; ToggleSelectedGroup(); e.Handled = true; } }
    private void ContextSelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready) UpdateSelection(); }
    private void History_RightClick(object sender, MouseButtonEventArgs e) { if (FindRow(e.OriginalSource) is { } row) HistoryGrid.SelectedItem = row.Item; else HistoryGrid.SelectedItem = null; }
    private void HandleInteractionKey(KeyEventArgs e)
    {
        if (!ready || locked) return;
        if (ServiceMenu.IsSubmenuOpen && e.Key == Key.Escape) { ServiceMenu.IsSubmenuOpen = false; e.Handled = true; BasesGrid.Focus(); return; }
        if (actionMenuOpen || ServiceMenu.IsSubmenuOpen) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key; var modifiers = Keyboard.Modifiers;
        bool editingText = Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox || VersionFilter.IsKeyboardFocusWithin;
        void Do(Action action) { e.Handled = true; if (!e.IsRepeat) action(); }
        if (HandleBaseLaunchKey(key, modifiers, e.IsRepeat)) { e.Handled = true; return; }
        if (key == Key.I && modifiers == (ModifierKeys.Control | ModifierKeys.Alt)) { Do(() => Summary_Click(this, e)); return; }
        if (key == Key.F8 && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) { Do(() => AssignVersion_Click(this, e)); return; }
        if (key == Key.Insert && modifiers == ModifierKeys.Shift && !editingText) { Do(() => { if (Enabled("properties")) Duplicate_Click(this, e); }); return; }
        if (key == Key.P && modifiers == (ModifierKeys.Control | ModifierKeys.Alt)) { Do(() => Lock_Click(this, e)); return; }
        if (key == Key.F11 && modifiers == ModifierKeys.Shift) { Do(() => { if (Enabled("properties") && Selected?.Kind == "File") Edit(Selected, 1); }); return; }
        if (key == Key.F12 && modifiers == ModifierKeys.Shift) { Do(() => BaseFolder_Click(this, e)); return; }
        if (key == Key.F1 && modifiers is ModifierKeys.None or ModifierKeys.Alt) { Do(() => Help_Click(this, e)); return; }
        if (key == Key.B && modifiers == ModifierKeys.Control) { Do(() => { ServiceMenu.Focus(); ServiceMenu.IsSubmenuOpen = true; BookmarksMenu.IsSubmenuOpen = true; BookmarksMenu.Focus(); }); return; }
        if (key == Key.F && modifiers == ModifierKeys.Control) { Do(() => { SearchBox.Focus(); SearchBox.SelectAll(); }); return; }
        if (key == Key.F10 && modifiers == ModifierKeys.Alt) { Do(() => { ServiceMenu.Focus(); ServiceMenu.IsSubmenuOpen = true; }); return; }
        if (key == Key.F11 && modifiers == ModifierKeys.Alt) { Do(() => { if (Enabled("idle")) SystemMenu_Click(SystemMenuButton, e); }); return; }
        if (key == Key.M && modifiers is ModifierKeys.Control or ModifierKeys.Alt) { Do(SwitchMonitor); return; }
        if (key is Key.F3 or Key.F4 && modifiers is ModifierKeys.None or ModifierKeys.Control or ModifierKeys.Shift)
        {
            bool designer = key == Key.F4;
            Do(() => { if (!Enabled(designer ? "designer" : "launch")) return; if (modifiers == ModifierKeys.Control) LaunchOptions(designer); else Launch(designer, clearLogin: modifiers == ModifierKeys.Shift); }); return;
        }
        if (key == Key.F9 && modifiers == ModifierKeys.Alt) { Do(() => { if (CanEdit) ActualReleases_Click(this, e); }); return; }
        if (key == Key.F9 && modifiers == ModifierKeys.None) { Do(() => { if (Enabled("v8")) CheckUpdates_Click(this, e); }); return; }
        if (key == Key.F8 && modifiers == ModifierKeys.None) { Do(() => { if (Enabled("favorite")) Favorite_Click(this, e); }); return; }
        if (key == Key.F6 && modifiers == ModifierKeys.None) { Do(() => { if (CanEdit) Settings_Click(this, e); }); return; }
        if (key == Key.F5 && modifiers == ModifierKeys.None) { Do(() => { if (HistoryTab.IsChecked == true) RefreshHistory_Click(this, e); else if (Enabled("refresh")) Refresh_Click(this, e); }); return; }
        if (key == Key.L && modifiers == ModifierKeys.Control) { Do(() => SnapshotList_Click(this, e)); return; }
        if (key == Key.J && modifiers == ModifierKeys.Control && IsBaseView) { Do(JumpFavorites); return; }
        if (key == Key.Escape)
        {
            Do(() => { if (SearchBox.Text.Length > 0) SearchBox.Clear(); else if (IsBaseView && VersionFilter.SelectedIndex > 0) VersionFilter.SelectedIndex = 0; else if (catalog.Settings.EscapeToTray) HideToTray(); }); return;
        }
        if (key == Key.Enter && modifiers == ModifierKeys.Alt)
        { Do(() => { if (Enabled("properties")) Edit_Click(this, e); }); return; }
        if (editingText) return;
        if (modifiers == ModifierKeys.None && key is Key.F11 or Key.F12 && IsBaseView)
        { Do(() => { if (key == Key.F11) Collapse_Click(this, e); else Expand_Click(this, e); }); return; }
        if (key == Key.Insert && modifiers == ModifierKeys.Control && IsBaseView) { Do(() => { if (CanEdit) AddGroup_Click(this, e); }); return; }
        if (modifiers != ModifierKeys.None) return;
        if (key == Key.Insert) { Do(() => { if (!CanEdit) return; if (IsBaseView) AddBase(); }); }
        else if (key == Key.Delete) { Do(() => { if (IsBaseView && BasesGrid.IsKeyboardFocusWithin && Enabled("delete")) Delete_Click(this, e); }); }
        else if (key == Key.F2 && IsBaseView) { Do(() => { if (!CanEdit) return; if (IsRealGroup) RenameGroup_Click(this, e); else RenameBase_Click(this, e); }); }
        else if (key == Key.Enter)
        {
            if (HistoryGrid.IsKeyboardFocusWithin) Do(() => FindHistory_Click(this, e));
            else if (BasesGrid.IsKeyboardFocusWithin) Do(() => { if (BasesGrid.SelectedItem is ListRow { IsGroup: true }) ToggleSelectedGroup(); else Launch(false); });
        }
        else if (key is Key.Left or Key.Right && BasesGrid.IsKeyboardFocusWithin && BasesGrid.SelectedItem is ListRow { IsGroup: true }) Do(() => ToggleSelectedGroup(key == Key.Right));
    }
    private void JumpFavorites()
    {
        SearchBox.Clear(); collapsed.Remove("@favorites"); BuildRows();
        var row = ((IEnumerable<ListRow>)BasesGrid.ItemsSource).FirstOrDefault(r => r.GroupKey == "@favorites");
        if (row != null) { BasesGrid.SelectedItem = row; BasesGrid.ScrollIntoView(row); BasesGrid.Focus(); }
        else StatusText.Text = "В этом разделе пока нет избранных баз.";
    }
    private void RelocatePaths_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit) return;
        var f = new FormDialog("Замена пути файловых баз", 780, 460) { Owner = this }; f.Section("Пути");
        f.Text("old", "Старый диск / каталог", ""); f.Text("new", "Новый диск / каталог", "");
        f.Note("Адреса файловых подключений сохраняются в пользовательском списке 1С. Файлы и папки не перемещаются. Подключения из корпоративных списков меняются в их источнике.");
        f.SetAcceptText("Предпросмотр"); f.Validate = () => CatalogRelocation.Preview(catalog, f.Value("old"), f.Value("new")).Count > 0 || f.SetError("Подходящих файловых баз нет.");
        if (f.ShowDialog() != true) return;
        var changes = CatalogRelocation.Preview(catalog, f.Value("old"), f.Value("new"));
        var review = new FormDialog("Подтвердить замену путей", 850, 590) { Owner = this }; review.Section("Изменения");
        var text = review.Text("preview", "Подключения: " + changes.Count, string.Join("\n\n", changes.Select(c => c.Name + "\n" + c.Before + "\n→ " + c.After)), true);
        text.IsReadOnly = true; text.Height = 280; text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        review.Check("confirmed", "Подтверждаю изменение перечисленных подключений", false);
        review.Note("Перед сохранением будет создан снимок каталога. Доступность новых каталогов не проверяется автоматически.");
        review.Validate = () => review.Flag("confirmed") || review.SetError("Подтвердите список изменений."); review.SetAcceptText("Заменить пути");
        if (review.ShowDialog() != true) return;
        try
        {
            store.Save(catalog); store.Snapshot();
            if (Commit(() => { foreach (var change in changes) catalog.Bases.First(b => b.Id == change.Id).Location = change.After; }))
            { _ = RefreshMetadata(); StatusText.Text = "Обновлено подключений: " + changes.Count; }
        }
        catch (Exception ex) { Error("Замена путей", ex); }
    }
    private void Help_Click(object sender, RoutedEventArgs e) => FormDialog.ShowText(this, "Клавиши и действия", "F3 / F4 — Предприятие / Конфигуратор\nCtrl+F3 / Ctrl+F4 — параметры на один запуск\nShift+F3 / Shift+F4 — запросить вход в 1С\nF5 — обновить сведения\nF6 — настройки\nF8 — избранное\nF9 / Alt+F9 — релиз базы / актуальные релизы\nF11 / F12 — свернуть / развернуть группы\nCtrl+F — поиск, Escape — очистить поиск\nInsert / Ctrl+Insert — база / группа\nAlt+Enter — свойства выбранного элемента\nCtrl+L — снимки каталога\nCtrl+B — закладки\nF1 / Alt+F1 — справка\nAlt+F11 — системное меню\nCtrl+M / Alt+M — следующий монитор\n\nЩелчок по группе выделяет её. Стрелка слева и двойной щелчок сворачивают группу. Правое меню относится к строке под указателем. На вкладке истории Enter находит базу; Delete не удаляет её.");
}

internal sealed class OperationSet
{
    private readonly HashSet<string> items = new(StringComparer.OrdinalIgnoreCase);
    public event Action? Changed;
    public int Count => items.Count;
    public bool Contains(string key) => items.Contains(key);
    public bool Add(string key) { bool result = items.Add(key); if (result) Changed?.Invoke(); return result; }
    public bool Remove(string key) { bool result = items.Remove(key); if (result) Changed?.Invoke(); return result; }
}
