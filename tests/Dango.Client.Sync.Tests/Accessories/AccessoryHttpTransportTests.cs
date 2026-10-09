using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Accessories;
using Dango.Protocol.Accessories;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests.Accessories;

[TestClass]
public sealed class AccessoryHttpTransportTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };

    [TestMethod]
    public async Task UsesFormalCookieFreshCsrfAndActualPngBytesForUploadThenCommit()
    {
        using var setup = new Setup();
        await setup.LoginAsync();
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        var image = Image();
        var uploaded = await transport.UploadAssetAsync(setup.Scope, image, default);
        Assert.AreEqual(image.AssetId, uploaded.LocalAssetId);
        var outfit = new AccessoryOutfitManifest(1, [new("first", "Fictional image", image.AssetId, 1, 1) { Softness = 27 }]);
        var saved = await transport.CommitOutfitAsync(setup.Scope, new(0, new string('a', 32), outfit), default);
        Assert.AreEqual(1L, saved.Revision);
        Assert.AreEqual(outfit, saved.Outfit);
        var upload = setup.Handler.Requests.Single(request => request.Path == "api/v1/me/pet/accessory-assets");
        CollectionAssert.AreEqual(image.PngBytes.ToArray(), upload.Bytes);
        Assert.AreEqual("image/png", upload.MediaType);
        Assert.AreEqual(image.AssetId, upload.SourceDigest);
        Assert.IsTrue(upload.HadAuth);
        var commit = setup.Handler.Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.IsTrue(commit.HadAuth);
        Assert.IsNotNull(upload.Csrf);
        Assert.AreNotEqual(upload.Csrf, commit.Csrf);
        Assert.IsFalse(Encoding.UTF8.GetString(commit.Bytes).Contains("fictional-account", StringComparison.Ordinal));
        Assert.AreEqual(setup.Origin, transport.ServiceOrigin);
    }

    [TestMethod]
    public async Task ReadsCurrentOutfitAndBoundedVerifiedPngAndEmpty204()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        Assert.IsNull(await transport.GetCurrentOutfitAsync(setup.Scope, default));
        var image = Image();
        var actual = await transport.DownloadAssetAsync(setup.Scope, image.AssetId, default);
        CollectionAssert.AreEqual(image.PngBytes.ToArray(), actual.PngBytes.ToArray());
        Assert.AreEqual(image.AssetId, actual.AssetId);
    }

    [TestMethod]
    public async Task UnknownReceiptNeverBecomesDefinitiveNotCommitted()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        var unknown = await transport.ResolveCommitAsync(setup.Scope, new string('b', 32), default);
        Assert.AreEqual(AccessoryCommitResolutionKind.Unknown, unknown.Kind);
        Assert.IsNull(unknown.StoredOutfit);
        setup.Handler.Override = record => record.Path.Contains("/commits/", StringComparison.Ordinal)
            ? Json(new { status = "committed", storedOutfit = new StoredAccessoryOutfit(1, AccessoryOutfitManifest.Empty) }) : null;
        var committed = await transport.ResolveCommitAsync(setup.Scope, new string('b', 32), default);
        Assert.AreEqual(AccessoryCommitResolutionKind.Committed, committed.Kind);
        Assert.AreEqual(1L, committed.StoredOutfit!.Revision);
    }

    [TestMethod]
    public async Task WrongScopeAndSameIdentityReloginCannotReviveOldTransport()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var old = setup.Client.CreateAccessoryTransport(setup.Scope);
        await Assert.ThrowsExactlyAsync<AccessoryAccountTransportException>(() => old.GetCurrentOutfitAsync(setup.Scope with { SessionGeneration = 2 }, default));
        await setup.Client.LogoutAsync(); await setup.LoginAsync();
        var error = await Assert.ThrowsExactlyAsync<AccessoryAccountTransportException>(() => old.GetCurrentOutfitAsync(setup.Scope, default));
        Assert.AreEqual(AccessoryTransportFailure.Authentication, error.Failure);
        Assert.IsNull(await setup.Client.CreateAccessoryTransport(setup.Scope with { SessionGeneration = 2 })
            .GetCurrentOutfitAsync(setup.Scope with { SessionGeneration = 2 }, default));
    }

    [TestMethod]
    public async Task FactoryRejectsUnverifiedIdentityAndCredentialedOrWrongScopeBeforeNetwork()
    {
        using var setup = new Setup();
        Assert.ThrowsExactly<AccountClientException>(() => setup.Client.CreateAccessoryTransport(setup.Scope));
        await setup.LoginAsync();
        Assert.ThrowsExactly<AccountClientException>(() => setup.Client.CreateAccessoryTransport(setup.Scope with { PetId = Guid.NewGuid().ToString("N") }));
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        var count = setup.Handler.Requests.Count;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => transport.ResolveCommitAsync(setup.Scope, "../other", default));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => transport.DownloadAssetAsync(setup.Scope, "../other", default));
        Assert.AreEqual(count, setup.Handler.Requests.Count);
    }

    [TestMethod]
    [DataRow(302)]
    [DataRow(503)]
    public async Task AmbiguousCommitResponsesRemainUnknownInCoordinator(int status)
    {
        using var setup = new Setup(); await setup.LoginAsync();
        setup.Handler.Override = record => record.Method == HttpMethod.Put ? Json(new { code = "fictional-private-secret" }, (HttpStatusCode)status) : null;
        await using var sync = new AccessoryAccountSync(new EmptyRepository());
        sync.AttachAccount(setup.Scope, setup.Client.CreateAccessoryTransport(setup.Scope), 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
        Assert.IsFalse(sync.RetryLatestBackup());
        Assert.IsFalse(JsonSerializer.Serialize(sync.SafeDiagnostics).Contains("fictional-private-secret", StringComparison.Ordinal));
        Assert.AreEqual(1, setup.Handler.Requests.Count(record => record.Method == HttpMethod.Put));
    }

    [TestMethod]
    public async Task Definitive401InvalidatesIdentityAndPublishesSafeNotification()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var notifications = 0;
        setup.Client.AuthenticationInvalidated += () => throw new InvalidOperationException("observer_error");
        setup.Client.AuthenticationInvalidated += () => { Assert.IsNull(setup.Client.CurrentIdentity); notifications++; };
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        setup.Handler.Override = record => record.Method == HttpMethod.Put ? ErrorResponse("authentication_required", HttpStatusCode.Unauthorized) : null;
        var failure = await Assert.ThrowsExactlyAsync<AccessoryAccountTransportException>(() => transport.CommitOutfitAsync(setup.Scope,
            new(0, new string('b', 32), AccessoryOutfitManifest.Empty), default));
        Assert.AreEqual(AccessoryTransportFailure.Authentication, failure.Failure);
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.AreEqual(1, notifications);
    }

    [TestMethod]
    [DataRow(409, "accessory_revision_conflict", AccessoryTransportFailure.RevisionConflict)]
    [DataRow(409, "accessory_idempotency_conflict", AccessoryTransportFailure.Rejected)]
    [DataRow(400, "request_invalid", AccessoryTransportFailure.Rejected)]
    [DataRow(403, "request_protection_invalid", AccessoryTransportFailure.Rejected)]
    [DataRow(403, "access_denied", AccessoryTransportFailure.Authentication)]
    public async Task ParsesActualFormalErrorEnvelopeAndPreservesDefinitiveClassification(int status, string code, AccessoryTransportFailure expected)
    {
        using var setup = new Setup(); await setup.LoginAsync();
        setup.Handler.Override = record => record.Method == HttpMethod.Put ? ErrorResponse(code, (HttpStatusCode)status) : null;
        var error = await Assert.ThrowsExactlyAsync<AccessoryAccountTransportException>(() => setup.Client.CreateAccessoryTransport(setup.Scope)
            .CommitOutfitAsync(setup.Scope, new(0, new string('c', 32), AccessoryOutfitManifest.Empty), default));
        Assert.AreEqual(expected, error.Failure);
    }

    [TestMethod]
    [DataRow(true, 429, "accessory_upload_busy")]
    [DataRow(false, 408, "accessory_body_timeout")]
    public async Task ExplicitFormalBusyAndBodyDeadlineResponsesAreDefinitiveRejections(bool upload, int status, string code)
    {
        using var setup = new Setup(); await setup.LoginAsync();
        setup.Handler.Override = record => record.Method == (upload ? HttpMethod.Post : HttpMethod.Put)
            ? ErrorResponse(code, (HttpStatusCode)status) : null;
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        var failure = await Assert.ThrowsExactlyAsync<AccessoryAccountTransportException>(async () =>
        {
            if (upload) await transport.UploadAssetAsync(setup.Scope, Image(), default);
            else await transport.CommitOutfitAsync(setup.Scope, new(0, new string('a', 32), AccessoryOutfitManifest.Empty), default);
        });
        Assert.AreEqual(AccessoryTransportFailure.Rejected, failure.Failure);
        Assert.IsNotNull(setup.Client.CurrentIdentity);
    }

    [TestMethod]
    [DataRow("{\"code\":\"accessory_body_timeout\"}")]
    [DataRow("{\"code\":\"accessory_body_timeout\",\"correlationId\":\"fictional-correlation\",\"message\":\"fictional-secret\"}")]
    public async Task MalformedBodyDeadlineEnvelopeCannotProveCommitDidNotWrite(string raw)
    {
        using var setup = new Setup(); await setup.LoginAsync();
        setup.Handler.Override = record => record.Method == HttpMethod.Put ? RawJson(raw, HttpStatusCode.RequestTimeout) : null;
        await using var sync = new AccessoryAccountSync(new EmptyRepository());
        sync.AttachAccount(setup.Scope, setup.Client.CreateAccessoryTransport(setup.Scope), 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.IsFalse(sync.RetryLatestBackup());
    }

    [TestMethod]
    public async Task TransportTimeoutAfterDispatchCannotBecomeAnExplicitServerBodyRejection()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        setup.Handler.Override = record => record.Method == HttpMethod.Put ? throw new TaskCanceledException("fictional transport timeout") : null;
        await using var sync = new AccessoryAccountSync(new EmptyRepository());
        sync.AttachAccount(setup.Scope, setup.Client.CreateAccessoryTransport(setup.Scope), 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
        Assert.IsFalse(sync.RetryLatestBackup());
        Assert.AreEqual(1, setup.Handler.Requests.Count(record => record.Method == HttpMethod.Put));
    }

    [TestMethod]
    [DataRow("{\"code\":\"authentication_required\"}")]
    [DataRow("{\"code\":\"authentication_required\",\"correlationId\":null}")]
    [DataRow("{\"code\":false,\"correlationId\":\"fictional-correlation\"}")]
    [DataRow("{\"code\":\"authentication_required\",\"correlationId\":\"fictional-correlation\",\"message\":\"fictional-secret\"}")]
    public async Task MalformedErrorCannotEstablishNonwriteAuthenticationOutcome(string raw)
    {
        using var setup = new Setup(); await setup.LoginAsync();
        setup.Handler.Override = record => record.Method == HttpMethod.Put ? RawJson(raw, HttpStatusCode.Unauthorized) : null;
        await using var sync = new AccessoryAccountSync(new EmptyRepository());
        sync.AttachAccount(setup.Scope, setup.Client.CreateAccessoryTransport(setup.Scope), 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.IsNotNull(setup.Client.CurrentIdentity);
    }

    [TestMethod]
    public async Task BindingInvalidationAfterSendCannotBecomeDefinitiveRejection()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        setup.Handler.Override = record =>
        {
            if (record.Method != HttpMethod.Put) return null;
            setup.Client.Dispose();
            return Json(new StoredAccessoryOutfit(1, AccessoryOutfitManifest.Empty));
        };
        await using var sync = new AccessoryAccountSync(new EmptyRepository());
        sync.AttachAccount(setup.Scope, transport, 0);
        sync.QueueLocalCommit(new(AccessoryOutfitManifest.Empty, [], 1));
        await Until(() => sync.SafeDiagnostics.State == AccessorySyncState.OutcomeUnknown);
        Assert.IsNull(sync.SafeDiagnostics.LastBackedUpLocalVersion);
    }

    [TestMethod]
    public async Task DisposedOwnerDrainWaitsForUncancelledOldSecretCleanup()
    {
        var store = new DelayedStore();
        using var setup = new Setup(store);
        await setup.Client.LoginAsync("fictional@example.invalid", "fictional-password", true);
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        store.DelayDeletion = true;
        var disposed = false;
        setup.Client.AuthenticationInvalidated += () =>
        {
            if (disposed) return;
            disposed = true;
            setup.Client.Dispose();
        };
        setup.Handler.Override = record => record.Path == "api/v1/me/pet/accessories" ? ErrorResponse("authentication_required", HttpStatusCode.Unauthorized) : null;
        var oldRequest = transport.GetCurrentOutfitAsync(setup.Scope, default);
        await store.DeleteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var draining = setup.Client.DrainAsync();
        try { Assert.IsFalse(draining.IsCompleted, "New login must not reuse secret slots while the old owner can still delete them."); }
        finally { store.ReleaseDelete.TrySetResult(); }
        await draining;
        await Assert.ThrowsExactlyAsync<AccessoryAccountTransportException>(() => oldRequest);
        Assert.AreEqual(0, store.Values.Count);
    }

    [TestMethod]
    [DataRow("{\"revision\":1,\"revision\":2,\"outfit\":{\"schemaVersion\":1,\"items\":[]}}")]
    [DataRow("{\"revision\":1,\"outfit\":{\"schemaVersion\":1,\"items\":[]},\"unexpected\":true}")]
    [DataRow("{\"revision\":1}")]
    public async Task StrictBoundedJsonRejectsDuplicateUnknownOrMissingFields(string raw)
    {
        using var setup = new Setup(); await setup.LoginAsync();
        setup.Handler.Override = record => record.Path == "api/v1/me/pet/accessories" ? RawJson(raw) : null;
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.CreateAccessoryTransport(setup.Scope).GetCurrentOutfitAsync(setup.Scope, default));
    }

    [TestMethod]
    public async Task MissingItemParameterCannotSilentlyRestoreDefaultValue()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var outfit = new AccessoryOutfitManifest(1, [new("hat", "Fictional hat", "builtin-top-hat", 64, 64)]);
        var raw = JsonSerializer.Serialize(new StoredAccessoryOutfit(1, outfit), Wire).Replace("\"lockRotation\":true,", "", StringComparison.Ordinal);
        setup.Handler.Override = record => record.Path == "api/v1/me/pet/accessories" ? RawJson(raw) : null;
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.CreateAccessoryTransport(setup.Scope).GetCurrentOutfitAsync(setup.Scope, default));
    }

    [TestMethod]
    public async Task RejectsWrongUploadSourceMappingNormalizationAndDownloadHash()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var image = Image(); var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        setup.Handler.Override = record => record.Path == "api/v1/me/pet/accessory-assets"
            ? Json(new UploadedAccessoryAsset(new string('d', 64), image.AssetId, 1, 1, 1)) : null;
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => transport.UploadAssetAsync(setup.Scope, image, default));
        setup.Handler.Override = record => record.Path == "api/v1/me/pet/accessory-assets"
            ? Json(new UploadedAccessoryAsset(image.AssetId, image.AssetId, 1, 1, 99)) : null;
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => transport.UploadAssetAsync(setup.Scope, image, default));
        setup.Handler.Override = null;
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => transport.DownloadAssetAsync(setup.Scope, new string('d', 64), default));
    }

    [TestMethod]
    public async Task OversizedJsonAndPngContentLengthFailBeforeBodyAllocation()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        var transport = setup.Client.CreateAccessoryTransport(setup.Scope);
        setup.Handler.Override = record => record.Path == "api/v1/me/pet/accessories" ? Oversized("application/json", 32769) : null;
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => transport.GetCurrentOutfitAsync(setup.Scope, default));
        setup.Handler.Override = record => record.Path.Contains("accessory-assets/", StringComparison.Ordinal) ? Oversized("image/png", 32 * 1024 * 1024 + 1) : null;
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => transport.DownloadAssetAsync(setup.Scope, Image().AssetId, default));
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private static AccessoryImageSnapshot Image()
    {
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), 1, 1, bytes);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => RawJson(JsonSerializer.Serialize(body, Wire), status);
    private static HttpResponseMessage ErrorResponse(string code, HttpStatusCode status) =>
        Json(new { code, correlationId = "fictional-request-correlation" }, status);
    private static HttpResponseMessage RawJson(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Oversized(string media, long length)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentType = new(media); response.Content.Headers.ContentLength = length; return response;
    }

    private sealed class Setup : IDisposable
    {
        public Uri Origin { get; } = new("https://accessory-account.example.invalid/");
        public CookieContainer Cookies { get; } = new();
        public Handler Handler { get; }
        public FormalAccountClient Client { get; }
        public AccessoryAccountScope Scope => new(Handler.UserId.ToString("N"), Handler.PetId.ToString("N"), 1);
        public Setup(IAccountSecretStore? store = null) { Handler = new(Origin, Cookies); Client = FormalAccountClient.ForTesting(Origin, Handler, Cookies, store); }
        public Task<AccountIdentity> LoginAsync() => Client.LoginAsync("fictional@example.invalid", "fictional-password", false);
        public void Dispose() => Client.Dispose();
    }

    private sealed record Request(Uri Origin, string Path, HttpMethod Method, byte[] Bytes, string? MediaType,
        string? Csrf, string? SourceDigest, bool HadAuth);
    private sealed class Handler(Uri origin, CookieContainer cookies) : HttpMessageHandler
    {
        public Guid UserId { get; } = Guid.NewGuid(); public Guid PetId { get; } = Guid.NewGuid();
        public ConcurrentQueue<Request> Requests { get; } = [];
        public Func<Request, HttpResponseMessage?>? Override { get; set; }
        private int proofs;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Assert.AreEqual(origin.GetLeftPart(UriPartial.Authority), uri.GetLeftPart(UriPartial.Authority));
            var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var record = new Request(uri, uri.AbsolutePath.TrimStart('/'), request.Method, bytes, request.Content?.Headers.ContentType?.MediaType,
                Header("X-CSRF-TOKEN"), Header("X-Accessory-Source-Digest"), cookies.GetCookies(origin)["__Host-Dango.Auth"] is not null);
            Requests.Enqueue(record);
            var overridden = Override?.Invoke(record); if (overridden is not null) return overridden;
            if (record.Path == "api/v1/auth/request-protection") return Json(new { requestToken = "fictional_request_csrf_proof_" + ++proofs });
            if (record.Path == "api/v1/auth/login")
            {
                cookies.Add(origin, new Cookie("__Host-Dango.Auth", "fictional-auth-cookie", "/") { Secure = true, HttpOnly = true, Expires = DateTime.UtcNow.AddHours(1) });
                return Json(new { userId = UserId, sessionId = Guid.NewGuid(), expiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            }
            if (record.Path == "api/v1/auth/logout") return Json(new { status = "signed_out" });
            if (record.Path == "api/v1/me") return Json(new { userId = UserId });
            if (record.Path == "api/v1/me/pet") return Json(new { userId = UserId, petId = PetId, configurationVersion = 1,
                configuration = JsonDocument.Parse(PetConfigurationChoiceTests.AppearanceJson).RootElement.Clone() });
            Assert.IsTrue(record.HadAuth);
            if (record.Method != HttpMethod.Get) Assert.IsNotNull(record.Csrf);
            if (record.Path == "api/v1/me/pet/accessory-assets")
            {
                var hash = Convert.ToHexStringLower(SHA256.HashData(record.Bytes));
                Assert.AreEqual(hash, record.SourceDigest); return Json(new UploadedAccessoryAsset(hash, hash, 1, 1, 1));
            }
            if (record.Path.Contains("accessory-assets/", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Image().PngBytes.ToArray()) };
                response.Content.Headers.ContentType = new("image/png"); return response;
            }
            if (record.Path.Contains("/commits/", StringComparison.Ordinal)) return Json(new { status = "unknown", storedOutfit = (object?)null });
            if (record.Path == "api/v1/me/pet/accessories" && record.Method == HttpMethod.Get) return new(HttpStatusCode.NoContent);
            if (record.Path == "api/v1/me/pet/accessories" && record.Method == HttpMethod.Put)
            {
                var commit = JsonSerializer.Deserialize<CommitAccessoryOutfitRequest>(record.Bytes, Wire)!;
                return Json(new StoredAccessoryOutfit(commit.ExpectedRevision + 1, commit.Outfit));
            }
            throw new InvalidOperationException("unexpected_test_route");
            string? Header(string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
        }
    }

    private sealed class EmptyRepository : IAccessoryLocalRepository
    {
        public Task<AccessoryRestoreGuard> CaptureRestoreGuardAsync(CancellationToken cancellationToken) => Task.FromResult(new AccessoryRestoreGuard(0, false));
        public Task<IAccessoryRestoreStage> StageRestoreAsync(AccessoryLocalSnapshot snapshot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> TryCommitRestoreAsync(IAccessoryRestoreStage stage, AccessoryRestoreGuard guard, AccessoryRestoreAuthorization authorization, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DelayedStore : IAccountSecretStore
    {
        public Dictionary<string, byte[]> Values { get; } = [];
        public bool DelayDeletion { get; set; }
        public TaskCompletionSource DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) => Task.FromResult(Values.TryGetValue(slot, out var bytes) ? bytes.ToArray() : null);
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct) { Values[slot] = value.ToArray(); return Task.CompletedTask; }
        public async Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        {
            if (DelayDeletion) { DeleteStarted.TrySetResult(); await ReleaseDelete.Task; }
            Values.Remove(slot);
        }
    }
}
