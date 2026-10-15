using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class SettingsRoomIntegrationTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void InteractionFaultAloneIsVisibleAndCanBeReinitialized()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowDiagnostics();
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LightInteractionError))!.SetValue(runtime, "fictional interaction fault");
        Assert.IsNull(runtime.LastError);
        var snapshot = host.ReadProductSettingsScene();
        Assert.AreEqual("fictional interaction fault", snapshot.Error);
        Assert.IsTrue(snapshot.CanReinitialize);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        window.ShowDiagnostics();
        window.ConfirmSceneReinitialization = () => true;
        Click(window, "ReinitializeSceneButton");
        Assert.IsNull(runtime.LightInteractionError);
        Assert.IsFalse(host.LocalHandoffPending);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PreviousMinimizedWindowDoesNotOfferRestoreForAnotherPresentation()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowDiagnostics();
        host.LocalWindow!.WindowState = WindowState.Minimized;
        typeof(DangoApplicationHost).GetField("localRoomSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, true);
        foreach (var presentation in new[] { LocalRoomPresentation.DesktopPet, LocalRoomPresentation.Wallpaper })
        {
            typeof(DangoApplicationHost).GetField("preferredRoomPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, presentation);
            var gate = Field<LocalRoomHandoffGate>(host, "roomHandoff");
            var generation = gate.Begin(presentation);
            Assert.IsTrue(gate.TryComplete(generation, presentation, true));
            Assert.IsFalse(host.ReadProductSettingsScene().CanRestoreDisplay, "其他呈现不能使用旧窗口的最小化状态。");
        }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void SettingsRestoreHiddenRoomWithoutResettingEntityOrCreatingSecondOwner(bool desktopPet)
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(350);
        if (desktopPet) host.EnterDesktopPetRoom(); else host.EnterLocalRoom();
        Pump(desktopPet ? 900 : 450);
        var id = runtime.EntityIds.Single();
        Assert.AreEqual(desktopPet ? LocalRoomPresentation.DesktopPet : LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        host.HideLocalRoom();
        host.ShowDiagnostics();
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var snapshot = host.ReadProductSettingsScene();
        StringAssert.Contains(snapshot.Name, desktopPet ? "桌宠房" : "普通房");
        Assert.IsTrue(snapshot.CanRestoreDisplay);
        Assert.IsFalse(snapshot.CanReinitialize);
        Assert.IsNull(runtime.FrameDriver);
        Click(window, "RestoreRoomDisplayButton");
        Assert.IsTrue(host.LocalHandoffPending);
        Assert.IsFalse(host.ReadProductSettingsScene().CanRestoreDisplay);
        Pump(desktopPet ? 900 : 450);
        Assert.AreEqual(desktopPet ? LocalRoomPresentation.DesktopPet : LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.IsTrue(desktopPet ? host.DesktopPetHost!.IsReady : host.LocalWindow!.Scene.HasHealthyFrame);
        Assert.IsFalse(window.ProductHome.OwnsLiveFrameDriver);
        Assert.IsNotNull(runtime.FrameDriver);
        window.ShowDiagnostics();
        StringAssert.Contains(((TextBox)window.ProductHome.FindName("SettingsDiagnosticsText")).Text, desktopPet ? "桌宠房 · 本地单人" : "普通房 · 窗口");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SettingsReinitializeFaultedRoomThroughActualRoomRecovery()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(350);
        host.EnterLocalRoom(); Pump(450);
        var id = runtime.EntityIds.Single();
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!.SetValue(runtime, "fictional settings room fault");
        Pump(150);
        host.ShowDiagnostics();
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        Assert.IsTrue(host.ReadProductSettingsScene().CanReinitialize);
        window.ConfirmSceneReinitialization = () => true;
        Click(window, "ReinitializeSceneButton"); Pump(450);
        Assert.IsNull(runtime.LastError);
        Assert.IsNull(host.LocalRoomFailure);
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.IsTrue(host.LocalWindow!.Scene.HasHealthyFrame);
        Assert.IsFalse(window.ProductHome.OwnsLiveFrameDriver);
        Assert.IsNotNull(runtime.FrameDriver);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void HomeRecoveryDoesNotEnterTheRememberedRoomAndKeepsSettingsOpen()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowDiagnostics();
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!.SetValue(runtime, "fictional home fault");
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        window.ShowDiagnostics();
        window.ConfirmSceneReinitialization = () => true;
        Click(window, "ReinitializeSceneButton");
        Assert.IsNull(runtime.LastError);
        Assert.IsFalse(host.LocalHandoffPending);
        Assert.AreEqual(LocalRoomPresentation.Porch, host.LocalPresentation);
        Assert.IsFalse(host.LocalWindow!.IsVisible);
        Assert.IsTrue(window.ProductHome.IsSettingsOpen);
        Assert.IsNull(runtime.FrameDriver, "设置页不应在恢复后启动隐藏的首页 owner。");
        Click(window, "BackFromSettingsButton"); Pump(350);
        Assert.IsTrue(window.ProductHome.OwnsLiveFrameDriver);
    }

    [STATestMethod]
    public void MissingNativeModuleKeepsTheCurrentFailureAndDoesNotEnterRoom()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "settings-refresh", "missing-module", Guid.NewGuid().ToString("N"));
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), nativeLibraryPath: Path.Combine(root, "missing.dll"));
        host.ShowDiagnostics();
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        window.ConfirmSceneReinitialization = () => true;
        Click(window, "ReinitializeSceneButton");
        Assert.IsNotNull(host.ReadProductSettingsScene().Error);
        Assert.IsTrue(host.ReadProductSettingsScene().CanReinitialize);
        Assert.IsFalse(host.LocalHandoffPending);
        Assert.IsFalse(host.LocalWindow!.IsVisible);
    }

    private static DangoApplicationHost CreateHost() => new(settingsFile: Path.Combine(TestRepository.Root, "artifacts", "settings-refresh", "rooms", Guid.NewGuid().ToString("N"), "settings.json"), isolatedLocalRoomReview: true);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Click(ControlCenterWindow window, string name) => ((Button)window.ProductHome.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
