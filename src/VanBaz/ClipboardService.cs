using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace VanBaz;

public static class ClipboardService
{
    public const int CannotOpen = unchecked((int)0x800401D0);
    private static long generation;

    // Keep clipboard access on the UI STA, but release the dispatcher between attempts.
    // A newer copy supersedes an older pending copy, so it cannot overwrite it later.
    public static async Task<bool> WriteAsync(string text, Action<string>? write = null)
    {
        long request = Interlocked.Increment(ref generation);
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        write ??= TextClipboard.Write;
        for (int attempt = 0; ; attempt++)
        {
            if (request != Interlocked.Read(ref generation)) return false;
            try
            {
                bool written = false;
                void Write()
                {
                    if (request != Interlocked.Read(ref generation)) return;
                    write(text); written = true;
                }
                if (dispatcher.CheckAccess()) Write();
                else await dispatcher.InvokeAsync(Write);
                return written;
            }
            catch (ExternalException ex) when (ex.ErrorCode == CannotOpen)
            {
                if (attempt == 12)
                    throw new ExternalException("Буфер обмена пока недоступен. Повторите копирование через несколько секунд.", ex);
                await Task.Delay(Math.Min(25 * (attempt + 1), 200));
            }
        }
    }

    public static async Task<bool> CopyAsync(Window owner, string text, Action? success = null, Action<string>? failure = null, Action<string>? write = null)
    {
        try
        {
            if (!await WriteAsync(text, write)) return false;
            success?.Invoke(); return true;
        }
        catch (Exception ex)
        {
            string message = ex is ExternalException ? ex.Message : "Не удалось скопировать текст. " + ex.Message;
            if (failure != null) failure(message);
            else if (owner.IsVisible) MessageBox.Show(owner, message, "Копирование", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }
}
