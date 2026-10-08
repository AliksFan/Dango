using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dango.Server.Accounts;
using Dango.Server.Email;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MimeKit;

namespace Dango.Server.Tests.Accounts;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class B1HttpTests
{
    private const string AuthCookie = "__Host-Dango.Auth";

    [TestMethod]
    public async Task FormalHostHasReadinessButNoAnonymousPrototypeHubOrInvitationAdminRoute()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        using var client = fixture.CreateClient(out _);
        using var ready = await client.GetAsync("health/ready");
        Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
        using var prototype = await client.GetAsync("room");
        Assert.AreEqual(HttpStatusCode.NotFound, prototype.StatusCode);
        using var admin = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/invitations", new { email = "fictional@example.invalid" });
        Assert.AreEqual(HttpStatusCode.NotFound, admin.StatusCode);
    }

    [TestMethod]
    public async Task WritesRequireCookieBoundCsrfEvenForNativeRequestsAndRejectForeignOrigin()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        using var client = fixture.CreateClient(out _);
        var login = new { email = "unknown@example.invalid", password = B1PostgresTests.NewPassword(), remembered = false };
        using var missing = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login", login, csrf: false);
        await ErrorAsync(missing, HttpStatusCode.Forbidden, "request_protection_invalid");
        using var foreignOrigin = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login", login,
            origin: "https://foreign.example.invalid");
        await ErrorAsync(foreignOrigin, HttpStatusCode.Forbidden, "origin_invalid");
        var token = await FormalTestFixture.CsrfAsync(client);
        using var other = fixture.CreateClient(out _);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login") { Content = JsonContent.Create(login) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        using var detachedToken = await other.SendAsync(request);
        await ErrorAsync(detachedToken, HttpStatusCode.Forbidden, "request_protection_invalid");
        using var sameOrigin = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login", login,
            origin: fixture.Endpoint.GetLeftPart(UriPartial.Authority));
        Assert.AreEqual(HttpStatusCode.Unauthorized, sameOrigin.StatusCode);
    }

    [TestMethod]
    public async Task HttpJsonRejectsDuplicateUnknownMissingNullAndOversizedInputWithStableCodes()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        using var client = fixture.CreateClient(out _);
        foreach (var json in new[]
        {
            "{\"email\":\"fictional@example.invalid\",\"email\":\"other@example.invalid\",\"password\":\"fictional-only-marker\",\"remembered\":false}",
            "{\"email\":\"fictional@example.invalid\",\"password\":\"fictional-only-marker\",\"remembered\":false,\"ownerId\":\"forged\"}",
            "{\"email\":\"fictional@example.invalid\",\"password\":\"fictional-only-marker\"}",
            "{\"email\":null,\"password\":\"fictional-only-marker\",\"remembered\":false}"
        })
        {
            using var response = await RawJsonAsync(client, "api/v1/auth/login", json);
            await ErrorAsync(response, HttpStatusCode.BadRequest, "request_invalid");
            Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("fictional-only-marker", StringComparison.Ordinal));
        }
        using var oversized = await RawJsonAsync(client, "api/v1/auth/login", "{\"email\":\"" + new string('a', 9000) + "\"}");
        Assert.IsTrue(oversized.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge);
    }

    [TestMethod]
    public async Task HttpsApiControlledSmtpAndPostgresCompleteRegistrationWithoutIssuingLoginCookie()
    {
        await using var receiver = new ControlledSmtpReceiver();
        var email = $"http-{Guid.NewGuid():N}@example.invalid";
        var sender = new RecipientOnlySender(email, new MailKitEmailSender(SmtpOptions.ForLoopbackTest(receiver.Port)));
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, sender: sender);
        using var client = fixture.CreateClient(out var cookies);
        var invitationProof = B1InputTests.NewProof();
        var invitationId = await fixture.InviteAsync(email, invitationProof);
        var resumeProof = B1InputTests.NewProof();
        using var start = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/registrations",
            new { email, invitationId, invitationProof, requestKey = Guid.NewGuid() }, resumeProof);
        var started = await FormalTestFixture.ReadAsync<JsonElement>(start);
        var pendingId = started.GetProperty("id").GetGuid();
        Assert.AreEqual("pending", started.GetProperty("state").GetString());
        Assert.IsNull(cookies.GetCookies(fixture.Endpoint)[AuthCookie]);
        await PrioritizeRegistrationJobAsync(fixture, pendingId);
        await DispatchRegistrationJobAsync(fixture, pendingId, 1);
        var url = await ReadProofUrlAsync(receiver);
        var proof = QueryHelpers.ParseQuery(url.Query)["proof"].ToString();
        using var scanned = await client.GetAsync("auth/email-confirm?proof=" + Uri.EscapeDataString(proof));
        Assert.AreEqual(HttpStatusCode.OK, scanned.StatusCode);
        Assert.IsTrue(scanned.Headers.Contains("Content-Security-Policy"));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(RegistrationState.Pending, (await db.PendingRegistrations.SingleAsync(r => r.Id == pendingId)).State);
            Assert.IsNull((await db.EmailChallenges.SingleAsync(c => c.PendingRegistrationId == pendingId)).ConsumedAt);
            Assert.AreEqual(EmailJobState.Accepted, (await (from job in db.EmailJobs join challenge in db.EmailChallenges on job.ChallengeId equals challenge.Id
                where challenge.PendingRegistrationId == pendingId select job).SingleAsync()).State);
        });
        using var unprotectedConfirm = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/email-verifications/confirm",
            new { proof }, csrf: false);
        await ErrorAsync(unprotectedConfirm, HttpStatusCode.Forbidden, "request_protection_invalid");
        using var confirm = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/email-verifications/confirm", new { proof });
        Assert.AreEqual(HttpStatusCode.OK, confirm.StatusCode);
        var completionKey = Guid.NewGuid();
        var password = B1PostgresTests.NewPassword();
        using var complete = await FormalTestFixture.SendAsync(client, HttpMethod.Post, $"api/v1/auth/registrations/{pendingId}/complete",
            new { completionKey, password, appearance = B1InputTests.Parse(B1InputTests.ValidAppearance) }, resumeProof);
        var result = await FormalTestFixture.ReadAsync<RegistrationResult>(complete);
        Assert.IsFalse(complete.Headers.TryGetValues("Set-Cookie", out var setCookies) && setCookies.Any(c => c.StartsWith(AuthCookie + "=", StringComparison.Ordinal)));
        Assert.IsNull(cookies.GetCookies(fixture.Endpoint)[AuthCookie]);
        using var me = await client.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, me.StatusCode);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.Pets.CountAsync(p => p.Id == result.PetId && p.UserId == result.UserId));
            Assert.AreEqual(1, await db.PetRoomStates.CountAsync(s => s.PetId == result.PetId && s.RoomId == result.RoomId));
            Assert.IsFalse(await db.AuthSessions.AnyAsync(s => s.UserId == result.UserId));
        });
        using var login = await LoginAsync(client, email, password);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        AssertCookieAttributes(login);
        using var signedIn = await client.GetAsync("api/v1/me");
        Assert.AreEqual(result.UserId, (await FormalTestFixture.ReadAsync<JsonElement>(signedIn)).GetProperty("userId").GetGuid());
    }

    [TestMethod]
    public async Task LogoutRevokesStoredSessionAndReplayedCookieCannotAuthenticate()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await RegisterAsync(fixture);
        using var client = fixture.CreateClient(out var cookies);
        using var login = await LoginAsync(client, account.Email, account.Password);
        var loggedIn = await FormalTestFixture.ReadAsync<JsonElement>(login);
        var sessionId = loggedIn.GetProperty("sessionId").GetGuid();
        var oldCookie = cookies.GetCookies(fixture.Endpoint)[AuthCookie]!.Value;
        using var logout = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/logout");
        Assert.AreEqual(HttpStatusCode.OK, logout.StatusCode);
        using var replay = fixture.CreateClient(out _);
        replay.DefaultRequestHeaders.Add("Cookie", AuthCookie + "=" + oldCookie);
        using var denied = await replay.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        await fixture.InScopeAsync(async services =>
            Assert.IsNotNull((await services.GetRequiredService<DangoDbContext>().AuthSessions.SingleAsync(s => s.Id == sessionId)).RevokedAt));
    }

    [TestMethod]
    public async Task AuthenticationChangeRequiresRefreshingTheCsrfProof()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        var anonymousToken = await FormalTestFixture.CsrfAsync(client);
        using var login = await LoginAsync(client, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", anonymousToken);
        using var stale = await client.SendAsync(request);
        await ErrorAsync(stale, HttpStatusCode.Forbidden, "request_protection_invalid");
        using var refreshed = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/logout");
        Assert.AreEqual(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [TestMethod]
    [DataRow(false, 8)]
    [DataRow(true, 168)]
    public async Task StoredSessionHasAbsoluteExpiryAndSuccessfulRequestsDoNotSlideIt(bool remembered, int hours)
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, clock: clock);
        var account = await RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        using var login = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login",
            new { email = account.Email, password = account.Password, remembered });
        var sessionId = (await FormalTestFixture.ReadAsync<JsonElement>(login)).GetProperty("sessionId").GetGuid();
        var original = await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().AuthSessions.AsNoTracking()
            .SingleAsync(s => s.Id == sessionId));
        Assert.AreEqual(TimeSpan.FromHours(hours), original.ExpiresAt - original.CreatedAt);
        Assert.AreEqual(remembered, original.Remembered);
        clock.Advance(TimeSpan.FromHours(1));
        using var alive = await client.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.OK, alive.StatusCode);
        var afterRead = await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().AuthSessions.AsNoTracking()
            .SingleAsync(s => s.Id == sessionId));
        Assert.AreEqual(original.ExpiresAt, afterRead.ExpiresAt);
        clock.Advance(original.ExpiresAt - clock.GetUtcNow());
        using var expired = await client.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [TestMethod]
    public async Task FiveWrongPasswordsPersistLockoutAndCorrectPasswordCannotBypassIt()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, clock: clock);
        var account = await RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var incorrect = await LoginAsync(client, account.Email, B1PostgresTests.NewPassword());
            await ErrorAsync(incorrect, HttpStatusCode.Unauthorized, "login_failed");
        }
        var user = await fixture.InScopeAsync(services => services.GetRequiredService<DangoDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == account.UserId));
        Assert.IsNotNull(user.LockoutEnd);
        Assert.IsTrue(user.LockoutEnd > clock.GetUtcNow());
        using var locked = await LoginAsync(client, account.Email, account.Password);
        await ErrorAsync(locked, HttpStatusCode.Unauthorized, "login_failed");
        clock.Advance(TimeSpan.FromMinutes(15));
        using var unlocked = await LoginAsync(client, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.OK, unlocked.StatusCode);
    }

    [TestMethod]
    public async Task PasswordChangeRequiresCurrentPasswordAndRevokesOtherDeviceCookies()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        using var otherDevice = fixture.CreateClient(out _);
        using var firstLogin = await LoginAsync(client, account.Email, account.Password);
        using var secondLogin = await LoginAsync(otherDevice, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.OK, firstLogin.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondLogin.StatusCode);
        var replacement = B1PostgresTests.NewPassword();
        using var wrongCurrent = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password/change",
            new { currentPassword = B1PostgresTests.NewPassword(), newPassword = replacement });
        await ErrorAsync(wrongCurrent, HttpStatusCode.BadRequest, "current_password_invalid");
        using var change = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password/change",
            new { currentPassword = account.Password, newPassword = replacement });
        Assert.AreEqual(HttpStatusCode.OK, change.StatusCode);
        using var revokedOther = await otherDevice.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, revokedOther.StatusCode);
        using var oldPassword = await LoginAsync(client, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await LoginAsync(client, account.Email, replacement);
        Assert.AreEqual(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [TestMethod]
    public async Task RecreatedFormalHostPreservesDatabaseSessionAndEmailProofKeys()
    {
        Account account;
        B1PostgresTests.RegistrationAttempt pending;
        string cookie;
        string proof;
        await using (var original = await FormalTestFixture.CreateAsync(startHttp: true))
        {
            account = await RegisterAsync(original);
            pending = await B1PostgresTests.StartAsync(original);
            proof = await original.RegistrationProofAsync(pending.PendingId);
            using var client = original.CreateClient(out var cookies);
            using var login = await LoginAsync(client, account.Email, account.Password);
            Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
            cookie = cookies.GetCookies(original.Endpoint)[AuthCookie]!.Value;
        }
        await using var restarted = await FormalTestFixture.CreateAsync(startHttp: true);
        using var resumed = restarted.CreateClient(out _);
        resumed.DefaultRequestHeaders.Add("Cookie", AuthCookie + "=" + cookie);
        using var me = await resumed.GetAsync("api/v1/me");
        Assert.AreEqual(account.UserId, (await FormalTestFixture.ReadAsync<JsonElement>(me)).GetProperty("userId").GetGuid());
        using var confirm = await FormalTestFixture.SendAsync(resumed, HttpMethod.Post, "api/v1/auth/email-verifications/confirm", new { proof });
        Assert.AreEqual(HttpStatusCode.OK, confirm.StatusCode);
        await restarted.InScopeAsync(async services =>
            Assert.AreEqual(RegistrationState.Verified, (await services.GetRequiredService<DangoDbContext>().PendingRegistrations
                .SingleAsync(r => r.Id == pending.PendingId)).State));
    }

    [TestMethod]
    public async Task OwnSessionListAndRevokeCannotAccessAnotherAccountsSession()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var first = await RegisterAsync(fixture);
        var second = await RegisterAsync(fixture);
        using var firstClient = fixture.CreateClient(out _);
        using var secondClient = fixture.CreateClient(out _);
        using var firstLogin = await LoginAsync(firstClient, first.Email, first.Password);
        var firstSession = (await FormalTestFixture.ReadAsync<JsonElement>(firstLogin)).GetProperty("sessionId").GetGuid();
        using var secondLogin = await LoginAsync(secondClient, second.Email, second.Password);
        var secondSession = (await FormalTestFixture.ReadAsync<JsonElement>(secondLogin)).GetProperty("sessionId").GetGuid();
        using var list = await firstClient.GetAsync("api/v1/me/sessions");
        var sessions = await FormalTestFixture.ReadAsync<JsonElement>(list);
        Assert.IsTrue(sessions.EnumerateArray().Any(s => s.GetProperty("sessionId").GetGuid() == firstSession));
        Assert.IsFalse(sessions.EnumerateArray().Any(s => s.GetProperty("sessionId").GetGuid() == secondSession));
        using var foreignRevoke = await FormalTestFixture.SendAsync(firstClient, HttpMethod.Delete, $"api/v1/me/sessions/{secondSession}");
        Assert.AreEqual(HttpStatusCode.NotFound, foreignRevoke.StatusCode);
        using var secondMe = await secondClient.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.OK, secondMe.StatusCode);
        using var ownRevoke = await FormalTestFixture.SendAsync(firstClient, HttpMethod.Delete, $"api/v1/me/sessions/{firstSession}");
        Assert.AreEqual(HttpStatusCode.NoContent, ownRevoke.StatusCode);
        using var firstMe = await firstClient.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, firstMe.StatusCode);
    }

    [TestMethod]
    public async Task PasswordResetIsUniformConsumesProofOnceAndRevokesAllOldCookies()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, clock: clock);
        var account = await RegisterAsync(fixture);
        using var client = fixture.CreateClient(out var cookies);
        using var login = await LoginAsync(client, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var oldCookie = cookies.GetCookies(fixture.Endpoint)[AuthCookie]!.Value;
        clock.Advance(TimeSpan.FromSeconds(61));
        using var known = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password-reset/request", new { email = account.Email });
        using var unknown = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password-reset/request", new { email = $"unknown-{Guid.NewGuid():N}@example.invalid" });
        Assert.AreEqual(HttpStatusCode.OK, known.StatusCode);
        Assert.AreEqual(known.StatusCode, unknown.StatusCode);
        Assert.AreEqual(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        var proof = await ResetProofAsync(fixture, account.UserId);
        using var scanner = await client.GetAsync("auth/password-reset?proof=" + Uri.EscapeDataString(proof));
        Assert.AreEqual(HttpStatusCode.OK, scanner.StatusCode);
        var password = B1PostgresTests.NewPassword();
        using var reset = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password-reset/complete", new { proof, password });
        Assert.AreEqual(HttpStatusCode.OK, reset.StatusCode);
        using var retry = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password-reset/complete", new { proof, password });
        Assert.AreEqual(HttpStatusCode.Forbidden, retry.StatusCode);
        using var replay = fixture.CreateClient(out _);
        replay.DefaultRequestHeaders.Add("Cookie", AuthCookie + "=" + oldCookie);
        using var denied = await replay.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var oldLogin = await LoginAsync(client, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        using var newLogin = await LoginAsync(client, account.Email, password);
        Assert.AreEqual(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisabledOrRecoveryLockedAccountRejectsCookiePasswordAndPublicReset(bool recoveryLocked)
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, clock: clock);
        var account = await RegisterAsync(fixture);
        using var client = fixture.CreateClient(out var cookies);
        using var login = await LoginAsync(client, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var oldCookie = cookies.GetCookies(fixture.Endpoint)[AuthCookie]!.Value;
        clock.Advance(TimeSpan.FromSeconds(61));
        using var requested = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password-reset/request", new { email = account.Email });
        Assert.AreEqual(HttpStatusCode.OK, requested.StatusCode);
        var proof = await ResetProofAsync(fixture, account.UserId);
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == account.UserId);
            if (recoveryLocked) user.RecoveryLocked = true;
            else user.DisabledAt = clock.GetUtcNow();
            await db.SaveChangesAsync();
        });
        using var replay = fixture.CreateClient(out _);
        replay.DefaultRequestHeaders.Add("Cookie", AuthCookie + "=" + oldCookie);
        using var denied = await replay.GetAsync("api/v1/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var passwordDenied = await LoginAsync(client, account.Email, account.Password);
        Assert.AreEqual(HttpStatusCode.Unauthorized, passwordDenied.StatusCode);
        using var resetDenied = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/password-reset/complete",
            new { proof, password = B1PostgresTests.NewPassword() });
        Assert.AreEqual(HttpStatusCode.Forbidden, resetDenied.StatusCode);
        await fixture.InScopeAsync(async services =>
        {
            var user = await services.GetRequiredService<DangoDbContext>().Users.SingleAsync(u => u.Id == account.UserId);
            if (recoveryLocked) Assert.IsTrue(user.RecoveryLocked);
            else Assert.IsNotNull(user.DisabledAt);
        });
    }

    [TestMethod]
    public async Task ControlledSmtpFailureUsesBoundedPersistentRetriesAndReportsFailureTruthfully()
    {
        await using var receiver = new ControlledSmtpReceiver { RejectTransiently = true };
        var email = $"retry-{Guid.NewGuid():N}@example.invalid";
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        var sender = new RecipientOnlySender(email, new MailKitEmailSender(SmtpOptions.ForLoopbackTest(receiver.Port)));
        await using var fixture = await FormalTestFixture.CreateAsync(clock: clock, sender: sender);
        var invitationProof = B1InputTests.NewProof();
        var invitation = await fixture.InviteAsync(email, invitationProof);
        var resume = B1InputTests.NewProof();
        var registration = await fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
            email, invitation, invitationProof, Guid.NewGuid(), resume, CancellationToken.None));
        await PrioritizeRegistrationJobAsync(fixture, registration.Id);
        for (var attempt = 1; attempt <= EmailDispatchPolicy.MaximumAttempts; attempt++)
        {
            await DispatchRegistrationJobAsync(fixture, registration.Id, attempt);
            Assert.AreEqual(attempt, receiver.Attempts);
            var job = await RegistrationJobAsync(fixture, registration.Id);
            Assert.AreEqual(attempt, job.Attempts);
            Assert.AreEqual(attempt == EmailDispatchPolicy.MaximumAttempts ? EmailJobState.Failed : EmailJobState.Pending, job.State);
            Assert.IsNull(job.AcceptedAt);
            clock.Advance(job.DueAt - clock.GetUtcNow() + TimeSpan.FromSeconds(1));
        }
        for (var repeat = 0; repeat < 2; repeat++)
            await fixture.InScopeAsync(services => services.GetRequiredService<EmailDispatcher>().DispatchBatchAsync(CancellationToken.None));
        Assert.AreEqual(EmailDispatchPolicy.MaximumAttempts, receiver.Attempts);
        var status = await fixture.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StatusAsync(registration.Id, resume, CancellationToken.None));
        Assert.AreEqual("smtp_attempt_limit", status.EmailStatus);
    }

    [TestMethod]
    public async Task SmtpAcceptanceInterruptedBeforeRecordingIsRetriedAfterLeaseAndProofConsumesOnce()
    {
        await using var receiver = new ControlledSmtpReceiver();
        using var interrupted = new CancellationTokenSource();
        var email = $"interrupted-{Guid.NewGuid():N}@example.invalid";
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        var mailKit = new MailKitEmailSender(SmtpOptions.ForLoopbackTest(receiver.Port));
        Guid pendingId;
        string firstProof;
        await using (var original = await FormalTestFixture.CreateAsync(clock: clock,
            sender: new InterruptAfterAcceptanceSender(email, mailKit, interrupted)))
        {
            var invitationProof = B1InputTests.NewProof();
            var invitation = await original.InviteAsync(email, invitationProof);
            var registration = await original.InScopeAsync(services => services.GetRequiredService<RegistrationService>().StartAsync(
                email, invitation, invitationProof, Guid.NewGuid(), B1InputTests.NewProof(), CancellationToken.None));
            pendingId = registration.Id;
            await PrioritizeRegistrationJobAsync(original, pendingId);
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => original.InScopeAsync(services =>
                services.GetRequiredService<EmailDispatcher>().DispatchBatchAsync(interrupted.Token)));
            firstProof = QueryHelpers.ParseQuery((await ReadProofUrlAsync(receiver)).Query)["proof"].ToString();
            var leased = await RegistrationJobAsync(original, pendingId);
            Assert.AreEqual(EmailJobState.Leased, leased.State);
            Assert.AreEqual(1, leased.Attempts);
            Assert.IsNull(leased.AcceptedAt);
        }
        clock.Advance(EmailDispatchPolicy.LeaseDuration + TimeSpan.FromSeconds(1));
        await using var restarted = await FormalTestFixture.CreateAsync(clock: clock, sender: new RecipientOnlySender(email, mailKit));
        await DispatchRegistrationJobAsync(restarted, pendingId, 2);
        var secondProof = QueryHelpers.ParseQuery((await ReadProofUrlAsync(receiver)).Query)["proof"].ToString();
        var accepted = await RegistrationJobAsync(restarted, pendingId);
        Assert.AreEqual(EmailJobState.Accepted, accepted.State);
        Assert.AreEqual(2, receiver.Attempts);
        await restarted.InScopeAsync(async services =>
        {
            var protector = services.GetRequiredService<IEmailProofProtector>();
            Assert.IsTrue(protector.TryUnprotect(firstProof, out var first));
            Assert.IsTrue(protector.TryUnprotect(secondProof, out var second));
            Assert.AreEqual(first!.ChallengeId, second!.ChallengeId);
            await services.GetRequiredService<RegistrationService>().ConfirmAsync(firstProof, CancellationToken.None);
        });
        var replay = await Assert.ThrowsExactlyAsync<AccountFault>(() => restarted.InScopeAsync(services =>
            services.GetRequiredService<RegistrationService>().ConfirmAsync(secondProof, CancellationToken.None)));
        Assert.AreEqual("email_proof_invalid", replay.Code);
    }

    private static async Task<Account> RegisterAsync(FormalTestFixture fixture)
    {
        var registration = await B1PostgresTests.StartAsync(fixture);
        await B1PostgresTests.VerifyAsync(fixture, registration);
        var password = B1PostgresTests.NewPassword();
        var result = await B1PostgresTests.CompleteAsync(fixture, registration, Guid.NewGuid(), password);
        return new(registration.Email, password, result.UserId);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login", new { email, password, remembered = false });

    private static Task<string> ResetProofAsync(FormalTestFixture fixture, Guid userId) => fixture.InScopeAsync(async services =>
    {
        var challenge = await services.GetRequiredService<DangoDbContext>().EmailChallenges.AsNoTracking()
            .SingleAsync(c => c.UserId == userId && c.Purpose == EmailChallengePurpose.PasswordReset && c.RevokedAt == null && c.ConsumedAt == null);
        return services.GetRequiredService<IEmailProofProtector>().Protect(challenge);
    });

    private static Task PrioritizeRegistrationJobAsync(FormalTestFixture fixture, Guid id) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var job = await (from item in db.EmailJobs join challenge in db.EmailChallenges on item.ChallengeId equals challenge.Id
                         where challenge.PendingRegistrationId == id select item).SingleAsync();
        job.DueAt = services.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-1);
        await db.SaveChangesAsync();
    });

    private static Task<EmailJob> RegistrationJobAsync(FormalTestFixture fixture, Guid id) => fixture.InScopeAsync(services =>
        (from job in services.GetRequiredService<DangoDbContext>().EmailJobs.AsNoTracking()
         join challenge in services.GetRequiredService<DangoDbContext>().EmailChallenges on job.ChallengeId equals challenge.Id
         where challenge.PendingRegistrationId == id select job).SingleAsync());

    private static async Task DispatchRegistrationJobAsync(FormalTestFixture fixture, Guid id, int expectedAttempts)
    {
        for (var batch = 0; batch < 32; batch++)
        {
            if ((await RegistrationJobAsync(fixture, id)).Attempts >= expectedAttempts) return;
            var claimed = await fixture.InScopeAsync(services => services.GetRequiredService<EmailDispatcher>().DispatchBatchAsync(CancellationToken.None));
            Assert.IsTrue(claimed > 0, "The due registration job was not claimed.");
        }
        Assert.Fail("The registration job exceeded the bounded acceptance batch budget.");
    }

    private static async Task<Uri> ReadProofUrlAsync(ControlledSmtpReceiver receiver)
    {
        var wire = await receiver.ReceiveAsync();
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(wire));
        using var message = MimeMessage.Load(stream);
        var text = message.TextBody ?? throw new InvalidOperationException("The controlled receiver did not receive a text message.");
        var link = text.Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith("https://127.0.0.1:", StringComparison.Ordinal));
        return new Uri(link);
    }

    private static async Task<HttpResponseMessage> RawJsonAsync(HttpClient client, string path, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", await FormalTestFixture.CsrfAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(code, json.GetProperty("code").GetString());
        Assert.IsTrue(json.GetProperty("correlationId").GetString() is { Length: > 0 });
    }

    private static void AssertCookieAttributes(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthCookie + "=", StringComparison.Ordinal));
        Assert.IsTrue(cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(cookie.Contains("path=/", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(cookie.Contains("domain=", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record Account(string Email, string Password, Guid UserId);
    private sealed class RecipientOnlySender(string recipient, IEmailSender inner) : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailDelivery delivery, CancellationToken ct) => delivery.Recipient == recipient
            ? inner.SendAsync(delivery, ct) : Task.FromResult(new EmailSendResult(EmailSendStatus.NotConfigured));
    }
    private sealed class InterruptAfterAcceptanceSender(string recipient, IEmailSender inner, CancellationTokenSource interruption) : IEmailSender
    {
        public async Task<EmailSendResult> SendAsync(EmailDelivery delivery, CancellationToken ct)
        {
            if (delivery.Recipient != recipient) return new(EmailSendStatus.NotConfigured);
            var result = await inner.SendAsync(delivery, ct);
            Assert.AreEqual(EmailSendStatus.Accepted, result.Status);
            await interruption.CancelAsync();
            throw new OperationCanceledException(ct);
        }
    }
}
