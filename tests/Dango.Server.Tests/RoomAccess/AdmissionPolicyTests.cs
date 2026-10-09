using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Dango.Server.Tests.Accounts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
public sealed class AdmissionPolicyTests
{
    [TestMethod]
    public void EveryProtocolFieldHasAnExplicitBoundary()
    {
        var valid = new RoomEnterRequest(Guid.NewGuid(), Guid.NewGuid(), "desktop", null, 1);
        RoomAdmissionPolicy.Enter(valid);
        foreach (var invalid in new[] { valid with { RequestId = Guid.Empty }, valid with { InstanceId = Guid.Empty }, valid with { DeviceKind = "Desktop" },
            valid with { ExpectedRoomId = Guid.Empty }, valid with { ExpectedAssignmentVersion = 0 }, valid with { Code = "untrusted" } })
            Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.Enter(invalid));
        var control = new RoomControlRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0);
        RoomAdmissionPolicy.Control(control);
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.Control(control with { ExpectedControlGeneration = -1 }));
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.Lease(new(Guid.Empty, Guid.NewGuid())));
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.End(new(Guid.Empty, Guid.NewGuid(), Guid.NewGuid())));
    }
    [TestMethod]
    public void PublicCodeDigestMatchesCanonical32ByteAccountProofAndNeverAcceptsWhitespace()
    {
        var code = B1InputTests.NewProof();
        Assert.IsTrue(string.Equals(AccountInput.Digest(code), RoomAdmissionPolicy.CodeDigest(code), StringComparison.Ordinal));
        foreach (var invalid in new string?[] { null, code + " ", " " + code, code[..42], code + "=", new('!', 43) })
            Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.CodeDigest(invalid));
    }
    [TestMethod]
    public void MobileCannotEnterDesktopAndFirstStagePrivateAdmissionIsOwnerOnly()
    {
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var room = new PersistentRoom { OwnerId = owner, Kind = PersistentRoomKind.DesktopPet, Status = PersistentRoomStatus.Active };
        RoomAdmissionPolicy.RequireRoom(room, owner, "desktop");
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireRoom(room, owner, "mobile"));
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireRoom(room, other, "desktop"));
        room.Kind = PersistentRoomKind.PrivateShared;
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireRoom(room, other, "mobile"));
        room.Kind = PersistentRoomKind.PublicShared;
        RoomAdmissionPolicy.RequireRoom(room, other, "mobile");
        room.Status = PersistentRoomStatus.Dissolved;
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireRoom(room, owner, "desktop"));
    }
    [TestMethod]
    public void StaleAssignmentAndControllerNeverPassTheDomainGate()
    {
        var state = new PetRoomState { RoomId = Guid.NewGuid(), Version = 4, ControlGeneration = 7, ControlLeaseId = Guid.NewGuid() };
        var request = new RoomControlRequest(Guid.NewGuid(), state.ControlLeaseId.Value, Guid.NewGuid(), state.RoomId.Value, 4, 7);
        RoomAdmissionPolicy.RequireControl(state, request, true);
        foreach (var bad in new[] { request with { ExpectedRoomId = Guid.NewGuid() }, request with { ExpectedAssignmentVersion = 3 },
            request with { ExpectedControlGeneration = 6 }, request with { LeaseId = Guid.NewGuid() } })
            Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireControl(state, bad, true));
        RoomAdmissionPolicy.RequireControl(state, request with { LeaseId = Guid.NewGuid() }, false);
    }
    [TestMethod]
    public void LeasePetUserSessionInstanceAndTrustedTransportMustAllMatch()
    {
        var pet = new AccountPet { Id = Guid.NewGuid(), UserId = Guid.NewGuid() };
        var lease = new RoomDeviceLease { PetId = pet.Id, UserId = pet.UserId, SessionId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), DeviceKind = "desktop", TransportId = "new-connection" };
        RoomAdmissionPolicy.RequireLeaseActor(lease, pet, lease.SessionId, lease.InstanceId, "new-connection");
        RoomAdmissionPolicy.RequireLeaseActor(lease, pet, lease.SessionId, lease.InstanceId, null);
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireLeaseActor(lease, pet, Guid.NewGuid(), lease.InstanceId, null));
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireLeaseActor(lease, pet, lease.SessionId, Guid.NewGuid(), null));
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireLeaseActor(lease, pet, lease.SessionId, lease.InstanceId, "old-connection"));
        lease.UserId = Guid.NewGuid();
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireLeaseIdentity(lease, pet));
        lease.UserId = pet.UserId; lease.PetId = Guid.NewGuid();
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.RequireLeaseIdentity(lease, pet));
    }
    [TestMethod]
    public void TransportAndMaintenanceQueriesAreBounded()
    {
        RoomAdmissionPolicy.Transport(new('a', 128)); RoomAdmissionPolicy.Transport(null);
        foreach (var invalid in new[] { "", new string('a', 129), "connection\n" }) Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.Transport(invalid));
        RoomAdmissionPolicy.Batch(50, Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray());
        foreach (var count in new[] { 0, 51 }) Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.Batch(count, null));
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.Batch(1, Array.Empty<Guid>()));
        Assert.ThrowsExactly<AccountFault>(() => RoomAdmissionPolicy.Batch(1, [Guid.Empty]));
        Assert.AreEqual(TimeSpan.FromSeconds(30), RoomAccessPersistence.LeaseDuration);
    }
}
