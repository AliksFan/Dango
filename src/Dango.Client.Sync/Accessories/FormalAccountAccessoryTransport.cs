using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Client.Sync.Accessories;
using Dango.Protocol.Accessories;

namespace Dango.Client.Sync.Accounts;

public sealed partial class FormalAccountClient
{
    /// <summary>The caller supplies its attachment epoch; the returned transport also fences the formal login generation.</summary>
    public IAccessoryAccountTransport CreateAccessoryTransport(AccessoryAccountScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Validate();
        lock (sessionBindingGate)
        {
            ThrowIfDisposed();
            if (CurrentIdentity is not { } identity || !Guid.TryParse(scope.AccountId, out var userId) ||
                !Guid.TryParse(scope.PetId, out var petId) || userId != identity.UserId || petId != identity.PetId)
                throw new AccountClientException(AccountFailure.Authentication, "accessory_binding_changed");
            return new BoundAccessoryTransport(this, scope, identity, sessionGeneration);
        }
    }

    private sealed class BoundAccessoryTransport(FormalAccountClient owner, AccessoryAccountScope boundScope,
        AccountIdentity identity, int generation) : IAccessoryAccountTransport
    {
        private const string OutfitPath = "api/v1/me/pet/accessories";
        private const string AssetsPath = "api/v1/me/pet/accessory-assets";
        private const int MaximumJsonBytes = 32 * 1024;
        private static readonly JsonSerializerOptions AccessoryJson = new(Json)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
        private static readonly string[] ItemFields =
        [
            "id", "name", "assetId", "pixelWidth", "pixelHeight", "layer", "order", "sizePercent", "baseAngleDegrees",
            "imagePivotX", "imagePivotY", "bodyAnchorX", "bodyAnchorY", "softness", "allowFolding", "maxDeformationPercent",
            "connectionStrength", "maxConnectionTravelPercent", "rotationDamping", "lockRotation", "returnStrength",
            "maxSwingDegrees", "limitSwing", "gravityInfluence"
        ];
        private static readonly HashSet<string> AccessoryErrorCodes = new(StringComparer.Ordinal)
        {
            "accessory_revision_conflict", "accessory_idempotency_conflict", "accessory_body_too_large",
            "accessory_asset_unavailable", "accessory_staging_limit", "accessory_storage_unavailable",
            "accessory_request_invalid", "accessory_image_invalid", "accessory_source_digest_invalid",
            "accessory_revision_exhausted", "accessory_asset_dimensions_invalid", "accessory_content_type_invalid",
            "accessory_upload_busy", "accessory_body_timeout"
        };

        public Uri ServiceOrigin { get; } = owner.Origin;

        public Task<UploadedAccessoryAsset> UploadAssetAsync(AccessoryAccountScope scope, AccessoryImageSnapshot image,
            CancellationToken cancellationToken) => ExecuteAsync(scope, async (token, dispatch) =>
        {
            ArgumentNullException.ThrowIfNull(image);
            using var content = new ByteArrayContent(image.PngBytes.ToArray());
            content.Headers.ContentType = new("image/png");
            using var response = await SendAsync(HttpMethod.Post, AssetsPath, content, scope, dispatch, token, image.AssetId);
            RequireOk(response);
            var json = await ReadJsonAsync(response, token);
            Fields(json, "localAssetId", "assetId", "pixelWidth", "pixelHeight", "normalizationVersion");
            var uploaded = Deserialize<UploadedAccessoryAsset>(json);
            ValidateResponse(uploaded.Validate);
            if (uploaded.LocalAssetId != image.AssetId || uploaded.PixelWidth != image.PixelWidth || uploaded.PixelHeight != image.PixelHeight)
                throw InvalidAccessoryResponse();
            return uploaded;
        }, cancellationToken);

        public Task<StoredAccessoryOutfit> CommitOutfitAsync(AccessoryAccountScope scope, CommitAccessoryOutfitRequest request,
            CancellationToken cancellationToken) => ExecuteAsync(scope, async (token, dispatch) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            request.Validate();
            if (request.ExpectedRevision == long.MaxValue) throw new ArgumentException("accessory_revision_exhausted");
            using var content = JsonContent.Create(request, options: AccessoryJson);
            using var response = await SendAsync(HttpMethod.Put, OutfitPath, content, scope, dispatch, token);
            RequireOk(response);
            var stored = ParseStored(await ReadJsonAsync(response, token));
            if (stored.Revision != request.ExpectedRevision + 1 || !stored.Outfit.Equals(request.Outfit)) throw InvalidAccessoryResponse();
            return stored;
        }, cancellationToken);

