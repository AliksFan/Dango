using Dango.Desktop.DesktopHost.Win32;
using WinFormsCreateParams = System.Windows.Forms.CreateParams;
using WinFormsNativeWindow = System.Windows.Forms.NativeWindow;

namespace Dango.Desktop.DesktopHost.WorkerW;

internal sealed class WorkerWNativeHost : WinFormsNativeWindow, IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolWindow = 0x00000080;
    private bool disposed;

    public WorkerWNativeHost(WorkerWDesktopTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var parameters = new WinFormsCreateParams
        {
            Caption = "Dango P1-003 Native Desktop Host",
            Parent = nint.Zero,
            Style = WsPopup | WsVisible,
            ExStyle = WsExLayered | WsExToolWindow,
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1,
        };

        CreateHandle(parameters);
        try
        {
            SurfaceSize = NativeMethods.AttachWorkerWLayeredHost(Handle, target);
        }
        catch
        {
            DestroyHandle();
            throw;
        }
    }

    public (int Width, int Height) SurfaceSize { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DestroyHandle();
    }
}
