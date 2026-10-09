using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dango.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessoryAccountBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accessory_blobs",
                schema: "dango",
                columns: table => new
                {
                    asset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    pixel_width = table.Column<int>(type: "integer", nullable: false),
                    pixel_height = table.Column<int>(type: "integer", nullable: false),
                    normalization_version = table.Column<int>(type: "integer", nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accessory_blobs", x => x.asset_id);
                    table.CheckConstraint("ck_accessory_blob_hash", "asset_id ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_accessory_blob_image", "pixel_width BETWEEN 1 AND 4096 AND pixel_height BETWEEN 1 AND 4096 AND pixel_width::bigint * pixel_height <= 16000000 AND byte_length BETWEEN 1 AND 33554432 AND normalization_version = 1");
                });

            migrationBuilder.CreateTable(
                name: "accessory_commit_receipts",
                schema: "dango",
                columns: table => new
                {
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    request_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    outfit_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accessory_commit_receipts", x => new { x.pet_id, x.idempotency_key });
                    table.CheckConstraint("ck_accessory_receipt_key", "idempotency_key ~ '^[0-9a-f]{32}$' AND request_digest ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_accessory_receipt_recipe", "COALESCE((jsonb_typeof(outfit_json) = 'object' AND outfit_json -> 'schemaVersion' = '1'::jsonb AND jsonb_typeof(outfit_json -> 'items') = 'array' AND jsonb_array_length(outfit_json -> 'items') <= 8 AND octet_length(outfit_json::text) <= 32768), FALSE)");
                    table.CheckConstraint("ck_accessory_receipt_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "FK_accessory_commit_receipts_pets_pet_id",
                        column: x => x.pet_id,
                        principalSchema: "dango",
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accessory_current_outfits",
                schema: "dango",
                columns: table => new
                {
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    outfit_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accessory_current_outfits", x => x.pet_id);
                    table.CheckConstraint("ck_accessory_outfit_recipe", "COALESCE((jsonb_typeof(outfit_json) = 'object' AND outfit_json -> 'schemaVersion' = '1'::jsonb AND jsonb_typeof(outfit_json -> 'items') = 'array' AND jsonb_array_length(outfit_json -> 'items') <= 8 AND octet_length(outfit_json::text) <= 32768), FALSE)");
                    table.CheckConstraint("ck_accessory_outfit_revision", "revision >= 1");
                    table.ForeignKey(
                        name: "FK_accessory_current_outfits_pets_pet_id",
                        column: x => x.pet_id,
                        principalSchema: "dango",
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accessory_asset_references",
                schema: "dango",
                columns: table => new
                {
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accessory_asset_references", x => new { x.pet_id, x.asset_id });
                    table.ForeignKey(
                        name: "FK_accessory_asset_references_accessory_blobs_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "dango",
                        principalTable: "accessory_blobs",
                        principalColumn: "asset_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_accessory_asset_references_pets_pet_id",
                        column: x => x.pet_id,
                        principalSchema: "dango",
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_accessory_references_blob_retention",
                schema: "dango",
                table: "accessory_asset_references",
                columns: new[] { "asset_id", "is_current", "lease_until" });

            migrationBuilder.CreateIndex(
                name: "ix_accessory_references_pet_staging",
                schema: "dango",
                table: "accessory_asset_references",
                columns: new[] { "pet_id", "lease_until" },
                filter: "NOT is_current");

            migrationBuilder.CreateIndex(
                name: "ix_accessory_receipts_pet_created",
                schema: "dango",
                table: "accessory_commit_receipts",
                columns: new[] { "pet_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accessory_asset_references",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "accessory_commit_receipts",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "accessory_current_outfits",
                schema: "dango");

            migrationBuilder.DropTable(
                name: "accessory_blobs",
                schema: "dango");
        }
    }
}
