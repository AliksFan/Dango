using System.Net.Mail;
using System.Security.Cryptography;

namespace Dango.Client.Sync.Accounts;

// Implementations must protect these bytes with the user's operating-system secret store.
// Ordinary settings, save files and logs must never receive them.
// Read transfers ownership of its byte array. Write must copy bytes it needs after returning;
// the client clears both read and write buffers when the operation finishes.
public interface IAccountSecretStore
{
    Task<byte[]?> ReadAsync(Uri origin, string slot, CancellationToken ct);
    Task WriteAsync(Uri origin, string slot, byte[] value, CancellationToken ct);
    Task DeleteAsync(Uri origin, string slot, CancellationToken ct);
}

public static class AccountSecretSlots
{
    public const string Session = "session";
    public const string Registration = "registration";
}

public enum AccountFailure { Authentication, Conflict, Unavailable, InvalidResponse, Rejected, Storage }

public sealed class AccountClientException(AccountFailure failure, string code) : Exception(code)
{
    public AccountFailure Failure { get; } = failure;
    public string Code { get; } = code;
    // Populated for logout failures so local cleanup cannot masquerade as server revocation.
    public bool? ServerRevocationConfirmed { get; init; }
    public bool? LocalSecretCleared { get; init; }
}

public sealed record AccountIdentity(Guid UserId, Guid PetId);
public enum AccountRegistrationState { Pending, Verified, Completed }
public sealed record RegistrationStatus(Guid Id, AccountRegistrationState State, DateTimeOffset ExpiresAt,
    DateTimeOffset? CompletionExpiresAt, string EmailStatus);
public sealed record RegistrationResult(Guid UserId, Guid PetId, Guid RoomId);

public sealed class RegistrationAttempt
{
    private RegistrationAttempt(string email, Guid invitationId, string invitationProof, Guid requestKey,
        string clientProof, Guid completionKey, Guid? registrationId, Uri? origin)
    {
        AccountInputs.Email(email);
        AccountInputs.Proof(invitationProof);
        AccountInputs.Proof(clientProof);
        if (invitationId == Guid.Empty || requestKey == Guid.Empty || completionKey == Guid.Empty || registrationId == Guid.Empty)
            throw AccountInputs.Rejected("registration_input_invalid");
        Email = email;
        InvitationId = invitationId;
        InvitationProof = invitationProof;
        RequestKey = requestKey;
        ClientProof = clientProof;
        CompletionKey = completionKey;
        RegistrationId = registrationId;
        Origin = origin;
    }

    public static RegistrationAttempt Create(string email, Guid invitationId, string invitationProof) =>
        new(email, invitationId, invitationProof, Guid.NewGuid(), AccountInputs.NewProof(), Guid.NewGuid(), null, null);

    public string Email { get; }
    public Guid InvitationId { get; }
    public Guid RequestKey { get; }
    public Guid CompletionKey { get; }
    public Guid? RegistrationId { get; private set; }
    public Uri? Origin { get; private set; }
    internal string InvitationProof { get; }
    internal string ClientProof { get; }

    internal static RegistrationAttempt Restore(string email, Guid invitationId, string invitationProof, Guid requestKey,
        string clientProof, Guid completionKey, Guid? registrationId, Uri origin) =>
        new(email, invitationId, invitationProof, requestKey, clientProof, completionKey, registrationId, origin);

    internal void Bind(Uri origin)
    {
        if (Origin is not null && Origin != origin) throw AccountInputs.Rejected("registration_binding_changed");
        Origin = origin;
    }

    internal void SetRegistrationId(Guid id)
    {
        if (id == Guid.Empty || RegistrationId is { } existing && existing != id) throw AccountInputs.InvalidResponse();
        RegistrationId = id;
    }

    public override string ToString() => "RegistrationAttempt [redacted]";
}

internal static class AccountInputs
{
    internal static AccountClientException Rejected(string code) => new(AccountFailure.Rejected, code);
    internal static AccountClientException InvalidResponse() => new(AccountFailure.InvalidResponse, "remote_response_invalid");

    internal static Uri Origin(Uri origin)
    {
        if (origin is null || !origin.IsAbsoluteUri || origin.Scheme != "https" || origin.Host.Length == 0
            || origin.UserInfo.Length != 0 || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            throw Rejected("server_origin_invalid");
        return new Uri(origin.GetLeftPart(UriPartial.Authority) + "/");
    }

    internal static void Email(string? email)
    {
        if (email is null || email.Length is < 3 or > 254 || email != email.Trim()
            || email.Any(c => c > 127 || char.IsControl(c)) || !MailAddress.TryCreate(email, out var address)
            || address.Address != email || address.DisplayName.Length != 0 || !email.Contains('@') || address.Host.Length == 0)
            throw Rejected("email_invalid");
    }

    internal static void Password(string? password, bool registration)
    {
        if (password is null || password.Length > 128 || registration && password.Length < 12)
            throw Rejected("password_invalid");
    }

    internal static string NewProof() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static void Proof(string? proof)
    {
        if (proof is null || proof.Length != 43 || proof.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            throw Rejected("proof_invalid");
        try
        {
            var bytes = Convert.FromBase64String(proof.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 32 || Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != proof)
                throw Rejected("proof_invalid");
        }
        catch (FormatException) { throw Rejected("proof_invalid"); }
    }
}
