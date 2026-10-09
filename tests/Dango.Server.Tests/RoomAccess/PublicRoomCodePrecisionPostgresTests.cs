using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class PublicRoomCodePrecisionPostgresTests
{
    [TestMethod]
    public async Task SevenDigitClockPersistsExactlyTheAcknowledgedDeadlineAndReplaysWithoutPlaintextReceipts()
    {
        RoomAccessHttpHubTests.RequireApproval();
        var clock = PublicRoomTestSupport.Clock();
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var visitor = await PetTestSupport.RegisterAsync(fixture);
        var room = await RoomAccessTestSupport.CreateRoomAsync(fixture, owner);
        // Isolate the room receipt boundary after normal registration with a known seventh digit.
        clock.Advance(TimeSpan.FromTicks(1_234_567));
        Assert.AreEqual(7L, clock.GetUtcNow().UtcTicks % TimeSpan.TicksPerMicrosecond);
        var request = new RoomCodeResetRequest(Guid.NewGuid(), room.AppliedVersion);
        var issued = await fixture.InScopeAsync(s => s.GetRequiredService<PublicRoomAccessService>()
            .ResetCodeAsync(owner.UserId, owner.SessionId, room.RoomId, request, CancellationToken.None));
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            var stored = await db.PublicRoomCredentials.AsNoTracking().Where(c => c.Id == issued.CredentialId)
                .Select(c => new { c.CreatedAt, c.ExpiresAt }).SingleAsync();
            var audit = await db.RoomOperationAudits.AsNoTracking().SingleAsync(a => a.ActorUserId == owner.UserId && a.RequestId == request.RequestId);
            using var json = JsonDocument.Parse(audit.ResultJson!);
            var acknowledged = json.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
            Assert.AreEqual(0L, issued.ExpiresAt.UtcTicks - stored.ExpiresAt.UtcTicks,
                "The response deadline must exactly match PostgreSQL's stored deadline.");
            Assert.AreEqual(stored.ExpiresAt.UtcTicks, acknowledged.UtcTicks);
            Assert.AreEqual(0L, stored.CreatedAt.UtcTicks % 10);
            Assert.IsFalse(audit.ResultJson!.Contains(issued.Code, StringComparison.Ordinal));
        });
        var replay = await fixture.InScopeAsync(s => s.GetRequiredService<PublicRoomAccessService>()
            .ResetCodeAsync(owner.UserId, owner.SessionId, room.RoomId, request, CancellationToken.None));
        Assert.IsTrue(replay == issued, "Replay must return the same acknowledgement without printing its code.");
        await RoomAccessTestSupport.EnterVisitorAsync(fixture, visitor, room.RoomId, issued.Code);
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            var receipts = await db.RoomOperationAudits.AsNoTracking().Where(a => a.RoomId == room.RoomId).ToArrayAsync();
            var events = await db.RoomDomainEvents.AsNoTracking().Where(e => e.RoomId == room.RoomId).ToArrayAsync();
            Assert.IsFalse(JsonSerializer.Serialize(new { receipts, events }).Contains(issued.Code, StringComparison.Ordinal),
                "Neither successful admission nor code-reset audit/outbox can contain the plaintext code.");
        });
    }
}
