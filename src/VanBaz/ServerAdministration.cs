using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace VanBaz;

public sealed record ServerAdminCommand(string Caption, ProcessStartInfo Process, string Kind, string Target, string Expected = "", string ExpectedArguments = "")
{
    public string Preview => Caption + "\n" + LaunchService.Preview(Process);
}
public static class ServerAdministration
{
    public static string SystemExecutable(string name, string arch, string windows, bool os64, bool process64)
    {
        if (arch is not ("x86" or "x64") || arch == "x64" && !os64) throw new IOException("Неподдерживаемая архитектура команды.");
        string dir = arch == "x86" && os64 ? "SysWOW64" : arch == "x64" && !process64 ? "Sysnative" : "System32";
        string result = Path.Combine(windows, dir, name); if (!File.Exists(result)) throw new FileNotFoundException("Не найдена системная программа.", result); return result;
    }
    public static ServerAdminCommand Registration(PlatformInfo platform, string dllName, bool unregister, bool silent, string windows, bool os64, bool process64, Func<string, string, bool>? registered = null)
    {
        if (dllName is not ("radmin.dll" or "comcntr.dll")) throw new ArgumentException("Выберите radmin.dll или comcntr.dll.");
        string dll = Path.Combine(Path.GetFullPath(platform.Directory), dllName); CacheService.EnsureOrdinaryPath(dll);
        if (!File.Exists(dll)) throw new FileNotFoundException("Компонента выбранной платформы не установлена.", dll);
        if (unregister && !(registered ?? IsRegistered)(dll, platform.Architecture)) throw new InvalidOperationException("Выбранная DLL не зарегистрирована. Снятие регистрации другой версии запрещено.");
        var info = new ProcessStartInfo(SystemExecutable("regsvr32.exe", platform.Architecture, windows, os64, process64)) { UseShellExecute = true, Verb = "runas", WorkingDirectory = platform.Directory };
        if (silent) info.ArgumentList.Add("/s"); if (unregister) info.ArgumentList.Add("/u"); info.ArgumentList.Add(dll);
        return new((unregister ? "Снять регистрацию " : "Зарегистрировать для компьютера ") + dllName + " · " + platform.Display, info, unregister ? "unregister" : "register", dll, platform.Architecture);
    }
    public static bool IsRegistered(string dll, string architecture)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, architecture == "x64" ? RegistryView.Registry64 : RegistryView.Registry32);
        using var classes = root.OpenSubKey(@"SOFTWARE\Classes\CLSID"); if (classes == null) return false;
        var paths = new List<string>();
        foreach (string name in classes.GetSubKeyNames())
        { using var key = classes.OpenSubKey(name + "\\InprocServer32"); if (key?.GetValue(null) is string value && Path.GetFileName(value.Trim('"')).Equals(Path.GetFileName(dll), StringComparison.OrdinalIgnoreCase)) paths.Add(Path.GetFullPath(value.Trim('"'))); }
        // A conservative guard: mixed registrations of the same DLL name are not removed.
        return paths.Count > 0 && paths.All(path => path.Equals(Path.GetFullPath(dll), StringComparison.OrdinalIgnoreCase));
    }
    public static ServerAdminCommand Service(ServerService service, string action, string windows, bool os64, bool process64, PlatformInfo? replacement = null)
    {
        if (action is not ("start" or "stop" or "config")) throw new ArgumentException("Неизвестное действие службы.");
        if (!Path.GetFileName(service.Executable).Equals("ragent.exe", StringComparison.OrdinalIgnoreCase) || service.Name.IndexOfAny(new[] { '\0', '\r', '\n', '/', '\\' }) >= 0) throw new ArgumentException("Некорректная служба агента.");
        var info = new ProcessStartInfo(SystemExecutable("sc.exe", os64 ? "x64" : "x86", windows, os64, process64)) { UseShellExecute = true, Verb = "runas" };
        info.ArgumentList.Add(action); info.ArgumentList.Add(service.Name);
        if (action == "config")
        {
            if (replacement == null || replacement.Architecture != service.Architecture || service.State != "Остановлена") throw new InvalidOperationException("Перед сменой версии остановите службу; разрядность новой платформы должна совпадать.");
            string exe = Path.Combine(replacement.Directory, "ragent.exe"); if (!File.Exists(exe)) throw new FileNotFoundException("Не установлен сервер выбранной версии.", exe);
            CacheService.EnsureOrdinaryPath(exe); info.ArgumentList.Add("binPath="); info.ArgumentList.Add(DesktopIntegration.Quote(exe) + " " + service.Arguments);
        }
        return new((action == "start" ? "Запустить " : action == "stop" ? "Остановить " : "Сменить платформу службы ") + service.Name, info, action, service.Name, service.Executable, service.Arguments);
    }
    public static async Task Execute(ServerAdminCommand command, Func<ProcessStartInfo, Task<int>>? run = null, Func<IReadOnlyList<ServerService>>? services = null, Func<string, string, bool>? registered = null)
    {
        registered ??= IsRegistered; services ??= () => ServerServices.Read(false);
        if (command.Kind == "unregister" && !registered(command.Target, command.Expected)) throw new IOException("Регистрация изменилась после предпросмотра. Операция отменена.");
        if (command.Kind is "start" or "stop" or "config")
        {
            var current = services().SingleOrDefault(s => s.Name == command.Target);
            if (current == null || !current.Executable.Equals(command.Expected, StringComparison.OrdinalIgnoreCase) || current.Arguments != command.ExpectedArguments) throw new IOException("Служба изменилась после предпросмотра. Обновите список.");
            if (command.Kind == "config" && current.State != "Остановлена") throw new IOException("Служба уже запущена; смена версии отменена.");
        }
        run ??= async info => { using var process = Process.Start(info) ?? throw new IOException("Windows не создала процесс."); await process.WaitForExitAsync(); return process.ExitCode; };
        int exit = await run(command.Process);
        if (exit != 0) throw new IOException(command.Caption + ": код завершения " + exit + ". Последующие действия не выполнены.");
        if (command.Kind == "register" && !registered(command.Target, command.Expected)) throw new IOException("Регистрация DLL не подтверждена в реестре выбранной разрядности.");
        if (command.Kind == "unregister" && registered(command.Target, command.Expected)) throw new IOException("DLL осталась зарегистрирована. Проверьте результат regsvr32.");
        if (command.Kind == "config")
        {
            var target = ServerServices.ParseImage(command.Process.ArgumentList.Last()); var current = services().SingleOrDefault(s => s.Name == command.Target);
            if (target == null || current == null || !current.Executable.Equals(target.Value.Executable, StringComparison.OrdinalIgnoreCase) || current.Arguments != target.Value.Arguments) throw new IOException("Новая версия службы не подтверждена после команды sc. Обновите список служб.");
        }
        if (command.Kind is "start" or "stop")
        {
            string wanted = command.Kind == "start" ? "Работает" : "Остановлена";
            for (int attempt = 0; attempt < 30; attempt++) { if (services().SingleOrDefault(s => s.Name == command.Target)?.State == wanted) return; await Task.Delay(500); }
            throw new IOException("Команда принята, но служба не перешла в состояние «" + wanted + "» за 15 секунд. Проверьте её в Windows.");
        }
    }
}
