using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Management;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomsManagement;

[TestClass]
public sealed class RoomManagementQueryTests
{
    [TestMethod]
    public void ActualDetailAndCursorQueriesTranslateWithNpgsqlWithoutAnyConnection()
    {
        using var db = new DangoDbContext(DangoDatabaseOptions.Create());
        var service = new RoomManagementService(db, new AccountEnvironment(Guid.NewGuid()), TimeProvider.System, new RoomManagementCheckpoint());
        var detail = service.DetailQuery(Guid.NewGuid(), Guid.NewGuid()).ToQueryString();
        Assert.Contains("owner_id", detail);
        Assert.Contains("room_memberships", detail);
        Assert.Contains("pet_room_states", detail);
        foreach (var state in new[] { "active", "dissolved", "all" })
        {
            var page = service.ListQuery(Guid.NewGuid(), state, 50, Guid.NewGuid()).ToQueryString();
            Assert.Contains("LIMIT", page);
            Assert.Contains("ORDER BY", page);
            Assert.Contains("owner_id", page);
            Assert.Contains("pet_room_states", page);
        }
        Assert.AreEqual(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
