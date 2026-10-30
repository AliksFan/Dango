using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class SettingsRoomIntegrationTests
{
    [STATestMethod]
    public void CatalogPreparationBlocksRecoveryAndDoesNotReportThePreviousTarget()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "settings-refresh", "catalog-loading", Guid.NewGuid().ToString("N"));
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), roomModulePath: Path.Combine(root, "missing.dll"));
        host.ShowDiagnostics();
        var gate = Field<LocalRoomHandoffGate>(host, "roomHandoff");
        var attempt = gate.Begin(LocalRoomPresentation.DesktopPet);
        Assert.IsTrue(gate.Fail(attempt));
        var loading = typeof(DangoApplicationHost).GetField("catalogEntryLoading", BindingFlags.Instance | BindingFlags.NonPublic)!;
        loading.SetValue(host, true);
        try
        {
            var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
            window.ShowDiagnostics();
            var snapshot = host.ReadProductSettingsScene();
            StringAssert.Contains(snapshot.Name, "普通房");
            StringAssert.Contains(snapshot.Status, "切换");
            Assert.IsFalse(snapshot.CanRestoreDisplay);
            Assert.IsFalse(snapshot.CanReinitialize);
            window.ConfirmSceneReinitialization = () => throw new AssertFailedException("目录准备期间不应请求重建确认。");
            Click(window, "RestoreRoomDisplayButton");
            Click(window, "ReinitializeSceneButton");
            Assert.IsFalse(gate.Pending);
        }
        finally { loading.SetValue(host, false); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void InteractionFaultAloneIsVisibleAndCanBeReinitialized()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host);
        host.EnterLocalRoom(); WaitForRoom(host);
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
        WaitForRoom(host);
        Assert.IsNull(runtime.LightInteractionError);
        Assert.IsFalse(host.LocalHandoffPending);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void PreviousMinimizedWindowDoesNotOfferRestoreForAnotherPresentation()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom);
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
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); WaitForCatalog(host);
        if (desktopPet) host.EnterDesktopPetRoom(); else host.EnterLocalRoom();
        WaitForRoom(host, desktopPet);
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
        WaitForRoom(host, desktopPet);
        Assert.AreEqual(desktopPet ? LocalRoomPresentation.DesktopPet : LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.IsTrue(desktopPet ? host.DesktopPetHost!.IsReady : host.LocalWindow!.Scene.HasHealthyFrame);
        Assert.IsFalse((window.ProductHome.BoundBeltBody?.Metrics.Running == true));
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
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); WaitForCatalog(host);
        host.EnterLocalRoom(); WaitForRoom(host);
        var id = runtime.EntityIds.Single();
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!.SetValue(runtime, "fictional settings room fault");
        Pump(150);
        host.ShowDiagnostics();
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        Assert.IsTrue(host.ReadProductSettingsScene().CanReinitialize);
        window.ConfirmSceneReinitialization = () => true;
        Click(window, "ReinitializeSceneButton"); WaitForRoom(host);
        Assert.IsNull(runtime.LastError);
        Assert.IsNull(host.LocalRoomFailure);
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.IsTrue(host.LocalWindow!.Scene.HasHealthyFrame);
        Assert.IsFalse((window.ProductHome.BoundBeltBody?.Metrics.Running == true));
        Assert.IsNotNull(runtime.FrameDriver);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void HomeRecoveryDoesNotEnterTheRememberedRoomAndKeepsSettingsOpen()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var home = window.ProductHome;
        var worker = home.BoundBeltBody;
        typeof(ProductHomeView).GetMethod("LatchBeltFailure", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(home, ["fictional home fault"]);
        host.ShowDiagnostics();
        Assert.IsTrue(host.ReadProductSettingsScene().CanReinitialize);
        window.ConfirmSceneReinitialization = () => true;
        Click(window, "ReinitializeSceneButton");
        Assert.IsNull(host.ReadProductSettingsScene().Error);
        Assert.AreNotSame(worker, home.BoundBeltBody, "Recovery must replace the actual failed home worker.");
        Assert.IsFalse(host.LocalHandoffPending);
        Assert.AreEqual(LocalRoomPresentation.Porch, host.LocalPresentation);
        Assert.IsFalse(host.LocalWindow!.IsVisible);
        Assert.IsTrue(home.IsSettingsOpen);
        Assert.IsNull(runtime.FrameDriver);
        Assert.AreEqual(0, runtime.EntityIds.Count, "Home recovery must not initialize the remembered room.");
        Click(window, "BackFromSettingsButton");
        ProductHostTestSupport.WaitForHome(host);
    }

    [STATestMethod]
    public void MissingRoomModuleKeepsTheHealthySourceAndDoesNotAcknowledgeRoomEntry()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "settings-refresh", "missing-module", Guid.NewGuid().ToString("N"));
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"), roomModulePath: Path.Combine(root, "missing.dll"));
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        host.EnterLocalRoom();
        Until(() => host.LocalRoomFailure is not null, "The unavailable room module must report an actual failed entry.");
        Until(() => !host.LocalHandoffPending, "The failed entry must release its pending state.");
        Assert.IsTrue(window.IsVisible);
        Assert.IsFalse(host.LocalWindow!.IsVisible);
        Assert.AreEqual(0, runtime.EntityIds.Count);
        Assert.IsNull(runtime.FrameDriver);
        Assert.IsNotNull(runtime.LastError);
        ProductHostTestSupport.WaitForHome(host);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void SettingsRecoveryKeepsTheSecondCatalogRoomAndItsCheckpointIdentity()
    {
        using var host = CreateHost();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.UsesBeltRoom, "Product rooms must use the continuous body module.");
        host.ShowControlCenter(); ProductHostTestSupport.WaitForHome(host); WaitForCatalog(host);
        var store = Field<LocalRoomCatalogStore>(host, "roomCatalogStore");
        var first = store.ReadCurrent().Rooms.First();
        var firstLayout = File.ReadAllBytes(store.LayoutPath(first.RoomId));
        var resumeStore = Field<RoomResumeStore>(host, "roomResumeStore");
        var firstResume = resumeStore.Load(first.RoomId).Snapshot;
        var secondId = Guid.NewGuid();
        var created = store.Create(Guid.NewGuid(), secondId, "虚构设置验收房", LocalRoomKind.Private, 1);
        Assert.IsTrue(created.Success, created.Warning);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var refresh = window.ProductHome.BindRoomCatalogAsync(store);
        Until(() => refresh.IsCompleted, "目录刷新没有完成。"); refresh.GetAwaiter().GetResult();
        host.EnterCatalogRoom(secondId, false); WaitForRoom(host);
        var entity = runtime.EntityIds.Single();
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!.SetValue(runtime, "fictional second room fault");
        Until(() => host.LocalRoomFailure is not null, "运行故障没有被报告。");
        host.ShowDiagnostics();
        window.ConfirmSceneReinitialization = () => true;
        Click(window, "ReinitializeSceneButton"); WaitForRoom(host);
        Assert.AreEqual(secondId, Field<Guid?>(host, "selectedCatalogRoomId"));
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.AreEqual(store.LayoutPath(secondId), Field<string>(runtime.Furniture!, "path"));
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        Until(() => driver.CaptureCheckpoint() is not null, "恢复后没有安全地面检查点。");
        var beforeVersion = resumeStore.Load(secondId).Value?.ResumeVersion ?? 0;
        typeof(DangoApplicationHost).GetMethod("SaveRoomCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, [true]);
        var saved = resumeStore.Load(secondId);
        Assert.IsNotNull(saved.Value);
        Assert.AreEqual(secondId, saved.Value.RoomId);
        Assert.IsTrue(saved.Value.ResumeVersion > beforeVersion, "本次显式保存应真正推进第二房检查点版本。");
        var afterFirstResume = resumeStore.Load(first.RoomId).Snapshot;
        Assert.AreEqual(firstResume.State, afterFirstResume.State);
        CollectionAssert.AreEqual(firstResume.Bytes ?? [], afterFirstResume.Bytes ?? []);
        CollectionAssert.AreEqual(firstLayout, File.ReadAllBytes(store.LayoutPath(first.RoomId)));
    }

    private static void WaitForCatalog(DangoApplicationHost host) => Until(
        () => Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome.CatalogSnapshot is not null,
        "本地目录没有完成加载。");
    private static void WaitForRoom(DangoApplicationHost host, bool desktopPet = false) => Until(
        () => !host.LocalHandoffPending && host.LocalPresentation == (desktopPet ? LocalRoomPresentation.DesktopPet : LocalRoomPresentation.Window) &&
            (desktopPet ? host.DesktopPetHost?.IsReady == true : host.LocalWindow?.Scene.HasHealthyFrame == true),
        "目标房间没有完成健康首帧：" + host.LocalRoomFailure);
    private static void Until(Func<bool> done, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < deadline) Pump(25);
        Assert.IsTrue(done(), message);
    }
    private static DangoApplicationHost CreateHost() => new(settingsFile: Path.Combine(TestRepository.Root, "artifacts", "settings-refresh", "rooms", Guid.NewGuid().ToString("N"), "settings.json"));
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
