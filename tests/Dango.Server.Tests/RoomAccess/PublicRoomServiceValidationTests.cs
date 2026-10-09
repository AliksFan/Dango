using System.Data.Common;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
public sealed class PublicRoomServiceValidationTests
{
    [TestMethod]
    public async Task InvalidRequestIdsAndCodesAreRejectedWithoutOpeningAnyConnection()
    {
        var options = new DbContextOptionsBuilder<DangoDbContext>(DangoDatabaseOptions.Create(
            "Host=127.0.0.1;Database=unused;Username=unused")).AddInterceptors(new BlockConnection()).Options;
        await using var db = new DangoDbContext(options);
        var environment = new AccountEnvironment(Guid.NewGuid());
        var service = new PublicRoomAccessService(db, environment, TimeProvider.System, new RoomAccessCheckpoint(),
            new PublicRoomCodeProtector(new EphemeralDataProtectionProvider(), environment));
        var actor = Guid.NewGuid(); var session = Guid.NewGuid(); var room = Guid.NewGuid(); var target = Guid.NewGuid();
        Assert.AreEqual("room_request_invalid", Assert.ThrowsExactly<AccountFault>(() =>
            service.ResetCodeAsync(actor, session, room, new(Guid.Empty, 1), CancellationToken.None)).Code);
        foreach (var action in new Func<Task>[]
        {
            () => service.RemoveAsync(actor, session, room, target, new(Guid.Empty, 1), CancellationToken.None),
            () => service.BanAsync(actor, session, room, target, new(Guid.Empty, 1), CancellationToken.None),
            () => service.UnbanAsync(actor, session, room, target, new(Guid.Empty, 1), CancellationToken.None)
        })
            Assert.AreEqual("room_request_invalid", (await Assert.ThrowsExactlyAsync<AccountFault>(action)).Code);
        Assert.AreEqual("room_code_invalid", (await Assert.ThrowsExactlyAsync<AccountFault>(() =>
            service.ResolveAsync(actor, session, new(new string('!', 43)), CancellationToken.None))).Code);
    }

    private sealed class BlockConnection : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result) => throw new InvalidOperationException("test_database_connection_blocked");
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new InvalidOperationException("test_database_connection_blocked");
    }
}
