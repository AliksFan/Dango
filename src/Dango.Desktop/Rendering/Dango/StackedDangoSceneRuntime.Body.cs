using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Rendering.Visuals;

public sealed partial class StackedDangoSceneRuntime
{
    private DangoBodyControls physicsBody = DangoBodyControls.Default;

    /// <summary>The body controls the running world was actually built with.</summary>
    public DangoBodyControls PhysicsBody => physicsBody;
    public string? BodyPreviewError { get; private set; }

    /// <summary>Node radius of a live Dango; differs from the default rig once body size changes.</summary>
    internal double NodeRadiusOf(DangoEntityId id) =>
        (scene ?? throw new InvalidOperationException("本地物理场景尚未创建。")).ConfigurationOf(id).NodeRadius;

    private sealed record BodyCalibration(PhysicsPresentationFrame Baseline, double Width, double GroundOffset);

    // A temporary single-body world settles for four seconds; its neutral pose seeds the eye, hat
    // and accessory baselines. The world is released before anything else uses the result.
    private BodyCalibration Calibrate(DangoSelfPhysicsParameters configuration)
    {
        CalibrationStepCount = 0;
        using var calibration = new DesktopPhysicsScene(nativeLibraryPath, new(0, 0, 160, 120), configuration);
        for (var i = 0; i < 240; i++)
        {
            var frame = calibration.Step(TimeSpan.FromSeconds(1d / 60));
            CalibrationStepCount++;
            if (!frame.Physics.Succeeded)
                throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure, "眼帽标定失败。", frame.Physics.ErrorCode);
        }
        var baseline = calibration.LastFrame!.Bodies.Single().Presentation;
        var geometry = DangoStreamGeometryFactory.TryCreate(baseline.Contour, 160, 120);
        if (!geometry.Succeeded) throw new InvalidOperationException("中性身体轮廓不可用。");
        return new(baseline, geometry.Geometry!.Bounds.Width,
            calibration.GroundY - (calibration.LastFrame.Bodies.Single().Bounds.Y + geometry.Geometry.Bounds.Bottom));
    }

    /// <summary>
    /// Rebuilds the world so the local Dango uses <paramref name="body"/> and lands again. The new
    /// world and calibration are fully prepared (including a successful first step) before the old
    /// world is released; any failure keeps the old world, the old body and its state untouched.
    /// </summary>
    public bool TryApplyBody(DangoBodyControls body)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(body);
        BodyPreviewError = null;
        if (scene is null || InitializationState != SceneInitializationState.Ready)
        {
            if (RequiresInitialization) { BodyPreviewError = "场景尚未初始化，无法预览身体。"; return false; }
            physicsBody = body;
            return true;
        }
        if (LastError is not null) { BodyPreviewError = "物理场景处于故障状态，请先恢复团子。"; return false; }
        if (!TryDetachOrdinaryFurniture())
        {
            BodyPreviewError = Furniture?.Status ?? "请先确认家具提交结果，再预览身体。";
            return false;
        }

        var configuration = DangoBodyMaterial.Configure(body);
        var center = hostOrigin ?? new LogicalPoint(80, 60);
        var others = scene.EntityIds.Skip(1).Select(id => colorSlots[id]).ToArray();
        BodyCalibration? calibration = null;
        DesktopPhysicsScene? next = null;
        var nextSlots = new Dictionary<DangoEntityId, int>();
        try
        {
            if (requiresAttachments) calibration = Calibrate(configuration);
            next = new DesktopPhysicsScene(nativeLibraryPath, new(center.X - 80, center.Y - 60, 160, 120), configuration);
            nextSlots.Add(next.SelectedEntity, 0);
            foreach (var slot in others) nextSlots.Add(next.Add(slot == 1 ? -2.8 : 2.8, 0), slot);
            if (previewJumpSpeedScale != 1) next.SetPreviewJumpSpeedScale(previewJumpSpeedScale);
            if (unboundedHorizontalPreview) next.SetUnboundedHorizontalPreview(true);
            else if (localHorizontalBounds is { } limits) next.SetHorizontalBounds(limits.Left, limits.Right);
            var first = next.Step(TimeSpan.FromSeconds(1d / 60));
            if (!first.Physics.Succeeded)
                throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure, "新身体首帧失败。", first.Physics.ErrorCode);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            next?.Dispose();
            BodyPreviewError = exception is DangoPhysicsException physics
                ? $"身体预览失败：{physics.Kind}（{physics.NativeErrorCode}），仍保留原身体。"
                : $"身体预览失败：{exception.GetType().Name}，仍保留原身体。";
            return false;
        }

        // Hand over: nothing below can fail, so the old world is released only after success.
        ClearPhysicalInput();
        Furniture?.Dispose();
        Furniture = null;
        scene.Dispose();
        scene = next;
        scene.SetPaused(!running);
        colorSlots.Clear();
        foreach (var (id, slot) in nextSlots) colorSlots.Add(id, slot);
        pendingSpawns.Clear();
        if (calibration is not null)
        {
            attachments?.Dispose();
            attachments = new(calibration.Baseline);
            attachments.SetPaused(!running);
            (NeutralBodyVisibleWidth, NeutralBodyGroundOffset) = (calibration.Width, calibration.GroundOffset);
        }
        physicsBody = body;
        QuietIdle.Reset();
        Doze.Reset();
        Gait.Reset();
        Wander.Cancel();
        accessoryScene?.Reset();
        LastFrame = null;
        layoutPending = true;
        SynchronizeAppearance();
        EnsureOrdinaryFurniture();
        return true;
    }
}
