using System.IO;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
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

    public StackedDangoSceneRuntime(string? nativeLibraryPath = null, PhysicsPresentationFrame? attachmentBaseline = null)
    {
        this.nativeLibraryPath = nativeLibraryPath ?? Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        if (attachmentBaseline is not null) attachments = new(attachmentBaseline);
    }

    public static StackedDangoSceneRuntime CreateProduct(string? nativeLibraryPath = null) =>
        new(nativeLibraryPath) { requiresAttachments = true, RequiresInitialization = true };

    public static StackedDangoSceneRuntime CreateEyeHatReview(string? nativeLibraryPath = null, bool deferInitialization = false)
    {
        var runtime = CreateProduct(nativeLibraryPath);
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
    }

    public bool AddDango()
    {
        VerifyAccess();
        EnsureHealthy();
        EnsureScene(new(0, 0, 160, 120));
        if (DangoCount == MaximumDangoCount) return false;
        var slot = Enumerable.Range(0, MaximumDangoCount).First(value => !colorSlots.ContainsValue(value));
        var id = scene!.Add(slot == 0 ? 0 : slot == 1 ? -2.8 : 2.8, 0);
        colorSlots.Add(id, slot);
        return true;
    }

    public void SetViewport(LogicalRect bounds)
    {
        VerifyAccess();
        if (viewport == bounds) return;
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
            var frame = scene.Step(fixedStep);
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
            var attachmentFrames = attachments?.Advance(frame, fixedStep.TotalSeconds, scene.SelectedEntity,
                directionalIntent.Left == directionalIntent.Right ? DangoFacingDirection.Center :
                directionalIntent.Left ? DangoFacingDirection.Left : DangoFacingDirection.Right);
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
            return LastFrame;
        }
        catch (DangoPhysicsException exception)
        {
            LastError = $"物理模块不可用：{exception.Kind}（{exception.NativeErrorCode}）；请重置重试或退出。";
            ClearPhysicalInput();
            return LastFrame = LastFrame is null ? CreateUnavailable(primary) : LastFrame with { ConstraintsReleased = true };
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
