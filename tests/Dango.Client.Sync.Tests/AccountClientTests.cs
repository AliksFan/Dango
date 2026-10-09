using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class AccountClientTests
{
    private static readonly Uri Origin = new("https://accounts.example.invalid/");
    private const string Email = "fictional@example.invalid";
    private const string Password = "fictional-account-password";
    private const string AuthCookie = "__Host-Dango.Auth";

    [TestMethod]
    [DataRow("http://accounts.example.invalid/")]
    [DataRow("https://user:secret@accounts.example.invalid/")]
    [DataRow("https://accounts.example.invalid/api/")]
    [DataRow("https://accounts.example.invalid/?server=other")]
    [DataRow("https://accounts.example.invalid/#secret")]
    [DataRow("relative")]
    public void UnsafeOriginsAreRejectedBeforeCreatingTransport(string input)
    {
        var error = Assert.ThrowsExactly<AccountClientException>(() => new FormalAccountClient(new Uri(input, UriKind.RelativeOrAbsolute)));
        Assert.AreEqual("server_origin_invalid", error.Code);
    }

    [TestMethod]
    public async Task LoginReadsServerIdentityAndPetUsesSameControlledCookiesAndFreshCsrf()
    {
        using var setup = new Setup();
        var identity = await setup.Client.LoginAsync(Email, Password, remembered: false);
        Assert.AreEqual(new AccountIdentity(setup.Receiver.UserId, setup.Receiver.PetId), identity);
        Assert.AreSame(setup.Cookies, setup.Client.Cookies);
        Assert.AreEqual(0, setup.Store.Values.Count);
        var pet = setup.Client.CreatePetClient(identity);
        var first = await pet.ReadAsync(CancellationToken.None);
        var updated = await pet.UpdateAsync(first.ConfigurationVersion, Configuration("#123456"), CancellationToken.None);
        Assert.AreEqual(first.ConfigurationVersion + 1, updated.ConfigurationVersion);
        var mutations = setup.Receiver.Requests.Where(item => item.Method != HttpMethod.Get).ToArray();
        Assert.AreEqual(2, mutations.Length);
        Assert.IsTrue(mutations.All(item => item.Csrf is not null));
        Assert.IsFalse(mutations[0].Csrf == mutations[1].Csrf, "Authentication changes cannot reuse the anonymous proof.");
        Assert.AreEqual("api/v1/me/pet/configuration", mutations[1].Path);
        Assert.IsTrue(setup.Receiver.Requests.Where(item => item.Path.StartsWith("api/v1/me", StringComparison.Ordinal)).All(item => item.HadAuth));
    }

    [TestMethod]
    public async Task ReadIdentityRejectsCrossAccountOrPetResponse()
    {
        using var setup = new Setup();
        await setup.Client.LoginAsync(Email, Password, remembered: false);
        setup.Receiver.Override = (request, _) => request.Path == "api/v1/me"
            ? Task.FromResult<HttpResponseMessage?>(Response(new { userId = Guid.NewGuid() })) : Task.FromResult<HttpResponseMessage?>(null);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.ReadIdentityAsync());
        Assert.AreEqual(AccountFailure.InvalidResponse, error.Failure);
        Assert.AreEqual("remote_response_invalid", error.Code);
    }

    [TestMethod]
    [DataRow("{\"userId\":\"wrong\"}")]
    [DataRow("{\"userId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [DataRow("{\"userId\":null}")]
    [DataRow("{\"userId\":\"a91ee2c0-75b5-4b8f-bc72-619dd4489444\",\"extra\":1}")]
    [DataRow("{\"userId\":\"a91ee2c0-75b5-4b8f-bc72-619dd4489444\",\"userId\":\"a91ee2c0-75b5-4b8f-bc72-619dd4489444\"}")]
    [DataRow("{\"UserId\":\"a91ee2c0-75b5-4b8f-bc72-619dd4489444\"}")]
    public async Task IdentityResponseRequiresExactFieldsAndNonemptyGuid(string json)
    {
        using var setup = new Setup();
        setup.Receiver.Override = (_, _) => Task.FromResult<HttpResponseMessage?>(RawResponse(json));
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.ReadIdentityAsync());
        Assert.AreEqual(AccountFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ResponseLimitRejectsDeclaredAndChunkedOversizedBodies(bool declaredLength)
    {
        using var setup = new Setup();
        setup.Receiver.Override = (_, _) =>
        {
            var response = RawResponse(new string(' ', 16385));
            if (!declaredLength) response.Content.Headers.ContentLength = null;
            return Task.FromResult<HttpResponseMessage?>(response);
        };
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.ReadIdentityAsync());
        Assert.AreEqual(AccountFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    public async Task RedirectIsRejectedWithoutFollowingOrForwardingCredentials()
    {
        using var setup = new Setup();
        setup.Receiver.Override = (request, _) =>
        {
            if (request.Path == "api/v1/auth/request-protection") return Task.FromResult<HttpResponseMessage?>(null);
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://untrusted.example.invalid/");
            return Task.FromResult<HttpResponseMessage?>(response);
        };
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.LoginAsync(Email, Password, false));
        Assert.AreEqual(AccountFailure.InvalidResponse, error.Failure);
        Assert.AreEqual(2, setup.Receiver.Requests.Count);
        Assert.IsTrue(setup.Receiver.Requests.All(item => item.Origin == Origin));
        Assert.IsNull(setup.Client.CurrentIdentity);
    }

    [TestMethod]
    [DataRow("https://private.example.invalid/password-secret", "remote_request_rejected")]
    [DataRow("login_failed", "login_failed")]
    public async Task ExternalErrorCodeNeverEchoesUntrustedContents(string external, string expected)
    {
        using var setup = new Setup();
        setup.Receiver.Override = (_, _) => Task.FromResult<HttpResponseMessage?>(Response(new { code = external, correlationId = "fictional" }, HttpStatusCode.Unauthorized));
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.ReadIdentityAsync());
        Assert.AreEqual(expected, error.Code);
        Assert.AreEqual(expected, error.Message);
        Assert.IsNull(error.InnerException);
    }

    [TestMethod]
    public async Task HtmlGatewayFailureRemainsUnknownOutcome()
    {
        using var setup = new Setup();
        setup.Receiver.Override = (_, _) => Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.BadGateway)
        { Content = new StringContent("fictional gateway detail", Encoding.UTF8, "text/html") });
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.RequestPasswordResetAsync(Email));
        Assert.AreEqual(AccountFailure.Unavailable, error.Failure);
        Assert.AreEqual("request_outcome_unknown", error.Code);
    }

    [TestMethod]
    public async Task RememberedSessionStoresOnlyOriginBoundAuthAndRestoresAfterServerValidation()
    {
        using var setup = new Setup();
        var identity = await setup.Client.LoginAsync(Email, Password, remembered: true);
        Assert.AreEqual(1, setup.Store.Values.Count);
        var bytes = setup.Store.Values[(Origin.AbsoluteUri, AccountSecretSlots.Session)];
        var stored = Encoding.UTF8.GetString(bytes);
        Assert.IsFalse(stored.Contains(Password, StringComparison.Ordinal));
        Assert.IsFalse(stored.Contains("requestToken", StringComparison.Ordinal));
        Assert.IsFalse(stored.Contains("__Host-Dango.Csrf", StringComparison.Ordinal));
        using var restored = new Setup(setup.Store, setup.Clock);
        restored.Receiver.UserId = setup.Receiver.UserId;
        restored.Receiver.PetId = setup.Receiver.PetId;
        Assert.AreEqual(identity, await restored.Client.RestoreSessionAsync());
        CollectionAssert.AreEqual(new[] { "api/v1/me", "api/v1/me/pet" }, restored.Receiver.Requests.Select(item => item.Path).ToArray());
        Assert.IsTrue(restored.Receiver.Requests.All(item => item.HadAuth));
    }

    [TestMethod]
    public async Task ExpiredRememberedSessionIsRemovedWithoutSendingCookie()
    {
        using var setup = new Setup();
        await setup.Client.LoginAsync(Email, Password, remembered: true);
        setup.Clock.Advance(TimeSpan.FromHours(168));
        using var restored = new Setup(setup.Store, setup.Clock);
        Assert.IsNull(await restored.Client.RestoreSessionAsync());
        Assert.AreEqual(0, restored.Receiver.Requests.Count);
        Assert.AreEqual(0, setup.Store.Values.Count);
    }

    [TestMethod]
    public async Task RestoredCookieIsRejectedWhenServerRevokedIt()
    {
        using var setup = new Setup();
        await setup.Client.LoginAsync(Email, Password, remembered: true);
        using var restored = new Setup(setup.Store, setup.Clock);
        restored.Receiver.Override = (_, _) => Task.FromResult<HttpResponseMessage?>(Response(new { code = "authentication_required" }, HttpStatusCode.Unauthorized));
        Assert.IsNull(await restored.Client.RestoreSessionAsync());
        Assert.IsNull(restored.Client.CurrentIdentity);
        Assert.AreEqual(0, setup.Store.Values.Count);
        Assert.IsNull(restored.Cookies.GetCookies(Origin)[AuthCookie]);
    }

    [TestMethod]
    public async Task CrossOriginRememberedBlobCannotSendCookies()
    {
        using var setup = new Setup();
        await setup.Client.LoginAsync(Email, Password, remembered: true);
        var other = new Uri("https://other.example.invalid/");
        setup.Store.Values[(other.AbsoluteUri, AccountSecretSlots.Session)] = setup.Store.Values[(Origin.AbsoluteUri, AccountSecretSlots.Session)].ToArray();
        var cookies = new CookieContainer();
        var receiver = new Receiver(other, cookies, setup.Clock);
        using var client = FormalAccountClient.ForTesting(other, receiver, cookies, setup.Store, setup.Clock);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => client.RestoreSessionAsync());
        Assert.AreEqual("secret_storage_invalid", error.Code);
        Assert.AreEqual(0, receiver.Requests.Count);
        Assert.AreEqual(0, cookies.GetCookies(other).Count);
    }

    [TestMethod]
    [DataRow("{\"schemaVersion\":1,\"unexpected\":\"fictional\"}")]
    [DataRow("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [DataRow("[]")]
    [DataRow("null")]
    public async Task InvalidSecretDocumentNeverAuthenticates(string json)
    {
        using var setup = new Setup();
        setup.Store.Values[(Origin.AbsoluteUri, AccountSecretSlots.Session)] = Encoding.UTF8.GetBytes(json);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.RestoreSessionAsync());
        Assert.AreEqual("secret_storage_invalid", error.Code);
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.AreEqual(0, setup.Receiver.Requests.Count);
    }

    [TestMethod]
    [DataRow("[null]")]
    [DataRow("[{\"name\":null,\"value\":\"fictional\",\"expiresAt\":\"2030-01-01T00:00:00Z\"}]")]
    [DataRow("[{\"name\":\"ordinary-cookie\",\"value\":\"fictional\",\"expiresAt\":\"2030-01-01T00:00:00Z\"}]")]
    [DataRow("[]")]
    public async Task InvalidStoredCookieElementsFailSafelyBeforeNetwork(string cookieJson)
    {
        using var setup = new Setup();
        await setup.Client.LoginAsync(Email, Password, remembered: true);
        var key = (Origin.AbsoluteUri, AccountSecretSlots.Session);
        var document = JsonNode.Parse(setup.Store.Values[key])!;
        document["cookies"] = JsonNode.Parse(cookieJson);
        setup.Store.Values[key] = JsonSerializer.SerializeToUtf8Bytes(document);
        using var restored = new Setup(setup.Store, setup.Clock);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => restored.Client.RestoreSessionAsync());
        Assert.AreEqual("secret_storage_invalid", error.Code);
        Assert.AreEqual(0, restored.Receiver.Requests.Count);
        Assert.IsNull(restored.Client.CurrentIdentity);
    }

    [TestMethod]
    public async Task ExpiredServerIdentityClearsRememberedStateAndInvalidatesAdapters()
    {
        using var setup = new Setup();
        var identity = await setup.Client.LoginAsync(Email, Password, true);
        var pet = setup.Client.CreatePetClient(identity);
        setup.Receiver.Override = (request, _) => request.Path == "api/v1/me/pet"
            ? Task.FromResult<HttpResponseMessage?>(Response(new { code = "authentication_required" }, HttpStatusCode.Unauthorized))
            : Task.FromResult<HttpResponseMessage?>(null);
        var error = await Assert.ThrowsExactlyAsync<PetConfigurationHttpException>(() => pet.ReadAsync(CancellationToken.None));
        Assert.AreEqual(PetHttpFailure.Authentication, error.Failure);
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.AreEqual(0, setup.Store.Values.Count);
        Assert.IsNull(setup.Cookies.GetCookies(Origin)[AuthCookie]);
    }

    [TestMethod]
    public async Task CookieExceedingWindowsSecretLimitIsRejectedWithoutTruncatingOrStoring()
    {
        using var setup = new Setup();
        setup.Receiver.AfterResponse = request =>
        {
            if (request.Path == "api/v1/auth/login")
                setup.Cookies.Add(Origin, new Cookie(AuthCookie, new string('x', 2500), "/")
                { Secure = true, HttpOnly = true, Expires = setup.Clock.GetUtcNow().AddHours(168).UtcDateTime });
        };
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.LoginAsync(Email, Password, true));
        Assert.AreEqual("secret_storage_failed", error.Code);
        Assert.AreEqual(0, setup.Store.Writes);
        Assert.IsNull(setup.Client.CurrentIdentity);
    }

    [TestMethod]
    public async Task FailedRememberedStoreDoesNotExposeSignedInStateOrRetainCookie()
    {
        using var setup = new Setup();
        setup.Store.FailWrite = true;
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.LoginAsync(Email, Password, remembered: true));
        Assert.AreEqual("secret_storage_failed", error.Code);
        Assert.IsNull(error.InnerException);
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.IsNull(setup.Cookies.GetCookies(Origin)[AuthCookie]);
        Assert.AreEqual(0, setup.Store.Values.Count);
    }

    [TestMethod]
    public async Task LogoutRemovesRememberedCookieEvenWhenServerResponseIsLostAndInvalidatesOldPet()
    {
        using var setup = new Setup();
        var identity = await setup.Client.LoginAsync(Email, Password, remembered: true);
        var oldPet = setup.Client.CreatePetClient(identity);
        setup.Receiver.Override = (request, _) => request.Path == "api/v1/auth/logout"
            ? Task.FromException<HttpResponseMessage?>(new HttpRequestException("fictional transport failure")) : Task.FromResult<HttpResponseMessage?>(null);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.LogoutAsync());
        Assert.AreEqual("request_outcome_unknown", error.Code);
        Assert.AreEqual(false, error.ServerRevocationConfirmed);
        Assert.AreEqual(true, error.LocalSecretCleared);
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.AreEqual(0, setup.Store.Values.Count);
        Assert.IsNull(setup.Cookies.GetCookies(Origin)[AuthCookie]);
        var calls = setup.Receiver.Requests.Count;
        var stale = await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(() => oldPet.ReadAsync(CancellationToken.None));
        Assert.AreEqual("pet_binding_changed", stale.Code);
        Assert.AreEqual(calls, setup.Receiver.Requests.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedLocalSecretDeletionStillAttemptsRevokeAndReportsEachOutcome(bool remoteFails)
    {
        using var setup = new Setup();
        await setup.Client.LoginAsync(Email, Password, true);
        setup.Store.FailDelete = true;
        if (remoteFails)
            setup.Receiver.Override = (request, _) => request.Path == "api/v1/auth/logout"
                ? Task.FromException<HttpResponseMessage?>(new HttpRequestException("fictional lost revoke response"))
                : Task.FromResult<HttpResponseMessage?>(null);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.LogoutAsync());
        Assert.AreEqual("secret_storage_failed", error.Code);
        Assert.AreEqual(!remoteFails, error.ServerRevocationConfirmed);
        Assert.AreEqual(false, error.LocalSecretCleared);
        Assert.IsTrue(setup.Receiver.Requests.Any(request => request.Path == "api/v1/auth/logout"));
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.IsNull(setup.Cookies.GetCookies(Origin)[AuthCookie]);
        Assert.AreEqual(1, setup.Store.Values.Count, "The failed protected-store removal must be reported, not hidden.");
    }

    [TestMethod]
    public async Task ReloginToSameIdentityDoesNotRevivePreviousPetAdapter()
    {
        using var setup = new Setup();
        var identity = await setup.Client.LoginAsync(Email, Password, remembered: false);
        var oldPet = setup.Client.CreatePetClient(identity);
        await setup.Client.LogoutAsync();
        var again = await setup.Client.LoginAsync(Email, Password, remembered: false);
        Assert.AreEqual(identity, again);
        await Assert.ThrowsExactlyAsync<PetConfigurationChoiceException>(() => oldPet.ReadAsync(CancellationToken.None));
        Assert.AreEqual(setup.Receiver.PetId, (await setup.Client.CreatePetClient(again).ReadAsync(CancellationToken.None)).PetId);
    }

    [TestMethod]
    public async Task LoginCancellationAfterNetworkReturnCannotSaveSessionOrIdentity()
    {
        using var setup = new Setup();
        using var cancellation = new CancellationTokenSource();
        setup.Receiver.AfterResponse = request =>
        {
            if (request.Path == "api/v1/me/pet") cancellation.Cancel();
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() => setup.Client.LoginAsync(Email, Password, true, cancellation.Token));
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.AreEqual(0, setup.Store.Values.Count);
        Assert.AreEqual(0, setup.Store.Writes);
    }

    [TestMethod]
    public async Task CancellationWhileSavingRememberedSessionRemovesOnlyItsOwnStoredResult()
    {
        using var setup = new Setup();
        using var cancellation = new CancellationTokenSource();
        setup.Store.AfterWrite = () => cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => setup.Client.LoginAsync(Email, Password, true, cancellation.Token));
        Assert.AreEqual(1, setup.Store.Writes);
        Assert.AreEqual(0, setup.Store.Values.Count);
        Assert.IsNull(setup.Client.CurrentIdentity);
    }

    [TestMethod]
    public async Task CancelledOldLoginBeforePersistenceCannotDeleteNewClientsRememberedSession()
    {
        using var first = new Setup();
        using var cancellation = new CancellationTokenSource();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Receiver.Override = async (request, _) =>
        {
            if (request.Path == "api/v1/me/pet")
            {
                arrived.SetResult();
                await release.Task;
                return first.Receiver.PetResponse();
            }
            return null;
        };
        var oldLogin = first.Client.LoginAsync(Email, Password, true, cancellation.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        using var second = new Setup(first.Store, first.Clock);
        var newerIdentity = await second.Client.LoginAsync("second@example.invalid", Password, true);
        var before = first.Store.Values[(Origin.AbsoluteUri, AccountSecretSlots.Session)].ToArray();
        release.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => oldLogin);
        Assert.AreEqual(newerIdentity, second.Client.CurrentIdentity);
        Assert.IsTrue(before.AsSpan().SequenceEqual(first.Store.Values[(Origin.AbsoluteUri, AccountSecretSlots.Session)]));
    }

    [TestMethod]
    public async Task MutationsAreSerializedAcrossFreshCsrfAndReceiverAwait()
    {
        using var setup = new Setup();
        var attempt = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Receiver.Override = async (request, _) =>
        {
            if (request.Path == "api/v1/auth/registrations")
            {
                arrived.SetResult();
                await release.Task;
            }
            return null;
        };
        var start = setup.Client.StartRegistrationAsync(attempt);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var reset = setup.Client.RequestPasswordResetAsync(Email);
        Assert.AreEqual(2, setup.Receiver.Requests.Count, "Another mutation cannot invalidate the active operation's proof.");
        release.SetResult();
        await Task.WhenAll(start, reset);
        var writes = setup.Receiver.Requests.Where(request => request.Method == HttpMethod.Post).ToArray();
        Assert.AreEqual(2, writes.Length);
        Assert.IsFalse(writes[0].Csrf == writes[1].Csrf);
    }

    [TestMethod]
    public async Task DisposedPendingLoginCannotAdoptIdentityOrWriteAfterIgnoringReceiverCancellation()
    {
        using var setup = new Setup();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Receiver.Override = async (request, _) =>
        {
            if (request.Path == "api/v1/me/pet")
            {
                arrived.SetResult();
                await release.Task;
                return setup.Receiver.PetResponse();
            }
            return null;
        };
        var login = setup.Client.LoginAsync(Email, Password, true);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        setup.Client.Dispose();
        release.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => login);
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.AreEqual(0, setup.Store.Writes);
    }

    [TestMethod]
    public async Task RegistrationRetriesPreserveProofAndKeysAfterLostStartAndCompleteResponses()
    {
        using var setup = new Setup();
        var attempt = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        var loseStart = true;
        var loseComplete = true;
        setup.Receiver.AfterResponse = request =>
        {
            if (request.Path == "api/v1/auth/registrations" && loseStart)
            {
                loseStart = false;
                throw new HttpRequestException("fictional lost start response");
            }
            if (request.Path.EndsWith("/complete", StringComparison.Ordinal) && loseComplete)
            {
                loseComplete = false;
                throw new HttpRequestException("fictional lost completion response");
            }
        };
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.StartRegistrationAsync(attempt));
        var restored = await setup.Client.RestoreRegistrationAsync();
        Assert.IsNotNull(restored);
        Assert.AreEqual(attempt.RequestKey, restored.RequestKey);
        Assert.AreEqual(attempt.CompletionKey, restored.CompletionKey);
        Assert.IsTrue(attempt.ClientProof == restored.ClientProof);
        await setup.Client.StartRegistrationAsync(restored);
        Assert.AreEqual(setup.Receiver.PendingId, restored.RegistrationId);
        await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.CompleteRegistrationAsync(restored, Password, Configuration()));
        var result = await setup.Client.CompleteRegistrationAsync(restored, Password, Configuration());
        Assert.AreEqual(setup.Receiver.UserId, result.UserId);
        var starts = setup.Receiver.Requests.Where(item => item.Path == "api/v1/auth/registrations").ToArray();
        Assert.AreEqual(2, starts.Length);
        Assert.IsTrue(starts.All(item => item.Proof == attempt.ClientProof));
        Assert.IsTrue(starts.All(item => item.Body!.Value.GetProperty("requestKey").GetGuid() == attempt.RequestKey));
        var completions = setup.Receiver.Requests.Where(item => item.Path.EndsWith("/complete", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(2, completions.Length);
        Assert.IsTrue(completions.All(item => item.Body!.Value.GetProperty("completionKey").GetGuid() == attempt.CompletionKey));
        Assert.IsFalse(setup.Store.Values.Values.Any(value => Encoding.UTF8.GetString(value).Contains(Password, StringComparison.Ordinal)));
        Assert.IsNull(setup.Client.CurrentIdentity, "Registration completion does not authenticate the client.");
        Assert.IsNull(setup.Cookies.GetCookies(Origin)[AuthCookie]);
    }

    [TestMethod]
    public async Task RegistrationStatusAndResendRequireContinuationAndDoNotInventEmailVerification()
    {
        using var setup = new Setup();
        var attempt = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        await setup.Client.StartRegistrationAsync(attempt);
        var status = await setup.Client.ReadRegistrationStatusAsync(attempt);
        Assert.AreEqual(AccountRegistrationState.Pending, status.State);
        Assert.AreEqual("smtp_accepted", status.EmailStatus);
        var resent = await setup.Client.ResendRegistrationAsync(attempt);
        Assert.AreEqual(AccountRegistrationState.Pending, resent.State);
        Assert.IsTrue(setup.Receiver.Requests.Where(item => item.Path.Contains("/registrations", StringComparison.Ordinal)).All(item => item.Proof == attempt.ClientProof));
        setup.Receiver.RegistrationState = "verified";
        Assert.AreEqual(AccountRegistrationState.Verified, (await setup.Client.ReadRegistrationStatusAsync(attempt)).State);
    }

    [TestMethod]
    [DataRow("smtp_not_configured")]
    [DataRow("smtp_permanent")]
    [DataRow("smtp_attempt_limit")]
    [DataRow("smtp_transient")]
    [DataRow("email_challenge_inactive")]
    [DataRow("smtp_send_failed")]
    public async Task KnownEmailFailureCanBeShownWithoutBecomingInvalidProtocol(string status)
    {
        using var setup = new Setup();
        setup.Receiver.EmailStatus = status;
        var attempt = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        var result = await setup.Client.StartRegistrationAsync(attempt);
        Assert.AreEqual(status, result.EmailStatus);
        Assert.AreEqual(AccountRegistrationState.Pending, result.State);
    }

    [TestMethod]
    public async Task RegistrationPersistenceFailureBlocksServerSideEffects()
    {
        using var setup = new Setup();
        setup.Store.FailWrite = true;
        var attempt = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.StartRegistrationAsync(attempt));
        Assert.AreEqual("secret_storage_failed", error.Code);
        Assert.AreEqual(0, setup.Receiver.Requests.Count);
    }

    [TestMethod]
    public async Task RegistrationAttemptCannotSwitchOriginOrCallStatusBeforeStart()
    {
        using var setup = new Setup();
        var attempt = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.ReadRegistrationStatusAsync(attempt));
        Assert.AreEqual("registration_not_started", error.Code);
        Assert.AreEqual(0, setup.Receiver.Requests.Count);
        await setup.Client.StartRegistrationAsync(attempt);
        var other = new Uri("https://other.example.invalid/");
        var otherCookies = new CookieContainer();
        var receiver = new Receiver(other, otherCookies, setup.Clock);
        using var client = FormalAccountClient.ForTesting(other, receiver, otherCookies, setup.Store, setup.Clock);
        var cross = await Assert.ThrowsExactlyAsync<AccountClientException>(() => client.StartRegistrationAsync(attempt));
        Assert.AreEqual("registration_binding_changed", cross.Code);
        Assert.AreEqual(0, receiver.Requests.Count);
    }

    [TestMethod]
    public async Task InvalidCompletionInputsAreRejectedBeforeSendingAndForgetDeletesContinuation()
    {
        using var setup = new Setup();
        var attempt = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        await setup.Client.StartRegistrationAsync(attempt);
        var before = setup.Receiver.Requests.Count;
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.CompleteRegistrationAsync(attempt, "short", Configuration()));
        Assert.AreEqual("password_invalid", error.Code);
        using var bad = JsonDocument.Parse("{}");
        var appearance = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.CompleteRegistrationAsync(attempt, Password, bad.RootElement));
        Assert.AreEqual("configuration_invalid", appearance.Code);
        Assert.AreEqual(before, setup.Receiver.Requests.Count);
        await setup.Client.ForgetRegistrationAsync();
        Assert.IsNull(await setup.Client.RestoreRegistrationAsync());
    }

    [TestMethod]
    public async Task ForgotPasswordShowsOnlyRequestAcceptanceAndUsesCsrf()
    {
        using var setup = new Setup();
        await setup.Client.RequestPasswordResetAsync(Email);
        var request = setup.Receiver.Requests.Last();
        Assert.AreEqual("api/v1/auth/password-reset/request", request.Path);
        Assert.IsNotNull(request.Csrf);
        Assert.IsNull(setup.Client.CurrentIdentity);
        Assert.AreEqual(0, setup.Store.Values.Count);
    }

    [TestMethod]
    [DataRow("not-an-email")]
    [DataRow(" leading@example.invalid")]
    [DataRow("Display <fictional@example.invalid>")]
    public async Task InvalidEmailCannotReachServer(string email)
    {
        using var setup = new Setup();
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.LoginAsync(email, Password, false));
        Assert.AreEqual("email_invalid", error.Code);
        Assert.AreEqual(0, setup.Receiver.Requests.Count);
    }

    [TestMethod]
    public void RegistrationSecretIsCanonicalRandomAndToStringIsRedacted()
    {
        var first = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        var second = RegistrationAttempt.Create(Email, Guid.NewGuid(), AccountInputs.NewProof());
        Assert.AreEqual(43, first.ClientProof.Length);
        Assert.IsFalse(first.ClientProof == second.ClientProof);
        AccountInputs.Proof(first.ClientProof);
        Assert.AreEqual("RegistrationAttempt [redacted]", first.ToString());
        Assert.IsFalse(first.ToString().Contains(Email, StringComparison.Ordinal));
        var invalid = Assert.ThrowsExactly<AccountClientException>(() => RegistrationAttempt.Create(Email, Guid.NewGuid(), new string('a', 43)));
        Assert.AreEqual("proof_invalid", invalid.Code);
    }

    private static JsonElement Configuration(string color = "#FFFFFF")
    {
        using var document = JsonDocument.Parse(PetConfigurationChoiceTests.AppearanceJson.Replace("#FFFFFF", color, StringComparison.Ordinal));
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage Response(object body, HttpStatusCode status = HttpStatusCode.OK) => RawResponse(JsonSerializer.Serialize(body), status);
    private static HttpResponseMessage RawResponse(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Setup : IDisposable
    {
        public Setup(SecretStore? store = null, Clock? clock = null)
        {
            Store = store ?? new SecretStore();
            Clock = clock ?? new Clock();
            Cookies = new CookieContainer();
            Receiver = new Receiver(Origin, Cookies, Clock);
            Client = FormalAccountClient.ForTesting(Origin, Receiver, Cookies, Store, Clock);
        }
        public SecretStore Store { get; }
        public Clock Clock { get; }
        public CookieContainer Cookies { get; }
        public Receiver Receiver { get; }
        public FormalAccountClient Client { get; }
        public void Dispose() => Client.Dispose();
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class SecretStore : IAccountSecretStore
    {
        public Dictionary<(string Origin, string Slot), byte[]> Values { get; } = [];
        public bool FailWrite { get; set; }
        public bool FailDelete { get; set; }
        public Action? AfterWrite { get; set; }
        public int Writes { get; private set; }
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) =>
            Task.FromResult(Values.TryGetValue((origin.AbsoluteUri, slot), out var value) ? value.ToArray() : null);
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        {
            if (FailWrite) throw new IOException("fictional untrusted storage detail");
            ct.ThrowIfCancellationRequested();
            Writes++;
            Values[(origin.AbsoluteUri, slot)] = value.ToArray();
            AfterWrite?.Invoke();
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        {
            if (FailDelete) throw new IOException("fictional protected-store removal failure");
            Values.Remove((origin.AbsoluteUri, slot));
            return Task.CompletedTask;
        }
    }

    private sealed record RequestRecord(Uri Origin, HttpMethod Method, string Path, JsonElement? Body, string? Csrf, string? Proof, bool HadAuth)
    { public override string ToString() => "RequestRecord [redacted]"; }

    private sealed class Receiver(Uri origin, CookieContainer cookies, Clock clock) : HttpMessageHandler
    {
        private int csrfGeneration;
        private string csrf = string.Empty;
        public Guid UserId { get; set; } = Guid.NewGuid();
        public Guid PetId { get; set; } = Guid.NewGuid();
        public Guid RoomId { get; } = Guid.NewGuid();
        public Guid PendingId { get; } = Guid.NewGuid();
        public string RegistrationState { get; set; } = "pending";
        public string EmailStatus { get; set; } = "smtp_accepted";
        public long Version { get; private set; } = 1;
        public JsonElement Appearance { get; private set; } = Configuration();
        public List<RequestRecord> Requests { get; } = [];
        public Func<RequestRecord, CancellationToken, Task<HttpResponseMessage?>>? Override { get; set; }
        public Action<RequestRecord>? AfterResponse { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            JsonElement? body = null;
            if (request.Content is not null)
            {
                using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                body = document.RootElement.Clone();
            }
            var url = request.RequestUri!;
            var observed = new RequestRecord(new Uri(url.GetLeftPart(UriPartial.Authority) + "/"), request.Method,
                url.AbsolutePath.TrimStart('/'), body, Header(request, "X-CSRF-TOKEN"), Header(request, "X-Registration-Proof"),
                cookies.GetCookies(origin)[AuthCookie] is not null);
            Requests.Add(observed);
            var response = Override is null ? null : await Override(observed, ct);
            response ??= DefaultResponse(observed);
            AfterResponse?.Invoke(observed);
            return response;
        }

        public HttpResponseMessage PetResponse() => Response(new
        { userId = UserId, petId = PetId, configurationVersion = Version, configuration = Appearance });

        private HttpResponseMessage DefaultResponse(RequestRecord request)
        {
            if (request.Path == "api/v1/auth/request-protection")
            {
                csrf = "fictional_csrf_proof_for_request_" + ++csrfGeneration;
                cookies.Add(origin, new Cookie("__Host-Dango.Csrf", "fictional-csrf-cookie", "/") { Secure = true, HttpOnly = true });
                return Response(new { requestToken = csrf });
            }
            if (request.Method != HttpMethod.Get && request.Csrf != csrf)
                return Response(new { code = "request_protection_invalid" }, HttpStatusCode.Forbidden);
            if (request.Path.StartsWith("api/v1/me", StringComparison.Ordinal) && !request.HadAuth)
                return Response(new { code = "authentication_required" }, HttpStatusCode.Unauthorized);
            if (request.Path == "api/v1/auth/login")
            {
                var remembered = request.Body!.Value.GetProperty("remembered").GetBoolean();
                var expiry = clock.GetUtcNow().AddHours(remembered ? 168 : 8);
                cookies.Add(origin, new Cookie(AuthCookie, "fictional-auth-cookie", "/")
                { Secure = true, HttpOnly = true, Expires = remembered ? expiry.UtcDateTime : DateTime.MinValue });
                return Response(new { userId = UserId, sessionId = Guid.NewGuid(), expiresAt = expiry });
            }
            if (request.Path == "api/v1/auth/logout") return Response(new { status = "signed_out" });
            if (request.Path == "api/v1/me") return Response(new { userId = UserId });
            if (request.Path == "api/v1/me/pet") return PetResponse();
            if (request.Path == "api/v1/me/pet/configuration")
            {
                var expected = request.Body!.Value.GetProperty("expectedVersion").GetInt64();
                if (expected != Version) return Response(new { code = "pet_configuration_conflict" }, HttpStatusCode.Conflict);
                Appearance = request.Body.Value.GetProperty("configuration").Clone();
                Version++;
                return PetResponse();
            }
            if (request.Path == "api/v1/auth/registrations" || request.Path.EndsWith("/status", StringComparison.Ordinal)
                || request.Path.EndsWith("/resend", StringComparison.Ordinal))
            {
                Assert.IsNotNull(request.Proof);
                return Response(new
                {
                    id = PendingId,
                    state = RegistrationState,
                    expiresAt = clock.GetUtcNow().AddHours(1),
                    completionExpiresAt = RegistrationState == "pending" ? (DateTimeOffset?)null : clock.GetUtcNow().AddMinutes(10),
                    emailStatus = EmailStatus
                });
            }
            if (request.Path.EndsWith("/complete", StringComparison.Ordinal))
                return Response(new { userId = UserId, petId = PetId, roomId = RoomId });
            if (request.Path == "api/v1/auth/password-reset/request") return Response(new { status = "accepted" });
            throw new InvalidOperationException("Unexpected controlled route.");
        }

        private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }
}
