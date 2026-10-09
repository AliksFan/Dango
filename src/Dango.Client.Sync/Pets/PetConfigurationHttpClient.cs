using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dango.Protocol;

namespace Dango.Client.Sync.Pets;

public enum PetHttpFailure { Conflict, Authentication, Unavailable, InvalidResponse, Rejected }
public sealed class PetConfigurationHttpException(PetHttpFailure failure, string code) : PetConfigurationChoiceException(code)
{
    public PetHttpFailure Failure { get; } = failure;
}

public sealed class PetConfigurationHttpClient : IRemotePetConfigurationClient, IDisposable
{
    private const int MaximumResponseBytes = 16384;
    private readonly HttpClient client;

    public PetConfigurationHttpClient(Uri origin, Guid userId, Guid petId, CookieContainer? cookies = null)
        : this(origin, userId, petId, CreateHandler(origin, cookies)) { }

    private PetConfigurationHttpClient(Uri origin, Guid userId, Guid petId, HttpMessageHandler handler)
    {
        Origin = ValidateOrigin(origin);
        if (userId == Guid.Empty || petId == Guid.Empty) throw new PetConfigurationChoiceException("pet_binding_invalid");
        UserId = userId;
        PetId = petId;
        client = new HttpClient(handler) { BaseAddress = Origin, Timeout = TimeSpan.FromSeconds(20) };
    }

    // Explicit seam for controlled receivers/handlers; production construction always validates TLS normally.
    public static PetConfigurationHttpClient ForTesting(Uri origin, Guid userId, Guid petId, HttpMessageHandler handler) =>
        new(origin, userId, petId, handler);

    public Uri Origin { get; }
    public Guid UserId { get; }
    public Guid PetId { get; }

    public async Task<PetConfigurationSnapshot> ReadAsync(CancellationToken ct)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/v1/me/pet"), ct);
        RequireSuccess(response);
        return ParseSnapshot(await ReadJsonAsync(response, ct));
    }

    public async Task<PetConfigurationSnapshot> UpdateAsync(long expectedVersion, JsonElement configuration, CancellationToken ct)
    {
        if (expectedVersion is < 1 or long.MaxValue) throw new PetConfigurationChoiceException("expected_version_invalid");
        configuration = AppearanceContract.ValidateAndClone(configuration);
        using var csrfResponse = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "api/v1/auth/request-protection"), ct);
        RequireSuccess(csrfResponse);
        var protection = await ReadJsonAsync(csrfResponse, ct);
        RequireFields(protection, ["requestToken"]);
        var tokenValue = protection.GetProperty("requestToken");
        var token = tokenValue.ValueKind == JsonValueKind.String ? tokenValue.GetString() : null;
        if (token is null || token.Length is < 16 or > 4096 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw InvalidResponse();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "api/v1/me/pet/configuration")
        {
            Content = JsonContent.Create(new PetConfigurationUpdate(expectedVersion, configuration))
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var response = await SendAsync(request, ct);
        RequireSuccess(response);
        return ParseSnapshot(await ReadJsonAsync(response, ct));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using (request)
            try { return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            { throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown"); }
    }

    private PetConfigurationSnapshot ParseSnapshot(JsonElement input)
    {
        RequireFields(input, ["userId", "petId", "configurationVersion", "configuration"]);
        var user = input.GetProperty("userId");
        var pet = input.GetProperty("petId");
        var version = input.GetProperty("configurationVersion");
        if (user.ValueKind != JsonValueKind.String || !user.TryGetGuid(out var userId)
            || pet.ValueKind != JsonValueKind.String || !pet.TryGetGuid(out var petId)
            || version.ValueKind != JsonValueKind.Number || !version.TryGetInt64(out var configurationVersion) || configurationVersion < 1)
            throw InvalidResponse();
        if (userId != UserId || petId != PetId) throw new PetConfigurationChoiceException("pet_binding_changed");
        try { return new(userId, petId, configurationVersion, AppearanceContract.ValidateAndClone(input.GetProperty("configuration"))); }
        catch (AppearanceContractException) { throw InvalidResponse(); }
    }

    private static void RequireSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.OK) return;
        throw response.StatusCode switch
        {
            HttpStatusCode.Conflict => new PetConfigurationHttpException(PetHttpFailure.Conflict, "pet_configuration_conflict"),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new PetConfigurationHttpException(PetHttpFailure.Authentication, "authentication_required"),
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout
                => new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown"),
            _ => new PetConfigurationHttpException(PetHttpFailure.Rejected, "remote_request_rejected")
        };
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw InvalidResponse();
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token);
                if (read == 0) break;
                if (bytes.Length + read > MaximumResponseBytes) throw InvalidResponse();
                bytes.Write(buffer, 0, read);
            }
            using var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
            return json.RootElement.Clone();
        }
        catch (JsonException) { throw InvalidResponse(); }
        catch (Exception ex) when (ex is IOException or HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { throw new PetConfigurationHttpException(PetHttpFailure.Unavailable, "cloud_outcome_unknown"); }
    }

    private static void RequireFields(JsonElement input, string[] fields)
    {
        if (input.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        var properties = input.EnumerateObject().ToArray();
        if (properties.Length != fields.Length || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length
            || properties.Any(p => !fields.Contains(p.Name, StringComparer.Ordinal))) throw InvalidResponse();
    }

    internal static Uri ValidateOrigin(Uri origin)
    {
        if (!origin.IsAbsoluteUri || origin.Scheme != "https" || origin.Host.Length == 0 || origin.UserInfo.Length != 0
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw new PetConfigurationChoiceException("server_origin_invalid");
        return new Uri(origin.GetLeftPart(UriPartial.Authority) + "/");
    }

    private static HttpClientHandler CreateHandler(Uri origin, CookieContainer? source)
    {
        origin = ValidateOrigin(origin);
        var cookies = new CookieContainer();
        if (source is not null)
            foreach (Cookie cookie in source.GetCookies(origin))
                cookies.Add(origin, new Cookie(cookie.Name, cookie.Value, cookie.Path)
                {
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly,
                    Expires = cookie.Expires,
                    Discard = cookie.Discard,
                    Version = cookie.Version
                });
        return new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false, UseCookies = true };
    }

    private static PetConfigurationHttpException InvalidResponse() => new(PetHttpFailure.InvalidResponse, "remote_response_invalid");
    public void Dispose() => client.Dispose();
}
