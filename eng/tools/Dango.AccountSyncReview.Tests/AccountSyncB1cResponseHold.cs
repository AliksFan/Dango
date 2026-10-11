using System.Net;
using System.Net.Http;
using System.Text.Json;
using Dango.Protocol;

namespace Dango.AccountSyncReview.Tests;

internal sealed record AccountSyncB1cHoldSnapshot(string Case, string Method, Guid UserId, Guid PetId,
    Guid SessionId, long ResponseVersion, bool CancellationObserved, bool Released, bool Settled,
    bool CleanupRelease, bool ActualSuccessfulResponse);

// Only an already buffered real response may enter this barrier. Request cancellation is
// evidence, not release; fixture cancellation and the independent hard deadline still stop it.
internal sealed class AccountSyncB1cResponseHold : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan bound;
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource captured = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? actual;
    private Task? disposal;
    private string? caseName, method;
    private Guid user, pet, session;
    private long version;
    private bool cancelled, released, settled, cleanupRelease, successful, disposed, lifetimeDisposed;
    internal AccountSyncB1cResponseHold(TimeSpan? bound = null)
    {
        this.bound = bound ?? TimeSpan.FromSeconds(30);
        if (this.bound <= TimeSpan.Zero || this.bound > TimeSpan.FromSeconds(30)) throw Reject();
        _ = captured.Task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    internal Task Captured => captured.Task;
    internal void Arm(string name, string heldMethod, Guid userId, Guid petId, Guid sessionId)
    {
        lock (gate)
        {
            if (disposed || caseName is not null || name is not ("late-get" or "late-patch")
                || heldMethod != (name == "late-get" ? "GET" : "PATCH")
                || userId == Guid.Empty || petId == Guid.Empty || sessionId == Guid.Empty) throw Reject();
            caseName = name; method = heldMethod; user = userId; pet = petId; session = sessionId;
        }
    }
    internal bool Matches(string requestMethod, string path, Guid userId, Guid sessionId)
    {
        lock (gate) return !disposed && actual is null && caseName is not null && method == requestMethod
            && path == (method == "GET" ? "/api/v1/me/pet" : "/api/v1/me/pet/configuration")
            && user == userId && session == sessionId;
    }
    internal Task<HttpResponseMessage> CaptureAsync(HttpResponseMessage response, PetConfigurationSnapshot snapshot,
        Guid sessionId, CancellationToken requestCancellation, Action? onValidatedCapture = null)
    {
        lock (gate)
        {
            if (disposed || actual is not null || caseName is null || response.StatusCode != HttpStatusCode.OK
                || snapshot.UserId != user || snapshot.PetId != pet || sessionId != session
                || snapshot.ConfigurationVersion != (method == "GET" ? 1 : 2)) throw Reject();
            version = snapshot.ConfigurationVersion;
            var task = RunAsync(response, snapshot, requestCancellation, onValidatedCapture); actual = task;
            _ = task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }
    private async Task<HttpResponseMessage> RunAsync(HttpResponseMessage response, PetConfigurationSnapshot snapshot, CancellationToken requestCancellation,
        Action? onValidatedCapture)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(bound);
        using var registration = requestCancellation.Register(() => { lock (gate) cancelled = true; });
        try
        {
            // Publish the original task before validation can call into the fixture.
            // The hard deadline already includes this scheduling boundary.
            await Task.Yield();
            deadline.Token.ThrowIfCancellationRequested();
            if (response.Content.Headers.ContentLength > 16384 || response.Content.Headers.ContentType?.MediaType != "application/json") throw Reject();
            await response.Content.LoadIntoBufferAsync(16384, deadline.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            if (bytes.Length is <= 0 or > 16384) throw Reject();
            using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 16, AllowDuplicateProperties = false });
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object || json.EnumerateObject().Count() != 4
                || json.GetProperty("userId").GetGuid() != snapshot.UserId || json.GetProperty("petId").GetGuid() != snapshot.PetId
                || json.GetProperty("configurationVersion").GetInt64() != snapshot.ConfigurationVersion
                || !AppearanceContract.Equivalent(AppearanceContract.ValidateAndClone(json.GetProperty("configuration")), snapshot.Configuration)) throw Reject();
            deadline.Token.ThrowIfCancellationRequested();
            onValidatedCapture?.Invoke();
            lock (gate) { if (disposed) throw new OperationCanceledException(lifetime.Token); successful = true; }
            captured.TrySetResult();
            await release.Task.WaitAsync(bound, deadline.Token); return response;
        }
        catch (Exception error) { captured.TrySetException(error); response.Dispose(); throw; }
        finally { lock (gate) settled = true; }
    }
    internal void Release(bool cleanup = false)
    {
        lock (gate)
        {
            if (actual is null) { if (cleanup) return; throw Reject(); }
            if (disposed || settled) { if (cleanup) return; throw Reject(); }
            if (!successful && !cleanup) throw Reject();
            if (released) { if (cleanup) return; throw Reject(); }
            cleanupRelease = cleanup; released = true; release.TrySetResult();
        }
    }
    internal AccountSyncB1cHoldSnapshot Snapshot()
    {
        lock (gate) return new(caseName ?? "", method ?? "", user, pet, session, version,
            cancelled, released, settled, cleanupRelease, successful);
    }
    public ValueTask DisposeAsync() { lock (gate) return new(disposal ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        Task? task;
        lock (gate) { disposed = true; captured.TrySetCanceled(lifetime.Token); release.TrySetCanceled(lifetime.Token); task = actual; }
        lifetime.Cancel();
        try { if (task is not null) { try { await task.WaitAsync(bound); } catch when (task.IsCompleted) { _ = task.Exception; } } }
        finally
        {
            if (task is null || task.IsCompleted) { lock (gate) DisposeLifetimeWhenSettled(); }
            else _ = task.ContinueWith(_ => { lock (gate) DisposeLifetimeWhenSettled(); }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private void DisposeLifetimeWhenSettled()
    {
        // An unresponsive operation may outlive the bounded disposal caller. The
        // continuation still retires the CTS exactly once when it actually settles.
        if (disposed && !lifetimeDisposed) { lifetimeDisposed = true; lifetime.Dispose(); }
    }
    private static InvalidOperationException Reject() => new("accountsync_b1c_hold_rejected");
}