        public Task<StoredAccessoryOutfit?> GetCurrentOutfitAsync(AccessoryAccountScope scope,
            CancellationToken cancellationToken) => ExecuteAsync<StoredAccessoryOutfit?>(scope, async (token, dispatch) =>
        {
            using var response = await SendAsync(HttpMethod.Get, OutfitPath, null, scope, dispatch, token);
            if (response.StatusCode == HttpStatusCode.NoContent) return null;
            RequireOk(response);
            return ParseStored(await ReadJsonAsync(response, token));
        }, cancellationToken);

        public Task<AccessoryImageSnapshot> DownloadAssetAsync(AccessoryAccountScope scope, string assetId,
            CancellationToken cancellationToken) => ExecuteAsync(scope, async (token, dispatch) =>
        {
            if (!AccessoryOutfitItem.IsContentHash(assetId)) throw new ArgumentException("invalid_accessory_asset_id");
            using var response = await SendAsync(HttpMethod.Get, AssetsPath + "/" + assetId, null, scope, dispatch, token);
            RequireOk(response);
            var bytes = await ReadBoundedAsync(response, "image/png", AccessoryImageSnapshot.MaximumByteCount, token);
            try
            {
                if (bytes.Length < 33) throw new InvalidDataException();
                var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
                var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
                if (width > int.MaxValue || height > int.MaxValue) throw new InvalidDataException();
                // Hash/signature/IHDR/dimensions are verified here; the server and desktop stage also
                // decode the full PNG. No download is applied directly to active desktop state.
                return new AccessoryImageSnapshot(assetId, (int)width, (int)height, bytes);
            }
            catch (InvalidDataException) { throw InvalidAccessoryResponse(); }
        }, cancellationToken);

        public Task<AccessoryCommitResolution> ResolveCommitAsync(AccessoryAccountScope scope, string idempotencyKey,
            CancellationToken cancellationToken) => ExecuteAsync<AccessoryCommitResolution>(scope, async (token, dispatch) =>
        {
            if (idempotencyKey is not { Length: 32 } || idempotencyKey.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
                throw new ArgumentException("invalid_accessory_commit_key");
            using var response = await SendAsync(HttpMethod.Get, OutfitPath + "/commits/" + idempotencyKey, null, scope, dispatch, token);
            RequireOk(response);
            var json = await ReadJsonAsync(response, token);
            Fields(json, "status", "storedOutfit");
            var status = StringValue(json, "status");
            if (status == "committed" && json.GetProperty("storedOutfit").ValueKind != JsonValueKind.Null)
                return new(AccessoryCommitResolutionKind.Committed, ParseStored(json.GetProperty("storedOutfit")));
            if (status == "unknown" && json.GetProperty("storedOutfit").ValueKind == JsonValueKind.Null)
                return new(AccessoryCommitResolutionKind.Unknown);
            // Missing or expired receipts do not establish that an earlier PUT did not commit.
            throw InvalidAccessoryResponse();
        }, cancellationToken);

        private async Task<T> ExecuteAsync<T>(AccessoryAccountScope scope,
            Func<CancellationToken, DispatchState, Task<T>> operation, CancellationToken cancellationToken)
        {
            var dispatch = new DispatchState();
            try
            {
                return await owner.LockedAsync(async token =>
                {
                    RequireBinding(scope, token, beforeSend: true);
                    var result = await operation(token, dispatch);
                    RequireBinding(scope, token, beforeSend: !dispatch.WasSent);
                    return result;
                }, cancellationToken);
            }
            catch (AccountClientException error) when (error.Failure == AccountFailure.Authentication)
            {
                // This exception is emitted only by a definitive HTTP authentication response.
                // A retired binding after dispatch uses Unavailable/cancellation below instead.
                throw new AccessoryAccountTransportException(AccessoryTransportFailure.Authentication);
            }
        }

        private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content,
            AccessoryAccountScope scope, DispatchState dispatch, CancellationToken token, string? sourceDigest = null)
        {
            string? csrf = null;
            if (method != HttpMethod.Get)
            {
                var protection = await owner.RequestAsync(HttpMethod.Get, "api/v1/auth/request-protection", null, null, token);
                Fields(protection, "requestToken");
                csrf = StringValue(protection, "requestToken");
                if (csrf.Length is < 16 or > 4096 || csrf.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
                    throw InvalidAccessoryResponse();
            }
            RequireBinding(scope, token, beforeSend: true);
            using var request = new HttpRequestMessage(method, path) { Content = content };
            if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
            if (sourceDigest is not null) request.Headers.Add("X-Accessory-Source-Digest", sourceDigest);
            HttpResponseMessage? response = null;
            try
            {
                dispatch.WasSent = true;
                response = await owner.http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                token.ThrowIfCancellationRequested();
                if ((int)response.StatusCode is >= 300 and < 400) throw InvalidAccessoryResponse();
                if ((int)response.StatusCode >= 500) throw new AccountClientException(AccountFailure.Unavailable, "accessory_outcome_unknown");
                if ((int)response.StatusCode >= 400)
                {
                    var error = await ReadJsonAsync(response, token);
                    Fields(error, "code", "correlationId");
                    var code = StringValue(error, "code");
                    var correlation = StringValue(error, "correlationId");
                    if (correlation.Length is < 1 or > 256 || correlation.Any(char.IsControl) ||
                        !ErrorCodes.Contains(code) && !AccessoryErrorCodes.Contains(code)) throw InvalidAccessoryResponse();
                    if (response.StatusCode == HttpStatusCode.Unauthorized && code == "authentication_required" ||
                        response.StatusCode == HttpStatusCode.Forbidden && code == "access_denied")
                        throw new AccountClientException(AccountFailure.Authentication,
                            response.StatusCode == HttpStatusCode.Unauthorized ? "authentication_required" : "access_denied");
                    throw new AccessoryAccountTransportException(response.StatusCode == HttpStatusCode.Conflict && code == "accessory_revision_conflict"
                        ? AccessoryTransportFailure.RevisionConflict : AccessoryTransportFailure.Rejected);
                }
                return response;
            }
            catch (Exception error) when (error is HttpRequestException or IOException || error is OperationCanceledException && !token.IsCancellationRequested)
            {
                response?.Dispose();
                throw new AccountClientException(AccountFailure.Unavailable, "accessory_outcome_unknown");
            }
            catch { response?.Dispose(); throw; }
        }

        private void RequireBinding(AccessoryAccountScope scope, CancellationToken token, bool beforeSend)
        {
            token.ThrowIfCancellationRequested();
            lock (owner.sessionBindingGate)
            {
                if (!owner.disposed && generation == owner.sessionGeneration && owner.CurrentIdentity == identity && scope == boundScope)
                    return;
                if (beforeSend) throw new AccessoryAccountTransportException(AccessoryTransportFailure.Authentication);
                throw new AccountClientException(AccountFailure.Unavailable, "accessory_outcome_unknown");
            }
        }

        private static void RequireOk(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.OK) throw InvalidAccessoryResponse();
        }

