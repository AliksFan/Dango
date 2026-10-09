using Dango.Server.Configuration;
using Dango.Server.Rooms.Access;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
public sealed class RoomTransportPolicyTests
{
    [TestMethod]
    public void RegistryEnforcesAccountAndGlobalBoundsAndReleasesDisconnectedSlot()
    {
        var registry = new RoomConnectionRegistry(TimeProvider.System);
        var user = Guid.NewGuid(); var session = Guid.NewGuid();
        for (var i = 0; i < 8; i++) Assert.IsTrue(registry.Add($"c{i}", user, session));
        Assert.IsFalse(registry.Add("overflow", user, session));
        registry.Remove("c0");
        Assert.IsTrue(registry.Add("replacement", user, session));
        for (var i = 8; i < 128; i++) Assert.IsTrue(registry.Add($"c{i}", Guid.NewGuid(), session));
        Assert.IsFalse(registry.Add("global-overflow", Guid.NewGuid(), session));
        Assert.IsFalse(registry.Add("invalid", Guid.Empty, session));
    }

    [TestMethod]
    public void BindingIsOnlyCandidateFilterAndStaleTransportIsDetected()
    {
        var registry = new RoomConnectionRegistry(TimeProvider.System);
        var user = Guid.NewGuid(); var room = Guid.NewGuid();
        Assert.IsTrue(registry.Add("a", user, Guid.NewGuid()));
        Assert.HasCount(0, registry.Candidates(room, []));
        Assert.HasCount(1, registry.Candidates(room, [user]));
        registry.Bind("a", Guid.NewGuid(), Guid.NewGuid(), room);
        var prior = registry.Candidates(room, []).Single();
        registry.Bind("a", null, null, null);
        Assert.IsFalse(registry.IsCurrent(prior));
        Assert.HasCount(0, registry.Candidates(room, []));
    }

    [TestMethod]
    public void ReplayedOldLeaveCannotUnbindAReplacementLease()
    {
        var registry = new RoomConnectionRegistry(TimeProvider.System);
        Assert.IsTrue(registry.Add("a", Guid.NewGuid(), Guid.NewGuid()));
        var room = Guid.NewGuid(); var instance = Guid.NewGuid(); var lease = Guid.NewGuid();
        registry.Bind("a", lease, instance, room);
        registry.UnbindLease("a", Guid.NewGuid(), instance);
        Assert.HasCount(1, registry.Candidates(room, []));
        registry.UnbindLease("a", lease, instance);
        Assert.HasCount(0, registry.Candidates(room, []));
    }

    [TestMethod]
    public void HubCallsHaveFiniteWindowAndMissingConnectionIsRejected()
    {
        var clock = new TestClock(); var registry = new RoomConnectionRegistry(clock);
        Assert.IsFalse(registry.AllowCall("missing"));
        Assert.IsTrue(registry.Add("a", Guid.NewGuid(), Guid.NewGuid()));
        for (var i = 0; i < 30; i++) Assert.IsTrue(registry.AllowCall("a"));
        Assert.IsFalse(registry.AllowCall("a"));
        clock.Now = clock.Now.AddSeconds(10);
        Assert.IsTrue(registry.AllowCall("a"));
    }

    [TestMethod]
    public void OutboxScopeRejectsUnboundedOrAmbiguousTargetsBeforeDatabaseAccess()
    {
        Assert.IsNull(RoomOutboxDispatcher.ValidateScope(20, null));
        var id = Guid.NewGuid();
        CollectionAssert.AreEqual(new[] { id }, RoomOutboxDispatcher.ValidateScope(1, [id]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RoomOutboxDispatcher.ValidateScope(21, null));
        Assert.ThrowsExactly<ArgumentException>(() => RoomOutboxDispatcher.ValidateScope(1, []));
        Assert.ThrowsExactly<ArgumentException>(() => RoomOutboxDispatcher.ValidateScope(1, [id, id]));
        Assert.ThrowsExactly<ArgumentException>(() => RoomOutboxDispatcher.ValidateScope(1, [Guid.Empty]));
    }

    [TestMethod]
    public void InvalidSessionPruningAbortsOnlyTheCurrentBindingAndReleasesSlots()
    {
        var registry = new RoomConnectionRegistry(TimeProvider.System);
        var aborted = 0; var user = Guid.NewGuid();
        Assert.IsTrue(registry.Add("a", user, Guid.NewGuid(), () => aborted++));
        var stale = registry.All().Single();
        registry.Bind("a", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        registry.RemoveIfCurrent(stale);
        Assert.AreEqual(0, aborted);
        registry.RemoveIfCurrent(registry.All().Single());
        Assert.AreEqual(1, aborted); Assert.HasCount(0, registry.All());
        Assert.IsTrue(registry.Add("a", user, Guid.NewGuid()));
    }

    [TestMethod]
    public void LiveWorkersRequireExactOptIn()
    {
        Assert.IsFalse(RoomMaintenanceOptions.LiveEnabled(null));
        Assert.IsFalse(RoomMaintenanceOptions.LiveEnabled("false"));
        Assert.IsTrue(RoomMaintenanceOptions.LiveEnabled("true"));
        Assert.ThrowsExactly<Dango.Server.Accounts.AccountFault>(() => RoomMaintenanceOptions.LiveEnabled("TRUE"));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
