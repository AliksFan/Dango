using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Persistence;

public sealed partial class DangoDbContext
{
    public DbSet<AccessoryBlob> AccessoryBlobs => Set<AccessoryBlob>();
    public DbSet<AccessoryAssetReference> AccessoryAssetReferences => Set<AccessoryAssetReference>();
    public DbSet<AccessoryCurrentOutfit> AccessoryCurrentOutfits => Set<AccessoryCurrentOutfit>();
    public DbSet<AccessoryCommitReceipt> AccessoryCommitReceipts => Set<AccessoryCommitReceipt>();

    private static void ConfigureAccessories(ModelBuilder builder)
    {
        builder.Entity<AccessoryBlob>(entity =>
        {
            entity.ToTable("accessory_blobs", table =>
            {
                table.HasCheckConstraint("ck_accessory_blob_hash", "asset_id ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint("ck_accessory_blob_image", "pixel_width BETWEEN 1 AND 4096 AND pixel_height BETWEEN 1 AND 4096 AND pixel_width::bigint * pixel_height <= 16000000 AND byte_length BETWEEN 1 AND 33554432 AND normalization_version = 1");
            });
            entity.HasKey(blob => blob.AssetId);
            entity.Property(blob => blob.AssetId).HasMaxLength(64);
        });
        builder.Entity<AccessoryAssetReference>(entity =>
        {
            entity.ToTable("accessory_asset_references");
            entity.HasKey(reference => new { reference.PetId, reference.AssetId });
            entity.Property(reference => reference.AssetId).HasMaxLength(64);
            entity.HasIndex(reference => new { reference.AssetId, reference.IsCurrent, reference.LeaseUntil })
                .HasDatabaseName("ix_accessory_references_blob_retention");
            entity.HasIndex(reference => new { reference.PetId, reference.LeaseUntil })
                .HasFilter("NOT is_current").HasDatabaseName("ix_accessory_references_pet_staging");
            entity.HasOne<AccountPet>().WithMany().HasForeignKey(reference => reference.PetId);
            entity.HasOne<AccessoryBlob>().WithMany().HasForeignKey(reference => reference.AssetId);
        });
        builder.Entity<AccessoryCurrentOutfit>(entity =>
        {
            entity.ToTable("accessory_current_outfits", table =>
            {
                table.HasCheckConstraint("ck_accessory_outfit_revision", "revision >= 1");
                table.HasCheckConstraint("ck_accessory_outfit_recipe", RecipeConstraint);
            });
            entity.HasKey(outfit => outfit.PetId);
            entity.Property(outfit => outfit.Revision).IsConcurrencyToken();
            entity.Property(outfit => outfit.OutfitJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne<AccountPet>().WithOne().HasForeignKey<AccessoryCurrentOutfit>(outfit => outfit.PetId);
        });
        builder.Entity<AccessoryCommitReceipt>(entity =>
        {
            entity.ToTable("accessory_commit_receipts", table =>
            {
                table.HasCheckConstraint("ck_accessory_receipt_key", "idempotency_key ~ '^[0-9a-f]{32}$' AND request_digest ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint("ck_accessory_receipt_revision", "revision >= 1");
                table.HasCheckConstraint("ck_accessory_receipt_recipe", RecipeConstraint);
            });
            entity.HasKey(receipt => new { receipt.PetId, receipt.IdempotencyKey });
            entity.Property(receipt => receipt.IdempotencyKey).HasMaxLength(32);
            entity.Property(receipt => receipt.RequestDigest).HasMaxLength(64);
            entity.Property(receipt => receipt.OutfitJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(receipt => new { receipt.PetId, receipt.CreatedAt }).HasDatabaseName("ix_accessory_receipts_pet_created");
            entity.HasOne<AccountPet>().WithMany().HasForeignKey(receipt => receipt.PetId);
        });
    }

    // Full parameter validation stays at the centralized service boundary. These constraints
    // additionally reject non-recipe JSON, future schema, oversize storage and excess instances.
    private const string RecipeConstraint = "COALESCE((jsonb_typeof(outfit_json) = 'object' AND outfit_json -> 'schemaVersion' = '1'::jsonb AND jsonb_typeof(outfit_json -> 'items') = 'array' AND jsonb_array_length(outfit_json -> 'items') <= 8 AND octet_length(outfit_json::text) <= 32768), FALSE)";
}
