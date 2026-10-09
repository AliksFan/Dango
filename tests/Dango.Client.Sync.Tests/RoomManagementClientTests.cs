using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class RoomManagementClientTests
{
    private static readonly Uri Origin = new("https://rooms.example.invalid/");
    private static readonly Guid RoomId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LaterRoomId = Guid.Parse("20000000-0000-0000-0000-000000000001");

    [TestMethod]
    public async Task AllOperationsUseOriginalCookieTransportFreshCsrfAndCallerKeys()
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        Assert.AreEqual(Origin, rooms.Origin);
        Assert.AreEqual(setup.Receiver.UserId, rooms.UserId);
        Assert.AreEqual(setup.Receiver.PetId, rooms.PetId);
        var createId = Guid.NewGuid();
        var updateId = Guid.NewGuid();
        var dissolveId = Guid.NewGuid();
        var restoreId = Guid.NewGuid();
        var created = await rooms.CreateAsync(new(createId, "privateShared", "  cafe\u0301  ", 6), CancellationToken.None);
        var updated = await rooms.UpdateAsync(RoomId, new(updateId, 1, "修改名称", 4), CancellationToken.None);
        var dissolved = await rooms.DissolveAsync(RoomId, new(dissolveId, 2, true), CancellationToken.None);
        var restored = await rooms.RestoreAsync(RoomId, new(restoreId, 3), CancellationToken.None);
        Assert.AreEqual(new RoomOperationResult(RoomId, 1, "created"), created);
        Assert.AreEqual(new RoomOperationResult(RoomId, 2, "updated"), updated);
        Assert.AreEqual(new RoomOperationResult(RoomId, 3, "dissolved"), dissolved);
        Assert.AreEqual(new RoomOperationResult(RoomId, 4, "restored"), restored);
        var writes = setup.Receiver.Requests.Where(r => r.Path.StartsWith("api/v1/rooms", StringComparison.Ordinal) && r.Method != HttpMethod.Get).ToArray();
        CollectionAssert.AreEqual(new[] { createId, updateId, dissolveId, restoreId }, writes.Select(r => r.Body!.Value.GetProperty("requestId").GetGuid()).ToArray());
        CollectionAssert.AreEqual(new[] { HttpMethod.Post, HttpMethod.Patch, HttpMethod.Post, HttpMethod.Post }, writes.Select(r => r.Method).ToArray());
        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, writes.Skip(1).Select(r => r.Body!.Value.GetProperty("expectedVersion").GetInt64()).ToArray());
        Assert.AreEqual("café", writes[0].Body!.Value.GetProperty("name").GetString());
        Assert.IsTrue(writes[2].Body!.Value.GetProperty("confirmed").GetBoolean());
        Assert.IsTrue(writes.All(r => r.HasAuth && r.Origin == Origin && r.Proof is null));
        Assert.AreEqual(4, writes.Select(r => r.Csrf).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(writes.All(r => r.Csrf is not null));
        // Same owner transport also serves pet reads; no separate room login or copied cookie jar.
        var pet = await setup.Client.CreatePetClient(setup.Client.CurrentIdentity!).ReadAsync(CancellationToken.None);
        Assert.AreEqual(rooms.PetId, pet.PetId);
    }

    [TestMethod]
    public async Task DirectoryAndDetailAreReadsAndCursorIsBoundToLastItem()
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var page = await rooms.ListAsync("active", null, 1, CancellationToken.None);
        Assert.AreEqual(1, page.Rooms.Length);
        Assert.AreEqual(RoomId, page.Next);
        var second = await rooms.ListAsync("active", page.Next, 1, CancellationToken.None);
        Assert.AreEqual(LaterRoomId, second.Rooms.Single().RoomId);
        Assert.IsNull(second.Next);
        var detail = await rooms.ReadAsync(RoomId, CancellationToken.None);
        Assert.AreEqual(RoomId, detail.RoomId);
        var reads = setup.Receiver.Requests.Where(r => r.Path.Contains("rooms", StringComparison.Ordinal)).ToArray();
        Assert.IsTrue(reads.All(r => r.Method == HttpMethod.Get && r.Csrf is null && r.HasAuth));
        Assert.AreEqual("?state=active&limit=1", reads[0].Query);
        Assert.AreEqual($"?state=active&limit=1&after={RoomId:D}", reads[1].Query);
    }

    [TestMethod]
    public async Task FiftyItemUnicodePageCanExceedAccountResponseLimit()
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var views = Enumerable.Range(1, 50).Select(i => View(Guid.Parse($"{i:X8}-0000-0000-0000-000000000001")) with { Name = string.Concat(Enumerable.Repeat("🐾", 64)) }).ToArray();
        var payload = JsonSerializer.Serialize(new RoomManagementPage(views, views[^1].RoomId), Json);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(payload) > 16384);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(payload) < 65536);
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == "api/v1/me/rooms" ? Raw(payload) : null);
        var result = await rooms.ListAsync("active", null, 50, CancellationToken.None);
        Assert.AreEqual(50, result.Rooms.Length);
        Assert.AreEqual(views[^1].RoomId, result.Next);
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == "api/v1/me" ? Raw(payload) : null);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.ReadIdentityAsync());
        Assert.AreEqual(AccountFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow("roomId", "\"00000000-0000-0000-0000-000000000000\"")]
    [DataRow("roomId", "\"20000000-0000-0000-0000-000000000001\"")]
    [DataRow("kind", "\"other\"")]
    [DataRow("name", "null")]
    [DataRow("name", "\"\"")]
    [DataRow("name", "\" trailing \"")]
    [DataRow("name", "\"cafe\\u0301\"")]
    [DataRow("name", "\"bad\\nname\"")]
    [DataRow("name", "\"\\uD800\"")]
    [DataRow("name", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [DataRow("capacity", "0")]
    [DataRow("capacity", "13")]
    [DataRow("capacity", "2.5")]
    [DataRow("status", "\"expired\"")]
    [DataRow("version", "0")]
    [DataRow("version", "9223372036854775808")]
    [DataRow("reservedSeats", "-1")]
    [DataRow("reservedSeats", "7")]
    [DataRow("reservedSeats", "0")]
    [DataRow("occupancy", "-1")]
    [DataRow("occupancy", "7")]
    [DataRow("occupancy", "2")]
    [DataRow("recoverUntil", "\"2030-01-01T00:00:00Z\"")]
    [DataRow("recoverable", "true")]
    [DataRow("recoverable", "\"false\"")]
    [DataRow("templateId", "\"desktop-pet-default\"")]
    [DataRow("templateVersion", "2")]
    [DataRow("extra", "true")]
    public async Task InvalidDetailCannotBecomeSuccess(string property, string value)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var node = JsonNode.Parse(JsonSerializer.Serialize(View(RoomId), Json))!.AsObject();
        node[property] = JsonNode.Parse(value);
        // Preserve invalid surrogate escapes rather than asking the node serializer to decode them.
        var raw = property == "name" && value.Contains("D800", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(View(RoomId) with { Name = "placeholder" }, Json).Replace("\"placeholder\"", value, StringComparison.Ordinal)
            : node.ToJsonString();
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == $"api/v1/rooms/{RoomId:D}" ? Raw(raw) : null);
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => rooms.ReadAsync(RoomId, CancellationToken.None));
        Assert.AreEqual(RoomManagementFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow("desktopPet", "active", 2, 1, 1, false, true)]
    [DataRow("privateShared", "dissolved", 6, 1, 0, true, true)]
    [DataRow("publicShared", "active", 6, 0, 6, false, true)]
    [DataRow("publicShared", "dissolved", 6, 0, 0, false, true)]
    [DataRow("desktopPet", "active", 3, 1, 1, false, false)]
    [DataRow("desktopPet", "dissolved", 2, 1, 0, true, false)]
    [DataRow("publicShared", "active", 6, 1, 1, false, false)]
    [DataRow("privateShared", "dissolved", 6, 1, 1, true, false)]
    public async Task KindSeatsRecoveryAndTemplatesMustAgree(string kind, string state, int capacity, int seats, int occupancy, bool recoverable, bool accepted)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var view = View(RoomId) with
        {
            Kind = kind,
            Status = state,
            Capacity = capacity,
            ReservedSeats = seats,
            Occupancy = occupancy,
            RecoverUntil = state == "dissolved" ? DateTimeOffset.UtcNow.AddDays(7) : null,
            Recoverable = recoverable,
            TemplateId = kind == "desktopPet" ? "desktop-pet-default" : "ordinary-default"
        };
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == $"api/v1/rooms/{RoomId:D}" ? Response(view) : null);
        if (accepted) Assert.AreEqual(view, await rooms.ReadAsync(RoomId, CancellationToken.None));
        else
        {
            var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => rooms.ReadAsync(RoomId, CancellationToken.None));
            Assert.AreEqual(RoomManagementFailure.InvalidResponse, error.Failure);
        }
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    [DataRow("{\"rooms\":null,\"next\":null}")]
    [DataRow("{\"rooms\":[],\"next\":\"00000000-0000-0000-0000-000000000000\"}")]
    [DataRow("{\"rooms\":[],\"next\":\"10000000-0000-0000-0000-000000000001\"}")]
    [DataRow("{\"rooms\":[],\"next\":null,\"rooms\":[]}")]
    [DataRow("{\"rooms\":[],\"next\":null,\"unexpected\":0}")]
    public async Task MalformedPagesCannotBecomeSuccess(string raw)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == "api/v1/me/rooms" ? Raw(raw) : null);
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => rooms.ListAsync("all", null, 20, CancellationToken.None));
        Assert.AreEqual(RoomManagementFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("unordered")]
    [DataRow("before_cursor")]
    [DataRow("after_mismatch")]
    [DataRow("too_many")]
    [DataRow("wrong_state")]
    [DataRow("nested_duplicate")]
    public async Task PaginationCannotRepeatSkipOrExceedRequestedBound(string fault)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var values = fault switch
        {
            "duplicate" => new[] { View(RoomId), View(RoomId) },
            "unordered" => new[] { View(LaterRoomId), View(RoomId) },
            "wrong_state" => new[] { View(RoomId) with { Status = "dissolved", RecoverUntil = DateTimeOffset.UtcNow.AddDays(7), Recoverable = true } },
            "too_many" => Enumerable.Repeat(View(RoomId), 3).ToArray(),
            _ => new[] { View(RoomId) }
        };
        var payload = JsonSerializer.Serialize(new RoomManagementPage(values, fault == "after_mismatch" ? LaterRoomId : null), Json);
        if (fault == "nested_duplicate") payload = payload.Replace("\"capacity\":6", "\"capacity\":6,\"capacity\":6", StringComparison.Ordinal);
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == "api/v1/me/rooms" ? Raw(payload) : null);
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => rooms.ListAsync("active", fault == "before_cursor" ? LaterRoomId : null, 2, CancellationToken.None));
        Assert.AreEqual(RoomManagementFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow("created", 200)]
    [DataRow("created", 202)]
    [DataRow("updated", 201)]
    [DataRow("dissolved", 201)]
    [DataRow("restored", 201)]
    public async Task EachMutationRequiresItsExactSuccessStatus(string operation, int status)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(IsRoomWrite(r) ? Response(new RoomOperationResult(RoomId, operation == "created" ? 1 : 2, operation), (HttpStatusCode)status) : null);
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => Mutate(rooms, operation));
        Assert.AreEqual(RoomManagementFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow("created", "roomId")]
    [DataRow("created", "version")]
    [DataRow("created", "operation")]
    [DataRow("created", "extra")]
    [DataRow("updated", "roomId")]
    [DataRow("updated", "version")]
    [DataRow("updated", "operation")]
    [DataRow("dissolved", "version")]
    [DataRow("restored", "operation")]
    [DataRow("restored", "duplicate")]
    public async Task ReceiptsMustMatchOperationRoomAndAppliedCasVersion(string operation, string fault)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var node = JsonNode.Parse(JsonSerializer.Serialize(new RoomOperationResult(RoomId, operation == "created" ? 1 : 2, operation), Json))!.AsObject();
        if (fault == "roomId") node["roomId"] = (operation == "created" ? Guid.Empty : LaterRoomId).ToString();
        if (fault == "version") node["appliedVersion"] = operation == "created" ? 2 : 5;
        if (fault == "operation") node["operation"] = "created_again";
        if (fault == "extra") node["name"] = "receipt is not detail";
        var payload = node.ToJsonString();
        if (fault == "duplicate") payload = payload.Replace("\"appliedVersion\":2", "\"appliedVersion\":2,\"appliedVersion\":2", StringComparison.Ordinal);
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(IsRoomWrite(r) ? Raw(payload, operation == "created" ? HttpStatusCode.Created : HttpStatusCode.OK) : null);
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => Mutate(rooms, operation));
        Assert.AreEqual(RoomManagementFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow("declared")]
    [DataRow("streamed")]
    [DataRow("malformed")]
    [DataRow("html")]
    [DataRow("redirect")]
    [DataRow("depth")]
    public async Task BoundedReaderRejectsOversizeMalformedAndRedirectedResponses(string fault)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        setup.Receiver.Override = (r, _) =>
        {
            if (r.Path != "api/v1/me/rooms") return Task.FromResult<HttpResponseMessage?>(null);
            var response = Raw(fault switch { "malformed" => "{", "depth" => new string('[', 20) + "0" + new string(']', 20), _ => new string(' ', 65537) });
            if (fault == "streamed") response.Content = new StreamingContent(new string(' ', 65537));
            if (fault == "html") response.Content.Headers.ContentType!.MediaType = "text/html";
            if (fault == "redirect") { response.StatusCode = HttpStatusCode.Redirect; response.Headers.Location = new Uri("https://external.example.invalid/"); }
            return Task.FromResult<HttpResponseMessage?>(response);
        };
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => rooms.ListAsync("all", null, 50, CancellationToken.None));
        Assert.AreEqual(RoomManagementFailure.InvalidResponse, error.Failure);
        Assert.AreEqual(1, setup.Receiver.Requests.Count(r => r.Path == "api/v1/me/rooms"));
    }

    [TestMethod]
    [DataRow(401, "authentication_required", RoomManagementFailure.Authentication, "authentication_required")]
    [DataRow(403, "request_protection_invalid", RoomManagementFailure.Authentication, "request_protection_invalid")]
    [DataRow(409, "room_version_conflict", RoomManagementFailure.Conflict, "room_version_conflict")]
    [DataRow(409, "room_request_conflict", RoomManagementFailure.Conflict, "room_request_conflict")]
    [DataRow(409, "room_quota_reached", RoomManagementFailure.Conflict, "room_quota_reached")]
    [DataRow(409, "room_capacity_conflict", RoomManagementFailure.Conflict, "room_capacity_conflict")]
    [DataRow(409, "room_desktop_protected", RoomManagementFailure.Conflict, "room_desktop_protected")]
    [DataRow(409, "room_recovery_expired", RoomManagementFailure.Conflict, "room_recovery_expired")]
    [DataRow(404, "room_not_found", RoomManagementFailure.Rejected, "room_not_found")]
    [DataRow(400, "room_name_invalid", RoomManagementFailure.Rejected, "room_name_invalid")]
    [DataRow(429, "request_rate_limited", RoomManagementFailure.Rejected, "request_rate_limited")]
    [DataRow(503, "room_invalid_state", RoomManagementFailure.Unavailable, "request_outcome_unknown")]
    [DataRow(502, "anything", RoomManagementFailure.Unavailable, "request_outcome_unknown")]
    [DataRow(400, "https://external.example.invalid/fictional-secret", RoomManagementFailure.Rejected, "remote_request_rejected")]
    public async Task OnlyKnownErrorCodesEscapeResponseAndStatusMapping(int status, string code, RoomManagementFailure failure, string expectedCode)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == $"api/v1/rooms/{RoomId:D}" ? Response(new { code, detail = "fictional private detail" }, (HttpStatusCode)status) : null);
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => rooms.ReadAsync(RoomId, CancellationToken.None));
        Assert.AreEqual(failure, error.Failure);
        Assert.AreEqual(expectedCode, error.Code);
        Assert.IsNull(error.InnerException);
        Assert.IsFalse(error.ToString().Contains("fictional private detail", StringComparison.Ordinal));
        if (code == "authentication_required")
        {
            Assert.IsNull(setup.Client.CurrentIdentity);
            Assert.IsFalse(setup.Cookies.GetCookies(Origin)["__Host-Dango.Auth"] is not null);
        }
    }

    [TestMethod]
    [DataRow("invalid_state")]
    [DataRow("invalid_limit")]
    [DataRow("empty_cursor")]
    [DataRow("empty_room")]
    [DataRow("empty_request")]
    [DataRow("desktop_create")]
    [DataRow("capacity")]
    [DataRow("version")]
    [DataRow("exhausted_version")]
    [DataRow("unconfirmed")]
    [DataRow("control_name")]
    [DataRow("surrogate_name")]
    [DataRow("long_name")]
    [DataRow("empty_name")]
    [DataRow("null_request")]
    public async Task InvalidCallerInputsCauseNoRequest(string fault)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var count = setup.Receiver.Requests.Count;
        Task Invoke() => fault switch
        {
            "invalid_state" => rooms.ListAsync("active&limit=500", null, 20, CancellationToken.None),
            "invalid_limit" => rooms.ListAsync("all", null, 51, CancellationToken.None),
            "empty_cursor" => rooms.ListAsync("all", Guid.Empty, 20, CancellationToken.None),
            "empty_room" => rooms.ReadAsync(Guid.Empty, CancellationToken.None),
            "empty_request" => rooms.CreateAsync(new(Guid.Empty, "privateShared", "有效名称"), CancellationToken.None),
            "desktop_create" => rooms.CreateAsync(new(Guid.NewGuid(), "desktopPet", "有效名称"), CancellationToken.None),
            "capacity" => rooms.UpdateAsync(RoomId, new(Guid.NewGuid(), 1, "有效名称", 13), CancellationToken.None),
            "version" => rooms.UpdateAsync(RoomId, new(Guid.NewGuid(), 0, "有效名称", 6), CancellationToken.None),
            "exhausted_version" => rooms.RestoreAsync(RoomId, new(Guid.NewGuid(), long.MaxValue), CancellationToken.None),
            "unconfirmed" => rooms.DissolveAsync(RoomId, new(Guid.NewGuid(), 1, false), CancellationToken.None),
            "null_request" => rooms.CreateAsync(null!, CancellationToken.None),
            _ => rooms.CreateAsync(new(Guid.NewGuid(), "privateShared", fault switch { "control_name" => "bad\nname", "surrogate_name" => "\uD800", "long_name" => new string('a', 65), _ => "  " }), CancellationToken.None)
        };
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(Invoke);
        Assert.AreEqual(RoomManagementFailure.Rejected, error.Failure);
        Assert.AreEqual(count, setup.Receiver.Requests.Count);
    }

    [TestMethod]
    public async Task FactoryRejectsUnboundOrWrongIdentityAndReloginNeverRevivesOldAdapter()
    {
        using var setup = new Setup();
        var missing = Assert.ThrowsExactly<RoomManagementClientException>(() => setup.Client.CreateRoomManagementClient(new(Guid.NewGuid(), Guid.NewGuid())));
        Assert.AreEqual("room_binding_changed", missing.Code);
        var old = await setup.LoginAsync();
        Assert.ThrowsExactly<RoomManagementClientException>(() => setup.Client.CreateRoomManagementClient(new(old.UserId, Guid.NewGuid())));
        await setup.Client.LogoutAsync();
        await setup.LoginAsync();
        var count = setup.Receiver.Requests.Count;
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => old.ReadAsync(RoomId, CancellationToken.None));
        Assert.AreEqual(RoomManagementFailure.Authentication, error.Failure);
        Assert.AreEqual("room_binding_changed", error.Code);
        Assert.AreEqual(count, setup.Receiver.Requests.Count);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DisposedOrCancelledLateSuccessCannotPublishRoomResult(bool dispose)
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Receiver.Override = async (r, _) =>
        {
            if (r.Path != $"api/v1/rooms/{RoomId:D}") return null;
            arrived.SetResult();
            await release.Task;
            return Response(View(RoomId));
        };
        using var cancellation = new CancellationTokenSource();
        var read = rooms.ReadAsync(RoomId, cancellation.Token);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (dispose) setup.Client.Dispose();
        else cancellation.Cancel();
        release.SetResult();
        await Assert.ThrowsAsync<OperationCanceledException>(() => read);
        if (dispose) Assert.IsNull(setup.Client.CurrentIdentity);
        else Assert.AreEqual(rooms.UserId, setup.Client.CurrentIdentity!.UserId);
    }

    [TestMethod]
    public async Task RoomErrorAllowlistDoesNotExpandAccountEndpoints()
    {
        using var setup = new Setup();
        await setup.LoginAsync();
        setup.Receiver.Override = (r, _) => Task.FromResult<HttpResponseMessage?>(r.Path == "api/v1/me"
            ? Response(new { code = "room_not_found" }, HttpStatusCode.NotFound) : null);
        var error = await Assert.ThrowsExactlyAsync<AccountClientException>(() => setup.Client.ReadIdentityAsync());
        Assert.AreEqual("remote_request_rejected", error.Code);
    }

    [TestMethod]
    public async Task SharedAccountGateKeepsCsrfPairsTogetherAndQueuedLogoutRevokesAdapter()
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Receiver.Override = async (r, _) =>
        {
            if (r.Method == HttpMethod.Patch && r.Path == $"api/v1/rooms/{RoomId:D}")
            {
                arrived.SetResult();
                await release.Task;
            }
            return null;
        };
        var update = rooms.UpdateAsync(RoomId, new(Guid.NewGuid(), 1, "更新", 6), CancellationToken.None);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var count = setup.Receiver.Requests.Count;
        var logout = setup.Client.LogoutAsync();
        var staleRead = rooms.ReadAsync(RoomId, CancellationToken.None);
        Assert.AreEqual(count, setup.Receiver.Requests.Count);
        release.SetResult();
        await update;
        await logout;
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => staleRead);
        Assert.AreEqual("room_binding_changed", error.Code);
        var writes = setup.Receiver.Requests.Where(r => r.Method != HttpMethod.Get).ToArray();
        Assert.AreEqual(writes.Length, writes.Select(r => r.Csrf).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(0, setup.Receiver.Requests.Count(r => r.Path == $"api/v1/rooms/{RoomId:D}" && r.Method == HttpMethod.Get));
    }

    [TestMethod]
    public async Task UnknownMutationResultIsNotRetriedAndCallerMayReuseSameKey()
    {
        using var setup = new Setup();
        var rooms = await setup.LoginAsync();
        var key = Guid.NewGuid();
        var request = new RoomCreateRequest(key, "publicShared", "原始名称");
        setup.Receiver.Override = (r, _) => IsRoomWrite(r) ? Task.FromException<HttpResponseMessage?>(new HttpRequestException("fictional lost response")) : Task.FromResult<HttpResponseMessage?>(null);
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => rooms.CreateAsync(request, CancellationToken.None));
        Assert.AreEqual(RoomManagementFailure.Unavailable, error.Failure);
        Assert.AreEqual("request_outcome_unknown", error.Code);
        Assert.AreEqual(1, setup.Receiver.Requests.Count(IsRoomWrite));
        setup.Receiver.Override = null;
        await rooms.CreateAsync(request, CancellationToken.None);
        var writes = setup.Receiver.Requests.Where(IsRoomWrite).ToArray();
        Assert.AreEqual(2, writes.Length);
        Assert.IsTrue(writes.All(r => r.Body!.Value.GetProperty("requestId").GetGuid() == key));
        Assert.IsFalse(writes[0].Csrf == writes[1].Csrf);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static RoomManagementView View(Guid id) => new(id, "privateShared", "测试房间", 6, "active", 1, 1, 0, null, false, "ordinary-default", 1);
    private static Task<RoomOperationResult> Mutate(IRoomManagementClient rooms, string operation) => operation switch
    {
        "created" => rooms.CreateAsync(new(Guid.NewGuid(), "privateShared", "创建"), CancellationToken.None),
        "updated" => rooms.UpdateAsync(RoomId, new(Guid.NewGuid(), 1, "更新", 6), CancellationToken.None),
        "dissolved" => rooms.DissolveAsync(RoomId, new(Guid.NewGuid(), 1, true), CancellationToken.None),
        _ => rooms.RestoreAsync(RoomId, new(Guid.NewGuid(), 1), CancellationToken.None)
    };
    private static bool IsRoomWrite(RequestRecord request) => request.Path.StartsWith("api/v1/rooms", StringComparison.Ordinal) && request.Method != HttpMethod.Get;
    private static HttpResponseMessage Response(object value, HttpStatusCode status = HttpStatusCode.OK) => Raw(JsonSerializer.Serialize(value, Json), status);
    private static HttpResponseMessage Raw(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private sealed class Setup : IDisposable
    {
        public Setup()
        {
            Cookies = new CookieContainer();
            Receiver = new Receiver(Cookies);
            Client = FormalAccountClient.ForTesting(Origin, Receiver, Cookies);
        }
        public CookieContainer Cookies { get; }
        public Receiver Receiver { get; }
        public FormalAccountClient Client { get; }
        public async Task<IRoomManagementClient> LoginAsync() => Client.CreateRoomManagementClient(await Client.LoginAsync("fictional@example.invalid", "Fictional-password-42!", false));
        public void Dispose() => Client.Dispose();
    }

    private sealed record RequestRecord(Uri Origin, HttpMethod Method, string Path, string Query, JsonElement? Body, string? Csrf, string? Proof, bool HasAuth)
    { public override string ToString() => "RequestRecord [redacted]"; }

    private sealed class Receiver(CookieContainer cookies) : HttpMessageHandler
    {
        private string csrf = string.Empty;
        private int generation;
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PetId { get; } = Guid.NewGuid();
        public List<RequestRecord> Requests { get; } = [];
        public Func<RequestRecord, CancellationToken, Task<HttpResponseMessage?>>? Override { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            JsonElement? body = null;
            if (request.Content is not null)
            {
                using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
                body = json.RootElement.Clone();
            }
            var uri = request.RequestUri!;
            var record = new RequestRecord(new Uri(uri.GetLeftPart(UriPartial.Authority) + "/"), request.Method, uri.AbsolutePath.TrimStart('/'), uri.Query,
                body, Header(request, "X-CSRF-TOKEN"), Header(request, "X-Registration-Proof"), cookies.GetCookies(Origin)["__Host-Dango.Auth"] is not null);
            Requests.Add(record);
            return (Override is null ? null : await Override(record, ct)) ?? Default(record);
        }

        private HttpResponseMessage Default(RequestRecord request)
        {
            if (request.Path == "api/v1/auth/request-protection")
            {
                csrf = "fictional_room_csrf_generation_" + ++generation;
                return Response(new { requestToken = csrf });
            }
            if (request.Method != HttpMethod.Get && request.Csrf != csrf) return Response(new { code = "request_protection_invalid" }, HttpStatusCode.Forbidden);
            if (request.Path == "api/v1/auth/login")
            {
                cookies.Add(Origin, new Cookie("__Host-Dango.Auth", "fictional-room-auth", "/") { Secure = true, HttpOnly = true });
                return Response(new { userId = UserId, sessionId = Guid.NewGuid(), expiresAt = DateTimeOffset.UtcNow.AddHours(8) });
            }
            if (request.Path == "api/v1/auth/logout") return Response(new { status = "signed_out" });
            if (!request.HasAuth) return Response(new { code = "authentication_required" }, HttpStatusCode.Unauthorized);
            if (request.Path == "api/v1/me") return Response(new { userId = UserId });
            if (request.Path == "api/v1/me/pet") return Response(new
            {
                userId = UserId,
                petId = PetId,
                configurationVersion = 1,
                configuration = JsonSerializer.Deserialize<JsonElement>(PetConfigurationChoiceTests.AppearanceJson)
            });
            if (request.Path == "api/v1/me/rooms") return Response(request.Query.Contains("after=", StringComparison.Ordinal)
                ? new RoomManagementPage([View(LaterRoomId)], null) : new RoomManagementPage([View(RoomId)], RoomId));
            if (request.Path == $"api/v1/rooms/{RoomId:D}" && request.Method == HttpMethod.Get) return Response(View(RoomId));
            if (IsRoomWrite(request))
            {
                var operation = request.Path.EndsWith("/dissolve", StringComparison.Ordinal) ? "dissolved"
                    : request.Path.EndsWith("/restore", StringComparison.Ordinal) ? "restored" : request.Method == HttpMethod.Patch ? "updated" : "created";
                var version = operation == "created" ? 1 : request.Body!.Value.GetProperty("expectedVersion").GetInt64() + 1;
                return Response(new RoomOperationResult(RoomId, version, operation), operation == "created" ? HttpStatusCode.Created : HttpStatusCode.OK);
            }
            throw new InvalidOperationException("Unexpected controlled room route.");
        }
        private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? values.Single() : null;
    }

    private sealed class StreamingContent : HttpContent
    {
        private readonly byte[] bytes;
        public StreamingContent(string value)
        {
            bytes = Encoding.UTF8.GetBytes(value);
            Headers.ContentType = new("application/json");
        }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
}
