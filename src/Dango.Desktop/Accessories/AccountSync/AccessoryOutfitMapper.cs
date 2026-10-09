using System.IO;
using Dango.Protocol.Accessories;

namespace Dango.Desktop.Accessories.AccountSync;

/// <summary>Maps only committed wearing parameters, keeping the local library and drafts out of the contract.</summary>
public static class AccessoryOutfitMapper
{
    public static AccessoryOutfitManifest FromSavedConfiguration(AccessoryConfiguration saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        saved.Validate();
        return new(AccessoryOutfitManifest.CurrentSchemaVersion,
            saved.Items.Where(item => item.IsEquipped).Select(ToOutfitItem).ToArray());
    }

    public static AccessoryConfiguration MergeRestoredOutfit(AccessoryConfiguration saved, AccessoryOutfitManifest outfit)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(outfit);
        saved.Validate();
        outfit.Validate();
        var restored = outfit.Items.Select(ToLocalItem).ToDictionary(item => item.Id, StringComparer.Ordinal);
        // Unequipped local entries are independent library content. A remote ID cannot overwrite one.
        if (saved.Items.Any(item => !item.IsEquipped && restored.ContainsKey(item.Id)))
            throw new InvalidDataException("账号穿搭与本地未佩戴条目 ID 冲突；本地配饰库保留。");
        var merged = new List<AccessoryItem>();
        foreach (var item in saved.Items)
        {
            if (restored.Remove(item.Id, out var replacement)) merged.Add(replacement);
            else merged.Add(item with { IsEquipped = false });
        }
        merged.AddRange(outfit.Items.Where(item => restored.ContainsKey(item.Id)).Select(ToLocalItem));
        return new(merged);
    }

    private static AccessoryOutfitItem ToOutfitItem(AccessoryItem item) => new(item.Id, item.Name,
        item.AssetId, item.PixelWidth, item.PixelHeight)
    {
        Layer = item.Layer == AccessoryLayer.Front ? AccessoryOutfitLayer.Front : AccessoryOutfitLayer.Behind,
        Order = item.Order,
        SizePercent = item.SizePercent,
        BaseAngleDegrees = item.BaseAngleDegrees,
        ImagePivotX = item.ImagePivotX,
        ImagePivotY = item.ImagePivotY,
        BodyAnchorX = item.BodyAnchorX,
        BodyAnchorY = item.BodyAnchorY,
        Softness = item.Softness,
        AllowFolding = item.AllowFolding,
        MaxDeformationPercent = item.MaxDeformationPercent,
        ConnectionStrength = item.ConnectionStrength,
        MaxConnectionTravelPercent = item.MaxConnectionTravelPercent,
        RotationDamping = item.RotationDamping,
        LockRotation = item.LockRotation,
        ReturnStrength = item.ReturnStrength,
        MaxSwingDegrees = item.MaxSwingDegrees,
        LimitSwing = item.LimitSwing,
        GravityInfluence = item.GravityInfluence,
    };

    private static AccessoryItem ToLocalItem(AccessoryOutfitItem item) => new(item.Id, item.Name,
        item.AssetId, item.PixelWidth, item.PixelHeight)
    {
        IsEquipped = true,
        Layer = item.Layer == AccessoryOutfitLayer.Front ? AccessoryLayer.Front : AccessoryLayer.Behind,
        Order = item.Order,
        SizePercent = item.SizePercent,
        BaseAngleDegrees = item.BaseAngleDegrees,
        ImagePivotX = item.ImagePivotX,
        ImagePivotY = item.ImagePivotY,
        BodyAnchorX = item.BodyAnchorX,
        BodyAnchorY = item.BodyAnchorY,
        Softness = item.Softness,
        AllowFolding = item.AllowFolding,
        MaxDeformationPercent = item.MaxDeformationPercent,
        ConnectionStrength = item.ConnectionStrength,
        MaxConnectionTravelPercent = item.MaxConnectionTravelPercent,
        RotationDamping = item.RotationDamping,
        LockRotation = item.LockRotation,
        ReturnStrength = item.ReturnStrength,
        MaxSwingDegrees = item.MaxSwingDegrees,
        LimitSwing = item.LimitSwing,
        GravityInfluence = item.GravityInfluence,
    };
}
