namespace Dango.Desktop.DesktopHost.Interaction;

public enum DragCancellationReason
{
    Escape,
    LostCapture,
    WindowDeactivated,
    WindowClosing,
}

public sealed class DesktopInteractionController
{
    public bool IsDragging { get; private set; }

    public DragCancellationReason? LastCancellationReason { get; private set; }

    public bool TryBeginDrag(DesktopHitTarget target)
    {
        if (IsDragging || target != DesktopHitTarget.Dango)
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
