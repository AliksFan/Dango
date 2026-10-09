using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Persistence;

public sealed partial class DangoDbContext
{
    public DbSet<RoomDeviceLease> RoomDeviceLeases => Set<RoomDeviceLease>();
    public DbSet<PublicRoomCredential> PublicRoomCredentials => Set<PublicRoomCredential>();
    public DbSet<RoomBan> RoomBans => Set<RoomBan>();

    private static void ConfigureRoomAccess(ModelBuilder builder)
    {
        builder.Entity<RoomDeviceLease>(entity =>
        {
            entity.ToTable("room_device_leases", table =>
            {
                table.HasCheckConstraint("ck_leases_device", "device_kind IN ('desktop', 'mobile')");
                table.HasCheckConstraint("ck_leases_lifetime", "created_at <= last_seen_at AND expires_at > last_seen_at AND expires_at <= last_seen_at + INTERVAL '30 seconds'");
                table.HasCheckConstraint("ck_leases_binding", "revoked_at IS NULL OR (room_id IS NULL AND transport_id IS NULL)");
            });
            entity.HasKey(lease => lease.Id);
            entity.Property(lease => lease.DeviceKind).HasMaxLength(8).IsRequired();
            entity.Property(lease => lease.TransportId).HasMaxLength(128);
            entity.HasIndex(lease => new { lease.UserId, lease.SessionId, lease.InstanceId }).IsUnique()
                .HasFilter("revoked_at IS NULL").HasDatabaseName("ux_leases_instance");
            entity.HasIndex(lease => new { lease.PetId, lease.Id }).HasDatabaseName("ix_leases_pet");
            entity.HasIndex(lease => new { lease.ExpiresAt, lease.Id }).HasFilter("revoked_at IS NULL")
                .HasDatabaseName("ix_leases_due");
            entity.HasAlternateKey(lease => new { lease.PetId, lease.Id });
            entity.HasOne<AccountPet>().WithMany().HasForeignKey(lease => new { lease.PetId, lease.UserId })
                .HasPrincipalKey(pet => new { pet.Id, pet.UserId });
            entity.HasOne<DangoUser>().WithMany().HasForeignKey(lease => lease.UserId);
            entity.HasOne<AuthSession>().WithMany().HasForeignKey(lease => new { lease.SessionId, lease.UserId })
                .HasPrincipalKey(session => new { session.Id, session.UserId });
            entity.HasOne<PersistentRoom>().WithMany().HasForeignKey(lease => lease.RoomId);
        });
        builder.Entity<PublicRoomCredential>(entity =>
        {
            entity.ToTable("public_room_credentials", table =>
            {
                table.HasCheckConstraint("ck_public_code", "generation >= 1 AND code_digest ~ '^[A-F0-9]{64}$' AND length(protected_code) BETWEEN 1 AND 2048");
                table.HasCheckConstraint("ck_public_code_epoch", "recovery_epoch <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint("ck_public_code_lifetime", "expires_at > created_at AND expires_at <= created_at + INTERVAL '168 hours'");
            });
            entity.HasKey(code => code.Id);
            entity.Property(code => code.CodeDigest).HasMaxLength(64).IsRequired();
            entity.Property(code => code.ProtectedCode).HasMaxLength(2048).IsRequired();
            entity.HasIndex(code => code.CodeDigest).IsUnique().HasDatabaseName("ux_public_code_digest");
            entity.HasIndex(code => code.RoomId).IsUnique().HasFilter("revoked_at IS NULL")
                .HasDatabaseName("ux_public_code_active");
            entity.HasOne<PersistentRoom>().WithMany().HasForeignKey(code => code.RoomId);
        });
        builder.Entity<RoomBan>(entity =>
        {
            entity.ToTable("room_bans", table => table.HasCheckConstraint("ck_room_ban_version", "version >= 1"));
            entity.HasKey(ban => new { ban.RoomId, ban.UserId });
            entity.HasOne<PersistentRoom>().WithMany().HasForeignKey(ban => ban.RoomId);
            entity.HasOne<DangoUser>().WithMany().HasForeignKey(ban => ban.UserId);
        });
        builder.Entity<PetRoomState>().HasOne<RoomDeviceLease>().WithMany().HasForeignKey(state => new { state.PetId, state.ControlLeaseId })
            .HasPrincipalKey(lease => new { lease.PetId, lease.Id });
        builder.Entity<PetRoomState>().ToTable("pet_room_states", table => table.HasCheckConstraint("ck_pet_room_control",
            "control_lease_id IS NULL OR (room_id IS NOT NULL AND NOT is_sleeping)"));
    }
}
