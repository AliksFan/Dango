using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Protocol;

namespace Dango.Client.Sync.Pets;

public sealed class PetConfigurationFileJournal : IPetConfigurationJournal
{
    private const int MaximumDocumentBytes = 131072;
    private readonly string directory;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };
    private static readonly HashSet<string> ReceiptFailureCodes = new(StringComparer.Ordinal)
    {
        "pet_configuration_conflict", "pet_binding_changed", "local_unsaved_changes", "local_configuration_invalid",
        "local_configuration_conflict", "local_configuration_write_failed", "local_configuration_changed", "local_configuration_unavailable", "local_storage_failed",
        "authentication_required", "remote_request_rejected", "remote_response_invalid", "cloud_outcome_unknown",
        "expected_version_invalid", "configuration_choice_invalid", "server_origin_invalid"
    };

    public PetConfigurationFileJournal(string directory)
    {
        if (!Path.IsPathFullyQualified(directory)) throw new PetConfigurationChoiceException("archive_directory_invalid");
        this.directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (this.directory == Path.GetPathRoot(this.directory)) throw new PetConfigurationChoiceException("archive_directory_invalid");
        GuardDirectory();
    }

    public async Task<Guid> SaveOriginalsAsync(PetConfigurationOriginals originals, CancellationToken ct)
    {
        var origin = PetConfigurationHttpClient.ValidateOrigin(originals.Origin);
        if (originals.Choice is not { } choice || !Enum.IsDefined(choice)) throw InvalidArchive();
        ValidateCloud(originals.Cloud);
        var id = Guid.NewGuid();
        var document = new OriginalsDocument(1, origin.AbsoluteUri, originals.Local.Exists, originals.Local.OriginalBytes.ToArray(),
            originals.Local.NormalizedConfiguration, originals.Cloud.UserId, originals.Cloud.PetId,
            originals.Cloud.ConfigurationVersion, originals.Cloud.Configuration, originals.CapturedAt, choice);
        await WriteNewAsync(PathFor(id, "originals"), JsonSerializer.SerializeToUtf8Bytes(document, Json), ct);
        return id;
    }

    public async Task<PetConfigurationOriginals> LoadOriginalsAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var document = await ReadAsync<OriginalsDocument>(PathFor(id, "originals"), ct);
            if (document.SchemaVersion != 1 || !Enum.IsDefined(document.Choice) || !Uri.TryCreate(document.Origin, UriKind.Absolute, out var origin)) throw InvalidArchive();
            var cloud = new PetConfigurationSnapshot(document.UserId, document.PetId, document.ConfigurationVersion, document.CloudConfiguration);
            ValidateCloud(cloud);
            return new(PetConfigurationHttpClient.ValidateOrigin(origin),
                new LocalPetConfigurationSnapshot(document.LocalExists, document.LocalOriginalBytes, document.LocalConfiguration),
                cloud with { Configuration = cloud.Configuration.Clone() }, document.CapturedAt, document.Choice);
        }
        catch (Exception ex) when (ex is AppearanceContractException or PetConfigurationChoiceException)
        { throw InvalidArchive(); }
    }

    public async Task WriteReceiptAsync(Guid id, PetConfigurationReceipt receipt, CancellationToken ct)
    {
        if (!File.Exists(PathFor(id, "originals")) || !Enum.IsDefined(receipt.Choice) || !Enum.IsDefined(receipt.Outcome)
            || receipt.ObservedCloudVersion is < 1 || receipt.FailureCode is { } code && !ReceiptFailureCodes.Contains(code)) throw InvalidArchive();
        var originals = await LoadOriginalsAsync(id, ct);
        if (originals.Choice != receipt.Choice) throw InvalidArchive();
        await WriteNewAsync(PathFor(id, "receipt"), JsonSerializer.SerializeToUtf8Bytes(new ReceiptDocument(1, receipt), Json), ct);
    }

    public async Task<PetConfigurationReceipt?> LoadReceiptAsync(Guid id, CancellationToken ct = default)
    {
        var path = PathFor(id, "receipt");
        if (!File.Exists(path)) return null;
        var document = await ReadAsync<ReceiptDocument>(path, ct);
        if (document.SchemaVersion != 1 || !Enum.IsDefined(document.Receipt.Choice) || !Enum.IsDefined(document.Receipt.Outcome)
            || document.Receipt.ObservedCloudVersion is < 1
            || document.Receipt.FailureCode is { } code && !ReceiptFailureCodes.Contains(code)) throw InvalidArchive();
        var originals = await LoadOriginalsAsync(id, ct);
        if (originals.Choice != document.Receipt.Choice) throw InvalidArchive();
        return document.Receipt;
    }

    private async Task WriteNewAsync(string finalPath, byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length > MaximumDocumentBytes) throw InvalidArchive();
        ct.ThrowIfCancellationRequested();
        GuardDirectory();
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".temporary");
        var created = false;
        try
        {
            Directory.CreateDirectory(directory);
            GuardDirectory();
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                created = true;
                await file.WriteAsync(bytes, ct);
                await file.FlushAsync(ct);
                file.Flush(true);
            }
            GuardDirectory();
            File.Move(temporary, finalPath, false);
            created = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new PetConfigurationChoiceException("archive_write_failed"); }
        finally
        {
            if (created)
            {
                try { GuardDirectory(); File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PetConfigurationChoiceException) { }
            }
        }
    }

    private async Task<T> ReadAsync<T>(string path, CancellationToken ct)
    {
        GuardDirectory();
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw InvalidArchive();
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            if (file.Length > MaximumDocumentBytes) throw InvalidArchive();
            var bytes = new byte[checked((int)file.Length)];
            await file.ReadExactlyAsync(bytes, ct);
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw InvalidArchive();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { throw InvalidArchive(); }
    }

    private string PathFor(Guid id, string kind)
    {
        if (id == Guid.Empty) throw InvalidArchive();
        return Path.Combine(directory, id.ToString("N") + "." + kind + ".json");
    }

    private void GuardDirectory()
    {
        for (DirectoryInfo? part = new(directory); part is not null; part = part.Parent)
            if (part.Exists && (part.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new PetConfigurationChoiceException("archive_directory_invalid");
    }

    private static void ValidateCloud(PetConfigurationSnapshot cloud)
    {
        if (cloud.UserId == Guid.Empty || cloud.PetId == Guid.Empty || cloud.ConfigurationVersion < 1) throw InvalidArchive();
        try { AppearanceContract.ValidateAndClone(cloud.Configuration); }
        catch (AppearanceContractException) { throw InvalidArchive(); }
    }

    private static PetConfigurationChoiceException InvalidArchive() => new("archive_invalid");
    private sealed record OriginalsDocument(int SchemaVersion, string Origin, bool LocalExists, byte[] LocalOriginalBytes,
        JsonElement LocalConfiguration, Guid UserId, Guid PetId, long ConfigurationVersion, JsonElement CloudConfiguration, DateTimeOffset CapturedAt, PetConfigurationChoice Choice);
    private sealed record ReceiptDocument(int SchemaVersion, PetConfigurationReceipt Receipt);
}
