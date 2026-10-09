using System.Net;
using System.Text.Json;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dango.Server.Tests.Accounts;

[TestClass]
[TestCategory("PostgresIntegration")]
[TestCategory("BackendB5AIntegration")]
public sealed class AccountClientHttpsIntegrationTests
{
    private const string AuthCookie = "__Host-Dango.Auth";

    [TestMethod]
    public async Task RegistrationLoginAndPetUpdateUseRealHttpsSharedCookieCsrfAndPersistOnlyOwnConfiguration()
    {
        await using var fixture = await CreateFixtureAsync();
        var store = new MemorySecrets();
        var cookies = new CookieContainer();
        var transport = new ObservedTransport(fixture.CreatePinnedHandler(cookies));
        using var client = FormalAccountClient.ForTesting(fixture.Endpoint, transport, cookies, store);
        var account = await RegisterAsync(fixture, client);
        Assert.IsTrue(cookies.GetCookies(fixture.Endpoint)[AuthCookie] is null, "Completing registration cannot issue an authentication cookie.");
        Assert.IsNull(client.CurrentIdentity);
        var before = await GraphAsync(fixture, account);
        var identity = await client.LoginAsync(account.Email, account.Password, false);
        Assert.AreEqual(new AccountIdentity(account.Result.UserId, account.Result.PetId), identity);
        Assert.AreSame(cookies, client.Cookies);
        Assert.IsNotNull(cookies.GetCookies(fixture.Endpoint)[AuthCookie]);
        var pet = client.CreatePetClient(identity);
        var snapshot = await pet.ReadAsync(CancellationToken.None);
        var target = Appearance("#123456");
        var updated = await pet.UpdateAsync(snapshot.ConfigurationVersion, target, CancellationToken.None);
        Assert.AreEqual(snapshot.ConfigurationVersion + 1, updated.ConfigurationVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(target, updated.Configuration));
        var after = await GraphAsync(fixture, account);
        AssertIdentityAndRoomUnchanged(before, after);
        Assert.AreEqual(before.ConfigurationVersion + 1, after.ConfigurationVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(target, Parse(after.Configuration)));
        var login = transport.Requests.Single(request => request.Path == "/api/v1/auth/login");
        var patch = transport.Requests.Single(request => request.Method == HttpMethod.Patch);
        Assert.IsNotNull(login.Csrf);
        Assert.IsNotNull(patch.Csrf);
        Assert.IsFalse(login.Csrf == patch.Csrf, "The authenticated write must obtain a fresh proof after login.");
        Assert.IsTrue(transport.Requests.Where(request => request.Method != HttpMethod.Get).All(request => request.Csrf is not null));
    }

    [TestMethod]
    public async Task RememberedSessionRestoresOnlyAfterServerValidationAndRevocationRejectsSavedCookie()
    {
        await using var fixture = await CreateFixtureAsync();
        var store = new MemorySecrets();
        RegisteredAccount account;
        AccountIdentity identity;
        using (var first = CreateClient(fixture, store))
        {
            account = await RegisterAsync(fixture, first);
            identity = await first.LoginAsync(account.Email, account.Password, true);
        }
        var saved = store.Copy(fixture.Endpoint, AccountSecretSlots.Session);
        Assert.IsNotNull(saved);
        using (var restored = CreateClient(fixture, store))
        {
            Assert.AreEqual(identity, await restored.RestoreSessionAsync());
            Assert.AreEqual(identity.PetId, (await restored.CreatePetClient(identity).ReadAsync(CancellationToken.None)).PetId);
            await restored.LogoutAsync();
            Assert.IsFalse(store.Contains(fixture.Endpoint, AccountSecretSlots.Session));
        }
        var replayStore = new MemorySecrets();
        await replayStore.WriteAsync(fixture.Endpoint, AccountSecretSlots.Session, saved, CancellationToken.None);
        using var replay = CreateClient(fixture, replayStore);
        Assert.IsNull(await replay.RestoreSessionAsync());
        Assert.IsNull(replay.CurrentIdentity);
        Assert.IsFalse(replayStore.Contains(fixture.Endpoint, AccountSecretSlots.Session));
        await fixture.InScopeAsync(async services =>
        {
            var session = await services.GetRequiredService<DangoDbContext>().AuthSessions.AsNoTracking()
                .SingleAsync(row => row.UserId == account.Result.UserId);
            Assert.IsTrue(session.Remembered);
            Assert.AreEqual(TimeSpan.FromHours(168), session.ExpiresAt - session.CreatedAt);
            Assert.IsNotNull(session.RevokedAt);
        });
    }

