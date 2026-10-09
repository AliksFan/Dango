using Dango.AccessoryAssets;

namespace Dango.AccessoryModeration;

/// <summary>Trusted administrator primitive; formal account/administrator authorization belongs to its caller.</summary>
public sealed class AccessoryImageReviewService(FileAccessoryBlobStore blobs, FileAccessoryReviewRepository reviews)
{
    public async Task<AccessoryImageReview> RequestReviewAsync(string digest, CancellationToken cancellationToken = default)
    {
        var image = await blobs.ReadAsync(digest, cancellationToken);
        return await reviews.RequestAsync(image, cancellationToken);
    }
    public async Task<AccessoryImageReview> DecideAsync(string digest, int expectedRevision, AccessoryImageReviewDecision decision,
        string reason, CancellationToken cancellationToken = default)
    {
        if (decision == AccessoryImageReviewDecision.Approve)
        {
            // Never approve a missing/corrupt image based on a stale queue row alone.
            var image = await blobs.ReadAsync(digest, cancellationToken);
            var record = await reviews.ReadAsync(digest, cancellationToken);
            if (record is not null && (record.PixelWidth != image.Width || record.PixelHeight != image.Height || record.NormalizationVersion != image.NormalizationVersion))
                throw new InvalidDataException("Pending review metadata differs from the real image.");
        }
        return await reviews.DecideAsync(digest, expectedRevision, decision, reason, cancellationToken);
    }
    public async Task<CanonicalAccessoryImage> ReadPreviewAsync(string digest, CancellationToken cancellationToken = default)
    {
        var record = await reviews.ReadAsync(digest, cancellationToken) ??
            throw new InvalidOperationException("This private image has not been explicitly requested for display review.");
        var image = await blobs.ReadAsync(digest, cancellationToken);
        if (record.PixelWidth != image.Width || record.PixelHeight != image.Height || record.NormalizationVersion != image.NormalizationVersion)
            throw new InvalidDataException("Review metadata differs from the real image.");
        return image;
    }
}
