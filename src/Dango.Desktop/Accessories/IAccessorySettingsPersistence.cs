namespace Dango.Desktop.Accessories;

/// <summary>A manifest boundary independent of the view and its image cache.</summary>
public interface IAccessorySettingsPersistence
{
    AccessorySettingsLoadResult Load(string settingsRoot);
    void Save(string settingsRoot, AccessoryConfiguration configuration,
        IReadOnlyDictionary<string, AccessoryImageAsset>? pendingAssets = null);
    AccessoryImageAsset LoadImage(string settingsRoot, string assetId);
}
