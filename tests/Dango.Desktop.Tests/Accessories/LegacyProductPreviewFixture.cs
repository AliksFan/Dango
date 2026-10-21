using System.Windows;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Accessories;

// Historical module capability/physics diagnostics only. This deliberately has
// no DangoApplicationHost or App startup route and is not default product evidence.
internal sealed class LegacyProductPreviewFixture : IDisposable
{
    internal StackedDangoSceneRuntime Runtime { get; }
    internal ProductHomeView Home { get; }
    internal Window Window { get; }
    internal LegacyProductPreviewFixture(string root, string module)
    {
        Directory.CreateDirectory(root);
        Runtime = StackedDangoSceneRuntime.CreateProduct(module);
        Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
        Home = new ProductHomeView();
        var appearance = new AppearanceSettingsSession(Path.Combine(root, "appearance.json"));
        var accessories = new Dango.Desktop.Accessories.AccessorySettingsSession(root);
        Runtime.SetAppearance(appearance.SavedAppearance);
        Runtime.BindAccessories(accessories);
        Home.BindLiveScene(new DesktopSceneSession(), Runtime);
        Home.BindAppearance(appearance);
        Home.BindAccessories(accessories);
        Window = new Window { Content = Home, Width = 1024, Height = 700, Title = "Historical module accessory diagnostic" };
    }
    internal void Show() => Window.Show();
    public void Dispose()
    {
        Window.Close();
        Home.ReleaseLiveScene();
        Runtime.Dispose();
    }
}
