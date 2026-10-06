using Dango.Desktop.AppShell;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.DesktopHost;

public interface IDesktopDisplayModeControl
{
    void SetMode(DesktopDisplayMode mode);
}

public interface IDesktopHostSwitchGuard
{
    string? GetSwitchBlockReason();
}

public interface IPhysicsDebugControl
{
    bool IsDebugSkeletonVisible { get; }

    void SetDebugSkeletonVisible(bool isVisible);
}

public interface IDangoPopulationControl
{
    int DangoCount { get; }

    bool AddDango();

    bool RemoveDango();
}

public interface IDangoPhysicsTuningControl
{
    DangoSelfPhysicsParameters PhysicsParameters { get; }

    void ApplyPhysicsParameters(DangoSelfPhysicsParameters parameters);

    void RestoreDefaultPhysicsParameters();

    void RegenerateDango();
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

public sealed class DesktopHostCoordinator : IDesktopHostControl, IDesktopDisplayModeControl, IDesktopLayeringControl, IKeyboardControl, IPhysicsDebugControl, IDangoPopulationControl, IDangoPhysicsTuningControl, IDangoSceneControl
{
    private readonly IDesktopHostControl wallpaper;
    private readonly IDesktopHostControl window;
    private IDesktopHostControl active;
    private DesktopDisplayMode activeMode = DesktopDisplayMode.Wallpaper;
    private bool switchInProgress;
    private bool roomPresentationAllowed = true;
    private DesktopDisplayMode? roomDisplayModeLock;

    internal void SetRoomDisplayModeLock(DesktopDisplayMode? mode) => roomDisplayModeLock = mode;
    private readonly StackedDangoSceneRuntime? runtime;

    public DesktopHostCoordinator(IDesktopHostControl wallpaper, IDesktopHostControl window,
        StackedDangoSceneRuntime? runtime = null)
    {
        this.wallpaper = wallpaper ?? throw new ArgumentNullException(nameof(wallpaper));
        this.window = window ?? throw new ArgumentNullException(nameof(window));
        active = wallpaper;
        this.runtime = runtime;
    }

    public DesktopHostSnapshot GetSnapshot()
    {
        var snapshot = active.GetSnapshot();
        return runtime is null ? snapshot : snapshot with
        {
            LastError = runtime.LastError ?? runtime.AttachmentError ?? runtime.QuietIdleError ?? snapshot.LastError,
            VerificationStatus = runtime.InitializationState switch
            {
                SceneInitializationState.Ready => "场景已就绪",
                SceneInitializationState.Unavailable => "场景不可用",
                _ => "场景尚未初始化",
            },
        };
    }

    public void SetVisibility(bool isVisible)
    {
        if (isVisible && !roomPresentationAllowed)
            throw new InvalidOperationException("进入房间后才能在桌面显示团子。");
        if (isVisible && runtime is { RequiresInitialization: true } &&
            runtime.InitializationState != SceneInitializationState.Ready)
            throw new InvalidOperationException(runtime.LastError ?? "场景尚未初始化，请重试。");
        active.SetVisibility(isVisible);
    }

    public SceneInitializationState InitializationState =>
        runtime?.InitializationState ?? SceneInitializationState.Ready;
    public bool CanShowDesktop => roomPresentationAllowed;
    public void SetRoomPresentationAllowed(bool allowed)
    {
        if (!allowed) active.SetVisibility(false);
        roomPresentationAllowed = allowed;
    }
    public string HatPresetId => runtime?.CurrentHatPresetId ?? "sprout";
    public bool TryApplyHatPreset(string id) => runtime?.TryApplyHatPreset(id) == true;
    public bool RetryInitialization()
    {
        if (runtime is null || !runtime.TryInitialize()) return false;
        if (roomPresentationAllowed) SetVisibility(true);
        return true;
    }

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

    public bool IsDebugSkeletonVisible =>
        active is IPhysicsDebugControl debugControl && debugControl.IsDebugSkeletonVisible;

    public void SetDebugSkeletonVisible(bool isVisible)
    {
        ApplyDebugSkeleton(wallpaper, isVisible);
        ApplyDebugSkeleton(window, isVisible);
    }

    public int DangoCount =>
        active is IDangoPopulationControl populationControl
            ? populationControl.DangoCount
            : 1;

    public bool AddDango() =>
        active is IDangoPopulationControl populationControl && populationControl.AddDango();

    public bool RemoveDango() =>
        active is IDangoPopulationControl populationControl && populationControl.RemoveDango();

    public DangoSelfPhysicsParameters PhysicsParameters =>
        active is IDangoPhysicsTuningControl tuningControl
            ? tuningControl.PhysicsParameters
            : DangoSelfPhysicsParameters.CreateDefault();

    public void ApplyPhysicsParameters(DangoSelfPhysicsParameters parameters)
    {
        ApplyPhysicsTuning(wallpaper, parameters);
        ApplyPhysicsTuning(window, parameters);
    }

    public void RestoreDefaultPhysicsParameters()
    {
        if (active is not IDangoPhysicsTuningControl tuningControl)
        {
            throw new NotSupportedException("显示宿主不支持团子物理参数。");
        }
        tuningControl.RestoreDefaultPhysicsParameters();
    }

    public void RegenerateDango()
    {
        if (active is not IDangoPhysicsTuningControl tuningControl)
        {
            throw new NotSupportedException("显示宿主不支持重新生成团子。");
        }
        tuningControl.RegenerateDango();
    }

    public void SetMode(DesktopDisplayMode mode)
    {
        if (roomDisplayModeLock is { } required && mode != required)
            throw new DesktopHostSwitchException("桌宠房间使用壁纸层，请从房门返回门廊后选择普通房。");
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

    private static void ApplyDebugSkeleton(IDesktopHostControl host, bool isVisible)
    {
        if (host is not IPhysicsDebugControl debugControl)
        {
            throw new NotSupportedException("显示宿主不支持物理骨架设置。");
        }

        debugControl.SetDebugSkeletonVisible(isVisible);
    }

    private static void ApplyPhysicsTuning(IDesktopHostControl host, DangoSelfPhysicsParameters parameters)
    {
        if (host is not IDangoPhysicsTuningControl tuningControl)
        {
            throw new NotSupportedException("显示宿主不支持团子物理参数。");
        }
        tuningControl.ApplyPhysicsParameters(parameters);
    }
}
