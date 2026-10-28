using System.Diagnostics;
using System.IO;

namespace VanBaz;

public sealed record ServerCreationOptions(string Dbms, string DatabaseServer, string Database, string DatabaseUser = "", string DatabasePassword = "", string ClusterUser = "", string ClusterPassword = "", bool CreateDatabase = true, bool DisableJobs = true, string RawConnection = "");
public static class BaseCreation
{
    public static void RedactLog(string path, string connection)
    {
        if (!File.Exists(path)) return;
        var secrets = InfobaseConnection.Parse(connection).Where(p => p.Key.Contains("Pwd", StringComparison.OrdinalIgnoreCase) && p.Value.Length > 0).Select(p => p.Value).Distinct().OrderByDescending(s => s.Length).ToArray();
        if (secrets.Length == 0) return;
        string text = V8iImporter.ReadText(path); foreach (string secret in secrets) { text = text.Replace(secret.Replace("\"", "\"\""), "••••••", StringComparison.Ordinal); text = text.Replace(secret, "••••••", StringComparison.Ordinal); }
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(true));
    }
    public static string Connection(BaseEntry entry, ServerCreationOptions? server = null)
    {
        if (entry.Kind == "File") return entry.ConnectionDisplay;
        if (entry.Kind != "Server" || server == null) throw new ArgumentException("Для серверной базы нужны параметры кластера и СУБД.");
        var fields = server.RawConnection.Length > 0 ? InfobaseConnection.Parse(server.RawConnection) : InfobaseConnection.For(entry);
        if (server.RawConnection.Length == 0)
        {
            fields["DBMS"] = server.Dbms; fields["DBSrvr"] = server.DatabaseServer; fields["DB"] = server.Database; fields["DBUID"] = server.DatabaseUser;
            fields["CrSQLDB"] = server.CreateDatabase ? "Y" : "N"; fields["SchJobDn"] = server.DisableJobs ? "Y" : "N";
            if (server.DatabasePassword.Length > 0) fields["DBPwd"] = server.DatabasePassword;
            if (server.ClusterUser.Length > 0) fields["SUsr"] = server.ClusterUser;
            if (server.ClusterPassword.Length > 0) fields["SPwd"] = server.ClusterPassword;
        }
        var endpoint = InfobaseConnection.Endpoint(fields);
        if (endpoint.Kind != "Server" || !endpoint.Location.Equals(entry.Location, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Кластер и имя базы в строке подключения должны совпадать с выбранным размещением.");
        foreach (string key in new[] { "Srvr", "Ref", "DBMS", "DBSrvr", "DB" }) if (string.IsNullOrWhiteSpace(fields.GetValueOrDefault(key))) throw new ArgumentException("Заполните параметр серверной базы " + key + ".");
        if (!new[] { "MSSQLServer", "PostgreSQL", "IBMDB2", "OracleDatabase" }.Contains(fields["DBMS"])) throw new ArgumentException("Выберите поддерживаемую СУБД.");
        string[] allowed = { "Srvr", "Ref", "DBMS", "DBSrvr", "DB", "DBUID", "DBPwd", "SUsr", "SPwd", "CrSQLDB", "SchJobDn", "SQLYOffs", "Locale", "LicDstr", "SecurityLevel" };
        if (fields.Keys.Any(k => !allowed.Contains(k, StringComparer.OrdinalIgnoreCase))) throw new ArgumentException("Строка создания содержит неподдерживаемые параметры.");
        return InfobaseConnection.Format(InfobaseConnection.Parse(InfobaseConnection.Format(fields)));
    }
    public static ProcessStartInfo Build(BaseEntry entry, string template, string locale, string log, string result, IEnumerable<PlatformInfo> platforms, ServerCreationOptions? server = null)
    {
        if (entry.Kind is not ("File" or "Server") || entry.Version == "7.7") throw new ArgumentException("Создание доступно для файловых и серверных баз 8.x.");
        if (entry.Kind == "File" && !Path.IsPathFullyQualified(entry.Location)) throw new ArgumentException("Укажите полный путь новой базы.");
        if (entry.Kind == "File" && (File.Exists(entry.Location) || Directory.Exists(entry.Location) && Directory.EnumerateFileSystemEntries(entry.Location).Any())) throw new IOException("Для новой базы нужен пустой каталог.");
        if (entry.Kind == "File") CacheService.EnsureOrdinaryPath(entry.Location);
        if (template.Length > 0 && (!File.Exists(template) || !Path.GetExtension(template).Equals(".cf", StringComparison.OrdinalIgnoreCase))) throw new IOException("Выберите существующий шаблон конфигурации .cf.");
        if (locale.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(locale, @"^[A-Za-z]{2,3}(?:[_-][A-Za-z]{2})?$")) throw new ArgumentException("Код локализации: например ru_RU.");
        var copy = entry.Copy(); copy.ExtraArguments = ""; copy.ExternalProcessor = ""; copy.UseRepository = false; copy.DesignerLogin = false; copy.Advanced = new();
        var probe = LaunchService.Build(copy, true, "Толстый клиент", platforms);
        var info = new ProcessStartInfo(probe.FileName) { UseShellExecute = entry.RunAsAdmin }; if (entry.RunAsAdmin) info.Verb = "runas";
        info.ArgumentList.Add("CREATEINFOBASE"); info.ArgumentList.Add(Connection(entry, server));
        if (template.Length > 0) { info.ArgumentList.Add("/UseTemplate"); info.ArgumentList.Add(Path.GetFullPath(template)); }
        if (locale.Length > 0) info.ArgumentList.Add("/VL" + locale);
        info.ArgumentList.Add("/Out"); info.ArgumentList.Add(log); info.ArgumentList.Add("/DumpResult"); info.ArgumentList.Add(result); return info;
    }
    public static bool Succeeded(BaseEntry entry, int exitCode, string result) => exitCode == 0 && File.Exists(result) && File.ReadAllText(result).Trim() == "0" && (entry.Kind == "Server" || File.Exists(Path.Combine(entry.Location, "1Cv8.1CD")));
}
