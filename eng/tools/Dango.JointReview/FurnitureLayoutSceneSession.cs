using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Core.Furniture;
using Dango.Desktop.Furniture;
using Dango.Physics;

namespace Dango.JointReview;

internal enum FurnitureSceneState { Synced, Staging, PersistedNotApplied, CommitUnknown, RuntimeUnknown, Protected }

/// <summary>One owner and one uninterrupted world. Only final commit runs between steps.</summary>
internal sealed class FurnitureLayoutSceneSession : IDisposable
{
    internal static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    private static readonly JsonSerializerOptions SnapshotJson = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly DangoPhysicsWorld world;
    private readonly FurnitureLayoutStore store;
    private readonly string path;
    private FurnitureLayoutLease? lease;
    private Task<FurnitureLayoutStage>? staging;
    private TaskCompletionSource? completion;
    private FurnitureEditOperation? pendingOperation, lastSuccess, recoveryOperation;
    private FurnitureLayout? pendingLayout;
    private byte[]? baselineBytes;
    private DangoFurnitureFrame observation;
    private ulong appliedNativeInstanceId;
    private bool recoveryRequested, disposed;
    private string status = "选择家具开始布置。";
    private string? previewRejection;

    internal FurnitureLayoutSceneSession(string module, string dataRoot, FurnitureLayoutStore? store = null)
    {
        this.store = store ?? new();
        path = FurnitureLayoutStore.GetLayoutPath(dataRoot);
        var loaded = this.store.Load(path);
        Layout = PersistedLayout = loaded.Layout ?? FurnitureLayout.Empty;
        State = loaded.IsWritable ? FurnitureSceneState.Synced : FurnitureSceneState.Protected;
        if (!loaded.IsWritable) status = loaded.Warning ?? "原布局受保护，暂不能布置。";
        world = DangoPhysicsWorld.Create(module, capacity: 1);
        try
        {
            if (!world.SupportsLiveFurnitureEditing)
                throw new DangoPhysicsException(DangoPhysicsFailureKind.MissingExport, "当前物理模块缺少实时家具能力，不能打开编辑。");
            if (Layout.Platform is { } platform) world.SetStaticPlatform(ToDefinition(platform));
            Entity = world.Add(LocalFurnitureSceneGeometry.SpawnX, LocalFurnitureSceneGeometry.SpawnY);
            // Add clears the native horizontal bounds; set them after the entity exists.
            world.SetHorizontalBounds(LocalFurnitureSceneGeometry.Left, LocalFurnitureSceneGeometry.Right);
            observation = world.ReadFurnitureFrame();
            EnsureHealthy(observation);
            if (!MatchesPlatform(observation, Layout)) throw new InvalidDataException("初始布局与真实家具不一致。");
            LastFrame = observation.Physics;
            appliedNativeInstanceId = observation.PlatformInstanceId;
            OwnedWorldCount++;
        }
        catch { world.Dispose(); throw; }
    }

    internal static int OwnedWorldCount { get; private set; }
    internal DangoEntityId Entity { get; }
    internal FurnitureLayout Layout { get; private set; }
    internal FurnitureLayout PersistedLayout { get; private set; }
    internal FurniturePlatformInstance? Preview { get; private set; }
    internal bool HasPreview => Preview is not null;
    internal bool Busy => completion is not null;
    internal bool CanEdit => !disposed && !Busy && State == FurnitureSceneState.Synced;
    internal bool CanStep => !disposed && State != FurnitureSceneState.RuntimeUnknown;
    internal bool CanRecover => !disposed && !Busy && State is FurnitureSceneState.PersistedNotApplied or FurnitureSceneState.CommitUnknown;
    internal FurnitureSceneState State { get; private set; }
    internal string Status => status;
    internal DangoPhysicsFrame? LastFrame { get; private set; }
    internal ulong WorldGeneration => observation.WorldGeneration;
    internal ulong SceneRevision => observation.SceneRevision;
    internal ulong PhysicsStep => observation.PhysicsStep;
    internal IReadOnlyList<double> CommitBoundaryMilliseconds => boundaryTimes;
    private readonly List<double> boundaryTimes = [];
    internal DangoPhysicsWorld VerificationWorld => world;
    // Failure seams exercise actual native/file outcomes in the isolated verification entry point.
    internal Action? BeforeNativeApply { get; set; }
    internal Action? AfterNativeApply { get; set; }

