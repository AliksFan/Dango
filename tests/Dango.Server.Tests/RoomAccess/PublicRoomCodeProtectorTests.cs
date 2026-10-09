using Dango.Server.Accounts;
using Dango.Server.Rooms.Access;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.RoomAccess;

[TestClass]
public sealed class PublicRoomCodeProtectorTests
{
    [TestMethod]
    public void SameKeyRingEpochAndBindingCanReplayCodeAcrossInstances()
    {
        var provider = new EphemeralDataProtectionProvider();
        var environment = new AccountEnvironment(Guid.NewGuid());
        var credential = Guid.NewGuid(); var room = Guid.NewGuid(); var code = PublicRoomAccessPolicy.NewCode();
        var cipher = new PublicRoomCodeProtector(provider, environment).Protect(code, credential, room, 2);
        Assert.IsTrue(cipher.Length is > 0 and <= 2048); Assert.IsFalse(string.Equals(code, cipher, StringComparison.Ordinal));
        Assert.IsTrue(string.Equals(code, new PublicRoomCodeProtector(provider, environment).Unprotect(cipher, credential, room, 2), StringComparison.Ordinal));
    }

    [TestMethod]
    public void CipherRejectsOtherEpochRoomCredentialGenerationOrKeyRing()
    {
        var provider = new EphemeralDataProtectionProvider();
        var environment = new AccountEnvironment(Guid.NewGuid());
        var credential = Guid.NewGuid(); var room = Guid.NewGuid();
        var protector = new PublicRoomCodeProtector(provider, environment);
        var cipher = protector.Protect(PublicRoomAccessPolicy.NewCode(), credential, room, 2);
        Invalid(() => protector.Unprotect(cipher, Guid.NewGuid(), room, 2));
        Invalid(() => protector.Unprotect(cipher, credential, Guid.NewGuid(), 2));
        Invalid(() => protector.Unprotect(cipher, credential, room, 3));
        Invalid(() => new PublicRoomCodeProtector(provider, new(Guid.NewGuid())).Unprotect(cipher, credential, room, 2));
        Invalid(() => new PublicRoomCodeProtector(new EphemeralDataProtectionProvider(), environment).Unprotect(cipher, credential, room, 2));
    }

    [TestMethod]
    public void MalformedTamperedAndOversizedCipherReturnOnlyStableFault()
    {
        var protector = new PublicRoomCodeProtector(new EphemeralDataProtectionProvider(), new(Guid.NewGuid()));
        var credential = Guid.NewGuid(); var room = Guid.NewGuid();
        var cipher = protector.Protect(PublicRoomAccessPolicy.NewCode(), credential, room, 2);
        Invalid(() => protector.Unprotect("", credential, room, 2));
        Invalid(() => protector.Unprotect(new string('a', 2049), credential, room, 2));
        Invalid(() => protector.Unprotect(cipher[..^2] + "!?", credential, room, 2));
        Invalid(() => protector.Unprotect(cipher, Guid.Empty, room, 2));
        Invalid(() => protector.Unprotect(cipher, credential, room, 0));
    }

    private static void Invalid(Action action)
    {
        var fault = Assert.ThrowsExactly<AccountFault>(action);
        Assert.AreEqual("room_code_invalid_state", fault.Code); Assert.AreEqual(503, fault.Status);
        Assert.AreEqual(fault.Code, fault.Message);
    }
}
