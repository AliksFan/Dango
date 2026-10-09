using System.Text.Json.Serialization;
using Dango.AccessoryAssets;

namespace Dango.AccessoryModeration;

public enum AccessoryImageReviewStatus { Pending, Approved, Rejected, Revoked }
public enum AccessoryImageReviewDecision { Approve, Reject, Revoke }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessoryImageReviewEvent(int Revision, AccessoryImageReviewStatus Status, DateTimeOffset AtUtc, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccessoryImageReview(int SchemaVersion, string Digest, int PixelWidth, int PixelHeight, int NormalizationVersion,
    int Revision, AccessoryImageReviewStatus Status, IReadOnlyList<AccessoryImageReviewEvent> History)
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumReasonLength = 512;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || !IsDigest(Digest) || PixelWidth is < 1 or > AccessoryPngNormalizer.MaximumEdge ||
            PixelHeight is < 1 or > AccessoryPngNormalizer.MaximumEdge || (long)PixelWidth * PixelHeight > AccessoryPngNormalizer.MaximumPixels ||
            NormalizationVersion != AccessoryPngNormalizer.CurrentNormalizationVersion || Revision is < 1 or > 3 ||
            !Enum.IsDefined(Status) || History is null || History.Count != Revision)
            throw new InvalidDataException("Invalid image review record.");
        AccessoryImageReviewStatus? previous = null; DateTimeOffset? previousTime = null;
        for (var index = 0; index < History.Count; index++)
        {
            var entry = History[index];
            if (entry is null || entry.Revision != index + 1 || !Enum.IsDefined(entry.Status) || !IsReason(entry.Reason) ||
                entry.AtUtc == default || entry.AtUtc.Offset != TimeSpan.Zero || previousTime > entry.AtUtc)
                throw new InvalidDataException("Invalid image review history.");
            if (previous is null ? entry.Status != AccessoryImageReviewStatus.Pending : !IsTransition(previous.Value, entry.Status))
                throw new InvalidDataException("Invalid image review transition.");
            previous = entry.Status; previousTime = entry.AtUtc;
        }
        if (History[^1].Status != Status) throw new InvalidDataException("Review status does not match its history.");
    }

    internal static bool IsDigest(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static bool IsReason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumReasonLength && !value.Any(char.IsControl);
    internal static bool IsTransition(AccessoryImageReviewStatus previous, AccessoryImageReviewStatus next) =>
        previous == AccessoryImageReviewStatus.Pending && next is AccessoryImageReviewStatus.Approved or AccessoryImageReviewStatus.Rejected ||
        previous == AccessoryImageReviewStatus.Approved && next == AccessoryImageReviewStatus.Revoked;
}

public sealed class AccessoryReviewConflictException(int actualRevision) : Exception("The review revision has changed.")
{
    public int ActualRevision { get; } = actualRevision;
}
