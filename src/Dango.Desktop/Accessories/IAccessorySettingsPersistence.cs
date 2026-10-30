using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Accessories;

/// <summary>A manifest boundary independent of the view and its image cache.</summary>
public interface IAccessorySettingsPersistence
{
    AccessorySettingsLoadResult Load(string settingsRoot);
    // The manifest is the commit point. Return the exact committed revision, even when
    // an atomic writer throws after replacement and the target snapshot is verified.
    string Save(string settingsRoot, AccessoryConfiguration configuration,
        IReadOnlyDictionary<string, AccessoryImageAsset>? pendingAssets = null,
        IReadOnlyDictionary<string, AccessoryEditorProject>? pendingProjects = null,
        string? expectedManifestRevision = null);
    AccessoryImageAsset LoadImage(string settingsRoot, string assetId);
    AccessoryEditorProject LoadProject(string settingsRoot, string projectId);
}
