using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace VanBaz;

public partial class MainWindow
{
    private GlobalHotkeys? hotkeys;
    private bool locked;
    private string? modeOverride;
    private bool modeLocked;
    private string InterfaceMode => modeOverride ?? catalog.Settings.InterfaceMode;
    private bool AllowTools()
    {
        if (locked) return false;
        if (operations.Count > 0) { MessageBox.Show("Дождитесь завершения текущей операции с базой."); return false; }
        if (InterfaceMode != "Пользователь") return true;
        MessageBox.Show("В режиме «Пользователь» доступны только запуск и просмотр. Сменить режим можно в меню «Режим работы»."); return false;
    }
    private void SelectBase(string id)
    {
        ShowFromTray(); SelectBaseInList(id);
    }
    private bool SelectBaseInList(string id)
    {
        var target = catalog.Bases.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (target == null) return false;
        if (!catalog.Settings.EnableLegacy77 && target.Version == "7.7") { StatusText.Text = "Включите поддержку 1С 7.7 в настройках → Окно и запуск."; return false; }
        if (target.Version == "7.7") LegacyTab.IsChecked = true; else BasesTab.IsChecked = true;
        SearchBox.Clear(); VersionFilter.SelectedIndex = 0; collapsed.Clear(); BuildRows(target.Id); BasesGrid.ScrollIntoView(BasesGrid.SelectedItem);
        return Selected?.Id == target.Id;
    }
    public void HandleArguments(string[] args)
    {
        if (locked) { ShowFromTray(); return; }
        ShowFromTray();
        string Value(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
        string id = Value("--launch-id");
        if (id.Length > 0)
        {
            if (!catalog.Bases.Any(b => b.Id == id)) { MessageBox.Show("База из ярлыка отсутствует в этом каталоге."); return; }
            if (SelectBaseInList(id)) Launch(args.Contains("--designer")); return;
        }
        string file = Value("--open-file");
        if (file.Length > 0)
        {
            try
            {
                file = Path.GetFullPath(file);
                if (!Path.GetFileName(file).Equals("1Cv8.1CD", StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) throw new IOException("Выберите существующий файл 1Cv8.1CD.");
                string action = args.Contains("--designer") ? "Конфигуратор" : args.Contains("--enterprise") ? "Предприятие" : args.Contains("--register") ? "Зарегистрировать" : "";
                string folder = Path.GetDirectoryName(file)!;
                var entry = catalog.Bases.FirstOrDefault(b => b.Kind == "File" && Path.GetFullPath(b.Location).TrimEnd('\\').Equals(folder, StringComparison.OrdinalIgnoreCase));
                if (action.Length == 0) { var choice = new ExplorerActionWindow(file, CanEdit, entry != null) { Owner = this }; if (choice.ShowDialog() != true) return; action = choice.SelectedAction; }
                if (entry != null) SelectBase(entry.Id);
                else
                {
                    if (!CanEdit) { MessageBox.Show(this, "Эта база ещё не зарегистрирована. Для добавления записи выберите режим Специалист или Разработчик.", "Запуск файловой базы"); return; }
                    var added = new BaseEntry { Name = Path.GetFileName(folder), Location = folder, Version = catalog.Settings.DefaultVersion }; Edit(added);
                    entry = catalog.Bases.FirstOrDefault(b => b.Id == added.Id);
                }
                if (entry != null && action != "Зарегистрировать" && SelectBaseInList(entry.Id)) Launch(action == "Конфигуратор");
            }
            catch (Exception ex) { Error("Регистрация базы", ex); }
        }
        if (StartupOptions.Parse(args, AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)).Minimized) HideToTray();
    }
    private void ApplyDesktopSettings()
    {
        hotkeys?.Dispose(); hotkeys = null;
        if (!catalog.Settings.HotkeysEnabled || isolatedSession || !IsLoaded) return;
        var p = catalog.Settings;
        var registered = new GlobalHotkeys(new WindowInteropHelper(this).Handle, HotkeyPressed);
        try { registered.Register(1, p.ShowHotkey); registered.Register(2, p.TrayHotkey); if (p.ScreenshotFolder.Length > 0) registered.Register(3, p.ScreenshotHotkey); hotkeys = registered; }
        catch (Exception ex) { registered.Dispose(); Error("Горячие клавиши не включены", ex); }
    }
    private void HotkeyPressed(int id)
    {
        if (locked) { ShowFromTray(); return; }
        if (id == 1) ShowFromTray();
        else if (id == 2)
        {
            ShowTrayPanel();
        }
        else Screenshot_Click(this, new RoutedEventArgs());
    }
    private void RefreshTrayMenu()
    {
        if (tray?.ContextMenuStrip is { Visible: false } menu) PopulateTrayMenu(menu);
    }
    private void PopulateTrayMenu(System.Windows.Forms.ContextMenuStrip menu)
    {
        foreach (var item in menu.Items.Cast<System.Windows.Forms.ToolStripItem>().ToArray()) { menu.Items.Remove(item); item.Dispose(); }
        System.Windows.Forms.ToolStripMenuItem Action(string caption, bool enabled, Action action)
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(caption) { Enabled = enabled, ToolTipText = enabled ? caption : "Действие недоступно в текущем режиме или во время операции." };
            item.Click += (_, _) => Dispatcher.Invoke(action); return item;
        }
        menu.Items.Add(Action("Открыть ВанБаз", true, ShowFromTray));
        var favorites = new System.Windows.Forms.ToolStripMenuItem("Избранные базы") { Enabled = Enabled("idle") };
        foreach (var entry in catalog.Bases.Where(b => b.Favorite && (catalog.Settings.EnableLegacy77 || b.Version != "7.7")).OrderBy(b => b.Name))
        {
            string id = entry.Id; var favorite = new System.Windows.Forms.ToolStripMenuItem(entry.Name.Replace("&", "&&"));
            favorite.DropDownItems.Add(Action("1С:Предприятие", Enabled("idle"), () => LaunchFromTray(id, false)));
            if (entry.Kind != "Web") favorite.DropDownItems.Add(Action("Конфигуратор", CanEdit, () => LaunchFromTray(id, true)));
            favorites.DropDownItems.Add(favorite);
        }
        if (favorites.DropDownItems.Count == 0) favorites.DropDownItems.Add(new System.Windows.Forms.ToolStripMenuItem("Список избранных пуст") { Enabled = false });
        menu.Items.Add(favorites); menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        void FromWindow(RoutedEventHandler handler) { ShowFromTray(); handler(this, new RoutedEventArgs()); }
        menu.Items.Add(Action("Временная блокировка…", Enabled("idle"), () => { if (Enabled("idle")) FromWindow(Lock_Click); }));
        menu.Items.Add(Action("Снимок экрана…", Enabled("idle"), () => { if (Enabled("idle")) Screenshot_Click(this, new RoutedEventArgs()); }));
        menu.Items.Add(Action("Каталог настроек", Enabled("idle"), () => { if (Enabled("idle")) DataFolder_Click(this, new RoutedEventArgs()); }));
        menu.Items.Add(Action("Настройки…", CanEdit, () => { if (CanEdit) FromWindow(Settings_Click); }));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Action("Выход", Enabled("idle"), () => { if (Enabled("idle")) { exitRequested = true; Close(); } }));
    }
    private void LaunchFromTray(string id, bool designer)
    {
        if (!Enabled("idle") || designer && !CanEdit) return;
        ShowFromTray(); if (SelectBaseInList(id)) Launch(designer);
    }
    private void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (locked || isolatedSession) return;
        try
        {
            string folder = catalog.Settings.ScreenshotFolder;
            if (folder.Length == 0) { var dialog = new OpenFolderDialog { Title = "Папка снимков экрана" }; if (dialog.ShowDialog(this) != true) return; folder = dialog.FolderName; }
            Directory.CreateDirectory(folder);
            var bounds = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == catalog.Settings.ScreenshotMonitor)?.Bounds ?? System.Windows.Forms.SystemInformation.VirtualScreen;
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            string path = Path.Combine(folder, "VanBaz_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
            if (catalog.Settings.PromptScreenshot) { var save = new SaveFileDialog { Title = "Сохранить снимок экрана", InitialDirectory = folder, FileName = Path.GetFileName(path), Filter = "Изображение PNG|*.png" }; if (save.ShowDialog(this) != true) return; path = save.FileName; }
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            StatusText.Text = "Снимок экрана: " + path;
        }
        catch (Exception ex) { Error("Снимок экрана", ex); }
    }
    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || Selected is not { } entry || isolatedSession) return;
        var f = new FormDialog("Ярлык базы", 640, 380) { Owner = this }; f.Section("Запуск"); f.Choice("mode", "Режим", "Предприятие", new[] { "Предприятие", "Конфигуратор" });
        if (f.ShowDialog() != true) return;
        var dialog = new SaveFileDialog { Title = "Сохранить ярлык", Filter = "Ярлык Windows|*.lnk", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FileName = string.Concat(entry.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".lnk" };
        if (dialog.ShowDialog(this) != true) return;
        try { DesktopIntegration.Shortcut(dialog.FileName, store.Folder, entry.Id, f.Value("mode") == "Конфигуратор"); StatusText.Text = "Ярлык сохранён: " + dialog.FileName; }
        catch (Exception ex) { Error("Создание ярлыка", ex); }
    }
    private void Processes_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || isolatedSession) return;
        var w = new Window { Owner = this, Title = "Процессы 1С · текущий сеанс Windows", Width = 820, Height = 450, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var root = new DockPanel { Margin = new(12) }; w.Content = root; var list = new ListBox();
        var buttons = new WrapPanel(); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons); root.Children.Add(list);
        void Refresh() => list.ItemsSource = ClientProcesses.List();
        foreach (string label in new[] { "Обновить", "Закрыть окно 1С", "Завершить принудительно" })
        {
            var b = new Button { Content = label }; buttons.Children.Add(b);
            if (label != "Обновить") DialogBehavior.Selection(list, b);
            b.Click += (_, _) =>
            {
                if (label == "Обновить") { Refresh(); return; }
                if (list.SelectedItem is not ClientProcess item) return;
                bool force = label == "Завершить принудительно";
                if (operations.Count > 0) { MessageBox.Show(w, "Дождитесь завершения операций ВанБаз."); return; }
                if (MessageBox.Show(w, label + "?\n" + item + (force ? "\nНесохранённые изменения будут потеряны." : ""), "Процесс 1С", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                try { ClientProcesses.Close(item, force); Refresh(); } catch (Exception ex) { Error("Процесс 1С", ex); }
            };
        }
        DialogBehavior.CloseOnEscape(w); Refresh(); w.ShowDialog();
    }
    private async void CheckFile_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || isolatedSession || Selected is not { Kind: "File" } entry || entry.Version == "7.7") return;
        try
        {
            var platform = platforms.Where(p => p.Architecture == "x86").OrderByDescending(p => p.Version).Select(p => Path.Combine(p.Directory, "chdbfl.exe")).FirstOrDefault(File.Exists);
            if (platform == null) throw new FileNotFoundException("chdbfl.exe не найден в установленных 32-разрядных платформах.");
            if (MessageBox.Show("Будет открыта утилита chdbfl.\nВ ней выберите:\n" + Path.Combine(entry.Location, "1Cv8.1CD") + "\n\nЗакройте все сеансы базы. Сначала выполните проверку без исправления ошибок. Перед исправлением нужна резервная копия.", "Проверка файловой базы", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            await CopyTextAsync(Path.Combine(entry.Location, "1Cv8.1CD"), "Путь файла базы скопирован"); Process.Start(new ProcessStartInfo(platform) { UseShellExecute = true });
        }
        catch (Exception ex) { Error("Проверка файловой базы", ex); }
    }
    private async void CheckBase_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || isolatedSession || Selected is not { } entry || entry.Kind == "Web") return;
        if (operations.Count > 0) { MessageBox.Show("Дождитесь завершения текущих операций."); return; }
        var f = new FormDialog("Тестирование базы · " + entry.Name, 710, 440) { Owner = this };
        f.Section("Проверка");
        f.Check("references", "Проверять также ссылочную целостность", true);
        f.Check("repair", "Исправлять ошибки (изменяет базу)", false);
        f.Note("По умолчанию выполняется только тестирование. Закройте сеансы базы. Перед исправлением создайте резервную копию. Объекты по неверным ссылкам автоматически не создаются и не удаляются.");
        f.SetAcceptText("Начать");
        if (f.ShowDialog() != true) return;
        bool repair = f.Flag("repair");
        if (MessageBox.Show("База: " + entry.Name + "\n" + entry.Location + "\n\n" + (repair ? "Будет выполнено тестирование с исправлением. Подтвердите наличие актуальной резервной копии." : "Будет выполнено тестирование без исправления.") + "\nПродолжить?", "Тестирование базы", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        operations.Add(entry.Id);
        try
        {
            string folder = Path.Combine(store.Folder, "Logs"); Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "check-" + Guid.NewGuid().ToString("N") + ".log");
            using var process = LaunchService.Start(LaunchService.BuildCheck(entry, repair, f.Flag("references"), log, platforms));
            StatusText.Text = "Тестирование базы: " + entry.Name; await process.WaitForExitAsync();
            FormDialog.ShowText(this, "Результат тестирования", "Код завершения платформы: " + process.ExitCode + ". Подробный результат — в журнале.\n" + log + "\n\n" + (File.Exists(log) ? V8iImporter.ReadText(log) : "Платформа не создала журнал."));
        }
        catch (Exception ex) { Error("Тестирование базы", ex); }
        finally { operations.Remove(entry.Id); }
    }
    private void RemoveMissing_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        var missing = catalog.Bases.Where(b =>
        {
            if (b.Version == "7.7" || b.Kind != "File" || b.DisallowDelete || b.Location.StartsWith(@"\\") || !Path.IsPathFullyQualified(b.Location)) return false;
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(b.Location)!); if (!drive.IsReady || drive.DriveType != DriveType.Fixed) return false;
                // Directory enumeration distinguishes a missing file from access denied.
                return !Directory.EnumerateFiles(b.Location, "1Cv8.1CD", SearchOption.TopDirectoryOnly).Any();
            }
            catch (DirectoryNotFoundException) { return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
        }).ToList();
        if (missing.Count == 0) { MessageBox.Show("Отсутствующих локальных баз не найдено. Сетевые и недоступные папки пропущены."); return; }
        if (MessageBox.Show("Удалить из списка записи без локального 1Cv8.1CD?\n\n" + string.Join("\n", missing.Select(b => b.Name + " · " + b.Location)) + "\n\nФайлы и папки баз не меняются.", "Отсутствующие базы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { if (File.Exists(store.FilePath)) store.Snapshot(); Commit(() => catalog.Bases.RemoveAll(b => missing.Contains(b))); } catch (Exception ex) { Error("Удаление записей", ex); }
    }
    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (locked || modeLocked || operations.Count > 0) return;
        var f = new FormDialog("Режим работы", 650, 400) { Owner = this }; f.Section("Интерфейс"); f.Choice("mode", "Режим", InterfaceMode, new[] { "Пользователь", "Специалист", "Разработчик" });
        f.Note("Пользователь: запуск и просмотр. Специалист: редактирование и обслуживание. Разработчик: полный набор действий.\nЭто настройка интерфейса, а не ограничение прав Windows.");
        if (f.ShowDialog() == true && Commit(() => { catalog.Settings.InterfaceMode = f.Value("mode"); modeOverride = null; })) ApplyPreferences();
    }
    private void GroupColor_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools() || SelectedGroupPath.Length == 0) return;
        string path = SelectedGroupPath; var group = catalog.Groups.FirstOrDefault(g => g.Path == path);
        var f = new FormDialog("Цвет группы", 600, 380) { Owner = this }; f.Section("Оформление");
        f.ColorField("color", "Цвет", group?.Color ?? "#287EB0");
        f.Validate = () => { System.Windows.Media.ColorConverter.ConvertFromString(f.Value("color")); return true; };
        if (f.ShowDialog() == true) Commit(() => { if (group == null) catalog.Groups.Add(new BaseGroup { Path = path, Color = f.Value("color") }); else group.Color = f.Value("color"); });
    }
}
