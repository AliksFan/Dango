using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncB1c")]
public sealed class AccountSyncB1cResponseHoldTests
{
    private static readonly Guid User = Guid.Parse("724bd22c-082a-408c-a02a-fc756d423502");
    private static readonly Guid Pet = Guid.Parse("42e0189e-70fc-4f6b-aa31-d888bed2c263");
    private static readonly Guid Session = Guid.Parse("7d80a92d-4f17-465e-b4b0-9066105b3278");
    private static readonly Guid Other = Guid.Parse("3053f1ec-a78b-46c5-ba04-524325ec9982");
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(3);

    // These are synthetic responses and configuration snapshots. They test the barrier's
    // ownership and cancellation contract, not HTTPS, a database commit, or desktop behavior.
    [TestMethod]
    public async Task BoundsAndArmingRejectWideningUnknownCasesAndEmptyIdentities()
    {
        foreach (var bound in new[] { TimeSpan.Zero, TimeSpan.FromMilliseconds(-1), TimeSpan.FromSeconds(31) })
            Reject(() => new AccountSyncB1cResponseHold(bound));

        (string Case, string Method, Guid UserId, Guid PetId, Guid SessionId)[] invalid =
        [
            ("unknown", "GET", User, Pet, Session),
            ("late-get", "PATCH", User, Pet, Session),
            ("late-patch", "GET", User, Pet, Session),
            ("late-get", "get", User, Pet, Session),
            ("late-get", "GET", Guid.Empty, Pet, Session),
            ("late-get", "GET", User, Guid.Empty, Session),
            ("late-get", "GET", User, Pet, Guid.Empty)
        ];
        foreach (var candidate in invalid)
        {
            await using var hold = new AccountSyncB1cResponseHold();
            Reject(() => hold.Arm(candidate.Case, candidate.Method, candidate.UserId, candidate.PetId, candidate.SessionId));
            Assert.IsFalse(hold.Matches("GET", "/api/v1/me/pet", User, Session));
            Assert.AreEqual("", hold.Snapshot().Case, "A rejected arm must leave the barrier unused.");
        }

        await using var armed = Armed("late-get", "GET");
        Reject(() => armed.Arm("late-get", "GET", User, Pet, Session));
        Reject(() => armed.Arm("late-patch", "PATCH", Other, Pet, Session));
        var snapshot = armed.Snapshot();
        Assert.AreEqual("late-get", snapshot.Case);
        Assert.AreEqual(User, snapshot.UserId);
        Assert.AreEqual(Pet, snapshot.PetId);
        Assert.AreEqual(Session, snapshot.SessionId);
    }

    [TestMethod]
    [DataRow("late-get", "GET", "/api/v1/me/pet")]
    [DataRow("late-patch", "PATCH", "/api/v1/me/pet/configuration")]
    public async Task OnlyExactMethodPathActorAndSessionCanMatch(string name, string method, string path)
    {
        await using var hold = new AccountSyncB1cResponseHold();
        Assert.IsFalse(hold.Matches(method, path, User, Session), "An unarmed barrier cannot claim a request.");
        hold.Arm(name, method, User, Pet, Session);
        Assert.IsTrue(hold.Matches(method, path, User, Session));
        foreach (var wrongMethod in new[] { method.ToLowerInvariant(), method == "GET" ? "PATCH" : "GET", "POST" })
            Assert.IsFalse(hold.Matches(wrongMethod, path, User, Session));
        foreach (var wrongPath in new[] { path + "/", path + "?version=1", "/api/v1/me/profile" })
            Assert.IsFalse(hold.Matches(method, wrongPath, User, Session));
        foreach (var wrongIdentity in new[] { Other, Guid.Empty })
        {
            Assert.IsFalse(hold.Matches(method, path, wrongIdentity, Session));
            Assert.IsFalse(hold.Matches(method, path, User, wrongIdentity));
        }
        Assert.IsTrue(hold.Matches(method, path, User, Session), "Nonmatching requests must not consume the arm.");
    }

