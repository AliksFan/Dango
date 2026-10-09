using System.Globalization;
using System.Security.Cryptography;
using Dango.Server.Accounts;
using Microsoft.AspNetCore.DataProtection;

namespace Dango.Server.Rooms.Access;

public interface IPublicRoomCodeProtector
{
    string Protect(string code, Guid credentialId, Guid roomId, long generation);
    string Unprotect(string cipher, Guid credentialId, Guid roomId, long generation);
}

// The formal host owns the persistent key ring. Recovery and credential bindings cannot be exchanged.
public sealed class PublicRoomCodeProtector(IDataProtectionProvider provider, AccountEnvironment environment) : IPublicRoomCodeProtector
{
    public string Protect(string code, Guid credentialId, Guid roomId, long generation)
    {
        _ = PublicRoomAccessPolicy.CodeDigest(code);
        return Protector(credentialId, roomId, generation).Protect(code);
    }

    public string Unprotect(string cipher, Guid credentialId, Guid roomId, long generation)
    {
        if (string.IsNullOrEmpty(cipher) || cipher.Length > 2048) throw InvalidState();
        try
        {
            var code = Protector(credentialId, roomId, generation).Unprotect(cipher);
            _ = PublicRoomAccessPolicy.CodeDigest(code);
            return code;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or AccountFault)
        { throw InvalidState(); }
    }

    private IDataProtector Protector(Guid credentialId, Guid roomId, long generation)
    {
        if (environment.RecoveryEpoch == Guid.Empty || credentialId == Guid.Empty || roomId == Guid.Empty || generation < 1)
            throw InvalidState();
        return provider.CreateProtector("Dango.PublicRoomCode", "v1", environment.RecoveryEpoch.ToString("D"),
            credentialId.ToString("D"), roomId.ToString("D"), generation.ToString(CultureInfo.InvariantCulture));
    }

    private static AccountFault InvalidState() => new("room_code_invalid_state", 503);
}
