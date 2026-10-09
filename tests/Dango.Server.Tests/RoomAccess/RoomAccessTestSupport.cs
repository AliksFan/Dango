using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

internal static class RoomAccessTestSupport
{
    public static void RequireB4Approval()
    {
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_B4_TEST_APPROVED") != "1")
            Assert.Inconclusive("B4 migration and fictional access writes require separate precise approval before any connection.");
    }
    public static MutableTestClock Clock() => new(new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero));
    public static RoomAdmissionService Service(IServiceProvider services, IRoomAccessCheckpoint? checkpoint = null) =>
        new(services.GetRequiredService<DangoDbContext>(), services.GetRequiredService<AccountEnvironment>(), services.GetRequiredService<TimeProvider>(), checkpoint ?? new RoomAccessCheckpoint());
    public static Task<RoomOperationResult> CreateRoomAsync(FormalTestFixture fixture, PetTestSupport.Account owner, string kind = "publicShared", int capacity = 6) =>
        fixture.InScopeAsync(services => new RoomManagementService(services.GetRequiredService<DangoDbContext>(), services.GetRequiredService<AccountEnvironment>(),
            services.GetRequiredService<TimeProvider>(), new RoomManagementCheckpoint()).CreateAsync(owner.UserId, owner.SessionId,
                new(Guid.NewGuid(), kind, "fictional B4 room", capacity), CancellationToken.None));
    public static async Task<PersonalRoomSnapshot> PersonalAsync(FormalTestFixture fixture, PetTestSupport.Account account) =>
        await fixture.InScopeAsync(services => Service(services).PersonalSnapshotAsync(account.UserId, account.SessionId, CancellationToken.None));
    public static async Task<RoomAccessResult> EnterOwnerAsync(FormalTestFixture fixture, PetTestSupport.Account owner, Guid roomId, Guid? instance = null, string device = "desktop")
    {
        var current = await PersonalAsync(fixture, owner);
        return await fixture.InScopeAsync(services => Service(services).EnterAsync(owner.UserId, owner.SessionId, roomId,
            new(Guid.NewGuid(), instance ?? Guid.NewGuid(), device, current.RoomId, current.AssignmentVersion), CancellationToken.None));
    }
    // Admission tests control only the hash/generation/expiry fixture. Code protection and reset
    // API delivery have their own integration tests; this is never a product code-issuance path.
    public static async Task<string> CredentialAsync(FormalTestFixture fixture, Guid roomId, int lifetimeSeconds = 3600)
    {
        var code = B1InputTests.NewProof();
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var room = await db.Rooms.SingleAsync(r => r.Id == roomId);
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            db.PublicRoomCredentials.Add(new PublicRoomCredential
            {
                Id = Guid.NewGuid(), RoomId = roomId, Generation = room.AdmissionGeneration,
                RecoveryEpoch = services.GetRequiredService<AccountEnvironment>().RecoveryEpoch,
                CodeDigest = AccountInput.Digest(code), ProtectedCode = "fictional-credential-fixture",
                CreatedAt = now, ExpiresAt = now.AddSeconds(lifetimeSeconds)
            });
            await db.SaveChangesAsync();
        });
        return code;
    }
    public static async Task<RoomAccessResult> EnterVisitorAsync(FormalTestFixture fixture, PetTestSupport.Account visitor, Guid roomId, string code,
        Guid? instance = null, string device = "desktop")
    {
        var current = await PersonalAsync(fixture, visitor);
        return await fixture.InScopeAsync(services => Service(services).EnterAsync(visitor.UserId, visitor.SessionId, roomId,
            new(Guid.NewGuid(), instance ?? Guid.NewGuid(), device, current.RoomId, current.AssignmentVersion, code), CancellationToken.None));
    }
    public static Task<string> GraphAsync(FormalTestFixture fixture, params Guid[] petIds) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var pets = await db.Pets.AsNoTracking().Where(p => petIds.Contains(p.Id)).OrderBy(p => p.Id).ToArrayAsync();
        var users = pets.Select(p => p.UserId).ToArray();
        var states = await db.PetRoomStates.AsNoTracking().Where(s => petIds.Contains(s.PetId)).OrderBy(s => s.PetId).ToArrayAsync();
        var leases = await db.RoomDeviceLeases.AsNoTracking().Where(l => petIds.Contains(l.PetId)).OrderBy(l => l.Id).ToArrayAsync();
        var relatedRooms = states.Where(s => s.RoomId is not null).Select(s => s.RoomId!.Value).ToArray();
        var rooms = await db.Rooms.AsNoTracking().Where(r => users.Contains(r.OwnerId) || relatedRooms.Contains(r.Id)).OrderBy(r => r.Id).ToArrayAsync();
        var roomIds = rooms.Select(r => r.Id).ToArray();
        var events = await db.RoomDomainEvents.AsNoTracking().Where(e => roomIds.Contains(e.RoomId)).OrderBy(e => e.Id).ToArrayAsync();
        var receipts = await db.RoomOperationAudits.AsNoTracking().Where(a => roomIds.Contains(a.RoomId) || users.Contains(a.ActorUserId ?? Guid.Empty)).OrderBy(a => a.Id).ToArrayAsync();
        return JsonSerializer.Serialize(new { pets, states, leases, rooms, events, receipts });
    });
    public static async Task FaultAsync(string code, int status, Func<Task> action)
    {
        var fault = await Assert.ThrowsExactlyAsync<AccountFault>(action);
        Assert.AreEqual(code, fault.Code); Assert.AreEqual(status, fault.Status);
    }
    public sealed class InjectedFailure : Exception;
    public sealed class FailureCheckpoint(string step) : IRoomAccessCheckpoint
    {
        public Task ReachedAsync(string name, Guid petId, CancellationToken ct) => name == step ? throw new InjectedFailure() : Task.CompletedTask;
    }
    public sealed class ActionCheckpoint(string step, Func<Task> action) : IRoomAccessCheckpoint
    {
        public Task ReachedAsync(string name, Guid petId, CancellationToken ct) => name == step ? action() : Task.CompletedTask;
    }
}
