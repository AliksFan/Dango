using System.Text.Json;
using Dango.Protocol;

namespace Dango.Client.Sync.Accounts;

public interface IAccountProfileClient
{
    Uri Origin { get; }
    Guid UserId { get; }
    Task<AccountProfileSnapshot> ReadAsync(CancellationToken ct);
    Task<AccountProfileSnapshot> UpdateAsync(long expectedVersion, string nickname, CancellationToken ct);
}

public sealed partial class FormalAccountClient
{
    private static readonly HashSet<string> ProfileErrorCodes = new(StringComparer.Ordinal)
    { "nickname_invalid", "account_profile_conflict", "account_profile_version_exhausted", "account_profile_invalid_state" };

    public IAccountProfileClient CreateProfileClient(AccountIdentity identity)
    {
        ThrowIfDisposed();
        if (identity is null || identity.UserId == Guid.Empty || identity.PetId == Guid.Empty || CurrentIdentity != identity)
            throw new AccountClientException(AccountFailure.Authentication, "account_profile_binding_changed");
        return new BoundProfileClient(this, identity, sessionGeneration);
    }

    private static AccountProfileSnapshot ParseProfile(JsonElement input)
    {
        Fields(input, "userId", "nickname", "profileVersion");
        var rawVersion = input.GetProperty("profileVersion");
        if (rawVersion.ValueKind != JsonValueKind.Number || !rawVersion.TryGetInt64(out var version) || version < 1)
            throw AccountInputs.InvalidResponse();
        var rawName = input.GetProperty("nickname");
        var nickname = rawName.ValueKind == JsonValueKind.Null ? null : StringValue(input, "nickname");
        if (nickname is not null)
        {
            try { if (AccountNickname.Normalize(nickname) != nickname) throw AccountInputs.InvalidResponse(); }
            catch (AccountNicknameException) { throw AccountInputs.InvalidResponse(); }
        }
        return new(GuidValue(input, "userId"), nickname, version);
    }

    private sealed class BoundProfileClient(FormalAccountClient owner, AccountIdentity identity, int generation) : IAccountProfileClient
    {
        public Uri Origin => owner.Origin;
        public Guid UserId => identity.UserId;

        public Task<AccountProfileSnapshot> ReadAsync(CancellationToken ct) => ExecuteAsync(async token =>
            ParseProfile(await owner.RequestAsync(HttpMethod.Get, "api/v1/me/profile", null, null, token,
                additionalErrorCodes: ProfileErrorCodes)), ct);

        public Task<AccountProfileSnapshot> UpdateAsync(long expectedVersion, string nickname, CancellationToken ct) => ExecuteAsync(async token =>
        {
            if (expectedVersion is < 1 or long.MaxValue) throw AccountInputs.Rejected("expected_version_invalid");
            try { nickname = AccountNickname.Normalize(nickname); }
            catch (AccountNicknameException fault) { throw AccountInputs.Rejected(fault.Code); }
            var result = ParseProfile(await owner.RequestAsync(HttpMethod.Patch, "api/v1/me/profile",
                new AccountProfileUpdate(expectedVersion, nickname), null, token, additionalErrorCodes: ProfileErrorCodes));
            if (result.ProfileVersion != expectedVersion + 1 || result.Nickname != nickname) throw AccountInputs.InvalidResponse();
            return result;
        }, ct);

        private Task<AccountProfileSnapshot> ExecuteAsync(Func<CancellationToken, Task<AccountProfileSnapshot>> operation, CancellationToken ct) =>
            owner.LockedAsync(async token =>
            {
                RequireBinding(token);
                var result = await operation(token);
                RequireBinding(token);
                if (result.UserId != UserId) throw AccountInputs.InvalidResponse();
                return result;
            }, ct);

        private void RequireBinding(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            owner.ThrowIfDisposed();
            if (generation != owner.sessionGeneration || owner.CurrentIdentity != identity)
                throw new AccountClientException(AccountFailure.Authentication, "account_profile_binding_changed");
        }
    }
}
