using Dango.AccessoryAssets;
using Dango.Protocol.Accessories;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Accessories;

public sealed record AccessoryCommitStatus(string Status, StoredAccessoryOutfit? StoredOutfit);

public sealed class AccessoryAccountService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
    AccessoryStorage storage, AccessoryUploadGate uploadGate, IAccessoryCheckpoint checkpoint)
{
    public const int MaximumStagingAssets = 16;
    public static readonly TimeSpan UploadLease = TimeSpan.FromHours(24);

    public async Task AuthenticateAsync(Guid userId, Guid sessionId, CancellationToken ct)
    { _ = await AuthorizedPetAsync(userId, sessionId, ct); }

    public async Task<UploadedAccessoryAsset> UploadAsync(Guid userId, Guid sessionId, Stream source, string sourceDigest, CancellationToken ct)
    {
        await AuthenticateAsync(userId, sessionId, ct);
        var store = storage.RequireStore();
        if (!AccessoryOutfitItem.IsContentHash(sourceDigest)) throw new AccountFault("accessory_source_digest_invalid");
        if (!await uploadGate.Semaphore.WaitAsync(TimeSpan.FromSeconds(15), ct)) throw new AccountFault("accessory_upload_busy", 429);
        try
        {
            // Normalize before publishing a file. A quota rejection must not leave a new global blob.
            var bytes = await AccessoryEndpointInput.ReadLimitedAsync(source, AccessoryPngNormalizer.MaximumFileBytes, ct);
            CanonicalAccessoryImage normalized;
            try
            {
                if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != sourceDigest)
                    throw new InvalidDataException();
                normalized = AccessoryPngNormalizer.Normalize(bytes);
            }
            catch (InvalidDataException) { throw new AccountFault("accessory_image_invalid"); }
            await checkpoint.ReachedAsync("before_locks", ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var context = await LockAsync(userId, sessionId, ct);
            var now = clock.GetUtcNow();
            var reference = await db.AccessoryAssetReferences.SingleOrDefaultAsync(r => r.PetId == context.Pet.Id && r.AssetId == normalized.Digest, ct);
            if (reference is not null) await db.Entry(reference).ReloadAsync(ct);
            if (reference is null || !reference.IsCurrent && reference.LeaseUntil <= now)
            {
                var staged = await db.AccessoryAssetReferences.AsNoTracking()
                    .Where(r => r.PetId == context.Pet.Id && !r.IsCurrent && r.LeaseUntil > now)
                    .Take(MaximumStagingAssets + 1).CountAsync(ct);
                if (staged >= MaximumStagingAssets) throw new AccountFault("accessory_staging_limit", 409);
            }
            using var verifiedSource = new MemoryStream(bytes, writable: false);
            var imported = await store.ImportAsync(verifiedSource, sourceDigest, ct);
            if (imported.BlobDigest != normalized.Digest || imported.Width != normalized.Width || imported.Height != normalized.Height
                || imported.ByteLength != normalized.ByteLength || imported.NormalizationVersion != normalized.NormalizationVersion)
                throw AccessoryPersistence.InvalidState();
            await checkpoint.ReachedAsync("after_blob", ct);
            // Different accounts can import one global digest concurrently. Conflict does not grant a reference.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dango.accessory_blobs (asset_id, pixel_width, pixel_height, normalization_version, byte_length, created_at)
                VALUES ({imported.BlobDigest}, {imported.Width}, {imported.Height}, {imported.NormalizationVersion}, {(long)imported.ByteLength}, {now})
                ON CONFLICT (asset_id) DO NOTHING
                """, ct);
            var blob = await db.AccessoryBlobs.AsNoTracking().SingleAsync(b => b.AssetId == imported.BlobDigest, ct);
            RequireBlob(blob, normalized);
            if (reference is null)
            {
                reference = new AccessoryAssetReference { PetId = context.Pet.Id, AssetId = imported.BlobDigest };
                db.AccessoryAssetReferences.Add(reference);
            }
            reference.LeaseUntil = now.Add(UploadLease);
            await db.SaveChangesAsync(ct);
            await checkpoint.ReachedAsync("after_reference", ct);
            await BeforeCommitAsync(userId, sessionId, ct);
            await transaction.CommitAsync(ct);
            return new(imported.SourceDigest, imported.BlobDigest, imported.Width, imported.Height, imported.NormalizationVersion);
        }
        finally { uploadGate.Semaphore.Release(); }
    }

    public async Task<StoredAccessoryOutfit?> GetAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await AuthenticateAsync(userId, sessionId, ct);
        _ = storage.RequireStore();
        // Current recipe and its grants change together. Serialize this bounded read with those writes.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var context = await LockAsync(userId, sessionId, ct);
        var current = await db.AccessoryCurrentOutfits.AsNoTracking().SingleOrDefaultAsync(o => o.PetId == context.Pet.Id, ct);
        var result = current is null ? null : Describe(current);
        await checkpoint.ReachedAsync("after_read", ct);
        await ValidateCurrentReferencesAsync(context.Pet.Id, result?.Outfit ?? AccessoryOutfitManifest.Empty, ct);
        await BeforeCommitAsync(userId, sessionId, ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<StoredAccessoryOutfit> CommitAsync(Guid userId, Guid sessionId, CommitAccessoryOutfitRequest request, CancellationToken ct)
    {
        await AuthenticateAsync(userId, sessionId, ct);
        var store = storage.RequireStore();
        try { request.Validate(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { throw new AccountFault("accessory_request_invalid"); }
        var requestDigest = AccessoryPersistence.Digest(request);
        await checkpoint.ReachedAsync("before_locks", ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var context = await LockAsync(userId, sessionId, ct);
        // Durable replay precedes CAS and lease checks: an already committed request keeps its original result.
        var receipt = await db.AccessoryCommitReceipts.AsNoTracking()
            .SingleOrDefaultAsync(r => r.PetId == context.Pet.Id && r.IdempotencyKey == request.IdempotencyKey, ct);
        if (receipt is not null)
        {
            if (receipt.RequestDigest != requestDigest) throw new AccountFault("accessory_idempotency_conflict", 409);
            var replay = Describe(receipt);
            await BeforeCommitAsync(userId, sessionId, ct);
            await transaction.CommitAsync(ct);
            return replay;
        }
        var current = await db.AccessoryCurrentOutfits.SingleOrDefaultAsync(o => o.PetId == context.Pet.Id, ct);
        if (current is not null) await db.Entry(current).ReloadAsync(ct);
        var previous = current is null ? null : Describe(current);
        await ValidateCurrentReferencesAsync(context.Pet.Id, previous?.Outfit ?? AccessoryOutfitManifest.Empty, ct);
        var revision = current?.Revision ?? 0;
        if (request.ExpectedRevision != revision) throw new AccountFault("accessory_revision_conflict", 409);
        if (revision == long.MaxValue) throw new AccountFault("accessory_revision_exhausted", 409);
        var now = clock.GetUtcNow();
        var requested = CustomItems(request.Outfit).ToDictionary(i => i.AssetId, StringComparer.Ordinal);
        var originalGrants = new List<(bool WasCurrent, DateTimeOffset LeaseUntil)>();
        foreach (var item in requested.Values)
        {
            var grant = await db.AccessoryAssetReferences.AsNoTracking()
                .SingleOrDefaultAsync(r => r.PetId == context.Pet.Id && r.AssetId == item.AssetId && (r.IsCurrent || r.LeaseUntil > now), ct);
            if (grant is null) throw new AccountFault("accessory_asset_unavailable", 404);
            originalGrants.Add((grant.IsCurrent, grant.LeaseUntil));
            var blob = await db.AccessoryBlobs.AsNoTracking().SingleOrDefaultAsync(b => b.AssetId == item.AssetId, ct)
                ?? throw AccessoryPersistence.InvalidState();
            if (blob.PixelWidth != item.PixelWidth || blob.PixelHeight != item.PixelHeight) throw new AccountFault("accessory_asset_dimensions_invalid");
            CanonicalAccessoryImage image;
            try { image = await store.ReadAsync(item.AssetId, ct); }
            catch (Exception error) when (error is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
            { throw AccessoryPersistence.InvalidState(); }
            RequireBlob(blob, image);
        }
        var currentReferences = await db.AccessoryAssetReferences.Where(r => r.PetId == context.Pet.Id && r.IsCurrent)
            .Take(AccessoryOutfitManifest.MaximumEquippedItems + 1).ToListAsync(ct);
        if (currentReferences.Count > AccessoryOutfitManifest.MaximumEquippedItems) throw AccessoryPersistence.InvalidState();
        foreach (var reference in currentReferences)
        {
            if (requested.ContainsKey(reference.AssetId)) continue;
            reference.IsCurrent = false;
            // Past outfits do not gain permanent private access or overflow the staging allowance.
            if (reference.LeaseUntil > now) reference.LeaseUntil = now;
        }
        foreach (var assetId in requested.Keys)
        {
            var reference = await db.AccessoryAssetReferences.SingleAsync(r => r.PetId == context.Pet.Id && r.AssetId == assetId, ct);
            reference.IsCurrent = true;
        }
        var outfitJson = AccessoryPersistence.Serialize(request.Outfit);
        if (current is null)
        {
            current = new AccessoryCurrentOutfit { PetId = context.Pet.Id };
            db.AccessoryCurrentOutfits.Add(current);
        }
        current.Revision = revision + 1;
        current.OutfitJson = outfitJson;
        current.UpdatedAt = now;
        db.AccessoryCommitReceipts.Add(new AccessoryCommitReceipt { PetId = context.Pet.Id, IdempotencyKey = request.IdempotencyKey,
            RequestDigest = requestDigest, Revision = current.Revision, OutfitJson = outfitJson, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new AccountFault("accessory_revision_conflict", 409); }
        await checkpoint.ReachedAsync("after_outfit_and_receipt", ct);
        var result = Describe(current);
        await BeforeCommitAsync(userId, sessionId, ct);
        if (originalGrants.Any(grant => !grant.WasCurrent && grant.LeaseUntil <= clock.GetUtcNow()))
            throw new AccountFault("accessory_asset_unavailable", 404);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<AccessoryCommitStatus> ResolveAsync(Guid userId, Guid sessionId, string key, CancellationToken ct)
    {
        var pet = await AuthorizedPetAsync(userId, sessionId, ct);
        _ = storage.RequireStore();
        AccessoryPersistence.RequireKey(key);
        var receipt = await db.AccessoryCommitReceipts.AsNoTracking().SingleOrDefaultAsync(r => r.PetId == pet.Id && r.IdempotencyKey == key, ct);
        var result = receipt is null ? new AccessoryCommitStatus("unknown", null) : new("committed", Describe(receipt));
        await AuthenticateAsync(userId, sessionId, ct);
        return result;
    }

    public async Task<byte[]> DownloadAsync(Guid userId, Guid sessionId, string assetId, CancellationToken ct)
    {
        var pet = await AuthorizedPetAsync(userId, sessionId, ct);
        var store = storage.RequireStore();
        if (!AccessoryOutfitItem.IsContentHash(assetId)) throw new AccountFault("accessory_request_invalid");
        var blob = await AuthorizedBlobAsync(pet.Id, assetId, ct);
        CanonicalAccessoryImage image;
        try { image = await store.ReadAsync(assetId, ct); }
        catch (Exception error) when (error is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
        { throw AccessoryPersistence.InvalidState(); }
        RequireBlob(blob, image);
        await checkpoint.ReachedAsync("after_download", ct);
        await AuthenticateAsync(userId, sessionId, ct);
        _ = await AuthorizedBlobAsync(pet.Id, assetId, ct);
        return image.CopyPngBytes();
    }

    private async Task<AccessoryBlob> AuthorizedBlobAsync(Guid petId, string assetId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await (from reference in db.AccessoryAssetReferences.AsNoTracking()
                      where reference.PetId == petId && reference.AssetId == assetId && (reference.IsCurrent || reference.LeaseUntil > now)
                      join blob in db.AccessoryBlobs.AsNoTracking() on reference.AssetId equals blob.AssetId
                      select blob).SingleOrDefaultAsync(ct) ?? throw new AccountFault("accessory_asset_unavailable", 404);
    }

    private async Task ValidateCurrentReferencesAsync(Guid petId, AccessoryOutfitManifest outfit, CancellationToken ct)
    {
        var items = CustomItems(outfit).ToDictionary(i => i.AssetId, StringComparer.Ordinal);
        var references = await db.AccessoryAssetReferences.AsNoTracking().Where(r => r.PetId == petId && r.IsCurrent)
            .Take(AccessoryOutfitManifest.MaximumEquippedItems + 1).ToListAsync(ct);
        if (references.Count != items.Count || references.Any(r => !items.ContainsKey(r.AssetId))) throw AccessoryPersistence.InvalidState();
        foreach (var item in items.Values)
        {
            var blob = await db.AccessoryBlobs.AsNoTracking().SingleOrDefaultAsync(b => b.AssetId == item.AssetId, ct);
            if (blob is null || blob.PixelWidth != item.PixelWidth || blob.PixelHeight != item.PixelHeight
                || blob.NormalizationVersion != UploadedAccessoryAsset.CurrentNormalizationVersion || blob.ByteLength is < 1 or > AccessoryPngNormalizer.MaximumFileBytes)
                throw AccessoryPersistence.InvalidState();
        }
    }
    private static IEnumerable<AccessoryOutfitItem> CustomItems(AccessoryOutfitManifest outfit) =>
        outfit.Items.Where(i => !AccessoryOutfitItem.IsBuiltInAsset(i.AssetId)).DistinctBy(i => i.AssetId);
    private static StoredAccessoryOutfit Describe(AccessoryCurrentOutfit current) => new(current.Revision, AccessoryPersistence.ReadOutfit(current.OutfitJson, current.Revision));
    private static StoredAccessoryOutfit Describe(AccessoryCommitReceipt receipt) =>
        AccessoryPersistence.ReadReceipt(receipt.Revision, receipt.IdempotencyKey, receipt.RequestDigest, receipt.OutfitJson);
    private static void RequireBlob(AccessoryBlob blob, CanonicalAccessoryImage image)
    {
        if (blob.AssetId != image.Digest || blob.PixelWidth != image.Width || blob.PixelHeight != image.Height
            || blob.NormalizationVersion != image.NormalizationVersion || blob.ByteLength != image.ByteLength) throw AccessoryPersistence.InvalidState();
    }
    private async Task<AccountPet> AuthorizedPetAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var snapshot = await (from session in db.AuthSessions.AsNoTracking()
                              join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                              where user.Id == userId && session.Id == sessionId
                              join pet in db.Pets.AsNoTracking() on user.Id equals pet.UserId into pets
                              from pet in pets.DefaultIfEmpty()
                              select new { User = user, Session = session, Pet = pet }).SingleOrDefaultAsync(ct);
        if (snapshot is null) throw AuthenticationRequired();
        RequireSession(snapshot.User, snapshot.Session);
        if (snapshot.Pet is null || snapshot.Pet.UserId != userId) throw AccessoryPersistence.InvalidState();
        return snapshot.Pet;
    }
    private async Task<(DangoUser User, AuthSession Session, AccountPet Pet)> LockAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var normalizedEmail = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.NormalizedEmail)
            .SingleOrDefaultAsync(ct) ?? throw AuthenticationRequired();
        await AccountLocks.EmailAsync(db, normalizedEmail, ct);
        var user = await AccountLocks.UserAsync(db, userId, ct) ?? throw AuthenticationRequired();
        await db.Entry(user).ReloadAsync(ct);
        if (user.NormalizedEmail != normalizedEmail) throw AuthenticationRequired();
        var session = await db.AuthSessions.FromSqlInterpolated($"SELECT * FROM dango.auth_sessions WHERE id = {sessionId} AND user_id = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw AuthenticationRequired();
        await db.Entry(session).ReloadAsync(ct);
        RequireSession(user, session);
        var pet = await db.Pets.FromSqlInterpolated($"SELECT * FROM dango.pets WHERE user_id = {userId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw AccessoryPersistence.InvalidState();
        await db.Entry(pet).ReloadAsync(ct);
        if (pet.UserId != userId) throw AccessoryPersistence.InvalidState();
        await checkpoint.ReachedAsync("after_locks", ct);
        RequireSession(user, session);
        return (user, session, pet);
    }
    private async Task BeforeCommitAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        await checkpoint.ReachedAsync("before_commit", ct);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var session = await db.AuthSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        RequireSession(user, session);
    }
    private void RequireSession(DangoUser user, AuthSession session)
    {
        if (!AccountSessionPolicy.IsValid(user, session, environment.RecoveryEpoch, clock.GetUtcNow())) throw AuthenticationRequired();
    }
    private static AccountFault AuthenticationRequired() => new("authentication_required", 401);
}
