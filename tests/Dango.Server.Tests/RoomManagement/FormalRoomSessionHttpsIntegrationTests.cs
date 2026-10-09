using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Dango.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dango.Server.Tests.RoomManagement;

// Each method creates only its own fictional graph. No live/expiry/email worker is enabled.
[TestClass]
[DoNotParallelize]
[TestCategory("PostgresIntegration")]
[TestCategory("BackendB5CIntegration")]
public sealed class FormalRoomSessionHttpsIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [TestMethod]
    [Timeout(90_000)]
    public async Task RealMembersAttachAndHeartbeatWhileHttpsAndRoomOwnershipAreProtected()
    {
        await using var fixture = await FixtureAsync();
        using var owner = Client(fixture); using var guest = Client(fixture);
        var account = await RegisterAsync(fixture, owner.Account);
        var visitor = await RegisterAsync(fixture, guest.Account);
        var management = owner.Account.CreateRoomManagementClient(account.Identity);
        var created = await management.CreateAsync(new(Guid.NewGuid(), "publicShared", "Fictional B5C members", 6), None);
        var own = Access(fixture, owner, account.Identity); var other = Access(fixture, guest, visitor.Identity);
        var code = await own.ResetCodeAsync(created.RoomId, new(Guid.NewGuid(), created.AppliedVersion), None);
        Assert.AreEqual(created.RoomId, (await other.ResolveAsync(new(code.Code), None)).RoomId);
        await using var first = new FormalRoomSession(own, Guid.NewGuid());
        await using var second = new FormalRoomSession(other, Guid.NewGuid());
        await first.OpenAsync(); await second.OpenAsync();
        await first.EnterAsync(created.RoomId); await second.EnterAsync(created.RoomId, code.Code);
        await first.RefreshAsync();
        Awaiting(first, created.RoomId, FormalRoomRole.Control);
        Awaiting(second, created.RoomId, FormalRoomRole.Control);
        CollectionAssert.AreEquivalent(new[] { account.Identity.PetId, visitor.Identity.PetId },
            first.Current.Shared!.Occupants.Select(p => p.PetId).ToArray());
        Assert.AreEqual(2, second.Current.Shared!.Occupants.Length);
        var lease = OwnLease(second);
        var heartbeat = await other.Connections.Last().HeartbeatAsync(new(lease.LeaseId, second.Current.InstanceId), None);
        Assert.AreEqual(lease.LeaseId, heartbeat.LeaseId);
        Assert.AreEqual(created.RoomId, (await other.Connections.Last().SnapshotAsync(created.RoomId,
            new(lease.LeaseId, second.Current.InstanceId), None)).RoomId);
        var before = await AssignmentGraphAsync(fixture, account.Identity.PetId, visitor.Identity.PetId);
        var personal = await other.PersonalAsync(None);
        await FaultAsync(other.EnterAsync(account.DesktopRoomId, new(Guid.NewGuid(), second.Current.InstanceId, "desktop",
            personal.RoomId, personal.AssignmentVersion), None), RoomAccessFailure.Rejected, "room_access_forbidden");
        await FaultAsync(other.SnapshotAsync(account.DesktopRoomId, new(lease.LeaseId, second.Current.InstanceId), None),
            RoomAccessFailure.Rejected, "room_access_forbidden");
        Assert.AreEqual(before, await AssignmentGraphAsync(fixture, account.Identity.PetId, visitor.Identity.PetId));
        using (var raw = Raw(fixture, guest.Account.Cookies))
        {
            using (var missing = await FormalTestFixture.SendAsync(raw, HttpMethod.Post,
                "api/v1/room-hub/negotiate?negotiateVersion=1", csrf: false))
                await ErrorAsync(missing, HttpStatusCode.Forbidden, "request_protection_invalid");
            using (var foreign = await FormalTestFixture.SendAsync(raw, HttpMethod.Post,
                "api/v1/room-hub/negotiate?negotiateVersion=1", origin: "https://example.invalid"))
                await ErrorAsync(foreign, HttpStatusCode.Forbidden, "origin_invalid");
        }
        Assert.IsTrue(owner.Transport.Writes.Concat(guest.Transport.Writes).All(w => w.HasCsrf));
        Assert.IsTrue(own.HubTransport.UnsafeRequests > 0 && other.HubTransport.UnsafeRequests > 0);
        Assert.IsTrue(own.HubTransport.AllUnsafeProtected && other.HubTransport.AllUnsafeProtected);
        await CountsAsync(fixture, [account, visitor], 2, 1, 2, 1, 6, 8);
    }

    [TestMethod]
    [Timeout(90_000)]
    public async Task TwoSessionsObserveExplicitControlCasAndExactObserverEndBeforeControllerLeave()
    {
        await using var fixture = await FixtureAsync();
        using var one = Client(fixture); using var two = Client(fixture);
        var account = await RegisterAsync(fixture, one.Account);
        var identity = await two.Account.LoginAsync(account.Email, account.Password, false, None);
        var firstAccess = Access(fixture, one, account.Identity); var secondAccess = Access(fixture, two, identity);
        await using var first = new FormalRoomSession(firstAccess, Guid.NewGuid());
        await using var second = new FormalRoomSession(secondAccess, Guid.NewGuid());
        await first.OpenAsync(); await second.OpenAsync();
        await first.EnterAsync(account.DesktopRoomId); await second.EnterAsync(account.DesktopRoomId);
        await first.RefreshAsync();
        Awaiting(first, account.DesktopRoomId, FormalRoomRole.Observe);
        Awaiting(second, account.DesktopRoomId, FormalRoomRole.Control);
        var stale = Control(second);
        await first.TakeControlAsync();
        await FaultAsync(secondAccess.Connections.Last().ControlAsync(stale, None), RoomAccessFailure.Conflict, "room_assignment_conflict");
        await second.RefreshAsync();
        Awaiting(second, account.DesktopRoomId, FormalRoomRole.Observe);
        await FaultAsync(secondAccess.Connections.Last().LeaveAsync(Control(second), None),
            RoomAccessFailure.Conflict, "room_control_conflict");
        var controllingLease = OwnLease(first).LeaseId;
        await second.EndCurrentLeaseAsync();
        var surviving = await firstAccess.PersonalAsync(None);
        Assert.AreEqual(controllingLease, surviving.ControlLeaseId);
        Assert.AreEqual(account.DesktopRoomId, surviving.RoomId);
        await first.LeaveAsync();
        var left = await firstAccess.PersonalAsync(None);
        Assert.IsNull(left.RoomId); Assert.IsNull(left.ControlLeaseId); Assert.AreEqual(0, left.Leases.Length);
        await CountsAsync(fixture, [account], 2, 0, 2, 0, 6, 6);
    }

    [TestMethod]
    [Timeout(90_000)]
    public async Task SuspendKeepsLeaseResumeUsesNewTransportAndDeadlineCannotRevivePrivateOrPublicPresence()
    {
        var clock = Clock();
        await using var fixture = await FixtureAsync(clock);
        using var client = Client(fixture);
        var account = await RegisterAsync(fixture, client.Account);
        var access = Access(fixture, client, account.Identity);
        var management = client.Account.CreateRoomManagementClient(account.Identity);
        var created = await management.CreateAsync(new(Guid.NewGuid(), "publicShared", "Fictional B5C expiry", 6), None);
        await using var session = new FormalRoomSession(access, Guid.NewGuid());
        await session.OpenAsync(); await session.EnterAsync(account.DesktopRoomId);
        var original = OwnLease(session).LeaseId;
        // Real replacement transports do not allocate business leases or elect a controller.
        await using (var old = access.CreateConnection())
        await using (var replacement = access.CreateConnection())
        {
            await old.StartAsync(None); await old.AttachAsync(new(original, session.Current.InstanceId), None);
            await replacement.StartAsync(None); await replacement.AttachAsync(new(original, session.Current.InstanceId), None);
            await FaultAsync(old.HeartbeatAsync(new(original, session.Current.InstanceId), None),
                RoomAccessFailure.Conflict, "room_transport_conflict");
            await replacement.HeartbeatAsync(new(original, session.Current.InstanceId), None);
        }
        var writes = client.Transport.Writes.Length;
        await session.SuspendAsync();
        Assert.AreEqual(writes, client.Transport.Writes.Length, "Suspend must not leave or end a lease.");
        clock.Advance(TimeSpan.FromSeconds(20));
        access.ClearTrace(); await session.ResumeAsync();
        Awaiting(session, account.DesktopRoomId, FormalRoomRole.Control);
        Assert.AreEqual(original, OwnLease(session).LeaseId);
        AssertRecoveryTrace(access.Trace);
        Assert.AreEqual(writes, client.Transport.Writes.Length, "Resume must not Enter or take control.");
        await session.SuspendAsync(); clock.Advance(TimeSpan.FromSeconds(30));
        await session.ResumeAsync();
        Assert.AreEqual(FormalRoomSessionPhase.ReentryRequired, session.Current.Phase);
        Assert.IsTrue(session.Current.Personal!.IsSleeping);
        Assert.AreEqual(account.DesktopRoomId, session.Current.Personal.RoomId);
        Assert.AreEqual(writes, client.Transport.Writes.Length);
        await using (var expired = access.CreateConnection())
        {
            await expired.StartAsync(None);
            await FaultAsync(expired.AttachAsync(new(original, session.Current.InstanceId), None),
                RoomAccessFailure.Conflict, "room_lease_expired");
        }
        await session.EnterAsync(created.RoomId);
        var publicLease = OwnLease(session).LeaseId;
        await session.SuspendAsync(); clock.Advance(TimeSpan.FromSeconds(30)); await session.ResumeAsync();
        Assert.AreEqual(FormalRoomSessionPhase.ReentryRequired, session.Current.Phase);
        Assert.IsNull(session.Current.Personal!.RoomId); Assert.IsFalse(session.Current.Personal.IsSleeping);
        Assert.IsFalse(session.Current.ServerAttached);
        await using (var expired = access.CreateConnection())
        {
            await expired.StartAsync(None);
            await FaultAsync(expired.AttachAsync(new(publicLease, session.Current.InstanceId), None),
                RoomAccessFailure.Conflict, "room_lease_expired");
        }
        await CountsAsync(fixture, [account], 1, 1, 2, 0, 6, 7);
    }

    [TestMethod]
    [Timeout(90_000)]
    public async Task LostCommittedEnterReplaysOriginalKeyButAdoptsCurrentObserverReplacementNotHistoricalReceipt()
    {
        await using var fixture = await FixtureAsync();
        using var one = Client(fixture); using var two = Client(fixture);
        var account = await RegisterAsync(fixture, one.Account);
        var identity = await two.Account.LoginAsync(account.Email, account.Password, false, None);
        var management = one.Account.CreateRoomManagementClient(account.Identity);
        var roomA = await management.CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional B5C first"), None);
        var roomB = await management.CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional B5C current"), None);
        var accessA = Access(fixture, one, account.Identity); var accessB = Access(fixture, two, identity);
        await using var first = new FormalRoomSession(accessA, Guid.NewGuid());
        await using var second = new FormalRoomSession(accessB, Guid.NewGuid());
        await first.OpenAsync(); await second.OpenAsync();
        one.Transport.LoseNext = "entered";
        var before = one.Transport.Writes.Length;
        await FaultAsync(first.EnterAsync(roomA.RoomId), RoomAccessFailure.Unavailable, "request_outcome_unknown");
        Assert.AreEqual(before + 1, one.Transport.Writes.Length);
        Assert.AreEqual(FormalRoomSessionPhase.Unknown, first.Current.Phase);
        var original = one.Transport.LastEnter!;
        var originalReceipt = await accessA.EnterAsync(roomA.RoomId, original, None);
        var oldLease = originalReceipt.LeaseId!.Value;
        var pendingKey = first.Current.Pending!.RequestId;
        Assert.AreEqual(original.RequestId, pendingKey);
        await FaultAsync(first.EnterAsync(roomB.RoomId), RoomAccessFailure.Rejected, "room_pending_operation");
        await second.EnterAsync(roomB.RoomId);
        var controller = OwnLease(second).LeaseId;
        await first.RetryPendingAsync();
        Awaiting(first, roomB.RoomId, FormalRoomRole.Observe);
        Assert.IsNull(first.Current.Pending);
        Assert.AreNotEqual(oldLease, OwnLease(first).LeaseId);
        Assert.AreEqual(controller, first.Current.Personal!.ControlLeaseId);
        Assert.AreEqual(3, one.Transport.Writes.Count(w => w.Operation == "entered"));
        await FaultAsync(accessA.EnterAsync(roomA.RoomId, original with { DeviceKind = "mobile" }, None),
            RoomAccessFailure.Conflict, "room_request_conflict");
        var graph = await AssignmentGraphAsync(fixture, account.Identity.PetId);
        await FaultAsync(accessA.EndLeaseAsync(new(Guid.NewGuid(), oldLease, first.Current.InstanceId), None),
            RoomAccessFailure.Conflict, "room_lease_expired");
        Assert.AreEqual(graph, await AssignmentGraphAsync(fixture, account.Identity.PetId));
        var historical = await accessA.EnterAsync(roomA.RoomId, original, None);
        Assert.AreEqual(oldLease, historical.LeaseId); Assert.AreEqual(roomA.RoomId, historical.RoomId);
        Assert.AreEqual(roomB.RoomId, (await accessA.PersonalAsync(None)).RoomId);
        await CountsAsync(fixture, [account], 2, 2, 3, 0, 5, 7);
    }

    [TestMethod]
    [Timeout(90_000)]
    public async Task PresentationFailureEndsOnlyCapturedLeaseAndUnknownCompensationAndOldTokensCannotEndNewLease()
    {
        await using var fixture = await FixtureAsync();
        using var client = Client(fixture);
        var account = await RegisterAsync(fixture, client.Account);
        var created = await client.Account.CreateRoomManagementClient(account.Identity)
            .CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional B5C compensation"), None);
        var access = Access(fixture, client, account.Identity);
        await using var session = new FormalRoomSession(access, Guid.NewGuid());
        await session.OpenAsync(); await session.EnterAsync(created.RoomId);
        var oldToken = session.Current.PresentationToken!;
        client.Transport.LoseNext = "lease_ended";
        var count = client.Transport.Writes.Length;
        await FaultAsync(session.FailPresentationAsync(oldToken), RoomAccessFailure.Unavailable, "request_outcome_unknown");
        Assert.AreEqual(count + 1, client.Transport.Writes.Length);
        var pending = session.Current.Pending!.RequestId;
        Assert.AreEqual(FormalRoomOperation.EndLease, session.Current.Pending.Operation);
        Assert.AreEqual(oldToken.LeaseId, client.Transport.Writes.Last().LeaseId);
        await FaultAsync(session.TakeControlAsync(), RoomAccessFailure.Rejected, "room_pending_operation");
        await session.RetryPendingAsync();
        var endings = client.Transport.Writes.Where(w => w.Operation == "lease_ended").ToArray();
        Assert.AreEqual(2, endings.Length);
        Assert.IsTrue(endings.All(w => w.RequestId == pending && w.LeaseId == oldToken.LeaseId));
        Assert.IsNull(session.Current.Pending); Assert.IsTrue(session.Current.Personal!.IsSleeping);
        await session.EnterAsync(created.RoomId);
        var newLease = OwnLease(session).LeaseId;
        Assert.AreNotEqual(oldToken.LeaseId, newLease);
        count = client.Transport.Writes.Length;
        Assert.IsFalse(session.ConfirmPresentationReady(oldToken));
        await FaultAsync(session.FailPresentationAsync(oldToken), RoomAccessFailure.Rejected, "room_presentation_stale");
        Assert.AreEqual(count, client.Transport.Writes.Length);
        var adoptedToken = session.Current.PresentationToken!;
        await session.SuspendAsync(); await session.ResumeAsync();
        Assert.AreEqual(newLease, OwnLease(session).LeaseId);
        Assert.IsFalse(session.ConfirmPresentationReady(adoptedToken));
        await FaultAsync(session.FailPresentationAsync(adoptedToken), RoomAccessFailure.Rejected, "room_presentation_stale");
        Assert.AreEqual(count, client.Transport.Writes.Length);
        Awaiting(session, created.RoomId, FormalRoomRole.Control);
        await CountsAsync(fixture, [account], 1, 1, 2, 0, 6, 7);
    }

    [TestMethod]
    [Timeout(90_000)]
    public async Task ExactOutboxRetrySendsRealDuplicateNoticeWithoutWritesAndLogoutFencesOldBinding()
    {
        var checkpoint = new FailOnePublish();
        await using var fixture = await FixtureAsync(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IRoomOutboxCheckpoint>(checkpoint)));
        using var owner = Client(fixture); using var guest = Client(fixture);
        var account = await RegisterAsync(fixture, owner.Account); var visitor = await RegisterAsync(fixture, guest.Account);
        var management = owner.Account.CreateRoomManagementClient(account.Identity);
        var created = await management.CreateAsync(new(Guid.NewGuid(), "publicShared", "Fictional B5C notice", 6), None);
        var own = Access(fixture, owner, account.Identity); var other = Access(fixture, guest, visitor.Identity);
        var code = await own.ResetCodeAsync(created.RoomId, new(Guid.NewGuid(), created.AppliedVersion), None);
        await using var first = new FormalRoomSession(own, Guid.NewGuid());
        await using var second = new FormalRoomSession(other, Guid.NewGuid());
        await first.OpenAsync(); await second.OpenAsync();
        await first.EnterAsync(created.RoomId); await second.EnterAsync(created.RoomId, code.Code);
        await first.RefreshAsync();
        var oldToken = first.Current.PresentationToken!;
        var request = new RoomUpdateRequest(Guid.NewGuid(), (await management.ReadAsync(created.RoomId, None)).Version,
            "Fictional B5C updated through notice", 6);
        await management.UpdateAsync(created.RoomId, request, None);
        var eventId = await fixture.InScopeAsync(async services => await services.GetRequiredService<DangoDbContext>()
            .RoomOperationAudits.AsNoTracking().Where(a => a.ActorUserId == account.Identity.UserId && a.RequestId == request.RequestId)
            .Select(a => a.Id).SingleAsync());
        checkpoint.EventId = eventId;
        var writes = owner.Transport.Writes.Length + guest.Transport.Writes.Length;
        var ownerReads = own.Trace.Count(s => s == "hub.snapshot");
        var visitorReads = other.Trace.Count(s => s == "hub.snapshot");
        var firstDispatch = await DispatchAsync(fixture, eventId);
        Assert.AreEqual(1, firstDispatch.Failed); Assert.AreEqual(0, firstDispatch.Published);
        var secondDispatch = await DispatchAsync(fixture, eventId);
        Assert.AreEqual(1, secondDispatch.Published); Assert.AreEqual(0, secondDispatch.Failed);
        await UntilAsync(() => own.Notices.Count(n => n.EventId == eventId) == 2
            && other.Notices.Count(n => n.EventId == eventId) == 2
            && first.Current.Shared?.Name == request.Name && second.Current.Shared?.Name == request.Name);
        // Observe beyond the two one-second coalescing slots, so a continuing refresh loop fails.
        await Task.Delay(TimeSpan.FromMilliseconds(2200));
        Assert.AreEqual(writes, owner.Transport.Writes.Length + guest.Transport.Writes.Length,
            "An invalidation notice must not Enter, control, leave, end or update a room.");
        Assert.AreEqual(0, (await DispatchAsync(fixture, eventId)).Examined);
        Assert.IsTrue(own.Notices.Concat(other.Notices).All(n => n.EventId == eventId));
        Assert.IsTrue(own.Trace.Count(s => s == "hub.snapshot") - ownerReads is >= 1 and <= 2
            && other.Trace.Count(s => s == "hub.snapshot") - visitorReads is >= 1 and <= 2,
            "Two duplicate hints must have bounded refresh cost.");
        await owner.Account.LogoutAsync(None);
        await first.ResetAsync();
        Assert.IsFalse(first.ConfirmPresentationReady(oldToken));
        var before = owner.Transport.RequestCount;
        await FaultAsync(own.PersonalAsync(None), RoomAccessFailure.Authentication, "room_binding_changed");
        Assert.AreEqual(before, owner.Transport.RequestCount, "An obsolete binding must fail before HTTP.");
        var identity = await owner.Account.LoginAsync(account.Email, account.Password, false, None);
        await FaultAsync(own.PersonalAsync(None), RoomAccessFailure.Authentication, "room_binding_changed");
        var fresh = Access(fixture, owner, identity);
        await using var resumed = new FormalRoomSession(fresh, Guid.NewGuid());
        await resumed.OpenAsync();
        Assert.AreEqual(FormalRoomSessionPhase.ReentryRequired, resumed.Current.Phase);
        Assert.IsNull(resumed.Current.Personal!.RoomId);
        Assert.AreEqual(created.RoomId, (await other.PersonalAsync(None)).RoomId);
        Assert.IsFalse(resumed.Current.ServerAttached);
        await CountsAsync(fixture, [account, visitor], 3, 1, 2, 1, 8, 10, published: 1);
    }

    private static MutableTestClock Clock() => new(new(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero));
    private static Task<FormalTestFixture> FixtureAsync(MutableTestClock? clock = null,
        Action<IServiceCollection>? configure = null) => FormalTestFixture.CreateAsync(startHttp: true,
            clock: clock ?? Clock(), configure: configure, requireB5CApproval: true);
    private static ClientSession Client(FormalTestFixture fixture)
    {
        var cookies = new CookieContainer();
        var observed = new ObservedHttp(fixture.CreatePinnedHandler(cookies));
        return new(FormalAccountClient.ForTesting(fixture.Endpoint, observed, cookies, new MemorySecrets()), observed);
    }
    private static ObservedAccess Access(FormalTestFixture fixture, ClientSession client, AccountIdentity identity)
    {
        var observer = new HubHttpObserver();
        var access = client.Account.CreateRoomAccessClientForTesting(identity, handler =>
        {
            // Keep the product-created cookie owner. Pin only this fixture certificate.
            var transport = (HttpClientHandler)handler;
            using var pinned = fixture.CreatePinnedHandler(client.Account.Cookies);
            transport.ServerCertificateCustomValidationCallback = pinned.ServerCertificateCustomValidationCallback;
            return new ObservedHubHttp(handler, observer);
        });
        return new(access, observer);
    }
    private static HttpClient Raw(FormalTestFixture fixture, CookieContainer cookies) =>
        new(fixture.CreatePinnedHandler(cookies)) { BaseAddress = fixture.Endpoint, Timeout = TimeSpan.FromSeconds(20) };
    private static async Task<Registered> RegisterAsync(FormalTestFixture fixture, FormalAccountClient client)
    {
        var email = $"b5c-{Guid.NewGuid():N}@example.invalid";
        var proof = B1InputTests.NewProof(); var invitation = await fixture.InviteAsync(email, proof);
        var attempt = RegistrationAttempt.Create(email, invitation, proof);
        var pending = await client.StartRegistrationAsync(attempt, None);
        Assert.AreEqual("queued", pending.EmailStatus);
        var confirmation = await fixture.RegistrationProofAsync(pending.Id);
        using (var browser = fixture.CreateClient(out _))
        using (var response = await FormalTestFixture.SendAsync(browser, HttpMethod.Post,
            "api/v1/auth/email-verifications/confirm", new { proof = confirmation }))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var password = B1PostgresTests.NewPassword();
        var completed = await client.CompleteRegistrationAsync(attempt, password, B1InputTests.Parse(B1InputTests.ValidAppearance), None);
        var identity = await client.LoginAsync(email, password, false, None);
        Assert.AreEqual(completed.UserId, identity.UserId); Assert.AreEqual(completed.PetId, identity.PetId);
        return new(identity, completed.RoomId, email, password, invitation, pending.Id);
    }
    private static RoomLeaseView OwnLease(FormalRoomSession session) => session.Current.Personal!.Leases
        .Single(l => l.InstanceId == session.Current.InstanceId);
    private static RoomControlRequest Control(FormalRoomSession session) => new(Guid.NewGuid(), OwnLease(session).LeaseId,
        session.Current.InstanceId, session.Current.Personal!.RoomId!.Value,
        session.Current.Personal.AssignmentVersion, session.Current.Personal.ControlGeneration);
    private static void Awaiting(FormalRoomSession session, Guid roomId, FormalRoomRole role)
    {
        Assert.AreEqual(FormalRoomSessionPhase.AwaitingPresentation, session.Current.Phase);
        Assert.AreEqual(roomId, session.Current.Personal!.RoomId); Assert.AreEqual(role, session.Current.Role);
        Assert.IsTrue(session.Current.ServerAttached); Assert.IsFalse(session.Current.Presented);
        Assert.IsNotNull(session.Current.PresentationToken);
    }
    private static void AssertRecoveryTrace(string[] trace)
    {
        CollectionAssert.AreEqual(new[] { "http.personal", "hub.start", "hub.attach", "hub.snapshot", "hub.personal" }, trace);
    }
    private static async Task FaultAsync(Task work, RoomAccessFailure failure, string code)
    {
        var fault = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => work);
        Assert.AreEqual(failure, fault.Failure); Assert.AreEqual(code, fault.Code);
    }
    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    private static Task<RoomDispatchResult> DispatchAsync(FormalTestFixture fixture, Guid id) => fixture.InScopeAsync(services =>
        services.GetRequiredService<RoomOutboxDispatcher>().DispatchBatchAsync(1, [id], None));
    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) await Task.Delay(20, deadline.Token);
    }
    private static Task CountsAsync(FormalTestFixture fixture, Registered[] accounts, int sessions, int shared, int leases,
        int codes, int audits, int events, int published = 0) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var users = accounts.Select(a => a.Identity.UserId).ToArray(); var pets = accounts.Select(a => a.Identity.PetId).ToArray();
        var roomIds = await db.Rooms.AsNoTracking().Where(r => users.Contains(r.OwnerId)).Select(r => r.Id).ToArrayAsync();
        Assert.AreEqual(accounts.Length, await db.Users.CountAsync(u => users.Contains(u.Id)));
        Assert.AreEqual(accounts.Length, await db.Pets.CountAsync(p => pets.Contains(p.Id)));
        Assert.AreEqual(accounts.Length, await db.PetRoomStates.CountAsync(s => pets.Contains(s.PetId)));
        var invitations = accounts.Select(a => a.InvitationId).ToArray();
        var pending = accounts.Select(a => a.PendingId).ToArray();
        Assert.AreEqual(accounts.Length, await db.TestInvitations.CountAsync(i => invitations.Contains(i.Id)));
        Assert.AreEqual(accounts.Length, await db.PendingRegistrations.CountAsync(p => pending.Contains(p.Id)));
        var challenges = await db.EmailChallenges.AsNoTracking().Where(c => c.PendingRegistrationId != null
            && pending.Contains(c.PendingRegistrationId.Value)).Select(c => c.Id).ToArrayAsync();
        Assert.AreEqual(accounts.Length, challenges.Length);
        Assert.AreEqual(accounts.Length, await db.EmailJobs.CountAsync(j => challenges.Contains(j.ChallengeId)));
        Assert.IsFalse(await db.EmailJobs.AnyAsync(j => challenges.Contains(j.ChallengeId) && j.Attempts != 0));
        Assert.AreEqual(accounts.Length + shared, roomIds.Length);
        Assert.AreEqual(roomIds.Length, await db.RoomMemberships.CountAsync(m => roomIds.Contains(m.RoomId)));
        Assert.AreEqual(sessions, await db.AuthSessions.CountAsync(s => users.Contains(s.UserId)));
        Assert.AreEqual(leases, await db.RoomDeviceLeases.CountAsync(l => pets.Contains(l.PetId)), "Count revoked lease history too.");
        Assert.AreEqual(codes, await db.PublicRoomCredentials.CountAsync(c => roomIds.Contains(c.RoomId)));
        Assert.AreEqual(audits, await db.RoomOperationAudits.CountAsync(a => roomIds.Contains(a.RoomId)));
        Assert.AreEqual(events, await db.RoomDomainEvents.CountAsync(e => roomIds.Contains(e.RoomId)));
        Assert.AreEqual(published, await db.RoomDomainEvents.CountAsync(e => roomIds.Contains(e.RoomId) && e.PublishedAt != null));
    });
    private static Task<string> AssignmentGraphAsync(FormalTestFixture fixture, params Guid[] pets) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var states = await db.PetRoomStates.AsNoTracking().Where(s => pets.Contains(s.PetId)).OrderBy(s => s.PetId).ToArrayAsync();
        var leases = await db.RoomDeviceLeases.AsNoTracking().Where(l => pets.Contains(l.PetId)).OrderBy(l => l.Id).ToArrayAsync();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { states, leases })));
    });
    private sealed record Registered(AccountIdentity Identity, Guid DesktopRoomId, string Email, string Password,
        Guid InvitationId, Guid PendingId)
    { public override string ToString() => "Registered [redacted]"; }
    private sealed record ClientSession(FormalAccountClient Account, ObservedHttp Transport) : IDisposable
    { public void Dispose() => Account.Dispose(); }
    private sealed record Write(Guid RequestId, string Operation, Guid? LeaseId, bool HasCsrf);
    private sealed class MemorySecrets : IAccountSecretStore
    {
        private readonly Dictionary<(string, string), byte[]> values = [];
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) =>
            Task.FromResult(values.TryGetValue((origin.AbsoluteUri, slot), out var value) ? value.ToArray() : null);
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); values[(origin.AbsoluteUri, slot)] = value.ToArray(); return Task.CompletedTask; }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        { values.Remove((origin.AbsoluteUri, slot)); return Task.CompletedTask; }
    }
    private sealed class ObservedHttp(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly object sync = new(); private readonly List<Write> writes = []; private int requests;
        public Write[] Writes { get { lock (sync) return writes.ToArray(); } }
        public int RequestCount => Volatile.Read(ref requests);
        public string? LoseNext { get; set; }
        public RoomEnterRequest? LastEnter { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref requests);
            var path = request.RequestUri!.AbsolutePath;
            var operation = path.EndsWith("/enter", StringComparison.Ordinal) ? "entered"
                : path == "/api/v1/me/room/control" ? "control"
                : path == "/api/v1/me/room/leave" ? "left"
                : path == "/api/v1/me/room/lease/end" ? "lease_ended"
                : path.EndsWith("/code/reset", StringComparison.Ordinal) ? "code_reset"
                : path == "/api/v1/rooms" && request.Method == HttpMethod.Post ? "created"
                : path.StartsWith("/api/v1/rooms/", StringComparison.Ordinal) && request.Method == HttpMethod.Patch ? "updated" : null;
            if (operation is not null)
            {
                var bytes = await request.Content!.ReadAsByteArrayAsync(ct);
                using var json = JsonDocument.Parse(bytes);
                Guid? lease = json.RootElement.TryGetProperty("leaseId", out var value) ? value.GetGuid() : null;
                lock (sync)
                {
                    writes.Add(new(json.RootElement.GetProperty("requestId").GetGuid(), operation, lease,
                        request.Headers.Contains("X-CSRF-TOKEN")));
                    if (operation == "entered") LastEnter = JsonSerializer.Deserialize<RoomEnterRequest>(bytes, Json);
                }
            }
            var response = await base.SendAsync(request, ct);
            if (operation is null || operation != LoseNext) return response;
            LoseNext = null;
            var status = response.StatusCode; response.Dispose();
            Assert.AreEqual(HttpStatusCode.OK, status, "Only a committed real successful response may be lost.");
            throw new HttpRequestException("fictional_b5c_response_loss");
        }
    }
    private sealed class HubHttpObserver
    {
        private int requests, missing;
        public int UnsafeRequests => Volatile.Read(ref requests);
        public bool AllUnsafeProtected => Volatile.Read(ref missing) == 0;
        public void Observe(HttpRequestMessage request)
        {
            if (request.Method != HttpMethod.Post && request.Method != HttpMethod.Delete) return;
            Interlocked.Increment(ref requests);
            if (!request.Headers.Contains("X-CSRF-TOKEN")) Interlocked.Increment(ref missing);
        }
    }
    private sealed class ObservedHubHttp(HttpMessageHandler inner, HubHttpObserver observer) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { observer.Observe(request); return base.SendAsync(request, ct); }
    }
    private sealed class ObservedAccess(IRoomAccessClient inner, HubHttpObserver observer) : IRoomAccessClient
    {
        private readonly object sync = new(); private readonly List<string> trace = []; private readonly List<RoomChangeNotice> notices = [];
        public List<IFormalRoomConnection> Connections { get; } = [];
        public HubHttpObserver HubTransport => observer;
        public string[] Trace { get { lock (sync) return trace.ToArray(); } }
        public RoomChangeNotice[] Notices { get { lock (sync) return notices.ToArray(); } }
        private void Add(string method) { lock (sync) trace.Add(method); }
        public void ClearTrace() { lock (sync) trace.Clear(); }
        public Uri Origin => inner.Origin; public Guid UserId => inner.UserId; public Guid PetId => inner.PetId;
        public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct) { Add("http.personal"); return inner.PersonalAsync(ct); }
        public Task<SharedRoomSnapshot> SnapshotAsync(Guid room, RoomLeaseRequest request, CancellationToken ct) => inner.SnapshotAsync(room, request, ct);
        public Task<RoomAccessResult> EnterAsync(Guid room, RoomEnterRequest request, CancellationToken ct) => inner.EnterAsync(room, request, ct);
        public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct) => inner.ControlAsync(request, ct);
        public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct) => inner.LeaveAsync(request, ct);
        public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct) => inner.EndLeaseAsync(request, ct);
        public Task<RoomDoorView> ResolveAsync(RoomCodeResolveRequest request, CancellationToken ct) => inner.ResolveAsync(request, ct);
        public Task<RoomCodeResult> ResetCodeAsync(Guid room, RoomCodeResetRequest request, CancellationToken ct) => inner.ResetCodeAsync(room, request, ct);
        public IFormalRoomConnection CreateConnection()
        {
            var connection = new ObservedConnection(inner.CreateConnection(), Add, notice => { lock (sync) notices.Add(notice); });
            Connections.Add(connection); return connection;
        }
    }
    private sealed class ObservedConnection : IFormalRoomConnection
    {
        private readonly IFormalRoomConnection inner; private readonly Action<string> trace; private readonly Action<RoomChangeNotice> observe;
        public ObservedConnection(IFormalRoomConnection inner, Action<string> trace, Action<RoomChangeNotice> observe)
        { this.inner = inner; this.trace = trace; this.observe = observe; inner.RoomChanged += Notice; inner.Closed += Close; }
        public event Action<RoomChangeNotice>? RoomChanged; public event Action? Closed;
        private void Notice(RoomChangeNotice notice) { observe(notice); RoomChanged?.Invoke(notice); }
        private void Close() => Closed?.Invoke();
        public Task StartAsync(CancellationToken ct) { trace("hub.start"); return inner.StartAsync(ct); }
        public Task StopAsync(CancellationToken ct) { trace("hub.stop"); return inner.StopAsync(ct); }
        public Task<RoomAccessResult> AttachAsync(RoomLeaseRequest request, CancellationToken ct) { trace("hub.attach"); return inner.AttachAsync(request, ct); }
        public Task<RoomAccessResult> HeartbeatAsync(RoomLeaseRequest request, CancellationToken ct) { trace("hub.heartbeat"); return inner.HeartbeatAsync(request, ct); }
        public Task<PersonalRoomSnapshot> PersonalAsync(CancellationToken ct) { trace("hub.personal"); return inner.PersonalAsync(ct); }
        public Task<SharedRoomSnapshot> SnapshotAsync(Guid room, RoomLeaseRequest request, CancellationToken ct) { trace("hub.snapshot"); return inner.SnapshotAsync(room, request, ct); }
        public Task<RoomAccessResult> ControlAsync(RoomControlRequest request, CancellationToken ct) => inner.ControlAsync(request, ct);
        public Task<RoomAccessResult> LeaveAsync(RoomControlRequest request, CancellationToken ct) => inner.LeaveAsync(request, ct);
        public Task<RoomAccessResult> EndLeaseAsync(RoomLeaseEndRequest request, CancellationToken ct) => inner.EndLeaseAsync(request, ct);
        public async ValueTask DisposeAsync()
        { inner.RoomChanged -= Notice; inner.Closed -= Close; await inner.DisposeAsync(); }
    }
    private sealed class FailOnePublish : IRoomOutboxCheckpoint
    {
        private int failed; public Guid EventId { get; set; }
        public Task ReachedAsync(string step, Guid eventId, CancellationToken ct)
        {
            if (step == "after_send_before_publish" && eventId == EventId && Interlocked.Exchange(ref failed, 1) == 0)
                throw new InvalidOperationException("fictional_b5c_publish_failure");
            return Task.CompletedTask;
        }
    }
}
