using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace VanBaz;

public static class LaunchService
{
    public static List<PlatformInfo> Discover() => PlatformCatalog.Scan().Platforms;

    public static ProcessStartInfo Build(BaseEntry entry, bool designer, string client, IEnumerable<PlatformInfo> platforms, bool includeExtras = true, bool requireInstalled = true)
    {
        if (string.IsNullOrWhiteSpace(entry.Location)) throw new InvalidOperationException("Укажите подключение в свойствах базы.");
        if (entry.Version == "7.7") return Legacy77Service.Build(entry, designer, includeExtras, requireInstalled);
        if (designer && entry.Kind == "Web") throw new InvalidOperationException("Конфигуратор не подключается через веб-публикацию. Укажите файловое или серверное подключение.");
        if (client == "Авто" && entry.Client != "Авто") client = entry.Client;
        if (!designer && entry.Kind == "Web") client = "Тонкий клиент";
        string executable = entry.Executable.Trim();
        string fileName = !designer && (client == "Тонкий клиент" || entry.Kind == "Web") ? "1cv8c.exe" : "1cv8.exe";
        if (executable.Length == 0)
        {
            string requested = entry.Version.Trim().TrimEnd('.', '*');
            var platform = platforms.FirstOrDefault(p => (entry.Architecture == "Авто" || p.Architecture == entry.Architecture) && (requested.Length == 0 || p.Version == requested || p.Version.StartsWith(requested + ".", StringComparison.Ordinal)) &&
                (requireInstalled ? File.Exists(Path.Combine(p.Directory, fileName)) : fileName == "1cv8c.exe" ? p.ThinClient : p.ThickClient));
            if (platform == null && requireInstalled) throw new InvalidOperationException("Не найдена платформа " + (requested.Length == 0 ? "1С" : requested) + " с " + fileName + ". Выберите установленную версию или исполняемый файл в свойствах базы.");
            executable = platform == null ? Path.Combine(Path.GetTempPath(), "VanBaz-UnresolvedPlatform", fileName) : Path.Combine(platform.Directory, fileName);
        }
        else
        {
            string chosen = Path.GetFileName(executable);
            if (!chosen.Equals("1cv8.exe", StringComparison.OrdinalIgnoreCase) && !chosen.Equals("1cv8c.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Выберите 1cv8.exe или 1cv8c.exe из каталога платформы.");
            if (designer && chosen.Equals("1cv8c.exe", StringComparison.OrdinalIgnoreCase))
                executable = Path.Combine(Path.GetDirectoryName(executable)!, "1cv8.exe");
            else if (!designer && (client == "Тонкий клиент" || entry.Kind == "Web"))
                executable = Path.Combine(Path.GetDirectoryName(executable)!, "1cv8c.exe");
            else if (!designer && client is "Толстый клиент" or "Толстый управляемый" or "Обычный режим")
                executable = Path.Combine(Path.GetDirectoryName(executable)!, "1cv8.exe");
        }
        if (requireInstalled && !File.Exists(executable)) throw new FileNotFoundException("Исполняемый файл платформы не найден: " + executable);
        var info = new ProcessStartInfo(executable) { UseShellExecute = entry.RunAsAdmin, WorkingDirectory = Path.GetDirectoryName(executable)! };
        if (entry.RunAsAdmin) info.Verb = "runas";
        info.ArgumentList.Add(designer ? "DESIGNER" : "ENTERPRISE");
        bool login = designer ? entry.DesignerLogin : entry.EnterpriseLogin;
        string user = designer ? entry.DesignerUser : entry.UserName;
        string secret = designer ? entry.DesignerSecret : entry.EnterpriseSecret;
        void Add(string key, string value) { info.ArgumentList.Add(key); info.ArgumentList.Add(value); }
        var connection = InfobaseConnection.For(entry);
        if (!login || user.Length > 0) { connection.Remove("Usr"); connection.Remove("Pwd"); }
        if (InfobaseConnection.HasAdditional(connection)) Add("/IBConnectionString", InfobaseConnection.Format(connection));
        else Add(entry.Kind switch { "File" => "/F", "Server" => "/S", "Web" => "/WS", _ => throw new InvalidOperationException("Неизвестный тип подключения.") }, entry.Kind == "Server" ? entry.Location.Replace('/', '\\') : entry.Location);
        if (login && user.Length > 0)
        {
            Add("/N", user);
            if (secret.Length > 0) Add("/P", SecretStore.Reveal(secret));
        }
        if (entry.WindowsAuthentication != "Авто") info.ArgumentList.Add(entry.WindowsAuthentication == "Да" ? "/WA+" : "/WA-");
        if (!designer && client == "Авто") info.ArgumentList.Add("/AppAutoCheckMode");
        if (!designer && client == "Обычный режим") info.ArgumentList.Add("/RunModeOrdinaryApplication");
        if (!designer && client == "Толстый управляемый") info.ArgumentList.Add("/RunModeManagedApplication");
        if (designer && entry.UseRepository && entry.Repository.Length > 0)
        {
            Add("/ConfigurationRepositoryF", entry.Repository);
            if (entry.RepositoryUser.Length > 0) Add("/ConfigurationRepositoryN", entry.RepositoryUser);
            if (entry.RepositorySecret.Length > 0) Add("/ConfigurationRepositoryP", SecretStore.Reveal(entry.RepositorySecret));
        }
        if (includeExtras)
        {
            if (!designer && entry.ExternalProcessor.Length > 0) Add("/Execute", entry.ExternalProcessor);
            var a = entry.Advanced;
            if (a.Language.Length > 0) Add("/L", a.Language);
            if (a.StartupParameter.Length > 0) Add("/C", a.StartupParameter);
            if (!designer && a.DataArea.Length > 0) Add("/Z", a.DataArea);
            if (a.PermissionSecret.Length > 0) Add("/UC", SecretStore.Reveal(a.PermissionSecret));
            if (!designer && a.ClearCache) info.ArgumentList.Add("/ClearCache");
            if (!designer && a.DisableScheduledJobs) { info.ArgumentList.Add("/AllowExecuteScheduledJobs"); info.ArgumentList.Add("-Off"); }
            if (!designer && a.Taxi) info.ArgumentList.Add("/iTaxi");
            if (entry.Architecture != "Авто" && LaunchArguments.Parse(entry.ExtraArguments).Any(a => a.Key == "/AppArch")) throw new ArgumentException("Разрядность уже задана в свойствах. Уберите /AppArch из дополнительных ключей.");
            LaunchArguments.Append(info, entry.ExtraArguments);
            LaunchCapabilities.Validate(entry, designer, client, executable, info.ArgumentList, verifyExecutable: requireInstalled);
        }
        return info;
    }

    public static Process Start(ProcessStartInfo info) => Process.Start(info) ?? throw new InvalidOperationException("Windows не создала процесс 1С.");
    public static ProcessStartInfo BuildCheck(BaseEntry entry, bool repair, bool references, string log, IEnumerable<PlatformInfo> platforms)
    {
        if (entry.Version == "7.7") throw new InvalidOperationException("Проверка 7.7 выполняется в её Конфигураторе.");
        var info = Build(entry, true, "Толстый клиент", platforms, false);
        info.ArgumentList.Add("/IBCheckAndRepair");
        info.ArgumentList.Add(references ? "-LogAndRefsIntegrity" : "-LogIntegrity");
        info.ArgumentList.Add(repair ? "-BadRefNone" : "-TestOnly");
        info.ArgumentList.Add("/Out"); info.ArgumentList.Add(log); return info;
    }
    public static ProcessStartInfo BuildUpdate(BaseEntry entry, string file, string mergeSettings, bool updateDatabase, string log, IEnumerable<PlatformInfo> platforms)
    {
        if (entry.Version == "7.7") throw new InvalidOperationException("CFU/CF применяются только к платформе 8.x.");
        if (!File.Exists(file) || Path.GetExtension(file).ToLowerInvariant() is not (".cfu" or ".cf"))
            throw new ArgumentException("Выберите существующий файл обновления CFU или конфигурации CF.");
        var info = Build(entry, true, "Толстый клиент", platforms, false);
        info.ArgumentList.Add("/UpdateCfg"); info.ArgumentList.Add(file);
        if (mergeSettings.Length > 0)
        {
            if (!File.Exists(mergeSettings) || !Path.GetExtension(mergeSettings).Equals(".xml", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Файл настроек объединения должен быть XML.");
            info.ArgumentList.Add("-Settings"); info.ArgumentList.Add(mergeSettings);
        }
        if (updateDatabase) info.ArgumentList.Add("/UpdateDBCfg");
        info.ArgumentList.Add("/Out"); info.ArgumentList.Add(log); return info;
    }
    public static string Preview(ProcessStartInfo info)
    {
        var args = info.ArgumentList.ToArray();
        for (int i = 0; i < args.Length; i++)
        {
            if (Path.GetFileName(info.FileName).StartsWith("1cv7", StringComparison.OrdinalIgnoreCase) && args[i].StartsWith("/P", StringComparison.OrdinalIgnoreCase)) { args[i] = "/P••••••"; continue; }
            if (LaunchArguments.TakesValue(args[i]))
            {
                bool secret = new LaunchArgument(args[i], null).Secret;
                if (i + 1 < args.Length) { i++; if (secret) args[i] = "••••••"; }
                continue;
            }
            foreach (var key in new[] { "/IBConnectionString", "/ConfigurationRepositoryP", "/AccessToken", "/WSP", "/UC", "/P" })
                if (args[i].Equals(key, StringComparison.OrdinalIgnoreCase)) { if (i + 1 < args.Length) args[++i] = "••••••"; break; }
                else if (args[i].StartsWith(key, StringComparison.OrdinalIgnoreCase)) { args[i] = key + "••••••"; break; }
        }
        return Quote(info.FileName) + " " + string.Join(" ", args.Select(Quote));
    }
    public static string[] SplitArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var pointer = CommandLineToArgvW("vanbaz " + text, out int count);
        if (pointer == IntPtr.Zero) throw new ArgumentException("Не удалось разобрать параметры запуска.");
        try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(pointer); }
    }
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static string Quote(string value) => DesktopIntegration.Quote(value);
}
