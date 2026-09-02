using Dango.Desktop.AppShell;

namespace Dango.Desktop.DesktopHost;

public enum DesktopHostMode
{
    Overlay,
    WorkerW,
}

public interface IDesktopHostModeControl
{
    void SetMode(DesktopHostMode mode);
}

public sealed class DesktopHostCoordinator : IDesktopHostControl, IDesktopHostModeControl
{
    private readonly IDesktopHostControl overlay;
    private readonly IDesktopHostControl workerW;
    private IDesktopHostControl active;
    private DesktopHostMode activeMode = DesktopHostMode.Overlay;

    public DesktopHostCoordinator(IDesktopHostControl overlay, IDesktopHostControl workerW)
    {
        this.overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        this.workerW = workerW ?? throw new ArgumentNullException(nameof(workerW));
        active = overlay;
    }

    public DesktopHostSnapshot GetSnapshot() => active.GetSnapshot();

    public void SetVisibility(bool isVisible) => active.SetVisibility(isVisible);

    public void SetInteractionEnabled(bool isEnabled) => active.SetInteractionEnabled(isEnabled);

    public string CreateDiagnosticSummary() => active.CreateDiagnosticSummary();

    public void SetMode(DesktopHostMode mode)
    {
        if (mode == activeMode)
        {
            return;
        }

        var previous = active;
        var previousMode = activeMode;
        var previousSnapshot = previous.GetSnapshot();
        var candidate = mode == DesktopHostMode.Overlay ? overlay : workerW;
        try
        {
            previous.SetVisibility(false);
            candidate.SetInteractionEnabled(previousSnapshot.IsInteractionEnabled);
            candidate.SetVisibility(previousSnapshot.IsVisible);
            var candidateSnapshot = candidate.GetSnapshot();
            if (candidateSnapshot.IsVisible != previousSnapshot.IsVisible ||
                candidateSnapshot.IsInteractionEnabled != previousSnapshot.IsInteractionEnabled)
            {
                throw new InvalidOperationException("目标宿主未确认切换后的显示或交互状态。");
            }

            active = candidate;
            activeMode = mode;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            try
            {
                candidate.SetVisibility(false);
                previous.SetInteractionEnabled(previousSnapshot.IsInteractionEnabled);
                previous.SetVisibility(previousSnapshot.IsVisible);
            }
            catch (Exception rollbackException) when (rollbackException is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Unable to roll back desktop host switch: {rollbackException.GetType().Name}");
            }

            active = previous;
            activeMode = previousMode;
            throw new InvalidOperationException("桌面宿主切换失败，已恢复原宿主。", exception);
        }
    }
}
