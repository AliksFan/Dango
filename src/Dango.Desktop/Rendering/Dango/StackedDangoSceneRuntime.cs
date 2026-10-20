using System.IO;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Dango.Desktop.Rendering.Visuals;

public enum SceneInitializationState { NotStarted, Ready, Unavailable }

public sealed partial class StackedDangoSceneRuntime : IDisposable
{
    public const int MinimumDangoCount = 1;
    public const int MaximumDangoCount = 3;

    private readonly string nativeLibraryPath;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<DangoEntityId, int> colorSlots = [];
    private readonly Dictionary<DangoEntityId, LogicalRect> pendingSpawns = [];
    private DesktopPhysicsScene? scene;
    private bool beltRoom;
    internal bool UsesBeltRoom => beltRoom;
    internal DangoBeltRoomFrame? LastBeltRoomFrame => scene?.BeltRoomFrame;
    private LogicalPoint? hostOrigin;
    private LogicalRect? viewport;
    private (double Left, double Right)? localHorizontalBounds;
    private bool unboundedHorizontalPreview;
    private double previewJumpSpeedScale = 1;
    private bool layoutPending;
    private DirectionalControlIntent directionalIntent;
    private bool running;
    private bool disposed;
    internal object? FrameDriver { get; set; }
    internal (string? Physics, long Attachments, string? Idle, string? Interaction) FrameFaultState =>
        (LastError, attachments?.FaultRevision ?? 0, QuietIdleError, LightInteractionError);
    internal static bool HasNewFrameFault(
        (string? Physics, long Attachments, string? Idle, string? Interaction) before,
        (string? Physics, long Attachments, string? Idle, string? Interaction) after) =>
        after.Attachments > before.Attachments ||
        after.Physics is not null && after.Physics != before.Physics ||
        after.Idle is not null && after.Idle != before.Idle ||
        after.Interaction is not null && after.Interaction != before.Interaction;
    private AttachmentSceneController? attachments;
    private bool requiresAttachments;
    public bool RequiresInitialization { get; private init; }
    public SceneInitializationState InitializationState { get; private set; }
    public int CalibrationStepCount { get; private set; }
    internal double? NeutralBodyVisibleWidth { get; private set; }
    internal double NeutralBodyGroundOffset { get; private set; }
    public bool AttachmentsEnabled => attachments is not null;
    public string CurrentHatPresetId { get; private set; } = "sprout";
    public HatAppearance CurrentHatAppearance => DangoHatPresets.GetAppearance(CurrentHatPresetId);
    public HatAppearance DisplayHatAppearance => CurrentHatAppearance;
    public double DisplayHatSize => AttachmentsEnabled ? 1.35 : 1;
    public DangoAppearanceConfiguration EffectiveAppearance { get; private set; } = DangoAppearanceConfiguration.Default;
    public Brush? BodyPreviewFill { get; private set; }
    public Brush? EyePreviewFill { get; private set; }
    public DangoEntityId? AppearanceEntity => scene?.EntityIds.FirstOrDefault();

