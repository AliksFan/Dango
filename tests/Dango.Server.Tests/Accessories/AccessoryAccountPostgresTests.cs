using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Client.Sync.Accessories;
using Dango.Client.Sync.Accounts;
using Dango.AccessoryAssets;
using Dango.Protocol.Accessories;
using Dango.Server.Accessories;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Tests.Accounts;
using Dango.Server.Tests.Pets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Dango.Server.Tests.Accessories;

// Prepared gates only. The fixture checks the independent C2 approval before any DB/port/key operation.
[TestClass]
[TestCategory("PostgresIntegration")]
[TestCategory("AccessoryC2Integration")]
public sealed class AccessoryAccountPostgresTests
{
    [TestMethod]
    public async Task SameRealImageNeedsAnIndependentUploadForEveryAccountAndHasOneGlobalBlob()
    {
        await using var fixture = await FixtureAsync();
        var a = await PetTestSupport.RegisterAsync(fixture); var b = await PetTestSupport.RegisterAsync(fixture);
        var source = Png();
        var uploaded = await UploadAsync(fixture, a, source);
        await FaultAsync("accessory_asset_unavailable", () => fixture.InScopeAsync(s => Service(s).DownloadAsync(b.UserId, b.SessionId, uploaded.AssetId, default)));
        await FaultAsync("accessory_asset_unavailable", () => CommitAsync(fixture, b, Request(0, uploaded)));
        var second = await UploadAsync(fixture, b, source);
        Assert.AreEqual(uploaded.AssetId, second.AssetId);
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1, await db.AccessoryBlobs.CountAsync(x => x.AssetId == uploaded.AssetId));
            Assert.AreEqual(1, await db.AccessoryAssetReferences.CountAsync(x => x.PetId == a.PetId && x.AssetId == uploaded.AssetId));
            Assert.AreEqual(1, await db.AccessoryAssetReferences.CountAsync(x => x.PetId == b.PetId && x.AssetId == uploaded.AssetId));
            var image = await s.GetRequiredService<AccessoryStorage>().RequireStore().ReadAsync(uploaded.AssetId);
            CollectionAssert.AreEqual(image.CopyPngBytes(), await Service(s).DownloadAsync(b.UserId, b.SessionId, uploaded.AssetId, default));
        });
    }

    [TestMethod]
    public async Task ReceiptReplaysOriginalRecipeAfterLaterRevisionAndResolvesLostResponse()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var upload = await UploadAsync(fixture, account, Png()); var request = Request(0, upload);
        var first = await CommitAsync(fixture, account, request);
        Assert.AreEqual(1L, first.Revision);
        var second = await CommitAsync(fixture, account, new(1, Key(), AccessoryOutfitManifest.Empty));
        Assert.AreEqual(2L, second.Revision);
        Assert.AreEqual(first, await CommitAsync(fixture, account, request));
        var resolved = await fixture.InScopeAsync(s => Service(s).ResolveAsync(account.UserId, account.SessionId, request.IdempotencyKey, default));
        Assert.AreEqual("committed", resolved.Status); Assert.AreEqual(first, resolved.StoredOutfit);
        await FaultAsync("accessory_idempotency_conflict", () => CommitAsync(fixture, account, request with { ExpectedRevision = 2, Outfit = AccessoryOutfitManifest.Empty }));
        await FaultAsync("accessory_revision_conflict", () => CommitAsync(fixture, account, new(0, Key(), AccessoryOutfitManifest.Empty)));
        var missing = await fixture.InScopeAsync(s => Service(s).ResolveAsync(account.UserId, account.SessionId, Key(), default));
        Assert.AreEqual("unknown", missing.Status); Assert.IsNull(missing.StoredOutfit);
        Assert.AreEqual(second, await fixture.InScopeAsync(s => Service(s).GetAsync(account.UserId, account.SessionId, default)));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IndependentPhysicalConnectionsSerializeVersionAndIdempotencyRaces(bool sameKey)
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var ids = new ConcurrentBag<int>(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var arrived = 0;
        var one = new CommitAccessoryOutfitRequest(0, Key(), AccessoryOutfitManifest.Empty);
        var two = sameKey ? one : new(0, Key(), new(1, [new("builtin", "sprout", "builtin-sprout", 64, 64)]));
        async Task<(StoredAccessoryOutfit? Stored, string? Error)> Compete(CommitAccessoryOutfitRequest request) => await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>(); await db.Database.OpenConnectionAsync();
            ids.Add(((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID);
            if (Interlocked.Increment(ref arrived) == 2) release.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { return (await Service(s).CommitAsync(account.UserId, account.SessionId, request, default), (string?)null); }
            catch (AccountFault error) { return ((StoredAccessoryOutfit?)null, error.Code); }
        });
        var results = await Task.WhenAll(Compete(one), Compete(two)); Assert.AreEqual(2, ids.Distinct().Count());
        Assert.AreEqual(sameKey ? 2 : 1, results.Count(r => r.Stored is not null));
        if (sameKey) Assert.AreEqual(results[0].Stored, results[1].Stored);
        else Assert.AreEqual("accessory_revision_conflict", results.Single(r => r.Error is not null).Error);
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(1L, (await db.AccessoryCurrentOutfits.SingleAsync(o => o.PetId == account.PetId)).Revision);
            Assert.AreEqual(1, await db.AccessoryCommitReceipts.CountAsync(o => o.PetId == account.PetId));
        });
    }

    [TestMethod]
    [DataRow("after_outfit_and_receipt")]
    [DataRow("before_commit")]
    public async Task FailureRollsBackOutfitReferenceConversionAndReceiptTogether(string point)
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var upload = await UploadAsync(fixture, account, Png()); var request = Request(0, upload);
        await Assert.ThrowsExactlyAsync<InjectedFailure>(() => fixture.InScopeAsync(s => Service(s, new Failure(point))
            .CommitAsync(account.UserId, account.SessionId, request, default)));
        await AssertNoOutfitAsync(fixture, account);
        await fixture.InScopeAsync(async s =>
        {
            var reference = await s.GetRequiredService<DangoDbContext>().AccessoryAssetReferences.SingleAsync(r => r.PetId == account.PetId);
            Assert.IsFalse(reference.IsCurrent); Assert.IsTrue(reference.LeaseUntil > DateTimeOffset.UtcNow);
        });
        Assert.AreEqual(1L, (await CommitAsync(fixture, account, request)).Revision);
    }

    [TestMethod]
    public async Task UploadRollbackDoesNotGrantAccessEvenWhenPhysicalFileWasWritten()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var source = Png(); var normalized = AccessoryPngNormalizer.Normalize(source);
        await Assert.ThrowsExactlyAsync<InjectedFailure>(() => fixture.InScopeAsync(s => Service(s, new Failure("after_reference"))
            .UploadAsync(account.UserId, account.SessionId, new MemoryStream(source), Digest(source), default)));
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>();
            Assert.AreEqual(0, await db.AccessoryAssetReferences.CountAsync(r => r.PetId == account.PetId));
            Assert.AreEqual(0, await db.AccessoryBlobs.CountAsync(b => b.AssetId == normalized.Digest));
            Assert.AreEqual(normalized.Digest, (await s.GetRequiredService<AccessoryStorage>().RequireStore().ReadAsync(normalized.Digest)).Digest);
        });
        await FaultAsync("accessory_asset_unavailable", () => fixture.InScopeAsync(s => Service(s).DownloadAsync(account.UserId, account.SessionId, normalized.Digest, default)));
        await UploadAsync(fixture, account, source);
    }

    [TestMethod]
    public async Task ExpiryAtPrecommitRollsBackThePersistedWrite()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FixtureAsync(clock); var account = await PetTestSupport.RegisterAsync(fixture);
        var upload = await UploadAsync(fixture, account, Png());
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s, new Advance(clock, "before_commit", TimeSpan.FromHours(8)))
            .CommitAsync(account.UserId, account.SessionId, Request(0, upload), default)));
        await AssertNoOutfitAsync(fixture, account);
        await fixture.InScopeAsync(async s => Assert.IsFalse((await s.GetRequiredService<DangoDbContext>().AccessoryAssetReferences.SingleAsync(r => r.PetId == account.PetId)).IsCurrent));
    }

    [TestMethod]
    public async Task StagingLeaseExpiringAtPrecommitCannotBePromotedToPermanentCurrentAccess()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FixtureAsync(clock); var account = await PetTestSupport.RegisterAsync(fixture);
        account = await RememberAsync(fixture, account);
        var upload = await UploadAsync(fixture, account, Png());
        await FaultAsync("accessory_asset_unavailable", () => fixture.InScopeAsync(s => Service(s, new Advance(clock, "before_commit", TimeSpan.FromHours(25)))
            .CommitAsync(account.UserId, account.SessionId, Request(0, upload), default)));
        await AssertNoOutfitAsync(fixture, account);
        await fixture.InScopeAsync(async s => Assert.IsFalse((await s.GetRequiredService<DangoDbContext>().AccessoryAssetReferences.SingleAsync(r => r.PetId == account.PetId)).IsCurrent));
    }

    [TestMethod]
    public async Task StagingQuotaIsCheckedBeforeCreatingAnotherPhysicalFileAndSameImageRenewsItsLease()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var firstSource = Png(); var first = await UploadAsync(fixture, account, firstSource);
        for (var i = 1; i < AccessoryAccountService.MaximumStagingAssets; i++) await UploadAsync(fixture, account, Png());
        Assert.AreEqual(first.AssetId, (await UploadAsync(fixture, account, firstSource)).AssetId);
        var rejectedSource = Png(); var rejected = AccessoryPngNormalizer.Normalize(rejectedSource);
        var root = Environment.GetEnvironmentVariable("DANGO_BACKEND_C2_BLOB_DIRECTORY")!;
        var path = Path.Combine(root, rejected.Digest[..2], rejected.Digest + ".png");
        Assert.IsFalse(File.Exists(path));
        await FaultAsync("accessory_staging_limit", () => UploadAsync(fixture, account, rejectedSource)); Assert.IsFalse(File.Exists(path));
        await fixture.InScopeAsync(async s => Assert.AreEqual(16, await s.GetRequiredService<DangoDbContext>().AccessoryAssetReferences.CountAsync(r => r.PetId == account.PetId)));
    }

    [TestMethod]
    public async Task CurrentAssetsSurviveLeaseExpiryAndReplacingTheOutfitEndsOldCurrentAccess()
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FixtureAsync(clock); var account = await PetTestSupport.RegisterAsync(fixture);
        account = await RememberAsync(fixture, account);
        var current = await UploadAsync(fixture, account, Png()); var staged = await UploadAsync(fixture, account, Png());
        await CommitAsync(fixture, account, Request(0, current));
        clock.Advance(TimeSpan.FromHours(25));
        await fixture.InScopeAsync(s => Service(s).DownloadAsync(account.UserId, account.SessionId, current.AssetId, default));
        await FaultAsync("accessory_asset_unavailable", () => fixture.InScopeAsync(s => Service(s).DownloadAsync(account.UserId, account.SessionId, staged.AssetId, default)));
        await FaultAsync("accessory_asset_unavailable", () => CommitAsync(fixture, account, Request(1, staged)));
        await CommitAsync(fixture, account, new(1, Key(), AccessoryOutfitManifest.Empty));
        await FaultAsync("accessory_asset_unavailable", () => fixture.InScopeAsync(s => Service(s).DownloadAsync(account.UserId, account.SessionId, current.AssetId, default)));
        // Reupload creates a new finite grant without replacing the existing current recipe.
        await UploadAsync(fixture, account, Png());
    }

    [TestMethod]
    [DataRow("revoked")]
    [DataRow("expired")]
    [DataRow("disabled")]
    [DataRow("recovery_locked")]
    [DataRow("temporary_lock")]
    [DataRow("security_stamp")]
    [DataRow("recovery_epoch")]
    public async Task EveryOperationRechecksCurrentSessionStateAndDeniedUploadDoesNotReadTheBody(string kind)
    {
        var clock = new MutableTestClock(DateTimeOffset.UtcNow);
        await using var fixture = await FixtureAsync(clock); var account = await PetTestSupport.RegisterAsync(fixture);
        var uploaded = await UploadAsync(fixture, account, Png()); var request = Request(0, uploaded); await CommitAsync(fixture, account, request);
        await fixture.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<DangoDbContext>(); var user = await db.Users.SingleAsync(u => u.Id == account.UserId);
            var session = await db.AuthSessions.SingleAsync(x => x.Id == account.SessionId);
            switch (kind)
            {
                case "revoked": session.RevokedAt = clock.GetUtcNow(); break;
                case "expired": clock.Advance(TimeSpan.FromHours(8)); break;
                case "disabled": user.DisabledAt = clock.GetUtcNow(); break;
                case "recovery_locked": user.RecoveryLocked = true; break;
                case "temporary_lock": user.LockoutEnd = clock.GetUtcNow().AddMinutes(15); break;
                case "security_stamp": user.SecurityStamp = Key(); break;
                case "recovery_epoch": session.RecoveryEpoch = Guid.NewGuid(); break;
            }
            await db.SaveChangesAsync();
        });
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s).GetAsync(account.UserId, account.SessionId, default)));
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s).ResolveAsync(account.UserId, account.SessionId, request.IdempotencyKey, default)));
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s).DownloadAsync(account.UserId, account.SessionId, uploaded.AssetId, default)));
        await FaultAsync("authentication_required", () => CommitAsync(fixture, account, request));
        var stream = new UnreadableStream();
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s).UploadAsync(account.UserId, account.SessionId, stream, new string('a', 64), default)));
        Assert.IsFalse(stream.ReadAttempted);
    }

    [TestMethod]
    public async Task RevocationBetweenPreflightAndWriteLocksRejectsTheCommit()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var checkpoint = new Callback("before_locks", () => fixture.InScopeAsync(s => s.GetRequiredService<SessionService>().RevokeAsync(account.UserId, account.SessionId, default)));
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s, checkpoint)
            .CommitAsync(account.UserId, account.SessionId, new(0, Key(), AccessoryOutfitManifest.Empty), default)));
        await AssertNoOutfitAsync(fixture, account);
    }

    [TestMethod]
    public async Task AnotherAccountsSessionCannotReadWriteOrResolveTheOwnersOutfit()
    {
        await using var fixture = await FixtureAsync(); var a = await PetTestSupport.RegisterAsync(fixture); var b = await PetTestSupport.RegisterAsync(fixture);
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s).GetAsync(a.UserId, b.SessionId, default)));
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s).CommitAsync(a.UserId, b.SessionId, new(0, Key(), AccessoryOutfitManifest.Empty), default)));
        await FaultAsync("authentication_required", () => fixture.InScopeAsync(s => Service(s).ResolveAsync(a.UserId, b.SessionId, Key(), default)));
        await AssertNoOutfitAsync(fixture, a);
    }

    [TestMethod]
    public async Task BadSourceDigestAndInvalidPngNeverGrantAReference()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture); var png = Png();
        await FaultAsync("accessory_image_invalid", () => fixture.InScopeAsync(s => Service(s).UploadAsync(account.UserId, account.SessionId, new MemoryStream(png), new string('a', 64), default)));
        var bad = Encoding.UTF8.GetBytes("fictional invalid PNG");
        await FaultAsync("accessory_image_invalid", () => UploadAsync(fixture, account, bad));
        await fixture.InScopeAsync(async s => Assert.AreEqual(0, await s.GetRequiredService<DangoDbContext>().AccessoryAssetReferences.CountAsync(x => x.PetId == account.PetId)));
    }

    [TestMethod]
    public async Task CorruptRecipeCannotBeReturnedOrSilentlyRepaired()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        await CommitAsync(fixture, account, new(0, Key(), AccessoryOutfitManifest.Empty));
        await fixture.InScopeAsync(s => s.GetRequiredService<DangoDbContext>().AccessoryCurrentOutfits.Where(x => x.PetId == account.PetId)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.OutfitJson, "{\"schemaVersion\":1,\"items\":[{}]}")));
        await FaultAsync("accessory_invalid_state", () => fixture.InScopeAsync(s => Service(s).GetAsync(account.UserId, account.SessionId, default)));
        await FaultAsync("accessory_invalid_state", () => CommitAsync(fixture, account, new(1, Key(), AccessoryOutfitManifest.Empty)));
    }

    [TestMethod]
    public async Task ValidButChangedReceiptJsonCannotMasqueradeAsTheCommittedResult()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var request = new CommitAccessoryOutfitRequest(0, Key(), new(1, [new("one", "flower", "builtin-sprout", 64, 64)]));
        await CommitAsync(fixture, account, request);
        await fixture.InScopeAsync(s => s.GetRequiredService<DangoDbContext>().AccessoryCommitReceipts.Where(x => x.PetId == account.PetId && x.IdempotencyKey == request.IdempotencyKey)
            .ExecuteUpdateAsync(set => set.SetProperty(x => x.OutfitJson, AccessoryPersistence.Serialize(AccessoryOutfitManifest.Empty))));
        await FaultAsync("accessory_invalid_state", () => fixture.InScopeAsync(s => Service(s).ResolveAsync(account.UserId, account.SessionId, request.IdempotencyKey, default)));
        await FaultAsync("accessory_invalid_state", () => CommitAsync(fixture, account, request));
    }

    [TestMethod]
    public async Task ReadingDuringReplacementKeepsRecipeAndCurrentReferencesConsistent()
    {
        await using var fixture = await FixtureAsync(); var account = await PetTestSupport.RegisterAsync(fixture);
        var uploaded = await UploadAsync(fixture, account, Png()); var original = await CommitAsync(fixture, account, Request(0, uploaded));
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = fixture.InScopeAsync(s => Service(s, new Callback("after_read", async () => { reading.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); }))
            .GetAsync(account.UserId, account.SessionId, default));
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var replace = fixture.InScopeAsync(s => Service(s, new Callback("before_locks", () => { replacing.SetResult(); return Task.CompletedTask; }))
            .CommitAsync(account.UserId, account.SessionId, new(1, Key(), AccessoryOutfitManifest.Empty), default));
        await replacing.Task.WaitAsync(TimeSpan.FromSeconds(10)); release.SetResult();
        Assert.AreEqual(original, await read); var changed = await replace;
        Assert.AreEqual(changed, await fixture.InScopeAsync(s => Service(s).GetAsync(account.UserId, account.SessionId, default)));
    }

    [TestMethod]
    public async Task ANewHostAndDbContextReadTheDurableRecipeAndReceipt()
    {
        PetTestSupport.Account account; StoredAccessoryOutfit committed; CommitAccessoryOutfitRequest request;
        CookieContainer cookies;
        await using (var first = await FormalTestFixture.CreateAsync(startHttp: true, requireC2Approval: true))
        {
            account = await PetTestSupport.RegisterAsync(first); var upload = await UploadAsync(first, account, Png()); request = Request(0, upload);
            committed = await CommitAsync(first, account, request);
            using var client = first.CreateClient(out cookies);
            using var login = await FormalTestFixture.SendAsync(client, HttpMethod.Post, "api/v1/auth/login", new { account.Email, account.Password, remembered = false });
            Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        }
        await using var second = await FormalTestFixture.CreateAsync(startHttp: true, requireC2Approval: true);
        using var restoredClient = new HttpClient(second.CreatePinnedHandler(cookies)) { BaseAddress = second.Endpoint, Timeout = TimeSpan.FromSeconds(20) };
        using var currentResponse = await restoredClient.GetAsync("api/v1/me/pet/accessories"); Assert.AreEqual(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.AreEqual(committed, JsonSerializer.Deserialize<StoredAccessoryOutfit>(await currentResponse.Content.ReadAsByteArrayAsync(), AccessoryPersistence.Json));
        using var receiptResponse = await restoredClient.GetAsync($"api/v1/me/pet/accessories/commits/{request.IdempotencyKey}"); Assert.AreEqual(HttpStatusCode.OK, receiptResponse.StatusCode);
        Assert.AreEqual(committed, JsonSerializer.Deserialize<AccessoryCommitStatus>(await receiptResponse.Content.ReadAsByteArrayAsync(), AccessoryPersistence.Json)!.StoredOutfit);
    }

    [TestMethod]
    public async Task FormalHttpsUsesTheRealCookieCsrfAndPrivateImageRoutes()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, requireC2Approval: true);
        var a = await PetTestSupport.RegisterAsync(fixture); var b = await PetTestSupport.RegisterAsync(fixture);
        using var owner = fixture.CreateClient(out _); using var other = fixture.CreateClient(out _);
        using var loginA = await FormalTestFixture.SendAsync(owner, HttpMethod.Post, "api/v1/auth/login", new { a.Email, a.Password, remembered = false }); Assert.AreEqual(HttpStatusCode.OK, loginA.StatusCode);
        using var loginB = await FormalTestFixture.SendAsync(other, HttpMethod.Post, "api/v1/auth/login", new { b.Email, b.Password, remembered = false }); Assert.AreEqual(HttpStatusCode.OK, loginB.StatusCode);
        using var empty = await owner.GetAsync("api/v1/me/pet/accessories"); Assert.AreEqual(HttpStatusCode.NoContent, empty.StatusCode);
        var source = Png(64); Assert.IsTrue(source.Length > 8192, "The real PNG endpoint must raise only its own default 8 KiB body budget.");
        using var noCsrf = await SendPngAsync(owner, source, csrf: false); Assert.AreEqual(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        using var uploadedResponse = await SendPngAsync(owner, source);
        Assert.AreEqual(HttpStatusCode.OK, uploadedResponse.StatusCode);
        var uploaded = JsonSerializer.Deserialize<UploadedAccessoryAsset>(await uploadedResponse.Content.ReadAsByteArrayAsync(), AccessoryPersistence.Json)!; uploaded.Validate();
        using var denied = await other.GetAsync($"api/v1/me/pet/accessory-assets/{uploaded.AssetId}"); Assert.AreEqual(HttpStatusCode.NotFound, denied.StatusCode);
        using var downloaded = await owner.GetAsync($"api/v1/me/pet/accessory-assets/{uploaded.AssetId}"); Assert.AreEqual("image/png", downloaded.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual(uploaded.AssetId, Digest(await downloaded.Content.ReadAsByteArrayAsync()));
        var request = Request(0, uploaded);
        using var commit = await SendJsonAsync(owner, JsonSerializer.Serialize(request, AccessoryPersistence.Json)); Assert.AreEqual(HttpStatusCode.OK, commit.StatusCode);
        using var receipt = await owner.GetAsync($"api/v1/me/pet/accessories/commits/{request.IdempotencyKey}");
        Assert.AreEqual(HttpStatusCode.OK, receipt.StatusCode);
        var resolved = JsonSerializer.Deserialize<AccessoryCommitStatus>(await receipt.Content.ReadAsByteArrayAsync(), AccessoryPersistence.Json)!;
        Assert.AreEqual("committed", resolved.Status); Assert.AreEqual(1L, resolved.StoredOutfit!.Revision);
        using var malformed = await SendJsonAsync(owner, JsonSerializer.Serialize(request with { IdempotencyKey = Key(), ExpectedRevision = 1 }, AccessoryPersistence.Json)
            .Replace("\"softness\":12", "\"softness\":12,\"softness\":12", StringComparison.Ordinal)); Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [TestMethod]
    public async Task FormalAccountClientUsesActualHttpsPngCommitAndResolvesACommittedLostResponse()
    {
        await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, requireC2Approval: true);
        var account = await PetTestSupport.RegisterAsync(fixture); var cookies = new CookieContainer();
        using var secrets = new MemorySecrets();
        var handler = new DropAccessoryCommit(fixture.CreatePinnedHandler(cookies));
        using var client = FormalAccountClient.ForTesting(fixture.Endpoint, handler, cookies, secrets);
        var identity = await client.LoginAsync(account.Email, account.Password, false);
        var scope = new AccessoryAccountScope(identity.UserId.ToString("D"), identity.PetId.ToString("D"), 1);
        var transport = client.CreateAccessoryTransport(scope);
        Assert.IsNull(await transport.GetCurrentOutfitAsync(scope, default));
        var source = Png();
        var upload = await transport.UploadAssetAsync(scope, new(Digest(source), 4, 4, source), default);
        var request = Request(0, upload);
        // Cover real jsonb signed-zero normalization as well as the pure normalization regression.
        request = request with { Outfit = new(1, [request.Outfit.Items[0] with { BaseAngleDegrees = -0d }]) };
        handler.DropNextSuccessfulPut = true;
        var lost = await Assert.ThrowsExactlyAsync<AccountClientException>(() => transport.CommitOutfitAsync(scope, request, default));
        Assert.AreEqual(AccountFailure.Unavailable, lost.Failure); Assert.AreEqual("accessory_outcome_unknown", lost.Code);
        var resolved = await transport.ResolveCommitAsync(scope, request.IdempotencyKey, default);
        Assert.AreEqual(AccessoryCommitResolutionKind.Committed, resolved.Kind); Assert.AreEqual(1L, resolved.StoredOutfit!.Revision);
        Assert.AreEqual(request.Outfit, resolved.StoredOutfit.Outfit);
        Assert.AreEqual(resolved.StoredOutfit, await transport.CommitOutfitAsync(scope, request, default));
        Assert.AreEqual(resolved.StoredOutfit, await transport.GetCurrentOutfitAsync(scope, default));
        var downloaded = await transport.DownloadAssetAsync(scope, upload.AssetId, default);
        Assert.AreEqual(upload.AssetId, downloaded.AssetId); Assert.AreEqual(upload.AssetId, Digest(downloaded.PngBytes.ToArray()));
        Assert.IsTrue(handler.MutationCsrfChecks.Count >= 4 && handler.MutationCsrfChecks.All(valid => valid));
        await fixture.InScopeAsync(async s => Assert.AreEqual(1, await s.GetRequiredService<DangoDbContext>().AccessoryCommitReceipts.CountAsync(x => x.PetId == account.PetId)));
    }

    private static Task<FormalTestFixture> FixtureAsync(TimeProvider? clock = null) => FormalTestFixture.CreateAsync(clock: clock, requireC2Approval: true);
    private static AccessoryAccountService Service(IServiceProvider services, IAccessoryCheckpoint? checkpoint = null) => new(
        services.GetRequiredService<DangoDbContext>(), services.GetRequiredService<AccountEnvironment>(), services.GetRequiredService<TimeProvider>(),
        services.GetRequiredService<AccessoryStorage>(), services.GetRequiredService<AccessoryUploadGate>(), checkpoint ?? new AccessoryCheckpoint());
    private static CommitAccessoryOutfitRequest Request(long revision, UploadedAccessoryAsset image) => new(revision, Key(), new(1,
        [new("one", "fictional flower", image.AssetId, image.PixelWidth, image.PixelHeight) { Softness = 12, AllowFolding = true, BodyAnchorX = .3 }]));
    private static Task<StoredAccessoryOutfit> CommitAsync(FormalTestFixture fixture, PetTestSupport.Account account, CommitAccessoryOutfitRequest request) =>
        fixture.InScopeAsync(s => Service(s).CommitAsync(account.UserId, account.SessionId, request, default));
    private static Task<UploadedAccessoryAsset> UploadAsync(FormalTestFixture fixture, PetTestSupport.Account account, byte[] png) =>
        fixture.InScopeAsync(s => Service(s).UploadAsync(account.UserId, account.SessionId, new MemoryStream(png, false), Digest(png), default));
    private static string Key() => Guid.NewGuid().ToString("N");
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task<PetTestSupport.Account> RememberAsync(FormalTestFixture fixture, PetTestSupport.Account account)
    {
        var login = await fixture.InScopeAsync(s => s.GetRequiredService<SessionService>().LoginAsync(account.Email, account.Password, true, default));
        return account with { SessionId = login.SessionId };
    }
    private static byte[] Png(int edge = 4)
    {
        var pixels = RandomNumberGenerator.GetBytes(edge * edge * 4);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        pixels[3] = 0;
        using var stream = new MemoryStream();
        new StbImageWriteSharp.ImageWriter().WritePng(pixels, edge, edge, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
        return stream.ToArray();
    }
    private static Task FaultAsync(string code, Func<Task> work) => PetTestSupport.FaultAsync(code, work);
    private static Task AssertNoOutfitAsync(FormalTestFixture fixture, PetTestSupport.Account account) => fixture.InScopeAsync(async s =>
    {
        var db = s.GetRequiredService<DangoDbContext>(); Assert.AreEqual(0, await db.AccessoryCurrentOutfits.CountAsync(o => o.PetId == account.PetId));
        Assert.AreEqual(0, await db.AccessoryCommitReceipts.CountAsync(o => o.PetId == account.PetId));
    });
    private static async Task<HttpResponseMessage> SendPngAsync(HttpClient client, byte[] png, bool csrf = true)
    {
        var token = csrf ? await FormalTestFixture.CsrfAsync(client) : null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/me/pet/accessory-assets") { Content = new ByteArrayContent(png) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png"); request.Headers.Add("X-Accessory-Source-Digest", Digest(png));
        if (token is not null) request.Headers.Add("X-CSRF-TOKEN", token); return await client.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, string json)
    {
        var token = await FormalTestFixture.CsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1/me/pet/accessories") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-CSRF-TOKEN", token); return await client.SendAsync(request);
    }
    private sealed class InjectedFailure : Exception;
    private sealed class Failure(string point) : IAccessoryCheckpoint
    { public Task ReachedAsync(string name, CancellationToken ct) => name == point ? throw new InjectedFailure() : Task.CompletedTask; }
    private sealed class Advance(MutableTestClock clock, string point, TimeSpan elapsed) : IAccessoryCheckpoint
    { public Task ReachedAsync(string name, CancellationToken ct) { if (name == point) clock.Advance(elapsed); return Task.CompletedTask; } }
    private sealed class Callback(string point, Func<Task> action) : IAccessoryCheckpoint
    { public Task ReachedAsync(string name, CancellationToken ct) => name == point ? action() : Task.CompletedTask; }
    private sealed class UnreadableStream : MemoryStream
    {
        public bool ReadAttempted { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { ReadAttempted = true; throw new InvalidOperationException("An unauthorized image must never be read."); }
    }
    private sealed class MemorySecrets : IAccountSecretStore, IDisposable
    {
        private readonly Dictionary<(string Origin, string Slot), byte[]> values = [];
        public Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct) =>
            Task.FromResult(values.TryGetValue((origin.AbsoluteUri, slot), out var bytes) ? bytes.ToArray() : null);
        public Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (values.Remove((origin.AbsoluteUri, slot), out var previous)) CryptographicOperations.ZeroMemory(previous);
            values[(origin.AbsoluteUri, slot)] = value.ToArray(); return Task.CompletedTask;
        }
        public Task DeleteAsync(Uri origin, string slot, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (values.Remove((origin.AbsoluteUri, slot), out var previous)) CryptographicOperations.ZeroMemory(previous);
            return Task.CompletedTask;
        }
        public void Dispose() { foreach (var bytes in values.Values) CryptographicOperations.ZeroMemory(bytes); values.Clear(); }
    }
    private sealed class DropAccessoryCommit(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public bool DropNextSuccessfulPut { get; set; }
        public List<bool> MutationCsrfChecks { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) MutationCsrfChecks.Add(request.Headers.Contains("X-CSRF-TOKEN"));
            var response = await base.SendAsync(request, ct);
            if (DropNextSuccessfulPut && request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/v1/me/pet/accessories")
            {
                DropNextSuccessfulPut = false;
                Assert.IsTrue(response.IsSuccessStatusCode, "Drop only the response of an actual committed server transaction.");
                await response.Content.ReadAsByteArrayAsync(ct); response.Dispose();
                throw new HttpRequestException("fictional_accessory_response_loss");
            }
            return response;
        }
    }
}
