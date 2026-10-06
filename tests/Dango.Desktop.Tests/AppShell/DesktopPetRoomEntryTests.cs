using System.Collections;
using System.Reflection;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class DesktopPetRoomEntryTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [TestMethod]
    public void DesktopPetRoomHasItsOwnPresentationAndExplicitHostEntry()
    {
        var desktopPet = DesktopPetPresentation();
        Assert.AreNotEqual(LocalRoomPresentation.Window, desktopPet);
        Assert.AreNotEqual(LocalRoomPresentation.Wallpaper, desktopPet,
            "A desktop-pet room must not alias an ordinary room's wallpaper display.");
        Assert.IsNotNull(typeof(DangoApplicationHost).GetMethod("EnterDesktopPetRoom", Instance),
            "The desktop-pet door needs its own application entry.");
        Assert.IsNotNull(typeof(DangoApplicationHost).GetProperty("DesktopPetSelected", Instance),
            "Selection must remain observable after the room is hidden.");
    }

    [STATestMethod]
    public void LocalDoorsAreOptInAndCanBeEnabledIndependently()
    {
        var porch = new PorchSceneView();
        Assert.AreEqual(0, porch.DoorCount);
        EnableDesktopPet(porch, true);
        Assert.AreEqual(1, porch.DoorCount);
        CollectionAssert.AreEqual(new[] { "local-desktop-pet" }, DoorIds(porch));
        porch.SetLocalRoomEnabled(true);
        Assert.AreEqual(2, porch.DoorCount);
        CollectionAssert.AreEquivalent(new[] { "local-single", "local-desktop-pet" }, DoorIds(porch));
        EnableDesktopPet(porch, false);
        Assert.AreEqual(1, porch.DoorCount);
        CollectionAssert.AreEqual(new[] { "local-single" }, DoorIds(porch));
        porch.SetLocalRoomEnabled(false);
        Assert.AreEqual(0, porch.DoorCount);
    }

    [STATestMethod]
    public void NearestWTargetDispatchesOnlyItsOwnLocalRoomRequest()
    {
        var porch = TwoLocalDoors();
        var ordinaryRequests = 0;
        var desktopPetRequests = 0;
        porch.LocalRoomRequested += () => ordinaryRequests++;
        Subscribe(porch, "DesktopPetRoomRequested", () => desktopPetRequests++);
        porch.ResetProjection(560);
        Assert.AreEqual("local-single", porch.NearbyDoorId);
        Assert.IsTrue(porch.InteractNearestDoor());
        Assert.AreEqual(1, ordinaryRequests);
        Assert.AreEqual(0, desktopPetRequests);
        porch.ResetProjection(780);
        Assert.AreEqual("local-desktop-pet", porch.NearbyDoorId);
        Assert.IsTrue(porch.InteractNearestDoor());
        Assert.AreEqual(1, ordinaryRequests);
        Assert.AreEqual(1, desktopPetRequests);
        Assert.AreEqual("local-desktop-pet", porch.SelectedDoorId);
        Assert.IsFalse(porch.CanEnterRoom, "Local navigation is not formal room admission.");
    }

    [STATestMethod]
    public void DesktopPetDoorClickUsesTheSameNearGateAsW()
    {
        var porch = TwoLocalDoors();
        var desktopPetRequests = 0;
        var ordinaryRequests = 0;
        Subscribe(porch, "DesktopPetRoomRequested", () => desktopPetRequests++);
        porch.LocalRoomRequested += () => ordinaryRequests++;
        porch.ResetProjection(560);
        Assert.IsFalse(SelectDoor(porch, "local-desktop-pet"),
            "Clicking a distant desktop-pet door cannot bypass walking near it.");
        Assert.AreEqual(0, desktopPetRequests);
        Assert.AreEqual(0, ordinaryRequests);
        porch.ResetProjection(780);
        Assert.IsTrue(SelectDoor(porch, "local-desktop-pet"));
        Assert.AreEqual(1, desktopPetRequests);
        Assert.AreEqual(0, ordinaryRequests);
        Assert.IsFalse(porch.CanEnterRoom);
    }

    [STATestMethod]
    [DataRow(648d, true)]
    [DataRow(647d, false)]
    [DataRow(912d, true)]
    [DataRow(913d, false)]
    public void DesktopPetDoorEnforcesTheRealInteractionDistance(double petX, bool accepted)
    {
        var porch = TwoLocalDoors();
        var requests = 0;
        Subscribe(porch, "DesktopPetRoomRequested", () => requests++);
        porch.ResetProjection(petX);
        Assert.AreEqual(accepted, SelectDoor(porch, "local-desktop-pet"));
        Assert.AreEqual(accepted ? 1 : 0, requests);
        Assert.IsFalse(porch.CanEnterRoom);
    }

    [STATestMethod]
    public void ReviewFixturesNeverDispatchRealLocalRoomRequests()
    {
        var porch = TwoLocalDoors();
        var ordinaryRequests = 0;
        var desktopPetRequests = 0;
        porch.LocalRoomRequested += () => ordinaryRequests++;
        Subscribe(porch, "DesktopPetRoomRequested", () => desktopPetRequests++);
        porch.SetReviewMode(true);
        Assert.AreEqual(4, porch.DoorCount);
        porch.ResetProjection(560);
        Assert.IsTrue(porch.InteractNearestDoor());
        Assert.AreEqual("preview-own", porch.SelectedDoorId);
        Assert.AreEqual(0, ordinaryRequests);
        Assert.AreEqual(0, desktopPetRequests);
        Assert.IsFalse(porch.CanEnterRoom);
        porch.SetReviewMode(false);
        Assert.AreEqual(2, porch.DoorCount);
    }

    [STATestMethod]
    public void ProductHomeForwardsDesktopPetRequestWithoutFabricatingAuthority()
    {
        var home = new ProductHomeView();
        typeof(ProductHomeView).GetMethod("EnableLocalRoomDoor", Instance)!.Invoke(home, null);
        var enable = typeof(ProductHomeView).GetMethod("EnableDesktopPetRoomDoor", Instance);
        Assert.IsNotNull(enable, "Product Home must wire the independent desktop-pet door.");
        enable.Invoke(home, null);
        var requests = 0;
        Subscribe(home, "DesktopPetRoomRequested", () => requests++);
        var porch = (PorchSceneView)home.FindName("PorchScene");
        Assert.AreEqual(2, porch.DoorCount);
        porch.ResetProjection(780);
        Assert.IsTrue(porch.InteractNearestDoor());
        Assert.AreEqual(1, requests);
        Assert.IsNull(home.RoomNavigation.Authority.RoomId);
        Assert.IsFalse(home.RoomNavigation.CanSendRoomInput,
            "A local desktop-pet request cannot unlock formal shared-room input.");
    }

    [TestMethod]
    public void DesktopPetHandoffRequiresItsCurrentHealthyFrameAndRejectsStaleCallbacks()
    {
        var desktopPet = DesktopPetPresentation();
        var gate = new LocalRoomHandoffGate();
        var first = gate.Begin(desktopPet);
        Assert.IsFalse(gate.TryComplete(first, desktopPet, false));
        Assert.IsFalse(gate.TryComplete(first, LocalRoomPresentation.Wallpaper, true));
        Assert.AreEqual(LocalRoomPresentation.Porch, gate.Current);
        Assert.IsTrue(gate.Pending);
        gate.Hide();
        Assert.IsFalse(gate.TryComplete(first, desktopPet, true));
        var second = gate.Begin(desktopPet);
        Assert.IsFalse(gate.TryComplete(first, desktopPet, true));
        Assert.IsTrue(gate.TryComplete(second, desktopPet, true));
        Assert.AreEqual(desktopPet, gate.Current);
        Assert.IsFalse(gate.TryComplete(second, desktopPet, true));
    }

    private static LocalRoomPresentation DesktopPetPresentation()
    {
        Assert.IsTrue(Enum.TryParse<LocalRoomPresentation>("DesktopPet", out var result),
            "DesktopPet must be a distinct local presentation before its door can be implemented.");
        return result;
    }

    private static void EnableDesktopPet(PorchSceneView porch, bool enabled)
    {
        var method = typeof(PorchSceneView).GetMethod("SetDesktopPetRoomEnabled", Instance);
        Assert.IsNotNull(method, "The desktop-pet room needs its own explicit door enablement.");
        method.Invoke(porch, [enabled]);
    }

    private static PorchSceneView TwoLocalDoors()
    {
        var porch = new PorchSceneView();
        porch.SetLocalRoomEnabled(true);
        EnableDesktopPet(porch, true);
        return porch;
    }

    private static void Subscribe(object source, string eventName, Action callback)
    {
        var info = source.GetType().GetEvent(eventName, Instance);
        Assert.IsNotNull(info, $"Missing independent request event: {eventName}.");
        info.GetAddMethod(true)!.Invoke(source, [callback]);
    }

    private static object[] Doors(PorchSceneView porch) => ((IEnumerable)typeof(PorchSceneView)
        .GetProperty("VisibleDoors", Instance)!.GetValue(porch)!).Cast<object>().ToArray();

    private static string[] DoorIds(PorchSceneView porch) => Doors(porch)
        .Select(door => (string)door.GetType().GetProperty("Id", Instance)!.GetValue(door)!).ToArray();

    private static bool SelectDoor(PorchSceneView porch, string id)
    {
        var door = Doors(porch).Single(item => (string)item.GetType().GetProperty("Id", Instance)!.GetValue(item)! == id);
        return (bool)typeof(PorchSceneView).GetMethod("SelectPreviewDoor", Instance)!.Invoke(porch, [door])!;
    }
}
