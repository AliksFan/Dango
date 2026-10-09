using Dango.Server.Accounts;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomsManagement;

[TestClass]
public sealed class RoomManagementPolicyTests
{
    [TestMethod]
    public void NamesNormalizeTrimAndCountUnicodeScalarsRatherThanUtf16Units()
    {
        Assert.AreEqual("é团子", RoomManagementPolicy.Name("  e\u0301团子  "));
        var sixtyFour = string.Concat(Enumerable.Repeat("🐾", 64));
        Assert.AreEqual(sixtyFour, RoomManagementPolicy.Name(sixtyFour));
        Fault("room_name_invalid", () => RoomManagementPolicy.Name(sixtyFour + "🐾"));
        Assert.AreEqual("é", RoomManagementPolicy.Name("e\u0301"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  \u3000 ")]
    [DataRow("room\0name")]
    [DataRow("room\nname")]
    [DataRow("\tname")]
    [DataRow("name\u007f")]
    [DataRow("name\u0085")]
    public void MissingBlankAndControlNamesAreRejected(string? name) => Fault("room_name_invalid", () => RoomManagementPolicy.Name(name));

    [TestMethod]
    public void InvalidSurrogatesAreRejectedInsteadOfBeingReplaced()
    {
        foreach (var name in new[] { "\ud800", "\udc00", "x\ud800y", "\udc00\ud800" })
            Fault("room_name_invalid", () => RoomManagementPolicy.Name(name));
    }

    [TestMethod]
    public void CreateKindIsExplicitAndDesktopCanOnlyRetainItsFixedCapacity()
    {
        Assert.AreEqual(PersistentRoomKind.PrivateShared, RoomManagementPolicy.CreateKind("privateShared"));
        Assert.AreEqual(PersistentRoomKind.PublicShared, RoomManagementPolicy.CreateKind("publicShared"));
        foreach (var kind in new string?[] { null, "", "desktopPet", "PRIVATEshared", "0", "privateShared " })
            Fault("room_kind_invalid", () => RoomManagementPolicy.CreateKind(kind));
        foreach (var kind in new[] { PersistentRoomKind.PrivateShared, PersistentRoomKind.PublicShared })
        {
            RoomManagementPolicy.Capacity(kind, 1);
            RoomManagementPolicy.Capacity(kind, 6);
            RoomManagementPolicy.Capacity(kind, 12);
            Fault("room_capacity_invalid", () => RoomManagementPolicy.Capacity(kind, 0));
            Fault("room_capacity_invalid", () => RoomManagementPolicy.Capacity(kind, 13));
        }
        RoomManagementPolicy.Capacity(PersistentRoomKind.DesktopPet, 2);
        foreach (var capacity in new[] { 1, 3, 6 }) Fault("room_capacity_invalid", () => RoomManagementPolicy.Capacity(PersistentRoomKind.DesktopPet, capacity));
        Fault("room_desktop_protected", () => RoomManagementPolicy.Shared(new() { Kind = PersistentRoomKind.DesktopPet }));
    }

    [TestMethod]
    public void CapacityUsesReservedSeatsForPrivateAndActualPetAssignmentsForPublic()
    {
        Fault("room_capacity_conflict", () => RoomManagementPolicy.Seats(PersistentRoomKind.PrivateShared, 2, 3, 0));
        RoomManagementPolicy.Seats(PersistentRoomKind.PrivateShared, 3, 3, 0);
        Fault("room_capacity_conflict", () => RoomManagementPolicy.Seats(PersistentRoomKind.PublicShared, 2, 1, 3));
        RoomManagementPolicy.Seats(PersistentRoomKind.PublicShared, 3, 99, 3);
        RoomManagementPolicy.Seats(PersistentRoomKind.PublicShared, 1, 99, 0);
    }

    [TestMethod]
    public void QuotaAndVersionsRejectBoundaryConflictsAndOverflow()
    {
        RoomManagementPolicy.Quota(2);
        Fault("room_quota_reached", () => RoomManagementPolicy.Quota(3));
        Fault("room_quota_reached", () => RoomManagementPolicy.Quota(4));
        Assert.AreEqual(8L, RoomManagementPolicy.NextVersion(7, 7));
        Fault("room_version_conflict", () => RoomManagementPolicy.NextVersion(7, 6));
        Fault("room_version_exhausted", () => RoomManagementPolicy.NextVersion(long.MaxValue, long.MaxValue));
        Fault("room_version_invalid", () => RoomManagementPolicy.ExpectedVersion(0));
        Fault("room_request_invalid", () => RoomManagementPolicy.Request(Guid.Empty));
    }

    [TestMethod]
    public void RecoveryUsesAnAbsolute168HourDeadlineWithAnExclusiveUpperBound()
    {
        var dissolved = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var until = dissolved.Add(RoomManagementPolicy.RecoveryWindow);
        Assert.AreEqual(TimeSpan.FromHours(168), until - dissolved);
        RoomManagementPolicy.RecoveryDeadline(until, until.AddTicks(-1));
        Fault("room_recovery_expired", () => RoomManagementPolicy.RecoveryDeadline(until, until));
        Fault("room_recovery_expired", () => RoomManagementPolicy.RecoveryDeadline(until, until.AddTicks(1)));
        Fault("room_recovery_expired", () => RoomManagementPolicy.RecoveryDeadline(null, dissolved));
    }

    [TestMethod]
    public void RequestHashIncludesEverySemanticArgumentAndCanonicalizesEquivalentNames()
    {
        var roomId = Guid.NewGuid();
        var hash = RoomManagementPolicy.RequestHash("updated", roomId, 7, PersistentRoomKind.PrivateShared, "é", 6, true);
        Assert.AreEqual(hash, RoomManagementPolicy.RequestHash("updated", roomId, 7, PersistentRoomKind.PrivateShared, " e\u0301 ", 6, true));
        Assert.AreEqual(64, hash.Length);
        var changes = new[]
        {
            RoomManagementPolicy.RequestHash("restored", roomId, 7, PersistentRoomKind.PrivateShared, "é", 6, true),
            RoomManagementPolicy.RequestHash("updated", Guid.NewGuid(), 7, PersistentRoomKind.PrivateShared, "é", 6, true),
            RoomManagementPolicy.RequestHash("updated", roomId, 8, PersistentRoomKind.PrivateShared, "é", 6, true),
            RoomManagementPolicy.RequestHash("updated", roomId, 7, PersistentRoomKind.PublicShared, "é", 6, true),
            RoomManagementPolicy.RequestHash("updated", roomId, 7, PersistentRoomKind.PrivateShared, "other", 6, true),
            RoomManagementPolicy.RequestHash("updated", roomId, 7, PersistentRoomKind.PrivateShared, "é", 5, true),
            RoomManagementPolicy.RequestHash("updated", roomId, 7, PersistentRoomKind.PrivateShared, "é", 6, false)
        };
        foreach (var changed in changes) Assert.AreNotEqual(hash, changed);
    }

    [TestMethod]
    public void ListingIsExplicitAndBounded()
    {
        Assert.AreEqual(PersistentRoomStatus.Active, RoomManagementPolicy.ListState("active"));
        Assert.AreEqual(PersistentRoomStatus.Dissolved, RoomManagementPolicy.ListState("dissolved"));
        Assert.IsNull(RoomManagementPolicy.ListState("all"));
        Fault("room_list_state_invalid", () => RoomManagementPolicy.ListState("public"));
        RoomManagementPolicy.PageSize(1);
        RoomManagementPolicy.PageSize(50);
        Fault("room_page_size_invalid", () => RoomManagementPolicy.PageSize(0));
        Fault("room_page_size_invalid", () => RoomManagementPolicy.PageSize(51));
    }

    private static void Fault(string code, Action action)
    {
        var fault = Assert.ThrowsExactly<AccountFault>(action);
        Assert.AreEqual(code, fault.Code);
    }
}
