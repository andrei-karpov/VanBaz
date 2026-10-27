using System.IO;
using System.Text.RegularExpressions;

namespace VanBaz;

public class BaseEditor : FormDialog
{
    public void UseCommonSource()
    {
        foreach (string key in new[] { "name", "kind", "location", "server", "reference", "url", "version", "client", "wa" }) SetEnabled(key, false);
        Field("name").ToolTip = "Имя и подключение заданы общим списком. Личная группа, избранное и параметры ВанБаз доступны для изменения.";
    }
    public BaseEntry Result { get; private set; }
    private readonly List<ConfigurationSource> configurations;
    private readonly bool enableLegacy77;
    public BaseEditor(BaseEntry entry, List<PlatformInfo> platforms, List<string> groups, List<ConfigurationSource>? configurations = null, bool allowLock = false, bool enableLegacy77 = false, ParameterExamples? examples = null) : base("Свойства информационной базы", 690, 540)
    {
        this.configurations = configurations ?? new();
        this.enableLegacy77 = enableLegacy77;
        Result = entry;
        TopText("name", "Наименование", entry.Name);
        Section("1. Сведения");
        Text("description", "Комментарий", entry.Description);
        Text("notes", "Примечание", entry.Notes, true);
        Text("configversion", "Релиз конфигурации", entry.ConfigurationVersion);
        Text("configname", "Имя конфигурации", entry.ConfigurationName);
        string Label(ConfigurationSource c) => c.Name + " [" + c.Id + "]";
        Choice("configsource", "Источник релизов", this.configurations.FirstOrDefault(c => c.Id == entry.ConfigurationId) is { } selected ? Label(selected) : "", new[] { "" }.Concat(this.configurations.Select(Label)));
        Text("updateurl", "Свой UpdInfo.txt", entry.UpdateInfoUrl);
        Choice("group", "Группа", entry.Group, groups, true);
        Check("favorite", "В избранном", entry.Favorite);
        Heading("Используемая платформа");
        Choice("version", "Версия платформы", entry.Version, new[] { "8.3", "8.4", "8.5", "8.2", "8.1", "7.7" }.Concat(platforms.Select(p => p.Version)).Where(v => enableLegacy77 || v != "7.7").Distinct(), true);
        Choice("client", "Режим", entry.Client, new[] { "Авто", "Обычный режим", "Толстый клиент", "Толстый управляемый", "Тонкий клиент" });
        Choice("arch", "Разрядность", entry.Architecture, new[] { "Авто", "x64", "x86" });
        BrowseText("exe", "Исполняемый файл", entry.Executable, false, enableLegacy77 ? "Клиент 1С|1cv8.exe;1cv8c.exe;1cv7*.exe" : "Клиент 1С|1cv8.exe;1cv8c.exe");
        Note("Пустой путь — подбор установленной платформы по версии и разрядности.");
        Heading("Параметры запуска");
        var arguments = Text("args", "Доп. параметры", entry.ExtraArguments);
        (examples ?? ParameterExamples.Empty).Attach(this, arguments);
        BrowseText("epf", "Внешняя обработка", entry.ExternalProcessor, false, "Обработки и отчёты 1С|*.epf;*.erf|Все файлы|*.*");
        Section("2. Подключение");
        Choice("kind", "Тип базы", entry.Kind switch { "Server" => "На сервере 1С", "Web" => "Веб-публикация", _ => "В каталоге" }, new[] { "В каталоге", "На сервере 1С", "Веб-публикация" });
        BrowseText("location", "Каталог базы", entry.Kind == "File" ? entry.Location : "", true);
        var serverParts = entry.Location.Replace('\\', '/').Split('/', 2);
        Text("server", "Кластер серверов", entry.Kind == "Server" ? serverParts[0] : "");
        Text("reference", "Имя базы на сервере", entry.Kind == "Server" && serverParts.Length == 2 ? serverParts[1] : "");
        Text("url", "Адрес публикации", entry.Kind == "Web" ? entry.Location : "");
        void ConnectionState()
        {
            SetVisible("location", Value("kind") == "В каталоге");
            SetVisible("server", Value("kind") == "На сервере 1С"); SetVisible("reference", Value("kind") == "На сервере 1С");
            SetVisible("url", Value("kind") == "Веб-публикация");
        }
        ((System.Windows.Controls.ComboBox)Field("kind")).SelectionChanged += (_, _) => ConnectionState(); ConnectionState();
        Section("3. Аутентификация");
        Choice("wa", "Вход Windows", entry.WindowsAuthentication, new[] { "Авто", "Да", "Нет" });
        Heading("1С:Предприятие");
        Check("entlogin", "Использовать указанного пользователя", entry.EnterpriseLogin);
        Text("entuser", "Пользователь", entry.UserName);
        Password("entpass", "Пароль", entry.EnterpriseSecret, entry.EnterprisePasswordMissing);
        Heading("Конфигуратор");
        Check("cfglogin", "Использовать указанного пользователя", entry.DesignerLogin);
        Text("cfguser", "Пользователь", entry.DesignerUser);
        Password("cfgpass", "Пароль", entry.DesignerSecret, entry.DesignerPasswordMissing);
        Heading("Хранилище конфигурации");
        Check("repositoryuse", "Подключать хранилище в Конфигураторе", entry.UseRepository);
        Text("repository", "Адрес хранилища", entry.Repository);
        Text("repouser", "Пользователь", entry.RepositoryUser);
        Password("repopass", "Пароль", entry.RepositorySecret, entry.RepositoryPasswordMissing);
        Section("4. Прочие свойства");
        Choice("doubleclick", "Двойной щелчок", entry.DoubleClickAction, new[] { "Предприятие", "Предприятие с аутентификацией", "Предприятие с параметрами", "Конфигуратор", "Свойства" });
        Check("admin", "Запускать 1С от имени администратора", entry.RunAsAdmin);
        Note("Последний запуск: " + (entry.LastLaunch?.ToString("dd.MM.yyyy HH:mm") ?? "нет данных"));
        Note("Идентификатор: " + entry.Id);
        Note(entry.ListSources.Count == 0 ? "Источник списка не зафиксирован. Он появится после импорта или согласования." : "Источники регистрации:\n" + string.Join("\n", entry.ListSources));
        Check("nodelete", "Запретить удаление записи из списка", entry.DisallowDelete);
        Section("5. Блокировка");
        Element(new SessionLockPanel(entry.Copy(), allowLock));
        void AuthenticationState()
        {
            foreach (var name in new[] { "entuser", "entpass" }) SetEnabled(name, Flag("entlogin"));
            foreach (var name in new[] { "cfguser", "cfgpass" }) SetEnabled(name, Flag("cfglogin"));
            foreach (var name in new[] { "repository", "repouser", "repopass" }) SetEnabled(name, Flag("repositoryuse"));
        }
        foreach (string name in new[] { "entlogin", "cfglogin", "repositoryuse" })
        { var check = (System.Windows.Controls.CheckBox)Field(name); check.Checked += (_, _) => AuthenticationState(); check.Unchecked += (_, _) => AuthenticationState(); }
        AuthenticationState();
        Validate = Save;
    }
    public void UseNewNamePolicy(Preferences preferences)
    {
        if (!preferences.NewBaseNameFromFolder && !preferences.NewBaseNameWithDate) return;
        var name = (System.Windows.Controls.TextBox)Field("name");
        var folder = (System.Windows.Controls.TextBox)Field("location");
        string generated = name.Text;
        void Update()
        {
            if (name.Text != generated || Value("kind") != "В каталоге") return;
            string basis = preferences.NewBaseNameFromFolder && folder.Text.Trim().Length > 0 ? Path.GetFileName(folder.Text.Trim().TrimEnd('\\', '/')) : "Новая база";
            if (basis.Length == 0) basis = "Новая база";
            generated = basis + (preferences.NewBaseNameWithDate ? " " + DateTime.Today.ToString("dd.MM.yyyy") : ""); name.Text = generated;
        }
        folder.TextChanged += (_, _) => Update(); Update();
    }
    private bool Save()
    {
        string name = Value("name"), location = Value("location"), version = Value("version").TrimEnd('*', '.'), exe = Value("exe").Trim('"');
        if (name.Length == 0 || name.IndexOfAny(new[] { '\r', '\n', ']' }) >= 0) return SetError("Укажите корректное наименование.");
        string kind = Value("kind") switch { "На сервере 1С" => "Server", "Веб-публикация" => "Web", _ => "File" };
        if (kind == "Server") location = Value("server") + "/" + Value("reference");
        if (kind == "Web") location = Value("url");
        if (location.Length == 0 || location.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) return SetError("Укажите адрес базы.");
        if (version == "7.7" && !enableLegacy77) return SetError("Включите поддержку 1С 7.7 в настройках программы → Окно и запуск.");
        if (version == "7.7" && (kind != "File" || exe.Length == 0)) return SetError("Для 7.7 укажите каталог базы и установленный 1cv7*.exe.");
        if (kind == "File" && !Path.IsPathFullyQualified(location)) return SetError("Нужен полный путь к каталогу базы.");
        if (kind == "Server") { location = location.Replace('\\', '/'); if (location.IndexOf('/') <= 0 || location.EndsWith('/')) return SetError("Формат подключения: сервер/имя_базы."); }
        if (kind == "Web" && (!Uri.TryCreate(location, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))) return SetError("Укажите адрес http:// или https://.");
        if (version.Length > 0 && version != "7.7" && !Regex.IsMatch(version, @"^8\.\d+(?:\.\d+){0,2}$")) return SetError("Укажите версию платформы, например 8.3 или 8.3.27.1719.");
        if (exe.Length > 0 && (!Path.IsPathFullyQualified(exe) || !(new[] { "1cv8.exe", "1cv8c.exe" }.Contains(Path.GetFileName(exe), StringComparer.OrdinalIgnoreCase) || version == "7.7" && Path.GetFileName(exe).StartsWith("1cv7", StringComparison.OrdinalIgnoreCase) && Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase)))) return SetError("Исполняемый файл: 1cv8.exe / 1cv8c.exe либо 1cv7*.exe для 7.7.");
        var args = LaunchService.SplitArguments(Value("args"));
        if (version != "7.7")
        {
            var parsed = LaunchArguments.Parse(Value("args"));
            if (parsed.Any(a => a.Secret || a.Key == "/N")) return SetError("Пользователь, пароль и код разрешения задаются в отдельных полях базы или профиля запуска, не в сохраняемой строке ключей.");
            var preview = new System.Diagnostics.ProcessStartInfo(); preview.ArgumentList.Add("ENTERPRISE"); LaunchArguments.Append(preview, Value("args"));
        }
        if (args.Any(a => new[] { "/F", "/S", "/WS", "/N", "/P", "/DumpIB", "/RestoreIB", "/DumpCfg", "/LoadCfg" }.Contains(a, StringComparer.OrdinalIgnoreCase))) return SetError("Подключение и вход задаются в своих разделах; операции резервирования в ВанБаз не поддерживаются.");
        Result.Name = name; Result.Kind = kind; Result.Location = location; Result.Version = version; Result.Executable = exe;
        Result.Group = Value("group").Trim('/', '\\').Replace('\\', '/'); Result.Description = Value("description"); Result.Notes = Value("notes"); Result.ConfigurationVersion = Value("configversion");
        if (Value("updateurl").Length > 0) ConfigurationService.Https(Value("updateurl"));
        Result.ConfigurationName = Value("configname"); Result.UpdateInfoUrl = Value("updateurl");
        Result.ConfigurationId = configurations.FirstOrDefault(c => c.Name + " [" + c.Id + "]" == Value("configsource"))?.Id ?? "";
        Result.Favorite = Flag("favorite"); Result.Client = Value("client"); Result.Architecture = Value("arch"); Result.ExtraArguments = Value("args"); Result.ExternalProcessor = Value("epf");
        Result.WindowsAuthentication = Value("wa"); Result.EnterpriseLogin = Flag("entlogin"); Result.DesignerLogin = Flag("cfglogin");
        Result.UserName = Value("entuser"); Result.DesignerUser = Value("cfguser");
        Result.EnterpriseSecret = Secret("entpass", Result.EnterpriseSecret); Result.DesignerSecret = Secret("cfgpass", Result.DesignerSecret);
        if (PasswordChanged("entpass")) Result.EnterprisePasswordMissing = false;
        if (PasswordChanged("cfgpass")) Result.DesignerPasswordMissing = false;
        Result.UseRepository = Flag("repositoryuse"); Result.Repository = Value("repository"); Result.RepositoryUser = Value("repouser"); Result.RepositorySecret = Secret("repopass", Result.RepositorySecret);
        if (PasswordChanged("repopass")) Result.RepositoryPasswordMissing = false;
        Result.DoubleClickAction = Value("doubleclick");
        Result.RunAsAdmin = Flag("admin"); Result.DisallowDelete = Flag("nodelete"); return true;
    }
}
