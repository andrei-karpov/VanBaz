using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace VanBaz;

public static class DialogBehavior
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rectangle { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rect);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    // Fit modal dialogs to the owner's actual monitor, including scaled and small RDP desktops.
    public static void FitToScreen(Window window)
    {
        void Fit()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || window.WindowState != WindowState.Normal) return;
            var screen = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            double scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
            window.MinWidth = Math.Min(window.MinWidth, screen.Width / scale);
            window.MinHeight = Math.Min(window.MinHeight, screen.Height / scale);
            if (!GetWindowRect(handle, out var bounds)) return;
            int width = Math.Min(bounds.Right - bounds.Left, screen.Width), height = Math.Min(bounds.Bottom - bounds.Top, screen.Height);
            SetWindowPos(handle, IntPtr.Zero, Math.Clamp(bounds.Left, screen.Left, screen.Right - width), Math.Clamp(bounds.Top, screen.Top, screen.Bottom - height), width, height, 0x14);
        }
        window.Loaded += (_, _) => Fit();
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke(new Action(Fit), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    public static void Selection(Selector list, params Button[] buttons)
    {
        void Update() { foreach (var b in buttons) { b.IsEnabled = list.SelectedItem != null; ToolTipService.SetShowOnDisabled(b, true); b.ToolTip = b.IsEnabled ? b.Content?.ToString() : "Сначала выберите строку списка."; } }
        list.SelectionChanged += (_, _) => Update(); Update();
    }
    public static bool OnItem(object source)
    {
        for (var d = source as DependencyObject; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : null)
            if (d is ListBoxItem or DataGridRow) return true;
        return false;
    }
    public static void CloseOnEscape(Window window)
    {
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; window.Close(); } };
    }
}
