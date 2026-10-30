using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.AppShell.Rooms.Storage;
using Button = System.Windows.Controls.Button;
using StackPanel = System.Windows.Controls.StackPanel;
using Size = System.Windows.Size;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class PorchDoorInteractionTests
{
    [STATestMethod]
    public void PreviewModeHidesCatalogActionsAndRestoresThemOnExit()
    {
        var scene = Scene(1);
        scene.SetReviewMode(true); scene.ResetProjection(560);
        Assert.AreEqual(Visibility.Collapsed, Actions(scene).Visibility);
        Assert.AreEqual(Visibility.Collapsed, Tools(scene).Visibility);
        scene.SetReviewMode(false);
        Assert.AreEqual(Visibility.Visible, Actions(scene).Visibility);
        Assert.AreEqual(Visibility.Visible, Tools(scene).Visibility);
    }

    [STATestMethod]
    public void LegacySingleRoomOriginRemainsReachableAndCombinedFallbackPutsDesktopFirst()
    {
        var scene = new PorchSceneView(); scene.SetLocalRoomEnabled(true);
        Assert.AreEqual("local-single", scene.NearbyDoorId);
        Assert.IsTrue(scene.InteractNearestDoor());
        scene.SetDesktopPetRoomEnabled(true);
        Assert.AreEqual("local-desktop-pet", scene.NearbyDoorId);
        scene.ResetProjection(780);
        Assert.AreEqual("local-single", scene.NearbyDoorId);
    }

    [STATestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    public void CatalogOrdersDesktopThenExistingRoomsAndOneCreationDoor(int count)
    {
        var scene = Scene(count);
        Assert.AreEqual(count + 2, scene.DoorCount);
        Assert.AreEqual("local-desktop-pet", scene.NearbyDoorId);
        for (var i = 0; i < count; i++)
        {
            scene.ResetProjection(780 + 220 * i);
            Assert.AreEqual(RoomId(i).ToString("D"), scene.NearbyDoorId);
        }
        scene.ResetProjection(780 + count * 220);
        Assert.AreEqual("new-room", scene.NearbyDoorId);
        var create = Actions(scene).Children.OfType<Button>().Single();
        Assert.AreEqual(count < 3, create.IsEnabled);
        var calls = 0;
        scene.CatalogCreateRequested += () => calls++;
        Click(create);
        Assert.AreEqual(count < 3 ? 1 : 0, calls);
        Assert.AreEqual(count < 3, scene.InteractNearestDoor());
        Assert.AreEqual(count < 3 ? 2 : 0, calls);
    }

    [STATestMethod]
    public void DoorGeometryCannotBeClickedAndBackgroundOnlyRequestsFocus()
    {
        var scene = Scene(1);
        scene.ResetProjection(780);
        var calls = 0; var focus = 0;
        scene.CatalogRoomRequested += _ => calls++;
        scene.GameplayFocusRequested += () => focus++;
        Assert.AreEqual(4, Buttons(scene).Count(), "Only explicit actions and one tool may be hit targets.");
        typeof(PorchSceneView).GetMethod("OnMouseLeftButtonDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(scene, [new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent }]);
        Assert.AreEqual(1, focus);
        Assert.AreEqual(0, calls);
        Assert.IsNull(scene.SelectedDoorId);
        Click(Action(scene, "进入"));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(RoomId(0).ToString("D"), scene.SelectedDoorId);
        Assert.IsTrue(scene.InteractNearestDoor());
        Assert.AreEqual(2, calls);
    }

    [STATestMethod]
    public void ExplicitActionsAndKeyboardRespectBlockFaultAndOfflineQualifications()
    {
        var scene = Scene(1);
        scene.ResetProjection(780);
        var enter = Action(scene, "进入"); var edit = Action(scene, "布置"); var dissolve = Action(scene, "解散");
        var calls = 0;
        scene.CatalogRoomRequested += _ => calls++;
        scene.CatalogFurnitureRequested += _ => calls++;
        scene.CatalogDissolveRequested += _ => calls++;
        Click(edit); Click(dissolve);
        Assert.AreEqual(2, calls);
        scene.CatalogInputBlocked = true;
        Click(enter); Click(edit); Click(dissolve);
        Assert.IsFalse(scene.InteractNearestDoor()); Assert.IsFalse(scene.RequestNearbyFurniture());
        Assert.AreEqual(2, calls);
        scene.CatalogInputBlocked = false;
        scene.ShowUnavailable();
        Click(enter); Click(edit); Click(dissolve);
        Assert.IsFalse(scene.InteractNearestDoor()); Assert.IsFalse(scene.RequestNearbyFurniture());
        Assert.AreEqual(2, calls);

        var cached = Record(0) with { Source = LocalRoomSource.ServerCache, ServerRoomId = Guid.NewGuid(), ServerVersion = 1,
            Access = RoomAccessEvidence.Unopened with { PendingInvitation = true } };
        scene = Scene(0); scene.SetCatalog(Catalog([cached])); scene.ResetProjection(780);
        Assert.IsFalse(Action(scene, "布置").IsEnabled); Assert.IsFalse(Action(scene, "解散").IsEnabled);
        calls = 0; scene.CatalogFurnitureRequested += _ => calls++; scene.CatalogDissolveRequested += _ => calls++;
        Click(Action(scene, "布置")); Click(Action(scene, "解散"));
        Assert.IsFalse(scene.RequestNearbyFurniture()); Assert.AreEqual(0, calls);
    }

    [STATestMethod]
    public void RecoveryToolDoesNotNeedDoorProximityAndProtectedCatalogStillAllowsDesktop()
    {
        var scene = Scene(0); scene.ResetProjection(100);
        var calls = 0; scene.CatalogRecoveryRequested += () => calls++;
        Click(Tools(scene).Children.OfType<Button>().Single()); Assert.AreEqual(1, calls);
        scene.CatalogInputBlocked = true; Click(Tools(scene).Children.OfType<Button>().Single()); Assert.AreEqual(1, calls);
        scene.CatalogInputBlocked = false; scene.SetCatalog(null);
        scene.CatalogReloadRequested += () => calls++;
        Click(Tools(scene).Children.OfType<Button>().Single()); Assert.AreEqual(2, calls);
        scene.ResetProjection(780); Assert.IsFalse(scene.InteractNearestDoor());
        scene.ResetProjection(560);
        scene.DesktopPetRoomRequested += () => calls++;
        Click(Action(scene, "进入")); Assert.AreEqual(3, calls);
    }

    [STATestMethod]
    [DataRow(960d, 540d)]
    [DataRow(1280d, 720d)]
    [DataRow(520d, 820d)]
    public void ActionRowAndRecoveryStayInsideHeadlessLayouts(double width, double height)
    {
        var scene = Scene(3);
        scene.Measure(new Size(width, height)); scene.Arrange(new Rect(0, 0, width, height)); scene.UpdateLayout();
        scene.ResetProjection(780);
        scene.Measure(new Size(width, height)); scene.Arrange(new Rect(0, 0, width, height)); scene.UpdateLayout();
        foreach (var door in scene.DoorBounds)
        {
            Assert.AreEqual(194 * scene.SceneScale, door.Height, .001,
                "At supported viewport sizes the original full-height doors fit; tool spacing must not shorten them.");
            Assert.AreEqual(scene.BackWallGroundY, door.Bottom, .001, "Doors keep their original ground baseline.");
        }
        var rows = new[] { Actions(scene), Tools(scene) };
        var rectangles = rows.Select(row => new Rect(row.TranslatePoint(new System.Windows.Point(), scene), new Size(row.ActualWidth, row.ActualHeight))).ToArray();
        Assert.IsFalse(rectangles[0].IntersectsWith(rectangles[1]), "Recovery tools must not cover nearby door actions.");
        foreach (var row in rows)
        {
            var origin = row.TranslatePoint(new System.Windows.Point(), scene);
            Assert.IsTrue(origin.X >= 0 && origin.Y >= 0);
            Assert.IsTrue(origin.X + row.ActualWidth <= width + .1);
            Assert.IsTrue(origin.Y + row.ActualHeight <= height + .1);
        }
    }

    private static PorchSceneView Scene(int count)
    {
        var scene = new PorchSceneView(); scene.SetCatalog(Catalog(Enumerable.Range(0, count).Select(Record).ToArray()));
        scene.Measure(new Size(960, 540)); scene.Arrange(new Rect(0, 0, 960, 540)); scene.ResetProjection(560);
        return scene;
    }
    private static LocalRoomCatalog Catalog(LocalRoomRecord[] rooms) => new(1, Guid.NewGuid(), 1, Guid.NewGuid(), new string('a', 64), rooms, null);
    private static Guid RoomId(int index) => Guid.Parse($"00000000-0000-0000-0000-{index + 1:000000000000}");
    private static LocalRoomRecord Record(int index) => new(RoomId(index), "虚构普通房" + index, LocalRoomKind.Private, 1,
        DateTimeOffset.UnixEpoch, LocalRoomSource.LocalOnly, null, null, RoomAccessEvidence.Unopened, LocalRoomLife.Active, null, null);
    private static StackPanel Actions(PorchSceneView scene) => Field<StackPanel>(scene, "catalogActions");
    private static StackPanel Tools(PorchSceneView scene) => Field<StackPanel>(scene, "catalogControls");
    private static T Field<T>(object value, string field) => (T)value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static Button Action(PorchSceneView scene, string prefix) => Actions(scene).Children.OfType<Button>()
        .Single(button => AutomationProperties.GetName(button).StartsWith(prefix, StringComparison.Ordinal));
    private static IEnumerable<Button> Buttons(PorchSceneView scene) => Actions(scene).Children.OfType<Button>().Concat(Tools(scene).Children.OfType<Button>());
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
