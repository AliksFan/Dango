using System.IO;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Rendering.Visuals;

public enum SceneInitializationState { NotStarted, Ready, Unavailable }

public sealed class StackedDangoSceneRuntime : IDisposable
{
    public const int MinimumDangoCount = 1;
    public const int MaximumDangoCount = 3;

    private readonly string nativeLibraryPath;
    private readonly int ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly Dictionary<DangoEntityId, int> colorSlots = [];
    private readonly Dictionary<DangoEntityId, LogicalRect> pendingSpawns = [];
    private DesktopPhysicsScene? scene;
    private LogicalPoint? hostOrigin;
    private LogicalRect? viewport;
    private bool layoutPending;
    private DirectionalControlIntent directionalIntent;
    private bool running;
    private bool disposed;
    private AttachmentSceneController? attachments;
    private bool requiresAttachments;
    public bool RequiresInitialization { get; private init; }
    public SceneInitializationState InitializationState { get; private set; }
    public int CalibrationStepCount { get; private set; }
    public bool AttachmentsEnabled => attachments is not null;
    public string CurrentHatPresetId { get; private set; } = "sprout";
    public HatAppearance CurrentHatAppearance => DangoHatPresets.GetAppearance(CurrentHatPresetId);
    public HatAppearance DisplayHatAppearance => CurrentHatAppearance;
    public double DisplayHatSize => AttachmentsEnabled ? 1.35 : 1;
    public string? AttachmentError => attachments?.LastError;
    public event Action? HatPresetChanged;
    internal QuietIdleSceneController QuietIdle { get; } = new();
    internal ShortWanderController Wander { get; } = new();
    private double? wanderScreenWidth;
    internal void UpdateWanderScreenWidth(object owner, double width)
    {
        VerifyAccess();
        if (!ReferenceEquals(quietIdleHost, owner)) return;
        if (!double.IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (wanderScreenWidth == width) return;
        Wander.Cancel();
        wanderScreenWidth = width;
    }
    public bool HasAutonomousAction => HasQuietIdleAction || Wander.Active;
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
        CancelQuietIdle();
    }

    internal long BeginLightPress(LightInteractionTarget target)
    {
        VerifyAccess();
        if (!IsLightInteractionTargetCurrent(target)) return 0;
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
        Wander.Cancel();
        QuietIdle.CancelAll(LastError is null ? attachments : null);
    }
    public bool HasQuietIdleAction => QuietIdle.HasActive;
    public string? QuietIdleError => QuietIdle.Error;

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
        if (!QuietIdle.Cancel(target.Entity, attachments)) return false;
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

