using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Management;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomsManagement;

internal static class RoomManagementTestSupport
{
    public static void RequireB3Approval()
    {
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_B3_TEST_APPROVED") != "1")
            Assert.Inconclusive("B3 migrations and fictional room writes need separate precise authorization before any connection.");
    }

    public static RoomManagementService Service(IServiceProvider services, IRoomManagementCheckpoint? checkpoint = null,
        AccountEnvironment? environment = null) => new(services.GetRequiredService<DangoDbContext>(),
        environment ?? services.GetRequiredService<AccountEnvironment>(), services.GetRequiredService<TimeProvider>(),
        checkpoint ?? new RoomManagementCheckpoint());

    public static Task<RoomOperationResult> CreateAsync(FormalTestFixture fixture, PetTestSupport.Account account,
        string kind = "privateShared", string name = "fictional room", int capacity = 6, Guid? requestId = null) =>
        fixture.InScopeAsync(services => Service(services).CreateAsync(account.UserId, account.SessionId,
            new(requestId ?? Guid.NewGuid(), kind, name, capacity), CancellationToken.None));

    public static Task<RoomManagementView> GetAsync(FormalTestFixture fixture, PetTestSupport.Account account, Guid roomId) =>
        fixture.InScopeAsync(services => Service(services).GetAsync(account.UserId, account.SessionId, roomId, CancellationToken.None));

    // New DI scope/context reads committed facts after the operation scope has been disposed.
    public static Task<string> GraphAsync(FormalTestFixture fixture, PetTestSupport.Account owner, params Guid[] extraPetIds) =>
        fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var rooms = await db.Rooms.AsNoTracking().Where(r => r.OwnerId == owner.UserId).OrderBy(r => r.Id).ToArrayAsync();
            var ids = rooms.Select(r => r.Id).ToArray();
            var petIds = extraPetIds.Append(owner.PetId).ToArray();
            var memberships = await db.RoomMemberships.AsNoTracking().Where(m => ids.Contains(m.RoomId)).OrderBy(m => m.RoomId).ThenBy(m => m.UserId).ToArrayAsync();
            var states = await db.PetRoomStates.AsNoTracking().Where(s => petIds.Contains(s.PetId) || ids.Contains(s.RoomId ?? Guid.Empty)).OrderBy(s => s.PetId).ToArrayAsync();
            var pets = await db.Pets.AsNoTracking().Where(p => petIds.Contains(p.Id)).OrderBy(p => p.Id).ToArrayAsync();
            var audits = await db.RoomOperationAudits.AsNoTracking().Where(a => a.ActorUserId == owner.UserId || ids.Contains(a.RoomId)).OrderBy(a => a.Id).ToArrayAsync();
            var events = await db.RoomDomainEvents.AsNoTracking().Where(e => ids.Contains(e.RoomId)).OrderBy(e => e.Id).ToArrayAsync();
            return JsonSerializer.Serialize(new { rooms, memberships, states, pets, audits, events });
        });

    public static Task PlaceAsync(FormalTestFixture fixture, Guid petId, Guid roomId, bool sleeping = false) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var state = await db.PetRoomStates.SingleAsync(s => s.PetId == petId);
        state.RoomId = roomId;
        state.PositionX = .25; state.PositionY = .5; state.IsSleeping = sleeping;
        state.Version = 7; state.ControlGeneration = 4;
        await db.SaveChangesAsync();
    });

    public static async Task FaultAsync(string code, int status, Func<Task> work)
    {
        var fault = await Assert.ThrowsExactlyAsync<AccountFault>(work);
        Assert.AreEqual(code, fault.Code);
        Assert.AreEqual(status, fault.Status);
    }

    internal sealed class InjectedFailure : Exception;
    internal sealed class FailureCheckpoint(string failureAt) : IRoomManagementCheckpoint
    {
        public Task ReachedAsync(string name, Guid roomId, CancellationToken ct) => name == failureAt ? throw new InjectedFailure() : Task.CompletedTask;
    }
    internal sealed class ActionCheckpoint(string step, Func<Task> action) : IRoomManagementCheckpoint
    {
        public Task ReachedAsync(string name, Guid roomId, CancellationToken ct) => name == step ? action() : Task.CompletedTask;
    }
}
