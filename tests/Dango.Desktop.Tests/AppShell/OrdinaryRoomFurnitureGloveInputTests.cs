using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.Rendering.Visuals;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class OrdinaryRoomFurnitureGloveInputTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void RuntimeCancellationEndsDriverGloveReleasesCaptureAndRejectsLateMove()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "furniture-room", "glove-input", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"),
            nativeLibraryPath: Environment.GetEnvironmentVariable("DANGO_ORDINARY_FURNITURE_TEST_MODULE") ??
                throw new InvalidOperationException("The real candidate furniture module is required."), isolatedLocalRoomReview: true);
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        host.ShowControlCenter(); Pump(250);
        host.EnterLocalRoom(); Until(() => !host.LocalHandoffPending, 4500);
        var window = host.LocalWindow!;
        var driver = Field<LocalRoomSceneDriver>(host, "localRoomDriver");
        Assert.AreEqual(LocalRoomPresentation.Window, host.LocalPresentation, host.LocalRoomFailure);
        Assert.IsTrue(driver.IsReady);
        window.FocusScene(); Pump(50);
        Assert.IsTrue(window.BeginFurnitureEditing(), runtime.Furniture?.Status);
        var scene = window.Scene;
        Assert.IsNotNull(scene.PetScreenBounds);
        var painted = scene.PetScreenBounds.Value;
        var hit = new Point(painted.Center.X, painted.Center.Y);
        Assert.IsTrue(scene.IsPetHit(hit), "The test must start on the actual displayed room body.");
        var entity = runtime.EntityIds.Single();
        var generation = runtime.Furniture!.WorldGeneration;
        var physicsFrame = runtime.LastPhysicsFrame;
        Assert.IsTrue(driver.BeginFurnitureGlove(hit), runtime.Furniture.Status);
        Assert.IsTrue(Field<bool>(driver, "furnitureGloveActive"));
        Assert.IsTrue(runtime.IsDragging);
        Assert.IsTrue(scene.CaptureMouse(), "The existing room scene must own the real WPF gesture capture.");
        Assert.AreSame(scene, Mouse.Captured);
        Assert.IsTrue(driver.MoveFurnitureGlove(new(hit.X + 16, hit.Y - 12)));
        var cancellations = 0;
        driver.PointerCancelled += () => cancellations++;

        runtime.CancelInput();

        Assert.AreEqual(1, cancellations, "Runtime cancellation must notify the host so it releases its scene capture.");
        Assert.IsFalse(Field<bool>(driver, "furnitureGloveActive"), "The driver cannot retain a glove after the native input owner cancelled it.");
        Assert.IsFalse(Field<bool>(runtime, "ordinaryFurnitureGlove"));
        Assert.IsFalse(runtime.IsDragging);
        Assert.AreNotSame(scene, Mouse.Captured);
        var targetAfterCancellation = Field<LogicalPoint>(runtime, "gloveRequestedNative");
        Assert.IsFalse(driver.MoveFurnitureGlove(new(hit.X + 120, hit.Y - 60)), "A queued pointer move cannot revive the cancelled gesture.");
        Assert.AreEqual(targetAfterCancellation, Field<LogicalPoint>(runtime, "gloveRequestedNative"), "Late pointer motion must not inject another native target.");
        Assert.IsFalse(driver.EndFurnitureGlove(hit));
        Assert.AreEqual(entity, runtime.EntityIds.Single());
        Assert.AreEqual(generation, runtime.Furniture.WorldGeneration);
        Assert.AreSame(physicsFrame, runtime.LastPhysicsFrame, "Cancellation and a rejected queued move cannot hide a physics step or recreate the body.");
        Assert.AreSame(scene, window.Scene);
        host.ReturnToPorch(); Until(() => !host.LocalHandoffPending, 4500);
        Assert.AreEqual(LocalRoomPresentation.Porch, host.LocalPresentation, host.LocalRoomFailure);
    }

    private static T Field<T>(object instance, string name) =>
        (T)instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;
    private static void Until(Func<bool> condition, int timeout)
    {
        var clock = Stopwatch.StartNew();
        do { Pump(30); } while (!condition() && clock.ElapsedMilliseconds < timeout);
        Assert.IsTrue(condition(), "The real product-room host did not reach readiness before its bounded deadline.");
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
