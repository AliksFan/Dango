using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dango.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nickname",
                schema: "dango",
                table: "users",
                type: "character varying(192)",
                maxLength: 192,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "profile_version",
                schema: "dango",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_nickname",
                schema: "dango",
                table: "users",
                sql: "nickname IS NULL OR (length(btrim(nickname)) BETWEEN 1 AND 192 AND octet_length(nickname) <= 192 AND nickname = btrim(nickname))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_profile_version",
                schema: "dango",
                table: "users",
                sql: "profile_version >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_nickname",
                schema: "dango",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_profile_version",
                schema: "dango",
                table: "users");

            migrationBuilder.DropColumn(
                name: "nickname",
                schema: "dango",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_version",
                schema: "dango",
                table: "users");
        }
    }
}
