using System.Text;
using System.Text.Json;
using Dango.Protocol;
using Dango.Server.Accounts;
using Dango.Server.Persistence;
using Dango.Server.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dango.Server.Pets;

public interface IPetConfigurationCheckpoint
{
    Task ReachedAsync(string name, CancellationToken ct);
}

public sealed class PetConfigurationCheckpoint : IPetConfigurationCheckpoint
{
    public Task ReachedAsync(string name, CancellationToken ct) => Task.CompletedTask;
}

public static class PetConfigurationPolicy
{
    public static JsonElement ReadPersisted(string configuration, long version)
    {
        if (version < 1 || string.IsNullOrEmpty(configuration) || Encoding.UTF8.GetByteCount(configuration) > AppearanceContract.MaximumBytes)
            throw InvalidState();
        try
        {
            using var document = JsonDocument.Parse(configuration, new JsonDocumentOptions { MaxDepth = 16 });
            return AppearanceContract.ValidateAndClone(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or AppearanceContractException)
        { throw InvalidState(); }
    }

    public static AccountFault InvalidState() => new("pet_configuration_invalid_state", 503);
}

public sealed class PetConfigurationService(DangoDbContext db, AccountEnvironment environment, TimeProvider clock,
    IPetConfigurationCheckpoint checkpoint)
{
    public async Task<PetConfigurationSnapshot> GetAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        // Authorization and the requested pet come from one fresh PostgreSQL statement snapshot.
        var snapshot = await (from session in db.AuthSessions.AsNoTracking()
                              join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                              where user.Id == userId && session.Id == sessionId
                              join pet in db.Pets.AsNoTracking() on user.Id equals pet.UserId into ownedPets
                              from pet in ownedPets.DefaultIfEmpty()
                              select new { User = user, Session = session, Pet = pet }).SingleOrDefaultAsync(ct);
        if (snapshot is null) throw AuthenticationRequired();
        RequireSession(snapshot.User, snapshot.Session);
        if (snapshot.Pet is null || snapshot.Pet.UserId != userId) throw PetConfigurationPolicy.InvalidState();
        return Describe(snapshot.Pet);
    }

    public async Task<PetConfigurationSnapshot> UpdateAsync(Guid userId, Guid sessionId, PetConfigurationUpdate update, CancellationToken ct)
    {
        if (update.ExpectedVersion < 1) throw new AccountFault("pet_configuration_version_invalid");
        JsonElement configuration;
        try { configuration = AppearanceContract.ValidateAndClone(update.Configuration); }
        catch (AppearanceContractException fault) { throw new AccountFault(fault.Code); }
        var normalized = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.NormalizedEmail).SingleOrDefaultAsync(ct) ?? throw AuthenticationRequired();
        await checkpoint.ReachedAsync("before_locks", ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AccountLocks.EmailAsync(db, normalized, ct);
        var user = await AccountLocks.UserAsync(db, userId, ct) ?? throw AuthenticationRequired();
        await db.Entry(user).ReloadAsync(ct);
        if (user.NormalizedEmail != normalized) throw AuthenticationRequired();
        // Keep the same lock order as credential changes and session revocation.
        var session = await db.AuthSessions.FromSqlInterpolated(
            $"SELECT * FROM dango.auth_sessions WHERE id = {sessionId} AND user_id = {userId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw AuthenticationRequired();
        await db.Entry(session).ReloadAsync(ct);
        RequireSession(user, session);
        var pet = await db.Pets.FromSqlInterpolated(
            $"SELECT * FROM dango.pets WHERE user_id = {userId} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw PetConfigurationPolicy.InvalidState();
        await db.Entry(pet).ReloadAsync(ct);
        if (pet.UserId != userId) throw PetConfigurationPolicy.InvalidState();
        // A valid PATCH is not an implicit repair of damaged persistent data.
        _ = PetConfigurationPolicy.ReadPersisted(pet.Configuration, pet.ConfigurationVersion);
        await checkpoint.ReachedAsync("after_locks", ct);
        RequireSession(user, session);
        if (pet.ConfigurationVersion != update.ExpectedVersion) throw ConfigurationConflict();
        if (pet.ConfigurationVersion == long.MaxValue) throw new AccountFault("pet_configuration_version_exhausted", 409);
        pet.Configuration = SerializeForStorage(configuration);
        pet.ConfigurationVersion++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ConfigurationConflict(); }
        await checkpoint.ReachedAsync("after_configuration", ct);
        await checkpoint.ReachedAsync("before_commit", ct);
        var result = Describe(pet);
        // Expiry can pass while locks, storage or a checkpoint wait. The locked rows protect
        // stamp/state/revocation changes; use fresh reads as well for same-transaction changes.
        var currentUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var currentSession = await db.AuthSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        RequireSession(currentUser, currentSession);
        await transaction.CommitAsync(ct);
        return result;
    }

    private void RequireSession(DangoUser user, AuthSession session)
    {
        if (!AccountSessionPolicy.IsValid(user, session, environment.RecoveryEpoch, clock.GetUtcNow()))
            throw AuthenticationRequired();
    }

    private static PetConfigurationSnapshot Describe(AccountPet pet) => new(pet.UserId, pet.Id, pet.ConfigurationVersion,
        PetConfigurationPolicy.ReadPersisted(pet.Configuration, pet.ConfigurationVersion));
    private static string SerializeForStorage(JsonElement configuration) => JsonSerializer.Serialize(new
    {
        appearanceSchemaVersion = configuration.GetProperty("appearanceSchemaVersion").GetInt32(),
        bodyColorHex = configuration.GetProperty("bodyColorHex").GetString(),
        eyeColorHex = configuration.GetProperty("eyeColorHex").GetString(),
        eyeSizePercent = configuration.GetProperty("eyeSizePercent").GetDouble(),
        eyeOvalityPercent = configuration.GetProperty("eyeOvalityPercent").GetDouble(),
        eyeHeightControlPercent = configuration.GetProperty("eyeHeightControlPercent").GetDouble(),
        eyeSpacingControlPercent = configuration.GetProperty("eyeSpacingControlPercent").GetDouble()
    });
    private static AccountFault AuthenticationRequired() => new("authentication_required", 401);
    private static AccountFault ConfigurationConflict() => new("pet_configuration_conflict", 409);
}
