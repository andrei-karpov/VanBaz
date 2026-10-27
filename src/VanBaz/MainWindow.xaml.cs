using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow : Window
{
    private readonly CatalogStore store;
    private Catalog catalog;
    private readonly bool isolatedSession;
    private readonly HashSet<string> collapsed = new();
    private PlatformInventory platformInventory = new();
    private Task? platformRefresh;
    private List<PlatformInfo> platforms => platformInventory.Platforms;
    private readonly Dictionary<string, (string Date, string Size, long? Bytes, DateTime? Modified)> metadata = new();
    private bool ready;
    private bool scanning;
    private BaseEntry? Selected => IsBaseView ? (BasesGrid.SelectedItem as ListRow)?.Entry : HistoryTab.IsChecked == true && HistoryGrid.SelectedItem is LaunchRecord r ? catalog.Bases.FirstOrDefault(b => b.Id == r.BaseId) : null;

    public MainWindow(CatalogStore store, Catalog catalog, StartupOptions? options = null, bool useOfficialLists = false)
        : this(store, catalog, false, options, useOfficialLists) { }

    // Test harnesses can suppress external actions without a user-facing mode.
    internal MainWindow(CatalogStore store, Catalog catalog, bool isolatedSession, StartupOptions? options = null, bool useOfficialLists = false)
    {
        this.store = store; this.catalog = catalog; this.isolatedSession = isolatedSession; this.useOfficialLists = useOfficialLists && !isolatedSession; modeOverride = options?.Mode; modeLocked = options?.LockMode == true;
        InitializeComponent();
        SearchBox.ToolTip = "Поиск по имени, ID, группе и подключению · Ctrl+F";
        VersionFilter.ItemsSource = new[] { "Все", "8.1", "8.2", "8.3", "8.4", "8.5" };
        VersionFilter.SelectedIndex = 0;
        ready = true;
        foreach (var key in catalog.Settings.CollapsedGroups) collapsed.Add(key);
        ApplyPreferences(true);
        BuildRows(catalog.Settings.SelectedId);
        InitializeTray();
        InitializeInteraction(); InitializeColumns();
        Loaded += async (_, _) => { ApplyStartupDisplay(); if (catalog.Settings.StartMinimized) WindowState = WindowState.Minimized; ApplyDesktopSettings(); CheckPendingRecovery(); await RefreshOfficialListsAsync(); await RefreshMetadata(); await CheckAppUpdateOnStartup(); };
        Closed += (_, _) => { platformInventory.Cancel(); hotkeys?.Dispose(); };
    }

    private void BuildRows(string? selectId = null)
    {
        if (!ready) return;
        string? selectGroup = selectId == null && (BasesGrid.SelectedItem as ListRow) is { IsGroup: true } selectedGroup ? selectedGroup.GroupKey : null;
        selectId ??= (BasesGrid.SelectedItem as ListRow)?.Entry?.Id;
        var term = SearchBox.Text.Trim();
        var version = LegacyTab.IsChecked != true && VersionFilter.SelectedIndex > 0 ? (string)VersionFilter.SelectedItem : "";
        var filtered = catalog.Bases.Where(b => (LegacyTab.IsChecked == true ? b.Version == "7.7" : b.Version != "7.7") && (version.Length == 0 || b.Version == version || b.Version.StartsWith(version + ".")) &&
            (term.Length == 0 || (b.Name + " " + b.Id + " " + b.Group + " " + b.Location + " " + b.Notes + " " + b.Description).Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToList();
        var rows = new List<ListRow>();
        void AddHeader(string title, string key, int count, int indent = 0)
        {
            rows.Add(new() { IsGroup = true, Name = title + (catalog.Settings.ShowCounts ? "   [" + count + "]" : ""), GroupKey = key, Icon = collapsed.Contains(key) && term.Length == 0 ? "▸" : "▾", Indent = indent, Foreground = ThemeService.Readable(key.StartsWith("group:") ? catalog.Groups.FirstOrDefault(g => g.Path == key[6..])?.Color ?? "#287EB0" : "#287EB0", "ThemeGroup") });
        }
        void AddBase(BaseEntry b, int indent = 17, bool favorite = false)
        {
            bool checkedFile = metadata.TryGetValue(b.Id, out var meta);
            rows.Add(new() { Entry = b, Name = b.Name, Icon = favorite ? "★" : b.Kind switch { "Server" => "▥", "Web" => "◎", _ => "▪" },
                Date = meta.Date ?? "", Size = meta.Size ?? "", Indent = indent, Hint = b.Location, MissingBold = checkedFile && meta.Size == "" && catalog.Settings.CheckExistence && catalog.Settings.MissingBold, MissingItalic = checkedFile && meta.Size == "" && catalog.Settings.CheckExistence && catalog.Settings.MissingItalic, MissingStrike = checkedFile && meta.Size == "" && catalog.Settings.CheckExistence && catalog.Settings.MissingStrike,
                Foreground = ThemeService.Readable(ListEntryColor(b, favorite, checkedFile && meta.Size == "" && catalog.Settings.CheckExistence)) });
        }
        var favorites = filtered.Where(b => b.Favorite).ToList();
        if (favorites.Count > 0)
        {
            AddHeader("ИЗБРАННОЕ", "@favorites", favorites.Count);
            if (!collapsed.Contains("@favorites") || term.Length > 0) foreach (var b in SortEntries(favorites)) AddBase(b, 17, true);
        }
        if (filtered.Count > 0 || catalog.Groups.Count > 0)
        {
            AddHeader("ИНФОРМАЦИОННЫЕ БАЗЫ", "@all", filtered.Count);
            if (!collapsed.Contains("@all") || term.Length > 0)
            {
                var allGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in catalog.Groups.Select(g => g.Path).Concat(filtered.Select(b => b.Group)))
                {
                    string prefix = "";
                    foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries)) { prefix = prefix.Length == 0 ? part : prefix + "/" + part; allGroups.Add(prefix); }
                }
                void Children(string parent, int depth)
                {
                    var children = allGroups.Where(g => ParentGroup(g).Equals(parent, StringComparison.OrdinalIgnoreCase) && (term.Length == 0 && version.Length == 0 && LegacyTab.IsChecked != true || filtered.Any(b => InGroup(b.Group, g))));
                    children = catalog.Settings.Alphabetical ? children.OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase) : children.OrderBy(g => catalog.Groups.FirstOrDefault(x => x.Path == g)?.Order ?? 0);
                    var bases = filtered.Where(b => b.Group.Equals(parent, StringComparison.OrdinalIgnoreCase));
                    bases = SortEntries(bases);
                    void AddEntries() { foreach (var b in bases) AddBase(b, 16 + depth * 17); }
                    if (!catalog.Settings.GroupsFirst) AddEntries();
                    foreach (var group in children)
                    {
                        var key = "group:" + group;
                        AddHeader(group.Split('/').Last(), key, filtered.Count(b => InGroup(b.Group, group)), 7 + depth * 17);
                        if (!collapsed.Contains(key) || term.Length > 0) Children(group, depth + 1);
                    }
                    if (catalog.Settings.GroupsFirst) AddEntries();
                }
                Children("", 0);
            }
        }
        BasesGrid.ItemsSource = rows;
        var selected = (selectId == null ? null : rows.FirstOrDefault(r => r.Entry?.Id == selectId)) ?? (selectGroup == null ? null : rows.FirstOrDefault(r => r.GroupKey == selectGroup)) ?? rows.FirstOrDefault(r => r.Entry != null);
        BasesGrid.SelectedItem = selected;
        EmptyPanel.Visibility = catalog.Bases.All(b => LegacyTab.IsChecked == true ? b.Version != "7.7" : b.Version == "7.7") && IsBaseView && term.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Visibility = term.Length > 0 && filtered.Count == 0 && IsBaseView ? Visibility.Visible : Visibility.Collapsed;
        var historySelection = HistoryGrid.SelectedItem;
        HistoryGrid.ItemsSource = catalog.History.Where(h => term.Length == 0 || (h.Name + " " + h.Mode + " " + h.BaseId).Contains(term, StringComparison.CurrentCultureIgnoreCase)).OrderByDescending(h => h.Time).ToList();
        HistoryGrid.SelectedItem = historySelection;
        if (!IsBaseView) NoResults.Visibility = Visible(term.Length > 0 && HistoryGrid.Items.Count == 0);
        StatusText.Text = IsBaseView ? $"Баз: {catalog.Bases.Count(b => LegacyTab.IsChecked == true ? b.Version == "7.7" : b.Version != "7.7")} · найдено: {filtered.Count}  |  Alt+Enter — свойства" : $"История: {HistoryGrid.Items.Count} · Enter — найти базу";
        UpdateSelection();
        RefreshTrayMenu();
    }

    private async Task RefreshMetadata()
    {
        if (scanning) return;
        scanning = true; UpdateActionStates();
        var entries = catalog.Bases.Select(b => b.Copy()).ToList();
        try
        {
            var result = await Task.Run(() =>
            {
                var found = new Dictionary<string, (string, string, long?, DateTime?)>();
                foreach (var b in entries.Where(b => b.Kind == "File"))
                {
                    try
                    {
                        // Do not block UI or probe network drives just to populate a list.
                        var root = Path.GetPathRoot(b.Location);
                        if (string.IsNullOrEmpty(root) || b.Location.StartsWith(@"\\") || new DriveInfo(root).DriveType != DriveType.Fixed) continue;
                        var file = new FileInfo(Path.Combine(b.Location, b.Version == "7.7" ? "1Cv7.MD" : "1Cv8.1CD"));
                        found[b.Id] = file.Exists ? (file.LastWriteTime.ToString("dd.MM.yy"), FormatSize(file.Length), file.Length, file.LastWriteTime) : ("", "", null, null);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
                return found;
            });
            metadata.Clear();
            foreach (var item in result) metadata[item.Key] = item.Value;
            BuildRows();
        }
        finally { scanning = false; UpdateActionStates(); }
    }
    private static string FormatSize(long length) => length >= 1024L*1024*1024 ? (length / (1024d*1024*1024)).ToString("0.##") + " ГБ" : (length / (1024d*1024)).ToString("0.#") + " МБ";
    private void UpdateSelection()
    {
        var b = Selected;
        SelectedName.Text = b?.Name ?? "Выберите информационную базу";
        SelectedNotes.Text = b == null ? "" : (b.Description + " " + b.Notes).Trim();
        SelectedConnection.Text = b?.ConnectionDisplay ?? "";
        bool fileLink = b?.Kind == "File";
        ConnectionLink.Content = b?.ConnectionDisplay ?? "";
        ConnectionLink.Visibility = fileLink ? Visibility.Visible : Visibility.Collapsed;
        SelectedConnection.Visibility = fileLink ? Visibility.Collapsed : Visibility.Visible;
        ConnectionLink.IsEnabled = fileLink && !locked;
        ConnectionLink.ToolTip = "Открыть каталог (Shift+F12)\n" + b?.Location;
        if (b != null) lastSelectedBaseId = b.Id;
        SelectedName.ToolTip = b?.Name; SelectedNotes.ToolTip = SelectedNotes.Text; SelectedConnection.ToolTip = b?.ConnectionDisplay;
        UpdateActionStates();
        PlatformStatus.Text = b == null ? "Платформ: " + platforms.Count : b.Version + " · " + b.Architecture;
        if (b != null && sessionBaseId != b.Id)
        {
            sessionBaseId = b.Id;
            ThinMode.IsChecked = b.Client == "Тонкий клиент";
            ThickMode.IsChecked = b.Client == "Толстый клиент";
            AutoMode.IsChecked = b.Client == "Авто";
            OrdinaryMode.IsChecked = b.Client == "Обычный режим";
        }
    }
    private bool Commit(Action mutation)
    {
        var old = catalog.Copy();
        try
        {
            if (HasPendingCatalogRecovery()) throw new IOException("Сначала завершите восстановление прерванного согласования в меню сервиса. Каталог сохранён без новых изменений.");
            mutation();
            if (useOfficialLists && OfficialListSession.HasOfficialChanges(old, catalog))
            {
                if (officialLists == null) throw new InvalidOperationException("Штатный список ещё не прочитан или недоступен. Нажмите F5. Личные настройки можно сохранять независимо.");
                catalog = officialLists.SaveChanges(old, catalog, store);
                StatusText.Text = officialLists.Warning ?? "Изменения сохранены в списке 1С. Личные настройки остаются в ВанБаз.";
            }
            else store.Save(catalog);
            BuildRows(); return true;
        }
        catch (Exception ex) { catalog = old; BuildRows(); Error("Изменение не сохранено", ex); return false; }
    }
    private static void Error(string title, Exception ex) => MessageBox.Show(ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    private void Edit(BaseEntry? entry, int section = 0)
    {
        if (!AllowTools()) return;
        var dialog = new BaseEditor(entry?.Copy() ?? new BaseEntry { Group = SelectedGroupPath, Version = catalog.Settings.DefaultVersion, Location = catalog.Settings.DefaultFolder }, platforms, catalog.Bases.Select(b => b.Group).Concat(catalog.Groups.Select(g => g.Path)).Distinct().ToList(), catalog.Configurations, allowLock: !isolatedSession && entry != null, enableLegacy77: catalog.Settings.EnableLegacy77, examples: CreateParameterExamples()) { Owner = this };
        if (entry == null || !catalog.Bases.Any(b => b.Id == entry.Id)) dialog.UseNewNamePolicy(catalog.Settings);
        dialog.SelectSection(section);
        if (entry != null && officialLists?.IsCommon(entry.Id) == true) dialog.UseCommonSource();
        dialog.ApplyChanges = () =>
        {
            bool saved = Commit(() =>
            {
                var value = dialog.Result.Copy();
                int index = catalog.Bases.FindIndex(b => b.Id == value.Id);
                if (index < 0) catalog.Bases.Add(value); else catalog.Bases[index] = value;
            });
            if (saved) SelectBase(dialog.Result.Id);
            return saved;
        };
        dialog.ShowDialog();
    }
    private string CurrentClient => ThinMode.IsChecked == true ? "Тонкий клиент" : ThickMode.IsChecked == true ? "Толстый клиент" : OrdinaryMode.IsChecked == true ? "Обычный режим" : "Авто";
    private async void Launch(bool designer, BaseEntry? temporary = null, string? clientOverride = null, bool clearLogin = false, bool administrator = false)
        => await LaunchAsync(designer, temporary, clientOverride, clearLogin, administrator);
    private async Task LaunchAsync(bool designer, BaseEntry? temporary = null, string? clientOverride = null, bool clearLogin = false, bool administrator = false)
    {
        if (locked || designer && !AllowTools()) return;
        if (operations.Count > 0) { MessageBox.Show("Дождитесь завершения операции с базой."); return; }
        var b = Selected;
        if (b == null || !catalog.Settings.EnableLegacy77 && (temporary ?? b).Version == "7.7") return;
        string launchKey = b.Id + ":" + designer;
        if (launchKey == lastLaunchKey && DateTime.UtcNow - lastLaunchClick < TimeSpan.FromMilliseconds(700)) return;
        lastLaunchKey = launchKey; lastLaunchClick = DateTime.UtcNow;
        if (isolatedSession) return;
        string op = "launch:" + b.Id; operations.Add(op);
        try
        {
            var launch = (temporary ?? b).Copy();
            if (clearLogin) { launch.EnterpriseLogin = launch.DesignerLogin = false; launch.EnterpriseSecret = launch.DesignerSecret = ""; launch.WindowsAuthentication = "Нет"; }
            if (administrator) launch.RunAsAdmin = true;
            string client = clientOverride ?? CurrentClient;
            // A shortcut can arrive before asynchronous startup discovery finishes.
            if (launch.Version != "7.7" && launch.Executable.Length == 0 && platformRefresh is { IsCompleted: false } search)
            {
                StatusText.Text = "Ожидание поиска платформ…";
                try { await search.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { throw new IOException("Поиск платформ ещё выполняется. Повторите запуск после его завершения или закрепите нужный EXE в свойствах базы."); }
            }
            var available = platforms.ToList();
            using var process = await Task.Run(() => LaunchService.Start(LaunchService.Build(launch, designer, client, available)));
            Commit(() => { b.LastLaunch = DateTime.Now; catalog.History.Insert(0, new() { BaseId = b.Id, Name = b.Name, Mode = designer ? "Конфигуратор" : "Предприятие", Time = DateTime.Now }); catalog.History = catalog.History.Take(200).ToList(); });
            StatusText.Text = "Создан процесс " + process.Id + " · " + b.Name;
            switch (catalog.Settings.AfterLaunchAction.StartsWith("Закрыть ", StringComparison.Ordinal) ? "Закрыть ВанБаз" : catalog.Settings.AfterLaunchAction)
            {
                case "Свернуть в панель задач": WindowState = WindowState.Minimized; break;
                case "Свернуть в трей": HideToTray(); break;
                case "Закрыть ВанБаз": operations.Remove(op); exitRequested = true; Close(); break;
            }
        }
        catch (Exception ex) { Error("Не удалось запустить 1С", ex); }
        finally { operations.Remove(op); }
    }
    private void Add_Click(object sender, RoutedEventArgs e) => AddBase();
    private void Edit_Click(object sender, RoutedEventArgs e) { if (Selected is { } b) Edit(b); else RenameGroup_Click(sender, e); }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!IsBaseView || !AllowTools()) return;
        if (Selected is not { } b) { if (IsRealGroup) DeleteGroup(); return; }
        if (b.DisallowDelete) { MessageBox.Show("Удаление записи запрещено в свойствах базы.", "ВанБаз"); return; }
        if (InterfaceMode == "Разработчик" && b.Kind == "File" && b.Version != "7.7") { DeleteBaseFile(b); return; }
        if (MessageBox.Show("Удалить «" + b.Name + "» из списка ВанБаз?\nФайлы информационной базы останутся на месте.", "Удаление записи", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            Commit(() => catalog.Bases.Remove(b));
    }
    private void Favorite_Click(object sender, RoutedEventArgs e) { if (!locked && operations.Count == 0 && Selected is { } b) Commit(() => b.Favorite = !b.Favorite); }
    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        var dialog = new OpenFileDialog { Title = "Импорт списка информационных баз", Filter = "Список 1С (*.v8i)|*.v8i|Все файлы|*.*", Multiselect = true };
        string initial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "1C", "1CEStart");
        if (Directory.Exists(initial)) dialog.InitialDirectory = initial;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imports = dialog.FileNames.Select(V8iImporter.Read).ToList();
            int added = 0;
            if (!Commit(() => { foreach (var result in imports) added += V8iImporter.Merge(catalog, result); })) return;
            MessageBox.Show($"Добавлено: {added}\nПовторных подключений: {imports.Sum(i => i.Entries.Count) - added}\nГрупп в файлах: {imports.Sum(i => i.Groups.Count)}\nНеподдерживаемых записей: {imports.Sum(i => i.Skipped)}", "Импорт списка", MessageBoxButton.OK, MessageBoxImage.Information);
            _ = RefreshMetadata();
        }
        catch (Exception ex) { Error("Ошибка импорта", ex); }
    }
    private void Enterprise_Click(object sender, RoutedEventArgs e) => Launch(false);
    private void Designer_Click(object sender, RoutedEventArgs e) => Launch(true);
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        ShowLaunchPreview(false);
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (operations.Count > 0) return;
        string op = "platform-refresh:" + Guid.NewGuid(); operations.Add(op);
        try { await RefreshPlatformsAsync(); await RefreshMetadata(); await RefreshOfficialListsAsync(); }
        catch (Exception ex) { Error("Обновление платформ", ex); }
        finally { operations.Remove(op); }
    }
    private async Task RefreshPlatformsAsync(PlatformScan? prepared = null)
    {
        try
        {
            var pending = platformInventory.RefreshAsync(catalog.Settings, prepared); platformRefresh = pending;
            var scan = await pending;
            if (scan != null) StatusText.Text = "Доступно платформ: " + platforms.Count + (scan.Errors.Count == 0 ? "" : ". Часть каталогов недоступна: " + string.Join("; ", scan.Errors.Take(3)));
        }
        catch (Exception ex) { StatusText.Text = "Поиск платформ: " + ex.Message; }
    }
    private void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Directory.CreateDirectory(store.Folder); Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { store.Folder }, UseShellExecute = false }); }
        catch (Exception ex) { Error("Папка каталога", ex); }
    }
    private void Expand_Click(object sender, RoutedEventArgs e) { collapsed.Clear(); BuildRows(); }
    private void Collapse_Click(object sender, RoutedEventArgs e) { foreach (var row in ((IEnumerable<ListRow>)BasesGrid.ItemsSource).Where(r => r.IsGroup)) collapsed.Add(row.GroupKey); BuildRows(); }
    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show("ВанБаз " + typeof(App).Assembly.GetName().Version?.ToString(3) + "\nМенеджер информационных баз 1С.\n\nЗапуск баз, штатные списки 1С, группы, избранное, профили запуска, кэш, ярлыки, горячие клавиши и оформление.\n\nКаталог: " + store.Folder, "О ВанБаз", MessageBoxButton.OK, MessageBoxImage.Information);
    private void Exit_Click(object sender, RoutedEventArgs e) { exitRequested = true; Close(); }
    private void Search_Changed(object sender, TextChangedEventArgs e) => BuildRows();
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => BuildRows();
    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (ready) UpdateSelection(); }
    private void Grid_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InButton(e.OriginalSource) || dragging || FindRow(e.OriginalSource) is not { Item: ListRow row }) return;
        BasesGrid.SelectedItem = row;
        if (row.IsGroup) ToggleSelectedGroup();
        else if (row.Entry is { } b) { if (b.DoubleClickAction == "Свойства") Edit(b); else if (b.DoubleClickAction == "Предприятие с параметрами") LaunchOptions(false); else Launch(b.DoubleClickAction == "Конфигуратор", clearLogin: b.DoubleClickAction == "Предприятие с аутентификацией"); }
        e.Handled = true;
    }
    private static DataGridRow? FindRow(object source)
    {
        var current = source as DependencyObject;
        while (current != null && current is not DataGridRow) current = current is Visual ? VisualTreeHelper.GetParent(current) : null;
        return current as DataGridRow;
    }
    private void Grid_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (HeaderAt(e.OriginalSource) != null) return;
        BasesGrid.SelectedItem = FindRow(e.OriginalSource)?.Item;
    }
    private void Tab_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (!catalog.Settings.EnableLegacy77 && LegacyTab.IsChecked == true) { BasesTab.IsChecked = true; return; }
        bool history = HistoryTab.IsChecked == true;
        HistoryGrid.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        BasesGrid.Visibility = IsBaseView ? Visibility.Visible : Visibility.Collapsed;
        VersionFilter.SelectedIndex = 0;
        SearchBox.Clear();
        BuildRows();
    }
    private void History_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindRow(e.OriginalSource)?.Item is not LaunchRecord record) return;
        if (catalog.Bases.Any(b => b.Id == record.BaseId)) SelectBase(record.BaseId);
        else StatusText.Text = "База из этой записи истории уже удалена из каталога.";
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        HandleInteractionKey(e);
    }
}
