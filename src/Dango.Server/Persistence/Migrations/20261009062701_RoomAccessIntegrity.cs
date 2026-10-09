using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dango.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RoomAccessIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pet_room_states_room_device_leases_control_lease_id",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.DropForeignKey(
                name: "FK_room_device_leases_auth_sessions_session_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropForeignKey(
                name: "FK_room_device_leases_pets_pet_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropIndex(
                name: "IX_room_device_leases_session_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropIndex(
                name: "IX_pet_room_states_control_lease_id",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.AddColumn<Guid>(
                name: "recovery_epoch",
                schema: "dango",
                table: "public_room_credentials",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_room_device_leases_pet_id_id",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "pet_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_pets_id_user_id",
                schema: "dango",
                table: "pets",
                columns: new[] { "id", "user_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_auth_sessions_id_user_id",
                schema: "dango",
                table: "auth_sessions",
                columns: new[] { "id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_room_device_leases_pet_id_user_id",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "pet_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_room_device_leases_session_id_user_id",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "session_id", "user_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_public_code_epoch",
                schema: "dango",
                table: "public_room_credentials",
                sql: "recovery_epoch <> '00000000-0000-0000-0000-000000000000'::uuid");

            migrationBuilder.CreateIndex(
                name: "IX_pet_room_states_pet_id_control_lease_id",
                schema: "dango",
                table: "pet_room_states",
                columns: new[] { "pet_id", "control_lease_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pet_room_control",
                schema: "dango",
                table: "pet_room_states",
                sql: "control_lease_id IS NULL OR (room_id IS NOT NULL AND NOT is_sleeping)");

            migrationBuilder.AddForeignKey(
                name: "FK_pet_room_states_room_device_leases_pet_id_control_lease_id",
                schema: "dango",
                table: "pet_room_states",
                columns: new[] { "pet_id", "control_lease_id" },
                principalSchema: "dango",
                principalTable: "room_device_leases",
                principalColumns: new[] { "pet_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_room_device_leases_auth_sessions_session_id_user_id",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "session_id", "user_id" },
                principalSchema: "dango",
                principalTable: "auth_sessions",
                principalColumns: new[] { "id", "user_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_room_device_leases_pets_pet_id_user_id",
                schema: "dango",
                table: "room_device_leases",
                columns: new[] { "pet_id", "user_id" },
                principalSchema: "dango",
                principalTable: "pets",
                principalColumns: new[] { "id", "user_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pet_room_states_room_device_leases_pet_id_control_lease_id",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.DropForeignKey(
                name: "FK_room_device_leases_auth_sessions_session_id_user_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropForeignKey(
                name: "FK_room_device_leases_pets_pet_id_user_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_room_device_leases_pet_id_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropIndex(
                name: "IX_room_device_leases_pet_id_user_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropIndex(
                name: "IX_room_device_leases_session_id_user_id",
                schema: "dango",
                table: "room_device_leases");

            migrationBuilder.DropCheckConstraint(
                name: "ck_public_code_epoch",
                schema: "dango",
                table: "public_room_credentials");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_pets_id_user_id",
                schema: "dango",
                table: "pets");

            migrationBuilder.DropIndex(
                name: "IX_pet_room_states_pet_id_control_lease_id",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pet_room_control",
                schema: "dango",
                table: "pet_room_states");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_auth_sessions_id_user_id",
                schema: "dango",
                table: "auth_sessions");

            migrationBuilder.DropColumn(
                name: "recovery_epoch",
                schema: "dango",
                table: "public_room_credentials");

            migrationBuilder.CreateIndex(
                name: "IX_room_device_leases_session_id",
                schema: "dango",
                table: "room_device_leases",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_pet_room_states_control_lease_id",
                schema: "dango",
                table: "pet_room_states",
                column: "control_lease_id");

            migrationBuilder.AddForeignKey(
                name: "FK_pet_room_states_room_device_leases_control_lease_id",
                schema: "dango",
                table: "pet_room_states",
                column: "control_lease_id",
                principalSchema: "dango",
                principalTable: "room_device_leases",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_room_device_leases_auth_sessions_session_id",
                schema: "dango",
                table: "room_device_leases",
                column: "session_id",
                principalSchema: "dango",
                principalTable: "auth_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_room_device_leases_pets_pet_id",
                schema: "dango",
                table: "room_device_leases",
                column: "pet_id",
                principalSchema: "dango",
                principalTable: "pets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
