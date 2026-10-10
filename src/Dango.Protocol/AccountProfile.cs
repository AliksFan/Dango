using System.Globalization;
using System.Text;

namespace Dango.Protocol;

public sealed record AccountProfileSnapshot(Guid UserId, string? Nickname, long ProfileVersion);
public sealed record AccountProfileUpdate(long ExpectedVersion, string Nickname)
{ public override string ToString() => "AccountProfileUpdate [redacted]"; }

public sealed class AccountNicknameException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class AccountNickname
{
    public const int MaximumTextElements = 24;
    public const int MaximumUtf8Bytes = 192;

    public static string Normalize(string? input)
    {
        if (input is null || input.Length > 1024) throw Invalid();
        // Validate before trimming: a line break must not become a valid name by trimming.
        for (var index = 0; index < input.Length;)
        {
            if (!Rune.TryGetRuneAt(input, index, out var rune)) throw Invalid();
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                || category == UnicodeCategory.Format && rune.Value is not (0x200C or 0x200D)) throw Invalid();
            index += rune.Utf16SequenceLength;
        }
        var value = input.Trim().Normalize(NormalizationForm.FormC);
        if (value.Length == 0 || Encoding.UTF8.GetByteCount(value) > MaximumUtf8Bytes
            || StringInfo.ParseCombiningCharacters(value).Length > MaximumTextElements
            || !value.EnumerateRunes().Any(r => Rune.GetUnicodeCategory(r) is not
                (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format or UnicodeCategory.SpaceSeparator))) throw Invalid();
        return value;
    }

    private static AccountNicknameException Invalid() => new("nickname_invalid");
}
