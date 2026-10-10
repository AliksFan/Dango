using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Core.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Physics;

namespace Dango.Desktop.Furniture;

internal enum OrdinaryFurnitureSceneState { Synced, Staging, PersistedNotApplied, CommitUnknown, RuntimeUnknown, Protected }

internal sealed record OrdinaryRoomFurnitureRenderItem(Guid InstanceId, FurnitureItemKind Kind,
    FurnitureAnchor Anchor, double AngleRadians = 0);

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
    private RoomFurnitureEditOperation? pendingOperation, lastSuccess, recoveryOperation;
    private FurnitureLayout? pendingLayout;
    private byte[]? baselineBytes;
    private DangoFurnitureCollectionFrame? observation;
    private Dictionary<Guid, ulong> appliedInstances = [];
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
        capability = scene.SupportsFurnitureCollection;
        if (!capability && loaded.IsWritable) status = "当前物理模块不支持家具编辑。";
    }
    internal FurnitureLayout Layout { get; private set; }
    internal FurnitureLayout PersistedLayout { get; private set; }
    internal FurnitureItemInstance? Preview { get; private set; }
    internal Guid? SelectedInstanceId { get; private set; }
    internal bool HasPreview => Preview is not null;
    internal bool Busy => completion is not null;
    internal bool CanEdit => !disposed && IsAttached && capability && ownsActiveRoom() && !Busy && State == OrdinaryFurnitureSceneState.Synced;
    internal bool CanStep => !disposed && State != OrdinaryFurnitureSceneState.RuntimeUnknown;
    internal bool CanRecover => !disposed && IsAttached && capability && ownsActiveRoom() && !Busy && State is OrdinaryFurnitureSceneState.PersistedNotApplied or OrdinaryFurnitureSceneState.CommitUnknown;
    internal OrdinaryFurnitureSceneState State { get; private set; }
    internal string Status => status;
    internal DangoPhysicsFrame? LastFrame { get; private set; }
    internal DangoFurnitureCollectionFrame? NativeFrame => observation;
    internal IReadOnlyList<OrdinaryRoomFurnitureRenderItem> RenderItems
    {
        get
        {
            if (observation is null || !IsAttached) return [];
            var items = new List<OrdinaryRoomFurnitureRenderItem>(Layout.Items.Count);
            foreach (var item in Layout.Items)
            {
                if (!appliedInstances.TryGetValue(item.InstanceId, out var runtimeId)) continue;
                var actual = observation.Items.SingleOrDefault(value => value.InstanceId == runtimeId);
                if (actual is null) continue;
                if (item.Kind == FurnitureItemKind.RoundBall && actual.BallState is { } ball)
                    items.Add(new(item.InstanceId, item.Kind, scene.OrdinaryBallAnchor(ball), ball.AngleRadians));
                else items.Add(new(item.InstanceId, item.Kind, item.Anchor));
            }
            return items.AsReadOnly();
        }
    }
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
            if (Layout.Items.Count != 0) return false;
            IsAttached = true;
            return true;
        }
        try
        {
            var current = scene.ReadFurnitureCollectionFrame();
            EnsureHealthy(current);
            if (current.Items.Count != 0)
                throw new InvalidDataException("离房后仍存在家具绑定，不能重复进入。");
            appliedInstances.Clear();
            foreach (var item in Layout.Items)
            {
                var before = current.Items.Select(value => value.InstanceId).ToHashSet();
                var prepared = scene.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert, 0,
                    ToDefinition(item), current.SceneRevision);
                if (!prepared.Probe.Allowed || prepared.Token is null)
                    throw new InvalidDataException("已保存家具无法载入：" + RejectionMessage(prepared.Probe.Rejection));
                current = scene.ApplyPreparedFurnitureChange(prepared.Token.Value);
                var added = current.Items.Where(value => !before.Contains(value.InstanceId)).ToArray();
                if (added.Length != 1) throw new InvalidDataException("家具载入实例身份不一致。");
                appliedInstances.Add(item.InstanceId, added[0].InstanceId);
            }
            EnsureHealthy(current);
            if (!MatchesItems(current, Layout, appliedInstances)) throw new InvalidDataException("家具初始布局未真实应用。");
            observation = current; LastFrame = current.Physics;
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
        DangoPreparedFurnitureChange? token = null;
        try
        {
            if (!MappingIsCurrent()) throw new InvalidDataException("房间坐标绑定已变化。");
            var current = scene.ReadFurnitureCollectionFrame(); EnsureHealthy(current);
            if (!MatchesItems(current, Layout, appliedInstances))
                throw new InvalidDataException("离房前真实家具与布局不一致。");
            foreach (var runtimeId in current.Items.Select(value => value.InstanceId).ToArray())
            {
                var prepared = scene.PrepareFurnitureChange(DangoFurnitureChangeKind.Delete,
                    runtimeId, null, current.SceneRevision);
                if (!prepared.Probe.Allowed || prepared.Token is null)
                    throw new InvalidDataException(RejectionMessage(prepared.Probe.Rejection));
                token = prepared.Token;
                current = scene.ApplyPreparedFurnitureChange(token.Value);
                token = null;
            }
            EnsureHealthy(current);
            if (current.Items.Count != 0)
                throw new InvalidDataException("家具尚未卸载，禁止换场。");
            observation = current; LastFrame = current.Physics;
            appliedInstances.Clear();
            IsAttached = false; Preview = null; SelectedInstanceId = null;
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
                var moving = Layout.Find(Preview.InstanceId) is not null;
                var operation = new RoomFurnitureEditOperation(Guid.NewGuid(), Layout.LayoutVersion,
                    moving ? FurnitureEditKind.Move : FurnitureEditKind.Insert, moving ? Preview.InstanceId : null, Preview);
                _ = operation.BuildCandidate(Layout);
                readingNative = true;
                var current = scene.ReadFurnitureCollectionFrame();
                EnsureHealthy(current);
                if (!MatchesItems(current, Layout, appliedInstances))
                    throw new InvalidDataException("真实家具与已应用布局不一致。");
                var probe = scene.ProbeFurnitureChange(moving ? DangoFurnitureChangeKind.Move : DangoFurnitureChangeKind.Insert,
                    moving ? appliedInstances[Preview.InstanceId] : 0, ToDefinition(Preview), current.SceneRevision);
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
        var existing = Layout.Platform;
        if (existing is not null) PreviewMoveFurniture(existing.InstanceId, new(anchorX, anchorY));
        else if (Preview is { Kind: FurnitureItemKind.StaticPlatform } preview)
            PreviewMoveFurniture(preview.InstanceId, new(anchorX, anchorY));
        else PreviewNewFurniture(FurnitureItemKind.StaticPlatform, new(anchorX, anchorY));
    }

    internal void SelectFurniture(Guid? instanceId)
    {
        CheckOwner();
        if (!CanEdit || (instanceId is { } id && Layout.Find(id) is null)) return;
        Preview = null;
        SelectedInstanceId = instanceId;
    }

    internal void PreviewNewFurniture(FurnitureItemKind kind, FurnitureAnchor anchor)
    {
        CheckOwner();
        if (!CanEdit) return;
        if (Layout.Items.Count >= FurnitureLayoutContract.OrdinaryRoom.MaximumItems)
        { status = "最多放置 30 件家具，请先删除一件。"; return; }
        Preview = new(Guid.NewGuid(), kind, anchor);
        SelectedInstanceId = null;
        status = "勾确认后应用并保存。";
    }

    internal void PreviewMoveFurniture(Guid instanceId, FurnitureAnchor anchor)
    {
        CheckOwner();
        if (!CanEdit) return;
        var item = Layout.Find(instanceId) ?? (Preview?.InstanceId == instanceId ? Preview : null);
        if (item is null) return;
        Preview = item with { Anchor = anchor };
        SelectedInstanceId = Layout.Find(instanceId) is null ? null : instanceId;
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
        var moving = Layout.Find(Preview!.InstanceId) is not null;
        return SubmitRoomOperationAsync(new(Guid.NewGuid(), PersistedLayout.LayoutVersion,
            moving ? FurnitureEditKind.Move : FurnitureEditKind.Insert,
            moving ? Preview.InstanceId : null, Preview));
    }

    internal Task DeleteAsync()
    {
        CheckOwner();
        if (!CanEdit || SelectedInstanceId is not { } id || Layout.Find(id) is null) return Task.CompletedTask;
        return SubmitRoomOperationAsync(new(Guid.NewGuid(), PersistedLayout.LayoutVersion, FurnitureEditKind.Delete, id, null));
    }

    internal Task SubmitAsync(FurnitureEditOperation operation) => SubmitRoomOperationAsync(new(
        operation.OperationId, operation.ExpectedVersion, operation.Kind, operation.TargetInstanceId,
        operation.Candidate is { } item ? new(item.InstanceId, FurnitureItemKind.StaticPlatform, item.Anchor) : null));

    internal Task SubmitRoomOperationAsync(RoomFurnitureEditOperation operation)
    {
        CheckOwner();
        if (lastSuccess?.OperationId == operation.OperationId)
        {
            if (lastSuccess != operation) throw new InvalidOperationException("同一操作标识不能改变内容。");
            return Task.CompletedTask;
        }
        if (!CanEdit || Layout != PersistedLayout) throw new InvalidOperationException("当前布局尚未对齐，不能继续提交。");
        var candidate = operation.BuildCandidate(PersistedLayout);
        if (candidate == PersistedLayout)
        { Preview = null; lastSuccess = operation; status = "位置未改变。"; return Task.CompletedTask; }
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
            var current = scene.ReadFurnitureCollectionFrame();
            EnsureHealthy(current);
            if (!MatchesItems(current, Layout, appliedInstances) ||
                current.Physics.Bodies.Single().EntityId != attachedEntity)
                throw new InvalidDataException("普通房家具与团子运行绑定不一致。");
            observation = current; LastFrame = current.Physics;
        }
        catch (Exception error) { FailRuntime("物理状态异常，已停止运行：" + error.Message); }
    }
    private void FinishStaging()
    {
        FurnitureLayoutStage? stage = null;
        DangoPreparedFurnitureChange? token = null;
        var saved = false;
        var commitAttempted = false;
        var start = Stopwatch.GetTimestamp();
        DangoFurnitureCollectionFrame? old = null;
        try
        {
            stage = staging!.GetAwaiter().GetResult();
            old = scene.ReadFurnitureCollectionFrame(); EnsureHealthy(old);
            if (!MatchesItems(old, Layout, appliedInstances))
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
                scene.CancelPreparedFurnitureChange(token.Value); token = null;
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
        DangoPreparedFurnitureChange? token = null;
        DangoFurnitureCollectionFrame? old = null;
        try
        {
            old = scene.ReadFurnitureCollectionFrame(); EnsureHealthy(old);
            if (!MatchesItems(old, Layout, appliedInstances))
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

    private DangoFurniturePreparation Prepare(RoomFurnitureEditOperation operation, FurnitureLayout candidate, DangoFurnitureCollectionFrame current) =>
        scene.PrepareFurnitureChange(operation.Kind switch
        {
            FurnitureEditKind.Insert => DangoFurnitureChangeKind.Insert,
            FurnitureEditKind.Move => DangoFurnitureChangeKind.Move,
            _ => DangoFurnitureChangeKind.Delete,
        }, operation.TargetInstanceId is { } id ? appliedInstances[id] : 0,
            operation.Candidate is null ? null : ToDefinition(operation.Candidate), current.SceneRevision);

    private void ApplyAndConfirm(DangoPreparedFurnitureChange token, FurnitureLayout candidate, RoomFurnitureEditOperation operation)
    {
        BeforeNativeApply?.Invoke();
        scene.ApplyPreparedFurnitureChange(token);
        AfterNativeApply?.Invoke();
        var current = scene.ReadFurnitureCollectionFrame();
        EnsureHealthy(current);
        if (current.LastAppliedToken != token.TokenId || current.WorldGeneration != token.WorldGeneration ||
            current.PhysicsStep != token.PhysicsStep || current.SceneRevision != token.SceneRevision + 1 ||
            !TryCandidateBinding(current, candidate, operation, out _))
            throw new InvalidDataException("文件已保存，但真实家具应用结果无法确认。");
        MarkApplied(current, candidate, operation);
    }

    private void MarkApplied(DangoFurnitureCollectionFrame current, FurnitureLayout candidate, RoomFurnitureEditOperation operation)
    {
        observation = current; LastFrame = current.Physics;
        if (!TryCandidateBinding(current, candidate, operation, out var bindings))
            throw new InvalidDataException("家具身份或定义与已确认布局不一致。");
        appliedInstances = bindings;
        Layout = candidate; lastSuccess = operation; Preview = null;
        SelectedInstanceId = operation.Kind == FurnitureEditKind.Delete ? null : operation.Candidate?.InstanceId;
        State = OrdinaryFurnitureSceneState.Synced; recoveryOperation = null; pendingLayout = null;
        status = "已应用并保存。";
    }

    private void ResolveApplyFailure(DangoFurnitureCollectionFrame old, FurnitureLayout candidate, RoomFurnitureEditOperation operation,
        DangoPreparedFurnitureChange? token, Exception error)
    {
        try
        {
            var current = scene.ReadFurnitureCollectionFrame(); EnsureHealthy(current);
            if (token is { } receipt && current.LastAppliedToken == receipt.TokenId && current.WorldGeneration == receipt.WorldGeneration &&
                current.PhysicsStep == receipt.PhysicsStep && current.SceneRevision == receipt.SceneRevision + 1 &&
                TryCandidateBinding(current, candidate, operation, out _))
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

    private bool ObserveUnchanged(DangoFurnitureCollectionFrame old)
    {
        try { var current = scene.ReadFurnitureCollectionFrame(); EnsureHealthy(current); return SameWorld(old, current); }
        catch (Exception) { return false; }
    }
    private static bool SameWorld(DangoFurnitureCollectionFrame left, DangoFurnitureCollectionFrame right) =>
        JsonSerializer.Serialize(left, SnapshotJson) == JsonSerializer.Serialize(right, SnapshotJson);
    private void TryCancel(DangoPreparedFurnitureChange token)
    { try { scene.CancelPreparedFurnitureChange(token); } catch (Exception) { } }
    private static void EnsureHealthy(DangoFurnitureCollectionFrame frame)
    { if (!frame.Physics.Succeeded || frame.Physics.Bodies.Count != 1) throw new InvalidDataException("真实物理世界不健康。"); }
    private DangoFurnitureDefinition ToDefinition(FurnitureItemInstance item) => scene.OrdinaryFurnitureDefinition(item);

    private bool TryCandidateBinding(DangoFurnitureCollectionFrame frame, FurnitureLayout layout,
        RoomFurnitureEditOperation operation, out Dictionary<Guid, ulong> bindings)
    {
        bindings = new(appliedInstances);
        if (operation.Kind == FurnitureEditKind.Insert)
        {
            var known = appliedInstances.Values.ToHashSet();
            var added = frame.Items.Where(item => !known.Contains(item.InstanceId)).ToArray();
            if (operation.Candidate is null || added.Length != 1 || bindings.ContainsKey(operation.Candidate.InstanceId)) return false;
            bindings.Add(operation.Candidate.InstanceId, added[0].InstanceId);
        }
        else if (operation.Kind == FurnitureEditKind.Delete)
        {
            if (operation.TargetInstanceId is not { } removed || !bindings.Remove(removed)) return false;
        }
        return MatchesItems(frame, layout, bindings);
    }

    private bool MatchesItems(DangoFurnitureCollectionFrame frame, FurnitureLayout layout, IReadOnlyDictionary<Guid, ulong> bindings)
    {
        if (frame.Items.Count != layout.Items.Count || bindings.Count != layout.Items.Count) return false;
        var actual = frame.Items.ToDictionary(item => item.InstanceId);
        var seen = new HashSet<ulong>();
        foreach (var item in layout.Items)
        {
            if (!bindings.TryGetValue(item.InstanceId, out var id) || id == 0 || !seen.Add(id) ||
                !actual.TryGetValue(id, out var runtime) || !DefinitionMatches(runtime.Definition, ToDefinition(item))) return false;
        }
        return true;
    }

    private static bool DefinitionMatches(DangoFurnitureDefinition actual, DangoFurnitureDefinition expected)
    {
        static bool Close(double left, double right) => Math.Abs(left - right) <= 1e-6;
        if (actual.Kind != expected.Kind) return false;
        if (actual.Platform is { } platform && expected.Platform is { } wanted)
            return Close(platform.CenterX, wanted.CenterX) && Close(platform.CenterY, wanted.CenterY) &&
                Close(platform.Width, wanted.Width) && Close(platform.Height, wanted.Height) && Close(platform.Friction, wanted.Friction);
        if (actual.Ball is { } ball && expected.Ball is { } wantedBall)
            return Close(ball.CenterQX, wantedBall.CenterQX) && Close(ball.CenterQY, wantedBall.CenterQY) &&
                Close(ball.RadiusQ, wantedBall.RadiusQ) && Close(ball.Mass, wantedBall.Mass) &&
                Close(ball.FloorFriction, wantedBall.FloorFriction) && Close(ball.DangoFriction, wantedBall.DangoFriction) &&
                Close(ball.FloorRestitution, wantedBall.FloorRestitution) && Close(ball.DangoRestitution, wantedBall.DangoRestitution);
        return false;
    }
    private static string RejectionMessage(DangoFurnitureChangeRejection reason) => reason switch
    {
        DangoFurnitureChangeRejection.Penetration => "家具碰撞重叠，不能确认。",
        DangoFurnitureChangeRejection.CapacityExceeded => "最多放置 30 件家具。",
        DangoFurnitureChangeRejection.OutsideRoom => "家具超出房间，不能确认。",
        DangoFurnitureChangeRejection.StaleScene => "场景已变化，请重新确认。",
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
