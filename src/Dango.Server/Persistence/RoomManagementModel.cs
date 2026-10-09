using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Persistence;

public sealed partial class DangoDbContext
{
    public DbSet<RoomOperationAudit> RoomOperationAudits => Set<RoomOperationAudit>();
    public DbSet<RoomDomainEvent> RoomDomainEvents => Set<RoomDomainEvent>();

    private static void ConfigureRoomManagement(ModelBuilder builder)
    {
        const string operations = "'created', 'updated', 'dissolved', 'restored', 'expired'";
        builder.Entity<RoomOperationAudit>(entity =>
        {
            entity.ToTable("audit_logs", table =>
            {
                table.HasCheckConstraint("ck_room_audit_version", "applied_version >= 1");
                table.HasCheckConstraint("ck_room_audit_operation", $"operation IN ({operations})");
                table.HasCheckConstraint("ck_room_audit_request", "(operation = 'expired' AND actor_user_id IS NULL AND request_id IS NULL AND request_hash IS NULL) OR (operation <> 'expired' AND actor_user_id IS NOT NULL AND request_id IS NOT NULL AND request_hash IS NOT NULL AND request_hash ~ '^[A-F0-9]{64}$')");
            });
            entity.HasKey(audit => audit.Id);
            entity.Property(audit => audit.Operation).HasMaxLength(16).IsRequired();
            entity.Property(audit => audit.RequestHash).HasMaxLength(64);
            entity.HasIndex(audit => new { audit.ActorUserId, audit.RequestId }).IsUnique()
                .HasFilter("request_id IS NOT NULL").HasDatabaseName("ux_room_audit_request");
            entity.HasIndex(audit => new { audit.RoomId, audit.AppliedVersion }).IsUnique()
                .HasDatabaseName("ux_room_audit_version");
        });
        builder.Entity<RoomDomainEvent>(entity =>
        {
            entity.ToTable("domain_outbox", table =>
            {
                table.HasCheckConstraint("ck_room_outbox_version", "room_version >= 1");
                table.HasCheckConstraint("ck_room_outbox_event", $"event_type IN ({operations})");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EventType).HasMaxLength(16).IsRequired();
            entity.HasIndex(item => new { item.RoomId, item.RoomVersion }).IsUnique().HasDatabaseName("ux_room_outbox_version");
            entity.HasIndex(item => new { item.OccurredAt, item.Id }).HasFilter("published_at IS NULL")
                .HasDatabaseName("ix_room_outbox_pending");
        });
    }
}
