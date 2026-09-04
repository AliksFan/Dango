using Dango.Desktop.AppShell;

namespace Dango.Desktop.DesktopHost;

public interface IDesktopDisplayModeControl
{
    void SetMode(DesktopDisplayMode mode);
}

public interface IDesktopHostSwitchGuard
{
    string? GetSwitchBlockReason();
}

public sealed class DesktopHostSwitchException : InvalidOperationException
{
    public DesktopHostSwitchException(string safeReason, Exception? innerException = null)
        : base("桌面宿主切换失败。", innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeReason);
        SafeReason = safeReason;
    }

    public string SafeReason { get; }
}

public sealed class DesktopHostCoordinator : IDesktopHostControl, IDesktopDisplayModeControl, IDesktopLayeringControl, IKeyboardControl
{
    private readonly IDesktopHostControl wallpaper;
    private readonly IDesktopHostControl window;
    private IDesktopHostControl active;
    private DesktopDisplayMode activeMode = DesktopDisplayMode.Wallpaper;
    private bool switchInProgress;

    public DesktopHostCoordinator(IDesktopHostControl wallpaper, IDesktopHostControl window)
    {
        this.wallpaper = wallpaper ?? throw new ArgumentNullException(nameof(wallpaper));
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        active = wallpaper;
    }

    public DesktopHostSnapshot GetSnapshot() => active.GetSnapshot();

    public void SetVisibility(bool isVisible) => active.SetVisibility(isVisible);

    public void SetInteractionEnabled(bool isEnabled) => active.SetInteractionEnabled(isEnabled);

    public string CreateDiagnosticSummary() => active.CreateDiagnosticSummary();

    public DesktopLayeringSettings GetLayeringSettings() =>
        wallpaper is IDesktopLayeringControl layeringControl
            ? layeringControl.GetLayeringSettings()
            : DesktopLayeringSettings.CreateDefault();

    public void ApplyLayeringSettings(DesktopLayeringSettings settings)
    {
        if (wallpaper is not IDesktopLayeringControl layeringControl)
        {
            throw new NotSupportedException("壁纸层宿主不支持对象分层设置。");
        }

        layeringControl.ApplyLayeringSettings(settings);
    }

    public KeyboardControlSettings GetKeyboardControlSettings() =>
        active is IKeyboardControl keyboardControl
            ? keyboardControl.GetKeyboardControlSettings()
            : KeyboardControlSettings.CreateDefault();

    public void ApplyKeyboardControlSettings(KeyboardControlSettings settings)
    {
        ApplyKeyboardControl(wallpaper, settings);
        ApplyKeyboardControl(window, settings);
    }

    public void SetMode(DesktopDisplayMode mode)
    {
        if (mode == activeMode)
        {
            return;
        }

        if (switchInProgress)
        {
            throw new DesktopHostSwitchException("已有宿主切换正在进行。");
        }

        if (active is IDesktopHostSwitchGuard guard && guard.GetSwitchBlockReason() is { } blockReason)
        {
            throw new DesktopHostSwitchException(blockReason);
        }

        switchInProgress = true;
        var previous = active;
        var previousMode = activeMode;
        var previousSnapshot = previous.GetSnapshot();
        var candidate = mode == DesktopDisplayMode.Wallpaper ? wallpaper : window;
        try
        {
            previous.SetVisibility(false);
            candidate.SetInteractionEnabled(previousSnapshot.IsInteractionEnabled);
            candidate.SetVisibility(previousSnapshot.IsVisible);
            var candidateSnapshot = candidate.GetSnapshot();
            if (candidateSnapshot.IsVisible != previousSnapshot.IsVisible ||
                candidateSnapshot.IsInteractionEnabled != previousSnapshot.IsInteractionEnabled)
            {
                throw new DesktopHostSwitchException(
                    candidateSnapshot.LastError ?? "目标宿主未确认切换后的显示或交互状态。");
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
            var safeReason = exception is DesktopHostSwitchException switchException
                ? switchException.SafeReason
                : "目标宿主发生内部错误，已恢复原宿主。";
            throw new DesktopHostSwitchException(safeReason, exception);
        }
        finally
        {
            switchInProgress = false;
        }
    }

    private static void ApplyKeyboardControl(IDesktopHostControl host, KeyboardControlSettings settings)
    {
        if (host is not IKeyboardControl keyboardControl)
        {
            throw new NotSupportedException("显示宿主不支持键盘控制设置。");
        }

        keyboardControl.ApplyKeyboardControlSettings(settings);
    }
}
