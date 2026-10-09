using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Dango.Server.Rooms.Access;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

internal static class PublicRoomTestSupport
{
    public static Task<FormalTestFixture> FixtureAsync(TimeProvider? clock = null)
    {
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_B4_TEST_APPROVED") != "1")
            Assert.Inconclusive("B4 public credential, ban and lease writes need separate precise authorization before any connection.");
        return FormalTestFixture.CreateAsync(clock: clock);
    }

    public static MutableTestClock Clock() => new(new DateTimeOffset(DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero));

    public static PublicRoomAccessService Service(IServiceProvider services, IRoomAccessCheckpoint? checkpoint = null)
    {
        var environment = services.GetRequiredService<AccountEnvironment>();
        return new(services.GetRequiredService<DangoDbContext>(), environment, services.GetRequiredService<TimeProvider>(),
            checkpoint ?? new RoomAccessCheckpoint(), new PublicRoomCodeProtector(services.GetRequiredService<IDataProtectionProvider>(), environment));
    }

    public static Task<Guid> RoomAsync(FormalTestFixture fixture, PetTestSupport.Account owner,
        PersistentRoomKind kind = PersistentRoomKind.PublicShared) => fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var room = new PersistentRoom
            {
                Id = Guid.NewGuid(),
                OwnerId = owner.UserId,
                Kind = kind,
                Name = "Fictional public room",
                Status = PersistentRoomStatus.Active,
                Capacity = 6,
                Version = 1,
                AdmissionGeneration = 1,
                CreatedAt = services.GetRequiredService<TimeProvider>().GetUtcNow()
            };
            db.Rooms.Add(room);
            db.RoomMemberships.Add(new() { RoomId = room.Id, UserId = owner.UserId, Role = PersistentRoomRole.Owner });
            RoomPersistenceOperations.Append(db, room.Id, 1, "created", room.CreatedAt, owner.UserId, Guid.NewGuid(), new string('A', 64));
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return room.Id;
        });

    public static Task<RoomCodeResult> ResetAsync(FormalTestFixture fixture, PetTestSupport.Account owner, Guid roomId,
        long expectedVersion, Guid? requestId = null, int hours = 24, IRoomAccessCheckpoint? checkpoint = null) =>
        fixture.InScopeAsync(services => Service(services, checkpoint).ResetCodeAsync(owner.UserId, owner.SessionId, roomId,
            new(requestId ?? Guid.NewGuid(), expectedVersion, hours), CancellationToken.None));

    public static Task<RoomDoorView> ResolveAsync(FormalTestFixture fixture, PetTestSupport.Account account, string code,
        IRoomAccessCheckpoint? checkpoint = null) => fixture.InScopeAsync(services => Service(services, checkpoint)
            .ResolveAsync(account.UserId, account.SessionId, new(code), CancellationToken.None));

    public static Task<Guid> LeaseAsync(FormalTestFixture fixture, PetTestSupport.Account account, Guid roomId) =>
        fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            var lease = new RoomDeviceLease
            {
                Id = Guid.NewGuid(),
                PetId = account.PetId,
                UserId = account.UserId,
                SessionId = account.SessionId,
                InstanceId = Guid.NewGuid(),
                DeviceKind = "desktop",
                RoomId = roomId,
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = now.AddSeconds(30),
                TransportId = "fictional-transport"
            };
            db.RoomDeviceLeases.Add(lease);
            var state = await db.PetRoomStates.SingleAsync(s => s.PetId == account.PetId);
            state.RoomId = roomId; state.PositionX = .3; state.PositionY = .5; state.IsSleeping = false;
            state.Version = 7; state.ControlGeneration = 4; state.ControlLeaseId = lease.Id;
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            return lease.Id;
        });

    public static Task<string> GraphAsync(FormalTestFixture fixture, Guid[] rooms, Guid[] pets) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        // Compare every saved fact without printing credentials if an assertion fails.
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Rooms = await db.Rooms.AsNoTracking().Where(r => rooms.Contains(r.Id)).OrderBy(r => r.Id).ToArrayAsync(),
            Members = await db.RoomMemberships.AsNoTracking().Where(m => rooms.Contains(m.RoomId)).OrderBy(m => m.RoomId).ThenBy(m => m.UserId).ToArrayAsync(),
            States = await db.PetRoomStates.AsNoTracking().Where(s => pets.Contains(s.PetId)).OrderBy(s => s.PetId).ToArrayAsync(),
            Leases = await db.RoomDeviceLeases.AsNoTracking().Where(l => pets.Contains(l.PetId)).OrderBy(l => l.Id).ToArrayAsync(),
            Bans = await db.RoomBans.AsNoTracking().Where(b => rooms.Contains(b.RoomId)).OrderBy(b => b.RoomId).ThenBy(b => b.UserId).ToArrayAsync(),
            Credentials = await db.PublicRoomCredentials.AsNoTracking().Where(c => rooms.Contains(c.RoomId)).OrderBy(c => c.Id).ToArrayAsync(),
            Audits = await db.RoomOperationAudits.AsNoTracking().Where(a => rooms.Contains(a.RoomId)).OrderBy(a => a.Id).ToArrayAsync(),
            Events = await db.RoomDomainEvents.AsNoTracking().Where(e => rooms.Contains(e.RoomId)).OrderBy(e => e.Id).ToArrayAsync()
        })));
    });

    public static async Task FaultAsync(string code, int status, Func<Task> action)
    {
        var fault = await Assert.ThrowsExactlyAsync<AccountFault>(action);
        Assert.AreEqual(code, fault.Code); Assert.AreEqual(status, fault.Status);
    }

    public sealed class CallbackCheckpoint(Func<string, Guid, CancellationToken, Task> callback) : IRoomAccessCheckpoint
    {
        public Task ReachedAsync(string name, Guid petId, CancellationToken ct) => callback(name, petId, ct);
    }

    public sealed class FailureCheckpoint(string failureAt) : IRoomAccessCheckpoint
    {
        public Task ReachedAsync(string name, Guid petId, CancellationToken ct) => name == failureAt ? throw new InjectedFailure() : Task.CompletedTask;
    }
    public sealed class InjectedFailure : Exception;
}