    internal bool CanConfirm
    {
        get
        {
            CheckOwner();
            if (!CanEdit || Preview is null) return false;
            var readingNative = false;
            try
            {
                Preview.Validate();
                readingNative = true;
                var current = world.ReadFurnitureFrame();
                EnsureHealthy(current);
                if (!MatchesPlatform(current, Layout) || current.PlatformInstanceId != appliedNativeInstanceId)
                    throw new InvalidDataException("真实家具与已应用布局不一致。");
                var probe = world.ProbeStaticPlatformChange(Layout.Platform is null ? DangoStaticPlatformChangeKind.Insert : DangoStaticPlatformChangeKind.Move,
                    current.PlatformInstanceId, ToDefinition(Preview), current.SceneRevision);
                if (!probe.Allowed) { status = previewRejection = RejectionMessage(probe.Rejection); return false; }
                if (status == previewRejection) status = "勾确认后应用并保存。";
                previewRejection = null;
                return true;
            }
            catch (Exception error)
            {
                if (readingNative) State = FurnitureSceneState.RuntimeUnknown;
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
        _ = CanConfirm;
    }

    internal void CancelPreview()
    {
        CheckOwner();
        if (Busy) return;
        Preview = null;
        if (State == FurnitureSceneState.Synced) status = "本次预览已取消。";
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
        var candidate = operation.BuildCandidate(PersistedLayout);
        try
        {
            lease = store.AcquireLease(path);
            if (!lease.Baseline.IsWritable || lease.Baseline.Layout != PersistedLayout)
                throw new InvalidOperationException("布局文件已变化或受保护，不能覆盖。");
            baselineBytes = lease.GetBaselineBytes();
            pendingOperation = operation; pendingLayout = candidate;
            State = FurnitureSceneState.Staging; status = "正在保存…";
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

    internal DangoBodyState Tick(double horizontal, bool squash, bool jump, bool drag, double targetX, double targetY)
    {
        CheckOwner();
        if (recoveryRequested) FinishRecovery();
        if (staging?.IsCompleted == true) FinishStaging();
        if (CanStep)
        {
            try
            {
                var frame = world.Step(FixedStep, [new(Entity, horizontal, squash ? 1 : 0, jump, drag, targetX, targetY)]);
                observation = world.ReadFurnitureFrame(); LastFrame = observation.Physics;
                if (!frame.Succeeded || !observation.Physics.Succeeded) throw new InvalidDataException("物理步失败。");
            }
            catch (Exception error)
            { State = FurnitureSceneState.RuntimeUnknown; status = "物理状态异常，已停止运行：" + error.Message; }
        }
        return LastFrame!.Bodies.Single(body => body.EntityId == Entity);
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
            old = world.ReadFurnitureFrame(); EnsureHealthy(old);
            if (!MatchesPlatform(old, Layout) || old.PlatformInstanceId != appliedNativeInstanceId)
            { State = FurnitureSceneState.RuntimeUnknown; throw new InvalidDataException("真实家具与当前布局不一致。"); }
            var prepared = Prepare(pendingOperation!, pendingLayout!, old);
            if (!prepared.Probe.Allowed || prepared.Token is null)
            {
                State = ObserveUnchanged(old) ? FurnitureSceneState.Synced : FurnitureSceneState.RuntimeUnknown;
                status = State == FurnitureSceneState.Synced ? RejectionMessage(prepared.Probe.Rejection) : "运行状态无法确认，已停止。";
                previewRejection = State == FurnitureSceneState.Synced ? status : null;
                return;
            }
            token = prepared.Token;
            commitAttempted = true;
            var result = stage.Commit();
            if (result.State == FurnitureCommitState.NotCommitted)
            {
                world.CancelPreparedStaticPlatformChange(token.Value); token = null;
                State = FurnitureSceneState.Synced; status = result.Warning ?? "保存失败，原家具保留。";
                return;
            }
            if (result.State == FurnitureCommitState.Unknown)
            {
                recoveryOperation = pendingOperation;
                TryCancel(token.Value); token = null;
                State = ObserveUnchanged(old) ? FurnitureSceneState.CommitUnknown : FurnitureSceneState.RuntimeUnknown;
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
                    bytes.AsSpan().SequenceEqual(FurnitureLayoutSerializer.Serialize(pendingLayout!)))
                { saved = true; PersistedLayout = pendingLayout!; recoveryOperation = pendingOperation; }
                else if (!((loaded.State == FurnitureLayoutLoadState.Missing && baselineBytes is null) ||
                    (loaded.State == FurnitureLayoutLoadState.Valid && bytes is not null && baselineBytes is not null && bytes.AsSpan().SequenceEqual(baselineBytes))))
                {
                    recoveryOperation = pendingOperation;
                    State = old is not null && ObserveUnchanged(old) ? FurnitureSceneState.CommitUnknown : FurnitureSceneState.RuntimeUnknown;
                    status = "保存结果待确认，暂不能继续布置。";
                    return;
                }
            }
            if (saved && old is not null)
                ResolveApplyFailure(old, pendingLayout!, pendingOperation!, token, error);
            else if ((stage is not null && old is null) || (old is not null && !ObserveUnchanged(old)) || State == FurnitureSceneState.RuntimeUnknown)
            { State = FurnitureSceneState.RuntimeUnknown; status = "运行状态无法确认，已停止。"; }
            else { State = FurnitureSceneState.Synced; status = "保存失败：" + error.Message; }
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
            old = world.ReadFurnitureFrame(); EnsureHealthy(old);
            if (!MatchesPlatform(old, Layout) || old.PlatformInstanceId != appliedNativeInstanceId)
            { State = FurnitureSceneState.RuntimeUnknown; throw new InvalidDataException("真实家具与旧布局不一致。"); }
            using var recoveryLease = store.AcquireLease(path);
            var loaded = recoveryLease.Baseline;
            var bytes = recoveryLease.GetBaselineBytes();
            if (State == FurnitureSceneState.CommitUnknown)
            {
                if ((loaded.State == FurnitureLayoutLoadState.Missing && baselineBytes is null) ||
                    (loaded.State == FurnitureLayoutLoadState.Valid && bytes is not null && baselineBytes is not null && bytes.AsSpan().SequenceEqual(baselineBytes)))
                { PersistedLayout = Layout; State = FurnitureSceneState.Synced; status = "已确认未保存，原布局保留。"; pendingLayout = null; recoveryOperation = null; return; }
                if (pendingLayout is null || bytes is null || !bytes.AsSpan().SequenceEqual(FurnitureLayoutSerializer.Serialize(pendingLayout)))
                { status = "文件仍不可确认或已被外部修改，继续保护原件。"; return; }
                PersistedLayout = pendingLayout;
                State = FurnitureSceneState.PersistedNotApplied;
            }
            if (loaded.State != FurnitureLayoutLoadState.Valid || loaded.Layout != PersistedLayout ||
                bytes is null || !bytes.AsSpan().SequenceEqual(FurnitureLayoutSerializer.Serialize(PersistedLayout)))
            { State = FurnitureSceneState.CommitUnknown; status = "保存文件已变化，暂不能应用。"; return; }
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
            if (State == FurnitureSceneState.PersistedNotApplied && old is not null)
                ResolveApplyFailure(old, PersistedLayout, recoveryOperation!, token, error);
            else if (old is null || !ObserveUnchanged(old))
            { State = FurnitureSceneState.RuntimeUnknown; status = "恢复时运行状态无法确认，已停止。"; }
            else status = "恢复未完成：" + error.Message;
        }
        finally { recoveryRequested = false; FinishCompletion(); }
    }

    private DangoStaticPlatformPreparation Prepare(FurnitureEditOperation operation, FurnitureLayout candidate, DangoFurnitureFrame current) =>
        world.PrepareStaticPlatformChange(operation.Kind switch
        {
            FurnitureEditKind.Insert => DangoStaticPlatformChangeKind.Insert,
            FurnitureEditKind.Move => DangoStaticPlatformChangeKind.Move,
            _ => DangoStaticPlatformChangeKind.Delete,
        }, current.PlatformInstanceId, candidate.Platform is null ? null : ToDefinition(candidate.Platform), current.SceneRevision);

    private void ApplyAndConfirm(DangoPreparedStaticPlatformChange token, FurnitureLayout candidate, FurnitureEditOperation operation)
    {
        BeforeNativeApply?.Invoke();
        world.ApplyPreparedStaticPlatformChange(token);
        AfterNativeApply?.Invoke();
        var current = world.ReadFurnitureFrame();
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
        State = FurnitureSceneState.Synced; recoveryOperation = null; pendingLayout = null;
        status = "已应用并保存。";
    }

    private void ResolveApplyFailure(DangoFurnitureFrame old, FurnitureLayout candidate, FurnitureEditOperation operation,
        DangoPreparedStaticPlatformChange? token, Exception error)
    {
        try
        {
            var current = world.ReadFurnitureFrame(); EnsureHealthy(current);
            if (token is { } receipt && current.LastAppliedToken == receipt.TokenId && current.WorldGeneration == receipt.WorldGeneration &&
                current.PhysicsStep == receipt.PhysicsStep && current.SceneRevision == receipt.SceneRevision + 1 && MatchesPlatform(current, candidate))
            { MarkApplied(current, candidate, operation); status = "已确认应用并保存（调用返回异常）。"; return; }
            if (SameWorld(old, current))
            {
                observation = current; LastFrame = current.Physics;
                State = FurnitureSceneState.PersistedNotApplied; recoveryOperation = operation; pendingLayout = candidate;
                status = "布局已保存，当前房间尚未应用；可重试应用。";
                return;
            }
        }
        catch (Exception) { }
        State = FurnitureSceneState.RuntimeUnknown;
        status = "布局已保存，运行结果无法确认，已停止：" + error.Message;
    }

    private bool ObserveUnchanged(DangoFurnitureFrame old)
    {
        try { var current = world.ReadFurnitureFrame(); EnsureHealthy(current); return SameWorld(old, current); }
        catch (Exception) { return false; }
    }
    private static bool SameWorld(DangoFurnitureFrame left, DangoFurnitureFrame right) =>
        JsonSerializer.Serialize(left, SnapshotJson) == JsonSerializer.Serialize(right, SnapshotJson);
    private void TryCancel(DangoPreparedStaticPlatformChange token)
    { try { world.CancelPreparedStaticPlatformChange(token); } catch (Exception) { } }
    private static void EnsureHealthy(DangoFurnitureFrame frame)
    { if (!frame.Physics.Succeeded || frame.Physics.Bodies.Count != 1) throw new InvalidDataException("真实物理世界不健康。"); }
    private static DangoStaticPlatformDefinition ToDefinition(FurniturePlatformInstance item) => new(item.Anchor.X,
        item.Anchor.Y - LocalFurnitureSceneGeometry.PlatformHeight / 2, LocalFurnitureSceneGeometry.PlatformWidth,
        LocalFurnitureSceneGeometry.PlatformHeight, LocalFurnitureSceneGeometry.PlatformFriction);
    private static bool MatchesPlatform(DangoFurnitureFrame frame, FurnitureLayout layout)
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
        if (Busy) throw new InvalidOperationException("家具提交尚未完成，不能提前释放世界。");
        world.Dispose(); disposed = true; OwnedWorldCount--;
    }
}
