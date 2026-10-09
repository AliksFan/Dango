using System.IO;
using System.Windows;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class ProductHomeReleaseTests
{
    [STATestMethod]
    public void TerminalReleasePreventsDelayedUnloadFromCallingDisposedRuntime()
    {
        var appearance = new AppearanceSettingsSession(NewPath());
        var scene = new DesktopSceneSession();
        using var runtime = StackedDangoSceneRuntime.CreateProduct("fictional-uninitialized-release.dll");
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(appearance);
        home.BindLiveScene(scene, runtime);

        home.ReleaseLiveScene();
        runtime.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.SetAppearance(DangoAppearanceConfiguration.Default),
            "This regression must use the real disposed-runtime guard.");

        // WPF can deliver Unloaded after the application host has already disposed the runtime.
        home.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        home.CancelAppearancePreview();
        home.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        home.ReleaseLiveScene();

        Assert.IsNull(home.BoundRuntime);
        Assert.IsNull(home.BoundSceneSession);
        Assert.IsNull(home.BoundAppearance);
        Assert.IsFalse(home.OwnsLiveFrameDriver);
    }

    [STATestMethod]
    public void OrdinaryUnloadAndReloadRetainBindingsAndCancelOnlyUnsavedPreview()
    {
        var appearance = new AppearanceSettingsSession(NewPath());
        var scene = new DesktopSceneSession();
        using var runtime = StackedDangoSceneRuntime.CreateProduct("fictional-uninitialized-hide.dll");
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindAppearance(appearance);
        home.BindLiveScene(scene, runtime);
        appearance.StagePreview(new DangoAppearanceConfiguration(OpaqueSrgbColor.Parse("#112233"), OpaqueSrgbColor.Parse("#445566"), appearance.SavedAppearance.EyeLayout));
        runtime.SetAppearance(appearance.DraftAppearance);
        try
        {
            home.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.AreSame(runtime, home.BoundRuntime);
            Assert.AreSame(scene, home.BoundSceneSession);
            Assert.AreSame(appearance, home.BoundAppearance);
            Assert.AreEqual(appearance.SavedAppearance, appearance.DraftAppearance);
            Assert.AreEqual(appearance.SavedAppearance, runtime.EffectiveAppearance);

            home.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.AreSame(runtime, home.BoundRuntime);
            Assert.AreSame(scene, home.BoundSceneSession);
            Assert.AreSame(appearance, home.BoundAppearance);
        }
        finally
        {
            home.ReleaseLiveScene();
        }
    }

    [STATestMethod]
    public void TerminalReleasePreservesAnotherHostsFrameDriverAndRunningRuntime()
    {
        using var runtime = new StackedDangoSceneRuntime("fictional-other-host-release.dll");
        runtime.Start();
        var otherHostDriver = new object();
        runtime.FrameDriver = otherHostDriver;
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindLiveScene(new DesktopSceneSession(), runtime);

        home.ReleaseLiveScene();

        Assert.AreSame(otherHostDriver, runtime.FrameDriver);
        Assert.IsTrue(runtime.IsRunning);
        Assert.IsNull(home.BoundRuntime);
        Assert.IsFalse(home.OwnsLiveFrameDriver);
    }

    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs", "product-home-release", Guid.NewGuid().ToString("N"), "appearance.json");
}
