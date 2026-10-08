using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accounts;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class B1PostgresTests
{
    [TestMethod]
    public async Task StartResponseLossReturnsSamePendingAndCannotBeTakenOverByAnotherResumeProof()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var registration = await StartAsync(fixture);
        var same = await fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            registration.Email.ToUpperInvariant(), registration.InvitationId, registration.InvitationProof,
            registration.RequestKey, registration.ResumeProof, CancellationToken.None));
        Assert.AreEqual(registration.PendingId, same.Id);
        await FaultAsync("registration_proof_invalid", () => fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            registration.Email, registration.InvitationId, registration.InvitationProof,
            registration.RequestKey, B1InputTests.NewProof(), CancellationToken.None)));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.PendingRegistrations.CountAsync(r => r.RequestKey == registration.RequestKey));
            Assert.IsFalse(await db.Users.AnyAsync(u => u.NormalizedEmail == registration.Email.ToUpperInvariant()));
            Assert.AreEqual(1, await db.EmailChallenges.CountAsync(c => c.PendingRegistrationId == registration.PendingId));
            var stored = await db.PendingRegistrations.SingleAsync(r => r.Id == registration.PendingId);
            Assert.AreEqual(AccountInput.Digest(registration.ResumeProof), stored.ResumeDigest);
            Assert.IsFalse(string.Equals(registration.ResumeProof, stored.ResumeDigest, StringComparison.Ordinal));
        });
    }

    [TestMethod]
    public async Task UnverifiedCompletionMissingInvitationAndWrongEmailCreateNoAccount()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var registration = await StartAsync(fixture);
        await FaultAsync("email_verification_required", () => CompleteAsync(fixture, registration, Guid.NewGuid(), NewPassword()));
        await FaultAsync("invitation_invalid", () => fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            registration.Email, Guid.NewGuid(), B1InputTests.NewProof(), Guid.NewGuid(), B1InputTests.NewProof(), CancellationToken.None)));
        await FaultAsync("invitation_invalid", () => fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            $"other-{Guid.NewGuid():N}@example.invalid", registration.InvitationId, registration.InvitationProof,
            Guid.NewGuid(), B1InputTests.NewProof(), CancellationToken.None)));
        await AssertNoAccountAsync(fixture, registration);
    }

    [TestMethod]
    public async Task RevokedAndExpiredInvitationsRejectStartAndVerifiedCompletion()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var registration = await StartAsync(fixture);
        await VerifyAsync(fixture, registration);
        await fixture.InScopeAsync(services => services.GetRequiredService<InvitationService>().RevokeAsync(registration.InvitationId, CancellationToken.None));
        await FaultAsync("invitation_invalid", () => CompleteAsync(fixture, registration, Guid.NewGuid(), NewPassword()));
        var proof = B1InputTests.NewProof();
        var email = $"expiry-{Guid.NewGuid():N}@example.invalid";
        var invitation = await fixture.InviteAsync(email, proof);
        clock.Advance(TimeSpan.FromDays(7));
        await FaultAsync("invitation_invalid", () => fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            email, invitation, proof, Guid.NewGuid(), B1InputTests.NewProof(), CancellationToken.None)));
        await AssertNoAccountAsync(fixture, registration);
    }

    [TestMethod]
    public async Task VerificationRejectsTamperingCrossPurposeReplayAndExpiredProof()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var registration = await StartAsync(fixture);
        var proof = await fixture.RegistrationProofAsync(registration.PendingId);
        var index = proof.Length / 2;
        var tampered = proof[..index] + (proof[index] == 'A' ? 'B' : 'A') + proof[(index + 1)..];
        await FaultAsync("email_proof_invalid", () => ConfirmAsync(fixture, tampered));
        var crossPurpose = await fixture.InScopeAsync(async services =>
        {
            var challenge = await services.GetRequiredService<DangoDbContext>().EmailChallenges.AsNoTracking()
                .SingleAsync(c => c.PendingRegistrationId == registration.PendingId);
            challenge.Purpose = EmailChallengePurpose.PasswordReset;
            challenge.UserId = challenge.PendingRegistrationId;
            challenge.PendingRegistrationId = null;
            challenge.SecurityStamp = Guid.NewGuid().ToString("N");
            return services.GetRequiredService<IEmailProofProtector>().Protect(challenge);
        });
        await FaultAsync("email_proof_invalid", () => ConfirmAsync(fixture, crossPurpose));
        clock.Advance(TimeSpan.FromMinutes(30));
        await FaultAsync("email_proof_invalid", () => ConfirmAsync(fixture, proof));
        await AssertNoAccountAsync(fixture, registration);
    }

    [TestMethod]
    public async Task ResendHasCooldownHourlyCapFixedPendingExpiryAndRevokesOldGeneration()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var registration = await StartAsync(fixture);
        var oldProof = await fixture.RegistrationProofAsync(registration.PendingId);
        var originalExpiry = await fixture.InScopeAsync(async services => (await services.GetRequiredService<DangoDbContext>()
            .PendingRegistrations.AsNoTracking().SingleAsync(r => r.Id == registration.PendingId)).ExpiresAt);
        await FaultAsync("email_rate_limited", () => ResendAsync(fixture, registration));
        for (var attempt = 0; attempt < 4; attempt++)
        {
            clock.Advance(TimeSpan.FromSeconds(61));
            var resent = await ResendAsync(fixture, registration);
            Assert.AreEqual(originalExpiry, resent.ExpiresAt);
        }
        clock.Advance(TimeSpan.FromSeconds(61));
        await FaultAsync("email_rate_limited", () => ResendAsync(fixture, registration));
        await FaultAsync("email_proof_invalid", () => ConfirmAsync(fixture, oldProof));
        var currentProof = await fixture.RegistrationProofAsync(registration.PendingId);
        await ConfirmAsync(fixture, currentProof);
        await FaultAsync("email_proof_invalid", () => ConfirmAsync(fixture, currentProof));
        await FaultAsync("registration_state_invalid", () => ResendAsync(fixture, registration));
    }

    [TestMethod]
    public async Task VerifiedCompletionWindowIsAbsoluteAndRejectsTheExactDeadline()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var registration = await StartAsync(fixture);
        await VerifyAsync(fixture, registration);
        clock.Advance(TimeSpan.FromMinutes(10));
        await FaultAsync("registration_expired", () => CompleteAsync(fixture, registration, Guid.NewGuid(), NewPassword()));
        await AssertNoAccountAsync(fixture, registration);
    }

    [TestMethod]
    public async Task ConcurrentCompletionUsesIndependentConnectionsAndReturnsOneFixedGraph()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var registration = await StartAsync(fixture);
        await VerifyAsync(fixture, registration);
        var completionKey = Guid.NewGuid();
        var password = NewPassword();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        var connections = new List<System.Data.Common.DbConnection>();
        async Task<RegistrationResult> Compete() => await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            lock (connections) connections.Add(db.Database.GetDbConnection());
            if (Interlocked.Increment(ref arrived) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return await services.GetRequiredService<RegistrationService>().CompleteAsync(registration.PendingId,
                registration.ResumeProof, completionKey, password, B1InputTests.Parse(B1InputTests.ValidAppearance), CancellationToken.None);
        });
        var results = await Task.WhenAll(Compete(), Compete());
        Assert.AreNotSame(connections[0], connections[1]);
        Assert.AreEqual(results[0], results[1]);
        await AssertCompleteGraphAsync(fixture, registration, results[0]);
    }

    [TestMethod]
    public async Task ConcurrentDistinctRegistrationsForNormalizedEmailCreateOneAccount()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock);
        var first = await StartAsync(fixture);
        clock.Advance(TimeSpan.FromSeconds(61));
        var invitationProof = B1InputTests.NewProof();
        var invitation = await fixture.InviteAsync(first.Email.ToUpperInvariant(), invitationProof);
        var resume = B1InputTests.NewProof();
        var request = Guid.NewGuid();
        var status = await fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            first.Email.ToUpperInvariant(), invitation, invitationProof, request, resume, CancellationToken.None));
        var second = new RegistrationAttempt(first.Email.ToUpperInvariant(), invitation, invitationProof, request, resume, status.Id);
        await VerifyAsync(fixture, first);
        await VerifyAsync(fixture, second);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<(RegistrationResult? Result, string? Error)> Compete(RegistrationAttempt registration) => await fixture.InScopeAsync<(RegistrationResult? Result, string? Error)>(async services =>
        {
            if (Interlocked.Increment(ref arrived) == 2) ready.SetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                return (await services.GetRequiredService<RegistrationService>().CompleteAsync(registration.PendingId,
                    registration.ResumeProof, Guid.NewGuid(), NewPassword(), B1InputTests.Parse(B1InputTests.ValidAppearance), CancellationToken.None), null);
            }
            catch (AccountFault fault) { return ((RegistrationResult?)null, fault.Code); }
        });
        var results = await Task.WhenAll(Compete(first), Compete(second));
        Assert.AreEqual(1, results.Count(r => r.Result is not null));
        Assert.AreEqual(1, results.Count(r => r.Error == "registration_conflict"));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.Users.CountAsync(u => u.NormalizedEmail == first.Email.ToUpperInvariant()));
            Assert.AreEqual(1, await db.PendingRegistrations.CountAsync(r => r.NormalizedEmail == first.Email.ToUpperInvariant() && r.State == RegistrationState.Completed));
            Assert.AreEqual(1, await db.TestInvitations.CountAsync(i => i.NormalizedEmail == first.Email.ToUpperInvariant() && i.ConsumedAt != null));
        });
    }

    [TestMethod]
    public async Task CompletionDeadlineCrossedBeforeCommitRollsBackTheEntireGraph()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock, checkpoint: new DeadlineCheckpoint(clock));
        var registration = await StartAsync(fixture);
        await VerifyAsync(fixture, registration);
        await FaultAsync("registration_expired", () => CompleteAsync(fixture, registration, Guid.NewGuid(), NewPassword()));
        await AssertNoAccountAsync(fixture, registration);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(RegistrationState.Verified, (await db.PendingRegistrations.SingleAsync(r => r.Id == registration.PendingId)).State);
            Assert.IsNull((await db.TestInvitations.SingleAsync(i => i.Id == registration.InvitationId)).ConsumedAt);
        });
    }

    [TestMethod]
    public async Task CompletionResponseLossRetryCannotChangePasswordAppearanceOrCreateSession()
    {
        await using var fixture = await FormalTestFixture.CreateAsync();
        var registration = await StartAsync(fixture);
        await VerifyAsync(fixture, registration);
        var key = Guid.NewGuid();
        var password = NewPassword();
        var result = await CompleteAsync(fixture, registration, key, password);
        var changedPassword = NewPassword();
        var retry = await fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().CompleteAsync(
            registration.PendingId, registration.ResumeProof, key, changedPassword,
            B1InputTests.Parse(B1InputTests.ValidAppearance.Replace("#FFFFFF", "#000000", StringComparison.Ordinal)), CancellationToken.None));
        Assert.AreEqual(result, retry);
        await FaultAsync("registration_already_completed", () => CompleteAsync(fixture, registration, Guid.NewGuid(), changedPassword));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var manager = services.GetRequiredService<UserManager<DangoUser>>();
            var user = await db.Users.SingleAsync(u => u.Id == result.UserId);
            Assert.IsTrue(await manager.CheckPasswordAsync(user, password));
            Assert.IsFalse(await manager.CheckPasswordAsync(user, changedPassword));
            var pet = await db.Pets.SingleAsync(p => p.Id == result.PetId);
            Assert.AreEqual("#FFFFFF", B1InputTests.Parse(pet.Configuration).GetProperty("bodyColorHex").GetString());
            Assert.IsFalse(await db.AuthSessions.AnyAsync(s => s.UserId == result.UserId));
        });
        await AssertCompleteGraphAsync(fixture, registration, result);
    }

    [TestMethod]
    [DataRow("after_user")]
    [DataRow("after_pet")]
    [DataRow("after_room")]
    [DataRow("after_membership")]
    [DataRow("after_assignment")]
    [DataRow("before_commit")]
    public async Task EveryInitializationFailureRollsBackAccountGraphInvitationAndCompletion(string checkpoint)
    {
        await using var fixture = await FormalTestFixture.CreateAsync(checkpoint: new FailureCheckpoint(checkpoint));
        var registration = await StartAsync(fixture);
        await VerifyAsync(fixture, registration);
        await Assert.ThrowsExactlyAsync<InjectedInitializationFailure>(() => CompleteAsync(fixture, registration, Guid.NewGuid(), NewPassword()));
        await AssertNoAccountAsync(fixture, registration);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var pending = await db.PendingRegistrations.AsNoTracking().SingleAsync(r => r.Id == registration.PendingId);
            Assert.AreEqual(RegistrationState.Verified, pending.State);
            Assert.IsNull(pending.UserId);
            Assert.IsNull(pending.PetId);
            Assert.IsNull(pending.RoomId);
            Assert.IsNull(pending.CompletionKey);
            var invitation = await db.TestInvitations.AsNoTracking().SingleAsync(i => i.Id == registration.InvitationId);
            Assert.IsNull(invitation.ConsumedAt);
            Assert.IsNull(invitation.UserId);
        });
    }

    [TestMethod]
    public async Task NewRecoveryEpochRejectsOldPendingProofAndCompletedRetry()
    {
        await using var original = await FormalTestFixture.CreateAsync();
        var completed = await StartAsync(original);
        await VerifyAsync(original, completed);
        var key = Guid.NewGuid();
        await CompleteAsync(original, completed, key, NewPassword());
        var pending = await StartAsync(original);
        var proof = await original.RegistrationProofAsync(pending.PendingId);
        await using var changed = await FormalTestFixture.CreateAsync(environment: new AccountEnvironment(Guid.NewGuid()));
        await FaultAsync("registration_proof_invalid", () => changed.InScopeAsync(services => services.GetRequiredService<RegistrationService>()
            .StatusAsync(pending.PendingId, pending.ResumeProof, CancellationToken.None)));
        await FaultAsync("registration_proof_invalid", () => CompleteAsync(changed, completed, key, NewPassword()));
        await FaultAsync("email_proof_invalid", () => ConfirmAsync(changed, proof));
    }

    internal static async Task<RegistrationAttempt> StartAsync(FormalTestFixture fixture)
    {
        var email = $"b1-{Guid.NewGuid():N}@example.invalid";
        var invitationProof = B1InputTests.NewProof();
        var invitation = await fixture.InviteAsync(email, invitationProof);
        var resume = B1InputTests.NewProof();
        var request = Guid.NewGuid();
        var pending = await fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            email, invitation, invitationProof, request, resume, CancellationToken.None));
        return new(email, invitation, invitationProof, request, resume, pending.Id);
    }

    internal static Task VerifyAsync(FormalTestFixture fixture, RegistrationAttempt registration) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var challenge = await db.EmailChallenges.AsNoTracking().SingleAsync(c => c.PendingRegistrationId == registration.PendingId && c.RevokedAt == null);
        var proof = services.GetRequiredService<IEmailProofProtector>().Protect(challenge);
        await services.GetRequiredService<RegistrationService>().ConfirmAsync(proof, CancellationToken.None);
    });

    internal static Task<RegistrationResult> CompleteAsync(FormalTestFixture fixture, RegistrationAttempt registration, Guid key, string password) =>
        fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().CompleteAsync(registration.PendingId,
            registration.ResumeProof, key, password, B1InputTests.Parse(B1InputTests.ValidAppearance), CancellationToken.None));

    internal static string NewPassword() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private static Task ConfirmAsync(FormalTestFixture fixture, string proof) => fixture.InScopeAsync(services =>
        services.GetRequiredService<RegistrationService>().ConfirmAsync(proof, CancellationToken.None));
    private static Task<RegistrationStatus> ResendAsync(FormalTestFixture fixture, RegistrationAttempt registration) => fixture.InScopeAsync(services =>
        services.GetRequiredService<RegistrationService>().ResendAsync(registration.PendingId, registration.ResumeProof, CancellationToken.None));
    private static async Task FaultAsync(string code, Func<Task> work)
    {
        var fault = await Assert.ThrowsExactlyAsync<AccountFault>(work);
        Assert.AreEqual(code, fault.Code);
    }

    private static Task AssertNoAccountAsync(FormalTestFixture fixture, RegistrationAttempt registration) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        Assert.AreEqual(0, await db.Users.CountAsync(u => u.NormalizedEmail == registration.Email.ToUpperInvariant()));
        Assert.AreEqual(0, await (from pet in db.Pets
                                  join user in db.Users on pet.UserId equals user.Id
                                  where user.NormalizedEmail == registration.Email.ToUpperInvariant()
                                  select pet).CountAsync());
        Assert.AreEqual(0, await (from room in db.Rooms
                                  join user in db.Users on room.OwnerId equals user.Id
                                  where user.NormalizedEmail == registration.Email.ToUpperInvariant()
                                  select room).CountAsync());
    });

    private static Task AssertCompleteGraphAsync(FormalTestFixture fixture, RegistrationAttempt registration, RegistrationResult result) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        Assert.AreEqual(1, await db.Users.CountAsync(u => u.NormalizedEmail == registration.Email.ToUpperInvariant() && u.Id == result.UserId && u.EmailConfirmed));
        Assert.AreEqual(1, await db.Pets.CountAsync(p => p.UserId == result.UserId && p.Id == result.PetId));
        Assert.AreEqual(1, await db.Rooms.CountAsync(r => r.OwnerId == result.UserId && r.Kind == PersistentRoomKind.DesktopPet
            && r.Id == result.RoomId && r.Capacity == 2 && r.Status == PersistentRoomStatus.Active));
        Assert.AreEqual(1, await db.RoomMemberships.CountAsync(m => m.RoomId == result.RoomId && m.UserId == result.UserId && m.Role == PersistentRoomRole.Owner));
        Assert.AreEqual(1, await db.PetRoomStates.CountAsync(s => s.PetId == result.PetId && s.RoomId == result.RoomId));
        Assert.IsFalse(await db.AuthSessions.AnyAsync(s => s.UserId == result.UserId));
        var pending = await db.PendingRegistrations.SingleAsync(r => r.Id == registration.PendingId);
        Assert.AreEqual(RegistrationState.Completed, pending.State);
        Assert.AreEqual(result.UserId, pending.UserId);
        Assert.AreEqual(result.PetId, pending.PetId);
        Assert.AreEqual(result.RoomId, pending.RoomId);
        var invitation = await db.TestInvitations.SingleAsync(i => i.Id == registration.InvitationId);
        Assert.IsNotNull(invitation.ConsumedAt);
        Assert.AreEqual(result.UserId, invitation.UserId);
    });

    internal sealed record RegistrationAttempt(string Email, Guid InvitationId, string InvitationProof, Guid RequestKey, string ResumeProof, Guid PendingId);
    private sealed class InjectedInitializationFailure : Exception;
    private sealed class FailureCheckpoint(string failureAt) : IRegistrationCheckpoint
    {
        public Task ReachedAsync(string name, CancellationToken ct) => name == failureAt ? throw new InjectedInitializationFailure() : Task.CompletedTask;
    }
    private sealed class DeadlineCheckpoint(MutableTestClock clock) : IRegistrationCheckpoint
    {
        public Task ReachedAsync(string name, CancellationToken ct)
        {
            if (name == "before_commit") clock.Advance(TimeSpan.FromMinutes(10));
            return Task.CompletedTask;
        }
    }
}
