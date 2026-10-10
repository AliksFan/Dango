using System.Security.Claims;
using System.IO;
using System.Text;
using System.Text.Json;
using Dango.Protocol;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Dango.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.BackendManualReview.Tests;

[TestClass]
public sealed class ManualReviewGuardTests
{
    private const string Email = "manual-test@dango.invalid";
    [TestMethod]
    public async Task Unknown_route_never_reaches_business_pipeline()
    {
        var forwarded = false;
        var guard = new ManualReviewHttpGuard(_ => { forwarded = true; return Task.CompletedTask; }, new(Email, Guid.NewGuid()));
        var context = Request("POST", "/api/v1/auth/password-reset/request", "{\"email\":\"manual-test@dango.invalid\"}");
        await guard.InvokeAsync(context);
        Assert.IsFalse(forwarded);
        Assert.AreEqual(403, context.Response.StatusCode);
    }

    [TestMethod]
    public void Same_key_with_changed_body_is_rejected_before_budget_changes()
    {
        var budget = new ManualReviewBudget(Email, Guid.NewGuid());
        budget.Reserve(ManualReviewOperation.Enter, "room:key", "first"u8);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(ManualReviewOperation.Enter, "room:key", "changed"u8));
    }

    [TestMethod]
    public async Task Only_two_concurrent_room_creations_receive_reservations()
    {
        var budget = new ManualReviewBudget(Email, Guid.NewGuid());
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            try { budget.Reserve(ManualReviewOperation.RoomCreate, "room:" + index, "same"u8); return true; }
            catch (InvalidOperationException) { return false; }
        })));
        Assert.AreEqual(2, outcomes.Count(value => value));
    }

    [TestMethod]
    [DataRow(ManualReviewOperation.RegistrationStart, 1)]
    [DataRow(ManualReviewOperation.RegistrationComplete, 1)]
    [DataRow(ManualReviewOperation.Login, 3)]
    [DataRow(ManualReviewOperation.RoomCreate, 2)]
    [DataRow(ManualReviewOperation.Enter, 4)]
    [DataRow(ManualReviewOperation.CodeReset, 2)]
    [DataRow(ManualReviewOperation.Configuration, 2)]
    public void Every_exact_operation_budget_rejects_next_distinct_key(ManualReviewOperation operation, int limit)
    {
        var budget = new ManualReviewBudget(Email, Guid.NewGuid());
        for (var i = 0; i < limit; i++) budget.Reserve(operation, "key:" + i, "body"u8);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(operation, "overflow", "body"u8));
        Assert.AreEqual(limit, budget.Snapshot()[operation]);
        budget.Reserve(operation, "key:0", "body"u8);
        Assert.AreEqual(limit, budget.Snapshot()[operation]);
    }

    [TestMethod]
    public void Same_key_cannot_switch_operations_or_bind_another_identity()
    {
        var budget = new ManualReviewBudget(Email, Guid.NewGuid());
        budget.Reserve(ManualReviewOperation.Enter, "key", "body"u8);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.Reserve(ManualReviewOperation.RoomCreate, "key", "body"u8));
        var identity = new ManualReviewIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        budget.BindIdentity(identity);
        budget.BindIdentity(identity);
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.BindIdentity(identity with { UserId = Guid.NewGuid() }));
        Assert.AreEqual(identity, budget.Identity);
    }

    [TestMethod]
    public void Constructor_rejects_real_email_empty_invitation_and_empty_identity()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new ManualReviewBudget("person@example.com", Guid.NewGuid()));
        Assert.ThrowsExactly<InvalidOperationException>(() => new ManualReviewBudget(Email, Guid.Empty));
        var budget = new ManualReviewBudget(Email, Guid.NewGuid());
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.BindIdentity(new(Guid.Empty, Guid.NewGuid(), Guid.NewGuid())));
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.AllowConfirmationProof("proof-before-registration"));
    }

    [TestMethod]
    [DataRow("POST", "/api/v1/auth/password-reset/request")]
    [DataRow("POST", "/api/v1/auth/password-reset/complete")]
    [DataRow("POST", "/api/v1/auth/password/change")]
    [DataRow("POST", "/api/v1/auth/registrations/00000000-0000-0000-0000-000000000001/resend")]
    [DataRow("POST", "/room")]
    [DataRow("GET", "/api/v1/rooms/00000000-0000-0000-0000-000000000001")]
    [DataRow("GET", "/api/v1/auth/password-reset")]
    [DataRow("POST", "/api/v1/room-hub/unknown")]
    [DataRow("DELETE", "/api/v1/me/sessions/00000000-0000-0000-0000-000000000001")]
    public async Task Denied_routes_do_not_reach_next(string method, string path)
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Calls;
        var response = await fixture.SendAsync(method, path, "{}");
        Assert.IsTrue(response.Response.StatusCode is 400 or 403);
        Assert.AreEqual(before, fixture.Calls);
    }

    [TestMethod]
    public async Task Registration_scope_is_fixed_and_only_captured_pending_can_be_completed_or_confirmed()
    {
        var pending = Guid.NewGuid();
        var invitation = Guid.NewGuid();
        var budget = new ManualReviewBudget(Email, invitation);
        var called = 0;
        var guard = new ManualReviewHttpGuard(async context =>
        {
            called++;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { id = pending }));
        }, budget);
        var key = Guid.NewGuid();
        var wrong = Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = "other@dango.invalid", invitationId = invitation, invitationProof = "fake", requestKey = key }));
        await guard.InvokeAsync(wrong);
        Assert.AreEqual(0, called);
        var wrongInvite = Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = Email, invitationId = Guid.NewGuid(), invitationProof = "fake", requestKey = key }));
        await guard.InvokeAsync(wrongInvite);
        Assert.AreEqual(0, called);
        var validBody = JsonSerializer.Serialize(new { email = Email, invitationId = invitation, invitationProof = "fake", requestKey = key });
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", validBody));
        Assert.AreEqual(pending, budget.PendingRegistrationId);
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", validBody));
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.RegistrationStart]);
        var badStatus = Request("GET", $"/api/v1/auth/registrations/{Guid.NewGuid():D}/status");
        await guard.InvokeAsync(badStatus);
        Assert.AreEqual(403, badStatus.Response.StatusCode);
        budget.AllowConfirmationProof("allowed-proof");
        Assert.ThrowsExactly<InvalidOperationException>(() => budget.AllowConfirmationProof("other-proof"));
        var badProof = Request("POST", "/api/v1/auth/email-verifications/confirm", "{\"proof\":\"other-proof\"}");
        await guard.InvokeAsync(badProof);
        Assert.AreEqual(403, badProof.Response.StatusCode);
        var before = called;
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/email-verifications/confirm", "{\"proof\":\"allowed-proof\"}"));
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/email-verifications/confirm", "{\"proof\":\"allowed-proof\"}"));
        Assert.AreEqual(before + 2, called);
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.Other]);
    }

    [TestMethod]
    public async Task Complete_verifies_callback_identity_and_preserves_response_stream()
    {
        var identity = new ManualReviewIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pending = Guid.NewGuid();
        var budget = new ManualReviewBudget(Email, Guid.NewGuid(), _ => Task.FromResult(identity));
        var guard = new ManualReviewHttpGuard(async context =>
        {
            var payload = context.Request.Path.Value!.EndsWith("/complete", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { userId = identity.UserId, petId = identity.PetId, roomId = identity.DefaultRoomId })
                : JsonSerializer.Serialize(new { id = pending });
            await context.Response.WriteAsync(payload);
        }, budget);
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = Email, invitationId = budget.InvitationId, invitationProof = "fake", requestKey = Guid.NewGuid() })));
        var completion = Request("POST", $"/api/v1/auth/registrations/{pending:D}/complete", JsonSerializer.Serialize(new { completionKey = Guid.NewGuid(), password = "fake-test-password", appearance = new { } }));
        var original = completion.Response.Body;
        await guard.InvokeAsync(completion);
        Assert.AreSame(original, completion.Response.Body);
        Assert.AreEqual(identity, budget.Identity);
        Assert.IsTrue(((MemoryStream)original).Length > 0);
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.RegistrationComplete]);
    }

    [TestMethod]
    public async Task Other_user_and_unknown_session_are_rejected_even_for_reads_and_hub_negotiate()
    {
        var fixture = await Fixture.CreateAsync();
        foreach (var claims in new[] { Principal(Guid.NewGuid(), fixture.Session), Principal(fixture.Identity.UserId, Guid.NewGuid()), new ClaimsPrincipal() })
        {
            var before = fixture.Calls;
            var read = await fixture.SendAsync("GET", "/api/v1/me/pet", user: claims);
            var negotiate = await fixture.SendAsync("POST", "/api/v1/room-hub/negotiate", user: claims);
            Assert.AreEqual(403, read.Response.StatusCode);
            Assert.AreEqual(403, negotiate.Response.StatusCode);
            Assert.AreEqual(before, fixture.Calls);
        }
    }

    [TestMethod]
    public async Task Login_without_idempotency_counts_identical_attempts_and_does_not_refund_500()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Respond = context => { context.Response.StatusCode = 500; return Task.CompletedTask; };
        var body = JsonSerializer.Serialize(new { email = Email, password = "fake-password", remembered = false });
        await fixture.SendAsync("POST", "/api/v1/auth/login", body);
        await fixture.SendAsync("POST", "/api/v1/auth/login", body);
        var before = fixture.Calls;
        var fourth = await fixture.SendAsync("POST", "/api/v1/auth/login", body);
        Assert.AreEqual(429, fourth.Response.StatusCode);
        Assert.AreEqual(before, fixture.Calls);
        Assert.AreEqual(3, fixture.Budget.Snapshot()[ManualReviewOperation.Login]);
    }

    [TestMethod]
    public async Task Failed_create_keeps_reservation_original_replay_succeeds_changed_name_fails()
    {
        var fixture = await Fixture.CreateAsync();
        var room = Guid.NewGuid();
        var key = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new RoomCreateRequest(key, "privateShared", "test"), Fixture.Json);
        fixture.Respond = _ => throw new IOException("simulated-response-loss");
        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.SendAsync("POST", "/api/v1/rooms", body));
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.RoomCreate]);
        fixture.Respond = context => context.Response.WriteAsync(JsonSerializer.Serialize(new { roomId = room }));
        var replay = await fixture.SendAsync("POST", "/api/v1/rooms", body);
        Assert.AreEqual(200, replay.Response.StatusCode);
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.RoomCreate]);
        var changed = await fixture.SendAsync("POST", "/api/v1/rooms", JsonSerializer.Serialize(new RoomCreateRequest(key, "privateShared", "changed"), Fixture.Json));
        Assert.AreEqual(409, changed.Response.StatusCode);
        var ownRead = await fixture.SendAsync("GET", $"/api/v1/rooms/{room:D}");
        Assert.AreEqual(200, ownRead.Response.StatusCode);
    }

    [TestMethod]
    public async Task Four_enter_reservations_bind_exact_room_lease_instance_and_fifth_is_prewrite_rejected()
    {
        var fixture = await Fixture.CreateAsync();
        for (var i = 0; i < 4; i++) await fixture.EnterAsync();
        var capturedLease = fixture.Lease;
        var capturedInstance = fixture.Instance;
        var before = fixture.Calls;
        await fixture.EnterAsync(expectedStatus: 429);
        Assert.AreEqual(before, fixture.Calls);
        Assert.AreEqual(4, fixture.Budget.Snapshot()[ManualReviewOperation.Enter]);
        var snapshot = await fixture.SendAsync("GET", $"/api/v1/rooms/{fixture.Identity.DefaultRoomId:D}/snapshot?leaseId={capturedLease:D}&instanceId={capturedInstance:D}");
        Assert.AreEqual(200, snapshot.Response.StatusCode);
        var wrongRoom = await fixture.SendAsync("GET", $"/api/v1/rooms/{Guid.NewGuid():D}/snapshot?leaseId={capturedLease:D}&instanceId={capturedInstance:D}");
        var wrongInstance = await fixture.SendAsync("GET", $"/api/v1/rooms/{fixture.Identity.DefaultRoomId:D}/snapshot?leaseId={capturedLease:D}&instanceId={Guid.NewGuid():D}");
        Assert.AreEqual(403, wrongRoom.Response.StatusCode);
        Assert.AreEqual(403, wrongInstance.Response.StatusCode);
    }

    [TestMethod]
    public async Task Unknown_codes_cannot_probe_existing_rooms_and_known_old_codes_can_reach_formal_rejection()
    {
        var fixture = await Fixture.CreateAsync();
        var unknown = await fixture.SendAsync("POST", "/api/v1/rooms/resolve", "{\"code\":\"not-this-run\"}");
        Assert.AreEqual(403, unknown.Response.StatusCode);
        for (var i = 0; i < 2; i++)
        {
            var code = "fictional-code-" + i;
            fixture.Respond = context => context.Response.WriteAsync(JsonSerializer.Serialize(new { roomId = fixture.Identity.DefaultRoomId, code }));
            var result = await fixture.SendAsync("POST", $"/api/v1/rooms/{fixture.Identity.DefaultRoomId:D}/code/reset", JsonSerializer.Serialize(new RoomCodeResetRequest(Guid.NewGuid(), i + 1), Fixture.Json));
            Assert.AreEqual(200, result.Response.StatusCode);
        }
        var overflow = await fixture.SendAsync("POST", $"/api/v1/rooms/{fixture.Identity.DefaultRoomId:D}/code/reset", JsonSerializer.Serialize(new RoomCodeResetRequest(Guid.NewGuid(), 3), Fixture.Json));
        Assert.AreEqual(429, overflow.Response.StatusCode);
        fixture.Respond = context => { context.Response.StatusCode = 404; return Task.CompletedTask; };
        var before = fixture.Calls;
        var old = await fixture.SendAsync("POST", "/api/v1/rooms/resolve", "{\"code\":\"fictional-code-0\"}");
        Assert.AreEqual(404, old.Response.StatusCode);
        Assert.AreEqual(before + 1, fixture.Calls);
    }

    [TestMethod]
    public async Task Configuration_same_version_changed_payload_conflicts_and_third_version_exceeds_budget()
    {
        var fixture = await Fixture.CreateAsync();
        var body = "{\"expectedVersion\":1,\"configuration\":{\"fictional\":1}}";
        await fixture.SendAsync("PATCH", "/api/v1/me/pet/configuration", body);
        await fixture.SendAsync("PATCH", "/api/v1/me/pet/configuration", body);
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.Configuration]);
        var changed = await fixture.SendAsync("PATCH", "/api/v1/me/pet/configuration", body.Replace(":1}", ":2}", StringComparison.Ordinal));
        Assert.AreEqual(409, changed.Response.StatusCode);
        await fixture.SendAsync("PATCH", "/api/v1/me/pet/configuration", body.Replace("Version\":1", "Version\":2", StringComparison.Ordinal));
        var third = await fixture.SendAsync("PATCH", "/api/v1/me/pet/configuration", body.Replace("Version\":1", "Version\":3", StringComparison.Ordinal));
        Assert.AreEqual(429, third.Response.StatusCode);
    }

    [TestMethod]
    [DataRow("{\"email\":\"manual-test@dango.invalid\",\"password\":\"fake\",\"remembered\":false,\"unexpected\":1}")]
    [DataRow("{\"email\":\"manual-test@dango.invalid\",\"email\":\"other@dango.invalid\",\"password\":\"fake\",\"remembered\":false}")]
    [DataRow("{\"Email\":\"manual-test@dango.invalid\",\"password\":\"fake\",\"remembered\":false}")]
    [DataRow("{broken")]
    public async Task Malformed_and_ambiguous_inputs_cannot_spend_login_budget(string body)
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Calls;
        var invalid = await fixture.SendAsync("POST", "/api/v1/auth/login", body);
        Assert.AreEqual(400, invalid.Response.StatusCode);
        Assert.AreEqual(before, fixture.Calls);
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.Login]);
    }

    [TestMethod]
    public async Task Oversized_request_and_response_are_bounded_and_response_body_is_restored_on_failure()
    {
        var fixture = await Fixture.CreateAsync();
        var before = fixture.Calls;
        var tooLarge = await fixture.SendAsync("POST", "/api/v1/auth/login", new string('x', 8193));
        Assert.AreEqual(403, tooLarge.Response.StatusCode);
        Assert.AreEqual(before, fixture.Calls);
        fixture.Respond = context => context.Response.WriteAsync(new string('x', 65537));
        var context = fixture.Request("POST", "/api/v1/rooms", JsonSerializer.Serialize(new RoomCreateRequest(Guid.NewGuid(), "publicShared", "test"), Fixture.Json));
        var original = context.Response.Body;
        await fixture.Guard.InvokeAsync(context);
        Assert.AreSame(original, context.Response.Body);
        Assert.AreEqual(403, context.Response.StatusCode);
        Assert.IsTrue(((MemoryStream)original).Length < 1000);
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.RoomCreate]);
    }

    [TestMethod]
    public async Task Concurrent_http_mutations_are_serialized_and_third_create_never_reaches_business()
    {
        var fixture = await Fixture.CreateAsync();
        var active = 0; var maximum = 0;
        fixture.Respond = async context =>
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            await Task.Delay(20);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { roomId = Guid.NewGuid() }));
            Interlocked.Decrement(ref active);
        };
        var before = fixture.Calls;
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.SendAsync("POST", "/api/v1/rooms", JsonSerializer.Serialize(new RoomCreateRequest(Guid.NewGuid(), "privateShared", "test"), Fixture.Json))));
        Assert.AreEqual(1, maximum);
        Assert.AreEqual(before + 2, fixture.Calls);
        Assert.AreEqual(2, responses.Count(context => context.Response.StatusCode == 200));
        Assert.AreEqual(6, responses.Count(context => context.Response.StatusCode == 429));
    }

    [TestMethod]
    public async Task Hub_rejects_unknown_user_method_lease_room_and_instance_before_next()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.EnterAsync();
        var filter = new ManualReviewHubFilter(fixture.Budget);
        var known = new RoomLeaseRequest(fixture.Lease, fixture.Instance);
        var rejected = new[]
        {
            Invocation(fixture.User, "Enter", []),
            Invocation(fixture.User, "Heartbeat", [known with { LeaseId = Guid.NewGuid() }]),
            Invocation(fixture.User, "Attach", [known with { InstanceId = Guid.NewGuid() }]),
            Invocation(fixture.User, "Snapshot", [Guid.NewGuid(), known]),
            Invocation(Principal(Guid.NewGuid(), fixture.Session), "PersonalSnapshot", []),
            Invocation(fixture.User, "TakeControl", [new RoomControlRequest(Guid.NewGuid(), fixture.Lease, fixture.Instance, Guid.NewGuid(), 1, 1)])
        };
        foreach (var invocation in rejected)
        {
            var forwarded = false;
            await Assert.ThrowsExactlyAsync<HubException>(async () => await filter.InvokeMethodAsync(invocation, _ => { forwarded = true; return ValueTask.FromResult<object?>(null); }));
            Assert.IsFalse(forwarded);
        }
    }

    [TestMethod]
    public async Task Hub_allows_only_exact_captured_lease_and_replay_cannot_change_parameters()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.EnterAsync();
        var filter = new ManualReviewHubFilter(fixture.Budget);
        var known = new RoomLeaseRequest(fixture.Lease, fixture.Instance);
        var forwarded = 0;
        ValueTask<object?> Next(HubInvocationContext _) { forwarded++; return ValueTask.FromResult<object?>(null); }
        await filter.InvokeMethodAsync(Invocation(fixture.User, "PersonalSnapshot", []), Next);
        await filter.InvokeMethodAsync(Invocation(fixture.User, "Attach", [known]), Next);
        await filter.InvokeMethodAsync(Invocation(fixture.User, "Heartbeat", [known]), Next);
        await filter.InvokeMethodAsync(Invocation(fixture.User, "Snapshot", [fixture.Identity.DefaultRoomId, known]), Next);
        var end = new RoomLeaseEndRequest(Guid.NewGuid(), fixture.Lease, fixture.Instance);
        await filter.InvokeMethodAsync(Invocation(fixture.User, "EndLease", [end]), Next);
        await filter.InvokeMethodAsync(Invocation(fixture.User, "EndLease", [end]), Next);
        var before = fixture.Budget.Snapshot()[ManualReviewOperation.Other];
        var control = new RoomControlRequest(end.RequestId, fixture.Lease, fixture.Instance, fixture.Identity.DefaultRoomId, 1, 1);
        await Assert.ThrowsExactlyAsync<HubException>(async () => await filter.InvokeMethodAsync(Invocation(fixture.User, "Leave", [control]), Next));
        Assert.AreEqual(6, forwarded);
        Assert.AreEqual(before, fixture.Budget.Snapshot()[ManualReviewOperation.Other]);
    }

    [TestMethod]
    public async Task Hub_connection_rejects_other_user_and_aborts_before_formal_admission()
    {
        var fixture = await Fixture.CreateAsync();
        var caller = new Caller(Principal(Guid.NewGuid(), fixture.Session));
        var filter = new ManualReviewHubFilter(fixture.Budget);
        var forwarded = false;
        await Assert.ThrowsExactlyAsync<HubException>(() => filter.OnConnectedAsync(new(caller, new ServiceCollection().BuildServiceProvider(), new FakeHub()), _ => { forwarded = true; return Task.CompletedTask; }));
        Assert.IsTrue(caller.Aborted);
        Assert.IsFalse(forwarded);
    }

    [TestMethod]
    public async Task Full_approved_known_object_lifecycle_is_forwarded_and_unknown_targets_are_blocked()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.EnterAsync();
        var room = fixture.Identity.DefaultRoomId;
        var operations = new (string Method, string Path, object? Body)[]
        {
            ("GET", "/api/v1/me", null), ("GET", "/api/v1/me/pet", null), ("GET", "/api/v1/me/sessions", null),
            ("GET", "/api/v1/me/room/snapshot", null), ("GET", "/api/v1/me/rooms?state=dissolved&limit=3", null),
            ("GET", "/api/v1/me/rooms?state=all&after=" + room.ToString("D"), null),
            ("PATCH", $"/api/v1/rooms/{room:D}", new RoomUpdateRequest(Guid.NewGuid(), 1, "fictional rename", 2)),
            ("POST", $"/api/v1/rooms/{room:D}/dissolve", new RoomDissolveRequest(Guid.NewGuid(), 2, true)),
            ("POST", $"/api/v1/rooms/{room:D}/restore", new RoomRestoreRequest(Guid.NewGuid(), 3)),
            ("POST", "/api/v1/me/room/control", new RoomControlRequest(Guid.NewGuid(), fixture.Lease, fixture.Instance, room, 1, 1)),
            ("POST", "/api/v1/me/room/leave", new RoomControlRequest(Guid.NewGuid(), fixture.Lease, fixture.Instance, room, 1, 1)),
            ("POST", "/api/v1/me/room/lease/end", new RoomLeaseEndRequest(Guid.NewGuid(), fixture.Lease, fixture.Instance)),
            ("POST", "/api/v1/auth/logout", null), ("POST", "/api/v1/room-hub/negotiate", null)
        };
        foreach (var operation in operations)
        {
            var before = fixture.Calls;
            var context = await fixture.SendAsync(operation.Method, operation.Path,
                operation.Body is null ? "" : JsonSerializer.Serialize(operation.Body, Fixture.Json));
            Assert.AreEqual(200, context.Response.StatusCode, operation.Path);
            Assert.AreEqual(before + 1, fixture.Calls, operation.Path);
        }
        var rejectedPaths = new[]
        {
            $"/api/v1/rooms/{Guid.NewGuid():D}/dissolve", $"/api/v1/rooms/{Guid.NewGuid():D}/restore",
            $"/api/v1/rooms/{room:D}/users/{fixture.Identity.UserId:D}/ban",
            $"/api/v1/rooms/{room:D}/users/{fixture.Identity.UserId:D}/remove",
            $"/api/v1/rooms/{room:D}/users/{fixture.Identity.UserId:D}/unban"
        };
        foreach (var path in rejectedPaths)
        {
            var before = fixture.Calls;
            var result = await fixture.SendAsync("POST", path, "{}");
            Assert.AreEqual(403, result.Response.StatusCode);
            Assert.AreEqual(before, fixture.Calls);
        }
    }

    [TestMethod]
    public async Task Reused_key_cannot_change_route_target_or_registration_resume_header()
    {
        var fixture = await Fixture.CreateAsync();
        var key = Guid.NewGuid();
        await fixture.SendAsync("PATCH", $"/api/v1/rooms/{fixture.Identity.DefaultRoomId:D}", JsonSerializer.Serialize(new RoomUpdateRequest(key, 1, "fake", 2), Fixture.Json));
        var changedOperation = await fixture.SendAsync("POST", $"/api/v1/rooms/{fixture.Identity.DefaultRoomId:D}/dissolve", JsonSerializer.Serialize(new RoomDissolveRequest(key, 1, true), Fixture.Json));
        Assert.AreEqual(409, changedOperation.Response.StatusCode);
        var invitation = Guid.NewGuid(); var pending = Guid.NewGuid();
        var budget = new ManualReviewBudget(Email, invitation);
        var called = 0;
        var guard = new ManualReviewHttpGuard(context => { called++; return context.Response.WriteAsync(JsonSerializer.Serialize(new { id = pending })); }, budget);
        var body = JsonSerializer.Serialize(new { email = Email, invitationId = invitation, invitationProof = "fake", requestKey = key });
        var first = Request("POST", "/api/v1/auth/registrations", body);
        first.Request.Headers["X-Registration-Proof"] = "fictional-original";
        await guard.InvokeAsync(first);
        var changed = Request("POST", "/api/v1/auth/registrations", body);
        changed.Request.Headers["X-Registration-Proof"] = "fictional-changed";
        await guard.InvokeAsync(changed);
        Assert.AreEqual(409, changed.Response.StatusCode);
        Assert.AreEqual(1, called);
    }

    [TestMethod]
    public async Task Response_loss_never_refunds_login_and_invalid_capture_never_adds_foreign_lease()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Respond = context => context.Response.WriteAsync(JsonSerializer.Serialize(new { petId = Guid.NewGuid(), roomId = fixture.Identity.DefaultRoomId, leaseId = Guid.NewGuid() }));
        var instance = Guid.NewGuid();
        var entered = await fixture.SendAsync("POST", $"/api/v1/rooms/{fixture.Identity.DefaultRoomId:D}/enter", JsonSerializer.Serialize(new RoomEnterRequest(Guid.NewGuid(), instance, "windows", fixture.Identity.DefaultRoomId, 1), Fixture.Json));
        Assert.AreEqual(403, entered.Response.StatusCode);
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.Enter]);
        fixture.Respond = _ => throw new IOException("simulated-network-write-loss");
        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.SendAsync("POST", "/api/v1/auth/login", JsonSerializer.Serialize(new { email = Email, password = "fake", remembered = false })));
        Assert.AreEqual(2, fixture.Budget.Snapshot()[ManualReviewOperation.Login]);
    }

    [TestMethod]
    public async Task Response_capture_is_bound_when_client_output_fails_and_replay_does_not_spend_extra_quota()
    {
        var fixture = await Fixture.CreateAsync();
        var room = Guid.NewGuid(); var key = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new RoomCreateRequest(key, "publicShared", "fake"), Fixture.Json);
        fixture.Respond = context => context.Response.WriteAsync(JsonSerializer.Serialize(new { roomId = room }));
        var request = fixture.Request("POST", "/api/v1/rooms", body);
        var original = new FailingOutput();
        request.Response.Body = original;
        await Assert.ThrowsExactlyAsync<IOException>(() => fixture.Guard.InvokeAsync(request));
        Assert.AreSame(original, request.Response.Body);
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.RoomCreate]);
        var read = await fixture.SendAsync("GET", $"/api/v1/rooms/{room:D}");
        Assert.AreEqual(200, read.Response.StatusCode);
        var replay = await fixture.SendAsync("POST", "/api/v1/rooms", body);
        Assert.AreEqual(200, replay.Response.StatusCode);
        Assert.AreEqual(1, fixture.Budget.Snapshot()[ManualReviewOperation.RoomCreate]);
    }

    [TestMethod]
    public async Task Invalid_complete_callback_identity_leaves_account_unbound_and_budget_consumed()
    {
        var identity = new ManualReviewIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pending = Guid.NewGuid();
        var budget = new ManualReviewBudget(Email, Guid.NewGuid(), _ => Task.FromResult(identity with { UserId = Guid.NewGuid() }));
        var guard = new ManualReviewHttpGuard(context => context.Response.WriteAsync(context.Request.Path.Value!.EndsWith("/complete", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(new { userId = identity.UserId, petId = identity.PetId, roomId = identity.DefaultRoomId })
            : JsonSerializer.Serialize(new { id = pending })), budget);
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = Email, invitationId = budget.InvitationId, invitationProof = "fake", requestKey = Guid.NewGuid() })));
        var complete = Request("POST", $"/api/v1/auth/registrations/{pending:D}/complete", JsonSerializer.Serialize(new { completionKey = Guid.NewGuid(), password = "fake", appearance = new { } }));
        await guard.InvokeAsync(complete);
        Assert.AreEqual(403, complete.Response.StatusCode);
        Assert.IsNull(budget.Identity);
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.RegistrationComplete]);
    }

    [TestMethod]
    public async Task Query_aliases_and_missing_json_fields_are_prewrite_rejected()
    {
        var fixture = await Fixture.CreateAsync();
        foreach (var path in new[] { "/api/v1/me?extra=1", "/api/v1/me/rooms?after=" + Guid.NewGuid().ToString("D"), "/api/v1/me/rooms?state=all&state=active", "/api/v1/rooms/", "/api//v1/me", "/api/v1/auth/request-protection?extra=1" })
        {
            var before = fixture.Calls;
            var result = await fixture.SendAsync("GET", path);
            Assert.AreEqual(403, result.Response.StatusCode);
            Assert.AreEqual(before, fixture.Calls);
        }
        var missing = await fixture.SendAsync("POST", "/api/v1/rooms", "{\"requestId\":\"" + Guid.NewGuid().ToString("D") + "\"}");
        Assert.AreEqual(400, missing.Response.StatusCode);
        var empty = await fixture.SendAsync("POST", "/api/v1/rooms", JsonSerializer.Serialize(new RoomCreateRequest(Guid.Empty, "privateShared", "fake"), Fixture.Json));
        Assert.AreEqual(403, empty.Response.StatusCode);
        Assert.AreEqual(0, fixture.Budget.Snapshot()[ManualReviewOperation.RoomCreate]);
    }

    [TestMethod]
    public async Task Complete_response_canceled_before_return_recovers_existing_identity_before_login()
    {
        var identity = new ManualReviewIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pending = Guid.NewGuid(); var session = Guid.NewGuid();
        var resolved = 0;
        var budget = new ManualReviewBudget(Email, Guid.NewGuid(), _ => { resolved++; return Task.FromResult(identity); });
        using var cancellation = new CancellationTokenSource();
        var guard = new ManualReviewHttpGuard(async context =>
        {
            if (context.Request.Path.Value!.EndsWith("/complete", StringComparison.Ordinal))
            {
                // The business transaction committed, but cancellation prevented its response
                // from returning to this guard. No identity has been captured yet.
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            if (context.Request.Path.Value.EndsWith("/login", StringComparison.Ordinal))
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { userId = identity.UserId, sessionId = session }));
            else await context.Response.WriteAsync(JsonSerializer.Serialize(new { id = pending }));
        }, budget);
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = Email, invitationId = budget.InvitationId, invitationProof = "fake", requestKey = Guid.NewGuid() })));
        var complete = Request("POST", $"/api/v1/auth/registrations/{pending:D}/complete", JsonSerializer.Serialize(new { completionKey = Guid.NewGuid(), password = "fake-test-password", appearance = new { } }));
        complete.RequestAborted = cancellation.Token;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => guard.InvokeAsync(complete));
        Assert.IsNull(budget.Identity);
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.RegistrationComplete]);
        var login = Request("POST", "/api/v1/auth/login", JsonSerializer.Serialize(new { email = Email, password = "fake-test-password", remembered = false }));
        await guard.InvokeAsync(login);
        Assert.AreEqual(200, login.Response.StatusCode);
        Assert.AreEqual(identity, budget.Identity);
        Assert.AreEqual(1, resolved);
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.Login]);
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.RegistrationComplete]);
    }

    [TestMethod]
    public async Task Successful_complete_capture_uses_host_lifetime_after_client_cancellation()
    {
        var identity = new ManualReviewIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pending = Guid.NewGuid();
        var resolverCanceled = false;
        var budget = new ManualReviewBudget(Email, Guid.NewGuid(), ct =>
        {
            resolverCanceled = ct.IsCancellationRequested;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(identity);
        });
        using var cancellation = new CancellationTokenSource();
        var guard = new ManualReviewHttpGuard(async context =>
        {
            if (context.Request.Path.Value!.EndsWith("/complete", StringComparison.Ordinal))
            {
                await context.Response.WriteAsync(JsonSerializer.Serialize(new { userId = identity.UserId, petId = identity.PetId, roomId = identity.DefaultRoomId }));
                cancellation.Cancel();
            }
            else await context.Response.WriteAsync(JsonSerializer.Serialize(new { id = pending }));
        }, budget);
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = Email, invitationId = budget.InvitationId, invitationProof = "fake", requestKey = Guid.NewGuid() })));
        var complete = Request("POST", $"/api/v1/auth/registrations/{pending:D}/complete", JsonSerializer.Serialize(new { completionKey = Guid.NewGuid(), password = "fake-test-password", appearance = new { } }));
        complete.RequestAborted = cancellation.Token;
        try { await guard.InvokeAsync(complete); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        Assert.IsFalse(resolverCanceled);
        Assert.AreEqual(identity, budget.Identity);
        Assert.AreEqual(1, budget.Snapshot()[ManualReviewOperation.RegistrationComplete]);
    }

    [TestMethod]
    public async Task Recovery_rejects_resolver_identity_different_from_successful_completion_receipt()
    {
        var identity = new ManualReviewIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var pending = Guid.NewGuid(); var loginForwarded = false;
        var budget = new ManualReviewBudget(Email, Guid.NewGuid(), _ => Task.FromResult(identity with { UserId = Guid.NewGuid() }));
        var guard = new ManualReviewHttpGuard(context =>
        {
            if (context.Request.Path.Value!.EndsWith("/login", StringComparison.Ordinal)) loginForwarded = true;
            return context.Response.WriteAsync(context.Request.Path.Value.EndsWith("/complete", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { userId = identity.UserId, petId = identity.PetId, roomId = identity.DefaultRoomId })
                : JsonSerializer.Serialize(new { id = pending }));
        }, budget);
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = Email, invitationId = budget.InvitationId, invitationProof = "fake", requestKey = Guid.NewGuid() })));
        var complete = Request("POST", $"/api/v1/auth/registrations/{pending:D}/complete", JsonSerializer.Serialize(new { completionKey = Guid.NewGuid(), password = "fake", appearance = new { } }));
        await guard.InvokeAsync(complete);
        Assert.AreEqual(403, complete.Response.StatusCode);
        var login = Request("POST", "/api/v1/auth/login", JsonSerializer.Serialize(new { email = Email, password = "fake", remembered = false }));
        await guard.InvokeAsync(login);
        Assert.AreEqual(403, login.Response.StatusCode);
        Assert.IsFalse(loginForwarded);
        Assert.IsNull(budget.Identity);
        Assert.AreEqual(0, budget.Snapshot()[ManualReviewOperation.Login]);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("null")]
    [DataRow("throws")]
    [DataRow("empty")]
    public async Task Recovery_missing_null_failed_or_empty_resolver_never_forwards_login(string mode)
    {
        var pending = Guid.NewGuid(); var loginForwarded = false;
        Func<CancellationToken, Task<ManualReviewIdentity>>? resolver = mode switch
        {
            "missing" => null,
            "null" => _ => Task.FromResult<ManualReviewIdentity>(null!),
            "throws" => _ => throw new IOException("fictional-not-completed"),
            _ => _ => Task.FromResult(new ManualReviewIdentity(Guid.Empty, Guid.NewGuid(), Guid.NewGuid()))
        };
        var budget = new ManualReviewBudget(Email, Guid.NewGuid(), resolver);
        var guard = new ManualReviewHttpGuard(context =>
        {
            if (context.Request.Path.Value!.EndsWith("/login", StringComparison.Ordinal)) loginForwarded = true;
            return context.Response.WriteAsync(JsonSerializer.Serialize(new { id = pending }));
        }, budget);
        await guard.InvokeAsync(Request("POST", "/api/v1/auth/registrations", JsonSerializer.Serialize(new { email = Email, invitationId = budget.InvitationId, invitationProof = "fake", requestKey = Guid.NewGuid() })));
        var login = Request("POST", "/api/v1/auth/login", JsonSerializer.Serialize(new { email = Email, password = "fake", remembered = false }));
        await guard.InvokeAsync(login);
        Assert.AreEqual(403, login.Response.StatusCode);
        Assert.IsFalse(loginForwarded);
        Assert.IsNull(budget.Identity);
        Assert.AreEqual(0, budget.Snapshot()[ManualReviewOperation.Login]);
    }

    private sealed class FailingOutput : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("simulated-client-output-loss");
    }

    private static HubInvocationContext Invocation(ClaimsPrincipal user, string method, object?[] arguments) =>
        new(new Caller(user), new ServiceCollection().BuildServiceProvider(), new FakeHub(), typeof(FakeHub).GetMethod(method)!, arguments);

    private sealed class FakeHub : Hub
    {
        public void PersonalSnapshot() { }
        public void Enter() { }
        public void Attach(RoomLeaseRequest request) { _ = request; }
        public void Heartbeat(RoomLeaseRequest request) { _ = request; }
        public void Snapshot(Guid room, RoomLeaseRequest request) { _ = room; _ = request; }
        public void TakeControl(RoomControlRequest request) { _ = request; }
        public void Leave(RoomControlRequest request) { _ = request; }
        public void EndLease(RoomLeaseEndRequest request) { _ = request; }
    }
    private sealed class Caller(ClaimsPrincipal user) : HubCallerContext
    {
        public bool Aborted { get; private set; }
        public override string ConnectionId => "fictional-connection";
        public override string? UserIdentifier => user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => Aborted = true;
    }

    private sealed class Fixture
    {
        internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        public ManualReviewIdentity Identity { get; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        public Guid Session { get; } = Guid.NewGuid();
        public Guid Instance { get; private set; }
        public Guid Lease { get; private set; }
        public ClaimsPrincipal User => Principal(Identity.UserId, Session);
        public ManualReviewBudget Budget { get; } = new(Email, Guid.NewGuid());
        public ManualReviewHttpGuard Guard { get; }
        public Func<HttpContext, Task> Respond { get; set; } = _ => Task.CompletedTask;
        public int Calls;
        private Fixture()
        {
            Guard = new(async context => { Interlocked.Increment(ref Calls); await Respond(context); }, Budget);
            Budget.BindIdentity(Identity);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.Respond = context => context.Response.WriteAsync(JsonSerializer.Serialize(new { userId = fixture.Identity.UserId, sessionId = fixture.Session }));
            await fixture.SendAsync("POST", "/api/v1/auth/login", JsonSerializer.Serialize(new { email = Email, password = "fake-test-password", remembered = false }));
            fixture.Respond = _ => Task.CompletedTask;
            return fixture;
        }
        public async Task EnterAsync(int expectedStatus = 200)
        {
            Instance = Guid.NewGuid(); Lease = Guid.NewGuid();
            Respond = context => context.Response.WriteAsync(JsonSerializer.Serialize(new { petId = Identity.PetId, roomId = Identity.DefaultRoomId, leaseId = Lease }));
            var response = await SendAsync("POST", $"/api/v1/rooms/{Identity.DefaultRoomId:D}/enter", JsonSerializer.Serialize(new RoomEnterRequest(Guid.NewGuid(), Instance, "windows", Identity.DefaultRoomId, 1), Json));
            Assert.AreEqual(expectedStatus, response.Response.StatusCode);
            Respond = _ => Task.CompletedTask;
        }
        public DefaultHttpContext Request(string method, string path, string body = "", ClaimsPrincipal? user = null)
        {
            var context = ManualReviewGuardTests.Request(method, path, body);
            context.User = user ?? User;
            return context;
        }
        public async Task<DefaultHttpContext> SendAsync(string method, string path, string body = "", ClaimsPrincipal? user = null)
        {
            var context = Request(method, path, body, user);
            await Guard.InvokeAsync(context);
            return context;
        }
    }

    private static ClaimsPrincipal Principal(Guid user, Guid session) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user.ToString("D")), new Claim(FormalHost.SessionClaim, session.ToString("D"))], "test-only"));

    private static DefaultHttpContext Request(string method, string path, string body = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        var queryIndex = path.IndexOf('?');
        context.Request.Path = queryIndex < 0 ? path : path[..queryIndex];
        context.Request.QueryString = queryIndex < 0 ? QueryString.Empty : new(path[queryIndex..]);
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Response.Body = new MemoryStream();
        return context;
    }
}
