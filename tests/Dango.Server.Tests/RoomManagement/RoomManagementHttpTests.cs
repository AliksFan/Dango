using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dango.Server.Tests.RoomManagement;

[TestClass]
[TestCategory("PostgresIntegration")]
public sealed class RoomManagementHttpTests
{
    [TestMethod]
    public async Task RealHttpsCrudPersistsTemplatesAndProtectsOtherOwnersAndDesktopRoom()
    {
        RequireApproval();
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var owner = await PetTestSupport.RegisterAsync(fixture);
        var other = await PetTestSupport.RegisterAsync(fixture);
        var otherBefore = await PetTestSupport.GraphAsync(fixture, other);
        using var client = fixture.CreateClient(out _);
        using var foreign = fixture.CreateClient(out _);
        using var anonymous = fixture.CreateClient(out _);
        using (var response = await anonymous.GetAsync("api/v1/me/rooms"))
            await ErrorAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        await LoginAsync(client, owner);
        await LoginAsync(foreign, other);
        var desktop = await GetAsync(client, owner.RoomId);
        Assert.AreEqual("desktopPet", desktop.Kind);
        Assert.AreEqual("desktop-pet-default", desktop.TemplateId);
        Assert.AreEqual(1, desktop.TemplateVersion);
        Assert.AreEqual(2, desktop.Capacity);
        var request = new RoomCreateRequest(Guid.NewGuid(), "publicShared", "  e\u0301 room  ");
        RoomOperationResult created;
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/rooms",
            new { request.RequestId, request.Kind, request.Name }))
        {
            created = await FormalTestFixture.ReadAsync<RoomOperationResult>(response, HttpStatusCode.Created);
            Assert.AreEqual($"/api/v1/rooms/{created.RoomId:D}", response.Headers.Location!.OriginalString);
        }
        var room = await GetAsync(client, created.RoomId);
        Assert.AreEqual("é room", room.Name);
        Assert.AreEqual(6, room.Capacity);
        Assert.AreEqual("ordinary-default", room.TemplateId);
        Assert.AreEqual(1, room.TemplateVersion);
        Assert.AreEqual(0, room.ReservedSeats, "Public owner management marker does not reserve a seat.");
        Assert.AreEqual(0, room.Occupancy);
        using (var response = await foreign.GetAsync($"api/v1/rooms/{room.RoomId:D}"))
            await ErrorAsync(response, HttpStatusCode.NotFound, "room_not_found");
        using (var response = await FormalTestFixture.SendAsync(foreign, HttpMethod.Patch, $"api/v1/rooms/{room.RoomId:D}",
            new RoomUpdateRequest(Guid.NewGuid(), room.Version, "foreign edit", 4)))
            await ErrorAsync(response, HttpStatusCode.NotFound, "room_not_found");
        using (var response = await FormalTestFixture.SendAsync(foreign, HttpMethod.Post, $"api/v1/rooms/{room.RoomId:D}/dissolve",
            new RoomDissolveRequest(Guid.NewGuid(), room.Version, true)))
            await ErrorAsync(response, HttpStatusCode.NotFound, "room_not_found");
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, $"api/v1/rooms/{room.RoomId:D}",
            new RoomUpdateRequest(Guid.NewGuid(), room.Version, "Fictional renamed room", 4)))
            Assert.AreEqual(2L, (await FormalTestFixture.ReadAsync<RoomOperationResult>(response)).AppliedVersion);
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, $"api/v1/rooms/{owner.RoomId:D}/dissolve",
            new RoomDissolveRequest(Guid.NewGuid(), desktop.Version, true)))
            await ErrorAsync(response, HttpStatusCode.Conflict, "room_desktop_protected");
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, $"api/v1/rooms/{room.RoomId:D}/dissolve",
            new RoomDissolveRequest(Guid.NewGuid(), 2, false)))
            await ErrorAsync(response, HttpStatusCode.BadRequest, "room_confirmation_required");
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, $"api/v1/rooms/{room.RoomId:D}/dissolve",
            new RoomDissolveRequest(Guid.NewGuid(), 2, true)))
            Assert.AreEqual(3L, (await FormalTestFixture.ReadAsync<RoomOperationResult>(response)).AppliedVersion);
        room = await GetAsync(client, room.RoomId);
        Assert.AreEqual("dissolved", room.Status);
        Assert.IsTrue(room.Recoverable);
        Assert.IsNotNull(room.RecoverUntil);
        using (var response = await client.GetAsync("api/v1/me/rooms?state=dissolved"))
            Assert.AreEqual(room.RoomId, (await FormalTestFixture.ReadAsync<RoomManagementPage>(response)).Rooms.Single().RoomId);
        using (var response = await FormalTestFixture.SendAsync(foreign, HttpMethod.Post, $"api/v1/rooms/{room.RoomId:D}/restore",
            new RoomRestoreRequest(Guid.NewGuid(), 3)))
            await ErrorAsync(response, HttpStatusCode.NotFound, "room_not_found");
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, $"api/v1/rooms/{room.RoomId:D}/restore",
            new RoomRestoreRequest(Guid.NewGuid(), 3)))
            Assert.AreEqual(4L, (await FormalTestFixture.ReadAsync<RoomOperationResult>(response)).AppliedVersion);
        room = await GetAsync(client, room.RoomId);
        Assert.AreEqual("active", room.Status);
        Assert.IsNull(room.RecoverUntil);
        Assert.AreEqual("Fictional renamed room", room.Name);
        Assert.AreEqual(4, room.Capacity);
        Assert.AreEqual(otherBefore, await PetTestSupport.GraphAsync(fixture, other));
        await fixture.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(owner.PetId, (await db.Pets.SingleAsync(p => p.UserId == owner.UserId)).Id);
            Assert.AreEqual(owner.RoomId, (await db.PetRoomStates.SingleAsync(s => s.PetId == owner.PetId)).RoomId);
            Assert.AreEqual(4, await db.RoomOperationAudits.CountAsync(a => a.RoomId == room.RoomId));
            Assert.AreEqual(4, await db.RoomDomainEvents.CountAsync(e => e.RoomId == room.RoomId && e.PublishedAt == null));
        });
    }

    [TestMethod]
    public async Task StrictHttpRejectsForgeryMalformedBodiesQueriesCsrfAndRevokedSession()
    {
        RequireApproval();
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true);
        var account = await PetTestSupport.RegisterAsync(fixture);
        using var client = fixture.CreateClient(out _);
        var session = await LoginAsync(client, account);
        var before = await PetTestSupport.GraphAsync(fixture, account);
        var request = new RoomCreateRequest(Guid.NewGuid(), "privateShared", "Fictional room");
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/rooms", request, csrf: false))
            await ErrorAsync(response, HttpStatusCode.Forbidden, "request_protection_invalid");
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/rooms", request,
            origin: "https://foreign.example.invalid"))
            await ErrorAsync(response, HttpStatusCode.Forbidden, "origin_invalid");
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/rooms",
            new { request.RequestId, request.Kind, request.Name, request.Capacity, ownerId = Guid.NewGuid() }))
            await ErrorAsync(response, HttpStatusCode.BadRequest, "request_invalid");
        string[] malformed =
        [
            "{\"requestId\":\"" + request.RequestId + "\",\"kind\":\"privateShared\",\"name\":\"one\",\"name\":\"two\"}",
            "{\"requestId\":\"" + request.RequestId + "\",\"kind\":\"privateShared\"}",
            "{\"requestId\":\"" + request.RequestId + "\",\"kind\":\"privateShared\",\"name\":null}"
        ];
        foreach (var json in malformed)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "api/v1/rooms")
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            message.Headers.Add("X-CSRF-TOKEN", await FormalTestFixture.CsrfAsync(client));
            using var response = await client.SendAsync(message);
            await ErrorAsync(response, HttpStatusCode.BadRequest, "request_invalid");
        }
        foreach (var query in new[] { "state=active&state=all", "ownerId=" + Guid.NewGuid(), "limit=", "state=" })
        {
            using var response = await client.GetAsync("api/v1/me/rooms?" + query);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var response = await client.GetAsync($"api/v1/rooms/{account.RoomId:D}?unused=1"))
            await ErrorAsync(response, HttpStatusCode.BadRequest, "room_query_invalid");
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
        await fixture.InScopeAsync(services => services.GetRequiredService<SessionService>().RevokeAsync(account.UserId, session, default));
        using (var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/rooms", request))
            await ErrorAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        using (var response = await client.GetAsync("api/v1/me/rooms"))
            await ErrorAsync(response, HttpStatusCode.Unauthorized, "authentication_required");
        Assert.AreEqual(before, await PetTestSupport.GraphAsync(fixture, account));
    }

    [TestMethod]
    public async Task NewHostAndSessionReplayOriginalAcknowledgementAndStablePages()
    {
        RequireApproval();
        PetTestSupport.Account account;
        var request = new RoomCreateRequest(Guid.NewGuid(), "privateShared", "Fictional persistent room");
        RoomOperationResult original;
        await using (var first = await FormalTestFixture.CreateAsync(startHttp: true))
        {
            account = await PetTestSupport.RegisterAsync(first);
            using var client = first.CreateClient(out _);
            await LoginAsync(client, account);
            // Leave the accepted body unread. Recovery cannot rely on the first acknowledgement.
            using (var discarded = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/rooms", request))
                Assert.AreEqual(HttpStatusCode.Created, discarded.StatusCode);
            using var list = await client.GetAsync("api/v1/me/rooms");
            var committed = (await FormalTestFixture.ReadAsync<RoomManagementPage>(list)).Rooms.Single(r => r.Kind == "privateShared");
            original = new(committed.RoomId, 1, "created");
            using var changed = await FormalTestFixture.SendAsync(client, HttpMethod.Patch, $"api/v1/rooms/{original.RoomId:D}",
                new RoomUpdateRequest(Guid.NewGuid(), 1, "Fictional current name", 7));
            Assert.AreEqual(HttpStatusCode.OK, changed.StatusCode);
        }
        await using var restarted = await FormalTestFixture.CreateAsync(startHttp: true);
        using var fresh = restarted.CreateClient(out _);
        await LoginAsync(fresh, account);
        using (var repeat = await FormalTestFixture.SendAsync(fresh, HttpMethod.Post, "api/v1/rooms", request))
            Assert.AreEqual(original, await FormalTestFixture.ReadAsync<RoomOperationResult>(repeat, HttpStatusCode.Created));
        Assert.AreEqual(2L, (await GetAsync(fresh, original.RoomId)).Version);
        using (var different = await FormalTestFixture.SendAsync(fresh, HttpMethod.Post, "api/v1/rooms", request with { Capacity = 8 }))
            await ErrorAsync(different, HttpStatusCode.Conflict, "room_request_conflict");
        var seen = new List<Guid>();
        Guid? after = null;
        do
        {
            using var pageResponse = await fresh.GetAsync("api/v1/me/rooms?state=all&limit=1" + (after is null ? "" : $"&after={after:D}"));
            var page = await FormalTestFixture.ReadAsync<RoomManagementPage>(pageResponse);
            Assert.HasCount(1, page.Rooms);
            seen.Add(page.Rooms[0].RoomId);
            after = page.Next;
            Assert.IsTrue(seen.Count <= 2, "A stable cursor must not loop or expose another owner's rooms.");
        } while (after is not null);
        CollectionAssert.AreEquivalent(new[] { account.RoomId, original.RoomId }, seen);
        await restarted.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.RoomOperationAudits.CountAsync(a => a.ActorUserId == account.UserId && a.RequestId == request.RequestId));
            Assert.AreEqual(2, await db.RoomDomainEvents.CountAsync(e => e.RoomId == original.RoomId));
        });
    }

    internal static void RequireApproval()
    {
        if (Environment.GetEnvironmentVariable("DANGO_BACKEND_B3_TEST_APPROVED") != "1")
            Assert.Inconclusive("B3 migration, fictional writes and scoped expiry require precise authorization before fixture creation.");
    }

    private static async Task<Guid> LoginAsync(HttpClient client, PetTestSupport.Account account)
    {
        using var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login",
            new { account.Email, account.Password, remembered = false });
        return (await FormalTestFixture.ReadAsync<JsonElement>(response)).GetProperty("sessionId").GetGuid();
    }

    private static async Task<RoomManagementView> GetAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"api/v1/rooms/{id:D}");
        return await FormalTestFixture.ReadAsync<RoomManagementView>(response);
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode expected, string code)
    {
        var json = await FormalTestFixture.ReadAsync<JsonElement>(response, expected);
        Assert.AreEqual(code, json.GetProperty("code").GetString());
    }
}
