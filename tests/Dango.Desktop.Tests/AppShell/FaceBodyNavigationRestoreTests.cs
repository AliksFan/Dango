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
public sealed class FaceBodyNavigationRestoreTests
{
    [STATestMethod, TestCategory("NativeIntegration")]
    public void ActualWindowCloseKeepsFailedRestoreVisibleAndClosesAfterExplicitRecovery()
    {
        using var host = CreateHost();
        var (window, runtime, session) = OpenPreview(host);
        InjectFault(runtime);

        window.Close();

        Assert.IsTrue(window.IsVisible, "A failed restore must keep the actual window and recovery action visible.");
        Assert.IsTrue(window.ProductHome.IsEditing);
        Assert.AreEqual(120d, runtime.PhysicsBody.SizePercent);
        Assert.AreEqual(120d, session.DraftAppearance.Body.SizePercent);
        Assert.AreEqual(Visibility.Visible, Element<FrameworkElement>(window.ProductHome, "BodyRestoreFeedback").Visibility);

        Click(window.ProductHome, "RestoreSavedBodyButton");
        Assert.IsNull(runtime.LastError);
        Assert.AreEqual(session.SavedAppearance.Body, runtime.PhysicsBody);
        window.Close();
        Assert.IsFalse(window.IsVisible, "A successful restore permits the normal hide-on-close behavior.");
    }

    [STATestMethod, TestCategory("NativeIntegration")]
    [DataRow("Window")]
    [DataRow("DesktopPet")]
    [DataRow("Switch")]
    public void FailedRestoreBlocksRoomHandoffBeforeTheRuntimeOwnerChanges(string entry)
    {
        using var host = CreateHost();
        var (window, runtime, session) = OpenPreview(host);
        var owner = runtime.FrameDriver;
        var presentation = host.LocalPresentation;
        var running = runtime.IsRunning;
        Assert.IsNotNull(owner);
        InjectFault(runtime);

        if (entry == "DesktopPet") host.EnterDesktopPetRoom();
        else if (entry == "Switch") host.SwitchLocalRoomPresentation(LocalRoomPresentation.Window);
        else host.EnterLocalRoom();

        Assert.IsFalse(host.LocalHandoffPending, "Rejected restoration cannot begin a handoff.");
        Assert.AreEqual(presentation, host.LocalPresentation);
        Assert.IsFalse(Field<bool>(host, "localRoomSelected"));
        Assert.AreSame(owner, runtime.FrameDriver, "The editor must retain its sole frame owner.");
        Assert.AreEqual(running, runtime.IsRunning);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.ProductHome.IsEditing);
        Assert.IsFalse(window.ProductHome.IsDangoAway);
        Assert.IsFalse(host.LocalWindow!.IsVisible);
        Assert.AreEqual(120d, session.DraftAppearance.Body.SizePercent);
        Assert.AreEqual(120d, runtime.PhysicsBody.SizePercent);
    }

    private static DangoApplicationHost CreateHost()
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "face-body", "navigation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new(settingsFile: Path.Combine(directory, "settings.json"), isolatedLocalRoomReview: true);
    }

    private static (ControlCenterWindow Window, StackedDangoSceneRuntime Runtime, AppearanceSettingsSession Session) OpenPreview(DangoApplicationHost host)
    {
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter();
        Pump(150);
        var window = Field<ControlCenterWindow>(host, "controlCenterWindow");
        var home = window.ProductHome;
        var session = home.BoundAppearance!;
        home.ShowEditor(HomeEditor.Face);
        Click(home, "AppearanceBodyTab");
        Element<Slider>(home, "BodySizeSlider").Value = 120;
        Click(home, "PreviewBodyButton");
        Assert.AreEqual(120d, runtime.PhysicsBody.SizePercent);
        return (window, runtime, session);
    }

    private static void InjectFault(StackedDangoSceneRuntime runtime) =>
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(runtime.LastError))!.SetValue(runtime, "fictional navigation restore fault");

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;

    private static T Element<T>(ProductHomeView home, string name) where T : class => (T)home.FindName(name);
    private static void Click(ProductHomeView home, string name) =>
        Element<Button>(home, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
