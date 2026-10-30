using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Dango.Tests.Support;

// Only this test process's foreground events are observed. Users may continue
// switching their own apps; no external HWND, title or input history is retained.
internal sealed class OwnForegroundObservation : IDisposable
{
    private readonly ForegroundCallback callback;
    private readonly nint hook;
    private bool activated;
    internal OwnForegroundObservation()
    {
        callback = (_, _, _, _, _, _, _) => activated = true;
        hook = SetWinEventHook(3, 3, 0, callback, (uint)Environment.ProcessId, 0, 0);
        Assert.IsTrue(hook != 0, "The self-process foreground observer must be installed.");
        ObserveCurrent();
    }
    private void ObserveCurrent()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        if (process == (uint)Environment.ProcessId) activated = true;
    }
    internal void AssertNoActivation()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        ObserveCurrent();
        Assert.IsFalse(activated, "No window of the background test may acquire foreground focus, even transiently.");
    }
    public void Dispose() { _ = UnhookWinEvent(hook); GC.KeepAlive(callback); }

    private delegate void ForegroundCallback(nint hook, uint evt, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, ForegroundCallback callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
