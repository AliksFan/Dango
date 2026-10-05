using Dango.Desktop.DesktopHost.Win32;
using WinFormsCreateParams = System.Windows.Forms.CreateParams;
using WinFormsMessage = System.Windows.Forms.Message;
using WinFormsNativeWindow = System.Windows.Forms.NativeWindow;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace Dango.Desktop.DesktopHost.WorkerW;

internal sealed class WorkerWInputProxy : WinFormsNativeWindow, IDisposable
{
    private const int WmCaptureChanged = 0x0215;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmMouseActivate = 0x0021;
    private const int WmMouseMove = 0x0200;
    private const int WmNcHitTest = 0x0084;
    private const int MaNoActivate = 3;
    private const int HtClient = 1;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private readonly Func<double, double, bool> beginDrag;
    private readonly Action<double, double> moveDrag;
    private readonly Action completeDrag;
    private readonly Action cancelDrag;
    private readonly Action<double, double>? completePointer;
    private readonly Action? contextRequested;
    private readonly WorkerWDesktopTarget target;
    private readonly WinFormsTimer inputPriorityTimer;
    private NativeMethods.NativePoint dragOrigin;
    private bool enabled;
    private bool dragging;
    private bool isWindowShown;
    private bool disposed;
    private bool hasBounds;
    private int boundsX;
    private int boundsY;
    private int boundsWidth;
    private int boundsHeight;

    public WorkerWInputProxy(
        WorkerWDesktopTarget target,
        Func<double, double, bool> beginDrag,
        Action<double, double> moveDrag,
        Action completeDrag,
        Action cancelDrag,
        Action<double, double>? completePointer = null, Action? contextRequested = null)
    {
        this.target = target;
        this.beginDrag = beginDrag;
        this.moveDrag = moveDrag;
        this.completeDrag = completeDrag;
        this.cancelDrag = cancelDrag;
        this.completePointer = completePointer;
        this.contextRequested = contextRequested;
        inputPriorityTimer = new WinFormsTimer
        {
            Interval = 50,
        };
        inputPriorityTimer.Tick += OnInputPriorityTimerTick;
        var parameters = new WinFormsCreateParams
        {
            Caption = "Dango P1-003 Desktop Input Proxy",
            Parent = nint.Zero,
            Style = WsPopup | WsVisible,
            ExStyle = WsExLayered | WsExNoActivate | WsExToolWindow,
            X = 0,
            Y = 0,
            Width = 1,
            Height = 1,
        };

        CreateHandle(parameters);
        try
        {
            NativeMethods.AttachWorkerWInputProxy(Handle, target);
        }
        catch
        {
            DestroyHandle();
            throw;
        }
    }

    public void SetEnabled(bool isEnabled)
    {
        enabled = isEnabled;
        if (!enabled)
        {
            CancelActiveDrag();
            inputPriorityTimer.Stop();
        }
        else
        {
            inputPriorityTimer.Start();
        }

        RefreshInputPriority();
    }

    public void SetBounds(int x, int y, int width, int height)
    {
        if (disposed)
        {
            return;
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (hasBounds &&
            boundsX == x &&
            boundsY == y &&
            boundsWidth == width &&
            boundsHeight == height)
        {
            return;
        }

        hasBounds = true;
        boundsX = x;
        boundsY = y;
        boundsWidth = width;
        boundsHeight = height;
        NativeMethods.PositionWorkerWInputProxy(Handle, x, y, width, height, isWindowShown);
        RefreshInputPriority();
    }

    protected override void WndProc(ref WinFormsMessage message)
    {
        switch (message.Msg)
        {
            case 0x0205 when enabled && contextRequested is not null: // WM_RBUTTONUP, bounded pet proxy only
                CancelActiveDrag();
                contextRequested();
                return;
            case WmNcHitTest when enabled:
                message.Result = HtClient;
                return;
            case WmMouseActivate:
                message.Result = MaNoActivate;
                return;
            case WmLeftButtonDown when enabled:
                var localX = unchecked((short)((long)message.LParam & 0xffff));
                var localY = unchecked((short)(((long)message.LParam >> 16) & 0xffff));
                if (NativeMethods.GetCursorPos(out dragOrigin) && beginDrag(localX, localY) && completePointer is null)
                {
                    _ = NativeMethods.SetCapture(Handle);
                    dragging = GetCapture() == Handle;
                    if (!dragging) cancelDrag();
                }

                return;
            case WmMouseMove when dragging:
                if (NativeMethods.GetCursorPos(out var current))
                {
                    moveDrag(current.X - dragOrigin.X, current.Y - dragOrigin.Y);
                }

                return;
            case WmLeftButtonUp when dragging:
                if (completePointer is not null)
                {
                    if (NativeMethods.GetCursorPos(out var end))
                        completePointer(end.X - dragOrigin.X, end.Y - dragOrigin.Y);
                    else CancelActiveDrag();
                }
                else
                {
                    ReleasePointerCapture();
                    completeDrag();
                }
                return;
            case WmCaptureChanged when dragging && GetCapture() != Handle:
                dragging = false;
                cancelDrag();
                return;
            case 0x02E0 when completePointer is not null: // WM_DPICHANGED
                cancelDrag();
                base.WndProc(ref message);
                return;
            default:
                base.WndProc(ref message);
                return;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        inputPriorityTimer.Stop();
        inputPriorityTimer.Dispose();
        CancelActiveDrag();
        DestroyHandle();
    }

    public void CancelDrag() => CancelActiveDrag();

    public bool TryCapturePointer()
    {
        if (disposed || !enabled) return false;
        _ = NativeMethods.SetCapture(Handle);
        return dragging = GetCapture() == Handle;
    }

    public void ReleasePointerCapture()
    {
        dragging = false;
        if (GetCapture() == Handle) _ = NativeMethods.ReleaseCapture();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetCapture();

    private void OnInputPriorityTimerTick(object? sender, EventArgs e) => RefreshInputPriority();

    private void RefreshInputPriority()
    {
        if (disposed)
        {
            return;
        }

        var cursorAvailable = NativeMethods.GetCursorPos(out var cursor);
        var cursorWithinBounds = cursorAvailable &&
            NativeMethods.IsPointWithinWindow(Handle, cursor.X, cursor.Y);
        var querySucceeded = false;
        var iconHit = false;
        if (enabled && !dragging && cursorWithinBounds)
        {
            querySucceeded = NativeMethods.TryIsDesktopIconAtPoint(
                target,
                cursor.X,
                cursor.Y,
                out iconHit);
        }

        var shouldShow = dragging || DesktopIconPriorityPolicy.ShouldShowInputProxy(
            enabled,
            cursorAvailable ? cursorWithinBounds : true,
            querySucceeded,
            iconHit);
        if (shouldShow == isWindowShown)
        {
            return;
        }

        _ = NativeMethods.ShowWindow(Handle, shouldShow ? SwShowNoActivate : SwHide);
        isWindowShown = shouldShow;
    }

    private void CancelActiveDrag()
    {
        if (!dragging)
        {
            return;
        }

        ReleasePointerCapture();
        cancelDrag();
    }
}
