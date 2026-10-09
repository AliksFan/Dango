using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dango.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_room_outbox_event",
                schema: "dango",
                table: "domain_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_audit_operation",
                schema: "dango",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_audit_request",
                schema: "dango",
                table: "audit_logs");

            migrationBuilder.AddColumn<long>(
                name: "admission_generation",
                schema: "dango",
                table: "rooms",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<Guid>(
                name: "control_lease_id",
                schema: "dango",
                table: "pet_room_states",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid[]>(
                name: "target_user_ids",
                schema: "dango",
                table: "domain_outbox",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "bound_session_id",
                schema: "dango",
                table: "audit_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_json",
                schema: "dango",
                table: "audit_logs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "public_room_credentials",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<long>(type: "bigint", nullable: false),
                    code_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    protected_code = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_room_credentials", x => x.id);
                    table.CheckConstraint("ck_public_code", "generation >= 1 AND code_digest ~ '^[A-F0-9]{64}$' AND length(protected_code) BETWEEN 1 AND 2048");
                    table.CheckConstraint("ck_public_code_lifetime", "expires_at > created_at AND expires_at <= created_at + INTERVAL '168 hours'");
                    table.ForeignKey(
                        name: "FK_public_room_credentials_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "dango",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_bans",
                schema: "dango",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_bans", x => new { x.room_id, x.user_id });
                    table.CheckConstraint("ck_room_ban_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_room_bans_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "dango",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_room_bans_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_device_leases",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    transport_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_device_leases", x => x.id);
                    table.CheckConstraint("ck_leases_binding", "revoked_at IS NULL OR (room_id IS NULL AND transport_id IS NULL)");
                    table.CheckConstraint("ck_leases_device", "device_kind IN ('desktop', 'mobile')");
                    table.CheckConstraint("ck_leases_lifetime", "created_at <= last_seen_at AND expires_at > last_seen_at AND expires_at <= last_seen_at + INTERVAL '30 seconds'");
                    table.ForeignKey(
                        name: "FK_room_device_leases_auth_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "dango",
                        principalTable: "auth_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_room_device_leases_pets_pet_id",
                        column: x => x.pet_id,
                        principalSchema: "dango",
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_room_device_leases_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "dango",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_room_device_leases_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_rooms_admission_generation",
                schema: "dango",
                table: "rooms",
                sql: "admission_generation >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_pet_room_states_control_lease_id",
                schema: "dango",
                table: "pet_room_states",
                column: "control_lease_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_outbox_event",
                schema: "dango",
                table: "domain_outbox",
                sql: "event_type IN ('created', 'updated', 'dissolved', 'restored', 'expired', 'entered', 'left', 'control', 'lease_ended', 'code_reset', 'ban_set', 'ban_removed', 'removed', 'offline')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_outbox_targets",
                schema: "dango",
                table: "domain_outbox",
                sql: "cardinality(target_user_ids) BETWEEN 0 AND 32 AND NOT ('00000000-0000-0000-0000-000000000000'::uuid = ANY(target_user_ids))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_audit_operation",
                schema: "dango",
                table: "audit_logs",
                sql: "operation IN ('created', 'updated', 'dissolved', 'restored', 'expired', 'entered', 'left', 'control', 'lease_ended', 'code_reset', 'ban_set', 'ban_removed', 'removed', 'offline')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_audit_request",
                schema: "dango",
                table: "audit_logs",
                sql: "(operation IN ('expired', 'offline') AND actor_user_id IS NULL AND request_id IS NULL AND request_hash IS NULL) OR (operation NOT IN ('expired', 'offline') AND actor_user_id IS NOT NULL AND request_id IS NOT NULL AND request_hash IS NOT NULL AND request_hash ~ '^[A-F0-9]{64}$')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_audit_result",
                schema: "dango",
                table: "audit_logs",
                sql: "result_json IS NULL OR (jsonb_typeof(result_json) = 'object' AND octet_length(result_json::text) <= 8192)");

            migrationBuilder.CreateIndex(
                name: "ux_public_code_active",
                schema: "dango",
                table: "public_room_credentials",
                column: "room_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_public_code_digest",
                schema: "dango",
                table: "public_room_credentials",
                column: "code_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_room_bans_user_id",
                schema: "dango",
                table: "room_bans",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_leases_due",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "expires_at", "id" },
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_leases_pet",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "pet_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_room_device_leases_room_id",
                schema: "dango",
                table: "room_device_leases",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "IX_room_device_leases_session_id",
                schema: "dango",
                table: "room_device_leases",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ux_leases_instance",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "user_id", "session_id", "instance_id" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_pet_room_states_room_device_leases_control_lease_id",
                schema: "dango",
                table: "pet_room_states",
                column: "control_lease_id",
                principalSchema: "dango",
                principalTable: "room_device_leases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pet_room_states_room_device_leases_control_lease_id",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.DropTable(
                name: "public_room_credentials",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "room_bans",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "room_device_leases",
                schema: "dango");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rooms_admission_generation",
                schema: "dango",
                table: "rooms");

            migrationBuilder.DropIndex(
                name: "IX_pet_room_states_control_lease_id",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_outbox_event",
                schema: "dango",
                table: "domain_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_outbox_targets",
                schema: "dango",
                table: "domain_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_audit_operation",
                schema: "dango",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_audit_request",
                schema: "dango",
                table: "audit_logs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_room_audit_result",
                schema: "dango",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "admission_generation",
                schema: "dango",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "control_lease_id",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.DropColumn(
                name: "target_user_ids",
                schema: "dango",
                table: "domain_outbox");

            migrationBuilder.DropColumn(
                name: "bound_session_id",
                schema: "dango",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "result_json",
                schema: "dango",
                table: "audit_logs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_outbox_event",
                schema: "dango",
                table: "domain_outbox",
                sql: "event_type IN ('created', 'updated', 'dissolved', 'restored', 'expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_audit_operation",
                schema: "dango",
                table: "audit_logs",
                sql: "operation IN ('created', 'updated', 'dissolved', 'restored', 'expired')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_room_audit_request",
                schema: "dango",
                table: "audit_logs",
                sql: "(operation = 'expired' AND actor_user_id IS NULL AND request_id IS NULL AND request_hash IS NULL) OR (operation <> 'expired' AND actor_user_id IS NOT NULL AND request_id IS NOT NULL AND request_hash IS NOT NULL AND request_hash ~ '^[A-F0-9]{64}$')");
        }
    }
}
