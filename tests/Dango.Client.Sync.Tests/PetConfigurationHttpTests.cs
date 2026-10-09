using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class PetConfigurationHttpTests
{
    private static readonly Uri Origin = new("https://formal.example.invalid/");

    [TestMethod]
    [DataRow("http://formal.example.invalid/")]
    [DataRow("https://user@formal.example.invalid/")]
    [DataRow("https://formal.example.invalid/api")]
    [DataRow("https://formal.example.invalid/?proof=fictional")]
    [DataRow("https://formal.example.invalid/#fragment")]
    public void UnsafeOriginsNeverConstructAClient(string value)
    {
        var error = Assert.ThrowsExactly<PetConfigurationChoiceException>(() => new PetConfigurationHttpClient(new(value), Guid.NewGuid(), Guid.NewGuid()));
        Assert.AreEqual("server_origin_invalid", error.Code);
    }

    [TestMethod]
    public async Task BoundReadAndPatchUseFixedRoutesAndFreshCsrfHeaderWithoutIdentityPayload()
    {
        var snapshot = Snapshot();
        var handler = new Handler();
        handler.Responses.Enqueue(_ => Json(snapshot));
        handler.Responses.Enqueue(_ => Json(new { requestToken = "fictional_csrf_request_token" }));
        handler.Responses.Enqueue(_ => Json(snapshot with { ConfigurationVersion = 2 }));
        using var client = PetConfigurationHttpClient.ForTesting(Origin, snapshot.UserId, snapshot.PetId, handler);
        var read = await client.ReadAsync(CancellationToken.None);
        Assert.AreEqual(snapshot.UserId, read.UserId);
        var changed = await client.UpdateAsync(1, snapshot.Configuration, CancellationToken.None);
        Assert.AreEqual(2L, changed.ConfigurationVersion);
        CollectionAssert.AreEqual(new[] { "GET /api/v1/me/pet", "GET /api/v1/auth/request-protection", "PATCH /api/v1/me/pet/configuration" }, handler.Requests.Select(r => r.Route).ToArray());
        Assert.AreEqual("fictional_csrf_request_token", handler.Requests[2].Csrf);
        using var body = JsonDocument.Parse(handler.Requests[2].Body!);
        CollectionAssert.AreEquivalent(new[] { "expectedVersion", "configuration" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.AreEqual(1L, body.RootElement.GetProperty("expectedVersion").GetInt64());
        Assert.IsTrue(handler.Requests.All(r => r.Uri.GetLeftPart(UriPartial.Authority) == Origin.GetLeftPart(UriPartial.Authority)));
    }

    [TestMethod]
    public async Task WrongUserAndPetResponsesAreRejectedWithoutAnotherRequest()
    {
        foreach (var wrongPet in new[] { false, true })
        {
            var snapshot = Snapshot();
            var handler = new Handler();
            handler.Responses.Enqueue(_ => Json(wrongPet ? snapshot with { PetId = Guid.NewGuid() } : snapshot with { UserId = Guid.NewGuid() }));
            using var client = PetConfigurationHttpClient.ForTesting(Origin, snapshot.UserId, snapshot.PetId, handler);
            var error = await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(() => client.ReadAsync(CancellationToken.None));
            Assert.AreEqual("pet_binding_changed", error.Code);
            Assert.AreEqual(1, handler.Requests.Count);
        }
    }

    [TestMethod]
    public async Task RedirectAuthenticationAndServerErrorsHaveStableNonEchoingSemantics()
    {
        foreach (var (status, kind) in new[]
        {
            (HttpStatusCode.Redirect, PetHttpFailure.Rejected), (HttpStatusCode.Unauthorized, PetHttpFailure.Authentication),
            (HttpStatusCode.Forbidden, PetHttpFailure.Authentication), (HttpStatusCode.Conflict, PetHttpFailure.Conflict),
            (HttpStatusCode.ServiceUnavailable, PetHttpFailure.Unavailable)
        })
        {
            var snapshot = Snapshot();
            var handler = new Handler();
            handler.Responses.Enqueue(_ => new(status) { Content = new StringContent("external_secret_marker"), Headers = { Location = new("https://foreign.example.invalid/") } });
            using var client = PetConfigurationHttpClient.ForTesting(Origin, snapshot.UserId, snapshot.PetId, handler);
            var error = await Assert.ThrowsExactlyAsync<PetConfigurationHttpException>(() => client.ReadAsync(CancellationToken.None));
            Assert.AreEqual(kind, error.Failure);
            Assert.IsFalse(error.Code.Contains("external_secret_marker", StringComparison.Ordinal));
            Assert.AreEqual(1, handler.Requests.Count);
        }
    }

    [TestMethod]
    public async Task DuplicateUnknownMalformedVersionAndAppearanceResponsesAreNeverDefaulted()
    {
        var snapshot = Snapshot();
        var valid = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        foreach (var json in new[]
        {
            "null", "[]", "{}", valid[..^1] + ",\"password\":\"external_secret_marker\"}",
            valid[..^1] + ",\"configurationVersion\":1}", valid.Replace("\"configurationVersion\":1", "\"configurationVersion\":0", StringComparison.Ordinal),
            valid.Replace("\"configurationVersion\":1", "\"configurationVersion\":\"1\"", StringComparison.Ordinal),
            valid.Replace("\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":4", StringComparison.Ordinal),
            valid.Replace("\"eyeSizePercent\":100", "\"eyeSizePercent\":1e400", StringComparison.Ordinal),
            valid.Replace("\"eyeSizePercent\":100", "\"eyeSizePercent\":100,\"eyeSizePercent\":100", StringComparison.Ordinal),
            new string(' ', 20000) + valid
        })
        {
            var handler = new Handler();
            handler.Responses.Enqueue(_ => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            using var client = PetConfigurationHttpClient.ForTesting(Origin, snapshot.UserId, snapshot.PetId, handler);
            var error = await Assert.ThrowsExactlyAsync<PetConfigurationHttpException>(() => client.ReadAsync(CancellationToken.None));
            Assert.AreEqual(PetHttpFailure.InvalidResponse, error.Failure);
        }
    }

    [TestMethod]
    public async Task InvalidCsrfResponseAndInvalidInputBlockPatch()
    {
        var snapshot = Snapshot();
        foreach (var token in new object?[] { null, "", "fictional\r\nInjected: yes", 123 })
        {
            var handler = new Handler();
            handler.Responses.Enqueue(_ => Json(new { requestToken = token }));
            using var client = PetConfigurationHttpClient.ForTesting(Origin, snapshot.UserId, snapshot.PetId, handler);
            await Assert.ThrowsExactlyAsync<PetConfigurationHttpException>(() => client.UpdateAsync(1, snapshot.Configuration, CancellationToken.None));
            Assert.AreEqual(1, handler.Requests.Count);
            Assert.IsTrue(handler.Requests.All(r => r.Route.StartsWith("GET", StringComparison.Ordinal)));
        }
        var unused = new Handler();
        using var invalid = PetConfigurationHttpClient.ForTesting(Origin, snapshot.UserId, snapshot.PetId, unused);
        await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(() => invalid.UpdateAsync(0, snapshot.Configuration, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AppearanceContractException>(() => invalid.UpdateAsync(1, PetConfigurationChoiceTests.Configuration("invalid"), CancellationToken.None));
        Assert.AreEqual(0, unused.Requests.Count);
    }

    [TestMethod]
    public async Task LostNetworkResponseMapsToUnknownOutcomeWithoutLeakingExceptionText()
    {
        var snapshot = Snapshot();
        var handler = new Handler();
        handler.Responses.Enqueue(_ => throw new HttpRequestException("external_secret_marker"));
        using var client = PetConfigurationHttpClient.ForTesting(Origin, snapshot.UserId, snapshot.PetId, handler);
        var error = await Assert.ThrowsExactlyAsync<PetConfigurationHttpException>(() => client.ReadAsync(CancellationToken.None));
        Assert.AreEqual(PetHttpFailure.Unavailable, error.Failure);
        Assert.IsFalse(error.Message.Contains("external_secret_marker", StringComparison.Ordinal));
    }

    private static PetConfigurationSnapshot Snapshot() => new(Guid.NewGuid(), Guid.NewGuid(), 1, PetConfigurationChoiceTests.Configuration());
    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    private sealed record Request(Uri Uri, string Route, string? Csrf, string? Body);
    private sealed class Handler : HttpMessageHandler
    {
        public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses { get; } = new();
        public List<Request> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(new(request.RequestUri!, request.Method.Method + " " + request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-CSRF-TOKEN", out var token) ? token.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return Responses.Dequeue()(request);
        }
    }
}
