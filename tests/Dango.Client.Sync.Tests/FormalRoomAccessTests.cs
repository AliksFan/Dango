using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;
using Microsoft.AspNetCore.SignalR;

namespace Dango.Client.Sync.Tests;

[TestClass]
public sealed class FormalRoomAccessTests
{
    private static readonly Uri Origin = new("https://formal.example.invalid/");
    private static readonly Guid RoomId = Guid.NewGuid(), LeaseId = Guid.NewGuid(), InstanceId = Guid.NewGuid();
    private static readonly CancellationToken None = CancellationToken.None;
    private static JsonElement Element(object value) => JsonSerializer.SerializeToElement(value, RoomAccessValidation.Json);
    private static JsonElement Configuration => JsonSerializer.Deserialize<JsonElement>(PetConfigurationChoiceTests.AppearanceJson);
    private static PersonalRoomSnapshot Personal(Guid user, Guid pet) => new(1, user, pet, RoomId, "privateShared", 5, 3, LeaseId, false,
        [new(LeaseId, InstanceId, "desktop", true, DateTimeOffset.UtcNow.AddSeconds(25))], DateTimeOffset.UtcNow);
    private static SharedRoomSnapshot Snapshot(Guid user, Guid pet) => new(1, RoomId, 4, "privateShared", "虚构房间", 6, "ordinary-default", 1,
        [new(pet, user, 1, Configuration, -200, 400, false, 5)], DateTimeOffset.UtcNow);
    private static RoomAccessResult Result(Guid pet, string operation) => new(pet, operation == "left" ? null : RoomId,
        operation is "left" or "lease_ended" ? null : LeaseId, 6, 4, 5, operation, DateTimeOffset.UtcNow,
        operation is "left" or "lease_ended" ? null : DateTimeOffset.UtcNow.AddSeconds(25));
    private static RoomControlRequest Control => new(Guid.NewGuid(), LeaseId, InstanceId, RoomId, 5, 3);

    [TestMethod]
    public async Task HttpOperationsReuseAccountCookieFreshCsrfAndOriginalKeys()
    {
        using var setup = new Setup(); var access = await setup.LoginAsync();
        Assert.AreEqual(setup.UserId, access.UserId); Assert.AreEqual(setup.PetId, access.PetId); Assert.AreEqual(Origin, access.Origin);
        await access.PersonalAsync(None); await access.SnapshotAsync(RoomId, new(LeaseId, InstanceId), None);
        var enter = new RoomEnterRequest(Guid.NewGuid(), InstanceId, "desktop", null, 1);
        await access.EnterAsync(RoomId, enter, None); await access.ControlAsync(Control, None); await access.LeaveAsync(Control, None);
        await access.EndLeaseAsync(new(Guid.NewGuid(), LeaseId, InstanceId), None);
        await access.ResolveAsync(new(new string('A', 43)), None); await access.ResetCodeAsync(RoomId, new(Guid.NewGuid(), 4), None);
        var writes = setup.Requests.Where(r => r.Method == HttpMethod.Post && r.Path != "/api/v1/auth/login").ToArray();
        Assert.AreEqual(6, writes.Length); Assert.IsTrue(writes.All(r => r.HasAuth && r.Csrf is not null));
        Assert.AreEqual(6, writes.Select(r => r.Csrf).Distinct().Count());
        Assert.AreEqual(enter.RequestId, writes[0].Body!.Value.GetProperty("requestId").GetGuid());
        Assert.AreEqual(enter.ExpectedAssignmentVersion, writes[0].Body!.Value.GetProperty("expectedAssignmentVersion").GetInt64());
        Assert.IsTrue(setup.Requests.Any(r => r.Query == $"?leaseId={LeaseId:D}&instanceId={InstanceId:D}"));
    }