    [TestMethod]
    [DataRow("late-get", "GET", 1L)]
    [DataRow("late-patch", "PATCH", 2L)]
    public async Task CaptureRejectsStatusAndExactUserPetSessionAndResponseVersionWithoutTakingOwnership(
        string name, string method, long expectedVersion)
    {
        await using var hold = Armed(name, method);
        var expected = Configuration(expectedVersion);
        foreach (var status in new[] { HttpStatusCode.Created, HttpStatusCode.NoContent, HttpStatusCode.Conflict, HttpStatusCode.InternalServerError })
        {
            using var rejected = Response(status, out var rejectedContent, expected);
            Reject(() => hold.CaptureAsync(rejected, expected, Session, CancellationToken.None));
            Assert.IsFalse(rejectedContent.Disposed, "A rejected response remains the caller's property.");
        }
        foreach (var wrongIdentity in new[] { Other, Guid.Empty })
        {
            using var rejected = Response(HttpStatusCode.OK, out var rejectedContent, expected);
            Reject(() => hold.CaptureAsync(rejected, expected with { UserId = wrongIdentity }, Session, CancellationToken.None));
            Reject(() => hold.CaptureAsync(rejected, expected with { PetId = wrongIdentity }, Session, CancellationToken.None));
            Reject(() => hold.CaptureAsync(rejected, expected, wrongIdentity, CancellationToken.None));
            Assert.IsFalse(rejectedContent.Disposed);
        }
        foreach (var wrongVersion in new[] { long.MinValue, 0L, expectedVersion == 1 ? 2L : 1L, 3L, long.MaxValue })
        {
            using var rejected = Response(HttpStatusCode.OK, out var rejectedContent, expected);
            Reject(() => hold.CaptureAsync(rejected, expected with { ConfigurationVersion = wrongVersion }, Session, CancellationToken.None));
            Assert.IsFalse(rejectedContent.Disposed);
        }
        Assert.IsFalse(hold.Captured.IsCompleted, "Rejected responses cannot manufacture a captured checkpoint.");
        Assert.IsFalse(hold.Snapshot().ActualSuccessfulResponse);
        Assert.AreEqual(0L, hold.Snapshot().ResponseVersion);

        using var accepted = Response(HttpStatusCode.OK, out var content, expected);
        var validatedCaptures = 0;
        var originalTask = hold.CaptureAsync(accepted, expected, Session, CancellationToken.None, () => validatedCaptures++);
        await hold.Captured.WaitAsync(Watchdog);
        Assert.AreEqual(1, validatedCaptures, "The validated response phase must precede the captured checkpoint.");
        Assert.IsFalse(originalTask.IsCompleted, "Only a valid capture may enter the held state.");
        hold.Release();
        Assert.AreSame(accepted, await originalTask.WaitAsync(Watchdog));
        Assert.IsFalse(content.Disposed);
    }

    [TestMethod]
    [DataRow("late-get", "GET", 1L, false)]
    [DataRow("late-get", "GET", 1L, true)]
    [DataRow("late-patch", "PATCH", 2L, false)]
    [DataRow("late-patch", "PATCH", 2L, true)]
    public async Task CallerCancellationIsObservedButOriginalTaskWaitsForExplicitRelease(
        string name, string method, long version, bool cancelBeforeCapture)
    {
        await using var hold = Armed(name, method, Watchdog);
        using var cancellation = new CancellationTokenSource();
        if (cancelBeforeCapture) cancellation.Cancel();
        using var response = Response(HttpStatusCode.OK, out var content, Configuration(version));
        var originalTask = hold.CaptureAsync(response, Configuration(version), Session, cancellation.Token);
        await hold.Captured.WaitAsync(Watchdog);
        if (!cancelBeforeCapture) cancellation.Cancel();
        Assert.IsTrue(hold.Snapshot().CancellationObserved);

        using var probeLifetime = new CancellationTokenSource();
        var probe = Task.Delay(TimeSpan.FromMilliseconds(75), probeLifetime.Token);
        try
        {
            Assert.AreSame(probe, await Task.WhenAny(originalTask, probe),
                "Caller cancellation cannot return, cancel, or fault the originally captured response task.");
        }
        finally
        {
            probeLifetime.Cancel();
            await ObserveCancelledDelay(probe);
        }
        Assert.IsFalse(originalTask.IsCompleted);
        Assert.IsFalse(content.Disposed);
        var held = hold.Snapshot();
        Assert.IsFalse(held.Released);
        Assert.IsFalse(held.Settled);
        Assert.IsFalse(held.CleanupRelease);

        hold.Release();
        Assert.AreSame(response, await originalTask.WaitAsync(Watchdog));
        Assert.IsFalse(content.Disposed, "Normal release transfers the original response back to its caller.");
        var released = hold.Snapshot();
        Assert.IsTrue(released.CancellationObserved && released.Released && released.Settled);
        Assert.IsFalse(released.CleanupRelease);
        AssertIdentity(released, name, method, version);
    }

