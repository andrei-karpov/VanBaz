using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VanBaz;

public sealed record ServerService(string Name, string Description, string State, string Account, string Executable, string Arguments, string Architecture, string Version)
{
    public int ClusterPort => ServerServices.Port(Arguments, "regport", 1541);
    public string? RunningExecutable { get; init; }
    public string? RunningArguments { get; init; }
    public string? RunningVersion { get; init; }
}

public static class ServerServices
{
    public static IReadOnlyList<ServerService> Read() => Read(true);
    public static IReadOnlyList<ServerService> Read(bool includeRuntime)
    {
        var result = new List<ServerService>();
        using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        if (root == null) throw new IOException("Список служб Windows недоступен.");
        foreach (string name in root.GetSubKeyNames())
        {
            try
            {
                using var key = root.OpenSubKey(name);
                string image = Environment.ExpandEnvironmentVariables(key?.GetValue("ImagePath") as string ?? "");
                var parsed = ParseImage(image); if (parsed == null) continue;
                string exe = parsed.Value.Executable;
                string version = "", arch = "?";
                if (File.Exists(exe)) { version = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? ""; arch = PlatformCatalog.Architecture(exe); }
                var runtime = includeRuntime ? Runtime(name) : null;
                result.Add(new(name, key?.GetValue("DisplayName") as string ?? name, State(name), key?.GetValue("ObjectName") as string ?? "", exe, parsed.Value.Arguments, arch, version) { RunningExecutable = runtime?.Executable, RunningArguments = runtime?.Arguments, RunningVersion = runtime?.Version });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return result.OrderBy(s => s.Name).ToArray();
    }
    public static (string Executable, string Arguments)? ParseImage(string value)
    {
        value = value.Trim(); string exe, args;
        if (value.StartsWith('"')) { int end = value.IndexOf('"', 1); if (end < 0) return null; exe = value[1..end]; args = value[(end + 1)..].Trim(); }
        else { int end = value.IndexOf("ragent.exe", StringComparison.OrdinalIgnoreCase); if (end < 0) return null; end += "ragent.exe".Length; exe = value[..end]; args = value[end..].Trim(); }
        if (!Path.IsPathFullyQualified(exe) || !Path.GetFileName(exe).Equals("ragent.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return (Path.GetFullPath(exe), args);
    }
    public static int Port(string args, string name, int fallback)
    {
        string[] parts = LaunchService.SplitArguments(args); var positions = parts.Select((p, i) => (p, i)).Where(p => p.p.Equals("-" + name, StringComparison.OrdinalIgnoreCase) || p.p.Equals("/" + name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (positions.Length == 0) return fallback;
        if (positions.Length != 1 || positions[0].i + 1 >= parts.Length || !int.TryParse(parts[positions[0].i + 1], out int port) || port is < 1 or > 65535) return -1;
        return port;
    }
    public static string State(string name)
    {
        IntPtr manager = OpenSCManager(null, null, 1); if (manager == IntPtr.Zero) return "Недоступно";
        try
        {
            IntPtr service = OpenService(manager, name, 4); if (service == IntPtr.Zero) return "Недоступно";
            try { if (!QueryServiceStatusEx(service, 0, out var state, Marshal.SizeOf<ServiceStatus>(), out _)) return "Недоступно"; return state.State switch { 1 => "Остановлена", 4 => "Работает", _ => "Переходное состояние" }; }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
    private static (string Executable, string Arguments, string Version)? Runtime(string name)
    {
        IntPtr manager = OpenSCManager(null, null, 1); if (manager == IntPtr.Zero) return null;
        try
        {
            IntPtr handle = OpenService(manager, name, 4); if (handle == IntPtr.Zero) return null;
            try
            {
                if (!QueryServiceStatusEx(handle, 0, out var state, Marshal.SizeOf<ServiceStatus>(), out _) || state.State != 4 || state.ProcessId == 0) return null;
                using var process = Process.GetProcessById((int)state.ProcessId); var started = process.StartTime;
                string? actual = process.MainModule?.FileName; if (actual == null) return null;
                dynamic? locator = null, service = null, rows = null;
                try
                {
                    locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!); service = locator!.ConnectServer(".", "root\\cimv2");
                    rows = service.ExecQuery("SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + state.ProcessId, "WQL", 48);
                    string? command = null; foreach (dynamic row in rows) { try { command = row.CommandLine as string; } finally { Marshal.FinalReleaseComObject(row); } }
                    var parsed = command == null ? null : ParseImage(command);
                    if (parsed == null || !parsed.Value.Executable.Equals(actual, StringComparison.OrdinalIgnoreCase) || process.HasExited || process.StartTime != started) return null;
                    return (actual, parsed.Value.Arguments, FileVersionInfo.GetVersionInfo(actual).FileVersion ?? "");
                }
                finally { if (rows != null) Marshal.FinalReleaseComObject(rows); if (service != null) Marshal.FinalReleaseComObject(service); if (locator != null) Marshal.FinalReleaseComObject(locator); }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return null; }
            finally { CloseServiceHandle(handle); }
        }
        finally { CloseServiceHandle(manager); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ServiceStatus { public uint Type, State, Controls, Win32Exit, ServiceExit, CheckPoint, WaitHint, ProcessId, Flags; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenService(IntPtr manager, string service, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatus status, int size, out int needed);
    [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
}

public sealed record AgentVersionResult(bool Verified, bool Compatible, string Message, string Version = "");
public static class ServerVersionProbe
{
    public static AgentVersionResult Evaluate(BaseEntry entry, string clientVersion, IEnumerable<ServerService> services, string localMachine)
    {
        if (entry.Kind != "Server") return new(false, false, "Проверка агента относится к серверной базе.");
        var connection = InfobaseConnection.For(entry); string server = connection["Srvr"];
        if (server.Contains(',') || server.Contains(';')) return new(false, false, "Несколько адресов кластера: агент не определён однозначно.");
        if (!Uri.TryCreate(server.Contains("://") ? server : "tcp://" + server, UriKind.Absolute, out var uri) || uri.Scheme != "tcp") return new(false, false, "Не удалось определить адрес кластера.");
        string host = uri.Host.Trim('[', ']'); int port = uri.Port < 0 ? 1541 : uri.Port;
        if (!(host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "." || host == "127.0.0.1" || host == "::1" || host.Equals(localMachine, StringComparison.OrdinalIgnoreCase))) return new(false, false, "Удалённый агент не проверен: поддержана проверка служб только этого компьютера. Адрес: " + server);
        var all = services.ToArray();
        if (all.Any(s => s.State == "Работает" && (s.RunningExecutable == null || s.RunningArguments == null))) return new(false, false, "Не удалось прочитать путь и командную строку одного из работающих агентов. Привязка к порту не подтверждена.");
        var matches = all.Where(s => s.State == "Работает" && ServerServices.Port(s.RunningArguments!, "regport", 1541) == port).ToArray();
        if (matches.Length != 1) return new(false, false, matches.Length == 0 ? "Не найдена служба агента для порта кластера " + port + "." : "Порту кластера " + port + " соответствуют несколько служб; агент не определён однозначно.");
        var service = matches[0];
        if (service.State != "Работает") return new(false, false, "Состояние службы «" + service.Name + "»: " + service.State + ". Версию работающего агента подтвердить нельзя.");
        if (!Version.TryParse(service.RunningVersion, out var agent) || !Version.TryParse(clientVersion, out var client)) return new(false, false, "Версия файла работающего агента или выбранного клиента недоступна.");
        bool equal = agent == client;
        return new(true, equal, "Служба: " + service.Name + "\nФайл работающего агента: " + service.RunningExecutable + "\nАгент: " + agent + " · клиент: " + client + (equal ? "\nВерсии совпадают." : "\nВерсии различаются."), agent.ToString());
    }
}
