using System.Net;
using System.Net.Http;
using System.IO;
using Dango.Server.Pets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncB1bFaultControl")]
public sealed class AccountSyncB1bFaultControlTests
{
    private const string Primary = AccountSyncB1bFaultControl.PrimaryDevice;
    private const string Secondary = AccountSyncB1bFaultControl.SecondaryDevice;
    private const string Path = AccountSyncB1bFaultControl.ConfigurationPath;

    [TestMethod]
    public async Task CasRequiresTwoArrivalsAndRetainsDistinctBackendIdentities()
    {
        await using var control = new AccountSyncB1bFaultControl();
        control.ArmCas();
        await ForwardCasPair(control);
        var opened = 0;
        var first = control.CreateOfflineCheckpoint(_ => { opened++; return Task.FromResult(101); });
        var second = control.CreateOfflineCheckpoint(_ => { opened++; return Task.FromResult(202); });
        await first.ReachedAsync("after_locks", CancellationToken.None);
        Assert.AreEqual(0, opened, "Only before_locks may open a connection or enter the barrier.");
        var pending = first.ReachedAsync("before_locks", CancellationToken.None);
        Assert.IsFalse(pending.IsCompleted, "One arrival cannot pass the barrier.");
        await Task.WhenAll(pending, second.ReachedAsync("before_locks", CancellationToken.None));
        var snapshot = control.Snapshot();
        Assert.AreEqual(2, opened);
        Assert.AreEqual(2, snapshot.CasArrivals);
        Assert.AreEqual(2, snapshot.CasCompleted);
        CollectionAssert.AreEquivalent(new[] { 101, 202 }, snapshot.BackendPids.ToArray());
        Assert.AreEqual(2, snapshot.Events.Count(e => e.Event == "before-locks-arrived"));
        Assert.AreEqual(2, snapshot.Events.Count(e => e.Event == "before-locks-released"));
        Assert.AreEqual(0, snapshot.ActiveOperations);
        control.AssertSettled();
        Assert.ThrowsExactly<InvalidOperationException>(control.ArmCas);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first.ReachedAsync("before_locks", CancellationToken.None));
    }

    [TestMethod]
    public async Task ReusedBackendPidFailsBothBarrierParticipants()
    {
        await using var control = new AccountSyncB1bFaultControl();
        control.ArmCas(); await ForwardCasPair(control);
        var first = control.CreateOfflineCheckpoint(_ => Task.FromResult(101)).ReachedAsync("before_locks", CancellationToken.None);
        var second = control.CreateOfflineCheckpoint(_ => Task.FromResult(101)).ReachedAsync("before_locks", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => second);
        Assert.IsTrue(control.Snapshot().Failed);
        Assert.AreEqual(0, control.Snapshot().ActiveOperations);
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
    }

    [TestMethod]
    public async Task MissingSecondCasArrivalTimesOutAndCannotBeAccepted()
    {
        await using var control = ShortBounds();
        control.ArmCas();
        await Forward(control, Primary);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => control.CreateOfflineCheckpoint(_ => Task.FromResult(101))
            .ReachedAsync("before_locks", CancellationToken.None));
        Assert.IsTrue(control.Snapshot().Events.Any(e => e.Event == "cas_wait_timeout"));
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
    }

    [TestMethod]
    public async Task DropAcceptedResponseClaimsOnlyPrimaryConfigurationOkAndOnlyOnce()
    {
        await using var control = new AccountSyncB1bFaultControl();
        control.ArmDropAcceptedResponse(false);
        using var secondary = Response(HttpStatusCode.OK, out var secondaryContent);
        await After(control, Secondary, secondary);
        using var conflict = Response(HttpStatusCode.Conflict, out var conflictContent);
        await After(control, Primary, conflict);
        using var otherPath = Response(HttpStatusCode.OK, out var otherContent);
        await control.AfterResponseAsync(Primary, "PATCH", "/api/v1/me/profile", otherPath, CancellationToken.None);
        Assert.AreEqual(0, control.Snapshot().AcceptedDropHits);
        Assert.IsFalse(secondaryContent.Disposed || conflictContent.Disposed || otherContent.Disposed);
        using var accepted = Response(HttpStatusCode.OK, out var content);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => After(control, Primary, accepted));
        Assert.IsTrue(content.Disposed);
        using var following = Response(HttpStatusCode.OK, out var followingContent);
        await After(control, Primary, following);
        Assert.IsFalse(followingContent.Disposed, "A second response must remain owned by its caller.");
        Assert.AreEqual(1, control.Snapshot().AcceptedDropHits);
        Assert.AreEqual(1, control.Snapshot().Events.Count(e => e.Event == "accepted-response-discarded"));
        control.AssertSettled();
        Assert.ThrowsExactly<InvalidOperationException>(() => control.ArmDropAcceptedResponse(false));
    }

    [TestMethod]
    public async Task HoldAllowsSecondaryResponseThenReleaseDisposesAndLosesPrimaryResponse()
    {
        await using var control = new AccountSyncB1bFaultControl();
        control.ArmDropAcceptedResponse(true);
        using var accepted = Response(HttpStatusCode.OK, out var content);
        var pending = After(control, Primary, accepted);
        await control.WaitForAcceptedPatchAsync();
        Assert.IsFalse(pending.IsCompleted);
        Assert.IsFalse(content.Disposed, "The accepted response stays alive during the bounded hold.");
        using var secondary = Response(HttpStatusCode.OK, out var secondaryContent);
        await After(control, Secondary, secondary);
        Assert.IsFalse(secondaryContent.Disposed);
        Assert.ThrowsExactly<InvalidOperationException>(control.ArmDropBeforeForward);
        control.ReleaseAcceptedPatch();
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => pending);
        Assert.IsTrue(content.Disposed);
        Assert.AreEqual(1, control.Snapshot().AcceptedHoldHits);
        Assert.AreEqual(0, control.Snapshot().ActiveOperations);
        control.AssertSettled();
        Assert.ThrowsExactly<InvalidOperationException>(control.ReleaseAcceptedPatch);
    }

    [TestMethod]
    public async Task HeldAcceptedResponseHasAFiniteDeadlineAndRetainsFailureEvidence()
    {
        await using var control = ShortBounds();
        control.ArmDropAcceptedResponse(true);
        using var accepted = Response(HttpStatusCode.OK, out var content);
        var pending = After(control, Primary, accepted);
        await control.WaitForAcceptedPatchAsync();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => pending);
        Assert.IsTrue(content.Disposed);
        Assert.IsTrue(control.Snapshot().Events.Any(e => e.Event == "accepted_response_hold_timeout"));
        Assert.AreEqual(0, control.Snapshot().ActiveOperations);
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
    }

    [TestMethod]
    public async Task WaitForAcceptedPatchCannotWaitForeverWhenNoSuccessfulResponseArrives()
    {
        await using var control = ShortBounds();
        control.ArmDropAcceptedResponse(true);
        await Assert.ThrowsExactlyAsync<TimeoutException>(control.WaitForAcceptedPatchAsync);
        Assert.IsTrue(control.Snapshot().Events.Any(e => e.Event == "accepted_patch_wait_timeout"));
        Assert.AreEqual(0, control.Snapshot().ActiveOperations);
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
    }

    [TestMethod]
    public async Task BeforeForwardDropIsOneUseAndLeavesSecondOriginalRequestUnchanged()
    {
        await using var control = new AccountSyncB1bFaultControl();
        control.ArmDropBeforeForward();
        await Forward(control, Secondary);
        await control.BeforeForwardAsync(Primary, "GET", "/api/v1/me/pet", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => Forward(control, Primary));
        await Forward(control, Primary);
        Assert.AreEqual(1, control.Snapshot().BeforeForwardDropHits);
        Assert.AreEqual(1, control.Snapshot().Events.Count(e => e.Event == "reserved-patch-not-forwarded"));
        control.AssertSettled();
        Assert.ThrowsExactly<InvalidOperationException>(control.ArmDropBeforeForward);
    }

    [TestMethod]
    public async Task DisposeReleasesAndDrainsWaitingCasAndRetainsTheAbandonedPhase()
    {
        var control = new AccountSyncB1bFaultControl();
        control.ArmCas(); await Forward(control, Primary);
        var pending = control.CreateOfflineCheckpoint(_ => Task.FromResult(101)).ReachedAsync("before_locks", CancellationToken.None);
        await control.DisposeAsync();
        await RequireClosed(pending);
        var snapshot = control.Snapshot();
        Assert.IsTrue(snapshot.Disposed && snapshot.Failed);
        Assert.AreEqual(0, snapshot.ActiveOperations);
        Assert.IsTrue(snapshot.Events.Any(e => e.Event == "dispose-drained"));
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
        await control.DisposeAsync();
    }

    [TestMethod]
    public async Task DisposeReleasesHeldResponseAndDrainsBothCallbackAndAcceptedWaiter()
    {
        var control = new AccountSyncB1bFaultControl();
        control.ArmDropAcceptedResponse(true);
        using var accepted = Response(HttpStatusCode.OK, out var content);
        var pending = After(control, Primary, accepted);
        await control.WaitForAcceptedPatchAsync();
        await control.DisposeAsync();
        await RequireClosed(pending);
        Assert.IsTrue(content.Disposed);
        Assert.AreEqual(0, control.Snapshot().ActiveOperations);
        Assert.IsTrue(control.Snapshot().Events.Any(e => e.Event == "dispose-drained"));
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
    }

    [TestMethod]
    public async Task CancellationIgnoringConnectionOpenRemainsOwnedAndMakesDrainFail()
    {
        var control = ShortBounds();
        var opening = new TaskCompletionSource<int>();
        control.ArmCas(); await Forward(control, Primary);
        var pending = control.CreateOfflineCheckpoint(_ => opening.Task).ReachedAsync("before_locks", CancellationToken.None);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => pending);
        Assert.AreEqual(1, control.Snapshot().PendingConnectionTasks);
        Assert.AreEqual(1, control.Snapshot().ActiveOperations);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => control.DisposeAsync().AsTask());
        Assert.IsTrue(control.Snapshot().Events.Any(e => e.Event == "fault_control_drain_timeout"));
        opening.SetResult(101); // ExecuteSynchronously retires the still-owned fake open.
        Assert.AreEqual(0, control.Snapshot().PendingConnectionTasks);
        Assert.AreEqual(0, control.Snapshot().ActiveOperations);
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
    }

    [TestMethod]
    public async Task UnconsumedArmsUnknownDevicesAndWidenedBoundsCannotPass()
    {
        await using var control = new AccountSyncB1bFaultControl();
        control.ArmDropBeforeForward();
        Assert.ThrowsExactly<InvalidOperationException>(control.AssertSettled);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Forward(control, "third-device"));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AccountSyncB1bFaultControl(
            TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20)));
        Assert.ThrowsExactly<InvalidOperationException>(() => new AccountSyncB1bFaultControl(
            TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromSeconds(20)));
    }

    private static AccountSyncB1bFaultControl ShortBounds() => new(TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    private static Task Forward(AccountSyncB1bFaultControl control, string device) =>
        control.BeforeForwardAsync(device, "PATCH", Path, CancellationToken.None);
    private static async Task ForwardCasPair(AccountSyncB1bFaultControl control)
    { await Forward(control, Primary); await Forward(control, Secondary); }
    private static Task After(AccountSyncB1bFaultControl control, string device, HttpResponseMessage response) =>
        control.AfterResponseAsync(device, "PATCH", Path, response, CancellationToken.None);
    private static HttpResponseMessage Response(HttpStatusCode code, out ObservedContent content)
    { content = new(); return new(code) { Content = content }; }
    private static async Task RequireClosed(Task task)
    {
        try { await task; Assert.Fail("An abandoned control cannot complete successfully."); }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException) { }
    }

    private sealed class ObservedContent : HttpContent
    {
        internal bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return true; }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
}
