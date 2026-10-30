using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms.Storage;

namespace Dango.Tests.Support;

internal static class ProductHostTestSupport
{
    // Wait for the actual worker acknowledgement and catalog, not a fixed UI delay.
    // This helper never opens or activates a window.
    internal static void WaitForHome(DangoApplicationHost host)
    {
        var window = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetField("controlCenterWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var home = window.ProductHome;
        var clock = Stopwatch.StartNew();
        while ((home.DisplayedBeltSnapshot is not { Error: null, Frame: not null } ||
                home.BoundAppearance?.RequiresBodyPreview != false || home.CatalogSnapshot is null) && clock.ElapsedMilliseconds < 10000)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        Assert.IsNotNull(home.DisplayedBeltSnapshot?.Frame, home.ReadBeltBodySettingsScene().Diagnostics);
        Assert.IsNull(home.DisplayedBeltSnapshot.Error);
        Assert.IsFalse(home.BoundAppearance!.RequiresBodyPreview);
        Assert.IsNotNull(home.CatalogSnapshot, "The real catalog must finish before requesting a room.");
        Assert.IsTrue(home.CatalogSnapshot.ActiveCount > 0, "The real catalog must provide a saved active room before entry.");
    }

    // Position the real porch fixture at the catalog door and wait for its actual
    // healthy presentation acknowledgement before invoking the normal door action.
    internal static void ResumePorchAtCatalogRoomDoor(ProductHomeView home, Guid roomId)
    {
        var rooms = home.CatalogSnapshot?.Rooms.Where(room => room.Life == LocalRoomLife.Active).ToArray()
            ?? throw new AssertFailedException("The real room catalog must be ready before selecting a door.");
        var index = Array.FindIndex(rooms, room => room.RoomId == roomId);
        Assert.IsTrue(index >= 0, $"Room {roomId:D} must be an active room in the real catalog.");

        typeof(ProductHomeView).GetField("porchResumeX", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(home, 780 + index * 220d);
        var ready = false;
        void OnReady(long generation) { if (generation == 1) ready = true; }
        home.HealthyPorchFrameReady += OnReady;
        try
        {
            home.ResumePorch(generation: 1);
            WaitFor(() => ready, "The real porch must present a healthy belt frame before furniture entry.");
        }
        finally { home.HealthyPorchFrameReady -= OnReady; }

        Assert.IsTrue(home.PorchScene.HasBeltBody, "The real porch body must be presented before furniture entry.");
        Assert.AreEqual(roomId.ToString("D"), home.PorchScene.NearbyDoorId,
            "The presented body must be within the selected catalog room door's interaction range.");
    }
    internal static void WaitFor(Func<bool> condition, string reason)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < 10000)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        Assert.IsTrue(condition(), reason);
    }
}
