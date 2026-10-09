using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Client.Sync.Pets;
using Dango.Protocol;

namespace Dango.Client.Sync.Accounts;

// The application lifecycle owns this client. When replacing it, cancel and await its
// outstanding operations before opening a new client against the same protected-store slots.
public sealed class FormalAccountClient : IDisposable
{
    private const int MaximumResponseBytes = 16384;
    private const int MaximumSecretBytes = 2560;
    private const string AuthCookieName = "__Host-Dango.Auth";
    private readonly HttpClient http;
    private readonly IAccountSecretStore? secrets;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private int sessionGeneration;
    private bool disposed;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16
    };
    private static readonly HashSet<string> ErrorCodes = new(StringComparer.Ordinal)
    {
        "login_failed", "authentication_required", "access_denied", "request_protection_invalid", "origin_invalid",
        "email_invalid", "password_invalid", "proof_invalid", "registration_input_invalid", "invitation_invalid",
        "registration_conflict", "registration_proof_invalid", "registration_state_invalid", "registration_expired",
        "registration_already_completed", "registration_result_expired", "email_verification_required", "email_rate_limited",
        "session_limit_reached", "session_not_found", "account_update_conflict", "request_rate_limited", "request_invalid",
        "write_conflict", "service_unavailable", "pet_configuration_conflict", "expected_version_invalid"
    };
    private static readonly HashSet<string> EmailStatuses = new(StringComparer.Ordinal)
    {
        "queued", "smtp_accepted", "send_failed", "cancelled", "smtp_not_configured", "smtp_permanent",
        "smtp_attempt_limit", "smtp_transient", "email_challenge_inactive", "smtp_delivery_invalid", "smtp_tls_required",
        "smtp_timeout", "smtp_tls_failed", "smtp_authentication_failed", "smtp_temporary_rejection", "smtp_rejected",
        "smtp_protocol_error", "smtp_unavailable", "smtp_feature_unsupported", "smtp_send_failed"
    };

    public FormalAccountClient(Uri origin, IAccountSecretStore? store = null)
    {
        Origin = AccountInputs.Origin(origin);
        Cookies = new CookieContainer();
        http = new HttpClient(new HttpClientHandler { CookieContainer = Cookies, UseCookies = true, AllowAutoRedirect = false })
        { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(20) };
        secrets = store;
        clock = TimeProvider.System;
    }

    private FormalAccountClient(Uri origin, HttpMessageHandler handler, CookieContainer cookies,
        IAccountSecretStore? store, TimeProvider? clock)
    {
        Origin = AccountInputs.Origin(origin);
        Cookies = cookies;
        http = new HttpClient(handler) { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(20) };
        secrets = store;
        this.clock = clock ?? TimeProvider.System;
    }

    // Controlled handlers only. Production construction retains normal certificate validation.
    public static FormalAccountClient ForTesting(Uri origin, HttpMessageHandler handler, CookieContainer cookies,
        IAccountSecretStore? store = null, TimeProvider? clock = null) => new(origin, handler, cookies, store, clock);

    public Uri Origin { get; }
    public CookieContainer Cookies { get; }
    public AccountIdentity? CurrentIdentity { get; private set; }

    public Task<AccountIdentity> ReadIdentityAsync(CancellationToken ct = default) => LockedAsync(async token =>
    {
        var generation = sessionGeneration;
        var identity = await ReadIdentityCoreAsync(token);
        CheckGeneration(generation, token);
        if (CurrentIdentity is { } previous && previous != identity) throw AccountInputs.InvalidResponse();
        CurrentIdentity = identity;
        return identity;
    }, ct);

    public Task<AccountIdentity> LoginAsync(string email, string password, bool remembered, CancellationToken ct = default) =>
        LockedAsync(async token =>
        {
            AccountInputs.Email(email);
            AccountInputs.Password(password, registration: false);
            ClearMemory();
            await DeleteSecretAsync(AccountSecretSlots.Session, token);
            var generation = sessionGeneration;
            Guid? persistedSessionId = null;
            try
            {
                var login = await RequestAsync(HttpMethod.Post, "api/v1/auth/login", new LoginBody(email, password, remembered), null, token);
                Fields(login, "userId", "sessionId", "expiresAt");
                var userId = GuidValue(login, "userId");
                var sessionId = GuidValue(login, "sessionId");
                var expiresAt = DateValue(login, "expiresAt");
                var now = clock.GetUtcNow();
                if (expiresAt <= now || expiresAt > now.AddHours(remembered ? 168 : 8).AddMinutes(1)) throw AccountInputs.InvalidResponse();
                var identity = await ReadIdentityCoreAsync(token);
                CheckGeneration(generation, token);
                if (identity.UserId != userId) throw AccountInputs.InvalidResponse();
                if (remembered)
                {
                    if (secrets is null) throw StorageFailed();
                    var document = new SessionSecret(1, Origin.AbsoluteUri, identity.UserId, identity.PetId, sessionId,
                        expiresAt, CaptureCookies(expiresAt));
                    CheckGeneration(generation, token);
                    persistedSessionId = sessionId;
                    await WriteSecretAsync(AccountSecretSlots.Session, document, token);
                    CheckGeneration(generation, token);
                }
                CurrentIdentity = identity;
                return identity;
            }
            catch
            {
                ClearMemory();
                // Never retain a remembered login whose completion was cancelled or could not be validated.
                if (persistedSessionId is { } written) await DeleteOwnedSessionAsync(written);
                throw;
            }
        }, ct);

    public Task<AccountIdentity?> RestoreSessionAsync(CancellationToken ct = default) => LockedAsync<AccountIdentity?>(async token =>
    {
        ClearMemory();
        if (secrets is null) return null;
        var document = await ReadSecretAsync<SessionSecret>(AccountSecretSlots.Session, token);
        if (document is null) return null;
        try
        {
            if (document.SchemaVersion != 1 || document.Origin != Origin.AbsoluteUri || document.UserId == Guid.Empty
                || document.PetId == Guid.Empty || document.SessionId == Guid.Empty || document.Cookies is null)
                throw StorageInvalid();
            if (document.ExpiresAt <= clock.GetUtcNow())
            {
                await DeleteSecretAsync(AccountSecretSlots.Session, token);
                return null;
            }
            if (document.ExpiresAt > clock.GetUtcNow().AddHours(168).AddMinutes(1)) throw StorageInvalid();
            ValidateStoredCookies(document.Cookies, document.ExpiresAt);
            foreach (var cookie in document.Cookies)
                Cookies.Add(Origin, new Cookie(cookie.Name, cookie.Value, "/")
                { Secure = true, HttpOnly = true, Expires = cookie.ExpiresAt.UtcDateTime });
            var generation = sessionGeneration;
            var identity = await ReadIdentityCoreAsync(token);
            CheckGeneration(generation, token);
            if (identity != new AccountIdentity(document.UserId, document.PetId)) throw StorageInvalid();
            CurrentIdentity = identity;
            return identity;
        }
        catch (AccountClientException error) when (error.Failure == AccountFailure.Authentication)
        {
            ClearMemory();
            await DeleteSecretAsync(AccountSecretSlots.Session, CancellationToken.None);
            return null;
        }
        catch
        {
            ClearMemory();
            throw;
        }
    }, ct);

    public Task LogoutAsync(CancellationToken ct = default) => LockedAsync(async token =>
    {
        AccountClientException? storageFailure = null;
        AccountClientException? remoteFailure = null;
        var revoked = false;
        try
        {
            // Remove persistent credentials even when the server is unavailable.
            try { await DeleteSecretAsync(AccountSecretSlots.Session, CancellationToken.None); }
            catch (AccountClientException error) { storageFailure = error; }
            if (CurrentIdentity is not null)
            {
                try
                {
                    var result = await RequestAsync(HttpMethod.Post, "api/v1/auth/logout", null, null, token);
                    Status(result, "signed_out");
                    revoked = true;
                }
                catch (AccountClientException error) { remoteFailure = error; }
                catch (OperationCanceledException)
                { remoteFailure = new(AccountFailure.Unavailable, "request_outcome_unknown"); }
            }
        }
        finally { ClearMemory(); }
        if (storageFailure is not null)
            throw new AccountClientException(storageFailure.Failure, storageFailure.Code)
            { ServerRevocationConfirmed = revoked, LocalSecretCleared = false };
        if (remoteFailure is not null)
            throw new AccountClientException(remoteFailure.Failure, remoteFailure.Code)
            { ServerRevocationConfirmed = revoked, LocalSecretCleared = true };
        return true;
    }, ct);

    public IRemotePetConfigurationClient CreatePetClient(AccountIdentity identity)
    {
        ThrowIfDisposed();
        if (identity is null || identity.UserId == Guid.Empty || identity.PetId == Guid.Empty || CurrentIdentity != identity)
            throw AccountInputs.Rejected("pet_binding_changed");
        return new BoundPetClient(this, identity, sessionGeneration);
    }

    public Task<RegistrationStatus> StartRegistrationAsync(RegistrationAttempt attempt, CancellationToken ct = default) =>
        LockedAsync(async token =>
        {
            await SaveAttemptAsync(attempt, token);
            var result = await RequestAsync(HttpMethod.Post, "api/v1/auth/registrations",
                new RegistrationStartBody(attempt.Email, attempt.InvitationId, attempt.InvitationProof, attempt.RequestKey), attempt.ClientProof, token);
            var status = ParseRegistrationStatus(result);
            attempt.SetRegistrationId(status.Id);
            await SaveAttemptAsync(attempt, token);
            return status;
        }, ct);

    public Task<RegistrationStatus> ReadRegistrationStatusAsync(RegistrationAttempt attempt, CancellationToken ct = default) =>
        LockedAsync(token => RegistrationStatusCoreAsync(attempt, resend: false, token), ct);

    public Task<RegistrationStatus> ResendRegistrationAsync(RegistrationAttempt attempt, CancellationToken ct = default) =>
        LockedAsync(token => RegistrationStatusCoreAsync(attempt, resend: true, token), ct);

    public Task<RegistrationResult> CompleteRegistrationAsync(RegistrationAttempt attempt, string password,
        JsonElement appearance, CancellationToken ct = default) => LockedAsync(async token =>
    {
        RequireAttempt(attempt, requireId: true);
        AccountInputs.Password(password, registration: true);
        try { appearance = AppearanceContract.ValidateAndClone(appearance); }
        catch (AppearanceContractException) { throw AccountInputs.Rejected("configuration_invalid"); }
        await SaveAttemptAsync(attempt, token);
        var result = await RequestAsync(HttpMethod.Post, $"api/v1/auth/registrations/{attempt.RegistrationId:D}/complete",
            new RegistrationCompleteBody(attempt.CompletionKey, password, appearance), attempt.ClientProof, token);
        Fields(result, "userId", "petId", "roomId");
        return new RegistrationResult(GuidValue(result, "userId"), GuidValue(result, "petId"), GuidValue(result, "roomId"));
    }, ct);

    public Task<RegistrationAttempt?> RestoreRegistrationAsync(CancellationToken ct = default) =>
        LockedAsync<RegistrationAttempt?>(async token =>
        {
            if (secrets is null) return null;
            var document = await ReadSecretAsync<RegistrationSecret>(AccountSecretSlots.Registration, token);
            if (document is null) return null;
            try
            {
                if (document.SchemaVersion != 1 || document.Origin != Origin.AbsoluteUri) throw StorageInvalid();
                return RegistrationAttempt.Restore(document.Email, document.InvitationId, document.InvitationProof,
                    document.RequestKey, document.ClientProof, document.CompletionKey, document.RegistrationId, Origin);
            }
            catch (AccountClientException) { throw StorageInvalid(); }
        }, ct);

    public Task ForgetRegistrationAsync(CancellationToken ct = default) => LockedAsync(async token =>
    {
        await DeleteSecretAsync(AccountSecretSlots.Registration, token);
        return true;
    }, ct);

    public Task RequestPasswordResetAsync(string email, CancellationToken ct = default) => LockedAsync(async token =>
    {
        AccountInputs.Email(email);
        var result = await RequestAsync(HttpMethod.Post, "api/v1/auth/password-reset/request", new ResetBody(email), null, token);
        Status(result, "accepted");
        return true;
    }, ct);

    private async Task<RegistrationStatus> RegistrationStatusCoreAsync(RegistrationAttempt attempt, bool resend, CancellationToken ct)
    {
        RequireAttempt(attempt, requireId: true);
        var result = await RequestAsync(resend ? HttpMethod.Post : HttpMethod.Get,
            $"api/v1/auth/registrations/{attempt.RegistrationId:D}/{(resend ? "resend" : "status")}", null, attempt.ClientProof, ct);
        var status = ParseRegistrationStatus(result);
        if (status.Id != attempt.RegistrationId) throw AccountInputs.InvalidResponse();
        return status;
    }

    private async Task<AccountIdentity> ReadIdentityCoreAsync(CancellationToken ct)
    {
        var result = await RequestAsync(HttpMethod.Get, "api/v1/me", null, null, ct);
        Fields(result, "userId");
        var userId = GuidValue(result, "userId");
        var pet = ParsePet(await RequestAsync(HttpMethod.Get, "api/v1/me/pet", null, null, ct));
        if (pet.UserId != userId) throw AccountInputs.InvalidResponse();
        return new(userId, pet.PetId);
    }

    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, string? registrationProof, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string? token = null;
        if (method != HttpMethod.Get)
        {
            var protection = await RequestAsync(HttpMethod.Get, "api/v1/auth/request-protection", null, null, ct);
            Fields(protection, "requestToken");
            token = StringValue(protection, "requestToken");
            if (token.Length is < 16 or > 4096 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
                throw AccountInputs.InvalidResponse();
        }
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token);
        if (registrationProof is not null) request.Headers.Add("X-Registration-Proof", registrationProof);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            ct.ThrowIfCancellationRequested();
            if ((int)response.StatusCode is >= 300 and < 400) throw AccountInputs.InvalidResponse();
            if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout)
                throw new AccountClientException(AccountFailure.Unavailable, "request_outcome_unknown");
            var json = await ReadJsonAsync(response, ct);
            if (response.StatusCode != HttpStatusCode.OK) throw ParseError(response.StatusCode, json);
            return json;
        }
        catch (Exception error) when (error is HttpRequestException or IOException || error is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new AccountClientException(AccountFailure.Unavailable, "request_outcome_unknown"); }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes
            || response.Content.Headers.ContentType?.MediaType != "application/json") throw AccountInputs.InvalidResponse();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, deadline.Token);
            if (read == 0) break;
            if (bytes.Length + read > MaximumResponseBytes) throw AccountInputs.InvalidResponse();
            bytes.Write(buffer, 0, read);
        }
        try
        {
            using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            if (HasDuplicates(json.RootElement)) throw AccountInputs.InvalidResponse();
            return json.RootElement.Clone();
        }
        catch (JsonException) { throw AccountInputs.InvalidResponse(); }
    }

    private static bool HasDuplicates(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in input.EnumerateObject())
                if (!names.Add(item.Name) || HasDuplicates(item.Value)) return true;
        }
        else if (input.ValueKind == JsonValueKind.Array)
            foreach (var item in input.EnumerateArray()) if (HasDuplicates(item)) return true;
        return false;
    }

    private static AccountClientException ParseError(HttpStatusCode status, JsonElement json)
    {
        var code = "remote_request_rejected";
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("code", out var raw) && raw.ValueKind == JsonValueKind.String
            && raw.GetString() is { } candidate && ErrorCodes.Contains(candidate)) code = candidate;
        var failure = status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => AccountFailure.Authentication,
            HttpStatusCode.Conflict => AccountFailure.Conflict,
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout => AccountFailure.Unavailable,
            _ => AccountFailure.Rejected
        };
        if (failure == AccountFailure.Unavailable) code = "request_outcome_unknown";
        return new(failure, code);
    }

    private static RegistrationStatus ParseRegistrationStatus(JsonElement input)
    {
        Fields(input, "id", "state", "expiresAt", "completionExpiresAt", "emailStatus");
        var state = StringValue(input, "state") switch
        {
            "pending" => AccountRegistrationState.Pending,
            "verified" => AccountRegistrationState.Verified,
            "completed" => AccountRegistrationState.Completed,
            _ => throw AccountInputs.InvalidResponse()
        };
        var expiry = DateValue(input, "expiresAt");
        var completion = input.GetProperty("completionExpiresAt").ValueKind == JsonValueKind.Null
            ? (DateTimeOffset?)null : DateValue(input, "completionExpiresAt");
        var emailStatus = StringValue(input, "emailStatus");
        if (!EmailStatuses.Contains(emailStatus) || state == AccountRegistrationState.Pending && completion is not null
            || state != AccountRegistrationState.Pending && completion is null || completion > expiry) throw AccountInputs.InvalidResponse();
        return new(GuidValue(input, "id"), state, expiry, completion, emailStatus);
    }

    private static PetConfigurationSnapshot ParsePet(JsonElement input)
    {
        Fields(input, "userId", "petId", "configurationVersion", "configuration");
        var version = input.GetProperty("configurationVersion");
        if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt64(out var number) || number < 1) throw AccountInputs.InvalidResponse();
        try
        {
            return new(GuidValue(input, "userId"), GuidValue(input, "petId"), number,
                AppearanceContract.ValidateAndClone(input.GetProperty("configuration")));
        }
        catch (AppearanceContractException) { throw AccountInputs.InvalidResponse(); }
    }

    private static void Fields(JsonElement input, params string[] names)
    {
        if (input.ValueKind != JsonValueKind.Object) throw AccountInputs.InvalidResponse();
        var fields = input.EnumerateObject().ToArray();
        if (fields.Length != names.Length || fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).Count() != names.Length
            || fields.Any(field => !names.Contains(field.Name, StringComparer.Ordinal))) throw AccountInputs.InvalidResponse();
    }

    private static string StringValue(JsonElement input, string name) => input.GetProperty(name) is { ValueKind: JsonValueKind.String } value
        ? value.GetString()! : throw AccountInputs.InvalidResponse();
    private static Guid GuidValue(JsonElement input, string name) => input.GetProperty(name) is { ValueKind: JsonValueKind.String } value
        && value.TryGetGuid(out var id) && id != Guid.Empty ? id : throw AccountInputs.InvalidResponse();
    private static DateTimeOffset DateValue(JsonElement input, string name) => input.GetProperty(name) is { ValueKind: JsonValueKind.String } value
        && value.TryGetDateTimeOffset(out var date) ? date : throw AccountInputs.InvalidResponse();
    private static void Status(JsonElement input, string expected)
    {
        Fields(input, "status");
        if (StringValue(input, "status") != expected) throw AccountInputs.InvalidResponse();
    }

    private void RequireAttempt(RegistrationAttempt attempt, bool requireId)
    {
        if (attempt is null) throw AccountInputs.Rejected("registration_input_invalid");
        attempt.Bind(Origin);
        if (requireId && attempt.RegistrationId is null) throw AccountInputs.Rejected("registration_not_started");
    }

    private Task SaveAttemptAsync(RegistrationAttempt attempt, CancellationToken ct)
    {
        RequireAttempt(attempt, requireId: false);
        return WriteSecretAsync(AccountSecretSlots.Registration, new RegistrationSecret(1, Origin.AbsoluteUri, attempt.Email,
            attempt.InvitationId, attempt.InvitationProof, attempt.RequestKey, attempt.ClientProof, attempt.CompletionKey, attempt.RegistrationId), ct);
    }

    private async Task WriteSecretAsync<T>(string slot, T document, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (secrets is null) return;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        try
        {
            if (bytes.Length > MaximumSecretBytes) throw StorageFailed();
            await secrets.WriteAsync(Origin, slot, bytes, ct);
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();
        }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException and not AccountClientException)
        { throw StorageFailed(); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private async Task<T?> ReadSecretAsync<T>(string slot, CancellationToken ct) where T : class
    {
        if (secrets is null) return null;
        byte[]? bytes = null;
        try
        {
            bytes = await secrets.ReadAsync(Origin, slot, ct);
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (bytes is null) return null;
            if (bytes.Length is < 1 or > MaximumSecretBytes) throw StorageInvalid();
            return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw StorageInvalid();
        }
        catch (JsonException) { throw StorageInvalid(); }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException and not AccountClientException)
        { throw StorageFailed(); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private async Task DeleteSecretAsync(string slot, CancellationToken ct)
    {
        if (secrets is null) return;
        try { await secrets.DeleteAsync(Origin, slot, ct); }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        { throw StorageFailed(); }
    }

    private async Task DeleteOwnedSessionAsync(Guid sessionId)
    {
        if (secrets is null) return;
        byte[]? bytes = null;
        try
        {
            bytes = await secrets.ReadAsync(Origin, AccountSecretSlots.Session, CancellationToken.None);
            if (bytes is null || bytes.Length > MaximumSecretBytes) return;
            var current = JsonSerializer.Deserialize<SessionSecret>(bytes, Json);
            if (current?.Origin == Origin.AbsoluteUri && current.SessionId == sessionId)
                await secrets.DeleteAsync(Origin, AccountSecretSlots.Session, CancellationToken.None);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { throw StorageFailed(); }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    private StoredCookie[] CaptureCookies(DateTimeOffset sessionExpiry)
    {
        var cookies = Cookies.GetCookies(Origin).Cast<Cookie>().Where(cookie => AuthCookie(cookie.Name)).ToArray();
        if (cookies.Any(cookie => !cookie.Secure || !cookie.HttpOnly || cookie.Path != "/" || cookie.Expires == DateTime.MinValue
            || !string.Equals(cookie.Domain, Origin.IdnHost, StringComparison.OrdinalIgnoreCase)))
            throw AccountInputs.InvalidResponse();
        var stored = cookies.Select(cookie => new StoredCookie(cookie.Name, cookie.Value, new DateTimeOffset(cookie.Expires.ToUniversalTime()))).ToArray();
        ValidateStoredCookies(stored, sessionExpiry);
        return stored;
    }

    private void ValidateStoredCookies(StoredCookie[] cookies, DateTimeOffset sessionExpiry)
    {
        if (cookies.Length is < 1 or > 8 || cookies.Any(cookie => cookie is null) || !cookies.Any(cookie => cookie.Name == AuthCookieName)
            || cookies.Select(cookie => cookie.Name).Distinct(StringComparer.Ordinal).Count() != cookies.Length
            || cookies.Any(cookie => !AuthCookie(cookie.Name) || cookie.Value is null || cookie.Value.Length is < 1 or > 4096
                || cookie.Value.Any(c => !char.IsAscii(c) || char.IsControl(c) || c is ';' or ',' or ' ')
                || cookie.ExpiresAt <= clock.GetUtcNow() || cookie.ExpiresAt > sessionExpiry.AddMinutes(1))) throw StorageInvalid();
    }

    private static bool AuthCookie(string? name) => name == AuthCookieName || name is not null && name.StartsWith(AuthCookieName + "C", StringComparison.Ordinal)
        && name[(AuthCookieName.Length + 1)..] is { Length: > 0 and <= 2 } suffix && suffix.All(char.IsAsciiDigit);

    private async Task<T> LockedAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        ThrowIfDisposed();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        await gate.WaitAsync(linked.Token);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            return await action(linked.Token);
        }
        catch (AccountClientException error) when (error.Code == "authentication_required" && CurrentIdentity is not null)
        {
            ClearMemory();
            await DeleteSecretAsync(AccountSecretSlots.Session, CancellationToken.None);
            throw;
        }
        finally { gate.Release(); }
    }

    private void CheckGeneration(int generation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (generation != sessionGeneration) throw new OperationCanceledException(ct);
    }

    private void ClearMemory()
    {
        Interlocked.Increment(ref sessionGeneration);
        CurrentIdentity = null;
        foreach (Cookie cookie in Cookies.GetCookies(Origin)) cookie.Expired = true;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    private static AccountClientException StorageFailed() => new(AccountFailure.Storage, "secret_storage_failed");
    private static AccountClientException StorageInvalid() => new(AccountFailure.Storage, "secret_storage_invalid");

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        ClearMemory();
        http.Dispose();
        // Running actions still own their linked tokens and release the gate on completion.
    }

    private sealed class BoundPetClient(FormalAccountClient owner, AccountIdentity identity, int generation) : IRemotePetConfigurationClient
    {
        public Uri Origin => owner.Origin;
        public Guid UserId => identity.UserId;
        public Guid PetId => identity.PetId;

        public Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct) => ExecuteAsync(async token =>
            ParsePet(await owner.RequestAsync(HttpMethod.Get, "api/v1/me/pet", null, null, token)), ct);

        public Task<PetConfigurationSnapshot> UpdateAsync(long expectedVersion, JsonElement configuration, CancellationToken ct) =>
            ExecuteAsync(async token =>
            {
                if (expectedVersion is < 1 or long.MaxValue) throw new PetConfigurationChoiceException("expected_version_invalid");
                configuration = AppearanceContract.ValidateAndClone(configuration);
                return ParsePet(await owner.RequestAsync(HttpMethod.Patch, "api/v1/me/pet/configuration",
                    new PetConfigurationUpdate(expectedVersion, configuration), null, token));
            }, ct);

        private async Task<PetConfigurationSnapshot> ExecuteAsync(Func<CancellationToken, Task<PetConfigurationSnapshot>> operation, CancellationToken ct)
        {
            try
            {
                return await owner.LockedAsync(async token =>
                {
                    RequireBinding(token);
                    var result = await operation(token);
                    RequireBinding(token);
                    if (result.UserId != UserId || result.PetId != PetId) throw new PetConfigurationChoiceException("pet_binding_changed");
                    return result;
                }, ct);
            }
            catch (AccountClientException error)
            {
                throw new PetConfigurationHttpException(error.Failure switch
                {
                    AccountFailure.Authentication => PetHttpFailure.Authentication,
                    AccountFailure.Conflict => PetHttpFailure.Conflict,
                    AccountFailure.Unavailable => PetHttpFailure.Unavailable,
                    AccountFailure.InvalidResponse => PetHttpFailure.InvalidResponse,
                    _ => PetHttpFailure.Rejected
                }, error.Failure == AccountFailure.Unavailable ? "cloud_outcome_unknown" : error.Code);
            }
        }

        private void RequireBinding(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            owner.ThrowIfDisposed();
            if (generation != owner.sessionGeneration || owner.CurrentIdentity != identity)
                throw new PetConfigurationChoiceException("pet_binding_changed");
        }
    }

    private sealed record LoginBody(string Email, string Password, bool Remembered)
    { public override string ToString() => "LoginBody [redacted]"; }
    private sealed record ResetBody(string Email)
    { public override string ToString() => "ResetBody [redacted]"; }
    private sealed record RegistrationStartBody(string Email, Guid InvitationId, string InvitationProof, Guid RequestKey)
    { public override string ToString() => "RegistrationStartBody [redacted]"; }
    private sealed record RegistrationCompleteBody(Guid CompletionKey, string Password, JsonElement Appearance)
    { public override string ToString() => "RegistrationCompleteBody [redacted]"; }
    private sealed record SessionSecret(int SchemaVersion, string Origin, Guid UserId, Guid PetId, Guid SessionId,
        DateTimeOffset ExpiresAt, StoredCookie[] Cookies)
    { public override string ToString() => "SessionSecret [redacted]"; }
    private sealed record StoredCookie(string Name, string Value, DateTimeOffset ExpiresAt)
    { public override string ToString() => "StoredCookie [redacted]"; }
    private sealed record RegistrationSecret(int SchemaVersion, string Origin, string Email, Guid InvitationId,
        string InvitationProof, Guid RequestKey, string ClientProof, Guid CompletionKey, Guid? RegistrationId)
    { public override string ToString() => "RegistrationSecret [redacted]"; }
}
