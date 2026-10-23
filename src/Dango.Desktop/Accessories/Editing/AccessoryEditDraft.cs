namespace Dango.Desktop.Accessories.Editing;

/// <summary>Native modal identity. The outer draft is unchanged until a complete matching result is accepted.</summary>
public sealed record AccessoryEditDraft(string SessionId, long Generation, string ItemId, long Revision, AccessoryItem Original);

public sealed class AccessoryEditResult
{
    public AccessoryEditorProject Project { get; }
    public AccessoryImageAsset Image { get; }
    public AccessoryItem Item { get; }
    public AccessoryEditResult(AccessoryEditorProject project, AccessoryImageAsset image, AccessoryItem item)
    {
        ArgumentNullException.ThrowIfNull(project); ArgumentNullException.ThrowIfNull(image); ArgumentNullException.ThrowIfNull(item);
        project.ValidateResult(item, image);
        Project = project; Image = image; Item = item;
    }
}
