using System.Text.RegularExpressions;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Persistence;

public sealed partial class DangoDbContext(DbContextOptions<DangoDbContext> options)
    : IdentityUserContext<DangoUser, Guid>(options)
{
    public DbSet<AccountPet> Pets => Set<AccountPet>();
    public DbSet<PersistentRoom> Rooms => Set<PersistentRoom>();
    public DbSet<PersistentRoomMembership> RoomMemberships => Set<PersistentRoomMembership>();
    public DbSet<PetRoomState> PetRoomStates => Set<PetRoomState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("dango");
        builder.Entity<DangoUser>(entity =>
        {
            entity.ToTable("users", table =>
            {
                table.HasCheckConstraint("ck_users_verified", "email_confirmed");
                table.HasCheckConstraint("ck_users_email", "length(btrim(email)) BETWEEN 3 AND 254 AND length(btrim(normalized_email)) BETWEEN 3 AND 254");
            });
            entity.Property(user => user.Email).HasMaxLength(254).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("ux_users_normalized_email");
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.Entity<AccountPet>(entity =>
        {
            entity.ToTable("pets", table =>
            {
                table.HasCheckConstraint("ck_pets_configuration", "jsonb_typeof(configuration) = 'object' AND configuration ? 'appearanceSchemaVersion' AND configuration -> 'appearanceSchemaVersion' = '3'::jsonb AND octet_length(configuration::text) <= 4096");
                table.HasCheckConstraint("ck_pets_configuration_version", "configuration_version >= 1");
            });
            entity.HasKey(pet => pet.Id);
            entity.HasIndex(pet => pet.UserId).IsUnique().HasDatabaseName("ux_pets_user_id");
            entity.Property(pet => pet.Configuration).HasColumnType("jsonb").IsRequired();
            entity.Property(pet => pet.ConfigurationVersion).IsConcurrencyToken();
            entity.HasOne<DangoUser>().WithMany().HasForeignKey(pet => pet.UserId);
        });

        builder.Entity<PersistentRoom>(entity =>
        {
            entity.ToTable("rooms", table =>
            {
                table.HasCheckConstraint("ck_rooms_kind", "kind IN (0, 1, 2)");
                table.HasCheckConstraint("ck_rooms_name", "length(btrim(name)) BETWEEN 1 AND 64");
                table.HasCheckConstraint("ck_rooms_capacity", "(kind = 0 AND capacity = 2) OR (kind IN (1, 2) AND capacity BETWEEN 1 AND 12)");
                table.HasCheckConstraint("ck_rooms_version", "version >= 1");
                table.HasCheckConstraint("ck_rooms_lifecycle", "(status = 0 AND dissolved_at IS NULL AND recover_until IS NULL) OR (kind <> 0 AND status IN (1, 2) AND dissolved_at IS NOT NULL AND recover_until IS NOT NULL AND recover_until = dissolved_at + INTERVAL '168 hours')");
            });
            entity.HasKey(room => room.Id);
            entity.Property(room => room.Name).HasMaxLength(64).IsRequired();
            entity.Property(room => room.Version).IsConcurrencyToken();
            entity.HasIndex(room => room.OwnerId).IsUnique().HasFilter("kind = 0").HasDatabaseName("ux_rooms_desktop_owner");
            entity.HasIndex(room => new { room.OwnerId, room.Status, room.Id }).HasDatabaseName("ix_rooms_owner_status_id");
            entity.HasIndex(room => new { room.RecoverUntil, room.Id }).HasFilter("status = 1").HasDatabaseName("ix_rooms_recovery_due");
            entity.HasOne<DangoUser>().WithMany().HasForeignKey(room => room.OwnerId);
        });

        builder.Entity<PersistentRoomMembership>(entity =>
        {
            entity.ToTable("room_memberships", table => table.HasCheckConstraint("ck_memberships_role", "role IN (0, 1)"));
            entity.HasKey(member => new { member.RoomId, member.UserId });
            entity.HasIndex(member => member.RoomId).IsUnique().HasFilter("role = 0").HasDatabaseName("ux_memberships_owner");
            entity.HasIndex(member => new { member.UserId, member.RoomId }).HasDatabaseName("ix_memberships_user_room");
            entity.HasOne<PersistentRoom>().WithMany().HasForeignKey(member => member.RoomId);
            entity.HasOne<DangoUser>().WithMany().HasForeignKey(member => member.UserId);
        });

        builder.Entity<PetRoomState>(entity =>
        {
            entity.ToTable("pet_room_states", table =>
            {
                table.HasCheckConstraint("ck_pet_room_versions", "version >= 1 AND control_generation >= 0");
                table.HasCheckConstraint("ck_pet_room_presence", "(room_id IS NULL AND position_x IS NULL AND position_y IS NULL AND NOT is_sleeping) OR (room_id IS NOT NULL AND position_x IS NOT NULL AND position_y IS NOT NULL AND position_x > '-Infinity'::double precision AND position_x < 'Infinity'::double precision AND position_y > '-Infinity'::double precision AND position_y < 'Infinity'::double precision)");
            });
            entity.HasKey(state => state.PetId);
            entity.Property(state => state.Version).IsConcurrencyToken();
            entity.HasIndex(state => new { state.RoomId, state.PetId }).HasDatabaseName("ix_pet_room_occupancy");
            entity.HasOne<AccountPet>().WithOne().HasForeignKey<PetRoomState>(state => state.PetId);
            entity.HasOne<PersistentRoom>().WithMany().HasForeignKey(state => state.RoomId);
        });

        ConfigureAccounts(builder);
        ConfigureRoomManagement(builder);
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties()) property.SetColumnName(SnakeCase(property.Name));
            foreach (var foreignKey in entity.GetForeignKeys()) foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    private static string SnakeCase(string name) => WordBoundary().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex WordBoundary();
}
