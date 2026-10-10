using System.Text;

namespace Dango.Protocol.Tests;

[TestClass]
public sealed class AccountProfileTests
{
    [TestMethod]
    public void NicknameIsTrimmedNfcAndAllowsChineseSpacesEmojiAndRepeatedNames()
    {
        Assert.AreEqual("团子 A", AccountNickname.Normalize("  团子 A  "));
        Assert.AreEqual("é团子", AccountNickname.Normalize("e\u0301团子"));
        Assert.AreEqual("👩‍💻", AccountNickname.Normalize("👩‍💻"));
        Assert.AreEqual(AccountNickname.Normalize("小团子"), AccountNickname.Normalize("小团子"));
        Assert.AreEqual(new string('团', 24), AccountNickname.Normalize(new string('团', 24)));
        Assert.AreEqual(string.Concat(Enumerable.Repeat("é", 24)), AccountNickname.Normalize(string.Concat(Enumerable.Repeat("e\u0301", 24))));
    }

    [TestMethod]
    public void InvalidInvisibleControlBidiMalformedAndOversizedNamesFailWithStableCode()
    {
        foreach (var input in new string?[] { null, "", "   ", "\t团子", "团子\n", "团\r子", "\u2028团子", "\u2029团子",
            "团\u0000子", "团\u202E子", "\u2066团子", "\u061C团子", "团\u200E子", "团\u200B子", "\u0301", "\u200D",
            "\ud800", "\udc00", new string('团', 25), new string('a', 1025) })
        {
            var error = Assert.ThrowsExactly<AccountNicknameException>(() => AccountNickname.Normalize(input));
            Assert.AreEqual("nickname_invalid", error.Code);
            Assert.AreEqual(error.Code, error.Message);
        }
        var oneLargeGrapheme = "a" + new string('\u0301', 100);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(oneLargeGrapheme) > AccountNickname.MaximumUtf8Bytes);
        Assert.ThrowsExactly<AccountNicknameException>(() => AccountNickname.Normalize(oneLargeGrapheme));
    }

    [TestMethod]
    public void RequestToStringDoesNotExposeInput()
    {
        Assert.DoesNotContain("fictional-name", new AccountProfileUpdate(1, "fictional-name").ToString());
    }
}
