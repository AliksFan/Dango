using Dango.Server.Accounts;
using Dango.Server.Configuration;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Email;

public sealed class EmailDispatcher(IServiceScopeFactory scopes, IEmailSender sender, IEmailProofProtector proofs,
    AccountEnvironment environment, TimeProvider clock, FormalServerOptions options)
{
    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        var candidates = await FindBatchAsync(ct);
        var claimed = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var lease = await ClaimAsync(candidate, ct);
            if (lease is null) continue;
            claimed++;
            var delivery = await PrepareAsync(lease, ct);
            if (delivery is null) continue;
            EmailSendResult result;
            try { result = await sender.SendAsync(delivery, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { result = new(EmailSendStatus.TransientFailure, "smtp_transient"); }
            await CompleteAsync(lease, result, ct);
        }
        return claimed;
    }

    private async Task<List<Guid>> FindBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
        var now = clock.GetUtcNow();
        return await db.EmailJobs.AsNoTracking()
            .Where(job => (job.State == EmailJobState.Pending && job.DueAt <= now)
                || (job.State == EmailJobState.Leased && job.LeaseUntil <= now))
            .OrderBy(job => job.DueAt).ThenBy(job => job.Id).Select(job => job.Id)
            .Take(EmailDispatchPolicy.BatchSize).ToListAsync(ct);
    }

    private async Task<JobLease?> ClaimAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await db.EmailJobs.FromSqlInterpolated($"SELECT * FROM dango.email_jobs WHERE id = {id} AND ((state = 0 AND due_at <= {now}) OR (state = 1 AND lease_until <= {now})) FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
        if (job is null) return null;
        JobLease? lease = null;
        if (job.Attempts >= EmailDispatchPolicy.MaximumAttempts)
        {
            job.State = EmailJobState.Failed;
            job.LeaseId = null;
            job.LeaseUntil = null;
            job.FailureCode = "smtp_attempt_limit";
        }
        else
        {
            job.State = EmailJobState.Leased;
            job.Attempts++;
            job.LeaseId = Guid.NewGuid();
            job.LeaseUntil = now.Add(EmailDispatchPolicy.LeaseDuration);
            lease = new(job.Id, job.ChallengeId, job.LeaseId.Value);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return lease;
    }

    private async Task<EmailDelivery?> PrepareAsync(JobLease lease, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
        var initial = await db.EmailChallenges.AsNoTracking().SingleOrDefaultAsync(c => c.Id == lease.ChallengeId, ct);
        if (initial is null) return null; // Foreign keys normally make this impossible; never send an unbound job.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, initial.NormalizedEmail, ct);
        PendingRegistration? pending = null;
        TestInvitation? invitation = null;
        DangoUser? user = null;
        if (initial.PendingRegistrationId is Guid pendingId)
        {
            pending = await AccountLocks.RegistrationAsync(db, pendingId, ct);
            if (pending is not null) invitation = await AccountLocks.InvitationAsync(db, pending.InvitationId, ct);
        }
        else if (initial.UserId is Guid userId) user = await AccountLocks.UserAsync(db, userId, ct);
        var challenge = await db.EmailChallenges.FromSqlInterpolated($"SELECT * FROM dango.email_challenges WHERE id = {lease.ChallengeId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var job = await db.EmailJobs.FromSqlInterpolated($"SELECT * FROM dango.email_jobs WHERE id = {lease.JobId} FOR UPDATE").SingleOrDefaultAsync(ct);
        var now = clock.GetUtcNow();
        if (job is null || job.State != EmailJobState.Leased || job.LeaseId != lease.LeaseId || job.LeaseUntil <= now)
            return null;
        if (challenge is null || !EmailDispatchPolicy.IsEligible(challenge, pending, invitation, user, environment.RecoveryEpoch, now))
        {
            job.State = EmailJobState.Cancelled;
            job.LeaseId = null;
            job.LeaseUntil = null;
            job.FailureCode = "email_challenge_inactive";
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        }
        var value = proofs.Protect(challenge);
        var path = challenge.Purpose == EmailChallengePurpose.Registration ? "auth/email-confirm" : "auth/password-reset";
        var url = QueryHelpers.AddQueryString(new Uri(options.Endpoint, path).AbsoluteUri, "proof", value);
        var delivery = new EmailDelivery(pending?.Email ?? user!.Email!,
            challenge.Purpose == EmailChallengePurpose.Registration ? "Dango email verification" : "Dango password reset",
            $"Open the following link and explicitly confirm your request before its expiry:\n{url}");
        job.LeaseUntil = now.Add(EmailDispatchPolicy.LeaseDuration);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        // SMTP runs after all database locks have been released. A later revocation makes the proof unusable.
        return delivery;
    }

    private async Task CompleteAsync(JobLease lease, EmailSendResult result, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DangoDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var job = await db.EmailJobs.FromSqlInterpolated($"SELECT * FROM dango.email_jobs WHERE id = {lease.JobId} FOR UPDATE").SingleOrDefaultAsync(ct);
        if (job is not null && EmailDispatchPolicy.ApplyResult(job, lease.LeaseId, result, clock.GetUtcNow()))
            await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private sealed record JobLease(Guid JobId, Guid ChallengeId, Guid LeaseId);
}
