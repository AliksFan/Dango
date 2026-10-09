using System.Text.Json;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomExpiry;

internal static class RoomExpiryTestSupport
{
    public static Task<FormalTestFixture> FixtureAsync(TimeProvider? clock = null)
    {
        // This check precedes the existing B1 fixture's first connection and all fictional writes.
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_B3_TEST_APPROVED") != "1")
            Assert.Inconclusive("B3 expiry fixtures and permanent deletion need their own precise authorization.");
        return FormalTestFixture.CreateAsync(clock: clock);
    }

    public static DateTimeOffset Now() => new(DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    public static RoomExpiryService Service(IServiceProvider services, IRoomManagementCheckpoint? checkpoint = null) =>
        new(services.GetRequiredService<DangoDbContext>(), services.GetRequiredService<TimeProvider>(),
            checkpoint ?? new RoomManagementCheckpoint());

    public static Task<RoomExpiryBatchResult> PurgeAsync(FormalTestFixture fixture, IReadOnlyCollection<Guid> scope,
        int limit = 50, RoomExpiryCursor? after = null, IRoomManagementCheckpoint? checkpoint = null) =>
        fixture.InScopeAsync(services => Service(services, checkpoint).PurgeBatchAsync(limit, after, scope, CancellationToken.None));

    public static Task<Guid> SeedRoomAsync(FormalTestFixture fixture, PetTestSupport.Account owner, DateTimeOffset deadline,
        PersistentRoomStatus status = PersistentRoomStatus.Dissolved, PersistentRoomKind kind = PersistentRoomKind.PrivateShared,
        Guid? id = null) => fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var roomId = id ?? Guid.NewGuid();
            db.Rooms.Add(new()
            {
                Id = roomId,
                OwnerId = owner.UserId,
                Kind = kind,
                Status = status,
                Name = "Fictional expiry room",
                Capacity = 6,
                Version = 4,
                CreatedAt = deadline.AddDays(-10),
                DissolvedAt = status == PersistentRoomStatus.Active ? null : deadline.AddHours(-168),
                RecoverUntil = status == PersistentRoomStatus.Active ? null : deadline
            });
            db.RoomMemberships.Add(new() { RoomId = roomId, UserId = owner.UserId, Role = PersistentRoomRole.Owner });
            RoomPersistenceOperations.Append(db, roomId, 4, status == PersistentRoomStatus.Active ? "created" : "dissolved",
                deadline.AddHours(-168), owner.UserId, Guid.NewGuid(), new string('A', 64));
            await db.SaveChangesAsync();
            return roomId;
        });

    public static Task AssignAsync(FormalTestFixture fixture, Guid petId, Guid roomId, bool sleeping = true) =>
        fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var state = await db.PetRoomStates.SingleAsync(item => item.PetId == petId);
            state.RoomId = roomId; state.PositionX = .3; state.PositionY = .7; state.IsSleeping = sleeping;
            state.Version = 9; state.ControlGeneration = 3;
            await db.SaveChangesAsync();
        });

    public static Task<string> GraphAsync(FormalTestFixture fixture, IReadOnlyCollection<Guid> rooms, IReadOnlyCollection<Guid> pets) =>
        fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            return JsonSerializer.Serialize(new
            {
                Rooms = await db.Rooms.AsNoTracking().Where(r => rooms.Contains(r.Id)).OrderBy(r => r.Id).ToArrayAsync(),
                Members = await db.RoomMemberships.AsNoTracking().Where(m => rooms.Contains(m.RoomId)).OrderBy(m => m.RoomId).ThenBy(m => m.UserId).ToArrayAsync(),
                Pets = await db.Pets.AsNoTracking().Where(p => pets.Contains(p.Id)).OrderBy(p => p.Id).ToArrayAsync(),
                States = await db.PetRoomStates.AsNoTracking().Where(s => pets.Contains(s.PetId)).OrderBy(s => s.PetId).ToArrayAsync(),
                Audits = await db.RoomOperationAudits.AsNoTracking().Where(a => rooms.Contains(a.RoomId)).OrderBy(a => a.Id).ToArrayAsync(),
                Outbox = await db.RoomDomainEvents.AsNoTracking().Where(e => rooms.Contains(e.RoomId)).OrderBy(e => e.Id).ToArrayAsync()
            });
        });

    public sealed class FailureCheckpoint(string failureAt, Guid? target = null) : IRoomManagementCheckpoint
    {
        public Task ReachedAsync(string name, Guid roomId, CancellationToken ct) =>
            name == failureAt && (target is null || roomId == target) ? throw new InjectedFailure() : Task.CompletedTask;
    }

    private sealed class InjectedFailure : Exception;
}
