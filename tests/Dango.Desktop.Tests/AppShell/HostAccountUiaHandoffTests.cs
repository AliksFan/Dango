using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.AccountSyncReview.Tests;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Pets;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Persistence;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.AppShell;

// The complete production AppHost is started. Only HTTP and the secret store are controlled;
// native rendering, HWND/UIA, saved appearance, dispatcher timer and file journals are real.
[TestClass]
[DoNotParallelize]
[TestCategory("OfflineHostAccountUiaHandoff")]
public sealed class HostAccountUiaHandoffTests
{
    private static readonly Uri Origin = new("https://127.0.0.1:54443/");
    private const string NativeSha256 = "BFDD6CD68835540569A7D96EFCDE9B4580846983BDD717111D6BC68811150507";

    [STATestMethod]
    public void FullHostIndependentUiaSaveAndClosedPanelRealTimerUseDurableFilesWithoutEcho()
    {
        Execute("normal-timer", rig =>
        {
            rig.Backend.A.Nickname = null;
            var baseline = rig.Backend.A.Snapshot();
            rig.Login(rig.Backend.A);
            rig.Wait(() => rig.Controller.IsLoggedIn && rig.PrimaryEnabled && rig.PrimaryName == "保存昵称");
            Assert.IsFalse(rig.Controller.ConfigurationReady);
            Assert.IsNull(rig.Controller.RoomClient);
            rig.Uia(client => { client.Value("AccountNickname", "宿主同步团子"); client.InvokeId("AccountPrimary"); });
            rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
            rig.Uia(client => Assert.AreEqual("宿主同步团子", client.Name("AccountButton")));
            rig.Mark("nickname-confirmed", capture: true);
            rig.CloseAccount();

            rig.Navigator.SaveEyeSize(111);
            var uploadBytes = rig.ReadDiskBytes();
            var uploadConfiguration = rig.ReadDiskConfiguration();
            rig.Wait(() => rig.Backend.A.ConfigurationWrites == 1 && rig.ReceiptCount(rig.Backend.A, "configuration-sync") == 1);
            Assert.IsTrue(AppearanceContract.Equivalent(uploadConfiguration, rig.Backend.A.Snapshot().Configuration));
            Assert.AreEqual(1, rig.SuccessfulSaveEvents);
            var uploaded = rig.Archives(rig.Backend.A, "configuration-sync").Single();
            rig.AssertArchive(uploaded, rig.Backend.A, uploadBytes, uploadConfiguration, baseline,
                PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 2);

            var savedPump = rig.CurrentPump;
            rig.Wait(() => savedPump.IsCompleted);
            savedPump.GetAwaiter().GetResult();
            // This idle check and the remote mutation share one owner Dispatcher turn.
            // No saved-to-disk pump can still reconcile the new remote version.
            Assert.IsTrue(rig.CurrentPump.IsCompleted);
            var reads = rig.Backend.A.PetReads;
            var remote = rig.Backend.CommitExternal(rig.Backend.A, Configuration("#9ECBEF", 118));
            // No manual Pump/Notify: the production two-second dispatcher timer must fetch this version.
            rig.Wait(() => AppearanceContract.Equivalent(remote.Configuration, rig.ReadDiskConfiguration())
                && rig.ReceiptCount(rig.Backend.A, "configuration-sync") == 2);
            rig.Wait(() => rig.Backend.A.PetReads >= reads + 2);
            Assert.IsFalse(rig.Home.IsAccountPanelOpen);
            Assert.AreEqual(1, rig.Backend.A.ConfigurationWrites);
            Assert.AreEqual(1, rig.SuccessfulSaveEvents, "A remote Apply cannot emit the explicit-local-save event.");
            Assert.IsTrue(AppearanceContract.Equivalent(remote.Configuration, rig.CaptureLocal().NormalizedConfiguration));
            var applied = rig.Archives(rig.Backend.A, "configuration-sync").Single(item => item.Id != uploaded.Id);
            rig.AssertArchive(applied, rig.Backend.A, uploadBytes, uploadConfiguration, remote,
                PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 3);
            Assert.AreEqual(4, Directory.GetFiles(rig.JournalDirectory(rig.Backend.A, "configuration-sync")).Length);
            rig.AssertHealthyFrame();
            rig.Mark("normal-complete", capture: true);
        });
    }

    [STATestMethod]
    public void FullHostLateTimerGetCrossesUiaLogoutAndNewAccountWaitsForItsActualPump()
        => Execute("late-get", rig => Handoff(rig, holdPatch: false));

    [STATestMethod]
    public void FullHostCommittedPatchAckCrossesUiaLogoutAndCannotBecomeTheNextAccountsBaseline()
        => Execute("late-patch", rig => Handoff(rig, holdPatch: true));

