using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

internal sealed class ManualTime : TimeProvider
{
    public double Milliseconds { get; set; }
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => (long)Milliseconds;
    // A hostile wall clock demonstrates that UTC never affects the estimator.
    public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Wall clock must not be read.");
}

[TestClass]
public sealed class ClockAndDispatcherTests
{
    [TestMethod]
    public void ClockUsesMidpointAndRejectsExcessiveRtt()
    {
        var time = new ManualTime { Milliseconds = 1000 };
        var clock = new ServerClockEstimator(time);
        time.Milliseconds = 1100;
        clock.Rebase(5050);
        Assert.IsTrue(clock.AddSample(0, 100, 5000));
        Assert.AreEqual(5050L, clock.EstimatedServerTimeMs);
        Assert.IsFalse(clock.AddSample(100, 700, 99999));
        time.Milliseconds = 1200;
        Assert.AreEqual(5150L, clock.EstimatedServerTimeMs);
        Assert.AreEqual(100d, clock.BestRttMs);
    }

    [TestMethod]
    public void LongSuspendFreezesUntilResyncAndTimeNeverRunsBackwards()
    {
        var time = new ManualTime();
        var clock = new ServerClockEstimator(time);
        clock.Rebase(1000);
        Assert.AreEqual(1000L, clock.EstimatedServerTimeMs);
        time.Milliseconds = 100;
        Assert.AreEqual(1100L, clock.EstimatedServerTimeMs);
        clock.AddSample(90, 100, 1075);
        Assert.IsGreaterThanOrEqualTo(1100L, clock.EstimatedServerTimeMs);
        time.Milliseconds = 20100;
        var frozen = clock.EstimatedServerTimeMs;
        Assert.IsTrue(clock.NeedsResync);
        time.Milliseconds = 20200;
        Assert.AreEqual(frozen, clock.EstimatedServerTimeMs);
        clock.Rebase(21200);
        Assert.IsFalse(clock.NeedsResync);
        Assert.AreEqual(21200L, clock.EstimatedServerTimeMs);
    }

