using System.Windows;
using System.Windows.Media;

namespace VanBaz;

public partial class MainWindow
{
    private System.Windows.Forms.NotifyIcon? tray;
    private bool exitRequested;
    private void InitializeTray()
    {
        tray = new System.Windows.Forms.NotifyIcon { Text = "ВанБаз", Icon = AppBrand.TrayIcon, Visible = false };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Opening += (_, _) => PopulateTrayMenu(menu);
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray); RefreshTrayMenu();
        tray.MouseClick += (_, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Dispatcher.Invoke(ShowFromTray); };
        tray.BalloonTipClosed += (_, _) => { if (!inTray && IsVisible && tray != null) tray.Visible = false; };
        StateChanged += (_, _) => { if (WindowState != WindowState.Minimized) lastWindowState = WindowState; if (WindowState == WindowState.Minimized && catalog.Settings.MinimizeToTray) HideToTray(); };
        Closing += (_, e) =>
        {
            if (Application.Current is App { IsFatalShutdown: true }) return;
            if (locked) { e.Cancel = true; return; }
            if (operations.Count > 0) { e.Cancel = true; MessageBox.Show("Дождитесь завершения операции с базой. Окно можно свернуть."); return; }
            if (!exitRequested && !isolatedSession && catalog.Settings.CloseToTray) { e.Cancel = true; HideToTray(); return; }
            try
            {
                // Do not invalidate the journal's exact before/after hashes with window preferences.
                if (HasPendingCatalogRecovery()) return;
                SaveWindowPlacement();
                catalog.Settings.SelectedId = Selected?.Id ?? ""; catalog.Settings.CollapsedGroups = collapsed.ToList(); store.Save(catalog);
            }
            catch (Exception ex) { e.Cancel = true; exitRequested = false; Error("Не удалось сохранить каталог", ex, applicationActions: false); }
        };
        Closed += (_, _) => { trayPanel?.Close(); CancelTrayAnimation(); DisposeDisplayTracking(); tray?.Dispose(); tray = null; };
    }
    private string ListEntryColor(BaseEntry entry, bool favorite, bool missing)
    {
        var p = catalog.Settings;
        if (!p.UseListColors) return ThemeService.TextColor;
        if (favorite && p.UseFavoriteColor) return p.FavoriteColor;
        if (missing) return p.MissingColor;
        string family = string.Join(".", entry.Version.Split('.').Take(2));
        return p.DisabledPlatformColors.Contains(family) ? ThemeService.TextColor : p.PlatformColors.GetValueOrDefault(family, ThemeService.TextColor);
    }
    private Window? settingsOwner;
    private Window SettingsOwner => settingsOwner ?? this;
    private void SettingsAction(FormDialog owner, RoutedEventHandler handler)
    { settingsOwner = owner; try { handler(owner, new RoutedEventArgs()); } finally { settingsOwner = null; } }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!AllowTools()) return;
        CreateSettingsDialog().ShowDialog();
    }
    private FormDialog CreateSettingsDialog()
    {
        var p = catalog.Settings;
        var form = new FormDialog("Настройки ВанБаз", 780, 620) { Owner = this };
        form.Section("Список баз");
        form.Heading("Шрифт и строки");
        form.Choice("font", "Шрифт списка", p.FontName, Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s).Prepend(p.FontName).Distinct(), true);
        form.Choice("size", "Размер шрифта (пт)", (p.FontSize * 72 / 96).ToString("0.##"), new[] { "8", "9", "10", "11", "12", "14", "16", "18", "20", "22", "24", "28" }, true); form.Text("height", "Высота строки", p.RowHeight.ToString());
        form.Check("headers", "Отображать заголовки колонок", p.ShowColumnHeaders); form.Check("tooltips", "Подсказки списка", p.ListTooltips);
        form.Check("alpha", "Сортировать по алфавиту", p.Alphabetical); form.Check("groups", "Группы в начале списка", p.GroupsFirst); form.Check("counts", "Показывать количество дочерних баз", p.ShowCounts);
        form.Check("rowlaunch", "Отображать кнопку запуска в строке списка", p.ShowLaunchButtonInRow);
        form.Check("platform", "Колонка «Платформа»", p.ShowPlatform); form.Check("date", "Колонка «Дата»", p.ShowDate); form.Check("bytes", "Колонка «Размер»", p.ShowSize);
        form.Check("cell", "Выделять текущую ячейку вместо всей строки", p.CellHighlight);
        form.Section("Цвета списка");
        form.Check("usecolors", "Использовать цветовое оформление списка", p.UseListColors);
        form.ColorToggle("favoriteenabled", "favorite", "Избранные базы", p.UseFavoriteColor, p.FavoriteColor);
        foreach (string version in new[] { "8.1", "8.2", "8.3", "8.4", "8.5" })
        {
            form.ColorToggle("usecolor" + version, "color" + version, "Платформа " + version, !p.DisabledPlatformColors.Contains(version), p.PlatformColors.GetValueOrDefault(version, "#141414"));
            form.WatchCheck("usecolor" + version, () => form.SetEnabled("color" + version, form.Flag("usecolors") && form.Flag("usecolor" + version)));
        }
        form.Heading("Отсутствующие базы");
        form.Check("exist", "Проверять существование локальных файловых баз", p.CheckExistence);
        form.ColorField("missing", "Цвет шрифта", p.MissingColor);
        form.Check("missingbold", "Полужирный", p.MissingBold); form.Check("missingitalic", "Курсив", p.MissingItalic); form.Check("missingstrike", "Зачёркнутый", p.MissingStrike);
        form.ActionButton("Темы и избранные стили…", () => ShowThemes(form));
        void ColorState()
        {
            form.SetEnabled("favoriteenabled", form.Flag("usecolors"));
            form.SetEnabled("favorite", form.Flag("usecolors") && form.Flag("favoriteenabled"));
            form.SetEnabled("missing", form.Flag("usecolors") && form.Flag("exist"));
            foreach (var key in new[] { "missingbold", "missingitalic", "missingstrike" }) form.SetEnabled(key, form.Flag("exist"));
            foreach (var v in new[] { "8.1", "8.2", "8.3", "8.4", "8.5" }) { form.SetEnabled("usecolor" + v, form.Flag("usecolors")); form.SetEnabled("color" + v, form.Flag("usecolors") && form.Flag("usecolor" + v)); }
        }
        foreach (var key in new[] { "usecolors", "favoriteenabled", "exist" }) form.WatchCheck(key, ColorState);
        form.Section("Окно и запуск");
        form.Check("legacy77", "Включить поддержку 1С:Предприятия 7.7", p.EnableLegacy77);
        form.Check("history", "Вкладка «История»", p.ShowHistory); form.Check("starter", "Кнопка штатного стартера 1С", p.ShowStarter);
        form.Check("position", "Запоминать положение окна", p.RestorePosition); form.Check("close", "При закрытии крестиком сворачивать в трей", p.CloseToTray); form.Check("minimize", "При сворачивании скрывать в трей", p.MinimizeToTray); form.Check("escape", "Escape — скрывать в трей", p.EscapeToTray); form.Choice("after", "После запуска базы", p.AfterLaunchAction.StartsWith("Закрыть ", StringComparison.Ordinal) ? "Закрыть ВанБаз" : p.AfterLaunchAction, new[] { "Свернуть в панель задач", "Свернуть в трей", "Закрыть ВанБаз", "Ничего не делать" });
        form.Check("startmin", "Свернуть ВанБаз после открытия", p.StartMinimized);
        form.Choice("startupmonitor", "Монитор при запуске", p.StartupMonitor, new[] { "Автоопределение" }.Concat(System.Windows.Forms.Screen.AllScreens.Select(s => s.DeviceName)));
        form.PreserveChoice("startupmonitor", p.StartupMonitor);
        form.Note("Сохранённое положение окна имеет приоритет над выбором монитора. Ctrl + колесо мыши меняет размер шрифта списка.");
        form.Note("Кнопка «Выход» полностью завершает ВанБаз.");
        form.Choice("doubleclick", "Двойной щелчок новых баз", p.DefaultDoubleClickAction, new[] { "Предприятие", "Предприятие с аутентификацией", "Предприятие с параметрами", "Конфигуратор", "Свойства" });
        form.Check("doubleall", "Применить выбранное действие ко всем существующим базам", false);
        form.Check("snapshot", "Автоматический снимок каталога при запуске", p.AutoSnapshot);
        form.Text("snapshotdays", "Интервал снимков, дней", p.AutoSnapshotDays.ToString()); form.WatchCheck("snapshot", () => form.SetEnabled("snapshotdays", form.Flag("snapshot")));
        form.Check("repeatlaunch", "Отслеживать повторный запуск базы", p.MonitorRepeatedLaunch); form.Check("launchballoon", "Всплывающее сообщение после запуска", p.ShowLaunchBalloon);
        form.Note("Повторный запуск проверяет процессы, ранее запущенные из ВанБаз: база, режим, PID и время создания процесса. Чужие процессы не определяются по похожему заголовку.");
        form.Section("Параметры запуска");
        form.Choice("connectionmode", "Способ подключения", p.ConnectionMode, LaunchBehavior.ConnectionModes); form.Note("Запуск по /IBName требует единственного совпадающего имени в штатном списке. При дополнительных параметрах соединения используйте Авто или /IBConnectionString.");
        form.ActionButton("Примеры параметров 1С…", () => CreateParameterExamples().CreateDialog(form).ShowDialog());
        form.Note("Примеры можно вставить в дополнительные параметры базы или профиля запуска. Изменение примера не меняет параметры уже настроенных баз.");
        form.ActionButton("Внешние команды…", () => CreateExternalCommandsDialog(form).ShowDialog());
        form.Section("Каталоги и платформа");
        form.ActionButton("Каталоги установленных платформ и браузер…", () => SettingsAction(form, PlatformRoots_Click));
        form.Check("namefolder", "Именовать новую базу по каталогу", p.NewBaseNameFromFolder); form.Check("namedate", "Добавлять дату к имени новой базы", p.NewBaseNameWithDate);
        form.Text("version", "Версия новых баз", p.DefaultVersion); form.BrowseText("folder", "Каталог новых баз", p.DefaultFolder, true);
        form.Text("templateroots", "Каталоги шаблонов CF", string.Join(Environment.NewLine, p.TemplateRoots), true); form.Note("Дополнительные каталоги шаблонов, по одному в строке. Стандартный каталог tmplts текущего пользователя читается автоматически.");
        form.Section("Источники списков");
        form.ActionButton("Штатные источники 1С и личные настройки…", () => SettingsAction(form, OfficialLists_Click));
        form.Note("Штатные источники обнаруживаются автоматически; окно показывает их состояние. Дополнительные списки сохраняются отдельно своей кнопкой «Сохранить».");
        form.ActionButton("Дополнительные списки баз по платформам…", () => SettingsAction(form, AlternativeLists_Click));
        form.Section("Горячие клавиши");
        form.ActionButton("Назначить клавиши отдельным базам…", () => SettingsAction(form, BaseKeys_Click));
        form.Check("hotkeys", "Включить глобальные горячие клавиши", p.HotkeysEnabled);
        form.Text("showkey", "Открыть окно", p.ShowHotkey); form.Text("traykey", "Меню избранного", p.TrayHotkey); form.Text("screenkey", "Снимок экрана", p.ScreenshotHotkey);
        form.BrowseText("screens", "Папка снимков", p.ScreenshotFolder, true);
        form.Note("Например Ctrl+Alt+S. Занятые сочетания будут показаны после сохранения. Снимок всего рабочего стола сохраняется только по нажатию команды или горячей клавиши. Для горячей клавиши снимка задайте папку.");
        form.Check("promptscreen", "Предлагать имя файла снимка", p.PromptScreenshot);
        form.Choice("screenmonitor", "Монитор для снимка", p.ScreenshotMonitor, new[] { "Все мониторы" }.Concat(System.Windows.Forms.Screen.AllScreens.Select(s => s.DeviceName)));
        form.PreserveChoice("screenmonitor", p.ScreenshotMonitor);
        form.WatchCheck("hotkeys", () => { foreach (var key in new[] { "showkey", "traykey", "screenkey" }) form.SetEnabled(key, form.Flag("hotkeys")); });
        form.ActionButton("Восстановить стандартные сочетания", () => { var defaults = new Preferences(); form.SetText("showkey", defaults.ShowHotkey); form.SetText("traykey", defaults.TrayHotkey); form.SetText("screenkey", defaults.ScreenshotHotkey); });
        form.Section("Интернет");
        var proxyMode = form.Choice("proxymode", "Подключение", p.ProxyMode == "Авто" ? p.ProxyUrl.Length > 0 ? "Указать прокси" : "Системные настройки" : p.ProxyMode, new[] { "Системные настройки", "Без прокси", "Указать прокси" });
        form.Text("proxy", "Прокси HTTP / HTTPS", p.ProxyUrl); form.Check("proxywin", "Использовать учётную запись Windows для этого прокси", p.ProxyWindowsCredentials); form.Text("proxyuser", "Пользователь прокси", p.ProxyUser); form.Password("proxypass", "Пароль прокси", p.ProxySecret, false);
        void ProxyState() { bool custom = form.Value("proxymode") == "Указать прокси"; foreach (var field in new[] { "proxy", "proxywin" }) form.SetEnabled(field, custom); foreach (var field in new[] { "proxyuser", "proxypass" }) form.SetEnabled(field, custom && !form.Flag("proxywin")); }
        proxyMode.SelectionChanged += (_, _) => ProxyState(); form.WatchCheck("proxywin", ProxyState);
        form.Note("Собственный пароль хранится с защитой Windows. Пароли из StartManager не переносятся.");
        form.Section("Интеграция Windows");
        form.Note("Действия ниже применяются сразу и только к текущей учётной записи Windows.");
        form.ActionButton("Включить автозапуск ВанБаз", () => { if (isolatedSession) throw new InvalidOperationException("Внешние действия отключены в проверочном сеансе."); DesktopIntegration.Autorun(true, store.Folder); form.SetError("Автозапуск включён."); });
        form.ActionButton("Отключить автозапуск ВанБаз", () => { if (isolatedSession) throw new InvalidOperationException("Внешние действия отключены в проверочном сеансе."); DesktopIntegration.Autorun(false, store.Folder); form.SetError("Автозапуск отключён."); });
        form.ActionButton("Добавить команды ВанБаз для .1CD", () => { if (isolatedSession) throw new InvalidOperationException("Внешние действия отключены в проверочном сеансе."); DesktopIntegration.Explorer(true, store.Folder); form.SetError("Добавлены регистрация, Предприятие и Конфигуратор."); });
        form.ActionButton("Убрать команды ВанБаз из Проводника", () => { if (isolatedSession) throw new InvalidOperationException("Внешние действия отключены в проверочном сеансе."); DesktopIntegration.Explorer(false, store.Folder); form.SetError("Команда Проводника удалена."); });
        form.Section("Стили");
        form.Note("Стили меняют палитру ВанБаз. Выбор, предпросмотр и отмена доступны в отдельном окне.");
        form.ActionButton("Выбрать стиль…", () => ShowThemes(form));
        form.Section("Обновление ВанБаз");
        form.Note("Проверка и загрузка обновления выполняются в отдельном окне. Установка требует отдельного подтверждения и перезапуска.");
        form.ActionButton("Проверить обновления ВанБаз…", () => { var window = CreateOnlineUpdateWindow(); window.Owner = form; window.ShowDialog(); });
        form.ActionButton("Сохранённые версии и восстановление…", () => { var window = CreateVersionHistoryWindow(); window.Owner = form; window.ShowDialog(); });
        form.Validate = () =>
        {
            if (!double.TryParse(form.Value("size"), out var font) || !double.IsFinite(font) || font < 8 || font > 28 || !double.TryParse(form.Value("height"), out var height) || !double.IsFinite(height) || height < 18 || height > 56) return form.SetError("Шрифт: 8–28 пт. Высота строки: 18–56.");
            try { ColorConverter.ConvertFromString(form.Value("favorite")); ColorConverter.ConvertFromString(form.Value("missing")); foreach (string version in new[] { "8.1", "8.2", "8.3", "8.4", "8.5" }) ColorConverter.ConvertFromString(form.Value("color" + version)); }
            catch { return form.SetError("Цвет: #RRGGBB, например #8A0D06."); }
            if (form.Value("proxymode") == "Указать прокси" && (!Uri.TryCreate(form.Value("proxy"), UriKind.Absolute, out var proxy) || proxy.Scheme is not ("http" or "https") || proxy.UserInfo.Length > 0)) return form.SetError("Нужен HTTP(S)-адрес прокси без пароля.");
            string requestedVersion = form.Value("version").TrimEnd('*', '.');
            if (!int.TryParse(form.Value("snapshotdays"), out int snapshotDays) || snapshotDays is < 1 or > 3650) return form.SetError("Интервал снимков: от1до3650дней.");
            foreach (string folder in form.Value("templateroots").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) if (!System.IO.Path.IsPathFullyQualified(folder)) return form.SetError("Для шаблонов укажите полные пути каталогов.");
            if (requestedVersion != "7.7" && !System.Text.RegularExpressions.Regex.IsMatch(requestedVersion, @"^8\.\d+(?:\.\d+){0,2}$")) return form.SetError("Укажите версию новых баз: например 8.3 или 8.3.27.1719.");
            if (!form.Flag("legacy77") && form.Value("version").TrimEnd('*', '.') == "7.7") return form.SetError("Для версии новых баз 7.7 включите её поддержку в разделе «Окно и запуск» или выберите версию 8.x.");
            if (form.Flag("hotkeys"))
            {
                var keys = new[] { form.Value("showkey"), form.Value("traykey"), form.Value("screenkey") }.Select(GlobalHotkeys.Parse).ToList();
                if (keys.Distinct().Count() != keys.Count) return form.SetError("Для действий нужны разные сочетания клавиш.");
                if (catalog.Settings.BaseKeys.Keys.Any(slot => keys.Contains(GlobalHotkeys.Parse("Ctrl+Alt+" + slot)))) return form.SetError("Сочетание занято запуском отдельной базы. Измените привязку базы или глобальную клавишу.");
            }
            return true;
        };
        form.ApplyChanges = () =>
        {
        p = catalog.Settings;
        bool saved = Commit(() =>
        {
            p.MissingBold = form.Flag("missingbold"); p.MissingItalic = form.Flag("missingitalic"); p.MissingStrike = form.Flag("missingstrike"); p.CellHighlight = form.Flag("cell");
            p.PromptScreenshot = form.Flag("promptscreen"); p.ScreenshotMonitor = form.Value("screenmonitor");
            p.FontName = form.Value("font"); p.FontSize = double.Parse(form.Value("size")) * 96 / 72; p.RowHeight = double.Parse(form.Value("height"));
            p.ShowColumnHeaders = form.Flag("headers"); p.ListTooltips = form.Flag("tooltips"); p.StartMinimized = form.Flag("startmin");
            p.StartupMonitor = form.Value("startupmonitor");
            p.NewBaseNameFromFolder = form.Flag("namefolder"); p.NewBaseNameWithDate = form.Flag("namedate");
            p.Alphabetical = form.Flag("alpha"); p.GroupsFirst = form.Flag("groups"); p.ShowCounts = form.Flag("counts"); p.ShowPlatform = form.Flag("platform"); p.ShowDate = form.Flag("date"); p.ShowSize = form.Flag("bytes"); p.CheckExistence = form.Flag("exist");
            p.ShowLaunchButtonInRow = form.Flag("rowlaunch");
            p.UseListColors = form.Flag("usecolors"); p.UseFavoriteColor = form.Flag("favoriteenabled"); p.DisabledPlatformColors = new[] { "8.1", "8.2", "8.3", "8.4", "8.5" }.Where(v => !form.Flag("usecolor" + v)).ToList();
            p.FavoriteColor = form.Value("favorite"); p.MissingColor = form.Value("missing"); p.ShowHistory = form.Flag("history"); p.ShowStarter = form.Flag("starter");
            p.RestorePosition = form.Flag("position"); p.CloseToTray = form.Flag("close"); p.MinimizeToTray = form.Flag("minimize"); p.EscapeToTray = form.Flag("escape"); p.AfterLaunchAction = form.Value("after"); p.MinimizeAfterLaunch = p.AfterLaunchAction == "Свернуть в панель задач";
            p.EnableLegacy77 = form.Flag("legacy77");
            p.DefaultDoubleClickAction = form.Value("doubleclick");
            if (form.Flag("doubleall")) foreach (var b in catalog.Bases) b.DoubleClickAction = p.DefaultDoubleClickAction;
            p.DefaultVersion = form.Value("version"); p.DefaultFolder = form.Value("folder");
            p.ProxyMode = form.Value("proxymode"); p.ProxyUser = form.Value("proxyuser"); p.ProxySecret = form.Secret("proxypass", p.ProxySecret);
            p.ProxyUrl = form.Value("proxy"); p.ProxyWindowsCredentials = form.Flag("proxywin");
            p.AutoSnapshot = form.Flag("snapshot");
            p.AutoSnapshotDays = int.Parse(form.Value("snapshotdays")); p.MonitorRepeatedLaunch = form.Flag("repeatlaunch"); p.ShowLaunchBalloon = form.Flag("launchballoon"); p.ConnectionMode = form.Value("connectionmode"); p.TemplateRoots = form.Value("templateroots").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            p.HotkeysEnabled = form.Flag("hotkeys"); p.ShowHotkey = form.Value("showkey"); p.TrayHotkey = form.Value("traykey"); p.ScreenshotHotkey = form.Value("screenkey"); p.ScreenshotFolder = form.Value("screens");
            foreach (string version in new[] { "8.1", "8.2", "8.3", "8.4", "8.5" }) p.PlatformColors[version] = form.Value("color" + version);
        });
        if (saved) { ApplyPreferences(); ApplyDesktopSettings(); form.SetChecked("doubleall", false); }
        return saved;
        };
        form.UseTreeNavigation(
            ("Общие", "Окно и запуск", null),
            ("Информационные базы", "Список баз", null),
            ("Версии платформы и каталоги", "Каталоги и платформа", "Информационные базы"),
            ("Общие списки", "Источники списков", "Информационные базы"),
            ("Параметры запуска", "Параметры запуска", null),
            ("Настройки платформы", "Каталоги и платформа", "Параметры запуска"),
            ("Оформление", "Цвета списка", null),
            ("Стили", "Стили", "Оформление"),
            ("Обновление", "Обновление ВанБаз", null),
            ("Сервис", "Горячие клавиши", null),
            ("Интеграция Windows", "Интеграция Windows", "Сервис"),
            ("PROXY", "Интернет", null));
        form.SelectSection(2);
        return form;
    }
}
