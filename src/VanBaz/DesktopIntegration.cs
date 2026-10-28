using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;

namespace VanBaz;

public static class DesktopIntegration
{
    private const string Verb = @"Software\Classes\SystemFileAssociations\.1cd\shell\VanBaz";
    public static string Quote(string value)
    {
        var text = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { text.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; }
            text.Append('\\', slashes).Append(c); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
    public static string Arguments(string data, params string[] args) => string.Join(" ", new[] { "--data-dir", data }.Concat(args).Select(Quote));
    public static bool AutorunEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("VanBaz") is string value && value.StartsWith(Quote(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase);
    }
    public static void Autorun(bool enabled, string data)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("VanBaz", Quote(Environment.ProcessPath!) + " " + Arguments(data, "--minimized"));
        else if (AutorunEnabled()) key.DeleteValue("VanBaz", false);
    }
    public static bool ExplorerEnabled() { using var key = Registry.CurrentUser.OpenSubKey(Verb + @"\command"); return key?.GetValue("") is string value && value.StartsWith(Quote(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase); }
    public static void Explorer(bool enabled, string data)
    {
        foreach (var item in new[] { (Suffix: "", Label: "Открыть в ВанБаз", Mode: ""), (Suffix: ".Enterprise", Label: "ВанБаз: 1С:Предприятие", Mode: "--enterprise"), (Suffix: ".Designer", Label: "ВанБаз: Конфигуратор", Mode: "--designer") })
        {
            string path = Verb + item.Suffix;
            if (!enabled)
            {
                bool owned; using (var command = Registry.CurrentUser.OpenSubKey(path + @"\command")) owned = command?.GetValue("") is string value && value.StartsWith(Quote(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase);
                if (owned) Registry.CurrentUser.DeleteSubKeyTree(path, false);
                continue;
            }
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("", item.Label); key.SetValue("Icon", Environment.ProcessPath!);
            using var target = key.CreateSubKey("command");
            target.SetValue("", ExplorerCommand(Environment.ProcessPath!, data, item.Mode));
        }
    }
    public static string ExplorerCommand(string executable, string data, string mode)
    {
        if (mode is not ("" or "--enterprise" or "--designer")) throw new ArgumentException("Неизвестный режим Проводника.");
        return Quote(executable) + " " + Arguments(data, mode.Length == 0 ? new[] { "--open-file", "%1", "--register" } : new[] { "--open-file", "%1", mode });
    }
    public static void Shortcut(string destination, string data, string id, bool designer)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Служба ярлыков Windows недоступна.");
        object shell = Activator.CreateInstance(type)!; object? shortcut = null;
        try
        {
            dynamic automation = shell; shortcut = automation.CreateShortcut(destination); dynamic link = shortcut;
            link.TargetPath = Environment.ProcessPath; link.WorkingDirectory = AppContext.BaseDirectory;
            link.Arguments = Arguments(data, "--launch-id", id, designer ? "--designer" : "--enterprise");
            link.Description = "ВанБаз · " + (designer ? "Конфигуратор" : "1С:Предприятие"); link.Save();
        }
        finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }
}

public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource source;
    private readonly Action<int> action;
    private readonly List<int> registered = new();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    public GlobalHotkeys(IntPtr handle, Action<int> action)
    {
        source = HwndSource.FromHwnd(handle)!; this.action = action; source.AddHook(Hook);
    }
    public static (uint Modifiers, uint Key) Parse(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new ArgumentException("Используйте сочетание вроде Ctrl+Alt+S.");
        uint mods = 0;
        foreach (string part in parts[..^1])
            mods |= part.ToUpperInvariant() switch { "CTRL" => 2u, "ALT" => 1u, "SHIFT" => 4u, "WIN" => 8u, _ => throw new ArgumentException("Неизвестный модификатор: " + part) };
        string keyText = parts[^1].Length == 1 && char.IsDigit(parts[^1][0]) ? "D" + parts[^1] : parts[^1];
        if (!Enum.TryParse<Key>(keyText, true, out var key) || key is Key.None or Key.System || KeyInterop.VirtualKeyFromKey(key) == 0)
            throw new ArgumentException("Неизвестная клавиша: " + parts[^1]);
        return (mods | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
    public void Register(int id, string text)
    {
        var key = Parse(text);
        if (!RegisterHotKey(source.Handle, id, key.Modifiers, key.Key)) throw new InvalidOperationException("Сочетание занято или недоступно: " + text);
        registered.Add(id);
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && registered.Contains(wParam.ToInt32())) { handled = true; action(wParam.ToInt32()); }
        return IntPtr.Zero;
    }
    public void Dispose() { foreach (int id in registered) UnregisterHotKey(source.Handle, id); registered.Clear(); source.RemoveHook(Hook); }
}

public sealed class InstanceBridge : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    public InstanceBridge(string name, Action<string[]> receive) => _ = Listen(name, receive);
    private async Task Listen(string name, Action<string[]> receive)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);
                using var reader = new StreamReader(pipe);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(3000);
                string? text = await reader.ReadLineAsync(deadline.Token);
                if (text is { Length: < 32768 } && JsonSerializer.Deserialize<string[]>(text) is { } args) receive(args);
            }
            catch (OperationCanceledException) { if (stop.IsCancellationRequested) return; }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
    }
    public static void Send(string name, string[] args)
    {
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        pipe.Connect(3000);
        using var writer = new StreamWriter(pipe) { AutoFlush = true }; writer.WriteLine(JsonSerializer.Serialize(args));
    }
    public void Dispose() => stop.Cancel();
}
