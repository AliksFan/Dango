using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Pets;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class PetConfigurationHttpTests
{
    [TestMethod]
    public async Task HttpsReadsOwnPetAndOnlyAllowsCsrfProtectedVersionedConfigurationChanges()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var a = await PetTestSupport.RegisterAsync(fixture);
        var b = await PetTestSupport.RegisterAsync(fixture);
        var beforeA = await PetTestSupport.GraphAsync(fixture, a);
        var beforeB = await PetTestSupport.GraphAsync(fixture, b);
        using var clientA = fixture.CreateClient(out _);
        using var clientB = fixture.CreateClient(out _);
        using var anonymous = fixture.CreateClient(out _);
        using var unauthenticated = await anonymous.GetAsync("api/v1/me/pet");
        await ErrorAsync(unauthenticated, HttpStatusCode.Unauthorized, "authentication_required");
        await LoginAsync(clientA, a);
        await LoginAsync(clientB, b);
        var snapshotA = await GetAsync(clientA);
        var snapshotB = await GetAsync(clientB);
        Assert.AreEqual(a.UserId, snapshotA.UserId);
        Assert.AreEqual(a.PetId, snapshotA.PetId);
        Assert.AreEqual(b.PetId, snapshotB.PetId);
        Assert.AreNotEqual(snapshotA.PetId, snapshotB.PetId);
        using var details = await clientA.GetAsync("api/v1/me/pet");
        var json = await details.Content.ReadFromJsonAsync<JsonElement>();
        CollectionAssert.AreEquivalent(new[] { "userId", "petId", "configurationVersion", "configuration" }, json.EnumerateObject().Select(p => p.Name).ToArray());
        var update = new PetConfigurationUpdate(snapshotA.ConfigurationVersion, PetTestSupport.Appearance("#112233"));
        using var unprotected = await FormalTestFixture.SendAsync(clientA, HttpMethod.Patch, "api/v1/me/pet/configuration", update, csrf: false);
        await ErrorAsync(unprotected, HttpStatusCode.Forbidden, "request_protection_invalid");
        using var foreignOrigin = await FormalTestFixture.SendAsync(clientA, HttpMethod.Patch, "api/v1/me/pet/configuration", update,
            origin: "https://foreign.example.invalid");
        await ErrorAsync(foreignOrigin, HttpStatusCode.Forbidden, "origin_invalid");
        using var forgedOwner = await FormalTestFixture.SendAsync(clientA, HttpMethod.Patch, "api/v1/me/pet/configuration",
            new { update.ExpectedVersion, update.Configuration, userId = b.UserId, petId = b.PetId, roomId = b.RoomId });
        await ErrorAsync(forgedOwner, HttpStatusCode.BadRequest, "request_invalid");
        using var malformed = await FormalTestFixture.SendAsync(clientA, HttpMethod.Patch, "api/v1/me/pet/configuration",
            new PetConfigurationUpdate(snapshotA.ConfigurationVersion, B1InputTests.Parse("{\"appearanceSchemaVersion\":99}")));
        await ErrorAsync(malformed, HttpStatusCode.BadRequest, "appearance_invalid");
        Assert.AreEqual(beforeA, await PetTestSupport.GraphAsync(fixture, a));
        using var success = await FormalTestFixture.SendAsync(clientA, HttpMethod.Patch, "api/v1/me/pet/configuration", update);
        var changed = await FormalTestFixture.ReadAsync<PetConfigurationSnapshot>(success);
        Assert.AreEqual(snapshotA.ConfigurationVersion + 1, changed.ConfigurationVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(update.Configuration, changed.Configuration));
        PetTestSupport.AssertIdentityAndRoomUnchanged(beforeA, await PetTestSupport.GraphAsync(fixture, a));
        Assert.AreEqual(beforeB, await PetTestSupport.GraphAsync(fixture, b));
    }

    [TestMethod]
    public async Task RepeatingAcceptedPatchConflictsAndFreshSnapshotSupportsResponseLossRepair()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await PetTestSupport.RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        await LoginAsync(client, account);
        var original = await GetAsync(client);
        var update = new PetConfigurationUpdate(original.ConfigurationVersion, PetTestSupport.Appearance("#112233"));
        // Discarding the first response tests the exact server-side contract used after response loss.
        using (var discarded = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/pet/configuration", update))
            Assert.AreEqual(HttpStatusCode.OK, discarded.StatusCode);
        using var repeat = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/pet/configuration", update);
        await ErrorAsync(repeat, HttpStatusCode.Conflict, "pet_configuration_conflict");
        var current = await GetAsync(client);
        Assert.AreEqual(original.PetId, current.PetId);
        Assert.AreEqual(original.ConfigurationVersion + 1, current.ConfigurationVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(update.Configuration, current.Configuration));
        using var competingChange = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/pet/configuration",
            new PetConfigurationUpdate(current.ConfigurationVersion, PetTestSupport.Appearance("#778899")));
        Assert.AreEqual(HttpStatusCode.OK, competingChange.StatusCode);
        using var oldRetry = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/pet/configuration", update);
        await ErrorAsync(oldRetry, HttpStatusCode.Conflict, "pet_configuration_conflict");
        Assert.IsFalse(AppearanceContract.Equivalent(update.Configuration, (await GetAsync(client)).Configuration));
    }

    [TestMethod]
    public async Task RevokedCookieCannotReadOrWriteAndCorruptConfigurationIsNotExposedAsDefault()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await PetTestSupport.RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        var sessionId = await LoginAsync(client, account);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .RevokeAsync(account.UserId, sessionId, CancellationToken.None));
        using var rejectedRead = await client.GetAsync("api/v1/me/pet");
        await ErrorAsync(rejectedRead, HttpStatusCode.Unauthorized, "authentication_required");
        using var rejectedWrite = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/pet/configuration",
            new PetConfigurationUpdate(before.Version, PetTestSupport.Appearance("#112233")));
        await ErrorAsync(rejectedWrite, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
        await LoginAsync(client, account);
        await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().Pets.Where(p => p.Id == account.PetId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Configuration, "{\"appearanceSchemaVersion\":3}")));
        using var corrupt = await client.GetAsync("api/v1/me/pet");
        await ErrorAsync(corrupt, HttpStatusCode.ServiceUnavailable, "pet_configuration_invalid_state");
    }

    private static async Task<Guid> LoginAsync(HttpClient client, PetTestSupport.Account account)
    {
        using var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login",
            new { account.Email, account.Password, remembered = false });
        var login = await FormalTestFixture.ReadAsync<JsonElement>(response);
        return login.GetProperty("sessionId").GetGuid();
    }

    private static async Task<PetConfigurationSnapshot> GetAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/v1/me/pet");
        return await FormalTestFixture.ReadAsync<PetConfigurationSnapshot>(response);
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var json = await FormalTestFixture.ReadAsync<JsonElement>(response, status);
        Assert.AreEqual(code, json.GetProperty("code").GetString());
    }
}
