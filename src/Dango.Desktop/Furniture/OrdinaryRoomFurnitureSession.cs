using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Core.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Physics;

namespace Dango.Desktop.Furniture;

internal enum OrdinaryFurnitureSceneState { Synced, Staging, PersistedNotApplied, CommitUnknown, RuntimeUnknown, Protected }

/// <summary>One owner and one uninterrupted scene. Only final commit runs between steps.</summary>
internal sealed class OrdinaryRoomFurnitureSession : IDisposable
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly DesktopPhysicsScene scene;
    private readonly FurnitureLayoutStore store;
    private readonly string path;
    private FurnitureLayoutLease? lease;
    private Task<FurnitureLayoutStage>? staging;
    private TaskCompletionSource? completion;
    private FurnitureEditOperation? pendingOperation, lastSuccess, recoveryOperation;
    private FurnitureLayout? pendingLayout;
    private byte[]? baselineBytes;
    private DangoFurnitureFrame? observation;
    private ulong appliedNativeInstanceId;
    private bool recoveryRequested, disposed;
    private string status = "选择家具开始布置。";
    private string? previewRejection;

    private readonly Func<bool> ownsActiveRoom;
    private readonly bool capability;
    private Dango.Core.Coordinates.LogicalPoint attachedOrigin;
    private double attachedGround;
    private DangoEntityId attachedEntity;
    internal bool IsAttached { get; private set; }

    internal OrdinaryRoomFurnitureSession(DesktopPhysicsScene scene, string dataRoot,
        FurnitureLayoutStore? store = null, Func<bool>? ownsActiveRoom = null)
    {
        this.scene = scene;
        this.store = store ?? new(contract: FurnitureLayoutContract.OrdinaryRoom);
        if (this.store.Contract != FurnitureLayoutContract.OrdinaryRoom)
            throw new ArgumentException("普通房必须使用专用家具存档合同。", nameof(store));
        this.ownsActiveRoom = ownsActiveRoom ?? (() => true);
        path = FurnitureLayoutStore.GetLayoutPath(dataRoot, FurnitureLayoutContract.OrdinaryRoom);
        var loaded = this.store.Load(path);
        Layout = PersistedLayout = loaded.Layout ?? FurnitureLayout.Empty;
        State = loaded.IsWritable ? OrdinaryFurnitureSceneState.Synced : OrdinaryFurnitureSceneState.Protected;
        if (!loaded.IsWritable) status = loaded.Warning ?? "原布局受保护，暂不能布置。";
        capability = scene.SupportsLiveFurnitureEditing;
        if (!capability && loaded.IsWritable) status = "当前物理模块不支持家具编辑。";
    }
    internal FurnitureLayout Layout { get; private set; }
    internal FurnitureLayout PersistedLayout { get; private set; }
    internal FurniturePlatformInstance? Preview { get; private set; }
    internal bool HasPreview => Preview is not null;
    internal bool Busy => completion is not null;
    internal bool CanEdit => !disposed && IsAttached && capability && ownsActiveRoom() && !Busy && State == OrdinaryFurnitureSceneState.Synced;
    internal bool CanStep => !disposed && State != OrdinaryFurnitureSceneState.RuntimeUnknown;
    internal bool CanRecover => !disposed && IsAttached && capability && ownsActiveRoom() && !Busy && State is OrdinaryFurnitureSceneState.PersistedNotApplied or OrdinaryFurnitureSceneState.CommitUnknown;
    internal OrdinaryFurnitureSceneState State { get; private set; }
    internal string Status => status;
    internal DangoPhysicsFrame? LastFrame { get; private set; }
    internal DangoFurnitureFrame? NativeFrame => observation;
    internal ulong WorldGeneration => observation?.WorldGeneration ?? 0;
    internal ulong SceneRevision => observation?.SceneRevision ?? 0;
    internal ulong PhysicsStep => observation?.PhysicsStep ?? 0;
    internal bool TryEnterEditing()
    {
        CheckOwner();
        if (CanRecover) return true;
        if (!CanEdit) return false;
        try
        {
            using var admission = store.AcquireLease(path);
            if (!admission.Baseline.IsWritable || admission.Baseline.Layout != PersistedLayout)
            { status = "布局文件已变化或受保护，请返回房门核对后重试。"; return false; }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { status = "家具存档暂不可写，请稍后重试：" + error.Message; return false; }
    }
    internal IReadOnlyList<double> CommitBoundaryMilliseconds => boundaryTimes;
    private readonly List<double> boundaryTimes = [];
    // Failure seams exercise actual native/file outcomes in the isolated verification entry point.
    internal Action? BeforeNativeApply { get; set; }
    internal Action? AfterNativeApply { get; set; }

    internal bool Attach()
    {
        CheckOwner();
        if (IsAttached) return CanStep;
        if (Busy || State is OrdinaryFurnitureSceneState.CommitUnknown or OrdinaryFurnitureSceneState.PersistedNotApplied or OrdinaryFurnitureSceneState.RuntimeUnknown)
            return false;
        var loaded = store.Load(path);
        if (!loaded.IsWritable)
        { State = OrdinaryFurnitureSceneState.Protected; status = loaded.Warning ?? "布局无法可靠读取。"; return false; }
        Layout = PersistedLayout = loaded.Layout!;
        State = OrdinaryFurnitureSceneState.Synced;
        Preview = null;
        attachedOrigin = scene.ToHost(new(0, 0));
        attachedGround = scene.GroundY;
        attachedEntity = scene.SelectedEntity;
        if (!capability)
        {
            status = "当前物理模块不支持家具编辑。";
            // An absent collider is honest only for a verified empty layout.
            if (Layout.Platform is not null) return false;
            IsAttached = true;
            return true;
        }
        try
        {
            var current = scene.ReadFurnitureFrame();
            EnsureHealthy(current);
            if (current.Platform is not null || current.PlatformInstanceId != 0)
                throw new InvalidDataException("离房后仍存在家具绑定，不能重复进入。");
            if (Layout.Platform is { } item)
            {
                var prepared = scene.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Insert, 0,
                    ToDefinition(item), current.SceneRevision);
                if (!prepared.Probe.Allowed || prepared.Token is null)
                { status = "已保存家具无法载入：" + RejectionMessage(prepared.Probe.Rejection); return false; }
                current = scene.ApplyPreparedStaticPlatformChange(prepared.Token.Value);
            }
            EnsureHealthy(current);
            if (!MatchesPlatform(current, Layout)) throw new InvalidDataException("家具初始布局未真实应用。");
            observation = current; LastFrame = current.Physics;
            appliedNativeInstanceId = current.PlatformInstanceId;
            IsAttached = true;
            status = "选择家具开始布置。";
            return true;
        }
        catch (Exception error)
        { FailRuntime("家具载入运行状态无法确认：" + error.Message); return false; }
    }

    internal bool TryDetach()
    {
        CheckOwner();
        if (Busy || State is OrdinaryFurnitureSceneState.CommitUnknown or OrdinaryFurnitureSceneState.PersistedNotApplied or OrdinaryFurnitureSceneState.RuntimeUnknown)
            return false;
        if (!IsAttached) return true;
        if (!capability)
        { IsAttached = false; Preview = null; return true; }
        DangoPreparedStaticPlatformChange? token = null;
        try
        {
            if (!MappingIsCurrent()) throw new InvalidDataException("房间坐标绑定已变化。");
            var current = scene.ReadFurnitureFrame(); EnsureHealthy(current);
            if (!MatchesPlatform(current, Layout) || current.PlatformInstanceId != appliedNativeInstanceId)
                throw new InvalidDataException("离房前真实家具与布局不一致。");
            if (current.Platform is not null)
            {
                var prepared = scene.PrepareStaticPlatformChange(DangoStaticPlatformChangeKind.Delete,
                    current.PlatformInstanceId, null, current.SceneRevision);
                if (!prepared.Probe.Allowed || prepared.Token is null)
                { status = RejectionMessage(prepared.Probe.Rejection); return false; }
                token = prepared.Token;
                current = scene.ApplyPreparedStaticPlatformChange(token.Value);
                token = null;
            }
            EnsureHealthy(current);
            if (current.Platform is not null || current.PlatformInstanceId != 0)
                throw new InvalidDataException("家具尚未卸载，禁止换场。");
            observation = current; LastFrame = current.Physics;
            appliedNativeInstanceId = 0;
            IsAttached = false; Preview = null;
            // This is a runtime unbind, not a persisted Delete operation.
            return true;
        }
        catch (Exception error)
        { if (token is { } remaining) TryCancel(remaining); FailRuntime("家具卸载无法确认，已停止：" + error.Message); return false; }
    }

    private bool MappingIsCurrent() => scene.SelectedEntity == attachedEntity &&
        scene.ToHost(new(0, 0)) == attachedOrigin && scene.GroundY == attachedGround;

    internal void FailRuntime(string message)
    { State = OrdinaryFurnitureSceneState.RuntimeUnknown; status = message; }

    internal bool CanConfirm
    {
        get
        {
            CheckOwner();
            if (!CanEdit || Preview is null) return false;
            var readingNative = false;
            try
            {
                Preview.Validate(FurnitureLayoutContract.OrdinaryRoom);
                readingNative = true;
                var current = scene.ReadFurnitureFrame();
                EnsureHealthy(current);
                if (!MatchesPlatform(current, Layout) || current.PlatformInstanceId != appliedNativeInstanceId)
                    throw new InvalidDataException("真实家具与已应用布局不一致。");
                var probe = scene.ProbeStaticPlatformChange(Layout.Platform is null ? DangoStaticPlatformChangeKind.Insert : DangoStaticPlatformChangeKind.Move,
                    current.PlatformInstanceId, ToDefinition(Preview), current.SceneRevision);
                if (!probe.Allowed) { status = previewRejection = RejectionMessage(probe.Rejection); return false; }
                if (status == previewRejection) status = "勾确认后应用并保存。";
                previewRejection = null;
                return true;
            }
            catch (Exception error)
            {
                if (readingNative) State = OrdinaryFurnitureSceneState.RuntimeUnknown;
                status = error.Message; return false;
            }
        }
    }

    internal void PreviewPlatform(double anchorX, double anchorY)
    {
        CheckOwner();
        if (!CanEdit) return;
        Preview = new(Layout.Platform?.InstanceId ?? Preview?.InstanceId ?? Guid.NewGuid(), new(anchorX, anchorY));
        status = "勾确认后应用并保存。";
    }

    internal void CancelPreview()
    {
        CheckOwner();
        if (Busy) return;
        Preview = null;
        if (State == OrdinaryFurnitureSceneState.Synced) status = "本次预览已取消。";
    }

    internal Task ConfirmAsync()
    {
        CheckOwner();
        if (!CanConfirm) return Task.CompletedTask;
        return SubmitAsync(new(Guid.NewGuid(), PersistedLayout.LayoutVersion,
            Layout.Platform is null ? FurnitureEditKind.Insert : FurnitureEditKind.Move,
            Layout.Platform?.InstanceId, Preview));
    }

    internal Task DeleteAsync()
    {
        CheckOwner();
        if (!CanEdit || Layout.Platform is null) return Task.CompletedTask;
        return SubmitAsync(new(Guid.NewGuid(), PersistedLayout.LayoutVersion, FurnitureEditKind.Delete, Layout.Platform.InstanceId, null));
    }

    internal Task SubmitAsync(FurnitureEditOperation operation)
    {
        CheckOwner();
        if (lastSuccess?.OperationId == operation.OperationId)
        {
            if (lastSuccess != operation) throw new InvalidOperationException("同一操作标识不能改变内容。");
            return Task.CompletedTask;
        }
        if (!CanEdit || Layout != PersistedLayout) throw new InvalidOperationException("当前布局尚未对齐，不能继续提交。");
        var candidate = operation.BuildCandidate(PersistedLayout, FurnitureLayoutContract.OrdinaryRoom);
        try
        {
            lease = store.AcquireLease(path);
            if (!lease.Baseline.IsWritable || lease.Baseline.Layout != PersistedLayout)
                throw new InvalidOperationException("布局文件已变化或受保护，不能覆盖。");
            baselineBytes = lease.GetBaselineBytes();
            pendingOperation = operation; pendingLayout = candidate;
            State = OrdinaryFurnitureSceneState.Staging; status = "正在保存…";
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var heldLease = lease;
            staging = Task.Run(() => heldLease.Stage(candidate, operation.ExpectedVersion));
            return completion.Task;
        }
        catch (Exception error)
        {
            lease?.Dispose(); lease = null;
            status = error.Message;
            return Task.CompletedTask;
        }
    }

    internal Task RecoverAsync()
    {
        CheckOwner();
        if (!CanRecover) return Task.CompletedTask;
        recoveryRequested = true;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return completion.Task;
    }

    internal void ProcessBoundary()
    {
        CheckOwner();
        if (!IsAttached || !capability) return;
        if (!MappingIsCurrent())
        { FailRuntime("房间坐标绑定已变化，已停止家具场景。"); return; }
        if (recoveryRequested) FinishRecovery();
        if (staging?.IsCompleted == true) FinishStaging();
    }

    internal void OnFrame()
    {
        CheckOwner();
        if (!IsAttached || !capability || State == OrdinaryFurnitureSceneState.RuntimeUnknown) return;
        try
        {
            if (!MappingIsCurrent()) throw new InvalidDataException("房间坐标绑定已变化。");
            var current = scene.ReadFurnitureFrame();
            EnsureHealthy(current);
            if (!MatchesPlatform(current, Layout) || current.PlatformInstanceId != appliedNativeInstanceId ||
                current.Physics.Bodies.Single().EntityId != attachedEntity)
                throw new InvalidDataException("普通房家具与团子运行绑定不一致。");
            observation = current; LastFrame = current.Physics;
        }
        catch (Exception error) { FailRuntime("物理状态异常，已停止运行：" + error.Message); }
    }
    private void FinishStaging()
    {
        FurnitureLayoutStage? stage = null;
        DangoPreparedStaticPlatformChange? token = null;
        var saved = false;
        var commitAttempted = false;
        var start = Stopwatch.GetTimestamp();
        DangoFurnitureFrame? old = null;
        try
        {
            stage = staging!.GetAwaiter().GetResult();
            old = scene.ReadFurnitureFrame(); EnsureHealthy(old);
            if (!MatchesPlatform(old, Layout) || old.PlatformInstanceId != appliedNativeInstanceId)
            { State = OrdinaryFurnitureSceneState.RuntimeUnknown; throw new InvalidDataException("真实家具与当前布局不一致。"); }
            var prepared = Prepare(pendingOperation!, pendingLayout!, old);
            if (!prepared.Probe.Allowed || prepared.Token is null)
            {
                State = ObserveUnchanged(old) ? OrdinaryFurnitureSceneState.Synced : OrdinaryFurnitureSceneState.RuntimeUnknown;
                status = State == OrdinaryFurnitureSceneState.Synced ? RejectionMessage(prepared.Probe.Rejection) : "运行状态无法确认，已停止。";
                previewRejection = State == OrdinaryFurnitureSceneState.Synced ? status : null;
                return;
            }
            token = prepared.Token;
            commitAttempted = true;
            var result = stage.Commit();
            if (result.State == FurnitureCommitState.NotCommitted)
            {
                scene.CancelPreparedStaticPlatformChange(token.Value); token = null;
                State = OrdinaryFurnitureSceneState.Synced; status = result.Warning ?? "保存失败，原家具保留。";
                return;
            }
            if (result.State == FurnitureCommitState.Unknown)
            {
                recoveryOperation = pendingOperation;
                TryCancel(token.Value); token = null;
                State = ObserveUnchanged(old) ? OrdinaryFurnitureSceneState.CommitUnknown : OrdinaryFurnitureSceneState.RuntimeUnknown;
                status = "保存结果待确认，暂不能继续布置。";
                return;
            }
            saved = true; PersistedLayout = pendingLayout!;
            recoveryOperation = pendingOperation;
            ApplyAndConfirm(token.Value, pendingLayout!, pendingOperation!);
            token = null;
        }
        catch (Exception error)
        {
            if (token is { } remaining) TryCancel(remaining);
            if (commitAttempted && !saved)
            {
                var loaded = store.Load(path);
                var bytes = loaded.CopyRawBytes();
                if (loaded.State == FurnitureLayoutLoadState.Valid && bytes is not null &&
                    bytes.AsSpan().SequenceEqual(FurnitureLayoutSerializer.Serialize(pendingLayout!, FurnitureLayoutContract.OrdinaryRoom)))
                { saved = true; PersistedLayout = pendingLayout!; recoveryOperation = pendingOperation; }
                else if (!((loaded.State == FurnitureLayoutLoadState.Missing && baselineBytes is null) ||
                    (loaded.State == FurnitureLayoutLoadState.Valid && bytes is not null && baselineBytes is not null && bytes.AsSpan().SequenceEqual(baselineBytes))))
                {
                    recoveryOperation = pendingOperation;
                    State = old is not null && ObserveUnchanged(old) ? OrdinaryFurnitureSceneState.CommitUnknown : OrdinaryFurnitureSceneState.RuntimeUnknown;
                    status = "保存结果待确认，暂不能继续布置。";
                    return;
                }
            }
            if (saved && old is not null)
                ResolveApplyFailure(old, pendingLayout!, pendingOperation!, token, error);
            else if ((stage is not null && old is null) || (old is not null && !ObserveUnchanged(old)) || State == OrdinaryFurnitureSceneState.RuntimeUnknown)
            { State = OrdinaryFurnitureSceneState.RuntimeUnknown; status = "运行状态无法确认，已停止。"; }
            else { State = OrdinaryFurnitureSceneState.Synced; status = "保存失败：" + error.Message; }
        }
        finally
        {
            boundaryTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            try { stage?.Dispose(); lease?.Dispose(); }
            finally { lease = null; staging = null; pendingOperation = null; FinishCompletion(); }
        }
    }

    private void FinishRecovery()
    {
        DangoPreparedStaticPlatformChange? token = null;
        DangoFurnitureFrame? old = null;
        try
        {
            old = scene.ReadFurnitureFrame(); EnsureHealthy(old);
            if (!MatchesPlatform(old, Layout) || old.PlatformInstanceId != appliedNativeInstanceId)
            { State = OrdinaryFurnitureSceneState.RuntimeUnknown; throw new InvalidDataException("真实家具与旧布局不一致。"); }
            using var recoveryLease = store.AcquireLease(path);
            var loaded = recoveryLease.Baseline;
            var bytes = recoveryLease.GetBaselineBytes();
            if (State == OrdinaryFurnitureSceneState.CommitUnknown)
            {
                if ((loaded.State == FurnitureLayoutLoadState.Missing && baselineBytes is null) ||
                    (loaded.State == FurnitureLayoutLoadState.Valid && bytes is not null && baselineBytes is not null && bytes.AsSpan().SequenceEqual(baselineBytes)))
                { PersistedLayout = Layout; State = OrdinaryFurnitureSceneState.Synced; status = "已确认未保存，原布局保留。"; pendingLayout = null; recoveryOperation = null; return; }
                if (pendingLayout is null || bytes is null || !bytes.AsSpan().SequenceEqual(FurnitureLayoutSerializer.Serialize(pendingLayout, FurnitureLayoutContract.OrdinaryRoom)))
                { status = "文件仍不可确认或已被外部修改，继续保护原件。"; return; }
                PersistedLayout = pendingLayout;
                State = OrdinaryFurnitureSceneState.PersistedNotApplied;
            }
            if (loaded.State != FurnitureLayoutLoadState.Valid || loaded.Layout != PersistedLayout ||
                bytes is null || !bytes.AsSpan().SequenceEqual(FurnitureLayoutSerializer.Serialize(PersistedLayout, FurnitureLayoutContract.OrdinaryRoom)))
            { State = OrdinaryFurnitureSceneState.CommitUnknown; status = "保存文件已变化，暂不能应用。"; return; }
            var prepared = Prepare(recoveryOperation!, PersistedLayout, old);
            if (!prepared.Probe.Allowed || prepared.Token is null)
            { status = "布局已保存，当前尚未应用：" + RejectionMessage(prepared.Probe.Rejection); return; }
            token = prepared.Token;
            ApplyAndConfirm(token.Value, PersistedLayout, recoveryOperation!);
            token = null;
        }
        catch (Exception error)
        {
            if (token is { } remaining) TryCancel(remaining);
            if (State == OrdinaryFurnitureSceneState.PersistedNotApplied && old is not null)
                ResolveApplyFailure(old, PersistedLayout, recoveryOperation!, token, error);
            else if (old is null || !ObserveUnchanged(old))
            { State = OrdinaryFurnitureSceneState.RuntimeUnknown; status = "恢复时运行状态无法确认，已停止。"; }
            else status = "恢复未完成：" + error.Message;
        }
        finally { recoveryRequested = false; FinishCompletion(); }
    }

    private DangoStaticPlatformPreparation Prepare(FurnitureEditOperation operation, FurnitureLayout candidate, DangoFurnitureFrame current) =>
        scene.PrepareStaticPlatformChange(operation.Kind switch
        {
            FurnitureEditKind.Insert => DangoStaticPlatformChangeKind.Insert,
            FurnitureEditKind.Move => DangoStaticPlatformChangeKind.Move,
            _ => DangoStaticPlatformChangeKind.Delete,
        }, current.PlatformInstanceId, candidate.Platform is null ? null : ToDefinition(candidate.Platform), current.SceneRevision);

    private void ApplyAndConfirm(DangoPreparedStaticPlatformChange token, FurnitureLayout candidate, FurnitureEditOperation operation)
    {
        BeforeNativeApply?.Invoke();
        scene.ApplyPreparedStaticPlatformChange(token);
        AfterNativeApply?.Invoke();
        var current = scene.ReadFurnitureFrame();
        EnsureHealthy(current);
        if (current.LastAppliedToken != token.TokenId || current.WorldGeneration != token.WorldGeneration ||
            current.PhysicsStep != token.PhysicsStep || current.SceneRevision != token.SceneRevision + 1 || !MatchesPlatform(current, candidate))
            throw new InvalidDataException("文件已保存，但真实家具应用结果无法确认。");
        MarkApplied(current, candidate, operation);
    }

    private void MarkApplied(DangoFurnitureFrame current, FurnitureLayout candidate, FurnitureEditOperation operation)
    {
        observation = current; LastFrame = current.Physics;
        appliedNativeInstanceId = current.PlatformInstanceId;
        Layout = candidate; lastSuccess = operation; Preview = null;
        State = OrdinaryFurnitureSceneState.Synced; recoveryOperation = null; pendingLayout = null;
        status = "已应用并保存。";
    }

    private void ResolveApplyFailure(DangoFurnitureFrame old, FurnitureLayout candidate, FurnitureEditOperation operation,
        DangoPreparedStaticPlatformChange? token, Exception error)
    {
        try
        {
            var current = scene.ReadFurnitureFrame(); EnsureHealthy(current);
            if (token is { } receipt && current.LastAppliedToken == receipt.TokenId && current.WorldGeneration == receipt.WorldGeneration &&
                current.PhysicsStep == receipt.PhysicsStep && current.SceneRevision == receipt.SceneRevision + 1 && MatchesPlatform(current, candidate))
            { MarkApplied(current, candidate, operation); status = "已确认应用并保存（调用返回异常）。"; return; }
            if (SameWorld(old, current))
            {
                observation = current; LastFrame = current.Physics;
                State = OrdinaryFurnitureSceneState.PersistedNotApplied; recoveryOperation = operation; pendingLayout = candidate;
                status = "布局已保存，当前房间尚未应用；可重试应用。";
                return;
            }
        }
        catch (Exception) { }
        State = OrdinaryFurnitureSceneState.RuntimeUnknown;
        status = "布局已保存，运行结果无法确认，已停止：" + error.Message;
    }

    private bool ObserveUnchanged(DangoFurnitureFrame old)
    {
        try { var current = scene.ReadFurnitureFrame(); EnsureHealthy(current); return SameWorld(old, current); }
        catch (Exception) { return false; }
    }
    private static bool SameWorld(DangoFurnitureFrame left, DangoFurnitureFrame right) =>
        JsonSerializer.Serialize(left, SnapshotJson) == JsonSerializer.Serialize(right, SnapshotJson);
    private void TryCancel(DangoPreparedStaticPlatformChange token)
    { try { scene.CancelPreparedStaticPlatformChange(token); } catch (Exception) { } }
    private static void EnsureHealthy(DangoFurnitureFrame frame)
    { if (!frame.Physics.Succeeded || frame.Physics.Bodies.Count != 1) throw new InvalidDataException("真实物理世界不健康。"); }
    private DangoStaticPlatformDefinition ToDefinition(FurniturePlatformInstance item) => scene.OrdinaryFurnitureDefinition(item);
    private bool MatchesPlatform(DangoFurnitureFrame frame, FurnitureLayout layout)
    {
        if (layout.Platform is null) return frame.Platform is null && frame.PlatformInstanceId == 0;
        if (frame.Platform is not { } actual || frame.PlatformInstanceId == 0) return false;
        var expected = ToDefinition(layout.Platform);
        return Math.Abs(actual.CenterX - expected.CenterX) <= 1e-6 && Math.Abs(actual.CenterY - expected.CenterY) <= 1e-6 &&
            Math.Abs(actual.Width - expected.Width) <= 1e-6 && Math.Abs(actual.Height - expected.Height) <= 1e-6 &&
            Math.Abs(actual.Friction - expected.Friction) <= 1e-6;
    }
    private static string RejectionMessage(DangoStaticPlatformChangeRejection reason) => reason switch
    {
        DangoStaticPlatformChangeRejection.Penetration => "家具穿入团子，不能确认。",
        DangoStaticPlatformChangeRejection.OutsideRoom => "家具超出房间，不能确认。",
        DangoStaticPlatformChangeRejection.StaleScene => "场景已变化，请重新确认。",
        _ => "当前不能应用家具：" + reason,
    };
    private void FinishCompletion()
    { var completed = completion; completion = null; completed?.TrySetResult(); }
    private void CheckOwner()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("家具世界只允许创建线程操作。");
    }
    public void Dispose()
    {
        if (disposed) return;
        CheckOwner();
        disposed = true;
        // Staging touches only temporary bytes. Shutdown never commits it and
        // cleanup waits for that file-only task without borrowing the world.
        if (staging is { } background)
        {
            var heldLease = lease;
            _ = background.ContinueWith(task =>
            {
                try { if (task.IsCompletedSuccessfully) task.Result.Dispose(); else _ = task.Exception; }
                finally { heldLease?.Dispose(); }
            }, TaskScheduler.Default);
        }
        else lease?.Dispose();
        lease = null; staging = null;
        completion?.TrySetCanceled(); completion = null;
    }
}
