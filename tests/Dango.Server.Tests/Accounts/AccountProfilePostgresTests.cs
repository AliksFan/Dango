using System.Collections.Concurrent;
using System.Net;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.Accounts;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class AccountProfilePostgresTests
{
    [TestInitialize]
    public void RequireNewProfileDataApprovalBeforeAnyResourceAccess()
    {
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_PROFILE_TEST_APPROVED") != "1")
            Assert.Inconclusive("Nickname migration and fictional profile writes require their own precise approval; B1/B5/R2 approval is insufficient.");
    }

    [TestMethod]
    public async Task IndependentConnectionsWithSameExpectedVersionProduceOneCommitAndOneConflict()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var account = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        var stamp = await fixture.InScopeAsync(s => s.GetRequiredService<DangoDbContext>().Users.AsNoTracking()
            .Where(u => u.Id == account.UserId).Select(u => u.SecurityStamp).SingleAsync());
        var processes = new ConcurrentBag<int>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<(AccountProfileSnapshot? Result, string? Error)> Compete(string nickname) => await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync();
            processes.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            if (Interlocked.Increment(ref arrived) == 2) release.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { return (await Service(s).UpdateAsync(account.UserId, account.SessionId, new(1, nickname), CancellationToken.None), (string?)null); }
            catch (AccountFault fault) { return ((AccountProfileSnapshot?)null, fault.Code); }
        });
        var results = await Task.WhenAll(Compete("团子甲"), Compete("团子乙"));
        Assert.AreEqual(2, processes.Distinct().Count());
        var winner = results.Single(r => r.Result is not null).Result!;
        Assert.AreEqual("account_profile_conflict", results.Single(r => r.Error is not null).Error);
        Assert.AreEqual(2L, winner.ProfileVersion);
        Assert.AreEqual(winner, await fixture.InScopeAsync(s => Service(s).GetAsync(account.UserId, account.SessionId, CancellationToken.None)));
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
        Assert.AreEqual(stamp, await fixture.InScopeAsync(s => s.GetRequiredService<DangoDbContext>().Users.AsNoTracking()
            .Where(u => u.Id == account.UserId).Select(u => u.SecurityStamp).SingleAsync()));
    }

    [TestMethod]
    public async Task DuplicateNamesAndSequentialEditsDoNotChangeAccountPetOrRoomIdentity()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var a = await PetTestSupport.RegisterAsync(fixture); var b = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, a);
        var first = await fixture.InScopeAsync(s => Service(s).UpdateAsync(a.UserId, a.SessionId, new(1, "小团子"), CancellationToken.None));
        var same = await fixture.InScopeAsync(s => Service(s).UpdateAsync(b.UserId, b.SessionId, new(1, "小团子"), CancellationToken.None));
        Assert.AreEqual(first.Nickname, same.Nickname); Assert.AreNotEqual(first.UserId, same.UserId);
        var changed = await fixture.InScopeAsync(s => Service(s).UpdateAsync(a.UserId, a.SessionId, new(2, "新团子"), CancellationToken.None));
        Assert.AreEqual(3L, changed.ProfileVersion);
        await Fault("account_profile_conflict", () => fixture.InScopeAsync(s => Service(s).UpdateAsync(a.UserId, a.SessionId,
            new(2, "过时修改"), CancellationToken.None)));
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, a));
    }

    [TestMethod]
    [DataRow("after_profile")]
    [DataRow("before_commit")]
    public async Task FailureAfterSaveRollsBackNicknameAndVersion(string failureAt)
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var account = await PetTestSupport.RegisterAsync(fixture);
        var before = await fixture.InScopeAsync(s => Service(s).GetAsync(account.UserId, account.SessionId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InjectedFailure>(() => fixture.InScopeAsync(s => Service(s,
            new CallbackCheckpoint((name, _) => name == failureAt ? Task.FromException(new InjectedFailure()) : Task.CompletedTask))
            .UpdateAsync(account.UserId, account.SessionId, new(1, "不会保存"), CancellationToken.None)));
        Assert.AreEqual(before, await fixture.InScopeAsync(s => Service(s).GetAsync(account.UserId, account.SessionId, CancellationToken.None)));
    }

    [TestMethod]
    public async Task CrossAccountSessionCannotReadOrWriteAndRevocationBeforeLocksIsRechecked()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var a = await PetTestSupport.RegisterAsync(fixture); var b = await PetTestSupport.RegisterAsync(fixture);
        await Fault("authentication_required", () => fixture.InScopeAsync(s => Service(s).GetAsync(a.UserId, b.SessionId, CancellationToken.None)));
        await Fault("authentication_required", () => fixture.InScopeAsync(s => Service(s).UpdateAsync(a.UserId, b.SessionId, new(1, "冒名修改"), CancellationToken.None)));
        var checkpoint = new CallbackCheckpoint(async (name, _) =>
        {
            if (name == "before_locks") await fixture.InScopeAsync(s => s.GetRequiredService<SessionService>()
                .RevokeAsync(a.UserId, a.SessionId, CancellationToken.None));
        });
        await Fault("authentication_required", () => fixture.InScopeAsync(s => Service(s, checkpoint)
            .UpdateAsync(a.UserId, a.SessionId, new(1, "撤销会话"), CancellationToken.None)));
        await fixture.InScopeAsync(async s =>
        {
            var user = await s.GetRequiredService<DangoDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == a.UserId);
            Assert.IsNull(user.Nickname); Assert.AreEqual(1L, user.ProfileVersion);
        });
    }

    [TestMethod]
    public async Task SessionExpiryAtPrecommitRollsBackNickname()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var checkpoint = new CallbackCheckpoint((name, _) =>
        {
            if (name == "before_commit") clock.Advance(TimeSpan.FromHours(8));
            return Task.CompletedTask;
        });
        await Fault("authentication_required", () => fixture.InScopeAsync(s => Service(s, checkpoint)
            .UpdateAsync(account.UserId, account.SessionId, new(1, "过期修改"), CancellationToken.None)));
        await fixture.InScopeAsync(async s =>
        {
            var user = await s.GetRequiredService<DangoDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == account.UserId);
            Assert.IsNull(user.Nickname); Assert.AreEqual(1L, user.ProfileVersion);
        });
    }

    [TestMethod]
    public async Task ProfileHttpsUsesCurrentSessionRejectsForeignOwnershipCsrfOriginAndMalformedBodies()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await PetTestSupport.RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        using var anonymous = await client.GetAsync("api/v1/me/profile");
        Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var login = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login",
            new { email = account.Email, password = account.Password, remembered = false });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        using var legacy = await client.GetAsync("api/v1/me/profile");
        Assert.IsNull((await FormalTestFixture.ReadAsync<AccountProfileSnapshot>(legacy)).Nickname);
        using var missing = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/profile", new AccountProfileUpdate(1, "团子"), csrf: false);
        Assert.AreEqual(HttpStatusCode.Forbidden, missing.StatusCode);
        using var foreign = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/profile", new AccountProfileUpdate(1, "团子"), origin: "https://foreign.invalid");
        Assert.AreEqual(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var forged = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/profile",
            new { expectedVersion = 1, nickname = "团子", userId = Guid.NewGuid() });
        Assert.AreEqual(HttpStatusCode.BadRequest, forged.StatusCode);
        foreach (var json in new[] { "{\"expectedVersion\":1}", "{\"expectedVersion\":1,\"nickname\":null}",
            "{\"expectedVersion\":1,\"nickname\":\"团子\",\"nickname\":\"重复字段\"}" })
        {
            var token = await FormalTestFixture.CsrfAsync(client);
            using var request = new HttpRequestMessage(HttpMethod.Patch, "api/v1/me/profile")
            { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Add("X-CSRF-TOKEN", token);
            using var malformed = await client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        }
        using var invalidName = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/profile", new AccountProfileUpdate(1, "团子\n"));
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidName.StatusCode);
        using var valid = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, "api/v1/me/profile", new AccountProfileUpdate(1, "  团子  "));
        var saved = await FormalTestFixture.ReadAsync<AccountProfileSnapshot>(valid);
        Assert.AreEqual(account.UserId, saved.UserId); Assert.AreEqual("团子", saved.Nickname); Assert.AreEqual(2L, saved.ProfileVersion);
        using var logout = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/logout");
        Assert.AreEqual(HttpStatusCode.OK, logout.StatusCode);
        using var revoked = await client.GetAsync("api/v1/me/profile");
        Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    private static AccountProfileService Service(IServiceProvider s, IAccountProfileCheckpoint? checkpoint = null) => new(
        s.GetRequiredService<DangoDbContext>(), s.GetRequiredService<AccountEnvironment>(), s.GetRequiredService<TimeProvider>(),
        checkpoint ?? new AccountProfileCheckpoint());
    private static async Task Fault(string code, Func<Task> work) =>
        Assert.AreEqual(code, (await Assert.ThrowsExactlyAsync<AccountFault>(work)).Code);
    private sealed class InjectedFailure : Exception;
    private sealed class CallbackCheckpoint(Func<string, CancellationToken, Task> callback) : IAccountProfileCheckpoint
    { public Task ReachedAsync(string name, CancellationToken ct) => callback(name, ct); }
}
