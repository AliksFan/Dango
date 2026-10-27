using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Dangos;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class OrdinaryRoomDangoIntegrationTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void RenderedRestoreWaitsForItsOwnCheckpointLockAndCancellationCannotReopen(bool cancel)
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "ordinary-room-three-slots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new DangoRosterStore(root);
        var roster = Commit(store.Create(store.LoadOrInitialize().Snapshot!, 0, "虚构团子甲", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        var id = roster.SelectedId!.Value;
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"));
        var home = ShowHome(host);
        var room = home.CatalogSnapshot!.Rooms.First(x => x.Life == LocalRoomLife.Active).RoomId;
        var catalog = new LocalRoomCatalogStore(root);
        var resume = new RoomResumeStore(catalog);
        Assert.AreEqual(FurnitureCommitState.Committed, resume.SaveForDango(room, id, catalog.ReadLayout(room).LayoutVersion,
            new(500, 0), new(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0)).State);
        Enter(host, home, room, id, "虚构团子甲", DangoAppearanceConfiguration.Default, 500);
        host.HideLocalRoom();
        Wait(() => Field<Task>(host, "roomCheckpointTask").IsCompleted, "the preceding real checkpoint must finish before this controlled write");
        var pendingSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(DangoApplicationHost).GetField("roomCheckpointTask", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host, pendingSave.Task);
        IDisposable? held = catalog.AcquireRoomGuard(room);
        try
        {
            var ready = false;
            var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
            driver.HealthyFrameReady += _ => ready = true;
            host.RestoreLocalRoom();
            Wait(() => ready, "the restored room must really render while its checkpoint still holds the catalog lock");
            Assert.IsTrue(host.LocalHandoffPending, "A live first frame must wait for our own write instead of treating its lock as a room change.");
            Assert.IsNull(host.LocalRoomFailure);
            if (cancel) host.HideLocalRoom();
            held.Dispose(); held = null;
            pendingSave.SetResult();
            if (cancel)
            {
                Wait(() => Field<Task>(host, "roomCheckpointTask").IsCompleted, "cancelled restore's queued checkpoint");
                var barrier = home.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                Wait(() => barrier.Task.IsCompleted, "drain the queued restore continuation before checking that cancellation remains effective");
                Assert.AreEqual(LocalRoomPresentation.Hidden, host.LocalPresentation);
                Assert.IsFalse(host.LocalHandoffPending);
                Assert.IsFalse(host.LocalWindow!.IsVisible);
                Assert.IsFalse(driver.OwnsRuntime, "A late save completion cannot revive the cancelled frame owner.");
            }
            else
            {
                Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Window,
                    "admission must complete after the actual shared catalog guard is released");
                Assert.IsNull(host.LocalRoomFailure);
                Assert.IsTrue(driver.OwnsRuntime);
                Return(host, home, id);
            }
        }
        finally { held?.Dispose(); pendingSave.TrySetResult(); }
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void RealRoomRoundTripsKeepNamesOwnersPositionsAndRestartSelectionSeparated(bool migrateLegacy)
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "ordinary-room-three-slots", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "settings.json");
        var appearanceA = DangoAppearanceConfiguration.Default.WithBodyColor(OpaqueSrgbColor.Parse("#E8C080"));
        var appearanceB = DangoAppearanceConfiguration.Default.WithBodyColor(OpaqueSrgbColor.Parse("#80A0D0"));
        if (migrateLegacy) new AppearanceSettingsStore().Save(Path.Combine(root, "appearance.json"), appearanceA);
        var store = new DangoRosterStore(root);
        var roster = store.LoadOrInitialize().Snapshot!;
        roster = migrateLegacy
            ? Commit(store.Rename(roster, roster.SelectedId!.Value, "虚构团子甲", Guid.NewGuid()))
            : Commit(store.Create(roster, 0, "虚构团子甲", appearanceA, Guid.NewGuid()));
        var a = roster.SelectedId!.Value;
        roster = Commit(store.Create(roster, 2, "虚构团子乙", appearanceB, Guid.NewGuid()));
        var b = roster.SelectedId!.Value;
        Commit(store.Select(roster, a, Guid.NewGuid()));
        Guid room;
        byte[] aPosition;
        string aPath;
        using (var host = new DangoApplicationHost(settingsFile: settings))
        {
            var home = ShowHome(host);
            room = home.CatalogSnapshot!.Rooms.First(x => x.Life == LocalRoomLife.Active).RoomId;
            var catalog = new LocalRoomCatalogStore(root);
            var resume = new RoomResumeStore(catalog);
            var version = catalog.ReadLayout(room).LayoutVersion;
            var camera = new RoomCameraSnapshot(RoomCameraMode.Follow, RoomViewPreset.Standard, 100, 0, 0);
            Assert.AreEqual(FurnitureCommitState.Committed, resume.SaveForDango(room, a, version, new(500, 0), camera).State);
            Assert.AreEqual(FurnitureCommitState.Committed, resume.SaveForDango(room, b, version, new(1500, 0), camera).State);
            var aSeedVersion = resume.LoadForDango(room, a).Value!.ResumeVersion;
            var bSeedVersion = resume.LoadForDango(room, b).Value!.ResumeVersion;

            Enter(host, home, room, a, "虚构团子甲", appearanceA, 500);
            host.ShowControlCenter();
            Assert.IsTrue(home.IsDangoAway);
            Assert.IsFalse(Element<Button>(home, "DangoSlot3").IsEnabled);
            Element<Button>(home, "DangoSlot3").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.AreEqual(a, host.DangoRoster!.SelectedId);
            Assert.AreEqual(a, host.ActiveDangoId);
            home.ShowSettingsPage();
            Element<Button>(home, "BackFromSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsFalse(home.IsSettingsOpen);
            Assert.IsTrue(home.IsDangoAway, "Visiting settings cannot recall the Dango from its room.");
            Assert.AreEqual(a, host.ActiveDangoId);
            host.LocalWindow!.Scene.SelectView(RoomViewPreset.Near);
            Return(host, home, a);
            aPath = Path.Combine(root, $"device/rooms/{room:D}/dangos/{a:D}/resume.json");
            aPosition = File.ReadAllBytes(aPath);
            Assert.IsTrue(resume.LoadForDango(room, a).Value!.ResumeVersion > aSeedVersion,
                "A's actual room return must publish a new position checkpoint, not merely retain the seed.");
            Assert.AreEqual(RoomViewPreset.Near, resume.LoadForDango(room, a).Value!.Camera.Preset);

            host.ShowControlCenter();
            Wait(() => !home.IsInPorch && !home.IsDangoAway && Element<Button>(home, "DangoSlot3").IsEnabled, "safe home selection controls");
            Element<Button>(home, "DangoSlot3").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(() => host.DangoRoster!.SelectedId == b && home.BoundAppearance!.RequiresBodyPreview == false,
                "B's actual preview must be confirmed before selection is committed");
            Assert.AreEqual(appearanceB, home.BoundAppearance!.SavedAppearance);
            Assert.AreEqual("虚构团子乙", Element<Button>(home, "DangoNameButton").Content);
            Assert.AreEqual("+", Element<Button>(home, "DangoSlot2").Content);

            Enter(host, home, room, b, "虚构团子乙", appearanceB, 1500);
            Assert.AreEqual(RoomViewPreset.Near, host.LocalWindow!.Scene.Camera.Preset, "Camera preference is shared by the room.");
            Return(host, home, b);
            Assert.IsTrue(resume.LoadForDango(room, b).Value!.ResumeVersion > bSeedVersion,
                "B's actual room return must publish its own new position checkpoint.");
            CollectionAssert.AreEqual(aPosition, File.ReadAllBytes(aPath), "B's checkpoint must not rewrite A's position.");
            Assert.AreEqual(500d, resume.LoadForDango(room, a).Value!.Position!.X, 4d);
            Assert.AreEqual(1500d, resume.LoadForDango(room, b).Value!.Position!.X, 4d);
        }

        using (var restarted = new DangoApplicationHost(settingsFile: settings))
        {
            var home = ShowHome(restarted);
            Assert.AreEqual(b, restarted.DangoRoster!.SelectedId);
            Assert.AreEqual(appearanceB, home.BoundAppearance!.SavedAppearance);
            Assert.IsNull(restarted.ActiveDangoId);
            Assert.IsFalse(home.IsInPorch || home.IsDangoAway, "Restart must stay at home without automatically entering a room.");
            Enter(restarted, home, room, b, "虚构团子乙", appearanceB, 1500);
            Return(restarted, home, b);
            CollectionAssert.AreEqual(aPosition, File.ReadAllBytes(aPath));
        }
    }

    private static ProductHomeView ShowHome(DangoApplicationHost host)
    {
        host.ShowControlCenter();
        ProductHostTestSupport.WaitForHome(host);
        var home = Field<ControlCenterWindow>(host, "controlCenterWindow").ProductHome;
        home.ReduceMotion = true;
        return home;
    }

    private static void Enter(DangoApplicationHost host, ProductHomeView home, Guid room, Guid id, string name,
        DangoAppearanceConfiguration appearance, double x)
    {
        Element<Button>(home, "EnterRoomButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(() => home.IsInPorch && home.PorchScene.HasBeltBody, "real porch body before ordinary-room entry");
        host.EnterCatalogRoom(room, false);
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Window &&
            host.LocalWindow?.Scene.HasHealthyFrame == true, host.LocalRoomFailure ?? "healthy ordinary-room handoff");
        Assert.AreEqual(id, host.ActiveDangoId);
        Assert.AreEqual(name, host.LocalWindow!.Scene.RenderedDangoName);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.AreEqual(appearance, runtime.EffectiveAppearance);
        Assert.AreEqual(1, runtime.EntityIds.Count);
        Assert.IsTrue(Field<LocalRoomSceneDriver>(host, "localRoomDriver").OwnsRuntime);
        Assert.IsFalse(home.BoundBeltBody!.Metrics.Running);
        Wait(() => runtime.CurrentSelectedCenter is { } point && Math.Abs(point.X - x) < 4, "the selected Dango's safe saved position");
        Wait(() => Field<LocalRoomSceneDriver>(host, "localRoomDriver").CaptureCheckpoint()?.Position is not null,
            "actual floor support before saving a checkpoint");
    }

    private static void Return(DangoApplicationHost host, ProductHomeView home, Guid id)
    {
        var window = host.LocalWindow!;
        window.OpenMenu();
        Element<Button>(Field<LocalRoomMenuView>(window, "menu"), "RoomReturnButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(() => !host.LocalHandoffPending && host.LocalPresentation == LocalRoomPresentation.Porch && home.IsInPorch,
            host.LocalRoomFailure ?? "healthy porch return");
        Assert.IsNull(host.LocalRoomFailure);
        Assert.AreEqual(id, host.ActiveDangoId, "The identity remains locked while it is still in the porch.");
        var otherSlot = Element<Button>(home, host.DangoRoster!.Slots[0] == id ? "DangoSlot3" : "DangoSlot1");
        Assert.IsFalse(otherSlot.IsEnabled);
        otherSlot.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(id, host.DangoRoster.SelectedId, "Even a forced slot request cannot change identity in the porch.");
        Assert.AreEqual(id, host.ActiveDangoId);
        Assert.IsFalse(window.IsVisible);
        Assert.IsTrue(home.PorchScene.HasBeltBody);
        Assert.IsFalse(Field<LocalRoomSceneDriver>(host, "localRoomDriver").OwnsRuntime);
        Wait(() => Field<Task>(host, "roomCheckpointTask").IsCompleted, "serialized room checkpoint commit");
    }

    private static DangoRosterSnapshot Commit(DangoRosterWriteResult result)
    {
        Assert.AreEqual(FurnitureCommitState.Committed, result.State, result.Warning);
        return result.Snapshot!;
    }
    private static void Wait(Func<bool> condition, string reason) => ProductHostTestSupport.WaitFor(condition, reason);
    private static T Element<T>(FrameworkElement view, string name) where T : class => (T)view.FindName(name);
    private static T Field<T>(object value, string name) =>
        (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
}