    [TestMethod]
    public async Task CommittedStartAndCompleteResponseLossRecoverWithSameKeysWithoutReplacingPasswordOrAppearance()
    {
        await using var fixture = await CreateFixtureAsync();
        var email = NewEmail();
        var invitationProof = B1InputTests.NewProof();
        var invitationId = await fixture.InviteAsync(email, invitationProof);
        var original = RegistrationAttempt.Create(email, invitationId, invitationProof);
        var store = new MemorySecrets();
        var cookies = new CookieContainer();
        var transport = new ObservedTransport(fixture.CreatePinnedHandler(cookies)) { LoseStart = true, LoseComplete = true };
        using var client = FormalAccountClient.ForTesting(fixture.Endpoint, transport, cookies, store);
        var lostStart = await Assert.ThrowsExactlyAsync<AccountClientException>(() => client.StartRegistrationAsync(original));
        Assert.AreEqual("request_outcome_unknown", lostStart.Code);
        var restored = await client.RestoreRegistrationAsync();
        Assert.IsNotNull(restored);
        Assert.AreEqual(original.RequestKey, restored.RequestKey);
        Assert.AreEqual(original.CompletionKey, restored.CompletionKey);
        var pendingId = await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var pending = await db.PendingRegistrations.AsNoTracking().SingleAsync(row => row.RequestKey == original.RequestKey);
            Assert.AreEqual(RegistrationState.Pending, pending.State);
            Assert.IsNull(pending.UserId);
            Assert.AreEqual(1, await db.EmailChallenges.CountAsync(row => row.PendingRegistrationId == pending.Id));
            return pending.Id;
        });
        var resumed = await client.StartRegistrationAsync(restored);
        Assert.AreEqual(pendingId, resumed.Id);
        await ConfirmAsync(fixture, resumed.Id);
        var password = B1PostgresTests.NewPassword();
        var lostComplete = await Assert.ThrowsExactlyAsync<AccountClientException>(() =>
            client.CompleteRegistrationAsync(restored, password, Appearance("#FFFFFF")));
        Assert.AreEqual("request_outcome_unknown", lostComplete.Code);
        var committed = await fixture.InScopeAsync(async services =>
        {
            var pending = await services.GetRequiredService<DangoDbContext>().PendingRegistrations.AsNoTracking()
                .SingleAsync(row => row.Id == resumed.Id);
            Assert.AreEqual(RegistrationState.Completed, pending.State);
            Assert.AreEqual(restored.CompletionKey, pending.CompletionKey);
            return new Dango.Client.Sync.Accounts.RegistrationResult(pending.UserId!.Value, pending.PetId!.Value, pending.RoomId!.Value);
        });
        var differentPassword = B1PostgresTests.NewPassword();
        var recovered = await client.CompleteRegistrationAsync(restored, differentPassword, Appearance("#112233"));
        Assert.AreEqual(committed, recovered);
        Assert.IsNull(client.CurrentIdentity);
        Assert.IsTrue(cookies.GetCookies(fixture.Endpoint)[AuthCookie] is null, "Recovering registration cannot authenticate the client.");
        var starts = transport.Requests.Where(request => request.Path == "/api/v1/auth/registrations").ToArray();
        var completes = transport.Requests.Where(request => request.Path.EndsWith("/complete", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(2, starts.Length);
        Assert.IsTrue(starts.All(request => request.RequestKey == original.RequestKey));
        Assert.IsTrue(starts.All(request => request.RegistrationProof == starts[0].RegistrationProof));
        Assert.AreEqual(2, completes.Length);
        Assert.IsTrue(completes.All(request => request.CompletionKey == original.CompletionKey));
        Assert.IsTrue(completes.All(request => request.RegistrationProof == starts[0].RegistrationProof));
        var account = new RegisteredAccount(email, password, restored, recovered);
        var graph = await GraphAsync(fixture, account);
        Assert.AreEqual(1L, graph.ConfigurationVersion);
        Assert.IsTrue(AppearanceContract.Equivalent(Appearance("#FFFFFF"), Parse(graph.Configuration)));
        var changedPassword = await Assert.ThrowsExactlyAsync<AccountClientException>(() => client.LoginAsync(email, differentPassword, false));
        Assert.AreEqual("login_failed", changedPassword.Code);
        Assert.AreEqual(recovered.UserId, (await client.LoginAsync(email, password, false)).UserId);
    }

    [TestMethod]
    public async Task TwoBoundPetClientsSurfaceVersionConflictAndDoNotChangeAccountOrDefaultRoom()
    {
        await using var fixture = await CreateFixtureAsync();
        using var first = CreateClient(fixture, new MemorySecrets());
        var account = await RegisterAsync(fixture, first);
        var before = await GraphAsync(fixture, account);
        var firstPet = first.CreatePetClient(await first.LoginAsync(account.Email, account.Password, false));
        using var second = CreateClient(fixture, new MemorySecrets());
        var secondPet = second.CreatePetClient(await second.LoginAsync(account.Email, account.Password, false));
        var firstVersion = await firstPet.ReadAsync(CancellationToken.None);
        var secondVersion = await secondPet.ReadAsync(CancellationToken.None);
        Assert.AreEqual(firstVersion.ConfigurationVersion, secondVersion.ConfigurationVersion);
        await firstPet.UpdateAsync(firstVersion.ConfigurationVersion, Appearance("#ABCDEF"), CancellationToken.None);
        var conflict = await Assert.ThrowsExactlyAsync<PetConfigurationHttpException>(() =>
            secondPet.UpdateAsync(secondVersion.ConfigurationVersion, Appearance("#654321"), CancellationToken.None));
        Assert.AreEqual(PetHttpFailure.Conflict, conflict.Failure);
        Assert.AreEqual("pet_configuration_conflict", conflict.Code);
        var observed = await secondPet.ReadAsync(CancellationToken.None);
        Assert.IsTrue(AppearanceContract.Equivalent(Appearance("#ABCDEF"), observed.Configuration));
        var after = await GraphAsync(fixture, account);
        AssertIdentityAndRoomUnchanged(before, after);
        Assert.AreEqual(before.ConfigurationVersion + 1, after.ConfigurationVersion);
    }

    [TestMethod]
    public async Task LostLogoutResponseClearsLocalSecretAndRealServerRejectsPreviouslySavedCookie()
    {
        await using var fixture = await CreateFixtureAsync();
        var store = new MemorySecrets();
        var cookies = new CookieContainer();
        var transport = new ObservedTransport(fixture.CreatePinnedHandler(cookies)) { LoseLogout = true };
        using var client = FormalAccountClient.ForTesting(fixture.Endpoint, transport, cookies, store);
        var account = await RegisterAsync(fixture, client);
        var identity = await client.LoginAsync(account.Email, account.Password, true);
        var saved = store.Copy(fixture.Endpoint, AccountSecretSlots.Session);
        Assert.IsNotNull(saved);
        var pet = client.CreatePetClient(identity);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => client.LogoutAsync());
        Assert.AreEqual("request_outcome_unknown", error.Code);
        Assert.AreEqual(false, error.ServerRevocationConfirmed, "A missing response is not a confirmed revoke in the client.");
        Assert.AreEqual(true, error.LocalSecretCleared);
        Assert.IsNull(client.CurrentIdentity);
        Assert.IsFalse(store.Contains(fixture.Endpoint, AccountSecretSlots.Session));
        await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(() => pet.ReadAsync(CancellationToken.None));
        var replayStore = new MemorySecrets();
        await replayStore.WriteAsync(fixture.Endpoint, AccountSecretSlots.Session, saved, CancellationToken.None);
        using var replay = CreateClient(fixture, replayStore);
        Assert.IsNull(await replay.RestoreSessionAsync());
        await fixture.InScopeAsync(async services =>
        {
            var session = await services.GetRequiredService<DangoDbContext>().AuthSessions.AsNoTracking()
                .SingleAsync(row => row.UserId == account.Result.UserId);
            Assert.IsNotNull(session.RevokedAt);
        });
    }

    private static Task<FormalTestFixture> CreateFixtureAsync() =>
        FormalTestFixture.CreateAsync(startHttp: true, requireB5AApproval: true);

    private static FormalAccountClient CreateClient(FormalTestFixture fixture, MemorySecrets store)
    {
        var cookies = new CookieContainer();
        return FormalAccountClient.ForTesting(fixture.Endpoint, fixture.CreatePinnedHandler(cookies), cookies, store);
    }

    private static async Task<RegisteredAccount> RegisterAsync(FormalTestFixture fixture, FormalAccountClient client)
    {
        var email = NewEmail();
        var proof = B1InputTests.NewProof();
        var invitationId = await fixture.InviteAsync(email, proof);
        var attempt = RegistrationAttempt.Create(email, invitationId, proof);
        var pending = await client.StartRegistrationAsync(attempt);
        Assert.AreEqual(AccountRegistrationState.Pending, pending.State);
        Assert.AreEqual("queued", pending.EmailStatus, "No email worker or SMTP delivery is enabled in B5A.");
        await ConfirmAsync(fixture, pending.Id);
        Assert.AreEqual(AccountRegistrationState.Verified, (await client.ReadRegistrationStatusAsync(attempt)).State);
        var password = B1PostgresTests.NewPassword();
        var result = await client.CompleteRegistrationAsync(attempt, password, Appearance("#FFFFFF"));
        return new(email, password, attempt, result);
    }

    private static async Task ConfirmAsync(FormalTestFixture fixture, Guid pendingId)
    {
        // Tests read only their own new challenge, then exercise the actual protected confirmation API.
        // This bypasses delivery and browser UI; neither is claimed as verified.
        var proof = await fixture.RegistrationProofAsync(pendingId);
        using var browser = fixture.CreateClient(out _);
        using var response = await FormalTestFixture.SendAsync(browser, HttpMethod.Post, "api/v1/auth/email-verifications/confirm", new { proof });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<AccountGraph> GraphAsync(FormalTestFixture fixture, RegisteredAccount account) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var userId = account.Result.UserId;
        Assert.AreEqual(1, await db.Users.CountAsync(row => row.Id == userId && row.EmailConfirmed));
        Assert.AreEqual(1, await db.Pets.CountAsync(row => row.UserId == userId));
        Assert.AreEqual(1, await db.Rooms.CountAsync(row => row.OwnerId == userId));
        var pet = await db.Pets.AsNoTracking().SingleAsync(row => row.UserId == userId);
        var room = await db.Rooms.AsNoTracking().SingleAsync(row => row.OwnerId == userId);
        Assert.AreEqual(account.Result.PetId, pet.Id);
        Assert.AreEqual(account.Result.RoomId, room.Id);
        Assert.AreEqual(PersistentRoomKind.DesktopPet, room.Kind);
        var membership = await db.RoomMemberships.AsNoTracking().SingleAsync(row => row.UserId == userId && row.RoomId == room.Id);
        var state = await db.PetRoomStates.AsNoTracking().SingleAsync(row => row.PetId == pet.Id);
        Assert.AreEqual(room.Id, state.RoomId);
        return new AccountGraph(userId, pet.Id, pet.CreatedAt, pet.ConfigurationVersion, pet.Configuration,
            JsonSerializer.Serialize(new { room, membership, state }));
    });

    private static void AssertIdentityAndRoomUnchanged(AccountGraph first, AccountGraph second)
    {
        Assert.AreEqual(first.UserId, second.UserId);
        Assert.AreEqual(first.PetId, second.PetId);
        Assert.AreEqual(first.PetCreatedAt, second.PetCreatedAt);
        Assert.IsTrue(first.RoomFacts == second.RoomFacts);
    }

    private static string NewEmail() => $"b5a-{Guid.NewGuid():N}@example.invalid";
    private static JsonElement Appearance(string color) => Parse(B1InputTests.ValidAppearance.Replace("#FFFFFF", color, StringComparison.Ordinal));
    private static JsonElement Parse(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private sealed record RegisteredAccount(string Email, string Password, RegistrationAttempt Attempt,
        Dango.Client.Sync.Accounts.RegistrationResult Result)
    { public override string ToString() => "RegisteredAccount [redacted]"; }
    private sealed record AccountGraph(Guid UserId, Guid PetId, DateTimeOffset PetCreatedAt, long ConfigurationVersion,
        string Configuration, string RoomFacts);

    private sealed class MemorySecrets : IAccountSecretStore
    {
        private readonly Dictionary<(string Origin, string Slot), byte[]> values = [];
        public bool Contains(Uri origin, string slot) => values.ContainsKey((origin.AbsoluteUri, slot));
        public byte[]? Copy(Uri origin, string slot) => values.TryGetValue((origin.AbsoluteUri, slot), out var bytes) ? bytes.ToArray() : null;
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) => Task.FromResult(Copy(origin, slot));
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            values[(origin.AbsoluteUri, slot)] = value.ToArray();
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        {
            values.Remove((origin.AbsoluteUri, slot));
            return Task.CompletedTask;
        }
    }

    private sealed record ObservedRequest(HttpMethod Method, string Path, string? Csrf, string? RegistrationProof,
        Guid? RequestKey, Guid? CompletionKey)
    { public override string ToString() => "ObservedRequest [redacted]"; }

    private sealed class ObservedTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<ObservedRequest> Requests { get; } = [];
        public bool LoseStart { get; set; }
        public bool LoseComplete { get; set; }
        public bool LoseLogout { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Guid? requestKey = null;
            Guid? completionKey = null;
            if (request.Content is not null)
            {
                using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                if (json.RootElement.TryGetProperty("requestKey", out var start)) requestKey = start.GetGuid();
                if (json.RootElement.TryGetProperty("completionKey", out var complete)) completionKey = complete.GetGuid();
            }
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(new(request.Method, path, Header(request, "X-CSRF-TOKEN"), Header(request, "X-Registration-Proof"), requestKey, completionKey));
            var response = await base.SendAsync(request, ct);
            var lose = false;
            if (path == "/api/v1/auth/registrations" && LoseStart) { LoseStart = false; lose = true; }
            if (path.EndsWith("/complete", StringComparison.Ordinal) && LoseComplete) { LoseComplete = false; lose = true; }
            if (path == "/api/v1/auth/logout" && LoseLogout) { LoseLogout = false; lose = true; }
            if (!lose) return response;
            var successful = response.IsSuccessStatusCode;
            response.Dispose();
            Assert.IsTrue(successful, "Only a successfully committed HTTP result is discarded by this fault seam.");
            throw new HttpRequestException("fictional_response_loss");
        }
        private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
