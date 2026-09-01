namespace Dango.Desktop.DesktopHost.Overlay;

public enum DragCancellationReason
{
    Escape,
    LostCapture,
    WindowDeactivated,
    WindowClosing,
}

public sealed class OverlayInteractionController
{
    public bool IsDragging { get; private set; }

    public DragCancellationReason? LastCancellationReason { get; private set; }

    public bool TryBeginDrag(OverlayHitTarget target)
    {
        if (IsDragging || target != OverlayHitTarget.Dango)
        {
            return false;
        }

        IsDragging = true;
        LastCancellationReason = null;
        return true;
    }

    public bool CompleteDrag()
    {
        if (!IsDragging)
        {
            return false;
        }

        IsDragging = false;
        return true;
    }

    public bool CancelDrag(DragCancellationReason reason)
    {
        if (!IsDragging)
        {
            return false;
        }

        IsDragging = false;
        LastCancellationReason = reason;
        return true;
    }
}
