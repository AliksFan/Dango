using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class DesktopPetAccessoryIntegrationTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualDesktopPetWindowConsumesAccessoryRuntimeAndReplacesLegacyHat()
    {
        using var rig = new Fixture(stableViewport: false);
        var outputs = rig.Runtime.GetAccessoryFrames(rig.Frame, 0);
        Assert.HasCount(2, outputs, "The native fixture must have real front and rear accessory outputs.");
        var visuals = rig.Accessories;
        Assert.HasCount(2, visuals,
            "The visible desktop-pet window must bind the same runtime as its hidden room scene.");
        Assert.IsTrue(visuals.All(visual => visual.IsVisible && visual.Frame is not null && visual.CaptureBitmap() is not null));
        Assert.AreEqual(Visibility.Collapsed, rig.Hat.Visibility,
            "The customized host must suppress its legacy hat when its accessory layers are active.");
        var rear = visuals.Single(visual => visual.Frame!.Bounds.Width > 100);
        var front = visuals.Single(visual => !ReferenceEquals(visual, rear));
        Assert.IsTrue(Canvas.GetZIndex((UIElement)rear.Parent) < Canvas.GetZIndex(rig.Body));
        Assert.IsTrue(Canvas.GetZIndex((UIElement)front.Parent) > Canvas.GetZIndex(rig.Body));
        AssertReadOnlyAndBodyOnly(rig);

        rig.Runtime.SetAccessories(AccessoryConfiguration.Empty);
        rig.ReapplyCurrent();
        Assert.HasCount(0, rig.Accessories);
        Assert.AreEqual(Visibility.Collapsed, rig.Hat.Visibility,
            "An intentionally empty wearing list cannot resurrect the legacy hat.");
        rig.Runtime.SetAccessories(AccessoryConfiguration.Empty, enabled: false);
        rig.ReapplyCurrent();
        Assert.HasCount(0, rig.Accessories);
        Assert.AreEqual(Visibility.Visible, rig.Hat.Visibility,
            "Leaving the accessory model must restore the current legacy hat in the same frame.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void ExistingRasterOutsideBodyExpandsActualPaintedBounds(bool stableViewport)
    {
        using var rig = new Fixture(stableViewport);
        // Prime only the existing host layer so the red test can independently
        // expose the bounds omission even before the runtime argument is wired.
        rig.PrimeAccessoryBinding();
        var visual = rig.Accessories.OrderByDescending(value => value.DrawingBounds.Width).First();
        var expectedWorld = visual.TransformToAncestor(rig.Canvas).TransformBounds(visual.Frame!.Bounds);
        var first = rig.Scene.RoomToScene(new(expectedWorld.Left, expectedWorld.Top));
        var last = rig.Scene.RoomToScene(new(expectedWorld.Right, expectedWorld.Bottom));
        var expectedScene = new Rect(new Point(first.X, first.Y), new Point(last.X, last.Y));
        Assert.IsTrue(expectedScene.Width > 100, "The fixture must exercise an oversized real raster footprint.");
        var actualWindow = visual.TransformToAncestor(rig.Window).TransformBounds(visual.Frame!.Bounds);
        Assert.IsTrue(actualWindow.Left >= 0 && actualWindow.Top >= 0 &&
            actualWindow.Right <= rig.Window.ActualWidth && actualWindow.Bottom <= rig.Window.ActualHeight,
            $"Raster {actualWindow} must fit the actual display HWND {rig.Window.ActualWidth} x {rig.Window.ActualHeight}.");
        var point = rig.OpaqueAccessoryPointOutsideBody(visual);
        Assert.IsTrue(point.Window.X >= 0 && point.Window.Y >= 0 &&
            point.Window.X < rig.Window.ActualWidth && point.Window.Y < rig.Window.ActualHeight,
            "The actual HWND must contain the visible raster outside the body.");
        AssertReadOnlyAndBodyOnly(rig);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(false)]
    [DataRow(true)]
    public void ExistingRasterOutsideBodyIsIncludedInNativeDisplayRegionAndRemovedWhenUnequipped(bool stableViewport)
    {
        using var rig = new Fixture(stableViewport);
        rig.PrimeAccessoryBinding();
        var visual = rig.Accessories.OrderByDescending(value => value.DrawingBounds.Width).First();
        var point = rig.OpaqueAccessoryPointOutsideBody(visual);
        Assert.IsTrue(InOwnWindowRegion(rig.Window, point.Window),
            "A real opaque accessory pixel outside body/hat paths cannot be clipped by the native region.");
        Assert.IsFalse(rig.Window.HitTestScenePoint(point.Scene),
            "Including a picture in the display region cannot make it a body interaction target.");
        Assert.AreEqual((nint)(-1), SendMessage(rig.Window.Handle, 0x0084, 0, Pack(rig.Window.PointToScreen(point.Window))));
        var bodyPoint = rig.Scene.RoomToScene(DangoHostFrameBinding.Bounds(rig.Frame, 0).Center +
            new LogicalVector(0, rig.Runtime.NeutralBodyGroundOffset));
        Assert.IsTrue(rig.Window.HitTestScenePoint(new(bodyPoint.X, bodyPoint.Y)));

        rig.Runtime.SetAccessories(AccessoryConfiguration.Empty);
        rig.ReapplyCurrent();
        Assert.HasCount(0, rig.Accessories);
        Assert.IsFalse(InOwnWindowRegion(rig.Window, rig.Window.PointFromScreen(point.Screen)),
            "Taking off the item must remove its previous native display region.");
        AssertReadOnlyAndBodyOnly(rig);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void StableViewportDragKeepsCurrentWpfOpaquePixelsInsideNativeRegion()
    {
        using var rig = new Fixture(stableViewport: true, beltRoom: true);
        DesktopPetRoomFixture.WaitUntil(() => rig.Driver.IsReady, rig.Runtime);
        var center = DangoHostFrameBinding.Bounds(rig.Frame, 0).Center;
        var bodyCenter = rig.Scene.RoomToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var start = new Point(bodyCenter.X, bodyCenter.Y);
        Assert.IsTrue(rig.Driver.PointerDown(start), "The real desktop-pet body must accept an in-process drag start.");
        var checkedFrames = 0;
        var maximumLift = 0d;
        var previousSteps = rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount;
        try
        {
            for (var i = 1; i <= 24; i++)
            {
                var point = new Point(start.X + 100 * Math.Sin(i * Math.PI / 12), start.Y - 120 * Math.Sin(i * Math.PI / 24));
                Assert.IsTrue(rig.Driver.PointerMove(point), $"Drag move {i} must be accepted.");
                rig.WaitForNewFrame(previousSteps);
                Assert.IsTrue(rig.Window.HasHealthyFrame, rig.Window.LastFrameRejection);
                Assert.AreSame(rig.Frame, rig.Window.LastAppliedFrame, "The natural scene callback must update the pet window without a test-side reapply.");
                Assert.IsTrue(rig.Driver.IsPointerActive, $"Drag sample {i} must retain its live pointer gesture.");
                if (i >= 4) Assert.IsTrue(rig.Runtime.IsDragging, "The trajectory must exercise physical dragging rather than light interaction.");
                var steps = rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount;
                Assert.IsTrue(steps > previousSteps, $"Drag sample {i} must observe a newly simulated real frame.");
                previousSteps = steps;
                maximumLift = Math.Max(maximumLift, center.Y - DangoHostFrameBinding.Bounds(rig.Frame, 0).Center.Y);
                Assert.AreEqual(DangoContourFailure.None, rig.Body.LastContourFailure,
                    $"The real drag frame {i} must keep a valid body contour.");
                Assert.AreEqual(PhysicsPresentationFallbackReason.None, rig.Frame.Bodies.Single().FallbackReason,
                    $"The real drag frame {i} must not use the presentation fallback.");
                Assert.AreEqual(2, rig.Accessories.Length,
                    $"The real drag frame {i} must retain both accessory layers.");
                Assert.IsTrue(rig.Accessories.All(visual => visual.IsVisible && visual.Frame is not null && visual.CaptureBitmap() is not null),
                    $"The real drag frame {i} must retain rendered images in both layers.");
                var coverage = CountWpfOpaquePixelsOutsideNativeRegion(rig.Window);
                Assert.IsTrue(coverage.SampledOpaquePixels >= 1000,
                    $"Frame {i} needs a substantial WPF alpha sample; saw {coverage.SampledOpaquePixels} pixels.");
                Assert.IsTrue(coverage.ClippedOpaquePixels <= Math.Max(2, coverage.SampledOpaquePixels / 10000),
                    $"Frame {i} clipped {coverage.ClippedOpaquePixels}/{coverage.SampledOpaquePixels} opaque WPF pixels " +
                    "after excluding one pixel of antialiased boundary. Native region must cover the current WPF frame.");
                checkedFrames++;
            }
        }
        finally { rig.Driver.PointerUp(start); }
        Assert.AreEqual(24, checkedFrames, "Every actual stable-viewport drag frame must be checked.");
        Assert.IsTrue(maximumLift > 20, "The real body must leave the floor during the drag trajectory.");
    }

    private static (int SampledOpaquePixels, int ClippedOpaquePixels) CountWpfOpaquePixelsOutsideNativeRegion(DesktopPetDangoWindow window)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.AreNotEqual((nint)0, region);
        try
        {
            Assert.IsTrue(GetWindowRgn(window.Handle, region) > 0, "The owned HWND must expose its current native region.");
            var sampled = 0;
            var clipped = 0;
            // Erode the opaque alpha mask by one pixel so antialiasing and native
            // integer edge rounding cannot be mistaken for missing interior pixels.
            for (var y = 1; y < height - 1; y++)
                for (var x = 1; x < width - 1; x++)
                {
                    var interior = true;
                    for (var dy = -1; dy <= 1 && interior; dy++)
                        for (var dx = -1; dx <= 1; dx++)
                            if (pixels[((y + dy) * width + x + dx) * 4 + 3] < 250) { interior = false; break; }
                    if (!interior) continue;
                    sampled++;
                    if (!PtInRegion(region, x, y)) clipped++;
                }
            return (sampled, clipped);
        }
        finally { _ = DeleteObject(region); }
    }

    private static void AssertReadOnlyAndBodyOnly(Fixture rig)
    {
        var frame = rig.Runtime.LastFrame;
        var native = rig.Runtime.LastPhysicsFrame;
        var owner = rig.Runtime.FrameDriver;
        var count = rig.Runtime.EntityIds.Count;
        var steps = rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount;
        Assert.IsTrue(rig.Window.ApplyFrame(rig.Frame, rig.Runtime), rig.Window.LastFrameRejection);
        Assert.AreSame(frame, rig.Runtime.LastFrame);
        Assert.AreSame(native, rig.Runtime.LastPhysicsFrame);
        Assert.AreSame(owner, rig.Runtime.FrameDriver);
        Assert.AreEqual(count, rig.Runtime.EntityIds.Count);
        Assert.AreEqual(steps, rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount,
            "Consuming a raster in another host cannot tick the body or accessories.");
        var guard = rig.Window.CurrentPointerGuard ?? throw new AssertFailedException("The current frame must retain its body pointer guard.");
        var bounds = DangoHostFrameBinding.Bounds(rig.Frame, 0).Translate(new(0, rig.Runtime.NeutralBodyGroundOffset));
        var first = rig.Scene.RoomToScene(new(bounds.X, bounds.Y));
        var last = rig.Scene.RoomToScene(new(bounds.Right, bounds.Bottom));
        var expected = new Rect(rig.Scene.PointToScreen(new(first.X, first.Y)), rig.Scene.PointToScreen(new(last.X, last.Y)));
        Assert.AreEqual(expected.X, guard.ScreenBounds.X, 1);
        Assert.AreEqual(expected.Y, guard.ScreenBounds.Y, 1);
        Assert.AreEqual(expected.Width, guard.ScreenBounds.Width, 1);
        Assert.AreEqual(expected.Height, guard.ScreenBounds.Height, 1,
            "The pointer guard remains the original body bounds rather than the enlarged raster footprint.");
        var paintedBody = Rect.Empty;
        foreach (var path in DesktopPetRoomFixture.Descendants<System.Windows.Shapes.Path>(rig.Window))
            if (path.IsVisible && path.Fill is not null && path.Data is not null)
                paintedBody.Union(path.TransformToAncestor(rig.Canvas).TransformBounds(path.Data.Bounds));
        var bodyFirst = rig.Scene.RoomToScene(new(paintedBody.X - 2, paintedBody.Y - 2));
        var bodyLast = rig.Scene.RoomToScene(new(paintedBody.Right + 2, paintedBody.Bottom + 2));
        var dragBounds = rig.Scene.PetScreenBounds!.Value;
        Assert.AreEqual(bodyFirst.X, dragBounds.X, 1e-9);
        Assert.AreEqual(bodyFirst.Y, dragBounds.Y, 1e-9);
        Assert.AreEqual(bodyLast.X - bodyFirst.X, dragBounds.Width, 1e-9);
        Assert.AreEqual(bodyLast.Y - bodyFirst.Y, dragBounds.Height, 1e-9,
            "The scene's existing drag safety footprint must stay independent of accessory display bounds.");
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly StackedDangoSceneRuntime Runtime;
        internal readonly LocalRoomSceneView Scene = new() { Width = 1024, Height = 640 };
        internal readonly LocalRoomSceneDriver Driver;
        internal readonly Window SceneWindow;
        internal readonly DesktopPetDangoWindow Window;
        internal Canvas Canvas => DesktopPetRoomFixture.Field<Canvas>(Window, "canvas");
        internal DeformableDangoVisual Body => DesktopPetRoomFixture.Field<DeformableDangoVisual>(Window, "body");
        internal DangoHatVisual Hat => DesktopPetRoomFixture.Field<DangoHatVisual>(Window, "hat");
        internal Dango.Desktop.Physics.Presentation.StackedPhysicsPresentationFrame Frame => Scene.LastAppliedFrame!;
        internal AccessoryMeshVisual[] Accessories => DesktopPetRoomFixture.Descendants<AccessoryMeshVisual>(Window).ToArray();

        internal Fixture(bool stableViewport, bool beltRoom = false)
        {
            Runtime = beltRoom
                ? ProductPhysicsComposition.Default.CreateRoom()
                : StackedDangoSceneRuntime.CreateProduct();
            if (beltRoom) Runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default, beginRoom: true);
            var root = System.IO.Path.Combine(TestRepository.Root, "artifacts", "accessory-v1-desktop-pet", Guid.NewGuid().ToString("N"));
            var settings = new AccessorySettingsSession(root);
            var configuration = new AccessoryConfiguration([
                new("rear", "Fictional rear raster", "builtin-sprout", 64, 64)
                {
                    Layer = AccessoryLayer.Behind, BodyAnchorX = .5, BodyAnchorY = 0,
                    SizePercent = 350, ImagePivotX = .9, ImagePivotY = 1,
                    LockRotation = true, Softness = 0, GravityInfluence = 0,
                },
                new("front", "Fictional front raster", "builtin-bowler", 64, 64)
                {
                    Layer = AccessoryLayer.Front, BodyAnchorX = .5, BodyAnchorY = .2,
                    SizePercent = 70, LockRotation = true, Softness = 0, GravityInfluence = 0,
                },
            ]);
            settings.Stage(configuration);
            Runtime.BindAccessories(settings);
            Runtime.SetAccessories(configuration);
            Scene.EnableDesktopPetPresentation();
            SceneWindow = new Window
            {
                Content = Scene,
                Width = 1024,
                Height = 640,
                Left = -20000,
                Top = -20000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            SceneWindow.Show();
            SceneWindow.UpdateLayout();
            var first = Scene.PointToScreen(default(Point));
            var last = Scene.PointToScreen(new(1024, 640));
            Window = new(Scene, point => Scene.PointFromScreen(point), point => Scene.PointToScreen(point),
                stableViewportPixels: stableViewport ? new Rect(first, last) : null);
            Driver = new(new DesktopSceneSession(), Runtime);
            try
            {
                Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
                Runtime.Wander.Next = (_, maximum) => maximum;
                Runtime.QuietIdle.Next = (_, maximum) => maximum;
                Window.Bind(Driver);
                Window.Show();
                Window.PositionStableViewport();
                Scene.FrameApplied += ApplyWindow;
                Driver.Start(Scene, 1, enterWorld: true);
                DesktopPetRoomFixture.WaitUntil(() => Window.HasHealthyFrame && Driver.OwnsRuntime, Runtime,
                    diagnostic: () => Window.LastFrameRejection);
            }
            catch { Dispose(); throw; }
        }

        private void ApplyWindow() => Window.ApplyFrame(Frame, Runtime);

        internal void PrimeAccessoryBinding()
        {
            DangoHostFrameBinding.Apply(Body, Hat, Frame, 0, new(0, Runtime.NeutralBodyGroundOffset), runtime: Runtime);
            Assert.IsTrue(Window.ApplyFrame(Frame, Runtime), Window.LastFrameRejection);
        }

        internal void ReapplyCurrent()
        {
            Assert.IsTrue(Scene.ApplyFrame(Runtime.LastFrame!, Runtime));
            Assert.IsTrue(Window.HasHealthyFrame, Window.LastFrameRejection);
        }

        internal void WaitForNewFrame(int previousSteps)
        {
            DesktopPetRoomFixture.WaitUntil(() => Frame.Bodies.Single().Diagnostics.SimulatedStepCount > previousSteps,
                Runtime, diagnostic: () => Window.LastFrameRejection);
            Window.UpdateLayout();
        }

        internal (Point Window, Point Scene, Point Screen) OpaqueAccessoryPointOutsideBody(AccessoryMeshVisual visual)
        {
            var bitmap = visual.CaptureBitmap()!;
            var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var local = visual.DrawingBounds;
            for (var y = 2; y < bitmap.PixelHeight - 2; y += 2)
                for (var x = 2; x < bitmap.PixelWidth - 2; x += 2)
                {
                    if ((uint)pixels[y * bitmap.PixelWidth + x] >> 24 < 200) continue;
                    var source = new Point(local.X + (x + .5) * local.Width / bitmap.PixelWidth,
                        local.Y + (y + .5) * local.Height / bitmap.PixelHeight);
                    var world = visual.TransformToAncestor(Canvas).Transform(source);
                    var scene = Scene.RoomToScene(new(world.X, world.Y));
                    var scenePoint = new Point(scene.X, scene.Y);
                    if (Window.HitTestScenePoint(scenePoint)) continue;
                    if (DesktopPetRoomFixture.Descendants<System.Windows.Shapes.Path>(Window).Any(path =>
                        path.IsVisible && path.Fill is not null && path.Data is not null &&
                        path.TransformToAncestor(Canvas).Inverse is { } inverse && path.Data.FillContains(inverse.Transform(world)))) continue;
                    var window = visual.TransformToAncestor(Window).Transform(source);
                    return (window, scenePoint, Window.PointToScreen(window));
                }
            throw new AssertFailedException("The oversized raster must contain an actual opaque pixel outside the body.");
        }

        public void Dispose()
        {
            Scene.FrameApplied -= ApplyWindow;
            Driver.Dispose();
            Window.Close();
            SceneWindow.Close();
            Runtime.Dispose();
        }
    }

    private static bool InOwnWindowRegion(DesktopPetDangoWindow window, Point point)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.AreNotEqual((nint)0, region);
        try
        {
            Assert.IsTrue(GetWindowRgn(window.Handle, region) > 0, "The owned HWND must have its actual native region.");
            var dpi = VisualTreeHelper.GetDpi(window);
            return PtInRegion(region, (int)Math.Round(point.X * dpi.DpiScaleX), (int)Math.Round(point.Y * dpi.DpiScaleY));
        }
        finally { _ = DeleteObject(region); }
    }

    private static nint Pack(Point point) => (nint)((uint)(ushort)(short)Math.Round(point.X) |
        ((uint)(ushort)(short)Math.Round(point.Y) << 16));

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
