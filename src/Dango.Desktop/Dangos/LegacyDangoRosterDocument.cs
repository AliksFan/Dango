using System.IO;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.Rooms.Storage;

namespace Dango.Desktop.Dangos;

// The exact released schema-one shape is kept separate from the current item contract.
// Retired material values are validated here and survive byte-for-byte in the immutable backup.
internal sealed record LegacyDangoRosterDocument(int SchemaVersion, long Revision, Guid LastOperationId,
    string OperationFingerprint, Guid InitializationId, Guid?[] Slots, LegacyDangoProfileDocument[] Profiles,
    Guid? SelectedId, LegacyAccessoryItem[] Library, OrdinaryRoomNameSource NameSource, Guid? LegacyDangoId)
{
    internal static LegacyDangoRosterDocument Read(byte[] bytes)
    {
        var document = RoomJson.Read<LegacyDangoRosterDocument>(bytes);
        _ = document.ToSnapshot();
        return document;
    }

    internal DangoRosterSnapshot ToSnapshot()
    {
        if (SchemaVersion != 1 || Slots is null || Profiles is null || Library is null || Profiles.Any(p => p is null))
            throw new InvalidDataException("旧团子目录版本或内容无法识别。");
        return new(Revision, LastOperationId, Slots, Profiles.Select(p => p.ToProfile()), SelectedId,
            new(Library.Select(item => item?.ToItem() ?? throw new InvalidDataException("旧共享配饰为空。"))),
            NameSource, LegacyDangoId, InitializationId, OperationFingerprint);
    }
}

internal sealed record LegacyDangoProfileDocument(Guid Id, string? Name, DangoAppearanceDocument Appearance,
    LegacyAccessoryItem[] Accessories, bool RequiresBodyPreview)
{
    internal DangoProfile ToProfile()
    {
        if (Appearance is null || Accessories is null) throw new InvalidDataException("旧个体资料不完整。");
        return new(Id, Name, Appearance.ToAppearance(), new(Accessories.Select(item => item?.ToItem() ??
            throw new InvalidDataException("旧个体配饰为空。"))), RequiresBodyPreview);
    }
}

internal sealed record LegacyAccessoryItem(string Id, string Name, string AssetId, int PixelWidth, int PixelHeight,
    bool IsEquipped, AccessoryLayer Layer, int Order, double SizePercent, double BaseAngleDegrees,
    double ImagePivotX, double ImagePivotY, double BodyAnchorX, double BodyAnchorY, double Softness,
    bool AllowFolding, double MaxDeformationPercent, double ConnectionStrength, double MaxConnectionTravelPercent,
    double RotationDamping, bool LockRotation, double ReturnStrength, double MaxSwingDegrees, bool LimitSwing,
    double GravityInfluence)
{
    internal AccessoryItem ToItem()
    {
        if (!double.IsFinite(Softness) || Softness is < 0 or > 100 ||
            !double.IsFinite(MaxDeformationPercent) || MaxDeformationPercent is < 0 or > 200)
            throw new InvalidDataException("旧配饰柔性参数无效。");
        var item = new AccessoryItem(Id, Name, AssetId, PixelWidth, PixelHeight)
        {
            IsEquipped = IsEquipped, Layer = Layer, Order = Order, SizePercent = SizePercent,
            BaseAngleDegrees = BaseAngleDegrees, ImagePivotX = ImagePivotX, ImagePivotY = ImagePivotY,
            BodyAnchorX = BodyAnchorX, BodyAnchorY = BodyAnchorY, ConnectionStrength = ConnectionStrength,
            MaxConnectionTravelPercent = MaxConnectionTravelPercent, RotationDamping = RotationDamping,
            LockRotation = LockRotation, ReturnStrength = ReturnStrength, MaxSwingDegrees = MaxSwingDegrees,
            LimitSwing = LimitSwing, GravityInfluence = GravityInfluence,
        };
        item.Validate();
        return item;
    }
}