    [TestMethod]
    public void UnsentAndUnconsumedOldIntentNeverReplaysAfterResume()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.BeginSession(1, RoomProtocol.RoomId, "epoch", 1, 1);
        dispatcher.EnqueueMove("left");
        dispatcher.Disconnect(1);
        dispatcher.BeginSession(2, RoomProtocol.RoomId, "epoch", 2, 1);
        Assert.IsNull(dispatcher.Prepare(2, 0, 0));
        dispatcher.EnqueueMove("right");
        var pending = dispatcher.Prepare(2, 0, 0);
        Assert.IsNotNull(pending);
        dispatcher.Disconnect(2);
        Assert.AreEqual(ResumeDisposition.DiscardedUnconsumedPending,
            dispatcher.BeginSession(3, RoomProtocol.RoomId, "epoch", 3, 1));
        Assert.IsNull(dispatcher.Prepare(3, 0, 10));
        dispatcher.EnqueueMove("stopped");
        var fresh = dispatcher.Prepare(3, 0, 10)!;
        Assert.AreEqual(1L, fresh.CommandOrdinal);
        Assert.AreNotEqual(pending.CommandId, fresh.CommandId);
    }

    [TestMethod]
    public void ConsumedPendingRetriesSameIdAndKeepsResumeOrdinalBarrier()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.BeginSession(1, RoomProtocol.RoomId, "epoch", 1, 1);
        dispatcher.EnqueueWave();
        var first = dispatcher.Prepare(1, 0, 0);
        Assert.IsNotNull(first);
        dispatcher.Disconnect(1);
        Assert.AreEqual(ResumeDisposition.QueryConsumedPending,
            dispatcher.BeginSession(2, RoomProtocol.RoomId, "epoch", 2, 3));
        var retry = dispatcher.Prepare(2, 2, 100)!;
        Assert.AreEqual(first.CommandId, retry.CommandId);
        Assert.AreEqual(1L, retry.CommandOrdinal);
        Assert.AreEqual(2L, retry.ConnectionGeneration);
        var result = new CommandResult(RoomProtocol.Version, RoomProtocol.RoomId, "epoch", first.CommandId,
            "duplicate", false, 0, true, 2, "rejected", null, null, 100, new("action_conflict"));
        Assert.AreEqual(CommandDisposition.Ignored, dispatcher.ApplyResult(1, result));
        Assert.AreEqual(CommandDisposition.Completed, dispatcher.ApplyResult(2, result));
        Assert.AreEqual(3L, dispatcher.NextCommandOrdinal);
        Assert.AreEqual("rejected", dispatcher.LastOutcome);
        Assert.AreEqual("action_conflict", dispatcher.LastError);
    }

    [TestMethod]
    public void ContinuousSameDirectionIsBoundedButStopIsImmediateAndUnnumberedInputsCoalesce()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.BeginSession(1, RoomProtocol.RoomId, "epoch", 1, 1);
        dispatcher.EnqueueMove("left");
        dispatcher.EnqueueMove("right");
        var first = dispatcher.Prepare(1, 0, 0)!;
        Assert.AreEqual("right", first.Payload.Direction);
        dispatcher.ApplyResult(1, Result(first, true));
        dispatcher.EnqueueMove("right");
        Assert.IsNull(dispatcher.Prepare(1, 1, 10));
        dispatcher.EnqueueMove("stopped");
        var stopped = dispatcher.Prepare(1, 1, 20)!;
        Assert.AreEqual("stopped", stopped.Payload.Direction);
        Assert.AreEqual(2L, stopped.CommandOrdinal);
    }

    [TestMethod]
    public void AdmissionFailureRetriesSameCommandAndRetryWindowDoesNotRestartOnResume()
    {
        var dispatcher = new CommandDispatcher();
        dispatcher.BeginSession(1, RoomProtocol.RoomId, "epoch", 1, 1);
        dispatcher.EnqueueWave();
        var command = dispatcher.Prepare(1, 0, 0)!;
        Assert.AreEqual(CommandDisposition.Retry, dispatcher.ApplyResult(1, Result(command, false)));
        var retry = dispatcher.Prepare(1, 0, 50)!;
        Assert.AreEqual(command.CommandId, retry.CommandId);
        Assert.AreEqual(1L, dispatcher.NextCommandOrdinal);
        Assert.IsNull(dispatcher.Prepare(1, 0, 30001));
        Assert.IsTrue(dispatcher.NeedsResume);
        dispatcher.BeginSession(2, RoomProtocol.RoomId, "epoch", 2, 2);
        Assert.IsNull(dispatcher.Prepare(2, 1, 30002));
        Assert.IsTrue(dispatcher.NeedsResume);
    }

    [TestMethod]
    public void InvalidOfflineInputsAndStaleDisconnectCannotTouchNewSession()
    {
        var dispatcher = new CommandDispatcher();
        Assert.IsFalse(dispatcher.EnqueueWave());
        Assert.Throws<ArgumentException>(() => dispatcher.EnqueueMove("up"));
        dispatcher.BeginSession(1, RoomProtocol.RoomId, "epoch", 1, 1);
        dispatcher.Disconnect(1);
        dispatcher.BeginSession(2, RoomProtocol.RoomId, "epoch", 2, 1);
        dispatcher.Disconnect(1);
        Assert.IsTrue(dispatcher.EnqueueMove("left"));
        Assert.IsNotNull(dispatcher.Prepare(2, 0, 1));
    }

    [TestMethod]
    public void LowestRecentRttWinsAndLargeOffsetJumpRequiresBarrier()
    {
        var time = new ManualTime();
        var clock = new ServerClockEstimator(time);
        clock.Rebase(1000);
        time.Milliseconds = 20;
        Assert.IsTrue(clock.AddSample(0, 20, 1010));
        time.Milliseconds = 400;
        Assert.IsTrue(clock.AddSample(100, 400, 1300));
        Assert.AreEqual(20d, clock.BestRttMs);
        Assert.AreEqual(1400L, clock.EstimatedServerTimeMs);
        Assert.IsFalse(clock.AddSample(400, 410, 9000));
        Assert.IsTrue(clock.NeedsResync);
    }

    private static CommandResult Result(RoomCommand command, bool consumed) => new(RoomProtocol.Version,
        RoomProtocol.RoomId, "epoch", command.CommandId, consumed ? "accepted" : "rejected", consumed,
        consumed ? 1 : 0, consumed, consumed ? command.CommandOrdinal + 1 : command.CommandOrdinal,
        null, consumed ? "event-1" : null, consumed ? 1 : null, 100, consumed ? null : new("rate_limited", 50));
}
