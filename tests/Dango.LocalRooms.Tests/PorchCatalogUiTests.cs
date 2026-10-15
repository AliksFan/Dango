using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.LocalRooms.Tests;

[TestClass]
public sealed class PorchCatalogUiTests
{
    [TestMethod]
    public void ATargetRoomsSuccessCannotClearAnotherRoomsCheckpointWarning()
        => Sta(() =>
        {
            using var rig = new Rig(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
            var notice = (Button)rig.Home.FindName("NoticeButton");
            rig.Home.SetRoomCheckpointSaveStatus(a, "虚构甲房的位置未保存");
            rig.Home.SetNotice(null, null);
            rig.Home.SetRoomCheckpointSaveStatus(b, null);
            Assert.AreEqual(Visibility.Visible, notice.Visibility);
            StringAssert.Contains((string)notice.ToolTip, "虚构甲房");
            rig.Home.SetRoomCheckpointSaveStatus(a, null);
            Assert.AreEqual(Visibility.Collapsed, notice.Visibility);
        });

    [TestMethod]
    public void SceneEntrancesCreateCancelAndModalBackgroundGuardUseRealCatalog()
        => Sta(() =>
        {
            using var rig = new Rig(); var before = File.ReadAllBytes(rig.CatalogPath);
            Assert.AreEqual(1, rig.Home.CatalogSnapshot!.ActiveCount);
            rig.Home.PorchScene.ResetProjection(780); Assert.IsTrue(rig.Home.PorchScene.InteractNearestDoor());
            Assert.IsTrue(rig.Home.IsRoomDialogOpen); Assert.IsFalse(rig.Home.PorchScene.InteractNearestDoor(), "A modal must block the underlying scene.");
            FindButton(rig.Home.RoomDialog!, "取消").RaiseEvent(new(Button.ClickEvent));
            Assert.IsFalse(rig.Home.IsRoomDialogOpen); CollectionAssert.AreEqual(before, File.ReadAllBytes(rig.CatalogPath));
            rig.Home.PorchScene.InteractNearestDoor();
            FindButton(rig.Home.RoomDialog!, "创建").RaiseEvent(new(Button.ClickEvent));
            Pump(() => !rig.Home.IsRoomDialogOpen);
            Assert.AreEqual(2, rig.Home.CatalogSnapshot!.ActiveCount); Assert.AreEqual(2, rig.Home.PorchScene.CatalogOrdinaryCount);
            Assert.AreEqual(0, rig.Entries, "Creating a room must never auto-enter it.");
        });
    [TestMethod]
    public void SceneDissolutionRequiresTwoConfirmationsAndRecoveryDoesNotEnter()
        => Sta(() =>
        {
            using var rig = new Rig(); var id = rig.Home.CatalogSnapshot!.Rooms.Single().RoomId;
            var before = File.ReadAllBytes(rig.Store.LayoutPath(id));
            rig.Home.PorchScene.ResetProjection(560);
            Assert.AreEqual(id.ToString("D"), rig.Home.PorchScene.NearbyDoorId);
            FindButton(rig.Home.PorchScene, "解散普通房").RaiseEvent(new(Button.ClickEvent));
            Assert.IsTrue(rig.Home.IsRoomDialogOpen); Assert.AreEqual(1, rig.Store.ReadCurrent().ActiveCount);
            FindButton(rig.Home.RoomDialog!, "继续确认").RaiseEvent(new(Button.ClickEvent));
            Assert.AreEqual(1, rig.Store.ReadCurrent().ActiveCount);
            FindButton(rig.Home.RoomDialog!, "× 取消").RaiseEvent(new(Button.ClickEvent));
            Assert.AreEqual(1, rig.Store.ReadCurrent().ActiveCount);
            FindButton(rig.Home.PorchScene, "解散普通房").RaiseEvent(new(Button.ClickEvent));
            FindButton(rig.Home.RoomDialog!, "继续确认").RaiseEvent(new(Button.ClickEvent));
            FindButton(rig.Home.RoomDialog!, "确认解散").RaiseEvent(new(Button.ClickEvent)); Pump(() => !rig.Home.IsRoomDialogOpen);
            Assert.AreEqual(0, rig.Home.CatalogSnapshot!.ActiveCount); CollectionAssert.AreEqual(before, File.ReadAllBytes(rig.Store.LayoutPath(id)));
            rig.Home.PorchScene.ResetProjection(1440); rig.Home.PorchScene.InteractNearestDoor();
            FindButton(rig.Home.RoomDialog!, "恢复普通房").RaiseEvent(new(Button.ClickEvent)); Pump(() => !rig.Home.IsRoomDialogOpen);
            Assert.AreEqual(1, rig.Home.CatalogSnapshot.ActiveCount); Assert.AreEqual(id, rig.Home.CatalogSnapshot.Rooms.Single().RoomId);
            Assert.AreEqual(0, rig.Entries);
        });
    [TestMethod]
    public void SelectedSceneEntryUsesTheTargetIdentityAndFurnitureCannotLeakThroughModal()
        => Sta(() =>
        {
            using var rig = new Rig(); var id = rig.Home.CatalogSnapshot!.Rooms.Single().RoomId;
            Guid? entered = null; rig.Home.CatalogRoomRequested += (room, _) => entered = room;
            rig.Home.PorchScene.ResetProjection(560); rig.Home.PorchScene.InteractNearestDoor(); Assert.AreEqual(id, entered);
            rig.Home.PorchScene.ResetProjection(780); rig.Home.PorchScene.InteractNearestDoor();
            Assert.IsFalse(rig.Home.PorchScene.RequestNearbyFurniture()); Assert.AreEqual(1, rig.Entries);
        });
    internal static Button FindButton(DependencyObject root, string name)
    {
        (root as FrameworkElement)?.UpdateLayout();
        return Walk(root).OfType<Button>().Single(button => AutomationProperties.GetName(button) == name || Equals(button.Content, name));
    }
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    internal static void Pump(Func<bool> completed)
    {
        var timer = Stopwatch.StartNew();
        while (!completed() && timer.Elapsed < TimeSpan.FromSeconds(12))
        {
            var frame = new DispatcherFrame();
            _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        Assert.IsTrue(completed(), "Timed out waiting for the actual async UI/storage result.");
    }
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "UI test did not terminate.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class Rig : IDisposable
    {
        internal LocalRoomCatalogStore Store { get; }
        internal ProductHomeView Home { get; } = new() { ReduceMotion = true };
        internal int Entries { get; private set; }
        internal string CatalogPath => Path.Combine(Store.DataRoot, LocalRoomCatalogStore.CatalogPath);
        private readonly Window window;
        internal Rig()
        {
            Store = new(Path.Combine(AppContext.BaseDirectory, "artifacts", "test-runs", "porch-catalog", Guid.NewGuid().ToString("N")));
            window = new Window { Width = 960, Height = 640, Content = Home, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000 };
            window.Show(); var load = Home.BindRoomCatalogAsync(Store); Pump(() => load.IsCompleted); load.GetAwaiter().GetResult();
            Home.ResumePorch(); window.UpdateLayout();
            Home.CatalogRoomRequested += (_, _) => Entries++;
        }
        public void Dispose() { if (Home.IsRoomDialogOpen) Home.CloseRoomDialog(); window.Close(); }
    }
}