    private static void Handoff(Rig rig, bool holdPatch)
    {
        var aBaseline = rig.Backend.A.Snapshot();
        rig.Login(rig.Backend.A);
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        rig.CloseAccount();
        rig.Backend.Arm(holdPatch ? HeldKind.Patch : HeldKind.Get, rig.Backend.A);
        byte[]? submittedBytes = null;
        JsonElement? submittedConfiguration = null;
        if (holdPatch)
        {
            rig.Navigator.SaveEyeSize(111);
            submittedBytes = rig.ReadDiskBytes();
            submittedConfiguration = rig.ReadDiskConfiguration();
        }
        else rig.Backend.CommitExternal(rig.Backend.A, Configuration("#9ECBEF", 121));

        rig.Wait(() => rig.Backend.Held is not null);
        var held = rig.Backend.Held!;
        var oldPump = rig.CurrentPump;
        var oldSync = Field<PetConfigurationLiveSyncCoordinator?>(rig.Controller, "configurationSync");
        Assert.IsNotNull(oldSync);
        Assert.IsFalse(oldPump.IsCompleted);
        Assert.AreEqual(rig.Backend.A.UserId, held.Request.UserId);
        Assert.AreEqual(rig.Backend.A.PetId, held.Request.PetId);
        Assert.AreEqual(rig.Backend.A.SessionId, held.Request.SessionId);
        Assert.AreEqual(holdPatch, held.Committed);
        if (holdPatch)
        {
            Assert.AreEqual(1L, held.Request.ExpectedVersion);
            Assert.IsNotNull(submittedConfiguration);
            Assert.IsTrue(AppearanceContract.Equivalent(submittedConfiguration.Value, held.Request.Configuration!.Value));
            Assert.AreEqual(2L, held.Response.ConfigurationVersion);
            Assert.AreEqual(1, rig.Backend.A.ConfigurationWrites);
        }
        rig.Backend.PreviousPump = oldPump;
        rig.Mark("old-request-held");

        rig.OpenAccount();
        rig.Uia(client => client.InvokeName("退出登录"));
        rig.Wait(() => !rig.Controller.IsLoggedIn && held.CancellationObserved);
        Assert.AreEqual(PetLiveSyncStatus.Stopped, oldSync.State.Status);
        Assert.IsFalse(rig.ConfigurationTimer.IsEnabled);
        Assert.IsFalse(oldPump.IsCompleted, "The transport deliberately ignores cancellation until its response is released.");
        Assert.AreEqual(0, rig.Backend.A.LogoutRequests, "Logout is queued behind the held FormalAccountClient request.");
        rig.Mark("local-logout-before-old-response");

        // Closing really cancels the queued logout; it does not revoke the controlled server session.
        rig.CloseAccount();
        rig.Wait(() => Field<SemaphoreSlim>(rig.Controller, "operations").CurrentCount == 1);
        Assert.IsTrue(Field<bool>(rig.Controller, "rememberedCleanupNeeded"));
        Assert.IsFalse(oldPump.IsCompleted);
        rig.Navigator.SaveEyeSize(118);
        var offlineBytes = rig.ReadDiskBytes();
        var offlineConfiguration = rig.ReadDiskConfiguration();
        Assert.AreEqual(118d, offlineConfiguration.GetProperty("eyeSizePercent").GetDouble());
        Assert.IsFalse(oldPump.IsCompleted);
        var deletes = rig.Secrets.Deletes;
        rig.OpenAccount();
        rig.Uia(client => client.InvokeName("重试清除记住登录"));
        rig.Wait(() => !Field<bool>(rig.Controller, "busy") && rig.PrimaryEnabled && rig.PrimaryName == "登录");
        Assert.IsFalse(Field<bool>(rig.Controller, "rememberedCleanupNeeded"));
        Assert.IsTrue(rig.Secrets.Deletes > deletes);
        Assert.AreEqual(0, rig.Backend.A.LogoutRequests);

        rig.Login(rig.Backend.B);
        rig.Wait(() => rig.Backend.B.ProfileReads == 1 && rig.Controller.IsLoggedIn
            && rig.Controller.Nickname == rig.Backend.B.Nickname && Field<bool>(rig.Controller, "busy"));
        Assert.AreEqual(rig.Backend.B.UserId, Field<AccountIdentity?>(rig.Controller, "identity")!.UserId);
        Assert.IsFalse(rig.Controller.ConfigurationReady);
        Assert.AreEqual(1, rig.Backend.B.PetReads, "Only login identity resolution may precede the old-pump drain.");
        Assert.IsFalse(oldPump.IsCompleted);
        rig.Uia(client =>
        {
            Assert.IsFalse(client.HasId("AccountCloudChoice"));
            Assert.AreEqual(rig.Backend.B.Nickname, client.Name("AccountButton"));
        });
        CollectionAssert.AreEqual(offlineBytes, rig.ReadDiskBytes());
        rig.Mark("new-account-identity-awaiting-old-pump");

        held.Release();
        rig.Wait(() => oldPump.IsCompleted && rig.HasChoice && !Field<bool>(rig.Controller, "busy") && !rig.PrimaryEnabled);
        oldPump.GetAwaiter().GetResult();
        Assert.IsTrue(held.Settled);
        Assert.AreEqual(PetLiveSyncStatus.Stopped, oldSync.State.Status);
        Assert.IsFalse(rig.Controller.ConfigurationReady);
        rig.Uia(client =>
        {
            Assert.IsFalse(client.Selected("AccountLocalChoice"));
            Assert.IsFalse(client.Selected("AccountCloudChoice"));
            Assert.IsFalse(client.Enabled("AccountPrimary"), "A source must be explicitly selected before confirmation is available.");
        });
        CollectionAssert.AreEqual(offlineBytes, rig.ReadDiskBytes(), "The old response must not replace an offline success-save.");
        Assert.IsTrue(AppearanceContract.Equivalent(offlineConfiguration, rig.CaptureLocal().NormalizedConfiguration));
        var comparisons = rig.Backend.Requests.Where(row => row.UserId == rig.Backend.B.UserId
            && row.Path == "/api/v1/me/pet" && row.PetReadOrdinal >= 2).ToArray();
        Assert.AreEqual(1, comparisons.Length);
        Assert.IsTrue(comparisons.All(row => row.PreviousPumpSettled == true), "B's actual baseline GET must follow A's actual pump settlement.");
        Assert.AreEqual(holdPatch ? 1 : 0, rig.Backend.A.ConfigurationWrites);
        Assert.AreEqual(0, rig.Backend.B.ConfigurationWrites);
        Assert.AreEqual(0, rig.Backend.A.LogoutRequests, "A cancelled queued Logout never reached the controlled receiver.");
        var aRequests = rig.Backend.Requests.Count(row => row.UserId == rig.Backend.A.UserId);
        rig.Mark("old-response-settled-before-new-comparison", capture: true);

        var bBaseline = rig.Backend.B.Snapshot();
        rig.Uia(client =>
        {
            client.Select("AccountCloudChoice");
            Assert.IsTrue(client.Selected("AccountCloudChoice"));
            Assert.IsFalse(client.Selected("AccountLocalChoice"));
            Assert.IsTrue(client.Enabled("AccountPrimary"));
            client.InvokeId("AccountPrimary");
        });
        rig.Wait(() => rig.Controller.ConfigurationReady && rig.PrimaryEnabled);
        Assert.AreEqual(rig.Backend.B.PetId, rig.Controller.RoomClient!.PetId);
        Assert.IsTrue(AppearanceContract.Equivalent(bBaseline.Configuration, rig.ReadDiskConfiguration()));
        var choice = rig.Archives(rig.Backend.B, "configuration-choices").Single();
        rig.AssertArchive(choice, rig.Backend.B, offlineBytes, offlineConfiguration, bBaseline,
            PetConfigurationChoice.Cloud, PetChoiceOutcome.LocalApplied, 1);
        rig.CloseAccount();
        rig.Navigator.SaveEyeSize(125);
        var bSavedBytes = rig.ReadDiskBytes();
        var bSavedConfiguration = rig.ReadDiskConfiguration();
        rig.Wait(() => rig.Backend.B.ConfigurationWrites == 1 && rig.ReceiptCount(rig.Backend.B, "configuration-sync") == 1);
        var reads = rig.Backend.B.PetReads;
        rig.Wait(() => rig.Backend.B.PetReads >= reads + 2);
        Assert.AreEqual(aRequests, rig.Backend.Requests.Count(row => row.UserId == rig.Backend.A.UserId), "Stopped A cannot start any later HTTP request.");
        Assert.AreEqual(holdPatch ? 1 : 0, rig.Backend.A.ConfigurationWrites);
        Assert.AreEqual(1, rig.Backend.B.ConfigurationWrites);
        Assert.IsTrue(AppearanceContract.Equivalent(bSavedConfiguration, rig.Backend.B.Snapshot().Configuration));
        CollectionAssert.AreEqual(bSavedBytes, rig.ReadDiskBytes());
        var bUpload = rig.Archives(rig.Backend.B, "configuration-sync").Single();
        rig.AssertArchive(bUpload, rig.Backend.B, bSavedBytes, bSavedConfiguration, bBaseline,
            PetConfigurationChoice.Local, PetChoiceOutcome.CloudUpdated, 2);
        var writes = rig.Backend.Requests.Where(row => row.Method == "PATCH" && row.Path == "/api/v1/me/pet/configuration").ToArray();
        Assert.AreEqual(holdPatch ? 2 : 1, writes.Length);
        Assert.IsTrue(writes.All(row => row.UserId == rig.Backend.A.UserId
            ? holdPatch && row.SessionId == held.Request.SessionId && row.ExpectedVersion == 1
            : row.UserId == rig.Backend.B.UserId && row.PetId == rig.Backend.B.PetId
                && row.SessionId == rig.Backend.B.SessionId && row.ExpectedVersion == 1));
        if (holdPatch)
        {
            Assert.IsTrue(AppearanceContract.Equivalent(submittedConfiguration!.Value, rig.Backend.A.Snapshot().Configuration));
            var aArchive = rig.Archives(rig.Backend.A, "configuration-sync").Single();
            rig.AssertOriginal(aArchive, rig.Backend.A, submittedBytes!, submittedConfiguration!.Value,
                aBaseline, PetConfigurationChoice.Local);
            Assert.IsNull(rig.LoadReceipt(aArchive), "A's cancelled success response cannot produce a fake acknowledged receipt.");
        }
        else Assert.AreEqual(0, rig.Archives(rig.Backend.A, "configuration-sync").Length);
        rig.Uia(client => Assert.AreEqual(rig.Backend.B.Nickname, client.Name("AccountButton")));
        rig.AssertHealthyFrame();
        rig.Mark("handoff-complete", capture: true);
    }

