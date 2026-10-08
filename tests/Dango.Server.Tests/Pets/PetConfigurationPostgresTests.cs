using System.Collections.Concurrent;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Pets;
using Dango.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.Pets;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class PetConfigurationPostgresTests
{
    [TestMethod]
    public async Task SameVersionOnIndependentConnectionsAllowsOnlyOneChangeAndPreservesIdentityAndRoomFacts()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var account = await PetTestSupport.RegisterAsync(fixture);
        await fixture.InScopeAsync(async services =>
        {
            var state = await services.GetRequiredService<DangoDbContext>().PetRoomStates.SingleAsync(s => s.PetId == account.PetId);
            state.PositionX = .3; state.PositionY = .6; state.IsSleeping = true; state.Version = 7; state.ControlGeneration = 4;
            await services.GetRequiredService<DangoDbContext>().SaveChangesAsync();
        });
        var before = await PetTestSupport.GraphAsync(fixture, account);
        var connectionIds = new ConcurrentBag<int>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<(PetConfigurationSnapshot? Snapshot, string? Error)> Compete(string body) => await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            await db.Database.OpenConnectionAsync();
            connectionIds.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            if (Interlocked.Increment(ref arrived) == 2) release.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                var changed = await PetTestSupport.Service(services).UpdateAsync(account.UserId, account.SessionId,
                    new(before.Version, PetTestSupport.Appearance(body)), CancellationToken.None);
                return (changed, (string?)null);
            }
            catch (AccountFault fault) { return ((PetConfigurationSnapshot?)null, fault.Code); }
        });
        var results = await Task.WhenAll(Compete("#112233"), Compete("#778899"));
        Assert.AreEqual(2, connectionIds.Distinct().Count(), "The race must use independent PostgreSQL connections.");
        var winner = results.Single(r => r.Snapshot is not null).Snapshot!;
        Assert.AreEqual("pet_configuration_conflict", results.Single(r => r.Error is not null).Error);
        var after = await PetTestSupport.GraphAsync(fixture, account);
        Assert.AreEqual(before.Version + 1, after.Version);
        Assert.IsTrue(AppearanceContract.Equivalent(winner.Configuration, B1InputTests.Parse(after.Configuration)));
        PetTestSupport.AssertIdentityAndRoomUnchanged(before, after);
    }

    [TestMethod]
    [DataRow("after_configuration")]
    [DataRow("before_commit")]
    public async Task FailureAfterPersistingConfigurationRollsBackTheWholeUpdate(string failureAt)
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var account = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        await Assert.ThrowsExactlyAsync<InjectedFailure>(() => fixture.InScopeAsync(services => PetTestSupport.Service(services,
            new FailureCheckpoint(failureAt)).UpdateAsync(account.UserId, account.SessionId,
                new(before.Version, PetTestSupport.Appearance("#112233")), CancellationToken.None)));
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
    }

    [TestMethod]
    public async Task SessionExpiringAtPrecommitRollsBackPersistedConfiguration()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        await PetTestSupport.FaultAsync("authentication_required", () => fixture.InScopeAsync(services => PetTestSupport.Service(services,
            new DeadlineCheckpoint(clock)).UpdateAsync(account.UserId, account.SessionId,
                new(before.Version, PetTestSupport.Appearance("#112233")), CancellationToken.None)));
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("expired")]
    [DataRow("disabled")]
    [DataRow("recovery_locked")]
    [DataRow("temporary_lock")]
    [DataRow("security_stamp")]
    [DataRow("recovery_epoch")]
    public async Task ReadsAndWritesRecheckCurrentAccountAndSessionState(string invalidation)
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var account = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        Assert.IsTrue(await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .ValidateAsync(account.UserId, account.SessionId, CancellationToken.None)));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == account.UserId);
            var session = await db.AuthSessions.SingleAsync(s => s.Id == account.SessionId);
            switch (invalidation)
            {
                case "revoked": session.RevokedAt = clock.GetUtcNow(); break;
                case "expired": clock.Advance(TimeSpan.FromHours(8)); break;
                case "disabled": user.DisabledAt = clock.GetUtcNow(); break;
                case "recovery_locked": user.RecoveryLocked = true; break;
                case "temporary_lock": user.LockoutEnd = clock.GetUtcNow().AddMinutes(15); break;
                case "security_stamp": user.SecurityStamp = Guid.NewGuid().ToString("N"); break;
                case "recovery_epoch": session.RecoveryEpoch = Guid.NewGuid(); break;
            }
            await db.SaveChangesAsync();
        });
        await PetTestSupport.FaultAsync("authentication_required", () => fixture.InScopeAsync(services => PetTestSupport.Service(services)
            .GetAsync(account.UserId, account.SessionId, CancellationToken.None)));
        await PetTestSupport.FaultAsync("authentication_required", () => fixture.InScopeAsync(services => PetTestSupport.Service(services)
            .UpdateAsync(account.UserId, account.SessionId, new(before.Version, PetTestSupport.Appearance("#112233")), CancellationToken.None)));
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
    }

    [TestMethod]
    public async Task AnotherAccountsSessionCannotReadOrUpdatePetEvenAfterAnEarlierValidSessionCheck()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var a = await PetTestSupport.RegisterAsync(fixture);
        var b = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, a);
        await PetTestSupport.FaultAsync("authentication_required", () => fixture.InScopeAsync(services => PetTestSupport.Service(services)
            .GetAsync(a.UserId, b.SessionId, CancellationToken.None)));
        await PetTestSupport.FaultAsync("authentication_required", () => fixture.InScopeAsync(services => PetTestSupport.Service(services)
            .UpdateAsync(a.UserId, b.SessionId, new(before.Version, PetTestSupport.Appearance("#112233")), CancellationToken.None)));
        await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().RevokeAsync(a.UserId, a.SessionId, CancellationToken.None));
        await PetTestSupport.FaultAsync("authentication_required", () => fixture.InScopeAsync(services => PetTestSupport.Service(services)
            .UpdateAsync(a.UserId, a.SessionId, new(before.Version, PetTestSupport.Appearance("#112233")), CancellationToken.None)));
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, a));
    }

    [TestMethod]
    public async Task RevocationCommittedAfterEarlierAuthenticationButBeforeWriteLocksRejectsUpdate()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var account = await PetTestSupport.RegisterAsync(fixture);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        Assert.IsTrue(await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .ValidateAsync(account.UserId, account.SessionId, CancellationToken.None)));
        var checkpoint = new RevokeBeforeLocksCheckpoint(() => fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .RevokeAsync(account.UserId, account.SessionId, CancellationToken.None)));
        await PetTestSupport.FaultAsync("authentication_required", () => fixture.InScopeAsync(services => PetTestSupport.Service(services, checkpoint)
            .UpdateAsync(account.UserId, account.SessionId, new(before.Version, PetTestSupport.Appearance("#112233")), CancellationToken.None)));
        Assert.IsTrue(checkpoint.Revoked);
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
        Assert.IsFalse(await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>()
            .ValidateAsync(account.UserId, account.SessionId, CancellationToken.None)));
    }

    [TestMethod]
    public async Task ValidNumericUnderflowIsStoredAsBoundedContractValueInsteadOfExpandingRawJsonb()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var account = await PetTestSupport.RegisterAsync(fixture);
        var configuration = B1InputTests.Parse(B1InputTests.ValidAppearance.Replace("\"eyeOvalityPercent\":0", "\"eyeOvalityPercent\":1e-9999", StringComparison.Ordinal));
        var changed = await fixture.InScopeAsync(services => PetTestSupport.Service(services).UpdateAsync(account.UserId, account.SessionId,
            new(1, configuration), CancellationToken.None));
        Assert.AreEqual(0d, changed.Configuration.GetProperty("eyeOvalityPercent").GetDouble());
        var stored = await PetTestSupport.GraphAsync(fixture, account);
        Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(stored.Configuration) <= AppearanceContract.MaximumBytes);
        Assert.IsTrue(AppearanceContract.Equivalent(configuration, B1InputTests.Parse(stored.Configuration)));
    }

    [TestMethod]
    public async Task CorruptPersistedConfigurationIsNeitherReturnedAsDefaultNorRepairedByPatch()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var account = await PetTestSupport.RegisterAsync(fixture);
        await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().Pets.Where(p => p.Id == account.PetId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Configuration, "{\"appearanceSchemaVersion\":3}")));
        var before = await PetTestSupport.GraphAsync(fixture, account);
        await PetTestSupport.FaultAsync("pet_configuration_invalid_state", () => fixture.InScopeAsync(services => PetTestSupport.Service(services)
            .GetAsync(account.UserId, account.SessionId, CancellationToken.None)));
        await PetTestSupport.FaultAsync("pet_configuration_invalid_state", () => fixture.InScopeAsync(services => PetTestSupport.Service(services)
            .UpdateAsync(account.UserId, account.SessionId, new(before.Version, PetTestSupport.Appearance("#112233")), CancellationToken.None)));
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
    }

    private sealed class InjectedFailure : Exception;
    private sealed class FailureCheckpoint(string failureAt) : IPetConfigurationCheckpoint
    {
        public Task ReachedAsync(string name, CancellationToken ct) => name == failureAt ? throw new InjectedFailure() : Task.CompletedTask;
    }
    private sealed class DeadlineCheckpoint(MutableTestClock clock) : IPetConfigurationCheckpoint
    {
        public Task ReachedAsync(string name, CancellationToken ct)
        {
            if (name == "before_commit") clock.Advance(TimeSpan.FromHours(8));
            return Task.CompletedTask;
        }
    }
    private sealed class RevokeBeforeLocksCheckpoint(Func<Task> revoke) : IPetConfigurationCheckpoint
    {
        public bool Revoked { get; private set; }
        public async Task ReachedAsync(string name, CancellationToken ct)
        {
            if (name != "before_locks") return;
            await revoke();
            Revoked = true;
        }
    }
}
