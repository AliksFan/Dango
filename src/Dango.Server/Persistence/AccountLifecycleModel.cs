using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Persistence;

public sealed partial class DangoDbContext
{
    public DbSet<TestInvitation> TestInvitations => Set<TestInvitation>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<EmailChallenge> EmailChallenges => Set<EmailChallenge>();
    public DbSet<EmailJob> EmailJobs => Set<EmailJob>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    private static void ConfigureAccounts(ModelBuilder builder)
    {
        builder.Entity<TestInvitation>(e =>
        {
            e.ToTable("test_invitations", t =>
            {
                t.HasCheckConstraint("ck_invitations_lifetime", "expires_at > created_at");
                t.HasCheckConstraint("ck_invitations_consumption", "(consumed_at IS NULL AND user_id IS NULL) OR (consumed_at IS NOT NULL AND user_id IS NOT NULL)");
                t.HasCheckConstraint("ck_invitations_digest", "length(secret_digest) = 64");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            e.Property(x => x.SecretDigest).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.SecretDigest).IsUnique();
            e.HasIndex(x => new { x.NormalizedEmail, x.ExpiresAt });
            e.HasOne<DangoUser>().WithMany().HasForeignKey(x => x.UserId);
        });
        builder.Entity<PendingRegistration>(e =>
        {
            e.ToTable("pending_registrations", t =>
            {
                t.HasCheckConstraint("ck_registration_lifetime", "expires_at > created_at AND expires_at <= created_at + INTERVAL '1 hour' AND generation >= 1 AND length(resume_digest) = 64");
                t.HasCheckConstraint("ck_registration_state", "(state = 0 AND verified_at IS NULL AND completion_expires_at IS NULL AND completed_at IS NULL AND completion_key IS NULL AND user_id IS NULL AND pet_id IS NULL AND room_id IS NULL) OR (state = 1 AND verified_at IS NOT NULL AND completion_expires_at IS NOT NULL AND completion_expires_at <= expires_at AND completion_expires_at <= verified_at + INTERVAL '10 minutes' AND completed_at IS NULL AND completion_key IS NULL AND user_id IS NULL AND pet_id IS NULL AND room_id IS NULL) OR (state = 2 AND verified_at IS NOT NULL AND completion_expires_at IS NOT NULL AND completed_at IS NOT NULL AND completion_key IS NOT NULL AND user_id IS NOT NULL AND pet_id IS NOT NULL AND room_id IS NOT NULL)");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RequestKey).IsUnique();
            e.HasIndex(x => x.InvitationId).IsUnique();
            e.Property(x => x.ResumeDigest).HasMaxLength(64).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            e.HasOne<TestInvitation>().WithMany().HasForeignKey(x => x.InvitationId);
            e.HasOne<DangoUser>().WithMany().HasForeignKey(x => x.UserId);
            e.HasOne<AccountPet>().WithMany().HasForeignKey(x => x.PetId);
            e.HasOne<PersistentRoom>().WithMany().HasForeignKey(x => x.RoomId);
        });
        builder.Entity<EmailChallenge>(e =>
        {
            e.ToTable("email_challenges", t =>
            {
                t.HasCheckConstraint("ck_challenges_subject", "(purpose = 0 AND pending_registration_id IS NOT NULL AND user_id IS NULL AND security_stamp IS NULL) OR (purpose = 1 AND pending_registration_id IS NULL AND user_id IS NOT NULL AND security_stamp IS NOT NULL)");
                t.HasCheckConstraint("ck_challenges_lifetime", "generation >= 1 AND expires_at > created_at AND expires_at <= created_at + INTERVAL '30 minutes'");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            e.Property(x => x.SecurityStamp).HasMaxLength(256);
            e.HasIndex(x => new { x.NormalizedEmail, x.CreatedAt });
            e.HasIndex(x => new { x.PendingRegistrationId, x.Generation }).IsUnique();
            e.HasOne<PendingRegistration>().WithMany().HasForeignKey(x => x.PendingRegistrationId);
            e.HasOne<DangoUser>().WithMany().HasForeignKey(x => x.UserId);
        });
        builder.Entity<EmailJob>(e =>
        {
            e.ToTable("email_jobs", t =>
            {
                t.HasCheckConstraint("ck_email_jobs_state", "state IN (0, 1, 2, 3, 4) AND attempts BETWEEN 0 AND 5 AND ((state = 1 AND lease_id IS NOT NULL AND lease_until IS NOT NULL) OR (state <> 1 AND lease_id IS NULL AND lease_until IS NULL)) AND ((state = 2 AND accepted_at IS NOT NULL) OR (state <> 2 AND accepted_at IS NULL))");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ChallengeId).IsUnique();
            e.HasIndex(x => new { x.State, x.DueAt, x.Id });
            e.Property(x => x.FailureCode).HasMaxLength(64);
            e.HasOne<EmailChallenge>().WithMany().HasForeignKey(x => x.ChallengeId);
        });
        builder.Entity<AuthSession>(e =>
        {
            e.ToTable("auth_sessions", t => t.HasCheckConstraint("ck_sessions_lifetime", "expires_at > created_at AND expires_at <= created_at + INTERVAL '168 hours' AND (remembered OR expires_at <= created_at + INTERVAL '8 hours')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.SecurityStamp).HasMaxLength(256).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CreatedAt, x.Id });
            e.HasIndex(x => x.ExpiresAt);
            e.HasOne<DangoUser>().WithMany().HasForeignKey(x => x.UserId);
        });
    }
}
