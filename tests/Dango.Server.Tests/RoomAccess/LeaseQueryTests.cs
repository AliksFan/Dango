using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
public sealed class LeaseQueryTests
{
    [TestMethod]
    public void ActualScopedMaintenanceCursorQueryTranslatesWithNpgsqlAndLeavesTheConnectionClosed()
    {
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var service = new RoomLeaseMaintenanceService(db, new AccountEnvironment(Guid.NewGuid()), TimeProvider.System, new RoomAccessCheckpoint());
        var sql = service.CandidatesQuery(50, [Guid.NewGuid(), Guid.NewGuid()], Guid.NewGuid()).ToQueryString();
        Assert.Contains("LIMIT", sql); Assert.Contains("ORDER BY", sql); Assert.Contains("room_device_leases", sql);
        Assert.Contains("auth_sessions", sql); Assert.Contains("users", sql);
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
