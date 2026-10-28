using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace VanBaz;

public static class LaunchBehavior
{
    public static readonly string[] ConnectionModes = { "Авто", "/IBName", "/IBConnectionString", "/F /S /WS" };
    public static LaunchRecord? Running(IEnumerable<LaunchRecord> history, string id, string mode, Func<int, DateTime?>? started = null)
    {
        started ??= Started;
        return history.Where(h => h.BaseId == id && h.Mode == mode && h.ProcessId > 0 && h.ProcessStarted.HasValue).FirstOrDefault(h => started(h.ProcessId)?.ToUniversalTime() == h.ProcessStarted!.Value.ToUniversalTime());
    }
    private static DateTime? Started(int id)
    { try { using var process = Process.GetProcessById(id); return !process.HasExited && process.SessionId == Process.GetCurrentProcess().SessionId ? process.StartTime : null; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; } }
    public static bool Activate(LaunchRecord record)
    {
        if (Started(record.ProcessId)?.ToUniversalTime() != record.ProcessStarted?.ToUniversalTime()) return false;
        using var process = Process.GetProcessById(record.ProcessId); var handle = process.MainWindowHandle; if (handle == IntPtr.Zero) return false; ShowWindowAsync(handle, 9); return SetForegroundWindow(handle);
    }
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
}

public partial class MainWindow
{
    private bool ConfirmRepeatedLaunch(BaseEntry entry, bool designer)
    {
        if (!catalog.Settings.MonitorRepeatedLaunch || isolatedSession) return true;
        var running = LaunchBehavior.Running(catalog.History, entry.Id, designer ? "Конфигуратор" : "Предприятие"); if (running == null) return true;
        var form = new FormDialog("База уже запущена", 590, 340) { Owner = this }; form.Section("Повторный запуск"); form.Note(entry.Name + " · " + running.Mode + " · процесс " + running.ProcessId);
        form.Choice("action", "Действие", "Открыть существующее окно", new[] { "Открыть существующее окно", "Запустить ещё один сеанс" }); form.SetAcceptText("Выполнить");
        if (form.ShowDialog() != true) return false;
        if (form.Value("action") == "Запустить ещё один сеанс") return true;
        try { if (!LaunchBehavior.Activate(running)) StatusText.Text = "Окно процесса пока недоступно. Можно повторить запуск и выбрать новый сеанс."; } catch (Exception ex) { Error("Открытие окна 1С", ex); }
        return false;
    }
    private void NotifyClientStarted(BaseEntry entry, bool designer)
    {
        if (!catalog.Settings.ShowLaunchBalloon || tray == null || isolatedSession) return;
        tray.Visible = true; tray.ShowBalloonTip(3500, "ВанБаз", entry.Name + " — " + (designer ? "Конфигуратор" : "Предприятие"), System.Windows.Forms.ToolTipIcon.Info);
    }
}
