using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Dango.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccountRoomFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dango");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_users_email", "length(btrim(email)) BETWEEN 3 AND 254 AND length(btrim(normalized_email)) BETWEEN 3 AND 254");
                    table.CheckConstraint("ck_users_verified", "email_confirmed");
                });

            migrationBuilder.CreateTable(
                name: "pets",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false),
                    configuration_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pets", x => x.id);
                    table.CheckConstraint("ck_pets_configuration", "jsonb_typeof(configuration) = 'object' AND configuration ? 'appearanceSchemaVersion' AND configuration -> 'appearanceSchemaVersion' = '3'::jsonb AND octet_length(configuration::text) <= 4096");
                    table.CheckConstraint("ck_pets_configuration_version", "configuration_version >= 1");
                    table.ForeignKey(
                        name: "FK_pets_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dissolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recover_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.id);
                    table.CheckConstraint("ck_rooms_capacity", "(kind = 0 AND capacity = 2) OR (kind IN (1, 2) AND capacity BETWEEN 1 AND 12)");
                    table.CheckConstraint("ck_rooms_kind", "kind IN (0, 1, 2)");
                    table.CheckConstraint("ck_rooms_lifecycle", "(status = 0 AND dissolved_at IS NULL AND recover_until IS NULL) OR (kind <> 0 AND status IN (1, 2) AND dissolved_at IS NOT NULL AND recover_until IS NOT NULL AND recover_until = dissolved_at + INTERVAL '168 hours')");
                    table.CheckConstraint("ck_rooms_name", "length(btrim(name)) BETWEEN 1 AND 64");
                    table.CheckConstraint("ck_rooms_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_rooms_users_owner_id",
                        column: x => x.owner_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "dango",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "FK_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "dango",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "FK_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pet_room_states",
                schema: "dango",
                columns: table => new
                {
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_x = table.Column<double>(type: "double precision", nullable: true),
                    position_y = table.Column<double>(type: "double precision", nullable: true),
                    is_sleeping = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    control_generation = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pet_room_states", x => x.pet_id);
                    table.CheckConstraint("ck_pet_room_presence", "(room_id IS NULL AND position_x IS NULL AND position_y IS NULL AND NOT is_sleeping) OR (room_id IS NOT NULL AND position_x IS NOT NULL AND position_y IS NOT NULL AND position_x > '-Infinity'::double precision AND position_x < 'Infinity'::double precision AND position_y > '-Infinity'::double precision AND position_y < 'Infinity'::double precision)");
                    table.CheckConstraint("ck_pet_room_versions", "version >= 1 AND control_generation >= 0");
                    table.ForeignKey(
                        name: "FK_pet_room_states_pets_pet_id",
                        column: x => x.pet_id,
                        principalSchema: "dango",
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pet_room_states_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "dango",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_memberships",
                schema: "dango",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_memberships", x => new { x.room_id, x.user_id });
                    table.CheckConstraint("ck_memberships_role", "role IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_room_memberships_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "dango",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_room_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pet_room_occupancy",
                schema: "dango",
                table: "pet_room_states",
                columns: new[] { "room_id", "pet_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pets_user_id",
                schema: "dango",
                table: "pets",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_room",
                schema: "dango",
                table: "room_memberships",
                columns: new[] { "user_id", "room_id" });

            migrationBuilder.CreateIndex(
                name: "ux_memberships_owner",
                schema: "dango",
                table: "room_memberships",
                column: "room_id",
                unique: true,
                filter: "role = 0");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_owner_status_id",
                schema: "dango",
                table: "rooms",
                columns: new[] { "owner_id", "status", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_rooms_recovery_due",
                schema: "dango",
                table: "rooms",
                columns: new[] { "recover_until", "id" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ux_rooms_desktop_owner",
                schema: "dango",
                table: "rooms",
                column: "owner_id",
                unique: true,
                filter: "kind = 0");

            migrationBuilder.CreateIndex(
                name: "IX_user_claims_user_id",
                schema: "dango",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_user_id",
                schema: "dango",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "dango",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                schema: "dango",
                table: "users",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pet_room_states",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "room_memberships",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "pets",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "rooms",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "users",
                schema: "dango");
        }
    }
}
