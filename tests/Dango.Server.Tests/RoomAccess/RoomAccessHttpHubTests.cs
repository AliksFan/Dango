using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Rooms.Access;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Dango.Server.Tests.RoomsManagement;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class RoomAccessHttpHubTests
{
    [TestMethod]
    public async Task HttpsPublicAdmissionAndRealHubNoticePersistAcrossNewHost()
    {
        RequireApproval();
        Guid roomId; Guid instance; Guid leaseId; PetTestSupport.Account owner;
        await using (var fixture = await FormalTestFixture.CreateAsync(startHttp: true))
        {
            owner = await PetTestSupport.RegisterAsync(fixture);
            var visitor = await PetTestSupport.RegisterAsync(fixture);
            var created = await RoomManagementTestSupport.CreateAsync(fixture, owner, "publicShared");
            roomId = created.RoomId;
            using var ownerClient = fixture.CreateClient(out _);
            using var guest = fixture.CreateClient(out var cookies);
            await LoginAsync(ownerClient, owner); await LoginAsync(guest, visitor);
            RoomCodeResult code;
            using (var response = await FormalTestFixture.SendAsync(ownerClient, HttpMethod.Post, $"api/v1/rooms/{roomId}/code/reset",
                new RoomCodeResetRequest(Guid.NewGuid(), created.AppliedVersion)))
                code = await FormalTestFixture.ReadAsync<RoomCodeResult>(response);
            using (var response = await FormalTestFixture.SendAsync(guest, HttpMethod.Post, "api/v1/rooms/resolve", new RoomCodeResolveRequest(code.Code)))
                Assert.AreEqual(roomId, (await FormalTestFixture.ReadAsync<RoomDoorView>(response)).RoomId);
            instance = Guid.NewGuid();
            var entered = await EnterAsync(guest, roomId, instance, code.Code);
            leaseId = entered.LeaseId!.Value;
            await using var hub = await ConnectAsync(fixture, guest, cookies);
            var attached = await hub.InvokeAsync<RoomAccessResult>("Attach", new RoomLeaseRequest(leaseId, instance));
            Assert.AreEqual(roomId, attached.RoomId);
            var snapshot = await hub.InvokeAsync<SharedRoomSnapshot>("Snapshot", roomId, new RoomLeaseRequest(leaseId, instance));
            Assert.AreEqual(visitor.PetId, snapshot.Occupants.Single().PetId);
            var received = new TaskCompletionSource<RoomChangeNotice>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = hub.On<RoomChangeNotice>("RoomChanged", notice => received.TrySetResult(notice));
            using (var response = await FormalTestFixture.SendAsync(ownerClient, HttpMethod.Patch, $"api/v1/rooms/{roomId}",
                new RoomUpdateRequest(Guid.NewGuid(), entered.RoomVersion, "fictional notice update", 6)))
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var eventId = await LatestEventAsync(fixture, roomId);
            var dispatch = await fixture.InScopeAsync(s => s.GetRequiredService<RoomOutboxDispatcher>().DispatchBatchAsync(1, [eventId], CancellationToken.None));
            Assert.AreEqual(1, dispatch.Published);
            Assert.AreEqual(eventId, (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).EventId);
            await hub.StopAsync();
        }
        await using var restarted = await FormalTestFixture.CreateAsync(startHttp: true);
        using var client = restarted.CreateClient(out _);
        await LoginAsync(client, owner);
        using var get = await client.GetAsync($"api/v1/rooms/{roomId}");
        Assert.AreEqual("fictional notice update", (await FormalTestFixture.ReadAsync<RoomManagementView>(get)).Name);
        await restarted.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            var lease = await db.RoomDeviceLeases.AsNoTracking().SingleAsync(l => l.Id == leaseId);
            Assert.AreEqual(roomId, lease.RoomId);
            Assert.AreEqual(instance, lease.InstanceId);
            Assert.IsNull(lease.RevokedAt, "Graceful transport stop must not terminate the lease.");
        });
    }

    [TestMethod]
    public async Task RealHubReattachRejectsOldTransportAndRevokedSession()
    {
        RequireApproval();
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        using var client = fixture.CreateClient(out var cookies);
        var sessionId = await LoginAsync(client, owner);
        var instance = Guid.NewGuid(); var entered = await EnterAsync(client, owner.RoomId, instance);
        var lease = new RoomLeaseRequest(entered.LeaseId!.Value, instance);
        await using var first = await ConnectAsync(fixture, client, cookies);
        await first.InvokeAsync<RoomAccessResult>("Attach", lease);
        await using var second = await ConnectAsync(fixture, client, cookies);
        await second.InvokeAsync<RoomAccessResult>("Attach", lease);
        var nullInput = await Assert.ThrowsExactlyAsync<HubException>(() => second.InvokeAsync<RoomAccessResult>("Heartbeat", (RoomLeaseRequest?)null));
        StringAssert.Contains(nullInput.Message, "room_request_invalid");
        await Assert.ThrowsExactlyAsync<HubException>(() => first.InvokeAsync<RoomAccessResult>("Heartbeat", lease));
        await second.InvokeAsync<RoomAccessResult>("Heartbeat", lease);
        await fixture.InScopeAsync(s => s.GetRequiredService<SessionService>().RevokeAsync(owner.UserId, sessionId, CancellationToken.None));
        try
        {
            await second.InvokeAsync<RoomAccessResult>("Heartbeat", lease).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("A revoked session cannot execute a Hub command.");
        }
        catch (Exception error) when (error is HubException or OperationCanceledException or IOException)
        {
            if (error is HubException) StringAssert.Contains(error.Message, "authentication_required");
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) { }
        catch (InvalidOperationException) when (second.State != HubConnectionState.Connected) { }
    }

    [TestMethod]
    public async Task FormalHostRejectsAnonymousHubAndForeignSnapshotAndMalformedBody()
    {
        RequireApproval();
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        using var anonymous = fixture.CreateClient(out _);
        using (var negotiate = await FormalTestFixture.SendAsync(anonymous, HttpMethod.Post, "api/v1/room-hub/negotiate?negotiateVersion=1"))
            Assert.AreEqual(HttpStatusCode.Unauthorized, negotiate.StatusCode);
        using (var prototype = await anonymous.GetAsync("room")) Assert.AreEqual(HttpStatusCode.NotFound, prototype.StatusCode);
        var owner = await PetTestSupport.RegisterAsync(fixture); var foreign = await PetTestSupport.RegisterAsync(fixture);
        using var own = fixture.CreateClient(out _); using var other = fixture.CreateClient(out _);
        await LoginAsync(own, owner); await LoginAsync(other, foreign);
        var instance = Guid.NewGuid(); var entered = await EnterAsync(own, owner.RoomId, instance);
        using (var response = await other.GetAsync($"api/v1/rooms/{owner.RoomId}/snapshot?leaseId={entered.LeaseId}&instanceId={instance}"))
            Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await FormalTestFixture.SendAsync(own, HttpMethod.Post, $"api/v1/rooms/{owner.RoomId}/enter",
            new { requestId = Guid.NewGuid(), instanceId = Guid.NewGuid(), deviceKind = "desktop", expectedRoomId = owner.RoomId,
                expectedAssignmentVersion = entered.AssignmentVersion, ownerId = foreign.UserId }))
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    internal static void RequireApproval()
    {
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_B4_TEST_APPROVED") != "1")
            Assert.Inconclusive("B4 exact forward migrations and fictional room/lease/code writes require authorization before fixture creation.");
    }
    internal static async Task<Guid> LoginAsync(HttpClient client, PetTestSupport.Account account)
    {
        using var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login",
            new { account.Email, account.Password, remembered = false });
        return (await FormalTestFixture.ReadAsync<JsonElement>(response)).GetProperty("sessionId").GetGuid();
    }
    internal static async Task<RoomAccessResult> EnterAsync(HttpClient client, Guid roomId, Guid instance, string? code = null)
    {
        using var snapshotResponse = await client.GetAsync("api/v1/me/room/snapshot");
        var snapshot = await FormalTestFixture.ReadAsync<PersonalRoomSnapshot>(snapshotResponse);
        using var enter = await FormalTestFixture.SendAsync(client, HttpMethod.Post, $"api/v1/rooms/{roomId}/enter",
            new RoomEnterRequest(Guid.NewGuid(), instance, "desktop", snapshot.RoomId, snapshot.AssignmentVersion, code));
        return await FormalTestFixture.ReadAsync<RoomAccessResult>(enter);
    }
    internal static async Task<HubConnection> ConnectAsync(FormalTestFixture fixture, HttpClient client, CookieContainer cookies)
    {
        var csrf = await FormalTestFixture.CsrfAsync(client);
        var hub = new HubConnectionBuilder().WithUrl(new Uri(fixture.Endpoint, "api/v1/room-hub"), options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.Cookies = cookies;
            options.Headers.Add("X-CSRF-TOKEN", csrf);
            options.HttpMessageHandlerFactory = handler => { handler.Dispose(); return fixture.CreatePinnedHandler(cookies); };
        }).Build();
        try { await hub.StartAsync(); return hub; }
        catch { await hub.DisposeAsync(); throw; }
    }
    internal static Task<Guid> LatestEventAsync(FormalTestFixture fixture, Guid roomId) => fixture.InScopeAsync(async s =>
        await s.GetRequiredService<DangoDbContext>().RoomDomainEvents.AsNoTracking().Where(e => e.RoomId == roomId)
            .OrderByDescending(e => e.RoomVersion).Select(e => e.Id).FirstAsync());
}
