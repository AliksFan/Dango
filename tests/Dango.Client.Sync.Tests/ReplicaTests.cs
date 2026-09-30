using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class ReplicaTests
{
    internal static PetState Pet(double x = .3) => new("pet-a", "participant-a", new(x, .12), "right", null, null,
        PresetRef.Default, PresetRef.Default);
    internal static RoomSnapshot Snapshot(long sequence = 10, double x = .3) => new(RoomProtocol.Version, 1,
        RoomProtocol.RoomId, "epoch", sequence, 1000, "pet-a", CoordinateSpace.Default,
        [Pet(x), Pet(.7) with { PetId = "pet-b", ParticipantId = "participant-b" }]);
    internal static RoomEvent Event(long sequence, double x = .4) => new(RoomProtocol.Version, $"event-{sequence}",
        RoomProtocol.RoomId, "epoch", sequence, 1000, null, RoomProtocol.MotionChanged, Pet(x));
    internal static (RoomReplica Replica, long Connection) Live()
    {
        var replica = new RoomReplica();
        var connection = replica.BeginConnection(RoomProtocol.RoomId, "epoch");
        replica.ApplySnapshot(replica.BeginSynchronization(connection), Snapshot());
        return (replica, connection);
    }

    [TestMethod]
    public void GapNeverAppliesArrivalOrderTwelveBeforeEleven()
    {
        var (replica, connection) = Live();
        replica.Receive(connection, Event(12, .6));
        Assert.AreEqual(10L, replica.Frame.LastSequence, "Arrival-order mutation incorrectly exposes sequence 12.");
        Assert.AreEqual(ReplicaState.Resyncing, replica.Frame.State);
        replica.Receive(connection, Event(11, .5));
        Assert.AreEqual(10L, replica.Frame.LastSequence);
        replica.ApplySnapshot(replica.BeginSynchronization(connection), Snapshot());
        Assert.AreEqual(12L, replica.Frame.LastSequence);
        Assert.AreEqual(.6, replica.Frame.Snapshot!.Pets[0].AnchorPosition.X);
    }

    [TestMethod]
    public void StaleAttemptInSameConnectionCannotRollSnapshotBack()
    {
        var (replica, connection) = Live();
        var old = replica.BeginSynchronization(connection);
        var current = replica.BeginSynchronization(connection);
        replica.ApplySnapshot(current, Snapshot(13, .6));
        Assert.AreEqual(ReplicaUpdate.Ignored, replica.ApplySnapshot(old, Snapshot(12, .5)));
        Assert.AreEqual(13L, replica.Frame.LastSequence, "Ignoring sync attempt fences rolls back the same connection.");
    }

    [TestMethod]
    public void PassiveTailLossIsDetectedByWatermarkWithoutFollowingEvent()
    {
        var (replica, connection) = Live();
        Assert.AreEqual(ReplicaUpdate.ResyncRequired, replica.ObserveWatermark(connection,
            new(RoomProtocol.Version, RoomProtocol.RoomId, "epoch", 3000, 11)),
            "Without watermark checking the quiet passive replica permanently remains at 10.");
        Assert.AreEqual(ReplicaState.Resyncing, replica.Frame.State);
        replica.ApplySnapshot(replica.BeginSynchronization(connection), Snapshot(11, .5));
        Assert.AreEqual(11L, replica.Frame.LastSequence);
    }

    [TestMethod]
    public void JoinBuffersEventBeforeSnapshotAndRetainsOnlyContiguousSuffix()
    {
        var replica = new RoomReplica();
        var connection = replica.BeginConnection(RoomProtocol.RoomId, "epoch");
        var attempt = replica.BeginSynchronization(connection);
        Assert.AreEqual(ReplicaUpdate.Buffered, replica.Receive(connection, Event(11)));
        Assert.IsNull(replica.Frame.Snapshot);
        replica.ApplySnapshot(attempt, Snapshot(10));
        Assert.AreEqual(11L, replica.Frame.LastSequence);
        Assert.AreEqual(ReplicaUpdate.Ignored, replica.Receive(connection, Event(11)));
        Assert.AreEqual(11L, replica.Frame.LastSequence);
    }

    [TestMethod]
    public void BufferOverflowRequiresFreshSnapshotWithoutUnboundedGrowth()
    {
        var (replica, connection) = Live();
        for (var sequence = 12; sequence < 76; sequence++) replica.Receive(connection, Event(sequence));
        Assert.AreEqual(ReplicaUpdate.ResyncRequired, replica.Receive(connection, Event(76)));
        Assert.AreEqual("buffer_overflow", replica.Frame.Reason);
        Assert.AreEqual(10L, replica.Frame.LastSequence);
        replica.ApplySnapshot(replica.BeginSynchronization(connection), Snapshot(76));
        Assert.AreEqual(ReplicaState.Live, replica.Frame.State);
        Assert.AreEqual(76L, replica.Frame.LastSequence);
    }

    [TestMethod]
    public void OldConnectionEventSnapshotDisconnectAndWatermarkAreFenced()
    {
        var (replica, first) = Live();
        var oldAttempt = replica.BeginSynchronization(first);
        var second = replica.BeginConnection(RoomProtocol.RoomId, "epoch");
        replica.ApplySnapshot(replica.BeginSynchronization(second), Snapshot(12, .5));
        Assert.AreEqual(ReplicaUpdate.Ignored, replica.Receive(first, Event(13, .8)));
        Assert.AreEqual(ReplicaUpdate.Ignored, replica.ApplySnapshot(oldAttempt, Snapshot(99)));
        Assert.AreEqual(ReplicaUpdate.Ignored, replica.ObserveWatermark(first,
            new(RoomProtocol.Version, RoomProtocol.RoomId, "new-epoch", 1, 0)));
        replica.Disconnect(first);
        Assert.AreEqual(ReplicaState.Live, replica.Frame.State);
        Assert.AreEqual(12L, replica.Frame.LastSequence);
    }

    [TestMethod]
    public void CurrentAttemptCannotApplyLowerThanAlreadyVisibleSequence()
    {
        var (replica, connection) = Live();
        var attempt = replica.BeginSynchronization(connection);
        Assert.AreEqual(ReplicaUpdate.ResyncRequired, replica.ApplySnapshot(attempt, Snapshot(9)));
        Assert.AreEqual(10L, replica.Frame.LastSequence);
    }

    [TestMethod]
    public void ContradictoryDuplicateAndUnknownEventFailClosed()
    {
        var (replica, connection) = Live();
        replica.Receive(connection, Event(11));
        Assert.AreEqual(ReplicaUpdate.Invalid, replica.Receive(connection, Event(11, .8)));
        Assert.AreEqual(ReplicaState.Faulted, replica.Frame.State);
        var (other, second) = Live();
        Assert.AreEqual(ReplicaUpdate.Invalid, other.Receive(second, Event(11) with { Type = "pet.unknown" }));
    }

    [TestMethod]
    public void ReturnedFrameAndActionParticipantsAreImmutableCopies()
    {
        var (replica, connection) = Live();
        string[] participants = ["pet-a"];
        var pet = Pet() with { Action = new("wave-a", "wave", participants, 1000, 2200, 1, 1, "wave-nod") };
        PetState[] pets = [pet, Pet(.7) with { PetId = "pet-b", ParticipantId = "participant-b" }];
        replica.ApplySnapshot(replica.BeginSynchronization(connection), Snapshot() with { Pets = pets });
        participants[0] = "changed";
        pets[0] = Pet(.9);
        Assert.AreEqual(.3, replica.Frame.Snapshot!.Pets[0].AnchorPosition.X);
        Assert.AreEqual("pet-a", replica.Frame.Snapshot.Pets[0].Action!.Participants[0]);
    }

    [TestMethod]
    public void RoomResetCannotBeDowngradedByTransportClosedOrManualReconnect()
    {
        var (replica, connection) = Live();
        replica.ObserveWatermark(connection, new(RoomProtocol.Version, RoomProtocol.RoomId, "new-epoch", 0, 0));
        Assert.AreEqual(ReplicaState.ResetRequired, replica.Frame.State);
        replica.Disconnect(connection);
        Assert.AreEqual(ReplicaState.ResetRequired, replica.Frame.State);
        Assert.IsNull(replica.Frame.Snapshot);
    }

    [TestMethod]
    public void UnsupportedProtocolSnapshotIsIncompatibleNotReconnectable()
    {
        var (replica, connection) = Live();
        replica.ApplySnapshot(replica.BeginSynchronization(connection), Snapshot() with { SnapshotSchemaVersion = 99 });
        Assert.AreEqual(ReplicaState.Incompatible, replica.Frame.State);
        replica.Disconnect(connection);
        Assert.AreEqual(ReplicaState.Incompatible, replica.Frame.State);
    }
}
