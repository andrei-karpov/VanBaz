using System.IO;
using System.Windows;

namespace VanBaz;

public partial class MainWindow
{
    private async void CreateBase_Click(object sender, RoutedEventArgs e)
    {
        if (!CanEdit || !IsBaseView) return;
        var placement = new NewBaseSelectionWindow("File", catalog.Settings.DefaultVersion, false, creating: true) { Owner = this };
        if (placement.ShowDialog() != true) return;
        var form = CreateBaseDialog(); form.SetChoice("kind", placement.SelectedKind == "Server" ? "На сервере 1С" : "На компьютере"); form.PreserveChoice("version", placement.SelectedVersion);
        if (form.ShowDialog() != true || isolatedSession) return;
        var entry = ReadCreationEntry(form); string operation = "create:" + entry.Id; operations.Add(operation);
        try
        {
            string logs = Path.Combine(store.Folder, "Logs"); Directory.CreateDirectory(logs);
            string stem = Path.Combine(logs, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-create");
            var info = BaseCreation.Build(entry, form.Value("template"), form.Value("locale"), stem + ".log", stem + ".result", platforms, ReadServerCreation(form));
            StatusText.Text = "Создание базы: " + entry.Name;
            using var process = LaunchService.Start(info); await process.WaitForExitAsync();
            BaseCreation.RedactLog(stem + ".log", info.ArgumentList[1]);
            if (!BaseCreation.Succeeded(entry, process.ExitCode, stem + ".result")) throw new IOException("Платформа не подтвердила создание базы. Журнал: " + stem + ".log. Файлы и серверная база не удалялись.");
            if (Commit(() => catalog.Bases.Add(entry))) { SelectBase(entry.Id); StatusText.Text = "База создана и зарегистрирована: " + entry.Name; }
        }
        catch (Exception ex) { Error("Создание информационной базы", ex); }
        finally { operations.Remove(operation); }
    }
    private FormDialog CreateBaseDialog()
    {
        var form = new FormDialog("Создание новой информационной базы", 780, 630) { Owner = this }; form.Section("Новая база");
        form.Text("name", "Наименование", "Новая база");
        var kind = form.Choice("kind", "Размещение", "На компьютере", new[] { "На компьютере", "На сервере 1С" });
        form.BrowseText("folder", "Новый пустой каталог", catalog.Settings.DefaultFolder, true);
        form.Text("server", "Кластер серверов", ""); form.Text("reference", "Имя базы в кластере", "");
        form.Choice("group", "Группа", SelectedGroupPath, new[] { "" }.Concat(catalog.Groups.Select(g => g.Path)), true);
        form.Choice("version", "Платформа", catalog.Settings.DefaultVersion, platforms.Where(p => p.ThickClient).Select(p => p.Version).Distinct(), true);
        form.Choice("arch", "Разрядность", "Авто", new[] { "Авто", "x64", "x86" });
        form.BrowseText("template", "Шаблон конфигурации", "", false, "Шаблон конфигурации|*.cf");
        form.ActionButton("Каталог установленных шаблонов…", () =>
        {
            string requested = form.Value("version").TrimEnd('.', '*');
            string selected = platforms.FirstOrDefault(p => p.ThickClient && (form.Value("arch") == "Авто" || form.Value("arch") == p.Architecture) && (p.Version == requested || p.Version.StartsWith(requested + ".", StringComparison.Ordinal)))?.Version ?? requested;
            var picker = new ConfigurationTemplateWindow(catalog.Settings.TemplateRoots.Prepend(ConfigurationTemplates.DefaultRoot).Distinct(StringComparer.OrdinalIgnoreCase), selected) { Owner = form };
            if (picker.ShowDialog() == true && picker.SelectedTemplate is { } template) { form.SetText("template", template.Source); form.SetText("name", template.Name); }
        });
        form.Text("locale", "Локализация", "ru_RU");
        form.Note("Без шаблона создаётся пустая база. Допускается CF. Запись появится в списке после подтверждённого успешного создания платформой 1С. Веб-публикация регистрируется как существующая база.");
        form.Section("Сервер и СУБД");
        form.Choice("dbms", "СУБД", "PostgreSQL", new[] { "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase" });
        form.Text("dbserver", "Сервер СУБД", ""); form.Text("database", "Имя базы СУБД", ""); form.Text("dbuser", "Пользователь СУБД", "");
        form.Password("dbpassword", "Пароль СУБД на этот раз", "", false); form.Text("clusteruser", "Администратор кластера", ""); form.Password("clusterpassword", "Пароль кластера на этот раз", "", false);
        form.Check("createdb", "Создать базу в СУБД, если отсутствует", true); form.Check("disablejobs", "Запретить регламентные задания в новой базе", true);
        form.Text("connection", "Своя строка создания", "", true);
        form.Note("Необязательная строка заменяет параметры СУБД выше; Srvr и Ref должны совпадать с размещением. Пароли и строка создания не сохраняются в каталоге ВанБаз.");
        void State()
        {
            bool server = form.Value("kind") == "На сервере 1С";
            form.SetVisible("folder", !server); form.SetVisible("server", server); form.SetVisible("reference", server);
            foreach (string key in new[] { "dbms", "dbserver", "database", "dbuser", "dbpassword", "clusteruser", "clusterpassword", "createdb", "disablejobs", "connection" }) form.SetEnabled(key, server);
        }
        kind.SelectionChanged += (_, _) => State(); State(); form.SetAcceptText("Создать");
        form.Section("Проверка команды");
        form.ActionButton("Показать команду создания", () => FormDialog.ShowText(form, "Команда создания", LaunchService.Preview(BaseCreation.Build(ReadCreationEntry(form), form.Value("template"), form.Value("locale"), "create.log", "create.result", platforms, ReadServerCreation(form)))));
        form.Validate = () =>
        {
            if (form.Value("name").Length == 0 || form.Value("name").IndexOfAny(new[] { '\r', '\n', ']' }) >= 0) return form.SetError("Укажите корректное наименование.");
            BaseCreation.Build(ReadCreationEntry(form), form.Value("template"), form.Value("locale"), "preview.log", "preview.result", platforms, ReadServerCreation(form)); return true;
        };
        return form;
    }
    private BaseEntry ReadCreationEntry(FormDialog form) => new()
    {
        Name = form.Value("name"), Kind = form.Value("kind") == "На сервере 1С" ? "Server" : "File",
        Location = form.Value("kind") == "На сервере 1С" ? form.Value("server").TrimEnd('/') + "/" + form.Value("reference") : form.Value("folder"),
        Group = form.Value("group").Replace('\\', '/').Trim('/'), Version = form.Value("version"), Architecture = form.Value("arch"), DoubleClickAction = catalog.Settings.DefaultDoubleClickAction
    };
    private static ServerCreationOptions ReadServerCreation(FormDialog f) => new(f.Value("dbms"), f.Value("dbserver"), f.Value("database"), f.Value("dbuser"), SecretStore.Reveal(f.Secret("dbpassword", "")), f.Value("clusteruser"), SecretStore.Reveal(f.Secret("clusterpassword", "")), f.Flag("createdb"), f.Flag("disablejobs"), f.Value("connection"));
}
