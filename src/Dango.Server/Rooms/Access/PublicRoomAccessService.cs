using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Dango.Server.Rooms.Access;

public sealed record PublicRoomModerationResult(Guid RoomId, Guid TargetUserId, long RoomVersion, string Operation);

public sealed class PublicRoomAccessService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
    IRoomAccessCheckpoint checkpoint, IPublicRoomCodeProtector protector)
{
    public async Task<RoomDoorView> ResolveAsync(Guid userId, Guid sessionId, RoomCodeResolveRequest request, CancellationToken ct)
    {
        var digest = PublicRoomAccessPolicy.CodeDigest(request.Code);
        var candidate = await db.PublicRoomCredentials.AsNoTracking().Where(code => code.CodeDigest == digest)
            .Select(code => (Guid?)code.RoomId).SingleOrDefaultAsync(ct);
        return await RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, candidate, null,
            async context =>
            {
                var resolved = await db.PublicRoomCredentials.AsNoTracking().SingleOrDefaultAsync(code => code.CodeDigest == digest, ct);
                if (resolved is null || !context.Rooms.TryGetValue(resolved.RoomId, out var room)
                    || !PublicRoomAccessPolicy.CredentialValid(room, resolved, environment.RecoveryEpoch, clock.GetUtcNow())) throw InvalidCode();
                if (await db.RoomBans.AnyAsync(ban => ban.RoomId == room.Id && ban.UserId == userId && ban.Active, ct))
                    throw new AccountFault("room_banned", 403);
                // A current-epoch ciphertext is also checked; a valid digest never repairs damaged credentials.
                if (PublicRoomAccessPolicy.CodeDigest(protector.Unprotect(resolved.ProtectedCode, resolved.Id, room.Id, resolved.Generation)) != digest)
                    throw new AccountFault("room_code_invalid_state", 503);
                context.RequiredCredentialAtCommit = resolved.Id;
                return PublicRoomAccessPolicy.Door(room);
            }, ct);
    }

    public Task<RoomCodeResult> ResetCodeAsync(Guid userId, Guid sessionId, Guid roomId, RoomCodeResetRequest request, CancellationToken ct)
    {
        PublicRoomAccessPolicy.Request(request.RequestId);
        var hash = RoomAccessPersistence.Hash(new { operation = "code_reset", roomId, request.ExpectedVersion, request.LifetimeHours });
        return RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, roomId, null,
            async context =>
            {
                var original = await RoomAccessPersistence.ReplayAsync(db, context, request.RequestId, hash, ct, bindSession: false);
                var room = context.Room(roomId);
                PublicRoomAccessPolicy.OwnedPublic(room, userId, requireActive: original is null);
                if (original is not null) return await ReplayCodeAsync(original, room, ct);
                PublicRoomAccessPolicy.Lifetime(request.LifetimeHours);
                PublicRoomAccessPolicy.ExpectedVersion(room, request.ExpectedVersion);
                if (room.AdmissionGeneration == long.MaxValue) throw new AccountFault("room_admission_generation_exhausted", 409);
                Guid[] targets = [userId];
                var now = PublicRoomAccessPolicy.CredentialTime(clock.GetUtcNow());
                // Expired, non-revoked credentials still occupy the partial unique index.
                await db.PublicRoomCredentials.Where(code => code.RoomId == roomId && code.RevokedAt == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(code => code.RevokedAt, now), ct);
                await checkpoint.ReachedAsync("public_after_revoke", context.Pet.Id, ct);
                room.AdmissionGeneration++;
                RoomAccessPersistence.AdvanceRoom(room);
                var code = PublicRoomAccessPolicy.NewCode();
                var credential = new PublicRoomCredential
                {
                    Id = Guid.NewGuid(), RoomId = room.Id, Generation = room.AdmissionGeneration,
                    RecoveryEpoch = environment.RecoveryEpoch, CodeDigest = PublicRoomAccessPolicy.CodeDigest(code),
                    CreatedAt = now, ExpiresAt = now.AddHours(request.LifetimeHours)
                };
                credential.ProtectedCode = protector.Protect(code, credential.Id, room.Id, credential.Generation);
                db.PublicRoomCredentials.Add(credential);
                context.RequiredCredentialAtCommit = credential.Id;
                await db.SaveChangesAsync(ct);
                await checkpoint.ReachedAsync("public_after_code", context.Pet.Id, ct);
                var receipt = PublicRoomAccessPolicy.Receipt(credential, room.Version);
                RoomAccessPersistence.Append(db, room, "code_reset", now, targets, context, request.RequestId, hash,
                    RoomAccessPersistence.Serialize(receipt));
                await db.SaveChangesAsync(ct);
                await checkpoint.ReachedAsync("public_after_receipt", context.Pet.Id, ct);
                return new RoomCodeResult(room.Id, credential.Id, room.Version, credential.Generation, code, credential.ExpiresAt);
            }, ct);
    }

    public Task<PublicRoomModerationResult> RemoveAsync(Guid userId, Guid sessionId, Guid roomId, Guid targetUserId,
        RoomModerationRequest request, CancellationToken ct) => ModerateAsync(userId, sessionId, roomId, targetUserId, request, "removed", ct);

    public Task<PublicRoomModerationResult> BanAsync(Guid userId, Guid sessionId, Guid roomId, Guid targetUserId,
        RoomModerationRequest request, CancellationToken ct) => ModerateAsync(userId, sessionId, roomId, targetUserId, request, "ban_set", ct);

    public Task<PublicRoomModerationResult> UnbanAsync(Guid userId, Guid sessionId, Guid roomId, Guid targetUserId,
        RoomModerationRequest request, CancellationToken ct) => ModerateAsync(userId, sessionId, roomId, targetUserId, request, "ban_removed", ct);

    private async Task<PublicRoomModerationResult> ModerateAsync(Guid userId, Guid sessionId, Guid roomId, Guid targetUserId,
        RoomModerationRequest request, string operation, CancellationToken ct)
    {
        PublicRoomAccessPolicy.Request(request.RequestId);
        var hash = RoomAccessPersistence.Hash(new { operation, roomId, targetUserId, request.ExpectedVersion });
        var targetPetId = await db.Pets.AsNoTracking().Where(pet => pet.UserId == targetUserId)
            .Select(pet => (Guid?)pet.Id).SingleOrDefaultAsync(ct);
        return await RoomAccessPersistence.ActorAsync(db, environment, clock, checkpoint, userId, sessionId, roomId, targetPetId,
            async context =>
            {
                var original = await RoomAccessPersistence.ReplayAsync(db, context, request.RequestId, hash, ct, bindSession: false);
                var room = context.Room(roomId);
                PublicRoomAccessPolicy.OwnedPublic(room, userId, requireActive: original is null);
                if (original is not null)
                {
                    var receipt = RoomAccessPersistence.Deserialize<PublicRoomModerationResult>(original.ResultJson);
                    if (original.RoomId != roomId || original.Operation != operation || receipt.RoomId != roomId
                        || receipt.TargetUserId != targetUserId || receipt.Operation != operation || receipt.RoomVersion != original.AppliedVersion)
                        throw new AccountFault("room_receipt_invalid", 503);
                    return receipt;
                }
                PublicRoomAccessPolicy.Target(userId, targetUserId);
                PublicRoomAccessPolicy.ExpectedVersion(room, request.ExpectedVersion);
                if (targetPetId is null || context.Pet.Id != targetPetId || context.Pet.UserId != targetUserId)
                    throw new AccountFault("room_target_not_found", 404);
                if (operation == "removed" && context.State.RoomId != roomId) throw new AccountFault("room_target_not_present", 409);
                Guid[] targets = [userId, targetUserId];
                var now = clock.GetUtcNow();
                if (operation is "ban_set" or "ban_removed")
                {
                    var ban = await db.RoomBans.FromSqlInterpolated(
                        $"SELECT * FROM dango.room_bans WHERE room_id = {roomId} AND user_id = {targetUserId} FOR UPDATE").SingleOrDefaultAsync(ct);
                    if (ban is not null) await db.Entry(ban).ReloadAsync(ct);
                    if (operation == "ban_removed" && (ban is null || !ban.Active)
                        || operation == "ban_set" && ban?.Active == true) throw new AccountFault("room_ban_state_conflict", 409);
                    if (ban is null)
                    {
                        await TargetKeyShareAsync(targetUserId, ct);
                        ban = new() { RoomId = roomId, UserId = targetUserId, Active = true, Version = 1 };
                        db.RoomBans.Add(ban);
                    }
                    else
                    {
                        if (ban.Version == long.MaxValue) throw new AccountFault("room_ban_version_exhausted", 409);
                        ban.Active = operation == "ban_set";
                        ban.Version++;
                    }
                }
                await checkpoint.ReachedAsync("public_after_ban", context.Pet.Id, ct);
                if (operation is "removed" or "ban_set" && context.State.RoomId == roomId)
                    RoomAccessPersistence.ClearAssignment(context, now);
                await db.SaveChangesAsync(ct);
                await checkpoint.ReachedAsync("public_after_detach", context.Pet.Id, ct);
                RoomAccessPersistence.AdvanceRoom(room);
                var result = new PublicRoomModerationResult(room.Id, targetUserId, room.Version, operation);
                RoomAccessPersistence.Append(db, room, operation, now, targets, context, request.RequestId, hash,
                    RoomAccessPersistence.Serialize(result));
                await db.SaveChangesAsync(ct);
                await checkpoint.ReachedAsync("public_after_receipt", context.Pet.Id, ct);
                return result;
            }, ct);
    }

    private async Task<RoomCodeResult> ReplayCodeAsync(RoomOperationAudit original, PersistentRoom room, CancellationToken ct)
    {
        var receipt = RoomAccessPersistence.Deserialize<PublicRoomCodeReceipt>(original.ResultJson);
        var credential = await db.PublicRoomCredentials.AsNoTracking().SingleOrDefaultAsync(code => code.Id == receipt.CredentialId, ct)
            ?? throw new AccountFault("room_code_replay_unavailable", 409);
        if (credential.RecoveryEpoch != environment.RecoveryEpoch) throw InvalidCode();
        PublicRoomAccessPolicy.ValidateReceipt(receipt, original, credential);
        if (receipt.RoomId != room.Id) throw new AccountFault("room_receipt_invalid", 503);
        var code = protector.Unprotect(credential.ProtectedCode, credential.Id, credential.RoomId, credential.Generation);
        if (PublicRoomAccessPolicy.CodeDigest(code) != credential.CodeDigest) throw new AccountFault("room_code_invalid_state", 503);
        return new(receipt.RoomId, receipt.CredentialId, receipt.RoomVersion, receipt.Generation, code, receipt.ExpiresAt);
    }

    private async Task TargetKeyShareAsync(Guid targetUserId, CancellationToken ct)
    {
        try
        {
            var target = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM dango.users WHERE id = {targetUserId} FOR KEY SHARE NOWAIT")
                .SingleOrDefaultAsync(ct);
            if (target == Guid.Empty) throw new AccountFault("room_target_not_found", 404);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        { throw new AccountFault("room_moderation_conflict", 409); }
        catch (InvalidOperationException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.LockNotAvailable })
        { throw new AccountFault("room_moderation_conflict", 409); }
    }

    private static AccountFault InvalidCode() => new("room_code_invalid", 404);

}
