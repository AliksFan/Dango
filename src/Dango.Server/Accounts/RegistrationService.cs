using System.Text.Json;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Accounts;

public sealed record RegistrationStatus(Guid Id, RegistrationState State, DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletionExpiresAt, string EmailStatus);
public sealed record RegistrationResult(Guid UserId, Guid PetId, Guid RoomId);

public sealed class RegistrationService(DangoDbContext db, UserManager<DangoUser> users, ChallengeIssuer issuer,
    IEmailProofProtector proofs, AccountEnvironment environment, TimeProvider clock, IRegistrationCheckpoint checkpoint)
{
    public async Task<RegistrationStatus> StartAsync(string email, Guid invitationId, string invitationProof,
        Guid requestKey, string resumeProof, CancellationToken ct)
    {
        var input = AccountInput.Email(email);
        var digest = AccountInput.Digest(resumeProof);
        if (requestKey == Guid.Empty || invitationId == Guid.Empty) throw new AccountFault("registration_input_invalid");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, input.Normalized, ct);
        var existing = await db.PendingRegistrations.SingleOrDefaultAsync(r => r.RequestKey == requestKey, ct);
        if (existing is not null)
        {
            if (existing.NormalizedEmail != input.Normalized || existing.InvitationId != invitationId)
                throw new AccountFault("registration_conflict", 409);
            CheckResume(existing, resumeProof);
            var original = await db.TestInvitations.SingleAsync(i => i.Id == invitationId, ct);
            if (!AccountInput.Matches(original.SecretDigest, invitationProof)) throw new AccountFault("invitation_invalid", 403);
            return await DescribeAsync(existing, ct);
        }
        var invitation = await AccountLocks.InvitationAsync(db, invitationId, ct);
        CheckInvitation(invitation, input.Normalized, clock.GetUtcNow());
        if (!AccountInput.Matches(invitation!.SecretDigest, invitationProof)) throw new AccountFault("invitation_invalid", 403);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == input.Normalized, ct)
            || await db.PendingRegistrations.AnyAsync(r => r.InvitationId == invitationId, ct)) throw new AccountFault("registration_conflict", 409);
        var now = clock.GetUtcNow();
        var pending = new PendingRegistration
        {
            Id = Guid.NewGuid(),
            RequestKey = requestKey,
            ResumeDigest = digest,
            InvitationId = invitationId,
            Email = input.Email,
            NormalizedEmail = input.Normalized,
            RecoveryEpoch = environment.RecoveryEpoch,
            State = RegistrationState.Pending,
            Generation = 1,
            CreatedAt = now,
            ExpiresAt = Earlier(now.AddHours(1), invitation.ExpiresAt),
            LastSentAt = now
        };
        db.PendingRegistrations.Add(pending);
        await issuer.AddAsync(EmailChallengePurpose.Registration, input.Normalized, pending.Id, null, null, 1,
            Earlier(now.AddMinutes(30), pending.ExpiresAt), ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await DescribeAsync(pending, ct);
    }

    public async Task<RegistrationStatus> StatusAsync(Guid id, string resumeProof, CancellationToken ct)
    {
        var pending = await db.PendingRegistrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new AccountFault("registration_proof_invalid", 403);
        CheckResume(pending, resumeProof);
        return await DescribeAsync(pending, ct);
    }

    public async Task<RegistrationStatus> ResendAsync(Guid id, string resumeProof, CancellationToken ct)
    {
        var snapshot = await ReadAsync(id, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, snapshot.NormalizedEmail, ct);
        var pending = (await AccountLocks.RegistrationAsync(db, id, ct))!;
        CheckResume(pending, resumeProof);
        if (pending.State != RegistrationState.Pending) throw new AccountFault("registration_state_invalid", 409);
        var invitation = await AccountLocks.InvitationAsync(db, pending.InvitationId, ct);
        var now = clock.GetUtcNow();
        CheckInvitation(invitation, pending.NormalizedEmail, now);
        if (pending.ExpiresAt <= now) throw new AccountFault("registration_expired", 410);
        pending.Generation++;
        await issuer.AddAsync(EmailChallengePurpose.Registration, pending.NormalizedEmail, pending.Id, null, null, pending.Generation,
            Earlier(now.AddMinutes(30), pending.ExpiresAt), ct);
        pending.LastSentAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await DescribeAsync(pending, ct);
    }

    public async Task ConfirmAsync(string proof, CancellationToken ct)
    {
        if (!proofs.TryUnprotect(proof, out var data) || data is null || data.Purpose != EmailChallengePurpose.Registration)
            throw new AccountFault("email_proof_invalid", 403);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, data.NormalizedEmail, ct);
        var pending = await AccountLocks.RegistrationAsync(db, data.SubjectId, ct);
        var now = clock.GetUtcNow();
        if (pending is null || pending.NormalizedEmail != data.NormalizedEmail || pending.RecoveryEpoch != environment.RecoveryEpoch
            || pending.State != RegistrationState.Pending || pending.Generation != data.Generation || pending.ExpiresAt <= now)
            throw new AccountFault("email_proof_invalid", 403);
        var invitation = await AccountLocks.InvitationAsync(db, pending.InvitationId, ct);
        CheckInvitation(invitation, pending.NormalizedEmail, now);
        var challenge = await db.EmailChallenges.SingleOrDefaultAsync(c => c.Id == data.ChallengeId, ct);
        now = clock.GetUtcNow();
        CheckInvitation(invitation, pending.NormalizedEmail, now);
        if (pending.ExpiresAt <= now) throw new AccountFault("email_proof_invalid", 403);
        if (challenge is null || !ChallengeIssuer.Matches(challenge, data, environment.RecoveryEpoch, now)) throw new AccountFault("email_proof_invalid", 403);
        challenge.ConsumedAt = now;
        pending.State = RegistrationState.Verified;
        pending.VerifiedAt = now;
        pending.CompletionExpiresAt = Earlier(Earlier(now.AddMinutes(10), pending.ExpiresAt), invitation!.ExpiresAt);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<RegistrationResult> CompleteAsync(Guid id, string resumeProof, Guid completionKey, string password,
        JsonElement appearance, CancellationToken ct)
    {
        var snapshot = await ReadAsync(id, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, snapshot.NormalizedEmail, ct);
        var pending = (await AccountLocks.RegistrationAsync(db, id, ct))!;
        CheckResume(pending, resumeProof);
        var now = clock.GetUtcNow();
        if (pending.State == RegistrationState.Completed)
        {
            if (pending.CompletionKey != completionKey) throw new AccountFault("registration_already_completed", 409);
            if (pending.CompletedAt!.Value.AddHours(24) <= now) throw new AccountFault("registration_result_expired", 410);
            return Result(pending);
        }
        if (completionKey == Guid.Empty) throw new AccountFault("registration_input_invalid");
        AccountInput.Password(password);
        var configuration = AccountInput.Appearance(appearance);
        var invitation = await AccountLocks.InvitationAsync(db, pending.InvitationId, ct);
        CheckInvitation(invitation, pending.NormalizedEmail, now);
        if (pending.State != RegistrationState.Verified || pending.CompletionExpiresAt is null) throw new AccountFault("email_verification_required", 409);
        if (pending.CompletionExpiresAt <= now || pending.ExpiresAt <= now) throw new AccountFault("registration_expired", 410);
        if (!await db.EmailChallenges.AnyAsync(c => c.PendingRegistrationId == id && c.Generation == pending.Generation
            && c.Purpose == EmailChallengePurpose.Registration && c.ConsumedAt != null && c.RevokedAt == null
            && c.RecoveryEpoch == environment.RecoveryEpoch, ct)) throw new AccountFault("email_verification_required", 409);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == pending.NormalizedEmail, ct)) throw new AccountFault("registration_conflict", 409);
        var user = new DangoUser
        {
            Id = Guid.NewGuid(),
            Email = pending.Email,
            UserName = pending.Email,
            EmailConfirmed = true,
            LockoutEnabled = true,
            CreatedAt = now
        };
        var identity = await users.CreateAsync(user, password);
        if (!identity.Succeeded) throw new AccountFault("registration_conflict", 409);
        await checkpoint.ReachedAsync("after_user", ct);
        var pet = new AccountPet { Id = Guid.NewGuid(), UserId = user.Id, Configuration = configuration, ConfigurationVersion = 1, CreatedAt = now };
        db.Pets.Add(pet);
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("after_pet", ct);
        var room = new PersistentRoom
        {
            Id = Guid.NewGuid(),
            OwnerId = user.Id,
            Kind = PersistentRoomKind.DesktopPet,
            Status = PersistentRoomStatus.Active,
            Name = "桌宠房间",
            Capacity = 2,
            TemplateId = "desktop-pet-default",
            TemplateVersion = 1,
            Version = 1,
            CreatedAt = now
        };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("after_room", ct);
        db.RoomMemberships.Add(new PersistentRoomMembership { RoomId = room.Id, UserId = user.Id, Role = PersistentRoomRole.Owner });
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("after_membership", ct);
        db.PetRoomStates.Add(new PetRoomState { PetId = pet.Id, RoomId = room.Id, PositionX = 0, PositionY = 0, Version = 1 });
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("after_assignment", ct);
        now = clock.GetUtcNow();
        if (pending.CompletionExpiresAt <= now || pending.ExpiresAt <= now || invitation!.ExpiresAt <= now)
            throw new AccountFault("registration_expired", 410);
        invitation!.ConsumedAt = now;
        invitation.UserId = user.Id;
        pending.State = RegistrationState.Completed;
        pending.CompletedAt = now;
        pending.CompletionKey = completionKey;
        pending.UserId = user.Id;
        pending.PetId = pet.Id;
        pending.RoomId = room.Id;
        await db.SaveChangesAsync(ct);
        await checkpoint.ReachedAsync("before_commit", ct);
        now = clock.GetUtcNow();
        if (pending.CompletionExpiresAt <= now || pending.ExpiresAt <= now || invitation.ExpiresAt <= now)
            throw new AccountFault("registration_expired", 410);
        await transaction.CommitAsync(ct);
        return Result(pending);
    }

    private async Task<PendingRegistration> ReadAsync(Guid id, CancellationToken ct) =>
        await db.PendingRegistrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new AccountFault("registration_proof_invalid", 403);

    private void CheckResume(PendingRegistration pending, string proof)
    {
        if (pending.RecoveryEpoch != environment.RecoveryEpoch || !AccountInput.Matches(pending.ResumeDigest, proof))
            throw new AccountFault("registration_proof_invalid", 403);
    }

    private void CheckInvitation(TestInvitation? invitation, string normalized, DateTimeOffset now)
    {
        if (invitation is null || invitation.NormalizedEmail != normalized || invitation.RecoveryEpoch != environment.RecoveryEpoch
            || invitation.RevokedAt is not null || invitation.ConsumedAt is not null || invitation.ExpiresAt <= now)
            throw new AccountFault("invitation_invalid", 403);
    }

    private async Task<RegistrationStatus> DescribeAsync(PendingRegistration pending, CancellationToken ct)
    {
        var state = await (from job in db.EmailJobs.AsNoTracking()
                           join challenge in db.EmailChallenges on job.ChallengeId equals challenge.Id
                           where challenge.PendingRegistrationId == pending.Id && challenge.Generation == pending.Generation
                           select new { job.State, job.FailureCode }).SingleOrDefaultAsync(ct);
        var delivery = state?.State switch
        {
            EmailJobState.Accepted => "smtp_accepted",
            EmailJobState.Failed => state.FailureCode ?? "send_failed",
            EmailJobState.Cancelled => "cancelled",
            _ => state?.FailureCode ?? "queued"
        };
        return new(pending.Id, pending.State, pending.ExpiresAt, pending.CompletionExpiresAt, delivery);
    }

    private static DateTimeOffset Earlier(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
    private static RegistrationResult Result(PendingRegistration pending) => new(pending.UserId!.Value, pending.PetId!.Value, pending.RoomId!.Value);
}