    private static void Execute(string name, Action<Rig> journey)
    {
        Rig? rig = null;
        var passed = false;
        try { rig = new(name); journey(rig); passed = true; }
        finally { rig?.Finish(passed); }
    }

    private static T Field<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("Owned host field missing: " + name);
        return (T)field.GetValue(instance)!;
    }

    private static JsonElement Configuration(string color = "#FFE8C2", double size = 103) => JsonSerializer.SerializeToElement(new
    {
        appearanceSchemaVersion = 3, bodyColorHex = color, eyeColorHex = "#272321", eyeSizePercent = size,
        eyeOvalityPercent = 0d, eyeHeightControlPercent = 51d, eyeSpacingControlPercent = 24.5d
    });

    private sealed record Archive(Guid Id, string Directory);
    private sealed record RequestFact(int Sequence, int Client, string Method, string Path, Guid? UserId,
        Guid? PetId, Guid? SessionId, int? PetReadOrdinal, long? ExpectedVersion, JsonElement? Configuration,
        bool? PreviousPumpSettled);
    private sealed record PhaseFact(int Sequence, string Phase, int? RequestSequence);
    private enum HeldKind { Get, Patch }

    private sealed class Account(string actor)
    {
        internal string Actor { get; } = actor;
        internal string Email { get; } = actor + "@full-host-fixture.invalid";
        internal Guid UserId { get; } = Guid.NewGuid();
        internal Guid PetId { get; } = Guid.NewGuid();
        internal Guid SessionId;
        internal string? Nickname = actor == "a" ? "旧账号团子" : "新账号团子";
        internal long ProfileVersion = 1, ConfigurationVersion = 1;
        internal JsonElement Configuration = HostAccountUiaHandoffTests.Configuration();
        internal int ProfileReads, PetReads, ConfigurationWrites, LogoutRequests;
        internal PetConfigurationSnapshot Snapshot() => new(UserId, PetId, ConfigurationVersion, Configuration.Clone());
    }

    private sealed class HeldResponse(Backend backend, RequestFact request, PetConfigurationSnapshot response, bool committed)
    {
        internal RequestFact Request { get; } = request;
        internal PetConfigurationSnapshot Response { get; } = response;
        internal bool Committed { get; } = committed;
        internal TaskCompletionSource ReleaseSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile bool CancellationObserved, Released, Settled;
        internal void Release(bool cleanup = false)
        {
            lock (backend.Gate)
            {
                if (Released) { Assert.IsTrue(cleanup, "A normal fault phase cannot be released twice."); return; }
                Released = true; backend.Phase("response-released", Request.Sequence); ReleaseSignal.TrySetResult();
            }
        }
    }

    private sealed class Backend
    {
        internal readonly object Gate = new();
        internal Account A { get; } = new("a");
        internal Account B { get; } = new("b");
        private readonly List<RequestFact> requests = [];
        private readonly List<PhaseFact> phases = [];
        private readonly List<Task> tasks = [];
        private (HeldKind Kind, Guid UserId)? armed;
        private int clientCount;
        internal HeldResponse? Held { get; private set; }
        internal Task? PreviousPump;
        internal RequestFact[] Requests { get { lock (Gate) return requests.ToArray(); } }
        internal PhaseFact[] Phases { get { lock (Gate) return phases.ToArray(); } }
        internal Task[] Tasks { get { lock (Gate) return tasks.ToArray(); } }
        internal Receiver CreateReceiver(CookieContainer cookies)
        {
            lock (Gate) { Assert.IsTrue(clientCount < 4); return new(this, ++clientCount, cookies); }
        }
        internal void Arm(HeldKind kind, Account account)
        {
            lock (Gate) { Assert.IsNull(armed); Assert.IsNull(Held); armed = (kind, account.UserId); Phase("response-armed", null); }
        }
        internal PetConfigurationSnapshot CommitExternal(Account account, JsonElement configuration)
        {
            lock (Gate)
            {
                account.Configuration = AppearanceContract.ValidateAndClone(configuration); account.ConfigurationVersion++;
                Phase("controlled-external-version", null); return account.Snapshot();
            }
        }
        internal void Phase(string phase, int? request)
        {
            lock (Gate) { Assert.IsTrue(phases.Count < 256); phases.Add(new(phases.Count + 1, phase, request)); }
        }
        internal void Track(Task task)
        {
            lock (Gate) { Assert.IsTrue(tasks.Count < 128); tasks.Add(task); }
            _ = task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        internal RequestFact Record(int client, HttpRequestMessage request, Account? account, long? expected, JsonElement? configuration)
        {
            lock (Gate)
            {
                Assert.IsTrue(requests.Count < 128, "Controlled full-host HTTP must remain bounded.");
                var path = request.RequestUri!.AbsolutePath;
                int? ordinal = null;
                if (path == "/api/v1/me/pet") ordinal = ++(account ?? throw new AssertFailedException("Pet read must be account-bound.")).PetReads;
                var result = new RequestFact(requests.Count + 1, client, request.Method.Method, path,
                    account?.UserId, account?.PetId, account?.SessionId, ordinal, expected, configuration?.Clone(),
                    PreviousPump is null ? null : PreviousPump.IsCompleted);
                requests.Add(result); Phase("request-arrived", result.Sequence); return result;
            }
        }
        internal HeldResponse? TryHold(HeldKind kind, RequestFact request, PetConfigurationSnapshot response)
        {
            lock (Gate)
            {
                if (armed != (kind, request.UserId!.Value)) return null;
                Assert.IsNull(Held); armed = null; Held = new(this, request, response, kind == HeldKind.Patch);
                Phase(kind == HeldKind.Patch ? "patch-committed-response-held" : "get-response-held", request.Sequence);
                return Held;
            }
        }
    }

    private sealed class Receiver(Backend backend, int client, CookieContainer cookies) : HttpMessageHandler
    {
        private Account? account;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var task = SendCoreAsync(request, ct); backend.Track(task); return task;
        }
        private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var uri = request.RequestUri ?? throw new AssertFailedException("Controlled request needs its exact origin.");
            Assert.AreEqual(Origin, new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"));
            var path = uri.AbsolutePath;
            long? expected = null; JsonElement? configuration = null;
            if (path == "/api/v1/auth/login")
            {
                Assert.AreEqual(HttpMethod.Post, request.Method);
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                account = body.GetProperty("email").GetString() switch
                {
                    var email when email == backend.A.Email => backend.A,
                    var email when email == backend.B.Email => backend.B,
                    _ => throw new AssertFailedException("Only the two fictional full-host accounts are allowed.")
                };
                account.SessionId = Guid.NewGuid();
                cookies.Add(Origin, new Cookie("__Host-Dango.Auth", "fictional_" + account.SessionId.ToString("N"), "/")
                    { Secure = true, HttpOnly = true, Expires = DateTime.UtcNow.AddHours(7) });
            }
            if (account is not null && path != "/api/v1/auth/login")
                Assert.AreEqual("fictional_" + account.SessionId.ToString("N"), cookies.GetCookies(Origin)["__Host-Dango.Auth"]?.Value,
                    "Each controlled authenticated request must keep its client's own synthetic session cookie.");
            if (path == "/api/v1/me/pet/configuration")
            {
                Assert.AreEqual(HttpMethod.Patch, request.Method); Assert.IsNotNull(account);
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                expected = body.GetProperty("expectedVersion").GetInt64();
                configuration = AppearanceContract.ValidateAndClone(body.GetProperty("configuration"));
            }
            var fact = backend.Record(client, request, account, expected, configuration);
            HttpResponseMessage response;
            PetConfigurationSnapshot? pet = null;
            lock (backend.Gate)
            {
                switch (path)
                {
                    case "/api/v1/auth/request-protection":
                        Assert.AreEqual(HttpMethod.Get, request.Method); response = Json(new { requestToken = "fictional_full_host_request_token" }); break;
                    case "/api/v1/auth/login":
                        response = Json(new { userId = account!.UserId, sessionId = account.SessionId, expiresAt = DateTimeOffset.UtcNow.AddHours(7) }); break;
                    case "/api/v1/me":
                        Assert.AreEqual(HttpMethod.Get, request.Method); Assert.IsNotNull(account); response = Json(new { userId = account.UserId }); break;
                    case "/api/v1/me/profile" when request.Method == HttpMethod.Get:
                        Assert.IsNotNull(account); account.ProfileReads++; response = Json(new { userId = account.UserId, nickname = account.Nickname, profileVersion = account.ProfileVersion }); break;
                    case "/api/v1/me/pet":
                        Assert.AreEqual(HttpMethod.Get, request.Method); Assert.IsNotNull(account); pet = account.Snapshot(); response = Json(pet); break;
                    case "/api/v1/me/pet/configuration":
                        Assert.IsNotNull(account); Assert.AreEqual(expected, account.ConfigurationVersion);
                        account.Configuration = configuration!.Value; account.ConfigurationVersion++; account.ConfigurationWrites++;
                        pet = account.Snapshot(); response = Json(pet); break;
                    case "/api/v1/auth/logout":
                        Assert.AreEqual(HttpMethod.Post, request.Method); Assert.IsNotNull(account); account.LogoutRequests++;
                        response = Json(new { status = "signed_out" }); break;
                    case "/api/v1/me/profile" when request.Method == HttpMethod.Patch:
                        response = null!; break;
                    default: throw new AssertFailedException("Unexpected controlled full-host request path or verb.");
                }
            }
            if (response is null)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                lock (backend.Gate)
                {
                    Assert.IsNotNull(account); Assert.AreEqual(body.GetProperty("expectedVersion").GetInt64(), account.ProfileVersion);
                    account.Nickname = AccountNickname.Normalize(body.GetProperty("nickname").GetString()); account.ProfileVersion++;
                    response = Json(new { userId = account.UserId, nickname = account.Nickname, profileVersion = account.ProfileVersion });
                }
            }
            HeldResponse? held = pet is null ? null : backend.TryHold(path == "/api/v1/me/pet" ? HeldKind.Get : HeldKind.Patch, fact, pet);
            try
            {
                if (held is not null)
                {
                    using var cancelled = ct.Register(() =>
                    {
                        lock (backend.Gate) { held.CancellationObserved = true; backend.Phase("held-request-cancellation-observed", fact.Sequence); }
                    });
                    // Ignore cancellation here deliberately. The actual late response crosses the UI logout boundary.
                    await held.ReleaseSignal.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    lock (backend.Gate) { held.Settled = true; backend.Phase("held-response-returned", fact.Sequence); }
                }
                backend.Phase("request-settled", fact.Sequence); return response;
            }
            catch { response.Dispose(); backend.Phase("request-faulted", fact.Sequence); throw; }
        }
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }

    private sealed class Rig
    {
        private readonly SynchronizationContext? previous = SynchronizationContext.Current;
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        private readonly string name;
        private readonly DangoUserDataPaths paths;
        private readonly List<object> transitions = [];
        private readonly HashSet<Task> pumps = [];
        private readonly List<Task> uiaTasks = [];
        private readonly HashSet<PetConfigurationLiveSyncCoordinator> coordinators = [];
        private DangoApplicationHost? host;
        private ControlCenterWindow? window;
        private DesktopAccountController? controller;
        private AppearanceSettingsSession? appearance;
        private StackedDangoSceneRuntime? runtime;
        private nint handle;
        private Task? pendingUia;
        private volatile bool finishing;
        private volatile bool independentUiaObserved;
        private bool finished;
        private bool hostStarted, nativeHashVerified, healthyFrameObserved;
        internal Backend Backend { get; } = new();
        internal MemorySecrets Secrets { get; } = new();
        internal ProductHomeView Home => window!.ProductHome;
        internal DesktopAccountController Controller => controller ?? throw new AssertFailedException("The production account controller is required.");
        internal DispatcherTimer ConfigurationTimer => Field<DispatcherTimer>(Controller, "configurationTimer");
        internal Task CurrentPump { get { RememberPump(); return Field<Task>(Controller, "configurationPump"); } }
        internal int SuccessfulSaveEvents { get; private set; }
        internal AccountSyncAppearanceNavigator Navigator { get; private set; } = null!;
        internal bool PrimaryEnabled => ((Button)Home.AccountPanel.FindName("AccountPrimary")).IsEnabled;
        internal string PrimaryName => AutomationProperties.GetName((Button)Home.AccountPanel.FindName("AccountPrimary"));
        internal bool HasChoice => Home.AccountPanel.FindName("AccountCloudChoice") is RadioButton;

        internal Rig(string name)
        {
            this.name = name;
            var root = Environment.GetEnvironmentVariable("DANGO_TEST_EVIDENCE_ROOT")
                ?? Path.Combine(TestRepository.Root, "artifacts", "host-account-uia-handoff");
            var directory = Path.Combine(root, name + "-" + Guid.NewGuid().ToString("N"));
            Assert.IsFalse(Directory.Exists(directory)); Directory.CreateDirectory(directory);
            paths = new(Path.Combine(directory, "settings.json"));
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                var native = Path.Combine(TestRepository.Root, "artifacts", "local-room", "account-profile-sync-20261010", "bin", "Dango.Desktop", "release", "dango_physics_module.dll");
                Assert.IsTrue(File.Exists(native), "The existing isolated native candidate is required; no fake-scene fallback is allowed.");
                Assert.AreEqual(NativeSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native))));
                nativeHashVerified = true;
                new AppearanceSettingsStore().Save(paths.AppearancePath, new DangoAppearanceConfiguration(
                    OpaqueSrgbColor.Parse("#FFE8C2"), OpaqueSrgbColor.Parse("#272321"), new EyeLayoutParameters(103)));
                var dependencies = new DangoAccountDependencies(Origin, Secrets, (origin, store) =>
                {
                    Assert.AreEqual(Origin, origin); Assert.AreSame(Secrets, store);
                    var cookies = new CookieContainer();
                    return FormalAccountClient.ForTesting(origin, Backend.CreateReceiver(cookies), cookies, store);
                });
                host = DangoApplicationHost.CreateForAccountIntegration(paths.SettingsPath, native, dependencies);
                runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
                host.Start();
                hostStarted = true;
                window = Field<ControlCenterWindow>(host, "controlCenterWindow");
                controller = Field<DesktopAccountController>(host, "accountController");
                appearance = Field<AppearanceSettingsSession>(host, "appearance");
                appearance.SavedToDisk += OnSaved;
                window.Left = 80; window.Top = 80; window.ShowInTaskbar = false;
                handle = new WindowInteropHelper(window).Handle; Assert.AreNotEqual(nint.Zero, handle);
                Uia(client => client.AssertOwnedWindow());
                Assert.AreSame(appearance, Home.BoundAppearance);
                Assert.AreSame(Secrets, Field<IAccountSecretStore>(Controller, "secrets"));
                Assert.AreEqual(TimeSpan.FromSeconds(2), ConfigurationTimer.Interval);
                Navigator = new(id => Uia(client => client.InvokeId(id)),
                    (id, value) => Uia(client => client.Range(id, value)), Wait, ReadNavigation);
                Wait(() => PrimaryEnabled && !Field<bool>(Controller, "busy"));
                AssertHealthyFrame(); Mark("host-ready");
            }
            catch { Finish(false); throw; }
        }

        private void OnSaved() => SuccessfulSaveEvents++;
        internal void Login(Account account)
        {
            if (!Home.IsAccountPanelOpen) OpenAccount();
            Wait(() => PrimaryEnabled && PrimaryName == "登录");
            Uia(client => client.Value("AccountEmail", account.Email));
            // PasswordBox has no readable ValuePattern. Only this fixture's synthetic password is supplied in-process.
            ((PasswordBox)Home.AccountPanel.FindName("AccountPassword")).Password = "fictional_full_host_only_password";
            Uia(client => client.InvokeId("AccountPrimary"));
        }
        internal void OpenAccount()
        {
            Uia(client => client.InvokeId("AccountButton"));
            Wait(() => Home.IsAccountPanelOpen);
        }
        internal void CloseAccount()
        {
            Uia(client => client.InvokeName("继续本地游玩"));
            Wait(() => !Home.IsAccountPanelOpen);
        }
        internal byte[] ReadDiskBytes() => File.ReadAllBytes(paths.AppearancePath);
        internal JsonElement ReadDiskConfiguration() { using var parsed = JsonDocument.Parse(ReadDiskBytes()); return AppearanceContract.ValidateAndClone(parsed.RootElement); }
        internal LocalPetConfigurationSnapshot CaptureLocal() => Complete(new LocalPetConfigurationRepository(paths.AppearancePath, appearance!, Home.Dispatcher).CaptureAsync(CancellationToken.None));
        internal string JournalDirectory(Account account, string slot) => Path.Combine(paths.GetAccountDirectory(Origin, account.UserId, account.PetId), slot);
        internal Archive[] Archives(Account account, string slot)
        {
            var directory = JournalDirectory(account, slot);
            return Directory.Exists(directory) ? Directory.GetFiles(directory, "*.originals.json")
                .Select(path => new Archive(Guid.ParseExact(Path.GetFileName(path).Split('.')[0], "N"), directory)).ToArray() : [];
        }
        internal int ReceiptCount(Account account, string slot) => Directory.Exists(JournalDirectory(account, slot))
            ? Directory.GetFiles(JournalDirectory(account, slot), "*.receipt.json").Length : 0;
        internal PetConfigurationOriginals LoadOriginal(Archive archive) => Complete(new PetConfigurationFileJournal(archive.Directory).LoadOriginalsAsync(archive.Id, CancellationToken.None));
        internal PetConfigurationReceipt? LoadReceipt(Archive archive) => Complete(new PetConfigurationFileJournal(archive.Directory).LoadReceiptAsync(archive.Id));
        internal void AssertArchive(Archive archive, Account account, byte[] localBytes, JsonElement localConfiguration,
            PetConfigurationSnapshot cloud, PetConfigurationChoice choice, PetChoiceOutcome outcome, long observed)
        {
            AssertOriginal(archive, account, localBytes, localConfiguration, cloud, choice);
            var originals = LoadOriginal(archive); var receipt = LoadReceipt(archive);
            Assert.IsNotNull(receipt);
            Assert.AreEqual(choice, receipt.Choice); Assert.AreEqual(outcome, receipt.Outcome);
            Assert.AreEqual((long?)observed, receipt.ObservedCloudVersion); Assert.IsNull(receipt.FailureCode);
            Assert.IsTrue(receipt.RecordedAt >= originals.CapturedAt);
        }
        internal void AssertOriginal(Archive archive, Account account, byte[] localBytes, JsonElement localConfiguration,
            PetConfigurationSnapshot cloud, PetConfigurationChoice choice)
        {
            var originals = LoadOriginal(archive);
            Assert.AreEqual(Origin, originals.Origin); Assert.IsTrue(originals.Local.Exists);
            CollectionAssert.AreEqual(localBytes, originals.Local.OriginalBytes.ToArray());
            Assert.IsTrue(AppearanceContract.Equivalent(localConfiguration, originals.Local.NormalizedConfiguration));
            Assert.AreEqual(account.UserId, originals.Cloud.UserId); Assert.AreEqual(account.PetId, originals.Cloud.PetId);
            Assert.AreEqual(cloud.ConfigurationVersion, originals.Cloud.ConfigurationVersion);
            Assert.IsTrue(AppearanceContract.Equivalent(cloud.Configuration, originals.Cloud.Configuration));
            Assert.AreEqual(choice, originals.Choice);
        }
        internal void AssertHealthyFrame()
        {
            var scene = runtime ?? throw new AssertFailedException("The actual native runtime is required.");
            var visual = (DeformableDangoVisual)Home.FindName("LiveDangoVisual");
            bool Healthy() => window!.IsVisible && Home.IsVisible && !Home.IsInPorch && visual.IsVisible
                && PresentationSource.FromVisual(visual) is not null
                && visual.LastContourFailure == DangoContourFailure.None
                && visual.RenderedBodyBottom is { } bottom && double.IsFinite(bottom)
                && scene.InitializationState == SceneInitializationState.Ready
                && scene.LastPhysicsFrame is { Succeeded: true } && scene.LastFrame is { Bodies.Count: > 0 }
                && scene.LastError is null && scene.IsRunning
                && Home.OwnsLiveFrameDriver;
            Wait(Healthy);
            Assert.IsTrue(scene.EntityIds.Count > 0);
            var frame = scene.LastPhysicsFrame;
            Wait(() => !ReferenceEquals(frame, scene.LastPhysicsFrame) && Healthy());
            var rendered = false;
            EventHandler rendering = (_, _) => { if (Healthy()) rendered = true; };
            CompositionTarget.Rendering += rendering;
            try { Wait(() => rendered); }
            finally { CompositionTarget.Rendering -= rendering; }
            Assert.IsTrue(Healthy()); healthyFrameObserved = true;
        }
        internal void Mark(string phase, bool capture = false)
        {
            Assert.IsTrue(transitions.Count < 128);
            var screenshot = capture ? SaveOwnWindow(phase) : null;
            transitions.Add(new { Phase = phase, UserId = Field<AccountIdentity?>(Controller, "identity")?.UserId,
                Controller.Nickname, Controller.ConfigurationReady, AccountOpen = Home.IsAccountPanelOpen,
                PumpSettled = CurrentPump.IsCompleted, TimerEnabled = ConfigurationTimer.IsEnabled,
                AppearanceSha256 = Convert.ToHexString(SHA256.HashData(ReadDiskBytes())), Screenshot = screenshot });
        }
        private string SaveOwnWindow(string phase)
        {
            AssertHealthyFrame();
            window!.UpdateLayout();
            var width = (int)Math.Ceiling(window.ActualWidth);
            var height = (int)Math.Ceiling(window.ActualHeight);
            Assert.IsTrue(width is > 0 and <= 2048 && height is > 0 and <= 2048
                && (long)width * height <= 4_194_304, "Only bounded pixels from this fixture's own WPF window may be recorded.");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var name = phase + ".png";
            using var file = new FileStream(Path.Combine(paths.RootDirectory, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            encoder.Save(file); file.Flush(true);
            return name;
        }
        private AccountSyncAppearanceNavigationState ReadNavigation()
        {
            bool Ready(string id) => Home.FindName(id) is FrameworkElement
                { IsVisible: true, IsEnabled: true, IsHitTestVisible: true, ActualWidth: > 0, ActualHeight: > 0 };
            var editor = !Home.IsEditing ? AccountSyncAppearanceEditor.None : Home.SelectedEditor == HomeEditor.Face
                ? AccountSyncAppearanceEditor.Face : AccountSyncAppearanceEditor.Wardrobe;
            return new(Home.IsAccountPanelOpen, Home.IsSettingsOpen, Home.IsInPorch, Home.IsDangoAway, editor,
                Ready("FaceButton"), Ready("AppearanceEyesTab"), Ready("AppearanceEyesPanel") && Ready("EyeSizeSlider"),
                Ready("ApplyButton"), Ready("CancelAppearanceButton"), Ready(editor == AccountSyncAppearanceEditor.Face
                    ? "FaceCloseEditorButton" : "CloseEditorButton"), appearance!.HasPendingSave,
                appearance.DraftAppearance.EyeLayout.SizePercent, appearance.SavedAppearance.EyeLayout.SizePercent);
        }
        internal void Uia(Action<UiaClient> action)
        {
            Assert.IsFalse(finishing); Assert.IsTrue(pendingUia is null || pendingUia.IsCompleted);
            Assert.IsTrue(uiaTasks.Count < 128);
            var task = pendingUia = Task.Run(() =>
            {
                Assert.AreNotEqual(ownerThread, Environment.CurrentManagedThreadId);
                independentUiaObserved = true;
                action(new UiaClient(handle, () => !finishing));
            });
            uiaTasks.Add(task);
            Observe(task);
            try { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
            finally { if (task.IsCompleted) pendingUia = null; }
        }
        internal void Wait(Func<bool> condition)
        {
            RememberPump(); if (condition()) return;
            var frame = new DispatcherFrame(); var elapsed = Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => { RememberPump(); if (condition() || elapsed.Elapsed >= TimeSpan.FromSeconds(8)) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Assert.IsTrue(condition(), "The bounded complete-host HWND/HTTP/dispatcher transition did not settle.");
        }
        private T Complete<T>(Task<T> task) { Wait(() => task.IsCompleted); return task.GetAwaiter().GetResult(); }
        private void RememberPump()
        {
            if (controller is null) return;
            var actual = Field<Task>(controller, "configurationPump");
            if (pumps.Add(actual)) { Assert.IsTrue(pumps.Count <= 128); Observe(actual); }
            var sync = Field<PetConfigurationLiveSyncCoordinator?>(controller, "configurationSync");
            if (sync is not null) { coordinators.Add(sync); Assert.IsTrue(coordinators.Count <= 4); }
        }
        private static void Observe(Task task) => _ = task.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        internal void Finish(bool journeyPassed)
        {
            if (finished) return; finished = true; finishing = true;
            var failures = new List<object>();
            var hostDisposed = false; var hwndClosed = false; var uiSettled = false; var pumpsSettled = false;
            var httpSettled = false; var timerStopped = false;
            var hostTimersStopped = false; var runtimeStopped = false; var homeDriverReleased = false;
            var coordinatorsStopped = false; var accountOperationsDrained = false;
            void Cleanup(string step, Action action)
            {
                try { action(); } catch (Exception error) { failures.Add(new { Step = step, FailureType = error.GetType().Name }); }
            }
            try
            {
                Cleanup("RememberPump", RememberPump);
                Cleanup("ControllerStop", () => { controller?.Dispose(); timerStopped = controller is null || !ConfigurationTimer.IsEnabled; });
                Cleanup("ReleaseFault", () => Backend.Held?.Release(cleanup: true));
                Cleanup("HostDispose", () => { host?.Dispose(); hostDisposed = true; });
                Cleanup("HostTimersStopped", () =>
                {
                    hostTimersStopped = host is not null && !Field<DispatcherTimer>(host, "geometrySaveTimer").IsEnabled
                        && !Field<DispatcherTimer>(host, "roomSaveTimer").IsEnabled;
                    Assert.IsTrue(hostTimersStopped);
                });
                Cleanup("RuntimeStopped", () =>
                {
                    runtimeStopped = runtime is not null && !runtime.IsRunning && runtime.FrameDriver is null;
                    Assert.IsTrue(runtimeStopped);
                });
                Cleanup("HomeDriverReleased", () => { homeDriverReleased = window is not null && !Home.OwnsLiveFrameDriver; Assert.IsTrue(homeDriverReleased); });
                Cleanup("HwndClose", () => { Wait(() => handle == nint.Zero || !IsWindow(handle)); hwndClosed = handle != nint.Zero && !IsWindow(handle); });
                Cleanup("UiaDrain", () =>
                {
                    Wait(() => uiaTasks.All(task => task.IsCompleted));
                    foreach (var task in uiaTasks) task.GetAwaiter().GetResult();
                    uiSettled = true;
                });
                Cleanup("PumpDrain", () => { Wait(() => pumps.All(task => task.IsCompleted)); foreach (var task in pumps) task.GetAwaiter().GetResult(); pumpsSettled = true; });
                Cleanup("HttpDrain", () => { Wait(() => Backend.Tasks.All(task => task.IsCompleted)); foreach (var task in Backend.Tasks) task.GetAwaiter().GetResult(); httpSettled = true; });
                Cleanup("AccountOperationsDrain", () =>
                {
                    Wait(() => controller is null || Field<SemaphoreSlim>(controller, "operations").CurrentCount == 1);
                    accountOperationsDrained = controller is not null && Field<SemaphoreSlim>(controller, "operations").CurrentCount == 1;
                    Assert.IsTrue(accountOperationsDrained);
                });
                // The production fire-and-forget DisposeAfterPumpAsync task is not observable here.
                // Record the observable original tasks and Stopped coordinator state, without claiming that task was awaited.
                Cleanup("CoordinatorsStopped", () => { coordinatorsStopped = coordinators.Count > 0 && coordinators.All(sync => sync.State.Status == PetLiveSyncStatus.Stopped); Assert.IsTrue(coordinatorsStopped); });
                Cleanup("SaveEventUnsubscribe", () => { if (appearance is not null) appearance.SavedToDisk -= OnSaved; });
                Cleanup("MemorySecretsDispose", Secrets.Dispose);
                Cleanup("Evidence", () =>
                {
                    using var file = new FileStream(Path.Combine(paths.RootDirectory, "journey-evidence.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = 1, Case = name, JourneyPassed = journeyPassed,
                        UsesCompleteAppHost = hostStarted, HostCreated = host is not null,
                        UsesActualNative = nativeHashVerified && healthyFrameObserved,
                        NativeHashVerified = nativeHashVerified, HealthyFrameObserved = healthyFrameObserved, UsesDatabase = false,
                        UsesTlsListener = false, UsesWindowsCredentials = false, UsesIndependentUiaClient = independentUiaObserved,
                        HostDisposed = hostDisposed, HwndClosed = hwndClosed, UiaSettled = uiSettled,
                        PumpsSettled = pumpsSettled, HttpSettled = httpSettled, TimerStopped = timerStopped,
                        HostTimersStopped = hostTimersStopped, RuntimeStopped = runtimeStopped,
                        HomeDriverReleased = homeDriverReleased, CoordinatorsStopped = coordinatorsStopped,
                        AccountOperationsDrained = accountOperationsDrained,
                        ObservedPumpCount = pumps.Count, UiaTaskCount = uiaTasks.Count, HttpTaskCount = Backend.Tasks.Length,
                        SuccessfulSaveEvents, Requests = Backend.Requests, Phases = Backend.Phases, Transitions = transitions,
                        Held = Backend.Held is { } held ? new { held.Committed, held.CancellationObserved, held.Released, held.Settled } : null,
                        Failures = failures.ToArray() });
                    Assert.IsTrue(bytes.Length <= 131072); file.Write(bytes); file.Flush(true);
                });
                if (journeyPassed)
                {
                    Assert.IsTrue(hostDisposed && hwndClosed && uiSettled && pumpsSettled && httpSettled && timerStopped
                        && hostTimersStopped && runtimeStopped && homeDriverReleased && coordinatorsStopped && accountOperationsDrained);
                    Assert.AreEqual(0, failures.Count, "Every owned cleanup step must settle independently.");
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }

    private sealed class UiaClient(nint handle, Func<bool> active)
    {
        private AutomationElement Root
        {
            get
            {
                Assert.IsTrue(active() && IsWindow(handle));
                var root = AutomationElement.FromHandle(handle); Assert.AreEqual(Process.GetCurrentProcess().Id, root.Current.ProcessId); return root;
            }
        }
        private AutomationElement Required(string id) => Root.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            ?? throw new AssertFailedException("Owned complete-host UIA control missing: " + id);
        internal void AssertOwnedWindow() => _ = Root;
        private AutomationElement Ready(string id)
        {
            var element = Required(id); Assert.IsTrue(element.Current.IsEnabled && !element.Current.IsOffscreen); Assert.IsTrue(active()); return element;
        }
        internal bool HasId(string id) => Root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id)) is not null;
        internal string Name(string id) => Required(id).Current.Name;
        internal bool Enabled(string id) => Required(id).Current.IsEnabled;
        internal bool Selected(string id) => ((SelectionItemPattern)Ready(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected;
        internal void Value(string id, string value) => ((ValuePattern)Ready(id).GetCurrentPattern(ValuePattern.Pattern)).SetValue(value);
        internal void Select(string id) => ((SelectionItemPattern)Ready(id).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        internal void Range(string id, double value) => ((RangeValuePattern)Ready(id).GetCurrentPattern(RangeValuePattern.Pattern)).SetValue(value);
        internal void InvokeId(string id) => ((InvokePattern)Ready(id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        internal void InvokeName(string name)
        {
            var button = Root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                ?? throw new AssertFailedException("Owned complete-host UIA button missing: " + name);
            Assert.IsTrue(button.Current.IsEnabled && !button.Current.IsOffscreen && active());
            ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        }
    }

    private sealed class MemorySecrets : IAccountSecretStore, IDisposable
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);
        internal int Deletes;
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Assert.AreEqual(Origin, origin);
            lock (values) return Task.FromResult(values.TryGetValue(slot, out var bytes) ? bytes.ToArray() : null);
        }
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Assert.AreEqual(Origin, origin);
            lock (values) { Delete(slot); values[slot] = value.ToArray(); } return Task.CompletedTask;
        }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Assert.AreEqual(Origin, origin);
            lock (values) { Deletes++; Delete(slot); } return Task.CompletedTask;
        }
        private void Delete(string slot) { if (values.Remove(slot, out var bytes)) CryptographicOperations.ZeroMemory(bytes); }
        public void Dispose() { lock (values) { foreach (var bytes in values.Values) CryptographicOperations.ZeroMemory(bytes); values.Clear(); } }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint handle);
}