    [TestMethod]
    [DataRow("late-get", "GET", "/api/v1/me/pet", 1L)]
    [DataRow("late-patch", "PATCH", "/api/v1/me/pet/configuration", 2L)]
    public async Task CapturedBarrierCannotBeRearmedOrRecapturedAndCleanupReleaseReturnsTheSameResponse(
        string name, string method, string path, long version)
    {
        await using var hold = Armed(name, method);
        using var response = Response(HttpStatusCode.OK, out var content, Configuration(version));
        var originalTask = hold.CaptureAsync(response, Configuration(version), Session, CancellationToken.None);
        await hold.Captured.WaitAsync(Watchdog);
        Assert.IsFalse(hold.Matches(method, path, User, Session), "One capture consumes the request match.");
        Reject(() => hold.Arm(name, method, User, Pet, Session));
        using var duplicate = Response(HttpStatusCode.OK, out var duplicateContent, Configuration(version));
        Reject(() => hold.CaptureAsync(duplicate, Configuration(version), Session, CancellationToken.None));
        Assert.IsFalse(duplicateContent.Disposed);

        hold.Release(cleanup: true);
        var returned = await originalTask.WaitAsync(Watchdog);
        Assert.AreSame(response, returned);
        Assert.AreSame(content, returned.Content);
        Assert.AreEqual(content.BodyText, await returned.Content.ReadAsStringAsync());
        Assert.IsFalse(content.Disposed);
        Reject(() => hold.Release());
        hold.Release(cleanup: true); // Repeated cleanup cannot rewrite the first release's evidence.
        var snapshot = hold.Snapshot();
        Assert.IsTrue(snapshot.Released && snapshot.Settled && snapshot.CleanupRelease);
        Assert.IsFalse(snapshot.CancellationObserved);
        AssertIdentity(snapshot, name, method, version);
        Reject(() => hold.Release());
        hold.Release(cleanup: true);
        Assert.AreEqual(snapshot, hold.Snapshot(), "Repeated closed release cannot rewrite the original release evidence.");
        Reject(() => hold.CaptureAsync(duplicate, Configuration(version), Session, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("late-get", "GET", "/api/v1/me/pet", 1L)]
    [DataRow("late-patch", "PATCH", "/api/v1/me/pet/configuration", 2L)]
    public async Task FixtureDisposalCancelsAndDrainsTheOwnedTaskWithoutPretendingTheCallerCancelled(
        string name, string method, string path, long version)
    {
        await using var hold = Armed(name, method);
        using var callerCancellation = new CancellationTokenSource();
        using var response = Response(HttpStatusCode.OK, out var content, Configuration(version));
        var originalTask = hold.CaptureAsync(response, Configuration(version), Session, callerCancellation.Token);
        await hold.Captured.WaitAsync(Watchdog);
        await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
        Assert.IsTrue(originalTask.IsCompleted, "Dispose must drain the original task before returning.");
        Assert.IsTrue(originalTask.IsCanceled);
        await RequireCancelled(originalTask);
        Assert.IsTrue(content.Disposed);
        Assert.IsTrue(hold.Captured.IsCompletedSuccessfully, "Disposal cannot rewrite an already validated capture checkpoint.");
        var snapshot = hold.Snapshot();
        Assert.IsTrue(snapshot.Settled);
        Assert.IsFalse(snapshot.CancellationObserved || snapshot.Released || snapshot.CleanupRelease);
        AssertIdentity(snapshot, name, method, version);
        Reject(() => hold.Release());
        hold.Release(cleanup: true);
        Assert.AreEqual(snapshot, hold.Snapshot(), "Closed release cannot rewrite disposal evidence.");
        callerCancellation.Cancel();
        Assert.IsFalse(hold.Snapshot().CancellationObserved, "The settled operation's caller registration must be retired.");
        Assert.IsFalse(hold.Matches(method, path, User, Session));
        Reject(() => hold.Arm(name, method, User, Pet, Session));
        using var rejected = Response(HttpStatusCode.OK, out var rejectedContent, Configuration(version));
        Reject(() => hold.CaptureAsync(rejected, Configuration(version), Session, CancellationToken.None));
        Assert.IsFalse(rejectedContent.Disposed);
        await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
    }

    [TestMethod]
    [DataRow("late-get", "GET", 1L)]
    [DataRow("late-patch", "PATCH", 2L)]
    public async Task IndependentHardDeadlineTerminatesOriginalTaskDisposesResponseAndKeepsFailureObservable(
        string name, string method, long version)
    {
        await using var hold = Armed(name, method, TimeSpan.FromMilliseconds(125));
        using var response = Response(HttpStatusCode.OK, out var content, Configuration(version));
        var elapsed = Stopwatch.StartNew();
        var originalTask = hold.CaptureAsync(response, Configuration(version), Session, CancellationToken.None);
        var faultTask = ObserveFailure(originalTask);
        await hold.Captured.WaitAsync(Watchdog);
        await WaitForOwnedTask(originalTask);
        var observed = await faultTask.WaitAsync(Watchdog);
        Assert.IsTrue(observed is TimeoutException or OperationCanceledException);
        if (originalTask.IsFaulted) Assert.AreSame(originalTask.Exception!.InnerExceptions[0], observed);
        Assert.IsTrue(elapsed.Elapsed >= TimeSpan.FromMilliseconds(75), "A deadline must actually wait rather than manufacture an immediate fault.");
        Assert.IsTrue(elapsed.Elapsed < Watchdog, "The owned operation must terminate inside the independent watchdog.");
        Assert.IsTrue((originalTask.IsFaulted || originalTask.IsCanceled) && content.Disposed);
        var snapshot = hold.Snapshot();
        Assert.IsTrue(snapshot.Settled);
        Assert.IsFalse(snapshot.CancellationObserved || snapshot.Released || snapshot.CleanupRelease);
        AssertIdentity(snapshot, name, method, version);
        Reject(() => hold.Release());
        hold.Release(cleanup: true);
        Assert.AreEqual(snapshot, hold.Snapshot(), "Release after deadline cannot manufacture successful release evidence.");
        await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
        Assert.IsTrue(faultTask.IsCompletedSuccessfully, "The test-owned fault observation must also be drained.");
    }

    [TestMethod]
    [DataRow("late-get", "GET", 1L)]
    [DataRow("late-patch", "PATCH", 2L)]
    public async Task InvalidOrOversizedJsonBodyConsumesTheArmButCannotPublishASuccessfulCapture(
        string name, string method, long version)
    {
        var expected = Configuration(version);
        var valid = JsonBody(expected);
        (string Reason, string Body, string? MediaType, bool KnownLength, bool HoldRejection)[] invalid =
        [
            ("different-user", JsonBody(expected with { UserId = Other }), "application/json", true, true),
            ("different-pet", JsonBody(expected with { PetId = Other }), "application/json", true, true),
            ("different-version", JsonBody(expected with { ConfigurationVersion = version + 1 }), "application/json", true, true),
            ("different-full-configuration", JsonBody(Configuration(version, 111)), "application/json", true, true),
            ("extra-field", valid[..^1] + ",\"extra\":true}", "application/json", true, true),
            ("missing-field", JsonSerializer.Serialize(new { userId = User, petId = Pet, configurationVersion = version }), "application/json", true, true),
            ("duplicate-field", valid[..^1] + ",\"userId\":\"" + Other + "\"}", "application/json", true, false),
            ("invalid-configuration-schema", valid.Replace("\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":2", StringComparison.Ordinal), "application/json", true, false),
            ("invalid-json", "{", "application/json", true, false),
            ("non-object", "[]", "application/json", true, true),
            ("empty-body", "", "application/json", true, true),
            ("known-oversized-body", valid.PadRight(16385), "application/json", true, true),
            ("unknown-oversized-body", valid.PadRight(16385), "application/json", false, false),
            ("wrong-content-type", valid, "text/plain", true, true),
            ("missing-content-type", valid, null, true, true)
        ];
        foreach (var candidate in invalid)
        {
            await using var hold = Armed(name, method);
            using var content = new ObservedContent(candidate.Body, candidate.MediaType, candidate.KnownLength);
            using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            var validatedCaptures = 0;
            var originalTask = hold.CaptureAsync(response, expected, Session, CancellationToken.None, () => validatedCaptures++);
            var faultTask = ObserveFailure(originalTask);
            await WaitForOwnedTask(originalTask);
            var observed = await faultTask.WaitAsync(Watchdog);
            Assert.IsTrue(originalTask.IsFaulted, candidate.Reason);
            Assert.AreSame(originalTask.Exception!.InnerExceptions[0], observed, candidate.Reason);
            if (candidate.HoldRejection)
            {
                Assert.IsInstanceOfType<InvalidOperationException>(observed, candidate.Reason);
                Assert.AreEqual("accountsync_b1c_hold_rejected", observed.Message, candidate.Reason);
            }
            Assert.IsTrue(content.Disposed, candidate.Reason);
            var capturedFailure = await RequireCapturedFailure(hold.Captured);
            Assert.AreSame(observed, capturedFailure, candidate.Reason);
            Assert.IsTrue(hold.Snapshot().Settled, candidate.Reason);
            Assert.IsFalse(hold.Snapshot().ActualSuccessfulResponse, candidate.Reason);
            Assert.AreEqual(0, validatedCaptures, candidate.Reason);
            var failedSnapshot = hold.Snapshot();
            Reject(() => hold.Release());
            hold.Release(cleanup: true);
            Assert.AreEqual(failedSnapshot, hold.Snapshot(), candidate.Reason);
            using var replacement = Response(HttpStatusCode.OK, out var replacementContent, expected);
            Reject(() => hold.CaptureAsync(replacement, expected, Session, CancellationToken.None));
            Assert.IsFalse(replacementContent.Disposed, candidate.Reason);
            await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
            Assert.IsTrue(faultTask.IsCompletedSuccessfully, candidate.Reason);
        }
    }

    [TestMethod]
    [DataRow("late-get", "GET", 1L)]
    [DataRow("late-patch", "PATCH", 2L)]
    public async Task WholeDeadlineIncludesBodyReadAndDrainsTheCancellationResponsiveSyntheticContent(
        string name, string method, long version)
    {
        foreach (var fixtureDisposal in new[] { false, true })
        {
            await using var hold = Armed(name, method, fixtureDisposal ? Watchdog : TimeSpan.FromMilliseconds(125));
            using var content = new WaitingContent();
            using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            var validatedCaptures = 0;
            var originalTask = hold.CaptureAsync(response, Configuration(version), Session, CancellationToken.None, () => validatedCaptures++);
            var faultTask = ObserveFailure(originalTask);
            await content.Entered.WaitAsync(Watchdog);
            Assert.IsFalse(hold.Captured.IsCompletedSuccessfully, "A pending body cannot publish a capture.");
            if (fixtureDisposal) await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
            await WaitForOwnedTask(originalTask);
            await faultTask.WaitAsync(Watchdog);
            Assert.IsTrue(originalTask.IsCanceled || originalTask.IsFaulted);
            var serialization = content.SerializationTask;
            Assert.IsNotNull(serialization);
            Assert.IsTrue(serialization.IsCompleted, "The body's own serialization task must settle before the hold returns.");
            var serializationFailure = await ObserveFailure(serialization).WaitAsync(Watchdog);
            Assert.IsTrue(serializationFailure is OperationCanceledException);
            Assert.IsTrue(content.ReadTokenCancellationObserved, "The deadline or fixture disposal must reach the body read itself.");
            Assert.IsTrue(content.Disposed);
            Assert.AreEqual(0, validatedCaptures);
            var capturedFailure = await RequireCapturedFailure(hold.Captured);
            var snapshot = hold.Snapshot();
            Assert.IsTrue(snapshot.Settled);
            Assert.IsFalse(snapshot.ActualSuccessfulResponse || snapshot.CancellationObserved || snapshot.Released);
            Reject(() => hold.Release());
            hold.Release(cleanup: true);
            Assert.AreEqual(snapshot, hold.Snapshot());
            var firstDisposal = hold.DisposeAsync().AsTask();
            Assert.AreSame(firstDisposal, hold.DisposeAsync().AsTask(), "Disposal must keep owning the same original drain task.");
            await firstDisposal.WaitAsync(Watchdog);
            var afterDisposalFailure = await RequireCapturedFailure(hold.Captured);
            if (hold.Captured.IsFaulted) Assert.AreSame(capturedFailure, afterDisposalFailure);
            else Assert.IsTrue(capturedFailure is OperationCanceledException && afterDisposalFailure is OperationCanceledException && hold.Captured.IsCanceled);
            Assert.IsTrue(faultTask.IsCompletedSuccessfully);
        }
    }

    [TestMethod]
    public async Task UnarmedOrDisposedBarrierRejectsCaptureAndNormalReleaseWithoutTakingTheResponse()
    {
        await using var hold = new AccountSyncB1cResponseHold();
        using var response = Response(HttpStatusCode.OK, out var content);
        Reject(() => hold.CaptureAsync(response, Configuration(1), Session, CancellationToken.None));
        Reject(() => hold.Release());
        hold.Release(cleanup: true);
        Assert.IsFalse(hold.Snapshot().Released);
        await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
        Assert.IsTrue(hold.Captured.IsCanceled);
        Assert.IsTrue(await RequireCapturedFailure(hold.Captured) is OperationCanceledException);
        Reject(() => hold.Arm("late-get", "GET", User, Pet, Session));
        Reject(() => hold.CaptureAsync(response, Configuration(1), Session, CancellationToken.None));
        Assert.IsFalse(content.Disposed);
    }

    [TestMethod]
    [DataRow("late-get", "GET", 1L)]
    [DataRow("late-patch", "PATCH", 2L)]
    public async Task DisposeBeforeCaptureSettlesCapturedAndRejectsTheUnusedArm(string name, string method, long version)
    {
        await using var hold = Armed(name, method);
        var capturedTask = hold.Captured;
        Assert.IsFalse(capturedTask.IsCompleted);
        await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
        Assert.AreSame(capturedTask, hold.Captured);
        Assert.IsTrue(capturedTask.IsCanceled);
        Assert.IsTrue(await RequireCapturedFailure(capturedTask) is OperationCanceledException);
        using var response = Response(HttpStatusCode.OK, out var content, Configuration(version));
        Reject(() => hold.CaptureAsync(response, Configuration(version), Session, CancellationToken.None));
        Assert.IsFalse(content.Disposed);
        var snapshot = hold.Snapshot();
        Assert.IsFalse(snapshot.ActualSuccessfulResponse || snapshot.Released || snapshot.CancellationObserved);
        await hold.DisposeAsync().AsTask().WaitAsync(Watchdog);
        Assert.IsTrue(capturedTask.IsCanceled);
    }

    private static AccountSyncB1cResponseHold Armed(string name, string method, TimeSpan? bound = null)
    {
        var hold = new AccountSyncB1cResponseHold(bound);
        hold.Arm(name, method, User, Pet, Session);
        return hold;
    }

    private static PetConfigurationSnapshot Configuration(long version, int eyeSize = 103)
    {
        using var document = JsonDocument.Parse("""
            {"appearanceSchemaVersion":3,"bodyColorHex":"#FFE9B5","eyeColorHex":"#000000","eyeSizePercent":103,"eyeOvalityPercent":0,"eyeHeightControlPercent":49,"eyeSpacingControlPercent":23}
            """.Replace("\"eyeSizePercent\":103", "\"eyeSizePercent\":" + eyeSize, StringComparison.Ordinal));
        return new(User, Pet, version, AppearanceContract.ValidateAndClone(document.RootElement));
    }

    private static void AssertIdentity(AccountSyncB1cHoldSnapshot snapshot, string name, string method, long version)
    {
        Assert.AreEqual(name, snapshot.Case);
        Assert.AreEqual(method, snapshot.Method);
        Assert.AreEqual(User, snapshot.UserId);
        Assert.AreEqual(Pet, snapshot.PetId);
        Assert.AreEqual(Session, snapshot.SessionId);
        Assert.AreEqual(version, snapshot.ResponseVersion);
        Assert.IsTrue(snapshot.ActualSuccessfulResponse, "This flag records acceptance at the barrier, not proof of a real server response.");
    }

    private static void Reject(Action operation)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(operation);
        Assert.AreEqual("accountsync_b1c_hold_rejected", error.Message);
    }

    private static async Task WaitForOwnedTask(Task owned)
    {
        using var watchdogLifetime = new CancellationTokenSource();
        var watchdog = Task.Delay(Watchdog, watchdogLifetime.Token);
        try
        {
            Assert.AreSame(owned, await Task.WhenAny(owned, watchdog),
                "The captured task itself, not a timeout wrapper, must have completed.");
        }
        finally
        {
            watchdogLifetime.Cancel();
            await ObserveCancelledDelay(watchdog);
        }
    }

    private static async Task RequireCancelled(Task owned)
    {
        try { await owned; Assert.Fail("Fixture disposal cannot release a successful response."); }
        catch (OperationCanceledException) { }
    }

    private static async Task<Exception> ObserveFailure(Task owned)
    {
        try { await owned; Assert.Fail("The owned operation must terminate unsuccessfully."); }
        catch (Exception error) when (error is not AssertFailedException) { return error; }
        throw new InvalidOperationException("unreachable_successful_failure_observation");
    }

    private static async Task<Exception> RequireCapturedFailure(Task capturedTask)
    {
        Assert.IsTrue(capturedTask.IsCompleted, "A rejected body or fixture disposal must settle the owned capture checkpoint.");
        Assert.IsFalse(capturedTask.IsCompletedSuccessfully);
        var observation = ObserveFailure(capturedTask);
        var failure = await observation.WaitAsync(Watchdog);
        Assert.IsTrue(observation.IsCompletedSuccessfully);
        return failure;
    }

    private static async Task ObserveCancelledDelay(Task delay)
    {
        try { await delay; }
        catch (OperationCanceledException) { }
    }

    private static HttpResponseMessage Response(HttpStatusCode status, out ObservedContent content,
        PetConfigurationSnapshot? configuration = null)
    {
        configuration ??= Configuration(1);
        content = new(JsonBody(configuration), "application/json");
        return new(status) { Content = content };
    }

    private static string JsonBody(PetConfigurationSnapshot configuration) => JsonSerializer.Serialize(new
    {
        userId = configuration.UserId,
        petId = configuration.PetId,
        configurationVersion = configuration.ConfigurationVersion,
        configuration = configuration.Configuration
    });

    private sealed class ObservedContent : HttpContent
    {
        private readonly byte[] body;
        private readonly bool knownLength;
        internal ObservedContent(string bodyText, string? mediaType, bool knownLength = true)
        {
            BodyText = bodyText;
            body = Encoding.UTF8.GetBytes(bodyText);
            this.knownLength = knownLength;
            if (mediaType is not null) Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }
        internal string BodyText { get; }
        internal bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();
        protected override bool TryComputeLength(out long length) { length = body.Length; return knownLength; }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }

    private sealed class WaitingContent : HttpContent
    {
        private readonly CancellationTokenSource cleanup = new();
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool disposed;
        internal WaitingContent() => Headers.ContentType = new MediaTypeHeaderValue("application/json");
        internal Task Entered => entered.Task;
        internal Task? SerializationTask { get; private set; }
        internal bool ReadTokenCancellationObserved { get; private set; }
        internal bool Disposed => disposed;
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Start(CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => Start(cancellationToken);
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        private Task Start(CancellationToken cancellationToken)
        {
            SerializationTask = Wait(cancellationToken);
            entered.TrySetResult();
            return SerializationTask;
        }
        private async Task Wait(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cleanup.Token);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token); }
            finally { ReadTokenCancellationObserved = cancellationToken.IsCancellationRequested; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                cleanup.Cancel();
                cleanup.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