    [TestMethod]
    [DataRow("userId", "\"00000000-0000-0000-0000-000000000001\"")]
    [DataRow("petId", "\"00000000-0000-0000-0000-000000000001\"")]
    [DataRow("schemaVersion", "2")]
    [DataRow("assignmentVersion", "0")]
    [DataRow("controlGeneration", "-1")]
    [DataRow("roomKind", "\"unknown\"")]
    [DataRow("roomId", "null")]
    [DataRow("controlLeaseId", "\"00000000-0000-0000-0000-000000000000\"")]
    [DataRow("isSleeping", "true")]
    [DataRow("leases", "null")]
    [DataRow("leases", "[null]")]
    [DataRow("extra", "true")]
    public async Task PersonalIdentityStateAndNullsAreRejected(string property, string raw)
    {
        using var setup = new Setup(); var access = await setup.LoginAsync();
        var node = JsonSerializer.SerializeToNode(Personal(setup.UserId, setup.PetId), RoomAccessValidation.Json)!.AsObject(); node[property] = JsonNode.Parse(raw);
        setup.Override = record => record.Path == "/api/v1/me/room/snapshot" ? Raw(node.ToJsonString()) : null;
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => access.PersonalAsync(None));
        Assert.AreEqual(RoomAccessFailure.InvalidResponse, error.Failure);
    }

    [TestMethod]
    [DataRow("duplicate_lease")]
    [DataRow("duplicate_instance")]
    [DataRow("nine_devices")]
    [DataRow("lease_too_long")]
    [DataRow("expired")]
    [DataRow("control_mismatch")]
    public void PersonalLeaseBoundAndControlAreValidated(string fault)
    {
        var user = Guid.NewGuid(); var pet = Guid.NewGuid(); var value = Personal(user, pet);
        value = fault switch
        {
            "duplicate_lease" => value with { Leases = [value.Leases[0], value.Leases[0]] },
            "duplicate_instance" => value with { Leases = [value.Leases[0], value.Leases[0] with { LeaseId = Guid.NewGuid(), Controls = false }] },
            "nine_devices" => value with { Leases = Enumerable.Range(0, 9).Select(_ => value.Leases[0] with { LeaseId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), Controls = false }).ToArray() },
            "lease_too_long" => value with { Leases = [value.Leases[0] with { ExpiresAt = value.ServerTime.AddSeconds(31) }] },
            "expired" => value with { Leases = [value.Leases[0] with { ExpiresAt = value.ServerTime }] },
            _ => value with { Leases = [value.Leases[0] with { Controls = false }] }
        };
        Assert.ThrowsExactly<RoomAccessClientException>(() => RoomAccessValidation.Personal(Element(value), user, pet));
    }

    [TestMethod]
    [DataRow("duplicate_pet")]
    [DataRow("duplicate_user")]
    [DataRow("thirteen")]
    [DataRow("capacity")]
    [DataRow("configuration")]
    [DataRow("configuration_version")]
    [DataRow("nonfinite")]
    [DataRow("public_sleep")]
    [DataRow("template")]
    [DataRow("name")]
    public void SharedSnapshotsValidateOccupantsAppearanceAndFinitePositions(string fault)
    {
        var user = Guid.NewGuid(); var pet = Guid.NewGuid(); var value = Snapshot(user, pet); var occupant = value.Occupants[0];
        value = fault switch
        {
            "duplicate_pet" => value with { Occupants = [occupant, occupant with { UserId = Guid.NewGuid() }] },
            "duplicate_user" => value with { Occupants = [occupant, occupant with { PetId = Guid.NewGuid() }] },
            "thirteen" => value with { Capacity = 12, Occupants = Enumerable.Range(0, 13).Select(_ => occupant with { UserId = Guid.NewGuid(), PetId = Guid.NewGuid() }).ToArray() },
            "capacity" => value with { Capacity = 0 },
            "configuration" => value with { Occupants = [occupant with { Configuration = Element(new { unknown = true }) }] },
            "configuration_version" => value with { Occupants = [occupant with { ConfigurationVersion = 0 }] },
            "nonfinite" => value with { Occupants = [occupant with { PositionX = double.PositiveInfinity }] },
            "public_sleep" => value with { Kind = "publicShared", Occupants = [occupant with { IsSleeping = true }] },
            "template" => value with { TemplateVersion = 2 },
            _ => value with { Name = "bad\nname" }
        };
        if (fault == "nonfinite")
        {
            var node = JsonSerializer.SerializeToNode(Snapshot(user, pet), RoomAccessValidation.Json)!.AsObject();
            node["occupants"]![0]!["positionX"] = "Infinity";
            Assert.ThrowsExactly<RoomAccessClientException>(() => RoomAccessValidation.Snapshot(JsonSerializer.Deserialize<JsonElement>(node.ToJsonString()), RoomId));
        }
        else Assert.ThrowsExactly<RoomAccessClientException>(() => RoomAccessValidation.Snapshot(Element(value), RoomId));
        // Finite values outside 0..1 are an existing contract; do not invent a coordinate range.
        Assert.AreEqual(-200d, RoomAccessValidation.Snapshot(Element(Snapshot(user, pet)), RoomId).Occupants[0].PositionX);
    }

    [TestMethod]
    [DataRow("entered", "left")]
    [DataRow("control", "heartbeat")]
    [DataRow("attached", "entered")]
    [DataRow("heartbeat", "attached")]
    [DataRow("left", "lease_ended")]
    public void ReceiptsCannotClaimAnotherOperation(string expected, string actual)
    {
        var pet = Guid.NewGuid();
        Assert.ThrowsExactly<RoomAccessClientException>(() => RoomAccessValidation.Result(Element(Result(pet, actual)), pet, expected));
    }

    [TestMethod]
    [DataRow("empty_id")]
    [DataRow("bad_device")]
    [DataRow("empty_expected")]
    [DataRow("version")]
    [DataRow("code")]
    [DataRow("lifetime")]
    public async Task InvalidRequestsNeverReachTransport(string fault)
    {
        using var setup = new Setup(); var access = await setup.LoginAsync(); var before = setup.Requests.Count;
        var enter = new RoomEnterRequest(Guid.NewGuid(), InstanceId, "desktop", null, 1);
        Task Work() => fault == "lifetime" ? access.ResetCodeAsync(RoomId, new(Guid.NewGuid(), 1, 169), None)
            : access.EnterAsync(RoomId, fault switch
            {
                "empty_id" => enter with { RequestId = Guid.Empty },
                "bad_device" => enter with { DeviceKind = "other" },
                "empty_expected" => enter with { ExpectedRoomId = Guid.Empty },
                "version" => enter with { ExpectedAssignmentVersion = 0 },
                _ => enter with { Code = new string('a', 43) }
            }, None);
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(Work);
        Assert.AreEqual(RoomAccessFailure.Rejected, error.Failure); Assert.AreEqual(before, setup.Requests.Count);
    }

    [TestMethod]
    public async Task UnknownEnterIsNotRetriedAndOldBindingDoesNotUseRelogin()
    {
        using var setup = new Setup(); var access = await setup.LoginAsync();
        setup.Override = r => r.Path.EndsWith("/enter", StringComparison.Ordinal) ? throw new HttpRequestException("fictional lost result") : null;
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => access.EnterAsync(RoomId, new(Guid.NewGuid(), InstanceId, "desktop", null, 1), None));
        Assert.AreEqual("request_outcome_unknown", error.Code); Assert.AreEqual(1, setup.Requests.Count(r => r.Path.EndsWith("/enter", StringComparison.Ordinal)));
        setup.Override = null; await setup.Client.LogoutAsync(); await setup.LoginAsync(); var before = setup.Requests.Count;
        error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => access.PersonalAsync(None));
        Assert.AreEqual("room_binding_changed", error.Code); Assert.AreEqual(before, setup.Requests.Count);
    }

    [TestMethod]
    [DataRow(403, "room_access_forbidden", RoomAccessFailure.Rejected)]
    [DataRow(403, "room_lease_forbidden", RoomAccessFailure.Rejected)]
    [DataRow(409, "room_assignment_conflict", RoomAccessFailure.Conflict)]
    [DataRow(401, "authentication_required", RoomAccessFailure.Authentication)]
    public async Task HttpPermissionFailureDoesNotImplyAccountRevocation(int status, string code, RoomAccessFailure expected)
    {
        using var setup = new Setup(); var access = await setup.LoginAsync();
        setup.Override = r => r.Path == "/api/v1/me/room/snapshot" ? new((HttpStatusCode)status)
        { Content = new StringContent(JsonSerializer.Serialize(new { code }), Encoding.UTF8, "application/json") } : null;
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => access.PersonalAsync(None));
        Assert.AreEqual(expected, error.Failure); Assert.AreEqual(code, error.Code);
        Assert.AreEqual(code == "authentication_required", setup.Client.CurrentIdentity is null);
    }

    [TestMethod]
    public void EightSessionDevicesRemainAValidBoundedSnapshot()
    {
        var user = Guid.NewGuid(); var pet = Guid.NewGuid(); var value = Personal(user, pet);
        value = value with
        {
            Leases = new[] { value.Leases[0] }.Concat(Enumerable.Range(0, 7)
            .Select(_ => value.Leases[0] with { LeaseId = Guid.NewGuid(), InstanceId = Guid.NewGuid(), Controls = false })).ToArray()
        };
        Assert.AreEqual(8, RoomAccessValidation.Personal(Element(value), user, pet).Leases.Length);
    }

    [TestMethod]
    public async Task ActualSignalRLongPollingWithControlledHandlerGetsFreshCsrfForEveryUnsafeRequest()
    {
        using var setup = new Setup(); await setup.LoginAsync();
        using var receiver = new HubReceiver(setup);
        var access = setup.Client.CreateRoomAccessClientForTesting(setup.Client.CurrentIdentity!, handler => { handler.Dispose(); return receiver; });
        await using var connection = access.CreateConnection();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await connection.StartAsync(timeout.Token);
        await connection.AttachAsync(new(LeaseId, InstanceId), timeout.Token); await connection.HeartbeatAsync(new(LeaseId, InstanceId), timeout.Token);
        await connection.PersonalAsync(timeout.Token); await connection.SnapshotAsync(RoomId, new(LeaseId, InstanceId), timeout.Token);
        await connection.ControlAsync(Control, timeout.Token); await connection.LeaveAsync(Control, timeout.Token);
        await connection.EndLeaseAsync(new(Guid.NewGuid(), LeaseId, InstanceId), timeout.Token);
        var notice = new TaskCompletionSource<RoomChangeNotice>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.RoomChanged += value => notice.TrySetResult(value);
        var change = new RoomChangeNotice(Guid.NewGuid(), RoomId, 5, "updated");
        receiver.Push(new { type = 1, target = "RoomChanged", arguments = new[] { change } });
        Assert.AreEqual(change, await notice.Task.WaitAsync(timeout.Token));
        await connection.StopAsync(timeout.Token);
        Assert.IsTrue(receiver.Writes.Count >= 10); // negotiate, handshake, seven commands, DELETE
        Assert.IsTrue(receiver.Writes.All(r => r.Csrf is not null && r.HasAuth && r.Origin == Origin.GetLeftPart(UriPartial.Authority)));
        Assert.AreEqual(receiver.Writes.Count, receiver.Writes.Select(r => r.Csrf).Distinct().Count());
        CollectionAssert.AreEqual(new[] { "Attach", "Heartbeat", "PersonalSnapshot", "Snapshot", "TakeControl", "Leave", "EndLease" }, receiver.Methods.ToArray());
    }

    [TestMethod]
    public async Task HubLateResultsAndCallbacksCannotEscapeStoppedGeneration()
    {
        var user = Guid.NewGuid(); var pet = Guid.NewGuid(); var wire = new Wire(); var valid = true; var callbacks = 0;
        var binding = new FormalRoomBinding(Origin, new(), user, pet, _ => { if (!valid) throw new RoomAccessClientException(RoomAccessFailure.Authentication, "room_binding_changed"); }, None,
            (_, _, _) => throw new AssertFailedException());
        await using var connection = new FormalRoomTransport(binding, wire); connection.RoomChanged += _ => callbacks++;
        await connection.StartAsync(None); var work = connection.PersonalAsync(None); valid = false;
        wire.Push(Element(new RoomChangeNotice(Guid.NewGuid(), RoomId, 2, "updated")));
        wire.Pending.SetResult(Element(Personal(user, pet)));
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => work);
        Assert.AreEqual("room_binding_changed", error.Code); Assert.AreEqual(0, callbacks);
        await connection.StopAsync(None); wire.Push(Element(new RoomChangeNotice(Guid.NewGuid(), RoomId, 3, "updated"))); Assert.AreEqual(0, callbacks);
    }

    [TestMethod]
    [DataRow("room_full", RoomAccessFailure.Conflict, "room_full")]
    [DataRow("room_access_forbidden", RoomAccessFailure.Rejected, "room_access_forbidden")]
    [DataRow("room_connection_rejected", RoomAccessFailure.Rejected, "room_connection_rejected")]
    [DataRow("authentication_required", RoomAccessFailure.Authentication, "authentication_required")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: room_assignment_conflict", RoomAccessFailure.Conflict, "room_assignment_conflict")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: room_transport_conflict", RoomAccessFailure.Conflict, "room_transport_conflict")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: room_lease_expired", RoomAccessFailure.Conflict, "room_lease_expired")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: authentication_required", RoomAccessFailure.Authentication, "authentication_required")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: room_connection_rejected", RoomAccessFailure.Rejected, "room_connection_rejected")]
    [DataRow("An unexpected error occurred invoking 'TakeControl' on the server. HubException: authentication_required", RoomAccessFailure.Unavailable, "request_outcome_unknown")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. AccountFault: authentication_required", RoomAccessFailure.Unavailable, "request_outcome_unknown")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: room_full fictional-secret", RoomAccessFailure.Unavailable, "request_outcome_unknown")]
    [DataRow("fictional-secret An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: room_full", RoomAccessFailure.Unavailable, "request_outcome_unknown")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: service_unavailable", RoomAccessFailure.Unavailable, "request_outcome_unknown")]
    [DataRow("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: unknown fictional-secret room_full", RoomAccessFailure.Unavailable, "request_outcome_unknown")]
    [DataRow("unknown fictional-secret room_full", RoomAccessFailure.Unavailable, "request_outcome_unknown")]
    public async Task HubErrorsOnlyMapExactAllowedCodes(string remote, RoomAccessFailure failure, string expected)
    {
        var wire = new Wire(); var invalidated = false;
        var binding = new FormalRoomBinding(Origin, new(), Guid.NewGuid(), Guid.NewGuid(), _ => { }, None, (_, _, _) => throw new AssertFailedException(),
            () => { invalidated = true; return Task.CompletedTask; });
        await using var connection = new FormalRoomTransport(binding, wire); await connection.StartAsync(None);
        wire.Pending.SetException(new HubException(remote));
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => connection.PersonalAsync(None));
        Assert.AreEqual(failure, error.Failure); Assert.AreEqual(expected, error.Code); Assert.IsNull(error.InnerException);
        Assert.IsFalse(error.ToString().Contains("fictional-secret", StringComparison.Ordinal));
        Assert.AreEqual(expected == "authentication_required", invalidated);
    }

    [TestMethod]
    [DataRow("https://external.example.invalid/api/v1/room-hub", false)]
    [DataRow("https://formal.example.invalid/api/v1/room-hub/other", false)]
    [DataRow("wss://formal.example.invalid/other", true)]
    [DataRow("wss://formal.example.invalid/api/v1/room-hub/negotiate", true)]
    [DataRow("https://user:fictional@formal.example.invalid/api/v1/room-hub", false)]
    [DataRow("https://formal.example.invalid:444/api/v1/room-hub", false)]
    public void FixedHttpAndWebSocketUriNeverAcceptOtherOriginsOrPaths(string value, bool ws) =>
        Assert.ThrowsExactly<RoomAccessClientException>(() => FormalHubHttpHandler.RequireUri(Origin, new(value), ws));

    [TestMethod]
    [DataRow("{\"connectionId\":\"fictional\",\"availableTransports\":[{}],\"url\":\"https://external.example.invalid/\"}")]
    [DataRow("{\"connectionId\":\"fictional\",\"availableTransports\":[{}],\"accessToken\":\"fictional\"}")]
    [DataRow("{\"connectionId\":\"a\",\"connectionId\":\"b\",\"availableTransports\":[{}]}")]
    [DataRow("{\"connectionId\":null,\"availableTransports\":[{}]}")]
    public void NegotiationCannotRedirectOrSupplyAnExternalToken(string value) =>
        Assert.ThrowsExactly<RoomAccessClientException>(() => FormalHubHttpHandler.ValidateNegotiation(JsonSerializer.Deserialize<JsonElement>(value)));

    [TestMethod]
    [DataRow("{\"type\":1,\"target\":\"Move\",\"arguments\":[{}]}")]
    [DataRow("{\"type\":1,\"target\":\"RoomChanged\",\"arguments\":[]}")]
    [DataRow("{\"type\":1,\"target\":\"RoomChanged\",\"arguments\":[{\"x\":1,\"x\":1}]}")]
    [DataRow("{\"type\":6,\"unexpected\":0}")]
    [DataRow("{\"type\":3,\"invocationId\":\"1\",\"result\":{},\"error\":\"other\"}")]
    [DataRow("{\"type\":8}")]
    public void FormalFramesRejectUnknownMethodsFieldsAndDuplicates(string value) =>
        Assert.ThrowsExactly<RoomAccessClientException>(() => BoundedFormalHubProtocol.ValidateFrame(JsonSerializer.Deserialize<JsonElement>(value)));

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("zero_id")]
    [DataRow("extra_field")]
    public async Task InvalidNoticeClosesOnceAndNeverPublishesLaterCallbacks(string fault)
    {
        var wire = new Wire(); var binding = new FormalRoomBinding(Origin, new(), Guid.NewGuid(), Guid.NewGuid(), _ => { }, None,
            (_, _, _) => throw new AssertFailedException());
        await using var connection = new FormalRoomTransport(binding, wire); var notices = 0; var closed = 0;
        connection.RoomChanged += _ => notices++; connection.Closed += () => closed++;
        await connection.StartAsync(None);
        var valid = new RoomChangeNotice(Guid.NewGuid(), RoomId, 5, "updated");
        var value = JsonSerializer.SerializeToNode(valid, RoomAccessValidation.Json)!.AsObject();
        if (fault == "unknown") value["eventType"] = "invented_event";
        else if (fault == "zero_id") value["eventId"] = Guid.Empty.ToString();
        else value["external"] = true;
        wire.Push(JsonSerializer.Deserialize<JsonElement>(value.ToJsonString())); wire.Push(Element(valid));
        Assert.AreEqual(0, notices); Assert.AreEqual(1, closed);
    }

    [TestMethod]
    public void ReturnedControlAndAssignmentMustAdvanceOriginalCas()
    {
        var pet = Guid.NewGuid(); var result = Element(Result(pet, "control"));
        Assert.ThrowsExactly<RoomAccessClientException>(() => RoomAccessValidation.Result(result, pet, "control", RoomId, LeaseId, 6, 3));
        Assert.ThrowsExactly<RoomAccessClientException>(() => RoomAccessValidation.Result(result, pet, "control", RoomId, LeaseId, 5, 4));
        Assert.ThrowsExactly<RoomAccessClientException>(() => RoomAccessValidation.Result(result, Guid.NewGuid(), "control", RoomId, LeaseId));
    }

    [TestMethod]
    public async Task ClosedDuringStartCannotBeReportedAsConnected()
    {
        var wire = new Wire { CloseDuringStart = true };
        var binding = new FormalRoomBinding(Origin, new(), Guid.NewGuid(), Guid.NewGuid(), _ => { }, None, (_, _, _) => throw new AssertFailedException());
        await using var connection = new FormalRoomTransport(binding, wire);
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => connection.StartAsync(None));
        Assert.AreEqual(RoomAccessFailure.Unavailable, error.Failure);
    }

    [TestMethod]
    public async Task ConnectionAdmissionFailureCannotInvalidateAccount()
    {
        var wire = new Wire { StartError = new HubException("room_connection_rejected") }; var invalidated = false;
        var binding = new FormalRoomBinding(Origin, new(), Guid.NewGuid(), Guid.NewGuid(), _ => { }, None,
            (_, _, _) => throw new AssertFailedException(), () => { invalidated = true; return Task.CompletedTask; });
        await using var connection = new FormalRoomTransport(binding, wire);
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => connection.StartAsync(None));
        Assert.AreEqual(RoomAccessFailure.Rejected, error.Failure); Assert.AreEqual("room_connection_rejected", error.Code);
        Assert.IsFalse(invalidated);
    }

    [TestMethod]
    public async Task InvocationErrorEnvelopeCannotBeAcceptedAsConnectionAdmissionError()
    {
        var wire = new Wire { StartError = new HubException("An unexpected error occurred invoking 'PersonalSnapshot' on the server. HubException: authentication_required") };
        var invalidated = false;
        var binding = new FormalRoomBinding(Origin, new(), Guid.NewGuid(), Guid.NewGuid(), _ => { }, None,
            (_, _, _) => throw new AssertFailedException(), () => { invalidated = true; return Task.CompletedTask; });
        await using var connection = new FormalRoomTransport(binding, wire);
        var error = await Assert.ThrowsExactlyAsync<RoomAccessClientException>(() => connection.StartAsync(None));
        Assert.AreEqual(RoomAccessFailure.Unavailable, error.Failure);
        Assert.AreEqual("request_outcome_unknown", error.Code);
        Assert.IsFalse(invalidated);
    }

    [TestMethod]
    public void FrameBoundIncludesHandshakeFragmentsAndResetsAtRecordSeparators()
    {
        var counter = new FormalFrameCounter(); counter.Add(new byte[65536]);
        Assert.ThrowsExactly<InvalidDataException>(() => counter.Add([1]));
        counter = new(); counter.Add(new byte[65536]); counter.Add([0x1e]); counter.Add(new byte[65536]);
        var protocol = new BoundedFormalHubProtocol(); var sequence = new ReadOnlySequence<byte>(new byte[65537]);
        Assert.ThrowsExactly<InvalidDataException>(() => protocol.TryParseMessage(ref sequence, null!, out _));
    }

    private static HttpResponseMessage Raw(string raw) => new(HttpStatusCode.OK) { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
    private sealed record Record(HttpMethod Method, string Path, string Query, JsonElement? Body, string? Csrf, bool HasAuth)
    { public override string ToString() => "Record [redacted]"; }
    private sealed class Setup : HttpMessageHandler
    {
        internal Guid UserId { get; } = Guid.NewGuid(); internal Guid PetId { get; } = Guid.NewGuid();
        internal CookieContainer Cookies { get; } = new(); internal List<Record> Requests { get; } = [];
        internal Func<Record, HttpResponseMessage?>? Override;
        internal string CurrentCsrf = string.Empty; private int generation;
        internal FormalAccountClient Client { get; }
        internal Setup() { Client = FormalAccountClient.ForTesting(Origin, this, Cookies); }
        internal async Task<IRoomAccessClient> LoginAsync() => Client.CreateRoomAccessClient(await Client.LoginAsync("fictional@example.invalid", "Fictional-password", false));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(await request.Content.ReadAsStringAsync(ct));
            var record = new Record(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body,
                request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null, Cookies.GetCookies(Origin)["__Host-Dango.Auth"] is not null);
            Requests.Add(record); var overridden = Override?.Invoke(record); if (overridden is not null) return overridden;
            object result;
            if (record.Path == "/api/v1/auth/request-protection") result = new { requestToken = CurrentCsrf = "fictional_csrf_generation_" + ++generation };
            else if (record.Path == "/api/v1/auth/login")
            { Cookies.Add(Origin, new Cookie("__Host-Dango.Auth", "fictional", "/") { Secure = true, HttpOnly = true }); result = new { userId = UserId, sessionId = Guid.NewGuid(), expiresAt = DateTimeOffset.UtcNow.AddHours(8) }; }
            else if (record.Path == "/api/v1/auth/logout") result = new { status = "signed_out" };
            else if (record.Path == "/api/v1/me") result = new { userId = UserId };
            else if (record.Path == "/api/v1/me/pet") result = new { userId = UserId, petId = PetId, configurationVersion = 1, configuration = Configuration };
            else if (record.Path == "/api/v1/me/room/snapshot") result = Personal(UserId, PetId);
            else if (record.Path.EndsWith("/snapshot", StringComparison.Ordinal)) result = Snapshot(UserId, PetId);
            else if (record.Path.EndsWith("/resolve", StringComparison.Ordinal)) result = new RoomDoorView(RoomId, "publicShared", "虚构公共房", 6, 4, "ordinary-default", 1);
            else if (record.Path.EndsWith("/code/reset", StringComparison.Ordinal)) result = new RoomCodeResult(RoomId, Guid.NewGuid(), 5, 1, new string('A', 43), DateTimeOffset.UtcNow.AddDays(1));
            else result = Result(PetId, record.Path.EndsWith("/enter", StringComparison.Ordinal) ? "entered" : record.Path.EndsWith("/control", StringComparison.Ordinal) ? "control" : record.Path.EndsWith("/leave", StringComparison.Ordinal) ? "left" : "lease_ended");
            return Raw(JsonSerializer.Serialize(result, RoomAccessValidation.Json));
        }
        protected override void Dispose(bool disposing) { if (disposing) Client.Dispose(); base.Dispose(disposing); }
    }
    private sealed class Wire : IFormalRoomWire
    {
        public event Action<JsonElement>? Notice; public event Action? Closed;
        internal readonly TaskCompletionSource<JsonElement> Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Push(JsonElement value) => Notice?.Invoke(value);
        internal bool CloseDuringStart;
        internal Exception? StartError;
        public Task StartAsync(CancellationToken ct) { if (CloseDuringStart) Closed?.Invoke(); return StartError is null ? Task.CompletedTask : Task.FromException(StartError); }
        public Task StopAsync(CancellationToken ct) { Closed?.Invoke(); return Task.CompletedTask; }
        public Task<JsonElement> InvokeAsync(string method, object?[] args, CancellationToken ct) => Pending.Task;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class HubReceiver(Setup setup) : HttpMessageHandler
    {
        private readonly Channel<string> received = Channel.CreateUnbounded<string>(); private bool firstGet = true;
        internal List<(string? Csrf, bool HasAuth, string? Origin)> Writes { get; } = []; internal List<string> Methods { get; } = [];
        internal void Push(object value) => received.Writer.TryWrite(JsonSerializer.Serialize(value, RoomAccessValidation.Json) + '\u001e');
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get)
            {
                var proof = request.Headers.TryGetValues("X-CSRF-TOKEN", out var values) ? values.Single() : null;
                Writes.Add((proof, setup.Cookies.GetCookies(Origin)["__Host-Dango.Auth"] is not null, request.Headers.GetValues("Origin").Single()));
                Assert.IsTrue(proof == setup.CurrentCsrf, "Every unsafe SignalR HTTP request must use its own fresh token.");
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/negotiate", StringComparison.Ordinal)) return Raw(JsonSerializer.Serialize(new
            {
                negotiateVersion = 1,
                connectionId = "fictional-id",
                connectionToken = "fictional-token",
                availableTransports = new[] { new { transport = "LongPolling", transferFormats = new[] { "Text", "Binary" } } }
            }));
            if (request.Method == HttpMethod.Delete) return new(HttpStatusCode.Accepted);
            if (request.Method == HttpMethod.Get)
            { if (firstGet) { firstGet = false; return Raw(string.Empty); } return Raw(await received.Reader.ReadAsync(ct)); }
            var raw = await request.Content!.ReadAsStringAsync(ct);
            foreach (var text in raw.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
            {
                using var json = JsonDocument.Parse(text); var frame = json.RootElement;
                if (frame.TryGetProperty("protocol", out _)) { received.Writer.TryWrite("{}\u001e"); continue; }
                if (!frame.TryGetProperty("target", out var target)) continue;
                var method = target.GetString()!; Methods.Add(method);
                var result = method == "PersonalSnapshot" ? (object)Personal(setup.UserId, setup.PetId) : method == "Snapshot" ? Snapshot(setup.UserId, setup.PetId)
                    : Result(setup.PetId, method switch { "Attach" => "attached", "Heartbeat" => "heartbeat", "TakeControl" => "control", "Leave" => "left", _ => "lease_ended" });
                Push(new { type = 3, invocationId = frame.GetProperty("invocationId").GetString(), result });
            }
            return new(HttpStatusCode.OK);
        }
    }
}
