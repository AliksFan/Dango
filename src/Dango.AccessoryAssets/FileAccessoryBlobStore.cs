using System.Security.Cryptography;

namespace Dango.AccessoryAssets;

public sealed record AccessoryBlobImport(string SourceDigest, string BlobDigest, int Width, int Height, int ByteLength, bool Created, int NormalizationVersion);
internal enum BlobWriteStage { AfterPartialWrite, BeforePublish }

/// <summary>Private file storage primitive. A caller must authorize all access before calling it.</summary>
public sealed class FileAccessoryBlobStore
{
    private readonly string root;
    private readonly Action<BlobWriteStage>? writeFault;
    public FileAccessoryBlobStore(string rootDirectory) : this(rootDirectory, null) { }
    internal FileAccessoryBlobStore(string rootDirectory, Action<BlobWriteStage>? writeFault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory)) throw new ArgumentException("Blob storage requires an operator-selected absolute directory.", nameof(rootDirectory));
        root = Path.GetFullPath(rootDirectory); EnsurePrivateDirectory(root); this.writeFault = writeFault;
    }

    /// <summary>Checks source identity, then computes its own canonical identity. Neither digest grants account access.</summary>
    public async Task<AccessoryBlobImport> ImportAsync(Stream source, string? claimedSourceDigest = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        var sourceBytes = await ReadLimitedAsync(source, cancellationToken);
        var sourceDigest = Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
        if (claimedSourceDigest is not null && claimedSourceDigest != sourceDigest)
            throw new InvalidDataException("Claimed source digest does not match the received PNG.");
        var image = AccessoryPngNormalizer.Normalize(sourceBytes); cancellationToken.ThrowIfCancellationRequested();
        var path = GetPath(image.Digest); EnsurePrivateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) { await ReadAsync(image.Digest, cancellationToken); return Result(created: false); }
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var bytes = image.CopyPngBytes();
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var split = bytes.Length / 2; await stream.WriteAsync(bytes.AsMemory(0, split), cancellationToken);
                writeFault?.Invoke(BlobWriteStage.AfterPartialWrite);
                await stream.WriteAsync(bytes.AsMemory(split), cancellationToken);
                await stream.FlushAsync(cancellationToken); stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested(); writeFault?.Invoke(BlobWriteStage.BeforePublish);
            EnsurePrivateDirectory(Path.GetDirectoryName(path)!);
            try { File.Move(temporary, path, overwrite: false); return Result(created: true); }
            catch (IOException) when (File.Exists(path))
            { await ReadAsync(image.Digest, cancellationToken); return Result(created: false); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        AccessoryBlobImport Result(bool created) => new(sourceDigest, image.Digest, image.Width, image.Height, image.ByteLength, created, image.NormalizationVersion);
    }

    public async Task<CanonicalAccessoryImage> ReadAsync(string digest, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); var path = GetPath(digest);
        EnsurePrivateDirectory(Path.GetDirectoryName(path)!);
        if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Blob cannot be a directory or link.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        var bytes = await ReadLimitedAsync(stream, cancellationToken);
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != digest) throw new InvalidDataException("Stored blob checksum does not match its identity.");
        var image = AccessoryPngNormalizer.Normalize(bytes);
        if (image.Digest != digest) throw new InvalidDataException("Stored blob is not a canonical accessory PNG.");
        return image;
    }

    private string GetPath(string digest)
    {
        if (digest is null || digest.Length != 64 || digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Blob digest must be a lowercase SHA-256 hex string.", nameof(digest));
        return Path.Combine(root, digest[..2], digest + ".png");
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken); if (count == 0) break;
            if (output.Length + count > AccessoryPngNormalizer.MaximumFileBytes) throw new InvalidDataException("PNG exceeds the 32 MiB storage limit.");
            output.Write(buffer, 0, count);
        }
        if (output.Length == 0) throw new InvalidDataException("PNG cannot be empty.");
        return output.ToArray();
    }

    // The root is a trusted operator-controlled directory; reject links in every existing ancestor.
    // This is not a defense against another process with permission to concurrently replace storage directories.
    private static void EnsurePrivateDirectory(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Private blob storage cannot contain linked directories.");
        Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Private blob storage cannot be linked.");
    }
}
