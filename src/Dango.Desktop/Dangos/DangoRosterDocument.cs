using System.IO;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Dangos;

internal sealed record DangoRosterDocument(int SchemaVersion, long Revision, Guid LastOperationId,
    string OperationFingerprint, Guid InitializationId, Guid?[] Slots, DangoProfileDocument[] Profiles,
    Guid? SelectedId, AccessoryItem[] Library, OrdinaryRoomNameSource NameSource, Guid? LegacyDangoId)
{
    internal DangoRosterSnapshot ToSnapshot()
    {
        if (SchemaVersion != 1 || Slots is null || Profiles is null || Library is null)
            throw new InvalidDataException("团子目录版本或内容无法识别。");
        if (Profiles.Any(p => p is null)) throw new InvalidDataException("个体资料为空。");
        return new(Revision, LastOperationId, Slots, Profiles.Select(p => p.ToProfile()), SelectedId,
            new(Library), NameSource, LegacyDangoId, InitializationId, OperationFingerprint);
    }

    internal static DangoRosterDocument From(DangoRosterSnapshot value) => new(1, value.Revision,
        value.LastOperationId, value.OperationFingerprint, value.InitializationId, value.Slots.ToArray(),
        value.Profiles.Select(DangoProfileDocument.From).ToArray(), value.SelectedId, value.Library.Items.ToArray(),
        value.NameSource, value.LegacyDangoId);
}

internal sealed record DangoProfileDocument(Guid Id, string? Name, DangoAppearanceDocument Appearance,
    AccessoryItem[] Accessories, bool RequiresBodyPreview)
{
    internal DangoProfile ToProfile()
    {
        if (Appearance is null || Accessories is null) throw new InvalidDataException("个体资料不完整。");
        return new(Id, Name, Appearance.ToAppearance(), new(Accessories), RequiresBodyPreview);
    }
    internal static DangoProfileDocument From(DangoProfile p) => new(p.Id, p.Name, DangoAppearanceDocument.From(p.Appearance),
        p.Accessories.Items.ToArray(), p.RequiresBodyPreview);
}

internal sealed record DangoAppearanceDocument(string BodyColorHex, string EyeColorHex, double EyeSizePercent,
    double EyeOvalityPercent, double EyeHeightControlPercent, double EyeSpacingControlPercent,
    double BodySizePercent, double BodySoftness, double BodyLandingBuild, string BodyPhysicsModel)
{
    internal DangoAppearanceConfiguration ToAppearance() => new(OpaqueSrgbColor.Parse(BodyColorHex),
        OpaqueSrgbColor.Parse(EyeColorHex), new EyeLayoutParameters(EyeSizePercent, EyeOvalityPercent,
            EyeHeightControlPercent, EyeSpacingControlPercent), new DangoBodyControls(BodySizePercent, BodySoftness,
            BodyLandingBuild), BodyPhysicsModel);
    internal static DangoAppearanceDocument From(DangoAppearanceConfiguration a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new(a.BodyColor.ToHex(), a.EyeColor.ToHex(), a.EyeLayout.SizePercent, a.EyeLayout.OvalPercent,
            a.EyeLayout.HeightControlPercent, a.EyeLayout.SpacingControlPercent, a.Body.SizePercent, a.Body.Softness,
            a.Body.LandingBuild, a.BodyPhysicsModel);
    }
}

internal sealed record DangoInitialization(int SchemaVersion, Guid InitializationId, Guid? LegacyDangoId,
    string? AppearanceSha256, string? AccessoriesSha256, string? SettingsSha256)
{
    internal void Validate()
    {
        if (SchemaVersion != 1 || InitializationId == Guid.Empty || LegacyDangoId == Guid.Empty ||
            new[] { AppearanceSha256, AccessoriesSha256, SettingsSha256 }.Any(hash => hash is not null &&
                (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c)))))
            throw new InvalidDataException("初始化保护记录无效。");
        if ((AppearanceSha256 is not null || AccessoriesSha256 is not null || SettingsSha256 is not null) != LegacyDangoId.HasValue)
            throw new InvalidDataException("初始化来源与身份不一致。");
    }
}
