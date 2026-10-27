using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;

namespace VanBaz;

internal static class TextClipboard
{
    private static HwndSource? owner;

    // Publish fully materialized Unicode text. There is no delayed OLE rendering
    // or synchronous retry/flush inside this single attempt.
    internal static void Write(string text)
    {
        if (owner == null)
        {
            owner = new HwndSource(new HwndSourceParameters("ВанБаз clipboard")
            { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 });
            Dispatcher.CurrentDispatcher.ShutdownStarted += (_, _) => { owner?.Dispose(); owner = null; };
        }
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr memory = GlobalAlloc(0x42, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Недостаточно памяти для копирования текста.");
        bool opened = false;
        try
        {
            IntPtr target = GlobalLock(memory);
            if (target == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { Marshal.Copy(bytes, 0, target, bytes.Length); }
            finally { GlobalUnlock(memory); }
            opened = OpenClipboard(owner.Handle);
            if (!opened) throw new ExternalException("Буфер обмена недоступен.", ClipboardService.CannotOpen);
            if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось подготовить буфер обмена.");
            if (SetClipboardData(13, memory) == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось передать текст в буфер обмена.");
            memory = IntPtr.Zero; // Windows now owns this GMEM_MOVEABLE allocation.
        }
        finally
        {
            if (opened) CloseClipboard();
            if (memory != IntPtr.Zero) GlobalFree(memory);
        }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
