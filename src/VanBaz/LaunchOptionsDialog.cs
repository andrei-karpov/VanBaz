using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace VanBaz;

public sealed class LaunchOptionsDialog : FormDialog
{
    private readonly LaunchProfile original;
    private readonly BaseEntry entry;
    private readonly List<PlatformInfo> platforms;
    public bool SaveProfile => Flag("save");
    public LaunchOptionsDialog(BaseEntry entry, LaunchProfile profile, IEnumerable<PlatformInfo> platforms, bool editing = false, ParameterExamples? examples = null) : base("Профиль запуска · " + entry.Name, 850, 640)
    {
        this.entry = entry; original = profile; this.platforms = platforms.ToList();
        Section("Запуск");
        Text("name", "Имя профиля", profile.Name); Check("save", editing ? "Сохранить изменения профиля" : "Сохранить как именованный профиль", editing);
        if (editing) SetEnabled("save", false);
        Check("bound", "Только для выбранной базы", profile.BaseId.Length > 0 || !editing);
        Choice("client", "Клиент", profile.Client, new[] { "Авто", "Обычный режим", "Толстый клиент", "Толстый управляемый", "Тонкий клиент" }).IsEnabled = !profile.Designer && entry.Version != "7.7" && entry.Kind != "Web";
        Choice("version", "Версия платформы", profile.Version, this.platforms.Select(p => p.Version).Prepend(profile.Version).Distinct(), true);
        Choice("arch", "Разрядность", profile.Architecture, new[] { "Авто", "x86", "x64" });
        BrowseText("exe", "Закреплённый EXE", profile.Executable, false, "Клиент 1С|1cv8.exe;1cv8c.exe;1cv7*.exe");
        Note("При изменении версии или разрядности очистите закреплённый EXE либо выберите соответствующий файл. Профиль меняет параметры запуска, но не адрес базы.");
        Check("admin", "Запустить от администратора", profile.RunAsAdmin);
        Section("Вход");
        Check("ask", "Запросить вход средствами 1С", profile.AskLogin); Text("user", "Пользователь 1С", profile.UserName); Password("password", "Пароль", profile.UserSecret, false);
        Choice("wa", "Вход Windows", profile.WindowsAuthentication, new[] { "Авто", "Да", "Нет" });
        Section("Параметры 8.x");
        Text("lang", "Язык (/L)", profile.Advanced.Language); Text("parameter", "Параметр запуска (/C)", profile.Advanced.StartupParameter); Text("area", "Области данных (/Z)", profile.Advanced.DataArea);
        Password("permission", "Код разрешения (/UC)", profile.Advanced.PermissionSecret, false);
        Check("clear", "Очистить клиент-серверный кэш (/ClearCache)", profile.Advanced.ClearCache);
        Check("jobs", "Запрет заданий файловой базы", profile.Advanced.DisableScheduledJobs); Check("taxi", "Интерфейс Такси", profile.Advanced.Taxi);
        Note("Запрет заданий доступен только файловой базе. Недоступные для выбранного семейства или режима флажки снимаются. Перед запуском ограничения проверяются повторно по выбранному EXE.");
        foreach (var key in new[] { "lang", "parameter", "area", "permission", "clear", "jobs", "taxi" }) SetEnabled(key, entry.Version != "7.7");
        if (profile.Designer) foreach (var key in new[] { "area", "clear", "jobs", "taxi" }) SetEnabled(key, false);
        if (entry.Kind != "File") SetEnabled("jobs", false);
        Section("Обработка и аргументы");
        BrowseText("epf", "Обработка EPF / ERF", profile.ExternalProcessor, false, "Обработки|*.epf;*.erf"); SetEnabled("epf", !profile.Designer && entry.Version != "7.7");
        var arguments = Text("args", "Дополнительные ключи", profile.ExtraArguments, true);
        (examples ?? ParameterExamples.Empty).Attach(this, arguments);
        ActionButton("Поддерживаемые ключи…", () => ShowText(this, "Синтаксис параметров", LaunchArguments.Help));
        Section("Хранилище"); Check("repo", "Использовать хранилище", profile.UseRepository); Text("repoPath", "Путь хранилища", profile.Repository);
        Text("repoUser", "Пользователь", profile.RepositoryUser); Password("repoPassword", "Пароль хранилища", profile.RepositorySecret, false);
        foreach (var key in new[] { "repo", "repoPath", "repoUser", "repoPassword" }) SetEnabled(key, profile.Designer && entry.Version != "7.7");
        Section("Проверка"); Note("Пароли и код разрешения из полей формы хранятся через защиту текущего пользователя Windows. Секреты скрыты в предпросмотре. Основная запись базы не изменяется.");
        ActionButton("Ограничения режимов и версий…", () => ShowText(this, "Совместимость запуска", LaunchCapabilities.Help));
        ActionButton("Показать команду запуска…", () => { var p = Read(); ShowText(this, "Команда запуска", LaunchService.Preview(LaunchService.Build(p.Apply(entry), p.Designer, p.Client, this.platforms, requireInstalled: false)) + "\n\nПредварительная команда по выбранным параметрам и последнему поиску платформ. UnresolvedPlatform означает, что подходящий клиент пока не найден. Перед запуском EXE проверяется повторно."); });
        Validate = () =>
        {
            var p = Read(); if (SaveProfile && p.Name.Length == 0) return SetError("Введите имя сохраняемого профиля.");
            if (SaveProfile && LaunchArguments.Parse(p.ExtraArguments).Any(t => t.Secret)) return SetError("Не сохраняйте пароль или код в строке ключей. Используйте защищённые поля формы.");
            if (p.Version.Length == 0) return SetError("Укажите версию платформы.");
            if (p.Executable == original.Executable && p.Executable.Length > 0 && (p.Version != original.Version || p.Architecture != original.Architecture)) return SetError("Версия или разрядность изменена. Очистите закреплённый EXE для автоматического выбора.");
            LaunchService.Build(p.Apply(entry), p.Designer, p.Client, this.platforms, requireInstalled: false); return true;
        };
        SetAcceptText(editing ? "Сохранить" : "Запустить");
        void UpdateAvailability(string? selectedVersion = null)
        {
            bool modern = Version.TryParse((selectedVersion ?? Value("version")).TrimEnd('*', '.'), out var version) && version >= new Version(8, 3);
            void Toggle(string key, bool enabled) { SetEnabled(key, enabled); if (!enabled) ((CheckBox)Field(key)).IsChecked = false; }
            Toggle("jobs", modern && !profile.Designer && entry.Kind == "File");
            Toggle("taxi", modern && !profile.Designer && Value("client") != "Обычный режим");
        }
        Field("version").AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateAvailability()));
        // Editable ComboBox.Text still contains the previous value during SelectionChanged.
        ((ComboBox)Field("version")).SelectionChanged += (_, _) => UpdateAvailability(((ComboBox)Field("version")).SelectedItem as string);
        ((ComboBox)Field("client")).SelectionChanged += (_, _) => UpdateAvailability();
        UpdateAvailability();
        void LoginState() { foreach (var key in new[] { "user", "password", "wa" }) SetEnabled(key, !Flag("ask")); }
        WatchCheck("ask", LoginState);
        void RepositoryState() { foreach (var key in new[] { "repoPath", "repoUser", "repoPassword" }) SetEnabled(key, profile.Designer && entry.Version != "7.7" && Flag("repo")); }
        WatchCheck("repo", RepositoryState);
    }
    public LaunchProfile Read() => new()
    {
        Id = original.Id, Name = Value("name"), BaseId = Flag("bound") ? entry.Id : "", Designer = original.Designer,
        Version = Value("version"), Executable = Value("exe"), Architecture = Value("arch"), Client = original.Designer || entry.Version == "7.7" || entry.Kind == "Web" ? original.Client : Value("client"),
        AskLogin = Flag("ask"), UserName = Value("user"), UserSecret = Secret("password", original.UserSecret), WindowsAuthentication = Value("wa"), RunAsAdmin = Flag("admin"),
        ExtraArguments = Value("args"), ExternalProcessor = Value("epf"), UseRepository = Flag("repo"), Repository = Value("repoPath"), RepositoryUser = Value("repoUser"), RepositorySecret = Secret("repoPassword", original.RepositorySecret),
        Advanced = new() { Language = Value("lang"), StartupParameter = Value("parameter"), DataArea = Value("area"), PermissionSecret = Secret("permission", original.Advanced.PermissionSecret), ClearCache = Flag("clear"), DisableScheduledJobs = Flag("jobs"), Taxi = Flag("taxi") }
    };
}
