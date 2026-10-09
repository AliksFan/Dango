using System.Data.Common;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Management;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomExpiry;

[TestClass]
public sealed class RoomExpiryPolicyTests
{
    [TestMethod]
    public void Exact168HourDeadlineIsIncludedAndOneTickBeforeIsProtected()
    {
        var dissolved = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var room = new PersistentRoom
        {
            Kind = PersistentRoomKind.PrivateShared,
            Status = PersistentRoomStatus.Dissolved,
            DissolvedAt = dissolved,
            RecoverUntil = dissolved.AddHours(168)
        };
        Assert.IsFalse(RoomExpiryPolicy.IsDue(room, dissolved.AddHours(168).AddTicks(-1)));
        Assert.IsTrue(RoomExpiryPolicy.IsDue(room, dissolved.AddHours(168)));
        room.Status = PersistentRoomStatus.Expired;
        Assert.IsTrue(RoomExpiryPolicy.IsDue(room, dissolved.AddHours(168)));
    }

    [TestMethod]
    [DataRow(PersistentRoomKind.DesktopPet, PersistentRoomStatus.Dissolved)]
    [DataRow(PersistentRoomKind.DesktopPet, PersistentRoomStatus.Expired)]
    [DataRow(PersistentRoomKind.PrivateShared, PersistentRoomStatus.Active)]
    [DataRow(PersistentRoomKind.PublicShared, PersistentRoomStatus.Active)]
    [DataRow((PersistentRoomKind)99, PersistentRoomStatus.Dissolved)]
    [DataRow(PersistentRoomKind.PrivateShared, (PersistentRoomStatus)99)]
    public void ProtectedOrUnknownLifecycleNeverBecomesDue(PersistentRoomKind kind, PersistentRoomStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.IsFalse(RoomExpiryPolicy.IsDue(new() { Kind = kind, Status = status, RecoverUntil = now.AddDays(-1) }, now));
    }

    [TestMethod]
    public void MissingDeadlineNeverBecomesDue() => Assert.IsFalse(RoomExpiryPolicy.IsDue(
        new() { Kind = PersistentRoomKind.PrivateShared, Status = PersistentRoomStatus.Dissolved }, DateTimeOffset.UtcNow));

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(51)]
    [DataRow(int.MaxValue)]
    public void BatchBoundsAreRejectedBeforeDatabaseAccess(int limit) =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => RoomExpiryPolicy.ValidateBatch(limit, null, null));

    [TestMethod]
    public void ScopeRequiresDistinctNonemptyBoundedIdsAndIsCopied()
    {
        var id = Guid.NewGuid();
        Assert.ThrowsExactly<ArgumentException>(() => RoomExpiryPolicy.ValidateBatch(50, null, []));
        Assert.ThrowsExactly<ArgumentException>(() => RoomExpiryPolicy.ValidateBatch(50, null, [Guid.Empty]));
        Assert.ThrowsExactly<ArgumentException>(() => RoomExpiryPolicy.ValidateBatch(50, null, [id, id]));
        Assert.ThrowsExactly<ArgumentException>(() => RoomExpiryPolicy.ValidateBatch(50, null,
            Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray()));
        var scope = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        var validated = RoomExpiryPolicy.ValidateBatch(50, null, scope)!;
        CollectionAssert.AreEquivalent(scope, validated);
        scope[0] = Guid.Empty;
        Assert.IsFalse(validated.Contains(Guid.Empty));
        Assert.IsNull(RoomExpiryPolicy.ValidateBatch(1, null, null));
    }

    [TestMethod]
    public void EmptyCursorIdIsRejected() => Assert.ThrowsExactly<ArgumentException>(() =>
        RoomExpiryPolicy.ValidateBatch(1, new(DateTimeOffset.UtcNow, Guid.Empty), null));

    [TestMethod]
    public async Task CanceledBatchPropagatesBeforeOpeningAConnection()
    {
        await using var db = new DangoDbContext(DangoDatabaseOptions.Create("Host=127.0.0.1;Database=unused;Username=unused"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new RoomExpiryService(db, TimeProvider.System,
            new RoomManagementCheckpoint()).PurgeBatchAsync(1, null, [Guid.NewGuid()], cancellation.Token));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProductionCompositeCursorQueryTranslatesWhileConnectionOpeningIsBlocked(bool scoped)
    {
        var options = new DbContextOptionsBuilder<DangoDbContext>(DangoDatabaseOptions.Create(
            "Host=127.0.0.1;Database=unused;Username=unused")).AddInterceptors(new BlockConnection()).Options;
        await using var db = new DangoDbContext(options);
        // Query translation happens before connection opening. The interceptor throws before any network operation.
        await Assert.ThrowsExactlyAsync<QueryTranslatedWithoutConnection>(() => new RoomExpiryService(db, TimeProvider.System,
            new RoomManagementCheckpoint()).PurgeBatchAsync(50, new(DateTimeOffset.UtcNow, Guid.NewGuid()),
                scoped ? [Guid.NewGuid()] : null, CancellationToken.None));
    }

    private sealed class QueryTranslatedWithoutConnection : Exception;
    private sealed class BlockConnection : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result) => throw new QueryTranslatedWithoutConnection();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new QueryTranslatedWithoutConnection();
    }
}