    public void SetAppearance(DangoAppearanceConfiguration configuration)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(configuration);
        if (beltRoom && scene is not null && configuration.Body != EffectiveAppearance.Body)
            throw new InvalidOperationException("请在房间交接暂停后应用新的身体参数。");
        EffectiveAppearance = configuration;
        BodyPreviewFill = AppearanceBrush(configuration.BodyColor);
        EyePreviewFill = AppearanceBrush(configuration.EyeColor);
        SynchronizeAppearance();
    }

    private static Brush AppearanceBrush(OpaqueSrgbColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private void SynchronizeAppearance()
    {
        if (attachments is null || scene is null) return;
        attachments.Synchronize(scene.EntityIds);
        if (scene.EntityIds.Count > 0) attachments.SetEyeLayout(scene.EntityIds[0], EffectiveAppearance.EyeLayout);
    }
    public void SetBodyPreviewColor(Color color)
    {
        VerifyAccess();
        SetAppearance(new(new(color.R, color.G, color.B), EffectiveAppearance.EyeColor, EffectiveAppearance.EyeLayout));
    }
    public string? AttachmentError => attachments?.LastError;
    public event Action? HatPresetChanged;
    internal QuietIdleSceneController QuietIdle { get; } = new();
    internal IdleDozeController Doze { get; } = new();
    private void CancelDoze()
    {
        Doze.Cancel(intent => LastError is not null || attachments is null || scene?.Count != 1 ||
            !attachments.IsHealthy(scene.EntityIds[0]) || attachments.SubmitQuietIdle(scene.EntityIds[0], intent).Accepted);
        if (LastFrame?.Bodies.Any(body => body.DozeSeconds is not null) == true)
            LastFrame = LastFrame with { Bodies = LastFrame.Bodies.Select(body => body with { DozeSeconds = null }).ToArray() };
    }
    internal ShortWanderController Wander { get; } = new();
    internal GaitController Gait { get; } = new();
    internal bool GaitEnabled { get; set; } = true;
    public string? GaitError => Gait.Error;
    private double? wanderScreenWidth;
    internal void UpdateWanderScreenWidth(object owner, double? width)
    {
        VerifyAccess();
        if (!ReferenceEquals(quietIdleHost, owner)) return;
        if (width is { } value && (!double.IsFinite(value) || value <= 0))
            throw new ArgumentOutOfRangeException(nameof(width));
        if (wanderScreenWidth == width) return;
        Gait.InvalidateSampling();
        CancelDoze();
        Wander.Cancel();
        wanderScreenWidth = width;
    }
    public bool HasAutonomousAction => HasQuietIdleAction || Wander.Active || Doze.Active;
    private object? quietIdleHost;
    private bool quietIdleHostEnabled;
    private readonly Dictionary<DangoEntityId, long> lightPresses = [];
    private long nextLightPress;

    internal void AttachQuietIdleHost(object owner, bool enabled)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(owner);
        if (!LightInteractionsEnabled || !running || quietIdleHost is not null && !ReferenceEquals(quietIdleHost, owner)) return;
        quietIdleHost = owner;
        UpdateQuietIdleHost(owner, enabled);
    }

    internal void UpdateQuietIdleHost(object owner, bool enabled)
    {
        VerifyAccess();
        if (!ReferenceEquals(quietIdleHost, owner) || quietIdleHostEnabled == enabled) return;
        quietIdleHostEnabled = enabled;
        Gait.InvalidateSampling();
        CancelQuietIdle();
    }

    internal long BeginLightPress(LightInteractionTarget target)
    {
        VerifyAccess();
        if (!IsLightInteractionTargetCurrent(target)) return 0;
        CancelDoze();
        Wander.Cancel();
        QuietIdle.Cancel(target.Entity, LastError is null ? attachments : null);
        var session = ++nextLightPress;
        lightPresses[target.Entity] = session;
        return session;
    }

    internal void EndLightPress(LightInteractionTarget target, long session)
    {
        VerifyAccess();
        if (session == 0 || target.Generation != interactionGeneration ||
            !lightPresses.TryGetValue(target.Entity, out var current) || current != session) return;
        lightPresses.Remove(target.Entity);
        QuietIdle.Cancel(target.Entity, LastError is null ? attachments : null);
    }

    private void CancelQuietIdle()
    {
        CancelDoze();
        Wander.Cancel();
        QuietIdle.CancelAll(LastError is null ? attachments : null);
    }
    public bool HasQuietIdleAction => QuietIdle.HasActive;
    public string? QuietIdleError => QuietIdle.Error ?? Doze.Error;

    private static long nextInteractionGeneration;
    private long interactionGeneration = Interlocked.Increment(ref nextInteractionGeneration);
    private readonly Dictionary<DangoEntityId, LightFeedback> lightFeedback = [];
    private sealed class LightFeedback(LightInteractionTarget target, bool petting)
    {
        public LightInteractionTarget Target { get; } = target;
        public bool Petting { get; } = petting;
        public double Elapsed;
        public double Cooldown = petting ? 1 : 0.3;
        public bool Active = true;
        public AttachmentChannel Channels => Petting
            ? AttachmentChannel.LeftEyePose | AttachmentChannel.RightEyePose
            : AttachmentChannel.LeftBlink | AttachmentChannel.RightBlink;
    }

    public bool LightInteractionsEnabled { get; private init; }
    public bool HasLightFeedback => lightFeedback.Values.Any(value => value.Active);
    public string? LightInteractionError { get; private set; }

    public LightInteractionTarget? GetLightInteractionTarget(DangoEntityId id)
    {
        VerifyAccess();
        return LightInteractionsEnabled && running && LastError is null && scene?.EntityIds.Contains(id) == true
            ? new(id, interactionGeneration) : null;
    }

    public bool IsLightInteractionTargetCurrent(LightInteractionTarget target) =>
        GetLightInteractionTarget(target.Entity) == target;

    public bool TryReact(LightInteractionTarget target, bool petting)
    {
        VerifyAccess();
        if (!IsLightInteractionTargetCurrent(target) || IsDragging || HasDirectionalInput ||
            LightInteractionError is not null || attachments is null || !attachments.IsHealthy(target.Entity)) return false;
        if (lightFeedback.TryGetValue(target.Entity, out var current) && (current.Active || current.Cooldown > 1e-9)) return false;
        Wander.Cancel();
        CancelDoze();
        if (Doze.Error is not null || !QuietIdle.Cancel(target.Entity, attachments)) return false;
        var result = attachments.Submit(target.Entity, petting
            ? new SetEyes(EyeSelection.Both, 0.35, EyeShape.Soft, 1)
            : Blink.Default(EyeSelection.Both));
        if (!result.Accepted) return false;
        lightFeedback[target.Entity] = new(target, petting);
        return true;
    }

    public void CancelLightFeedback()
    {
        VerifyAccess();
        foreach (var feedback in lightFeedback.Values)
        {
            // A failed world freezes its ledger until explicit Reset. Do not route
            // cleanup through the public submission method's health guard.
            if (!feedback.Active || LastError is not null || attachments?.IsHealthy(feedback.Target.Entity) != true) continue;
            if (!attachments.Submit(feedback.Target.Entity, new Release(feedback.Channels)).Accepted)
                LightInteractionError = "互动取消失败；请使用恢复团子清除旧状态。";
        }
        lightFeedback.Clear();
    }

    private bool HasDirectionalInput => directionalIntent.Left != directionalIntent.Right || directionalIntent.Jump || directionalIntent.Squash || directionalIntent.Stretch;

    private void BeforeLightFeedbackStep()
    {
        foreach (var feedback in lightFeedback.Values)
        {
            if (!feedback.Active || feedback.Elapsed + 1e-9 < (feedback.Petting ? 0.6 : 0.13)) continue;
            if (feedback.Petting && attachments?.IsHealthy(feedback.Target.Entity) == true &&
                !attachments.Submit(feedback.Target.Entity, new Release(feedback.Channels)).Accepted)
                LightInteractionError = "互动结束失败；请使用恢复团子清除旧状态。";
            feedback.Active = false;
        }
    }

    private void AfterLightFeedbackStep(double seconds)
    {
        foreach (var feedback in lightFeedback.Values.ToArray())
        {
            if (attachments?.IsHealthy(feedback.Target.Entity) != true)
            {
                lightFeedback.Remove(feedback.Target.Entity);
                continue;
            }
            feedback.Elapsed += seconds;
            feedback.Cooldown = Math.Max(0, feedback.Cooldown - seconds);
            if (!feedback.Active && feedback.Cooldown <= 1e-9) lightFeedback.Remove(feedback.Target.Entity);
        }
    }

    public StackedDangoSceneRuntime(string? nativeLibraryPath = null, PhysicsPresentationFrame? attachmentBaseline = null)
    {
        this.nativeLibraryPath = nativeLibraryPath ?? Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        if (attachmentBaseline is not null) attachments = new(attachmentBaseline);
    }

    public static StackedDangoSceneRuntime CreateProduct(string? nativeLibraryPath = null) =>
        new(nativeLibraryPath) { requiresAttachments = true, RequiresInitialization = true, LightInteractionsEnabled = true };

    internal static StackedDangoSceneRuntime CreateBeltRoom(string nativeLibraryPath) =>
        new(nativeLibraryPath) { beltRoom = true, requiresAttachments = true, RequiresInitialization = true, LightInteractionsEnabled = true };

    // This is an explicit stopped lifecycle handoff, never a live parameter edit.
    internal void ConfigureBeltRoomAppearance(DangoAppearanceConfiguration saved)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(saved);
        if (!beltRoom || running || FrameDriver is not null)
            throw new InvalidOperationException("身体模型只能在房间交接暂停时配置。");
        if (scene is not null && saved.Body != EffectiveAppearance.Body)
        {
            ClearPhysicalInput();
            scene.Dispose(); scene = null;
            attachments?.Dispose(); attachments = null;
            colorSlots.Clear(); pendingSpawns.Clear();
            hostOrigin = null; LastFrame = null;
            InitializationState = SceneInitializationState.NotStarted;
            NeutralBodyVisibleWidth = null; NeutralBodyGroundOffset = 0;
            QuietIdle.Reset(); Doze.Reset(); Gait.Reset(); accessoryScene?.Reset();
        }
        SetAppearance(saved);
    }

    public static StackedDangoSceneRuntime CreateEyeHatReview(string? nativeLibraryPath = null, bool deferInitialization = false)
    {
        var runtime = new StackedDangoSceneRuntime(nativeLibraryPath) { requiresAttachments = true, RequiresInitialization = true };
        runtime.TryApplyHatPreset("top-hat");
        if (!deferInitialization && !runtime.TryInitialize())
        {
            var error = runtime.LastError;
            runtime.Dispose();
            throw new InvalidOperationException(error);
        }
        return runtime;
    }

    public bool TryApplyHatPreset(string presetId)
    {
        VerifyAccess();
        if (!DangoHatPresets.IsKnown(presetId)) return false;
        if (CurrentHatPresetId == presetId) return true;
        CurrentHatPresetId = presetId;
        HatPresetChanged?.Invoke();
        return true;
    }

    public bool TryInitialize()
    {
        VerifyAccess();
        if (InitializationState == SceneInitializationState.Ready) return true;
        try
        {
            if (requiresAttachments && attachments is null)
            {
                CalibrationStepCount = 0;
                PhysicsPresentationFrame baseline;
                using (var calibration = beltRoom
                    ? new DesktopPhysicsScene(nativeLibraryPath, new(0, 0, 160, 120), EffectiveAppearance.Body)
                    : new DesktopPhysicsScene(nativeLibraryPath, new(0, 0, 160, 120)))
                {
                    for (var i = 0; i < 240; i++)
                    {
                        var frame = calibration.Step(TimeSpan.FromSeconds(1d / 60));
                        CalibrationStepCount++;
                        if (!frame.Physics.Succeeded)
                            throw new DangoPhysicsException(DangoPhysicsFailureKind.NativeFailure,
                                "眼帽标定失败。", frame.Physics.ErrorCode);
                    }
                    baseline = calibration.LastFrame!.Bodies.Single().Presentation;
                    var geometry = beltRoom ? DangoStreamGeometryFactory.TryCreateBelt(baseline.Contour, 160, 120)
                        : DangoStreamGeometryFactory.TryCreate(baseline.Contour, 160, 120);
                    if (!geometry.Succeeded) throw new InvalidOperationException("中性身体轮廓不可用。");
                    NeutralBodyVisibleWidth = geometry.Geometry!.Bounds.Width;
                    NeutralBodyGroundOffset = beltRoom ? 0 : calibration.GroundY -
                        (calibration.LastFrame.Bodies.Single().Bounds.Y + geometry.Geometry.Bounds.Bottom);
                }
                // The calibration world has been released before the live world is created.
                attachments = new(baseline);
                attachments.SetPaused(!running);
                SynchronizeAppearance();
            }
            EnsureScene(new(0, 0, 160, 120));
            SynchronizeAppearance();
            LastError = null;
            InitializationState = SceneInitializationState.Ready;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException or
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Furniture?.Dispose();
            Furniture = null;
            scene?.Dispose();
            scene = null;
            attachments?.Dispose();
            attachments = null;
            colorSlots.Clear();
            pendingSpawns.Clear();
            hostOrigin = null;
            LastFrame = null;
            InitializationState = SceneInitializationState.Unavailable;
            LastError = exception is DangoPhysicsException physics
                ? $"场景初始化失败：{physics.Kind}（{physics.NativeErrorCode}）；可重试或退出。"
                : $"场景初始化失败：{exception.GetType().Name}；可重试或退出。";
            Stop();
            return false;
        }
    }

    public AttachmentRequestResult SubmitAttachment(AttachmentIntent intent)
    {
        VerifyAccess(); EnsureHealthy();
        if (attachments is null || scene is null) throw new InvalidOperationException("眼帽预览尚未启动。");
        attachments.Synchronize(scene.EntityIds);
        return attachments.Submit(scene.SelectedEntity, intent);
    }

    public bool IsRunning => running;
    public bool IsDragging { get; private set; }
    public int DangoCount => scene?.Count ?? MinimumDangoCount;
    public DangoSelfPhysicsParameters Parameters => DangoSelfPhysicsParameters.CreateDefault();
    public StackedPhysicsPresentationFrame? LastFrame { get; private set; }
    public DangoPhysicsFrame? LastPhysicsFrame => scene?.LastFrame?.Physics;
    public string? LastError { get; private set; }
    public string? LayoutWarning { get; private set; }
    public DangoEntityId? SelectedEntity => scene?.SelectedEntity;
    public IReadOnlyList<DangoEntityId> EntityIds => scene?.EntityIds ?? [];
    public event Action? InputCancelled;

    public string CreateDiagnosticSummary() => string.Join(
        Environment.NewLine,
        $"场景初始化：{InitializationState}；标定步数：{CalibrationStepCount}",
        $"眼帽：{(AttachmentsEnabled ? "已启用" : "未启用")}；附件故障：{AttachmentError ?? "无"}",
        $"轻量互动：{(LightInteractionsEnabled ? "已启用" : "未启用")}；互动故障：{LightInteractionError ?? "无"}",
        $"安静待机：{(quietIdleHostEnabled ? "可用" : "暂停")}；待机故障：{QuietIdleError ?? "无"}",
        $"统一物理 world：{(running ? "运行" : "暂停")}",
        $"本地物理参与者：{DangoCount}；选中：{SelectedEntity}",
        $"contact：{LastFrame?.ContactCount ?? 0}",
        $"最大节点速度：{LastFrame?.MaximumNodeSpeed ?? 0:F3}",
        $"故障锁存：{LastError ?? "无"}；原生错误：{scene?.LastErrorCode ?? 0}",
        "参数版本：1（只读）",
        $"可见区域：{LayoutWarning ?? "正常"}");

    public void Start()
    {
        VerifyAccess();
        if (RequiresInitialization && InitializationState != SceneInitializationState.Ready)
            throw new InvalidOperationException(LastError ?? "场景尚未初始化，请重试。");
        if (LightInteractionError is not null) throw new InvalidOperationException(LightInteractionError);
        if (QuietIdleError is not null) throw new InvalidOperationException(QuietIdleError);
        if (running) return;
        running = true;
        scene?.SetPaused(false);
        attachments?.SetPaused(false);
        accessoryScene?.Resume();
    }

    public void Stop()
    {
        if (disposed) return;
        VerifyAccess();
        running = false;
        ClearPhysicalInput();
        scene?.SetPaused(true);
        attachments?.SetPaused(true);
        accessoryScene?.Pause();
        quietIdleHost = null;
        wanderScreenWidth = null;
        quietIdleHostEnabled = false;
    }

    public bool AddDango()
    {
        VerifyAccess();
        EnsureHealthy();
        EnsureScene(new(0, 0, 160, 120));
        if (DangoCount == MaximumDangoCount) return false;
        Gait.ClearMotion();
        CancelDoze();
        Wander.Cancel();
        var slot = Enumerable.Range(0, MaximumDangoCount).First(value => !colorSlots.ContainsValue(value));
        var id = scene!.Add(slot == 0 ? 0 : slot == 1 ? -2.8 : 2.8, 0);
        colorSlots.Add(id, slot);
        return true;
    }

    public void SetViewport(LogicalRect bounds)
    {
        VerifyAccess();
        if (viewport == bounds) return;
        if (LightInteractionsEnabled) ClearPhysicalInput();
        viewport = bounds;
        layoutPending = true;
    }

    internal double? LocalGroundY => scene?.GroundY;
    internal bool UnboundedHorizontalPreview => unboundedHorizontalPreview;
    internal int HorizontalRebaseCount => scene?.HorizontalRebaseCount ?? 0;
    internal double PreviewJumpSpeedScale => previewJumpSpeedScale;

    internal void SetPreviewJumpSpeedScale(double scale)
    {
        VerifyAccess();
        if (!double.IsFinite(scale) || scale is < .1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(scale));
        // Cleanup of a failed/stopped preview must still restore ordinary
        // launches. A failed opt-in never changes the managed desired state.
        if (scale != 1) EnsureHealthy();
        if (scene is null)
        {
            if (scale != 1) throw new InvalidOperationException("请等待本地场景初始化后切换低跳编辑预览。");
        }
        else scene.SetPreviewJumpSpeedScale(scale);
        previewJumpSpeedScale = scale;
    }

    internal void SetUnboundedHorizontalPreview(bool enabled)
    {
        VerifyAccess();
        EnsureHealthy();
        if (enabled && Furniture is { IsAttached: true })
            throw new InvalidOperationException("请先卸载普通房家具，再启用无边界预览。");
        (scene ?? throw new InvalidOperationException("请等待本地场景首帧后切换编辑预览。")).SetUnboundedHorizontalPreview(enabled);
        unboundedHorizontalPreview = enabled;
        if (enabled) localHorizontalBounds = null;
        layoutPending = true;
    }
    internal LogicalPoint? CurrentSelectedCenter => scene?.LastFrame is not null
        ? scene.SelectedCoreHost : null;

    internal bool TryGetHorizontalPlacement(LogicalPoint requestedCenter, double left, double right,
        out LogicalPoint safeCenter)
    {
        VerifyAccess();
        EnsureHealthy();
        safeCenter = default;
        return scene?.TryGetHorizontalPlacement(requestedCenter, left, right, out safeCenter) == true;
    }

    internal void SetHorizontalBounds(double left, double right)
    {
        VerifyAccess();
        EnsureHealthy();
        if (Furniture is { IsAttached: true } && localHorizontalBounds != (left, right))
            throw new InvalidOperationException("普通房家具绑定期间不能改变碰撞边界。");
        (scene ?? throw new InvalidOperationException("请等待本地场景首帧后设置边界。")).SetHorizontalBounds(left, right);
        localHorizontalBounds = (left, right);
    }

    internal void ClearHorizontalBounds()
    {
        VerifyAccess();
        EnsureHealthy();
        if (Furniture is { IsAttached: true })
            throw new InvalidOperationException("请先卸载普通房家具，再清除碰撞边界。");
        (scene ?? throw new InvalidOperationException("本地物理场景尚未创建。")).ClearHorizontalBounds();
        localHorizontalBounds = null;
    }

    internal bool RehostSelectedEntityAt(LogicalPoint actualCenter)
    {
        VerifyAccess();
        EnsureHealthy();
        if (Furniture is { IsAttached: true })
            throw new InvalidOperationException("请先卸载普通房家具运行绑定，再进行换场。");
        if (scene?.LastFrame is null) return false;
        ClearPhysicalInput();
        if (!scene.RehostSelectedEntityAt(actualCenter)) return false;
        accessoryScene?.Pause(); accessoryScene?.Resume();
        localHorizontalBounds = null;
        hostOrigin = actualCenter;
        layoutPending = true;
        Gait.ClearMotion();
        return true;
    }

    public bool AddDangoInViewport(LogicalRect bounds)
    {
        VerifyAccess();
        EnsureHealthy();
        if (DangoCount == MaximumDangoCount) return false;
        Gait.ClearMotion();
        if (scene?.LastFrame is not { } frame)
            throw new InvalidOperationException("请等待场景首帧完成后再添加团子。");
        var hostPoint = DangoHostLayout.FindSpawn(bounds, frame.Bodies.Select(body => body.Bounds).Concat(pendingSpawns.Values).ToArray());
        var worldPoint = scene.ToWorld(hostPoint);
        var slot = Enumerable.Range(0, MaximumDangoCount).First(value => !colorSlots.ContainsValue(value));
        CancelDoze();
        var id = scene.Add(worldPoint.X, worldPoint.Y - Parameters.DropHeight);
        Wander.Cancel();
        colorSlots.Add(id, slot);
        // UI actions can run twice before the next physics tick publishes new bounds.
        pendingSpawns.Add(id, new(hostPoint.X - 80, hostPoint.Y - 60, 160, 120));
        return true;
    }

    public bool RemoveDango()
    {
        VerifyAccess();
        EnsureHealthy();
        if (scene is null || scene.Count == MinimumDangoCount) return false;
        ClearPhysicalInput();
        var removed = scene.SelectedEntity;
        if (!scene.Remove(removed)) return false;
        colorSlots.Remove(removed);
        pendingSpawns.Remove(removed);
        SynchronizeAppearance();
        LastFrame = null;
        return true;
    }

    public void SelectEntity(DangoEntityId id)
    {
        VerifyAccess();
        EnsureHealthy();
        if (scene is null) throw new InvalidOperationException("场景尚未创建。");
        ClearPhysicalInput();
        scene.Select(id);
    }

    public void SetDirectionalControl(DirectionalControlIntent intent)
    {
        VerifyAccess();
        directionalIntent = LastError is null ? intent : DirectionalControlIntent.None;
        if (HasDirectionalInput)
        {
            CancelLightFeedback();
            CancelQuietIdle();
        }
    }

    public void BeginPhysicalDrag(LogicalPoint pointer)
    {
        if (SelectedEntity is not { } id) throw new InvalidOperationException("场景尚未创建。");
        BeginPhysicalDrag(id, pointer);
    }

    public void BeginPhysicalDrag(DangoEntityId id, LogicalPoint pointer)
    {
        VerifyAccess();
        EnsureHealthy();
        if (!running || scene is null) throw new InvalidOperationException("场景尚未运行。");
        SelectEntity(id);
        scene.BeginDrag(id, pointer);
        IsDragging = true;
    }

    public void MovePhysicalDrag(LogicalPoint pointer)
    {
        VerifyAccess();
        if (!IsDragging || scene is null) throw new InvalidOperationException("团子尚未开始物理拖动。");
        scene.MoveDrag(pointer);
    }

    public void EndPhysicalDrag()
    {
        if (disposed) return;
        VerifyAccess();
        IsDragging = false;
        scene?.EndDrag();
    }

    public void CancelInput()
    {
        VerifyAccess();
        ClearPhysicalInput();
    }

    public void ApplyPhysicsParameters(DangoSelfPhysicsParameters next)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(next);
        throw new InvalidOperationException("正式桌面固定已验收参数版本 1；请在实验工具中调参。");
    }

    public void RestoreDefaultPhysicsParameters() => VerifyAccess();

    public void RegenerateDango()
    {
        VerifyAccess();
        if (RequiresInitialization && InitializationState != SceneInitializationState.Ready)
            throw new InvalidOperationException(LastError ?? "请先重试初始化。");
        if (!TryDetachOrdinaryFurniture())
            throw new InvalidOperationException(Furniture?.Status ?? "请先确认家具提交结果，再恢复团子。");
        Furniture?.Dispose();
        Furniture = null;
        ClearPhysicalInput();
        scene?.Reset();
        pendingSpawns.Clear();
        attachments?.Reset();
        SynchronizeAppearance();
        QuietIdle.Reset();
        Doze.Reset();
        Gait.Reset();
        accessoryScene?.Reset();
        LightInteractionError = null;
        LastFrame = null;
        LastError = null;
        layoutPending = true;
        EnsureOrdinaryFurniture();
    }

    public StackedPhysicsPresentationFrame Tick(PhysicsPresentationInput primary, TimeSpan fixedStep)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(primary);
        if (!running) throw new InvalidOperationException("多体表现运行时尚未启动。");
        if (fixedStep <= TimeSpan.Zero || fixedStep > TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        if (LastError is not null) return LastFrame ?? CreateUnavailable(primary);
        var previousGait = LastFrame?.Gait;
        try
        {
            EnsureScene(primary.AuthoritativeBounds);
            var center = primary.AuthoritativeBounds.Center;
            if (!OrdinaryFurnitureAllowsRehost(center)) return LastFrame ?? CreateUnavailable(primary);
            if (hostOrigin != center)
            {
                ClearPhysicalInput();
                scene!.Rehost(center);
                accessoryScene?.Pause(); accessoryScene?.Resume();
                if (localHorizontalBounds is { } limits) scene.SetHorizontalBounds(limits.Left, limits.Right);
                hostOrigin = center;
                layoutPending = true;
            }
            scene!.SetInput(directionalIntent);
            if (!WanderEligible()) Wander.Cancel();
            var autonomousHorizontal = Wander.Input(fixedStep.TotalSeconds);
            if (!ProcessOrdinaryFurnitureBoundary(fixedStep)) return LastFrame ?? CreateUnavailable(primary);
            var frame = scene.Step(fixedStep, autonomousHorizontal);
            if (scene.LastErrorCode != 0)
            {
                LastError = $"物理故障已锁存：{scene.LastErrorCode}；请显式重置或退出。";
                ClearPhysicalInput();
                return LastFrame ?? CreateUnavailable(primary);
            }
            Furniture?.OnFrame();
            if (Furniture is { IsAttached: true, CanStep: false })
            {
                LastError = Furniture.Status;
                ClearPhysicalInput();
                return LastFrame ?? CreateUnavailable(primary);
            }
            if (frame.Physics.Succeeded) pendingSpawns.Clear();
            if (layoutPending && viewport is { } view && frame.Physics.Succeeded)
            {
                var bounds = frame.Bodies.Select(body => body.Bounds).ToArray();
                LayoutWarning = bounds.Length > 0 && (bounds.Max(b => b.Right) - bounds.Min(b => b.X) > view.Width ||
                    bounds.Max(b => b.Bottom) - bounds.Min(b => b.Y) > view.Height)
                    ? "当前窗口放不下整个场景；请放大窗口或重置。不会缩放或传送物理身体。" : null;
                // A bounded local world is viewed through a camera. Fitting it
                // into the current screen would move the floor and side walls.
                // Keep the accepted automatic fitting path for unbounded hosts.
                var translation = localHorizontalBounds is null && !unboundedHorizontalPreview
                    ? DangoHostLayout.FitTranslation(view, frame.Bodies.Select(body => body.Bounds).ToArray()) : default;
                if (translation != default)
                {
                    ClearPhysicalInput();
                    var currentOrigin = scene.ToHost(new(0, 0));
                    scene.Rehost(new(currentOrigin.X + translation.X, currentOrigin.Y + translation.Y));
                    accessoryScene?.Pause(); accessoryScene?.Resume();
                    frame = scene.LastFrame!;
                }
                layoutPending = false;
            }
            if (!frame.Physics.Succeeded)
            {
                LastError = $"物理故障已锁存：{frame.Physics.ErrorCode}；请显式重置或退出。";
                ClearPhysicalInput();
            }
            // Read-only motion consumers need continuous positions across the
            // native rolling origin. Keep LastPhysicsFrame as the actual local
            // native snapshot, and do not advance any consumer twice.
            var motionFrame = unboundedHorizontalPreview ? frame with
            {
                Physics = frame.Physics with
                {
                    Bodies = frame.Physics.Bodies.Select(body => body with { CoreX = (float)scene.ContinuousCoreX(body) }).ToArray(),
                },
            } : frame;
            if (frame.Physics.Succeeded) BeforeLightFeedbackStep();
            if (frame.Physics.Succeeded && attachments is not null && LightInteractionsEnabled)
            {
                SynchronizeAppearance();
                var dozeEligible = WanderEligible();
                var wasDozing = Doze.Active;
                if (frame.Physics.Bodies.Count == 1)
                {
                    var body = frame.Physics.Bodies[0];
                    Doze.Advance(new(body.IsGrounded, scene.ContinuousCoreX(body), body.CoreY), fixedStep.TotalSeconds,
                        dozeEligible, Wander.Active || QuietIdle.HasActive,
                        intent => attachments.SubmitQuietIdle(body.EntityId, intent).Accepted);
                }
                else CancelDoze();
                if (wasDozing != Doze.Active)
                {
                    Wander.Cancel();
                    QuietIdle.CancelAll(attachments);
                }
                QuietIdle.BeforeStep(motionFrame, fixedStep.TotalSeconds, attachments, id =>
                    quietIdleHostEnabled && (!Doze.Due || QuietIdle.HasActive) && !Doze.Active && !Wander.Active && !IsDragging && !HasDirectionalInput && LightInteractionError is null &&
                    !lightPresses.ContainsKey(id) && !lightFeedback.ContainsKey(id));
            }
            var attachmentFrames = attachments?.Advance(motionFrame, fixedStep.TotalSeconds, scene.SelectedEntity,
                directionalIntent.Left == directionalIntent.Right ?
                    (autonomousHorizontal < 0 ? DangoFacingDirection.Left : autonomousHorizontal > 0 ? DangoFacingDirection.Right : DangoFacingDirection.Center) :
                directionalIntent.Left ? DangoFacingDirection.Left : DangoFacingDirection.Right);
            if (attachmentFrames is not null && Doze.Sleeping)
                attachmentFrames = attachmentFrames.Select(body => body with { DozeSeconds = Doze.SleepSeconds }).ToArray();
            if (frame.Physics.Succeeded) AfterLightFeedbackStep(fixedStep.TotalSeconds);
            if (frame.Physics.Succeeded && attachments is not null && LightInteractionsEnabled)
                QuietIdle.AfterStep(fixedStep.TotalSeconds, attachments);
            LastFrame = new(
                attachmentFrames ?? frame.Bodies.Select(body => body.Presentation).ToArray(),
                frame.Bodies.Select(_ => primary.AuthoritativeBounds).ToArray(),
                frame.Bodies.Select(body => new LogicalVector(
                    body.Bounds.X - primary.AuthoritativeBounds.X,
                    body.Bounds.Y - primary.AuthoritativeBounds.Y)).ToArray(),
                frame.Physics.ContactStepCount + frame.Physics.Bodies.Sum(body => body.GroundContactCount),
                [],
                !frame.Physics.Succeeded,
                frame.Physics.Bodies.Select(body => (double)body.MaximumSpeed).DefaultIfEmpty().Max())
            {
                EntityIds = frame.Bodies.Select(body => body.EntityId).ToArray(),
                ColorSlots = frame.Bodies.Select(body => colorSlots[body.EntityId]).ToArray(),
                BeltRoom = scene.BeltRoomFrame,
            };
            if (frame.Physics.Succeeded) AdvanceWander(fixedStep.TotalSeconds);
            if (frame.Physics.Succeeded) UpdateGait(frame, autonomousHorizontal, fixedStep.TotalSeconds);
            else LastFrame = LastFrame with { Gait = previousGait };
            if (frame.Physics.Succeeded) accessoryScene?.Advance(LastFrame, fixedStep.TotalSeconds);
            return LastFrame;
        }
        catch (DangoPhysicsException exception)
        {
            LastError = $"物理模块不可用：{exception.Kind}（{exception.NativeErrorCode}）；请重置重试或退出。";
            ClearPhysicalInput();
            return LastFrame = LastFrame is null ? CreateUnavailable(primary) : LastFrame with { ConstraintsReleased = true };
        }
    }

    private bool WanderEligible() => LightInteractionsEnabled && quietIdleHostEnabled && scene?.Count == 1 &&
        viewport is not null && wanderScreenWidth is not null && LayoutWarning is null && LastError is null && AttachmentError is null &&
        QuietIdleError is null && LightInteractionError is null && !IsDragging && !HasDirectionalInput &&
        lightPresses.Count == 0 && lightFeedback.Count == 0;

    private void UpdateGait(DesktopPhysicsSceneFrame physical, double autonomousHorizontal, double seconds)
    {
        if (LastFrame is null) return;
        if (!GaitEnabled || !LightInteractionsEnabled || physical.Bodies.Count != 1)
        { Gait.ClearMotion(); return; }
        if (AttachmentError is not null) { Gait.Fail(); return; }
        try
        {
            var body = physical.Physics.Bodies[0];
            var presentation = LastFrame.Bodies[0];
            if (presentation.Attachments?.SourceShape is not { } shape || viewport is not { } view)
            { Gait.InvalidateSampling(); return; }
            var manual = directionalIntent.Left != directionalIntent.Right;
            var direction = manual ? (directionalIntent.Left ? -1 : 1) :
                Wander.Driving ? Math.Sign(autonomousHorizontal) : 0;
            var pressed = lightPresses.Count > 0 || lightFeedback.Count > 0;
            var strain = Gait.Advance(new(scene!.ContinuousCoreX(body) * 80d, body.CoreY * 60d,
                body.IsGrounded, shape.Width * 160, shape.Height * 160, body.SignedArea,
                direction, (quietIdleHostEnabled || unboundedHorizontalPreview) && LayoutWarning is null && QuietIdleError is null &&
                    LightInteractionError is null && !Doze.Active && !pressed,
                IsDragging || directionalIntent.Jump || directionalIntent.Squash || directionalIntent.Stretch || !body.IsGrounded,
                pressed, !manual), seconds);
            var bodyBounds = DangoHostFrameBinding.Bounds(LastFrame, 0);
            // The preview camera moves through world coordinates. A fixed
            // desktop viewport must not suppress gait after walking past its
            // original edge; the host camera owns screen clipping in this mode.
            var gaitViewport = unboundedHorizontalPreview
                ? view.Translate(new(bodyBounds.Center.X - view.Center.X, bodyBounds.Center.Y - view.Center.Y)) : view;
            var descriptor = GaitGeometry.Create(presentation, body.EntityId,
                bodyBounds, gaitViewport, strain, !manual && Wander.Active, DisplayHatSize);
            Gait.AcceptDisplayed((descriptor?.ScaleY ?? 1) - 1);
            LastFrame = LastFrame with { Gait = descriptor };
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or InvalidOperationException)
        {
            // A visual fault must not enter FrameFaultState and discard physical steps.
            Gait.Fail();
            LastFrame = LastFrame with { Gait = null };
        }
    }

    private void AdvanceWander(double seconds)
    {
        if (!WanderEligible() || LastFrame is null || scene?.LastFrame is not { } physical || viewport is not { } view)
        { Wander.Cancel(); return; }
        var bounds = WanderVisibleBounds.Get(LastFrame.Bodies[0], DangoHostFrameBinding.Bounds(LastFrame, 0), DisplayHatSize);
        if (bounds is not { } visible || visible.Y < view.Y || visible.Bottom > view.Bottom)
        { Wander.Cancel(); return; }
        var body = physical.Physics.Bodies[0];
        var wasActive = Wander.Active;
        Wander.Advance(new(scene.ContinuousCoreX(body), body.CoreY, body.IsGrounded,
            visible.X - view.X - 16, view.Right - visible.Right - 16, wanderScreenWidth!.Value), seconds, true, QuietIdle.HasActive || Doze.Due || Doze.Active);
        if (!wasActive && Wander.Active)
        {
            QuietIdle.CancelAll(attachments);
            if (QuietIdleError is not null) Wander.Cancel();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        VerifyAccess();
        Stop();
        Furniture?.Dispose();
        Furniture = null;
        scene?.Dispose();
        scene = null;
        attachments?.Dispose();
        disposed = true;
        InputCancelled = null;
        HatPresetChanged = null;
        AccessoryPreviewChanged = null;
        accessoryScene?.Reset();
        GC.SuppressFinalize(this);
    }

    private void EnsureScene(LogicalRect bounds)
    {
        if (scene is not null) return;
        scene = beltRoom ? new DesktopPhysicsScene(nativeLibraryPath, bounds, EffectiveAppearance.Body)
            : new DesktopPhysicsScene(nativeLibraryPath, bounds);
        if (previewJumpSpeedScale != 1) scene.SetPreviewJumpSpeedScale(previewJumpSpeedScale);
        if (unboundedHorizontalPreview) scene.SetUnboundedHorizontalPreview(true);
        colorSlots.Add(scene.SelectedEntity, 0);
        hostOrigin = bounds.Center;
        scene.SetPaused(!running);
        SynchronizeAppearance();
        EnsureOrdinaryFurniture();
    }

    private StackedPhysicsPresentationFrame CreateUnavailable(PhysicsPresentationInput primary) => new(
        [AuthoredPoseSampler.CreateFallback(primary, PhysicsPresentationFallbackReason.NativeUnavailable)],
        [primary.AuthoritativeBounds], [new LogicalVector(0, 0)], 0, [], true, 0);

    private void ClearPhysicalInput()
    {
        Gait.InvalidateSampling();
        CancelQuietIdle();
        lightPresses.Clear();
        CancelLightFeedback();
        interactionGeneration = Interlocked.Increment(ref nextInteractionGeneration);
        directionalIntent = DirectionalControlIntent.None;
        IsDragging = false;
        ordinaryFurnitureGlove = false;
        scene?.CancelInput();
        InputCancelled?.Invoke();
    }

    private void EnsureHealthy()
    {
        if (LastError is not null) throw new InvalidOperationException(LastError);
    }

    private void VerifyAccess()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThreadId)
            throw new InvalidOperationException("桌面 runtime 只能由创建线程访问。");
    }
}