    private bool HasDirectionalInput => directionalIntent.Left != directionalIntent.Right || directionalIntent.Jump || directionalIntent.Squash;

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
                using (var calibration = new DesktopPhysicsScene(nativeLibraryPath, new(0, 0, 160, 120)))
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
                }
                // The calibration world has been released before the live world is created.
                attachments = new(baseline);
                attachments.SetPaused(!running);
            }
            EnsureScene(new(0, 0, 160, 120));
            LastError = null;
            InitializationState = SceneInitializationState.Ready;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
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
    }

    public void Stop()
    {
        if (disposed) return;
        VerifyAccess();
        running = false;
        ClearPhysicalInput();
        scene?.SetPaused(true);
        attachments?.SetPaused(true);
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

    public bool AddDangoInViewport(LogicalRect bounds)
    {
        VerifyAccess();
        EnsureHealthy();
        if (DangoCount == MaximumDangoCount) return false;
        if (scene?.LastFrame is not { } frame)
            throw new InvalidOperationException("请等待场景首帧完成后再添加团子。");
        var hostPoint = DangoHostLayout.FindSpawn(bounds, frame.Bodies.Select(body => body.Bounds).Concat(pendingSpawns.Values).ToArray());
        var worldPoint = scene.ToWorld(hostPoint);
        var slot = Enumerable.Range(0, MaximumDangoCount).First(value => !colorSlots.ContainsValue(value));
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
        attachments?.Synchronize(scene.EntityIds);
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
        ClearPhysicalInput();
        scene?.Reset();
        pendingSpawns.Clear();
        attachments?.Reset();
        QuietIdle.Reset();
        LightInteractionError = null;
        LastFrame = null;
        LastError = null;
        layoutPending = true;
    }

    public StackedPhysicsPresentationFrame Tick(PhysicsPresentationInput primary, TimeSpan fixedStep)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(primary);
        if (!running) throw new InvalidOperationException("多体表现运行时尚未启动。");
        if (fixedStep <= TimeSpan.Zero || fixedStep > TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        if (LastError is not null) return LastFrame ?? CreateUnavailable(primary);
        try
        {
            EnsureScene(primary.AuthoritativeBounds);
            var center = primary.AuthoritativeBounds.Center;
            if (hostOrigin != center)
            {
                ClearPhysicalInput();
                scene!.Rehost(center);
                hostOrigin = center;
                layoutPending = true;
            }
            scene!.SetInput(directionalIntent);
            if (!WanderEligible()) Wander.Cancel();
            var autonomousHorizontal = Wander.Input(fixedStep.TotalSeconds);
            var frame = scene.Step(fixedStep, autonomousHorizontal);
            if (frame.Physics.Succeeded) pendingSpawns.Clear();
            if (layoutPending && viewport is { } view && frame.Physics.Succeeded)
            {
                var bounds = frame.Bodies.Select(body => body.Bounds).ToArray();
                LayoutWarning = bounds.Length > 0 && (bounds.Max(b => b.Right) - bounds.Min(b => b.X) > view.Width ||
                    bounds.Max(b => b.Bottom) - bounds.Min(b => b.Y) > view.Height)
                    ? "当前窗口放不下整个场景；请放大窗口或重置。不会缩放或传送物理身体。" : null;
                var translation = DangoHostLayout.FitTranslation(view, frame.Bodies.Select(body => body.Bounds).ToArray());
                if (translation != default)
                {
                    ClearPhysicalInput();
                    var currentOrigin = scene.ToHost(new(0, 0));
                    scene.Rehost(new(currentOrigin.X + translation.X, currentOrigin.Y + translation.Y));
                    frame = scene.LastFrame!;
                }
                layoutPending = false;
            }
            if (!frame.Physics.Succeeded)
            {
                LastError = $"物理故障已锁存：{frame.Physics.ErrorCode}；请显式重置或退出。";
                ClearPhysicalInput();
            }
            if (frame.Physics.Succeeded) BeforeLightFeedbackStep();
            if (frame.Physics.Succeeded && attachments is not null && LightInteractionsEnabled)
            {
                attachments.Synchronize(scene.EntityIds);
                QuietIdle.BeforeStep(frame, fixedStep.TotalSeconds, attachments, id =>
                    quietIdleHostEnabled && !Wander.Active && !IsDragging && !HasDirectionalInput && LightInteractionError is null &&
                    !lightPresses.ContainsKey(id) && !lightFeedback.ContainsKey(id));
            }
            var attachmentFrames = attachments?.Advance(frame, fixedStep.TotalSeconds, scene.SelectedEntity,
                directionalIntent.Left == directionalIntent.Right ?
                    (autonomousHorizontal < 0 ? DangoFacingDirection.Left : autonomousHorizontal > 0 ? DangoFacingDirection.Right : DangoFacingDirection.Center) :
                directionalIntent.Left ? DangoFacingDirection.Left : DangoFacingDirection.Right);
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
            };
            if (frame.Physics.Succeeded) AdvanceWander(fixedStep.TotalSeconds);
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

    private void AdvanceWander(double seconds)
    {
        if (!WanderEligible() || LastFrame is null || scene?.LastFrame is not { } physical || viewport is not { } view)
        { Wander.Cancel(); return; }
        var bounds = WanderVisibleBounds.Get(LastFrame.Bodies[0], DangoHostFrameBinding.Bounds(LastFrame, 0), DisplayHatSize);
        if (bounds is not { } visible || visible.Y < view.Y || visible.Bottom > view.Bottom)
        { Wander.Cancel(); return; }
        var body = physical.Physics.Bodies[0];
        var wasActive = Wander.Active;
        Wander.Advance(new(body.CoreX, body.CoreY, body.IsGrounded,
            visible.X - view.X - 16, view.Right - visible.Right - 16, wanderScreenWidth!.Value), seconds, true, QuietIdle.HasActive);
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
        scene?.Dispose();
        scene = null;
        attachments?.Dispose();
        disposed = true;
        InputCancelled = null;
        HatPresetChanged = null;
        GC.SuppressFinalize(this);
    }

    private void EnsureScene(LogicalRect bounds)
    {
        if (scene is not null) return;
        scene = new DesktopPhysicsScene(nativeLibraryPath, bounds);
        colorSlots.Add(scene.SelectedEntity, 0);
        hostOrigin = bounds.Center;
        scene.SetPaused(!running);
    }

    private StackedPhysicsPresentationFrame CreateUnavailable(PhysicsPresentationInput primary) => new(
        [AuthoredPoseSampler.CreateFallback(primary, PhysicsPresentationFallbackReason.NativeUnavailable)],
        [primary.AuthoritativeBounds], [new LogicalVector(0, 0)], 0, [], true, 0);

    private void ClearPhysicalInput()
    {
        CancelQuietIdle();
        lightPresses.Clear();
        CancelLightFeedback();
        interactionGeneration = Interlocked.Increment(ref nextInteractionGeneration);
        directionalIntent = DirectionalControlIntent.None;
        IsDragging = false;
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
