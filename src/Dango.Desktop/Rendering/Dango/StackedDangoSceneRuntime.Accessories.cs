using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation;
using System.Windows.Media.Imaging;

namespace Dango.Desktop.Rendering.Visuals;

public sealed partial class StackedDangoSceneRuntime
{
    private AccessorySceneController? accessoryScene;
    private string? accessoryRenderError;
    private Func<string, BitmapSource?>? accessoryImageOverride;
    public bool AccessoriesEnabled => accessoryScene?.Enabled == true;
    public string? AccessoryError => accessoryScene?.Error ?? accessoryRenderError;
    public event Action? AccessoryPreviewChanged;
    internal IReadOnlyList<AccessoryVisualFrame> GetAccessoryFrames(StackedPhysicsPresentationFrame frame, int index) =>
        accessoryScene?.Matches(frame, index) == true ? accessoryScene.Frames : [];

    internal void ReportAccessoryRenderError(string? error) => accessoryRenderError = error;

    internal void BindAccessories(AccessorySettingsSession session)
    {
        VerifyAccess();
        if (accessoryScene is not null) throw new InvalidOperationException("配饰会话已绑定。");
        accessoryScene = new(id => accessoryImageOverride?.Invoke(id) ?? session.TryGetImage(id));
        SetAccessories(session.SavedConfiguration, !session.IsCompatibilityMode);
    }

    internal void SetAccessories(AccessoryConfiguration configuration, bool enabled = true, Func<string, BitmapSource?>? imageOverride = null)
    {
        VerifyAccess();
        accessoryImageOverride = imageOverride;
        accessoryScene?.SetConfiguration(configuration, enabled, LastFrame);
        AccessoryPreviewChanged?.Invoke();
    }
}
