using Dango.Core.Coordinates;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.DesktopHost.Interaction;

internal sealed class LightInteractionSession(StackedDangoSceneRuntime runtime)
{
    private readonly LightInteractionController controller = new();
    private LightInteractionTarget? target;
    private Func<bool>? tryCapture;
    private Action? releaseCapture;
    private Func<bool>? canContinue;
    private LogicalPoint lastPoint;
    private bool dragging;
    private long lightPress;

    public bool IsActive => target is not null;

    public bool TryBegin(DangoEntityId id, LogicalPoint point, long nowMilliseconds, bool shift,
        Func<bool> tryCapture, Action releaseCapture, Func<bool> canContinue)
    {
        ArgumentNullException.ThrowIfNull(tryCapture);
        ArgumentNullException.ThrowIfNull(releaseCapture);
        ArgumentNullException.ThrowIfNull(canContinue);
        if (IsActive || !canContinue() || runtime.IsDragging || !runtime.LightInteractionsEnabled || runtime.GetLightInteractionTarget(id) is not { } next ||
            !TryCurrentBounds(next, out var beginBounds) || !beginBounds.Contains(point)) return false;
        if (!controller.TryBegin(next, point, nowMilliseconds, shift, out _)) return false;

        target = next;
        lightPress = runtime.BeginLightPress(next);
        lastPoint = point;
        this.tryCapture = tryCapture;
        this.releaseCapture = releaseCapture;
        this.canContinue = canContinue;
        if (tryCapture()) return true;
        ClearState();
        releaseCapture();
        return false;
    }

    public void Move(LogicalPoint point, long nowMilliseconds, bool shift)
    {
        if (!IsActive) return;
        lastPoint = point;
        if (dragging)
        {
            if (canContinue?.Invoke() != true) { Cancel(); return; }
            runtime.MovePhysicalDrag(point);
            return;
        }
        if (canContinue?.Invoke() != true) { Cancel(); return; }
        if (!TryCurrentBounds(target!.Value, out var bounds)) { Cancel(); return; }
        Handle(controller.Move(controller.SessionId, point, nowMilliseconds, shift, bounds), point);
    }

    public void End(LogicalPoint point, long nowMilliseconds, bool shift)
    {
        if (!IsActive) return;
        lastPoint = point;
        if (dragging)
        {
            var release = releaseCapture;
            ClearState();
            runtime.EndPhysicalDrag();
            release?.Invoke();
            return;
        }
        var result = TryCurrentBounds(target!.Value, out var bounds)
            ? controller.End(controller.SessionId, point, nowMilliseconds, shift, bounds)
            : controller.End(controller.SessionId, point, nowMilliseconds, shift, null);
        var releaseCaptureNow = releaseCapture;
        var current = target;
        ClearState();
        if (current is { } value && result is LightInteractionResult.Click or LightInteractionResult.Pet)
            _ = runtime.TryReact(value, result == LightInteractionResult.Pet);
        releaseCaptureNow?.Invoke();
    }

    public void Cancel()
    {
        if (!IsActive) return;
        var release = releaseCapture;
        var wasDragging = dragging;
        ClearState();
        if (wasDragging) runtime.EndPhysicalDrag();
        runtime.CancelLightFeedback();
        release?.Invoke();
    }

    public void RefreshButtons(long nowMilliseconds, bool shift, bool leftDown)
    {
        if (!IsActive) return;
        if (!leftDown) { Cancel(); return; }
        if (!dragging)
        {
            if (!TryCurrentBounds(target!.Value, out var bounds)) { Cancel(); return; }
            Handle(controller.Move(controller.SessionId, lastPoint, nowMilliseconds, shift, bounds), lastPoint);
        }
    }

    private void Handle(LightInteractionResult result, LogicalPoint point)
    {
        if (result == LightInteractionResult.None)
        {
            if (!controller.IsPressed) CompleteGesture();
            return;
        }
        if (result == LightInteractionResult.Cancelled) { Cancel(); return; }
        if (result is LightInteractionResult.Click or LightInteractionResult.Pet)
        {
            if (target is { } current) _ = runtime.TryReact(current, result == LightInteractionResult.Pet);
            return;
        }
        if (result != LightInteractionResult.BeginDrag || target is not { } dragTarget) return;

        var release = releaseCapture;
        var capture = tryCapture;
        var continueCheck = canContinue;
        ClearState();
        release?.Invoke();
        runtime.CancelLightFeedback();
        runtime.BeginPhysicalDrag(dragTarget.Entity, point);
        if (runtime.IsDragging && runtime.IsRunning && runtime.LastError is null && runtime.EntityIds.Contains(dragTarget.Entity) &&
            continueCheck?.Invoke() == true && capture?.Invoke() == true)
        {
            target = dragTarget;
            dragging = true;
            tryCapture = capture;
            releaseCapture = release;
            canContinue = continueCheck;
            return;
        }
        runtime.EndPhysicalDrag();
        release?.Invoke();
    }

    private void CompleteGesture()
    {
        var release = releaseCapture;
        ClearState();
        release?.Invoke();
    }

    private bool TryCurrentBounds(LightInteractionTarget candidate, out LogicalRect bounds)
    {
        bounds = default;
        if (!runtime.IsLightInteractionTargetCurrent(candidate) || runtime.LastFrame is not { } frame) return false;
        var index = Enumerable.Range(0, frame.EntityIds.Count).FirstOrDefault(i => frame.EntityIds[i] == candidate.Entity);
        if (frame.EntityIds.Count == 0 || frame.EntityIds[index] != candidate.Entity) return false;
        bounds = DangoHostFrameBinding.Bounds(frame, index);
        return true;
    }

    private void ClearState()
    {
        if (target is { } current) runtime.EndLightPress(current, lightPress);
        lightPress = 0;
        controller.Clear();
        target = null;
        dragging = false;
        tryCapture = null;
        releaseCapture = null;
        canContinue = null;
    }
}
