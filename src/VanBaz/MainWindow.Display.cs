using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace VanBaz;
public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    private HwndSource? displaySource;
    private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private SavedWindowPlacement? preferredPlacement;
    private PixelBounds? appliedBounds, trayBounds;
    private List<ScreenArea>? trayDisplays, knownDisplays;
    private readonly Dictionary<string, uint> displayDpis = new();
    private WindowState lastWindowState = WindowState.Normal, trayWindowState = WindowState.Normal;
    private bool applyingPlacement, displayPending, inTray, sessionNotification;
    private IntPtr DisplayHandle => new WindowInteropHelper(this).Handle;

    private List<ScreenArea> DisplayAreas() => System.Windows.Forms.Screen.AllScreens.Select(s =>
    {
        var a = s.WorkingArea;
        if (!displayDpis.TryGetValue(s.DeviceName, out uint dpi))
        {
            // An invisible, non-activating per-monitor-aware HWND avoids GetDpiForMonitor's
            // awareness-dependent results. Use the same GetDpiForWindow API as the real window.
            IntPtr probe = CreateWindowEx(0x08000080, "STATIC", "", 0x80000000, s.Bounds.Left + s.Bounds.Width / 2, s.Bounds.Top + s.Bounds.Height / 2, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            try { dpi = probe == IntPtr.Zero ? 96 : GetDpiForWindow(probe); }
            finally { if (probe != IntPtr.Zero) DestroyWindow(probe); }
            displayDpis[s.DeviceName] = dpi = Math.Max(96, dpi);
        }
        return new ScreenArea(s.DeviceName, a.Left, a.Top, a.Width, a.Height, dpi);
    }).ToList();
    private void QueueDisplayRepair()
    { displayPending = true; displayTimer.Stop(); displayTimer.Start(); }
    private PixelBounds? CurrentPixelBounds() => DisplayHandle != IntPtr.Zero && GetWindowRect(DisplayHandle, out var r) ? new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;
    private void CaptureNormalPlacement()
    {
        if (applyingPlacement || displayPending || IsIconic(DisplayHandle) || IsZoomed(DisplayHandle) || WindowState != WindowState.Normal || !IsVisible || inTray || CurrentPixelBounds() is not { Width: > 0, Height: > 0 } bounds || bounds == appliedBounds) return;
        var displays = DisplayAreas();
        if (knownDisplays != null && !knownDisplays.SequenceEqual(displays)) { QueueDisplayRepair(); return; }
        var device = System.Windows.Forms.Screen.FromHandle(DisplayHandle).DeviceName;
        var area = displays.FirstOrDefault(s => s.Device == device);
        if (area == null) return;
        preferredPlacement = new(area with { Dpi = Math.Max(96, GetDpiForWindow(DisplayHandle)) }, bounds);
        appliedBounds = bounds;
    }
    private void ApplyStartupDisplay()
    {
        displaySource = HwndSource.FromHwnd(DisplayHandle); displaySource?.AddHook(DisplayMessage);
        sessionNotification = WTSRegisterSessionNotification(DisplayHandle, 0);
        displayTimer.Tick += (_, _) => { displayTimer.Stop(); RepairDisplay(); displayPending = false; };
        LocationChanged += (_, _) => CaptureNormalPlacement(); SizeChanged += (_, _) => CaptureNormalPlacement();
        var p = catalog.Settings; var areas = DisplayAreas(); knownDisplays = areas;
        var area = areas.FirstOrDefault(s => s.Device == p.StartupMonitor) ?? areas.First(s => s.Device == System.Windows.Forms.Screen.FromHandle(DisplayHandle).DeviceName);
        if (p.RestorePosition && WindowGeometry.Valid(p.WindowPlacement)) preferredPlacement = p.WindowPlacement;
        else
        {
            double dpi = area.Dpi / 96;
            int width = (int)Math.Clamp(Width * dpi, 1, area.Width), height = (int)Math.Clamp(Height * dpi, 1, area.Height);
            var bounds = new PixelBounds(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
            // One-time migration of old WPF values. Negative coordinates are legitimate monitor positions.
            if (p.RestorePosition && double.IsFinite(p.Left) && double.IsFinite(p.Top) && (p.Left != -1 || p.Top != -1))
            {
                bounds = bounds with { Left = (int)Math.Clamp(p.Left * dpi, int.MinValue, int.MaxValue), Top = (int)Math.Clamp(p.Top * dpi, int.MinValue, int.MaxValue) };
                area = WindowGeometry.Fit(new(area with { Device = "" }, bounds), areas).Screen;
            }
            preferredPlacement = new(area, bounds);
        }
        PlaceNormalWindow();
        if (p.RestorePosition && p.Maximized) WindowState = WindowState.Maximized;
    }
    private void PlaceNormalWindow(PixelBounds? exactBounds = null)
    {
        if (!WindowGeometry.Valid(preferredPlacement)) return;
        var (screen, bounds) = WindowGeometry.Fit(preferredPlacement!, DisplayAreas());
        bounds = exactBounds ?? bounds;
        applyingPlacement = true;
        try
        {
            MinWidth = Math.Min(700, screen.Width * 96 / screen.Dpi); MinHeight = Math.Min(520, screen.Height * 96 / screen.Dpi);
            WindowState = WindowState.Normal;
            SetWindowPos(DisplayHandle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0014);
            appliedBounds = CurrentPixelBounds();
        }
        finally { applyingPlacement = false; }
    }
    private void RepairDisplay()
    {
        if (displaySource == null || !WindowGeometry.Valid(preferredPlacement)) return;
        var state = inTray ? trayWindowState : WindowState == WindowState.Minimized ? lastWindowState : WindowState;
        bool minimized = !inTray && WindowState == WindowState.Minimized;
        PlaceNormalWindow();
        WindowState = state;
        if (minimized) WindowState = WindowState.Minimized;
        knownDisplays = DisplayAreas();
    }
    private IntPtr DisplayMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WPF handles WM_DPICHANGED and its suggested rectangle before we capture the final size.
        if (message == 0x02E0)
        {
            displayDpis.Clear();
            Dispatcher.BeginInvoke(() =>
            {
                if (knownDisplays != null && !knownDisplays.SequenceEqual(DisplayAreas())) QueueDisplayRepair();
                else if (!displayPending) CaptureNormalPlacement();
            }, DispatcherPriority.Loaded);
        }
        if (message == 0x007E || message == 0x001A || message == 0x02B1)
        { displayDpis.Clear(); QueueDisplayRepair(); }
        return IntPtr.Zero;
    }
    private void SaveWindowPlacement()
    {
        if (displaySource == null) return;
        CaptureNormalPlacement();
        catalog.Settings.WindowPlacement = preferredPlacement;
        catalog.Settings.Maximized = (inTray ? trayWindowState : WindowState == WindowState.Minimized ? lastWindowState : WindowState) == WindowState.Maximized;
        if (preferredPlacement is { } p)
        { catalog.Settings.Width = p.Bounds.Width * 96 / p.Screen.Dpi; catalog.Settings.Height = p.Bounds.Height * 96 / p.Screen.Dpi; }
    }
    private void DisposeDisplayTracking()
    {
        displayTimer.Stop();
        if (sessionNotification) WTSUnRegisterSessionNotification(DisplayHandle);
        displaySource?.RemoveHook(DisplayMessage); displaySource = null;
    }
    private void ZoomList(int direction)
    {
        if (locked || operations.Count > 0) return;
        if (Commit(() => catalog.Settings.FontSize = Math.Clamp(catalog.Settings.FontSize + direction * 96d/72, 8d*96/72, 28d*96/72))) ApplyPreferences();
    }
}
