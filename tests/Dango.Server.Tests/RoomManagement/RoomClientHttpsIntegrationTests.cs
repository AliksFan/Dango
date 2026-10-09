using System.Net;
using System.Text.Json;
using Dango.Client.Sync.Accounts;
using Dango.Client.Sync.Rooms;
using Dango.Protocol;
using Dango.Server.Persistence;
using Dango.Server.Tests.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dango.Server.Tests.RoomManagement;

[TestClass]
[TestCategory("PostgresIntegration")]
[TestCategory("BackendB5BIntegration")]
public sealed class RoomClientHttpsIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [TestMethod]
    public async Task RealClientPagesOnlyOwnRoomsAndServerProtectsKindQuotaDefaultRoomAndOtherOwner()
    {
        await using var fixture = await CreateFixtureAsync();
        using var owner = CreateClient(fixture);
        using var other = CreateClient(fixture);
        var account = await RegisterAsync(fixture, owner.Account);
        var foreign = await RegisterAsync(fixture, other.Account);
        var own = owner.Account.CreateRoomManagementClient(account.Identity);
        var theirs = other.Account.CreateRoomManagementClient(foreign.Identity);
        Assert.AreEqual(fixture.Endpoint, own.Origin);
        Assert.AreEqual(account.Identity.UserId, own.UserId);
        Assert.AreEqual(account.Identity.PetId, own.PetId);
        var privateRoom = await own.CreateAsync(new(Guid.NewGuid(), "privateShared", "  e\u0301 room  "), None);
        var publicRoom = await own.CreateAsync(new(Guid.NewGuid(), "publicShared", "Fictional public room", 12), None);
        var third = await own.CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional third room", 1), None);
        await theirs.CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional foreign room"), None);
        var foreignBefore = await AccountGraphAsync(fixture, foreign.Identity.UserId);
        await FaultAsync(own.CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional fourth room"), None),
            RoomManagementFailure.Conflict, "room_quota_reached");
        CollectionAssert.AreEquivalent(new[] { account.DesktopRoomId, privateRoom.RoomId, publicRoom.RoomId, third.RoomId },
            await AllPagesAsync(own, "all", 4));
        CollectionAssert.AreEquivalent(new[] { foreign.DesktopRoomId, (await theirs.ListAsync("active", null, 50, None)).Rooms.Single(r => r.Kind != "desktopPet").RoomId },
            await AllPagesAsync(theirs, "all", 2));
        var detail = await own.ReadAsync(privateRoom.RoomId, None);
        Assert.AreEqual("é room", detail.Name);
        Assert.AreEqual("privateShared", detail.Kind);
        Assert.AreEqual("ordinary-default", detail.TemplateId);
        Assert.AreEqual(1, detail.TemplateVersion);
        Assert.AreEqual(6, detail.Capacity);
        Assert.AreEqual(1, detail.ReservedSeats);
        Assert.AreEqual(0, detail.Occupancy);
        await FaultAsync(theirs.ReadAsync(detail.RoomId, None), RoomManagementFailure.Rejected, "room_not_found");
        await FaultAsync(theirs.UpdateAsync(detail.RoomId, new(Guid.NewGuid(), detail.Version, "foreign edit", 6), None),
            RoomManagementFailure.Rejected, "room_not_found");
        await FaultAsync(theirs.DissolveAsync(detail.RoomId, new(Guid.NewGuid(), detail.Version, true), None),
            RoomManagementFailure.Rejected, "room_not_found");
        using (var raw = RawClient(fixture, owner.Account.Cookies))
        {
            using (var attemptedKindChange = await FormalTestFixture.SendAsync(raw, HttpMethod.Patch, $"api/v1/rooms/{detail.RoomId:D}",
                new { requestId = Guid.NewGuid(), expectedVersion = detail.Version, name = "immutable kind", capacity = 6, kind = "publicShared" }))
                await ErrorAsync(attemptedKindChange, HttpStatusCode.BadRequest, "request_invalid");
            using (var illegalKind = await FormalTestFixture.SendAsync(raw, HttpMethod.Post, "api/v1/rooms",
                new RoomCreateRequest(Guid.NewGuid(), "desktopPet", "Fictional illegal desktop room")))
                await ErrorAsync(illegalKind, HttpStatusCode.BadRequest, "room_kind_invalid");
            using (var illegalCapacity = await FormalTestFixture.SendAsync(raw, HttpMethod.Post, "api/v1/rooms",
                new RoomCreateRequest(Guid.NewGuid(), "privateShared", "Fictional illegal capacity", 0)))
                await ErrorAsync(illegalCapacity, HttpStatusCode.BadRequest, "room_capacity_invalid");
        }
        await own.UpdateAsync(detail.RoomId, new(Guid.NewGuid(), detail.Version, "Fictional renamed private room", 7), None);
        detail = await own.ReadAsync(detail.RoomId, None);
        Assert.AreEqual("privateShared", detail.Kind);
        var publicDetail = await own.ReadAsync(publicRoom.RoomId, None);
        Assert.AreEqual(0, publicDetail.ReservedSeats);
        await own.UpdateAsync(publicDetail.RoomId, new(Guid.NewGuid(), publicDetail.Version, "Fictional renamed public room", 11), None);
        Assert.AreEqual("publicShared", (await own.ReadAsync(publicDetail.RoomId, None)).Kind);
        var desktop = await own.ReadAsync(account.DesktopRoomId, None);
        Assert.AreEqual("desktopPet", desktop.Kind);
        Assert.AreEqual("desktop-pet-default", desktop.TemplateId);
        Assert.AreEqual(2, desktop.Capacity);
        await FaultAsync(own.DissolveAsync(desktop.RoomId, new(Guid.NewGuid(), desktop.Version, true), None),
            RoomManagementFailure.Conflict, "room_desktop_protected");
        await own.DissolveAsync(detail.RoomId, new(Guid.NewGuid(), detail.Version, true), None);
        detail = await own.ReadAsync(detail.RoomId, None);
        Assert.AreEqual("dissolved", detail.Status);
        Assert.IsTrue(detail.Recoverable);
        Assert.IsNotNull(detail.RecoverUntil);
        CollectionAssert.AreEquivalent(new[] { detail.RoomId }, await AllPagesAsync(own, "dissolved", 1));
        CollectionAssert.AreEquivalent(new[] { account.DesktopRoomId, publicRoom.RoomId, third.RoomId }, await AllPagesAsync(own, "active", 3));
        await FaultAsync(theirs.RestoreAsync(detail.RoomId, new(Guid.NewGuid(), detail.Version), None),
            RoomManagementFailure.Rejected, "room_not_found");
        Assert.AreEqual(foreignBefore, await AccountGraphAsync(fixture, foreign.Identity.UserId));
        await AssertManagementReceiptsAsync(fixture, account.Identity.UserId, 6);
        Assert.IsTrue(owner.Transport.RoomWrites.All(request => request.HasCsrf), "All real room writes need the controlled session's CSRF proof.");
    }

    [TestMethod]
    public async Task IndependentClientsRejectStaleVersionAndLoggedOutBindingCannotUseReplacementSession()
    {
        await using var fixture = await CreateFixtureAsync();
        using var first = CreateClient(fixture);
        var account = await RegisterAsync(fixture, first.Account);
        var firstRooms = first.Account.CreateRoomManagementClient(account.Identity);
        var created = await firstRooms.CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional CAS room"), None);
        using var second = CreateClient(fixture);
        var secondIdentity = await second.Account.LoginAsync(account.Email, account.Password, false, None);
        var secondRooms = second.Account.CreateRoomManagementClient(secondIdentity);
        var before = await AccountIdentityAsync(fixture, account.Identity.UserId);
        var left = await firstRooms.ReadAsync(created.RoomId, None);
        var right = await secondRooms.ReadAsync(created.RoomId, None);
        Assert.AreEqual(left.Version, right.Version);
        await firstRooms.UpdateAsync(created.RoomId, new(Guid.NewGuid(), left.Version, "Fictional first committed name", 7), None);
        await FaultAsync(secondRooms.UpdateAsync(created.RoomId, new(Guid.NewGuid(), right.Version, "Fictional stale name", 8), None),
            RoomManagementFailure.Conflict, "room_version_conflict");
        var current = await secondRooms.ReadAsync(created.RoomId, None);
        Assert.AreEqual(left.Version + 1, current.Version);
        Assert.AreEqual("Fictional first committed name", current.Name);
        Assert.AreEqual(7, current.Capacity);
        Assert.AreEqual(before, await AccountIdentityAsync(fixture, account.Identity.UserId));
        await first.Account.LogoutAsync(None);
        var beforeRejected = first.Transport.RequestCount;
        await FaultAsync(firstRooms.ListAsync("all", null, 20, None), RoomManagementFailure.Authentication, "room_binding_changed");
        Assert.AreEqual(beforeRejected, first.Transport.RequestCount, "An old binding must fail before transport after logout.");
        var replacement = first.Account.CreateRoomManagementClient(await first.Account.LoginAsync(account.Email, account.Password, false, None));
        beforeRejected = first.Transport.RequestCount;
        await FaultAsync(firstRooms.ReadAsync(created.RoomId, None), RoomManagementFailure.Authentication, "room_binding_changed");
        Assert.AreEqual(beforeRejected, first.Transport.RequestCount, "Re-login to the same identity cannot revive the old generation's adapter.");
        Assert.AreEqual(current, await replacement.ReadAsync(created.RoomId, None));
        // The independently authenticated second session survives the first session's logout.
        Assert.AreEqual(current, await secondRooms.ReadAsync(created.RoomId, None));
        await AssertManagementReceiptsAsync(fixture, account.Identity.UserId, 2);
        await fixture.InScopeAsync(async services =>
        {
            var sessions = await services.GetRequiredService<DangoDbContext>().AuthSessions.AsNoTracking()
                .Where(s => s.UserId == account.Identity.UserId).ToArrayAsync();
            Assert.AreEqual(3, sessions.Length);
            Assert.AreEqual(1, sessions.Count(s => s.RevokedAt is not null));
        });
    }

    [TestMethod]
    public async Task CommittedCrudResponseLossReplaysOriginalReceiptThenReadsCurrentStateAndRejectsChangedBody()
    {
        await using var fixture = await CreateFixtureAsync();
        using var session = CreateClient(fixture);
        var account = await RegisterAsync(fixture, session.Account);
        var rooms = session.Account.CreateRoomManagementClient(account.Identity);
        var create = new RoomCreateRequest(Guid.NewGuid(), "privateShared", "Fictional recovery room");
        session.Transport.LoseNext = "created";
        await UnknownAsync(() => rooms.CreateAsync(create, None), session.Transport);
        var committed = (await rooms.ListAsync("all", null, 20, None)).Rooms.Single(r => r.Kind != "desktopPet");
        var originalCreate = await rooms.CreateAsync(create, None);
        Assert.AreEqual(new RoomOperationResult(committed.RoomId, 1, "created"), originalCreate);
        var update = new RoomUpdateRequest(Guid.NewGuid(), 1, "Fictional recovered update", 7);
        session.Transport.LoseNext = "updated";
        await UnknownAsync(() => rooms.UpdateAsync(committed.RoomId, update, None), session.Transport);
        var originalUpdate = await rooms.UpdateAsync(committed.RoomId, update, None);
        Assert.AreEqual(new RoomOperationResult(committed.RoomId, 2, "updated"), originalUpdate);
        Assert.AreEqual(update.Name, (await rooms.ReadAsync(committed.RoomId, None)).Name);
        var dissolve = new RoomDissolveRequest(Guid.NewGuid(), 2, true);
        session.Transport.LoseNext = "dissolved";
        await UnknownAsync(() => rooms.DissolveAsync(committed.RoomId, dissolve, None), session.Transport);
        var originalDissolve = await rooms.DissolveAsync(committed.RoomId, dissolve, None);
        Assert.AreEqual(new RoomOperationResult(committed.RoomId, 3, "dissolved"), originalDissolve);
        Assert.AreEqual("dissolved", (await rooms.ReadAsync(committed.RoomId, None)).Status);
        var restore = new RoomRestoreRequest(Guid.NewGuid(), 3);
        session.Transport.LoseNext = "restored";
        await UnknownAsync(() => rooms.RestoreAsync(committed.RoomId, restore, None), session.Transport);
        var originalRestore = await rooms.RestoreAsync(committed.RoomId, restore, None);
        Assert.AreEqual(new RoomOperationResult(committed.RoomId, 4, "restored"), originalRestore);
        var restored = await rooms.ReadAsync(committed.RoomId, None);
        Assert.AreEqual("active", restored.Status);
        Assert.AreEqual(4L, restored.Version);
        Assert.IsNull(restored.RecoverUntil);
        // Every later replay still acknowledges its original version, even while current state is active v4.
        Assert.AreEqual(originalCreate, await rooms.CreateAsync(create, None));
        Assert.AreEqual(originalUpdate, await rooms.UpdateAsync(committed.RoomId, update, None));
        Assert.AreEqual(originalDissolve, await rooms.DissolveAsync(committed.RoomId, dissolve, None));
        Assert.AreEqual(originalRestore, await rooms.RestoreAsync(committed.RoomId, restore, None));
        Assert.AreEqual(restored, await rooms.ReadAsync(committed.RoomId, None));
        await FaultAsync(rooms.CreateAsync(create with { Name = "Different body" }, None), RoomManagementFailure.Conflict, "room_request_conflict");
        await FaultAsync(rooms.UpdateAsync(committed.RoomId, update with { Capacity = 8 }, None), RoomManagementFailure.Conflict, "room_request_conflict");
        await FaultAsync(rooms.DissolveAsync(committed.RoomId, dissolve with { ExpectedVersion = 4 }, None), RoomManagementFailure.Conflict, "room_request_conflict");
        await FaultAsync(rooms.RestoreAsync(committed.RoomId, restore with { ExpectedVersion = 4 }, None), RoomManagementFailure.Conflict, "room_request_conflict");
        Assert.AreEqual(restored, await rooms.ReadAsync(committed.RoomId, None));
        foreach (var requestId in new[] { create.RequestId, update.RequestId, dissolve.RequestId, restore.RequestId })
        {
            var submissions = session.Transport.RoomWrites.Where(r => r.RequestId == requestId).ToArray();
            Assert.AreEqual(4, submissions.Length, "One lost response, two explicit replays and one changed-body attempt only; no automatic retry.");
            Assert.IsTrue(submissions.All(r => r.HasCsrf));
        }
        await AssertManagementReceiptsAsync(fixture, account.Identity.UserId, 4);
    }

    [TestMethod]
    public async Task DissolutionDetachesCapturedPetsAndLeasesRevokesCodeAndRestorationNeverMovesThemBack()
    {
        await using var fixture = await CreateFixtureAsync();
        using var owner = CreateClient(fixture);
        using var visitor = CreateClient(fixture);
        using var unrelated = CreateClient(fixture);
        var account = await RegisterAsync(fixture, owner.Account);
        var guest = await RegisterAsync(fixture, visitor.Account);
        var untouched = await RegisterAsync(fixture, unrelated.Account);
        var rooms = owner.Account.CreateRoomManagementClient(account.Identity);
        var created = await rooms.CreateAsync(new(Guid.NewGuid(), "publicShared", "Fictional detach room", 2), None);
        await unrelated.Account.CreateRoomManagementClient(untouched.Identity)
            .CreateAsync(new(Guid.NewGuid(), "privateShared", "Fictional untouched room"), None);
        var otherBefore = await AccountGraphAsync(fixture, untouched.Identity.UserId);
        using var ownRaw = RawClient(fixture, owner.Account.Cookies);
        using var guestRaw = RawClient(fixture, visitor.Account.Cookies);
        RoomCodeResult code;
        using (var reset = await FormalTestFixture.SendAsync(ownRaw, HttpMethod.Post, $"api/v1/rooms/{created.RoomId:D}/code/reset",
            new RoomCodeResetRequest(Guid.NewGuid(), created.AppliedVersion)))
            code = await FormalTestFixture.ReadAsync<RoomCodeResult>(reset);
        using (var resolve = await FormalTestFixture.SendAsync(guestRaw, HttpMethod.Post, "api/v1/rooms/resolve", new RoomCodeResolveRequest(code.Code)))
            Assert.AreEqual(created.RoomId, (await FormalTestFixture.ReadAsync<RoomDoorView>(resolve)).RoomId);
        var ownerEntered = await EnterAsync(ownRaw, created.RoomId);
        var visitorEntered = await EnterAsync(guestRaw, created.RoomId, code.Code);
        Assert.AreEqual(account.Identity.PetId, ownerEntered.PetId);
        Assert.AreEqual(guest.Identity.PetId, visitorEntered.PetId);
        Assert.IsNotNull(ownerEntered.LeaseId);
        Assert.IsNotNull(visitorEntered.LeaseId);
        var occupied = await rooms.ReadAsync(created.RoomId, None);
        Assert.AreEqual(2, occupied.Occupancy);
        Assert.AreEqual(0, occupied.ReservedSeats);
        await rooms.DissolveAsync(created.RoomId, new(Guid.NewGuid(), occupied.Version, true), None);
        var dissolved = await rooms.ReadAsync(created.RoomId, None);
        Assert.AreEqual("dissolved", dissolved.Status);
        Assert.AreEqual(0, dissolved.Occupancy);
        await AssertDetachedAsync(fixture, ownerEntered, visitorEntered, code.CredentialId);
        await AssertInvalidCodeAsync(guestRaw, code.Code);
        var detachedBeforeRestore = await AssignmentGraphAsync(fixture, account.Identity.PetId, guest.Identity.PetId);
        await rooms.RestoreAsync(created.RoomId, new(Guid.NewGuid(), dissolved.Version), None);
        var restored = await rooms.ReadAsync(created.RoomId, None);
        Assert.AreEqual("active", restored.Status);
        Assert.AreEqual(0, restored.Occupancy);
        Assert.AreEqual("publicShared", restored.Kind);
        Assert.AreEqual(detachedBeforeRestore, await AssignmentGraphAsync(fixture, account.Identity.PetId, guest.Identity.PetId));
        await AssertInvalidCodeAsync(guestRaw, code.Code);
        await AssertDetachedAsync(fixture, ownerEntered, visitorEntered, code.CredentialId);
        Assert.AreEqual(otherBefore, await AccountGraphAsync(fixture, untouched.Identity.UserId));
        await AssertManagementReceiptsAsync(fixture, account.Identity.UserId, 3);
        Assert.AreEqual("active", (await rooms.ReadAsync(account.DesktopRoomId, None)).Status);
    }

    private static Task<FormalTestFixture> CreateFixtureAsync() =>
        FormalTestFixture.CreateAsync(startHttp: true, requireB5BApproval: true,
            clock: new MutableTestClock(new(DateTimeOffset.UtcNow.UtcTicks / 10 * 10, TimeSpan.Zero)));

    private static ClientSession CreateClient(FormalTestFixture fixture)
    {
        var cookies = new CookieContainer();
        var transport = new ObservedTransport(fixture.CreatePinnedHandler(cookies));
        return new(FormalAccountClient.ForTesting(fixture.Endpoint, transport, cookies, new MemorySecrets()), transport);
    }

    private static HttpClient RawClient(FormalTestFixture fixture, CookieContainer cookies) =>
        new(fixture.CreatePinnedHandler(cookies)) { BaseAddress = fixture.Endpoint, Timeout = TimeSpan.FromSeconds(20) };

    private static async Task<RegisteredAccount> RegisterAsync(FormalTestFixture fixture, FormalAccountClient client)
    {
        var email = $"b5b-{Guid.NewGuid():N}@example.invalid";
        var proof = B1InputTests.NewProof();
        var invitationId = await fixture.InviteAsync(email, proof);
        var attempt = RegistrationAttempt.Create(email, invitationId, proof);
        var pending = await client.StartRegistrationAsync(attempt, None);
        Assert.AreEqual("queued", pending.EmailStatus);
        var confirmation = await fixture.RegistrationProofAsync(pending.Id);
        using (var browser = fixture.CreateClient(out _))
        using (var response = await FormalTestFixture.SendAsync(browser, HttpMethod.Post, "api/v1/auth/email-verifications/confirm", new { proof = confirmation }))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var password = B1PostgresTests.NewPassword();
        var registered = await client.CompleteRegistrationAsync(attempt, password, B1InputTests.Parse(B1InputTests.ValidAppearance), None);
        var identity = await client.LoginAsync(email, password, false, None);
        Assert.AreEqual(new AccountIdentity(registered.UserId, registered.PetId), identity);
        return new(identity, registered.RoomId, email, password);
    }

    private static async Task<Guid[]> AllPagesAsync(IRoomManagementClient client, string state, int maximum)
    {
        List<Guid> seen = [];
        Guid? after = null;
        do
        {
            var page = await client.ListAsync(state, after, 1, None);
            Assert.IsTrue(page.Rooms.Length <= 1);
            foreach (var room in page.Rooms)
            {
                Assert.IsFalse(seen.Contains(room.RoomId), "Cursor cannot repeat a room.");
                seen.Add(room.RoomId);
            }
            after = page.Next;
            if (after is not null) Assert.AreEqual(page.Rooms.Single().RoomId, after.Value);
            Assert.IsTrue(seen.Count <= maximum, "Cursor cannot loop or expose unrelated rooms.");
        } while (after is not null);
        return seen.ToArray();
    }

    private static async Task<RoomManagementClientException> FaultAsync<T>(Task<T> work, RoomManagementFailure failure, string code)
    {
        var error = await Assert.ThrowsExactlyAsync<RoomManagementClientException>(() => work);
        Assert.AreEqual(failure, error.Failure);
        Assert.AreEqual(code, error.Code);
        return error;
    }

    private static async Task UnknownAsync(Func<Task<RoomOperationResult>> work, ObservedTransport transport)
    {
        var before = transport.RoomWrites.Count;
        await FaultAsync(work(), RoomManagementFailure.Unavailable, "request_outcome_unknown");
        Assert.AreEqual(before + 1, transport.RoomWrites.Count, "Unknown outcome must not trigger an automatic mutation retry.");
        Assert.IsNull(transport.LoseNext, "The fault must discard a successful response from the real server.");
    }

    private static Task AssertManagementReceiptsAsync(FormalTestFixture fixture, Guid userId, int expected) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var audits = await db.RoomOperationAudits.AsNoTracking().Where(a => a.ActorUserId == userId && a.BoundSessionId == null).ToArrayAsync();
        Assert.AreEqual(expected, audits.Length);
        Assert.AreEqual(expected, audits.Select(a => a.RequestId).Distinct().Count());
        var ids = audits.Select(a => a.Id).ToArray();
        Assert.AreEqual(expected, await db.RoomDomainEvents.CountAsync(e => ids.Contains(e.Id) && e.PublishedAt == null));
    });

    private static Task<string> AccountIdentityAsync(FormalTestFixture fixture, Guid userId) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var pet = await db.Pets.AsNoTracking().SingleAsync(p => p.UserId == userId);
        var desktop = await db.Rooms.AsNoTracking().SingleAsync(r => r.OwnerId == userId && r.Kind == Dango.Server.Persistence.Entities.PersistentRoomKind.DesktopPet);
        var state = await db.PetRoomStates.AsNoTracking().SingleAsync(s => s.PetId == pet.Id);
        return Hash(new { pet, desktop, state });
    });

    private static Task<string> AccountGraphAsync(FormalTestFixture fixture, Guid userId) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        var sessions = await db.AuthSessions.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.Id).ToArrayAsync();
        var pet = await db.Pets.AsNoTracking().SingleAsync(p => p.UserId == userId);
        var rooms = await db.Rooms.AsNoTracking().Where(r => r.OwnerId == userId).OrderBy(r => r.Id).ToArrayAsync();
        var ids = rooms.Select(r => r.Id).ToArray();
        var memberships = await db.RoomMemberships.AsNoTracking().Where(m => m.UserId == userId || ids.Contains(m.RoomId)).OrderBy(m => m.RoomId).ThenBy(m => m.UserId).ToArrayAsync();
        var states = await db.PetRoomStates.AsNoTracking().Where(s => s.PetId == pet.Id).ToArrayAsync();
        var leases = await db.RoomDeviceLeases.AsNoTracking().Where(s => s.PetId == pet.Id).OrderBy(s => s.Id).ToArrayAsync();
        var codes = await db.PublicRoomCredentials.AsNoTracking().Where(c => ids.Contains(c.RoomId)).OrderBy(c => c.Id).ToArrayAsync();
        var receipts = await db.RoomOperationAudits.AsNoTracking().Where(a => a.ActorUserId == userId || ids.Contains(a.RoomId)).OrderBy(a => a.Id).ToArrayAsync();
        var events = await db.RoomDomainEvents.AsNoTracking().Where(e => ids.Contains(e.RoomId)).OrderBy(e => e.Id).ToArrayAsync();
        return Hash(new { user, sessions, pet, rooms, memberships, states, leases, codes, receipts, events });
    });

    private static Task<string> AssignmentGraphAsync(FormalTestFixture fixture, params Guid[] pets) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        var states = await db.PetRoomStates.AsNoTracking().Where(s => pets.Contains(s.PetId)).OrderBy(s => s.PetId).ToArrayAsync();
        var leases = await db.RoomDeviceLeases.AsNoTracking().Where(s => pets.Contains(s.PetId)).OrderBy(s => s.Id).ToArrayAsync();
        return Hash(new { states, leases });
    });

    private static Task AssertDetachedAsync(FormalTestFixture fixture, RoomAccessResult owner, RoomAccessResult visitor, Guid codeId) => fixture.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<DangoDbContext>();
        foreach (var original in new[] { owner, visitor })
        {
            var state = await db.PetRoomStates.AsNoTracking().SingleAsync(s => s.PetId == original.PetId);
            Assert.IsNull(state.RoomId);
            Assert.IsNull(state.ControlLeaseId);
            Assert.IsNull(state.PositionX);
            Assert.IsNull(state.PositionY);
            Assert.IsFalse(state.IsSleeping);
            Assert.AreEqual(original.AssignmentVersion + 1, state.Version);
            Assert.AreEqual(original.ControlGeneration + 1, state.ControlGeneration);
            var lease = await db.RoomDeviceLeases.AsNoTracking().SingleAsync(l => l.Id == original.LeaseId);
            Assert.IsNotNull(lease.RevokedAt);
            Assert.IsNull(lease.RoomId);
        }
        var credential = await db.PublicRoomCredentials.AsNoTracking().SingleAsync(c => c.Id == codeId);
        Assert.IsNotNull(credential.RevokedAt);
        Assert.AreEqual(1, await db.PublicRoomCredentials.CountAsync(c => c.RoomId == credential.RoomId));
    });

    private static async Task<RoomAccessResult> EnterAsync(HttpClient raw, Guid roomId, string? code = null)
    {
        using var snapshotResponse = await raw.GetAsync("api/v1/me/room/snapshot");
        var snapshot = await FormalTestFixture.ReadAsync<PersonalRoomSnapshot>(snapshotResponse);
        using var response = await FormalTestFixture.SendAsync(raw, HttpMethod.Post, $"api/v1/rooms/{roomId:D}/enter",
            new RoomEnterRequest(Guid.NewGuid(), Guid.NewGuid(), "desktop", snapshot.RoomId, snapshot.AssignmentVersion, code));
        return await FormalTestFixture.ReadAsync<RoomAccessResult>(response);
    }

    private static async Task AssertInvalidCodeAsync(HttpClient client, string code)
    {
        using var response = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/rooms/resolve", new RoomCodeResolveRequest(code));
        await ErrorAsync(response, HttpStatusCode.NotFound, "room_code_invalid");
    }

    private static async Task ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var json = await FormalTestFixture.ReadAsync<JsonElement>(response, status);
        Assert.AreEqual(code, json.GetProperty("code").GetString());
    }

    private static string Hash(object graph) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(graph)));
    private sealed record RegisteredAccount(AccountIdentity Identity, Guid DesktopRoomId, string Email, string Password)
    { public override string ToString() => "RegisteredAccount [redacted]"; }
    private sealed record ClientSession(FormalAccountClient Account, ObservedTransport Transport) : IDisposable
    { public void Dispose() => Account.Dispose(); }
    private sealed record ObservedWrite(Guid RequestId, string Operation, bool HasCsrf);

    private sealed class MemorySecrets : IAccountSecretStore
    {
        private readonly Dictionary<(string Origin, string Slot), byte[]> values = [];
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) =>
            Task.FromResult(values.TryGetValue((origin.AbsoluteUri, slot), out var value) ? value.ToArray() : null);
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); values[(origin.AbsoluteUri, slot)] = value.ToArray(); return Task.CompletedTask; }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        { values.Remove((origin.AbsoluteUri, slot)); return Task.CompletedTask; }
    }

    private sealed class ObservedTransport(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public List<ObservedWrite> RoomWrites { get; } = [];
        public int RequestCount { get; private set; }
        public string? LoseNext { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestCount++;
            var path = request.RequestUri!.AbsolutePath;
            var operation = path == "/api/v1/rooms" && request.Method == HttpMethod.Post ? "created"
                : path.StartsWith("/api/v1/rooms/", StringComparison.Ordinal) && request.Method == HttpMethod.Patch ? "updated"
                : path.EndsWith("/dissolve", StringComparison.Ordinal) ? "dissolved"
                : path.EndsWith("/restore", StringComparison.Ordinal) ? "restored" : null;
            if (operation is not null)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                RoomWrites.Add(new(body.RootElement.GetProperty("requestId").GetGuid(), operation, request.Headers.Contains("X-CSRF-TOKEN")));
            }
            var response = await base.SendAsync(request, ct);
            if (operation is null || operation != LoseNext) return response;
            LoseNext = null;
            var status = response.StatusCode;
            response.Dispose();
            Assert.AreEqual(operation == "created" ? HttpStatusCode.Created : HttpStatusCode.OK, status,
                "Only the real server's successful committed response may be discarded.");
            throw new HttpRequestException("fictional_room_response_loss");
        }
    }
}
