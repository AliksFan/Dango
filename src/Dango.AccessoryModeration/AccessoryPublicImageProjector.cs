using Dango.Protocol.Accessories;

namespace Dango.AccessoryModeration;

public sealed class AccessoryPublicImageProjector(FileAccessoryReviewRepository reviews)
{
    public async Task<AccessoryOutfitManifest> ProjectAsync(AccessoryOutfitManifest outfit,
        Func<string, bool> isAuthorizedToDisplay, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outfit); ArgumentNullException.ThrowIfNull(isAuthorizedToDisplay); outfit.Validate();
        var result = new List<AccessoryOutfitItem>(); var eligibility = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in outfit.Items)
        {
            cancellationToken.ThrowIfCancellationRequested(); var id = item.AssetId;
            if (AccessoryOutfitItem.IsBuiltInAsset(id)) { result.Add(item with { Name = "配饰" }); continue; }
            if (!AccessoryImageReview.IsDigest(id)) throw new InvalidDataException("Outfit contains an invalid custom image identity.");
            if (!eligibility.TryGetValue(id, out var eligible))
            {
                // Approval is content eligibility, not an account reference or a download/display grant.
                eligible = isAuthorizedToDisplay(id) && (await reviews.ReadAsync(id, cancellationToken))?.Status == AccessoryImageReviewStatus.Approved;
                eligibility.Add(id, eligible);
            }
            if (eligible) result.Add(item with { Name = "配饰" });
        }
        return new(outfit.SchemaVersion, result);
    }
}