        private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, string mediaType, int maximum,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(response.Content.Headers.ContentType?.MediaType, mediaType, StringComparison.OrdinalIgnoreCase) ||
                response.Content.Headers.ContentLength > maximum || response.Content.Headers.ContentEncoding.Count != 0)
                throw InvalidAccessoryResponse();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, deadline.Token);
                if (count == 0) break;
                if (bytes.Length + count > maximum) throw InvalidAccessoryResponse();
                bytes.Write(buffer, 0, count);
            }
            if (response.Content.Headers.ContentLength is { } expected && expected != bytes.Length) throw InvalidAccessoryResponse();
            return bytes.ToArray();
        }

        private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var bytes = await ReadBoundedAsync(response, "application/json", MaximumJsonBytes, cancellationToken);
            try
            {
                using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
                if (HasDuplicates(document.RootElement)) throw InvalidAccessoryResponse();
                return document.RootElement.Clone();
            }
            catch (JsonException) { throw InvalidAccessoryResponse(); }
        }

        private static T Deserialize<T>(JsonElement json)
        {
            try { return JsonSerializer.Deserialize<T>(json, AccessoryJson) ?? throw InvalidAccessoryResponse(); }
            catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
            { throw InvalidAccessoryResponse(); }
        }

        private static StoredAccessoryOutfit ParseStored(JsonElement json)
        {
            Fields(json, "revision", "outfit");
            var outfit = json.GetProperty("outfit");
            Fields(outfit, "schemaVersion", "items");
            var items = outfit.GetProperty("items");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > AccessoryOutfitManifest.MaximumEquippedItems)
                throw InvalidAccessoryResponse();
            foreach (var item in items.EnumerateArray())
            {
                Fields(item, ItemFields);
                if (StringValue(item, "layer") is not ("front" or "behind")) throw InvalidAccessoryResponse();
            }
            var stored = Deserialize<StoredAccessoryOutfit>(json);
            ValidateResponse(stored.Validate);
            return stored;
        }

        private static void ValidateResponse(Action validate)
        {
            try { validate(); }
            catch (InvalidDataException) { throw InvalidAccessoryResponse(); }
        }

        private static AccountClientException InvalidAccessoryResponse() => new(AccountFailure.InvalidResponse, "accessory_response_invalid");
        private sealed class DispatchState { internal bool WasSent { get; set; } }
    }
}
