using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.AppShell;

public enum ApplicationUiMode { Product, DeveloperReview }

public interface IDangoSceneControl
{
    SceneInitializationState InitializationState { get; }
    string HatPresetId { get; }
    bool TryApplyHatPreset(string id);
    bool RetryInitialization();
}
