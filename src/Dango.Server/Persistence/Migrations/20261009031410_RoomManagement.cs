using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dango.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_version = table.Column<long>(type: "bigint", nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                    table.CheckConstraint("ck_room_audit_operation", "operation IN ('created', 'updated', 'dissolved', 'restored', 'expired')");
                    table.CheckConstraint("ck_room_audit_request", "(operation = 'expired' AND actor_user_id IS NULL AND request_id IS NULL AND request_hash IS NULL) OR (operation <> 'expired' AND actor_user_id IS NOT NULL AND request_id IS NOT NULL AND request_hash IS NOT NULL AND request_hash ~ '^[A-F0-9]{64}$')");
                    table.CheckConstraint("ck_room_audit_version", "applied_version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "domain_outbox",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    room_version = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_outbox", x => x.id);
                    table.CheckConstraint("ck_room_outbox_event", "event_type IN ('created', 'updated', 'dissolved', 'restored', 'expired')");
                    table.CheckConstraint("ck_room_outbox_version", "room_version >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ux_room_audit_request",
                schema: "dango",
                table: "audit_logs",
                columns: new[] { "actor_user_id", "request_id" },
                unique: true,
                filter: "request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_room_audit_version",
                schema: "dango",
                table: "audit_logs",
                columns: new[] { "room_id", "applied_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_room_outbox_pending",
                schema: "dango",
                table: "domain_outbox",
                columns: new[] { "occurred_at", "id" },
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_room_outbox_version",
                schema: "dango",
                table: "domain_outbox",
                columns: new[] { "room_id", "room_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "domain_outbox",
                schema: "dango");
        }
    }
}
