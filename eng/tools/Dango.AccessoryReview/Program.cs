using System.Diagnostics;
using System.Globalization;
using System.Text;
using Dango.AccessoryAssets;
using Dango.AccessoryModeration;

Console.OutputEncoding = Encoding.UTF8;
return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    try
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("Local administrator prototype: request | list [--all] | show [--open] | approve | reject | revoke");
            Console.WriteLine("All commands: --blob-root EXISTING_PRIVATE_DIRECTORY --review-root PRIVATE_REVIEW_DIRECTORY");
            Console.WriteLine("request/show: --id HASH. decisions: --id HASH --revision N --reason TEXT.");
            return 0;
        }
        var command = args[0];
        if (command is not ("request" or "list" or "show" or "approve" or "reject" or "revoke")) throw new ArgumentException("Unknown command.");
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var name = args[index]; string? value;
            if (name is "--open" or "--all") value = null;
            else
            {
                if (name is not ("--blob-root" or "--review-root" or "--id" or "--revision" or "--reason") || ++index == args.Length)
                    throw new ArgumentException("Invalid command option.");
                value = args[index];
            }
            if (!options.TryAdd(name, value)) throw new ArgumentException("Duplicate command option.");
        }
        var allowed = command switch
        {
            "list" => new[] { "--blob-root", "--review-root", "--all" },
            "show" => ["--blob-root", "--review-root", "--id", "--open"],
            "request" => ["--blob-root", "--review-root", "--id"],
            _ => ["--blob-root", "--review-root", "--id", "--revision", "--reason"]
        };
        if (options.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new ArgumentException("Option does not apply to command.");
        var blobRoot = Required("--blob-root"); var reviewRoot = Required("--review-root");
        if (!Path.IsPathFullyQualified(blobRoot) || !Directory.Exists(blobRoot)) throw new ArgumentException("The private blob directory must already exist and be absolute.");
        var blobs = new FileAccessoryBlobStore(blobRoot); var reviews = new FileAccessoryReviewRepository(reviewRoot);
        var service = new AccessoryImageReviewService(blobs, reviews);
        if (command == "list")
        {
            var records = await reviews.ListAsync();
            foreach (var record in records.Where(record => options.ContainsKey("--all") || record.Status == AccessoryImageReviewStatus.Pending)) Print(record);
            return 0;
        }
        var digest = Required("--id");
        if (command == "request") Print(await service.RequestReviewAsync(digest));
        else if (command == "show")
        {
            var image = await service.ReadPreviewAsync(digest); var record = await reviews.ReadAsync(digest);
            Console.WriteLine(record is null ? "Unrequested private image; no review has been queued." : $"{record.Status}, revision {record.Revision}, {record.History[^1].Reason}");
            var path = Path.Combine(Path.GetFullPath(blobRoot), image.Digest[..2], image.Digest + ".png");
            Console.WriteLine($"Verified PNG {image.Width} x {image.Height}; normalization {image.NormalizationVersion}");
            Console.WriteLine(path);
            if (options.ContainsKey("--open")) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        else
        {
            if (!int.TryParse(Required("--revision"), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
                throw new ArgumentException("Expected revision must be a positive integer.");
            var decision = command switch { "approve" => AccessoryImageReviewDecision.Approve, "reject" => AccessoryImageReviewDecision.Reject, _ => AccessoryImageReviewDecision.Revoke };
            Print(await service.DecideAsync(digest, revision, decision, Required("--reason")));
        }
        return 0;

        string Required(string name) => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing required command option.");
    }
    catch (AccessoryReviewConflictException error)
    { Console.Error.WriteLine($"Review changed: current revision {error.ActualRevision}; reread before deciding."); return 3; }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
        or InvalidOperationException or System.ComponentModel.Win32Exception)
    { Console.Error.WriteLine($"Local review operation failed ({error.GetType().Name}); existing private data was retained."); return 2; }
}

static void Print(AccessoryImageReview record) => Console.WriteLine(
    $"{record.Digest}  {record.Status}  revision={record.Revision}  {record.PixelWidth}x{record.PixelHeight}  {record.History[^1].Reason}");
