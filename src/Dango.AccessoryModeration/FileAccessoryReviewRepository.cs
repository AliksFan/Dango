using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.AccessoryAssets;

namespace Dango.AccessoryModeration;

internal enum ReviewWriteStage { AfterPartialWrite, BeforePublish }

public sealed class FileAccessoryReviewRepository
{
    private const int MaximumRecordBytes = 16 * 1024;
    private const int MaximumReviewRecords = 10_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    private readonly string root;
    private readonly Action<ReviewWriteStage>? writeFault;

    public FileAccessoryReviewRepository(string rootDirectory) : this(rootDirectory, null) { }
    internal FileAccessoryReviewRepository(string rootDirectory, Action<ReviewWriteStage>? writeFault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory)) throw new ArgumentException("Review storage requires an operator-selected absolute directory.", nameof(rootDirectory));
        root = Path.GetFullPath(rootDirectory); EnsurePrivateDirectory(); this.writeFault = writeFault;
    }

    public async Task<AccessoryImageReview?> ReadAsync(string digest, CancellationToken cancellationToken = default)
    {
        var path = GetPath(digest); cancellationToken.ThrowIfCancellationRequested(); EnsurePrivateDirectory();
        try
        {
            EnsureRegularFile(path);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (input.Length is < 1 or > MaximumRecordBytes) throw new InvalidDataException("Review record size is invalid.");
            var bytes = new byte[(int)input.Length]; await input.ReadExactlyAsync(bytes, cancellationToken);
            using var document = JsonDocument.Parse(bytes); RejectDuplicateFields(document.RootElement);
            var record = JsonSerializer.Deserialize<AccessoryImageReview>(bytes, JsonOptions) ?? throw new InvalidDataException("Empty image review record.");
            record.Validate();
            if (record.Digest != digest) throw new InvalidDataException("Review record identity differs from its filename.");
            return record with { History = Array.AsReadOnly(record.History.ToArray()) };
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
        catch (JsonException error) { throw new InvalidDataException("Review record JSON is invalid.", error); }
    }

    public async Task<IReadOnlyList<AccessoryImageReview>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsurePrivateDirectory(); var result = new List<AccessoryImageReview>();
        var paths = Directory.EnumerateFiles(root, "*.review.json", SearchOption.TopDirectoryOnly).Take(MaximumReviewRecords + 1).ToArray();
        if (paths.Length > MaximumReviewRecords) throw new InvalidDataException("Prototype review-list capacity exceeded.");
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var digest = Path.GetFileName(path)[..^".review.json".Length];
            var record = await ReadAsync(digest, cancellationToken);
            if (record is not null) result.Add(record);
        }
        return result.AsReadOnly();
    }

    internal async Task<AccessoryImageReview> RequestAsync(CanonicalAccessoryImage image, CancellationToken cancellationToken)
    {
        await using var locked = await AcquireLockAsync(image.Digest, cancellationToken);
        var existing = await ReadAsync(image.Digest, cancellationToken);
        if (existing is not null)
        {
            if (existing.PixelWidth != image.Width || existing.PixelHeight != image.Height || existing.NormalizationVersion != image.NormalizationVersion)
                throw new InvalidDataException("Image review metadata differs from its immutable blob.");
            return existing;
        }
        var history = Array.AsReadOnly(new[] { new AccessoryImageReviewEvent(1, AccessoryImageReviewStatus.Pending,
            DateTimeOffset.UtcNow, "Explicit public display review request.") });
        var pending = new AccessoryImageReview(AccessoryImageReview.CurrentSchemaVersion, image.Digest, image.Width, image.Height,
            image.NormalizationVersion, 1, AccessoryImageReviewStatus.Pending, history);
        await WriteAsync(pending, cancellationToken); return pending;
    }

    internal async Task<AccessoryImageReview> DecideAsync(string digest, int expectedRevision, AccessoryImageReviewDecision decision,
        string reason, CancellationToken cancellationToken)
    {
        if (expectedRevision < 1 || !Enum.IsDefined(decision) || !AccessoryImageReview.IsReason(reason))
            throw new ArgumentException("A valid decision, expected revision and nonempty reason of at most 512 characters are required.");
        await using var locked = await AcquireLockAsync(digest, cancellationToken);
        var existing = await ReadAsync(digest, cancellationToken) ?? throw new InvalidOperationException("Explicit display review has not been requested.");
        if (existing.Revision != expectedRevision) throw new AccessoryReviewConflictException(existing.Revision);
        var nextStatus = decision switch
        {
            AccessoryImageReviewDecision.Approve => AccessoryImageReviewStatus.Approved,
            AccessoryImageReviewDecision.Reject => AccessoryImageReviewStatus.Rejected,
            AccessoryImageReviewDecision.Revoke => AccessoryImageReviewStatus.Revoked,
            _ => throw new ArgumentException("Unknown review decision.", nameof(decision))
        };
        if (!AccessoryImageReview.IsTransition(existing.Status, nextStatus)) throw new InvalidOperationException("Review decision does not apply to the current state.");
        var atUtc = DateTimeOffset.UtcNow;
        if (atUtc < existing.History[^1].AtUtc) atUtc = existing.History[^1].AtUtc;
        var updated = existing with
        {
            Revision = existing.Revision + 1,
            Status = nextStatus,
            History = Array.AsReadOnly(existing.History.Append(new(existing.Revision + 1, nextStatus, atUtc, reason)).ToArray())
        };
        await WriteAsync(updated, cancellationToken); return updated;
    }

    private async Task<FileStream> AcquireLockAsync(string digest, CancellationToken cancellationToken)
    {
        _ = GetPath(digest); EnsurePrivateDirectory(); var path = Path.Combine(root, digest + ".lock");
        var watch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureRegularFileIfPresent(path);
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 or 11 && watch.Elapsed < TimeSpan.FromSeconds(5))
            { await Task.Delay(10, cancellationToken); }
        }
    }

    private async Task WriteAsync(AccessoryImageReview record, CancellationToken cancellationToken)
    {
        record.Validate(); var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
        if (bytes.Length > MaximumRecordBytes) throw new InvalidDataException("Review record exceeds its storage limit.");
        var path = GetPath(record.Digest); var temporary = Path.Combine(root, ".review-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var half = bytes.Length / 2; await output.WriteAsync(bytes.AsMemory(0, half), cancellationToken);
                writeFault?.Invoke(ReviewWriteStage.AfterPartialWrite);
                await output.WriteAsync(bytes.AsMemory(half), cancellationToken); await output.FlushAsync(cancellationToken); output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested(); writeFault?.Invoke(ReviewWriteStage.BeforePublish);
            EnsurePrivateDirectory(); EnsureRegularFileIfPresent(path);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string GetPath(string digest)
    {
        if (!AccessoryImageReview.IsDigest(digest)) throw new ArgumentException("Image identity must be a canonical lowercase SHA-256 digest.", nameof(digest));
        return Path.Combine(root, digest + ".review.json");
    }

    private void EnsurePrivateDirectory()
    {
        for (var path = root; path is not null; path = Path.GetDirectoryName(path))
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Private review storage cannot contain linked directories.");
            }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
        }
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Private review storage cannot be linked.");
    }
    private static void EnsureRegularFile(string path)
    {
        if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Review records and locks must be ordinary files.");
    }
    private static void EnsureRegularFileIfPresent(string path)
    {
        try { EnsureRegularFile(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { }
    }
    private static void RejectDuplicateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate review record field.");
                RejectDuplicateFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateFields(item);
    }
}
