using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using VanBaz;

// Opt-in integration test: replaces the clipboard, never included in the default harness.
internal static class NativeClipboardSmoke
{
    private const string Sample = "ВанБаз clipboard verification\r\nСтрока 2: Юникод;\t✓";
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    public static void Run(bool readOnly)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (readOnly)
                    {
                        if (Clipboard.GetText() != Sample) throw new Exception("Clipboard did not retain exact Unicode text after writer process exited");
                        Console.WriteLine("PASS native clipboard persistence: Unicode, CRLF, tab and symbol survive writer process exit");
                        return;
                    }
                    try
                    {
                        Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, Sample), false);
                        Console.WriteLine("WPF OleSetClipboard: success");
                        try { Clipboard.Flush(); Console.WriteLine("WPF OleFlushClipboard: success"); }
                        catch (Exception ex) { Console.WriteLine("WPF OleFlushClipboard failure: " + ex); }
                    }
                    catch (Exception ex) { Console.WriteLine("WPF OleSetClipboard failure: " + ex); }
                    using var held = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
                    bool opened = false;
                    var holder = new Thread(() =>
                    {
                        using var source = new HwndSource(new HwndSourceParameters("ВанБаз clipboard test lock") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
                        opened = OpenClipboard(source.Handle); held.Set();
                        if (opened) { release.Wait(); CloseClipboard(); }
                    }) { IsBackground = true };
                    holder.SetApartmentState(ApartmentState.STA); holder.Start();
                    try
                    {
                        if (!held.Wait(2000) || !opened) throw new Exception("Cannot establish clipboard contention fixture");
                        var copy = ClipboardService.WriteAsync(Sample);
                        bool remainedPending = !copy.IsCompleted, responsive = false;
                        _ = app.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => responsive = !copy.IsCompleted));
                        var releaseTask = Task.Run(async () => { await Task.Delay(350); release.Set(); });
                        if (!await copy || !remainedPending || !responsive) throw new Exception("Native clipboard retry did not preserve UI responsiveness");
                        if (Clipboard.GetText() != Sample) throw new Exception("Native clipboard readback differs");
                        Console.WriteLine("PASS native clipboard write: real contention, responsive dispatcher, exact Unicode/CRLF/tab readback");
                    }
                    finally { release.Set(); holder.Join(2000); }
                }
                catch (Exception ex) { failure = ex; }
                finally { app.Shutdown(); }
            }));
            app.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(15000)) throw new TimeoutException("Native clipboard test timed out");
        if (failure != null) throw failure;
    }
}
