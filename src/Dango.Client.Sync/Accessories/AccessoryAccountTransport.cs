using Dango.Protocol.Accessories;

namespace Dango.Client.Sync.Accessories;

/// <summary>Identity only; authentication stays inside the account transport and is never persisted here.</summary>
public sealed record AccessoryAccountScope(string AccountId, string PetId, long SessionGeneration)
{
    public void Validate()
    {
        if (!IsId(AccountId) || !IsId(PetId) || SessionGeneration < 1)
            throw new ArgumentException("invalid_accessory_account_scope");
    }

    private static bool IsId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public enum AccessoryTransportFailure
{
    Rejected,
    RevisionConflict,
    Unavailable
}

/// <summary>Only a definitive server rejection/conflict may classify a commit as not written.</summary>
public sealed class AccessoryAccountTransportException(AccessoryTransportFailure failure)
    : Exception("accessory_account_transport_failure")
{
    public AccessoryTransportFailure Failure { get; } = failure;
}

public enum AccessoryCommitResolutionKind
{
    Unknown,
    Committed,
    NotCommitted
}

/// <summary>NotCommitted requires a definitive receipt lookup; missing/expired receipts mean Unknown.</summary>
public sealed record AccessoryCommitResolution(AccessoryCommitResolutionKind Kind, StoredAccessoryOutfit? StoredOutfit = null);

/// <summary>
/// A formal account implementation must authorize every operation against scope, accept actual image
/// bytes before granting an account reference, and enforce expected revision plus idempotency on commit.
/// Hash knowledge alone must not grant image access. This seam does not pretend to be an HTTP endpoint.
/// Each instance binds an immutable service origin and account authorization for one attached session.
/// </summary>
public interface IAccessoryAccountTransport
{
    Uri ServiceOrigin { get; }

    Task<UploadedAccessoryAsset> UploadAssetAsync(AccessoryAccountScope scope, AccessoryImageSnapshot image,
        CancellationToken cancellationToken);

    Task<StoredAccessoryOutfit> CommitOutfitAsync(AccessoryAccountScope scope, CommitAccessoryOutfitRequest request,
        CancellationToken cancellationToken);

    Task<StoredAccessoryOutfit?> GetCurrentOutfitAsync(AccessoryAccountScope scope, CancellationToken cancellationToken);

    Task<AccessoryImageSnapshot> DownloadAssetAsync(AccessoryAccountScope scope, string assetId,
        CancellationToken cancellationToken);

    Task<AccessoryCommitResolution> ResolveCommitAsync(AccessoryAccountScope scope, string idempotencyKey,
        CancellationToken cancellationToken);
}
