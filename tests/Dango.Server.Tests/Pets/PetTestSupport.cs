using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Pets;
using Dango.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Pets;

internal static class PetTestSupport
{
    internal sealed record Account(Guid UserId, Guid PetId, Guid RoomId, Guid SessionId, string Email, string Password);
    internal sealed record Graph(Guid PetId, Guid UserId, DateTimeOffset CreatedAt, long Version, string Configuration, string RoomFacts);

    public static async Task<Account> RegisterAsync(FormalTestFixture fixture)
    {
        var pending = await B1PostgresTests.StartAsync(fixture);
        await B1PostgresTests.VerifyAsync(fixture, pending);
        var password = B1PostgresTests.NewPassword();
        var registered = await B1PostgresTests.CompleteAsync(fixture, pending, Guid.NewGuid(), password);
        var login = await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .LoginAsync(pending.Email, password, false, CancellationToken.None));
        return new(registered.UserId, registered.PetId, registered.RoomId, login.SessionId, pending.Email, password);
    }

    public static PetConfigurationService Service(IServiceProvider services, IPetConfigurationCheckpoint? checkpoint = null,
        AccountEnvironment? environment = null) => new(services.GetRequiredService<DangoDbContext>(),
        environment ?? services.GetRequiredService<AccountEnvironment>(), services.GetRequiredService<TimeProvider>(),
        checkpoint ?? new PetConfigurationCheckpoint());

    public static JsonElement Appearance(string bodyColor) => B1InputTests.Parse(
        B1InputTests.ValidAppearance.Replace("#FFFFFF", bodyColor, StringComparison.Ordinal));

    public static Task<Graph> GraphAsync(FormalTestFixture fixture, Account account) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var pet = await db.Pets.AsNoTracking().SingleAsync(p => p.UserId == account.UserId);
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.OwnerId == account.UserId).OrderBy(r => r.Id).ToListAsync();
        var ids = rooms.Select(r => r.Id).ToArray();
        var memberships = await db.RoomMemberships.AsNoTracking().Where(m => m.UserId == account.UserId || ids.Contains(m.RoomId))
            .OrderBy(m => m.RoomId).ThenBy(m => m.UserId).ToListAsync();
        var states = await db.PetRoomStates.AsNoTracking().Where(s => s.PetId == account.PetId).ToListAsync();
        Assert.AreEqual(1, await db.Pets.CountAsync(p => p.UserId == account.UserId));
        return new Graph(pet.Id, pet.UserId, pet.CreatedAt, pet.ConfigurationVersion, pet.Configuration,
            JsonSerializer.Serialize(new { rooms, memberships, states }));
    });

    public static void AssertIdentityAndRoomUnchanged(Graph before, Graph after)
    {
        Assert.AreEqual(before.PetId, after.PetId);
        Assert.AreEqual(before.UserId, after.UserId);
        Assert.AreEqual(before.CreatedAt, after.CreatedAt);
        Assert.AreEqual(before.RoomFacts, after.RoomFacts);
    }

    public static async Task FaultAsync(string code, Func<Task> work)
    {
        var fault = await Assert.ThrowsExactlyAsync<AccountFault>(work);
        Assert.AreEqual(code, fault.Code);
    }
}
