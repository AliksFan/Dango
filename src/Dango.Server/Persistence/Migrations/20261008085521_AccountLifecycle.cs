using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dango.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "disabled_at",
                schema: "dango",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "recovery_locked",
                schema: "dango",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "auth_sessions",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recovery_epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    remembered = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_auth_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_lifetime", "expires_at > created_at AND expires_at <= created_at + INTERVAL '168 hours' AND (remembered OR expires_at <= created_at + INTERVAL '8 hours')");
                    table.ForeignKey(
                        name: "FK_auth_sessions_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "test_invitations",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    secret_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recovery_epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_invitations", x => x.id);
                    table.CheckConstraint("ck_invitations_consumption", "(consumed_at IS NULL AND user_id IS NULL) OR (consumed_at IS NOT NULL AND user_id IS NOT NULL)");
                    table.CheckConstraint("ck_invitations_digest", "length(secret_digest) = 64");
                    table.CheckConstraint("ck_invitations_lifetime", "expires_at > created_at");
                    table.ForeignKey(
                        name: "FK_test_invitations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pending_registrations",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_key = table.Column<Guid>(type: "uuid", nullable: false),
                    resume_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    recovery_epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completion_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completion_key = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    room_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_registrations", x => x.id);
                    table.CheckConstraint("ck_registration_lifetime", "expires_at > created_at AND expires_at <= created_at + INTERVAL '1 hour' AND generation >= 1 AND length(resume_digest) = 64");
                    table.CheckConstraint("ck_registration_state", "(state = 0 AND verified_at IS NULL AND completion_expires_at IS NULL AND completed_at IS NULL AND completion_key IS NULL AND user_id IS NULL AND pet_id IS NULL AND room_id IS NULL) OR (state = 1 AND verified_at IS NOT NULL AND completion_expires_at IS NOT NULL AND completion_expires_at <= expires_at AND completion_expires_at <= verified_at + INTERVAL '10 minutes' AND completed_at IS NULL AND completion_key IS NULL AND user_id IS NULL AND pet_id IS NULL AND room_id IS NULL) OR (state = 2 AND verified_at IS NOT NULL AND completion_expires_at IS NOT NULL AND completed_at IS NOT NULL AND completion_key IS NOT NULL AND user_id IS NOT NULL AND pet_id IS NOT NULL AND room_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_pending_registrations_pets_pet_id",
                        column: x => x.pet_id,
                        principalSchema: "dango",
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pending_registrations_rooms_room_id",
                        column: x => x.room_id,
                        principalSchema: "dango",
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pending_registrations_test_invitations_invitation_id",
                        column: x => x.invitation_id,
                        principalSchema: "dango",
                        principalTable: "test_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pending_registrations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_challenges",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    pending_registration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    security_stamp = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    recovery_epoch = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_challenges", x => x.id);
                    table.CheckConstraint("ck_challenges_lifetime", "generation >= 1 AND expires_at > created_at AND expires_at <= created_at + INTERVAL '30 minutes'");
                    table.CheckConstraint("ck_challenges_subject", "(purpose = 0 AND pending_registration_id IS NOT NULL AND user_id IS NULL AND security_stamp IS NULL) OR (purpose = 1 AND pending_registration_id IS NULL AND user_id IS NOT NULL AND security_stamp IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_email_challenges_pending_registrations_pending_registration~",
                        column: x => x.pending_registration_id,
                        principalSchema: "dango",
                        principalTable: "pending_registrations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_email_challenges_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "dango",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_jobs",
                schema: "dango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_jobs", x => x.id);
                    table.CheckConstraint("ck_email_jobs_state", "state IN (0, 1, 2, 3, 4) AND attempts BETWEEN 0 AND 5 AND ((state = 1 AND lease_id IS NOT NULL AND lease_until IS NOT NULL) OR (state <> 1 AND lease_id IS NULL AND lease_until IS NULL)) AND ((state = 2 AND accepted_at IS NOT NULL) OR (state <> 2 AND accepted_at IS NULL))");
                    table.ForeignKey(
                        name: "FK_email_jobs_email_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalSchema: "dango",
                        principalTable: "email_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_expires_at",
                schema: "dango",
                table: "auth_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_auth_sessions_user_id_created_at_id",
                schema: "dango",
                table: "auth_sessions",
                columns: new[] { "user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_email_challenges_normalized_email_created_at",
                schema: "dango",
                table: "email_challenges",
                columns: new[] { "normalized_email", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_email_challenges_pending_registration_id_generation",
                schema: "dango",
                table: "email_challenges",
                columns: new[] { "pending_registration_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_challenges_user_id",
                schema: "dango",
                table: "email_challenges",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_email_jobs_challenge_id",
                schema: "dango",
                table: "email_jobs",
                column: "challenge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_jobs_state_due_at_id",
                schema: "dango",
                table: "email_jobs",
                columns: new[] { "state", "due_at", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_pending_registrations_invitation_id",
                schema: "dango",
                table: "pending_registrations",
                column: "invitation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_registrations_pet_id",
                schema: "dango",
                table: "pending_registrations",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_registrations_request_key",
                schema: "dango",
                table: "pending_registrations",
                column: "request_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_registrations_room_id",
                schema: "dango",
                table: "pending_registrations",
                column: "room_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_registrations_user_id",
                schema: "dango",
                table: "pending_registrations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_test_invitations_normalized_email_expires_at",
                schema: "dango",
                table: "test_invitations",
                columns: new[] { "normalized_email", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "IX_test_invitations_secret_digest",
                schema: "dango",
                table: "test_invitations",
                column: "secret_digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_test_invitations_user_id",
                schema: "dango",
                table: "test_invitations",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auth_sessions",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "email_jobs",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "email_challenges",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "pending_registrations",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "test_invitations",
                schema: "dango");

            migrationBuilder.DropColumn(
                name: "disabled_at",
                schema: "dango",
                table: "users");

            migrationBuilder.DropColumn(
                name: "recovery_locked",
                schema: "dango",
                table: "users");
        }
    }
}
