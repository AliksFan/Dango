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
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Canvas = System.Windows.Controls.Canvas;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
[DoNotParallelize]
public sealed class DesktopPetAccessoryIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(50)]
    [DataRow(100)]
    [DataRow(200)]
    public void OffscreenDisplayScalingKeepsTheNativeEntityFloorAndCompleteArtwork(int percent)
    {
        using var rig = new Fixture(stableViewport: true);
        var before = rig.Runtime.LastBeltRoomFrame!;
        var floor = rig.Runtime.LocalGroundY;
        var initialScale = rig.Scene.CurrentProjection.Scale;
        rig.Scene.DesktopPetDisplayPercent = percent;
        rig.Driver.RefreshDesktopProjection();
        DesktopPetRoomFixture.WaitUntil(() => rig.Window.HasHealthyFrame &&
            rig.Runtime.LastBeltRoomFrame!.Tick > before.Tick &&
            ReferenceEquals(rig.Window.LastAppliedFrame, rig.Runtime.LastFrame), rig.Runtime);
        var after = rig.Runtime.LastBeltRoomFrame!;
        Assert.AreEqual(before.OwnerEpoch, after.OwnerEpoch);
        Assert.AreEqual(before.Generation, after.Generation);
        Assert.AreEqual(before.BodyId, after.BodyId);
        Assert.AreEqual(before.ConfigurationRevision, after.ConfigurationRevision);
        Assert.AreEqual(floor, rig.Runtime.LocalGroundY, "Display scaling cannot move the physical floor.");
        Assert.AreEqual(initialScale * percent / 100d, rig.Scene.CurrentProjection.Scale, 1e-8);
        var artwork = Rect.Empty;
        foreach (var shape in DesktopPetRoomFixture.Descendants<System.Windows.Shapes.Shape>(rig.Canvas).Where(value => value.IsVisible && value.Fill is not null))
            artwork.Union(shape.TransformToAncestor(rig.Window).TransformBounds(shape.RenderedGeometry.Bounds));
        foreach (var accessory in rig.Accessories)
            artwork.Union(accessory.TransformToAncestor(rig.Window).TransformBounds(accessory.Frame!.Bounds));
        Assert.IsFalse(artwork.IsEmpty);
        if (artwork.Width <= rig.Window.ActualWidth && artwork.Height <= rig.Window.ActualHeight)
            Assert.IsTrue(artwork.Left >= 0 && artwork.Top >= 0 && artwork.Right <= rig.Window.ActualWidth && artwork.Bottom <= rig.Window.ActualHeight,
                "The complete scalable artwork must fit when the viewport has enough capacity.");
        var bitmap = RenderWindow(rig.Window);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert.IsTrue(Enumerable.Range(0, bitmap.PixelWidth * bitmap.PixelHeight).Any(index => pixels[index * 4 + 3] != 0),
            "A qualified native frame must actually paint scaled pixels into the owned offscreen target.");
    }

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
        var first = rig.ToScene(new(expectedWorld.Left, expectedWorld.Top));
        var last = rig.ToScene(new(expectedWorld.Right, expectedWorld.Bottom));
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
    public void ExistingRasterOutsideBodyIsPaintedAndRemovedWhenUnequipped(bool stableViewport)
    {
        using var rig = new Fixture(stableViewport);
        rig.PrimeAccessoryBinding();
        var visual = rig.Accessories.OrderByDescending(value => value.DrawingBounds.Width).First();
        var point = rig.OpaqueAccessoryPointOutsideBody(visual);
        Assert.IsTrue(IsPaintedPixel(rig.Window, point.Window),
            "A real opaque accessory pixel outside body/hat paths must remain painted.");
        Assert.IsFalse(rig.Window.HitTestScenePoint(point.Scene),
            "Including a picture in the display region cannot make it a body interaction target.");
        Assert.AreEqual((nint)(-1), SendMessage(rig.Window.Handle, 0x0084, 0, Pack(rig.Window.PointToScreen(point.Window))));
        var bodyPoint = rig.ToScene(DangoHostFrameBinding.Bounds(rig.Frame, 0).Center +
            new LogicalVector(0, rig.Runtime.NeutralBodyGroundOffset));
        Assert.IsTrue(rig.Window.HitTestScenePoint(new(bodyPoint.X, bodyPoint.Y)));

        rig.Runtime.SetAccessories(AccessoryConfiguration.Empty);
        rig.ReapplyCurrent();
        Assert.HasCount(0, rig.Accessories);
        Assert.IsFalse(IsPaintedPixel(rig.Window, rig.Window.PointFromScreen(point.Screen)),
            "Taking off the item must clear its previous painted pixels.");
        AssertReadOnlyAndBodyOnly(rig);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void StableViewportDragKeepsCurrentOpaquePixelsDrawable()
    {
        using var rig = new Fixture(stableViewport: true);
        DesktopPetRoomFixture.WaitUntil(() => rig.Driver.IsReady, rig.Runtime);
        var center = DangoHostFrameBinding.Bounds(rig.Frame, 0).Center;
        var bodyCenter = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
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
                // Advance a fixed amount of physical time per target; faster
                // presentation must not compress the whole gesture into 0.2 s.
                rig.WaitForNewFrame(previousSteps, minimumSteps: 6);
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

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void AdvancingNativeFrameDoesNotClipThePrecedingImage()
    {
        using var rig = new Fixture(stableViewport: true);
        DesktopPetRoomFixture.WaitUntil(() => rig.Driver.IsReady, rig.Runtime);
        rig.Runtime.SetAccessories(new AccessoryConfiguration([
            new("hat", "Fictional top hat", "builtin-top-hat", 64, 64) { BodyAnchorX = .5, BodyAnchorY = 0 },
            new("sprout", "Fictional sprout", "builtin-sprout", 64, 64) { BodyAnchorX = .5, BodyAnchorY = 0 },
        ]));
        rig.WaitForNewFrame(rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount);
        var center = DangoHostFrameBinding.Bounds(rig.Frame, 0).Center;
        var sceneCenter = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var start = new Point(sceneCenter.X, sceneCenter.Y);
        Assert.IsTrue(rig.Driver.PointerDown(start));
        try
        {
            for (var i = 1; i <= 16; i++)
            {
                // WPF submits image pixels asynchronously. Keep the preceding
                // real image while the UI thread advances its HWND state.
                var submittedImage = RenderWindow(rig.Window);
                var steps = rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount;
                Assert.IsTrue(rig.Driver.PointerMove(new(start.X + i * 4, start.Y - i * 7)));
                rig.WaitForNewFrame(steps);
                Assert.AreSame(rig.Frame, rig.Window.LastAppliedFrame);
                var coverage = CountWpfOpaquePixelsOutsideNativeRegion(rig.Window, submittedImage);
                Assert.IsTrue(coverage.SampledOpaquePixels > 1000);
                Assert.IsTrue(coverage.ClippedOpaquePixels <= 2,
                    $"The next HWND update cut {coverage.ClippedOpaquePixels}/{coverage.SampledOpaquePixels} interior pixels " +
                    "from the preceding real image. A separately updated native shape cannot safely clip asynchronously presented pixels.");
            }
        }
        finally { rig.Driver.PointerUp(start); }
        Assert.IsFalse(rig.Runtime.IsDragging);
    }

    private static RenderTargetBitmap RenderWindow(DesktopPetDangoWindow window)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(100d, 50d, 50d)]
    [DataRow(100d, 13d, 34d)]
    [DataRow(120d, 100d, 0d)]
    public void AirborneDownwardDragPaintsTheCompleteNativeBodyBoundary(double size, double softness, double build)
    {
        using var rig = new Fixture(stableViewport: true, new DangoBodyControls(size, softness, build));
        DesktopPetRoomFixture.WaitUntil(() => rig.Driver.IsReady, rig.Runtime);
        rig.Runtime.SetAccessories(AccessoryConfiguration.Empty);
        rig.WaitForNewFrame(rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount);
        var center = DangoHostFrameBinding.Bounds(rig.Frame, 0).Center;
        var origin = rig.ToScene(new(center.X, center.Y + rig.Runtime.NeutralBodyGroundOffset));
        var start = new Point(origin.X, origin.Y);
        Assert.IsTrue(rig.Driver.PointerDown(start));
        var maximumBottom = double.MinValue;
        try
        {
            for (var i = 1; i <= 16; i++)
            {
                var step = rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount;
                Assert.IsTrue(rig.Driver.PointerMove(new(start.X, start.Y - i * 14)));
                rig.WaitForNewFrame(step, minimumSteps: 6);
            }
            for (var i = 1; i <= 12; i++)
            {
                var step = rig.Frame.Bodies.Single().Diagnostics.SimulatedStepCount;
                Assert.IsTrue(rig.Driver.PointerMove(new(start.X, start.Y - 224 + i * 7)));
                rig.WaitForNewFrame(step, minimumSteps: 6);
                Assert.IsFalse(rig.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded,
                    "The downward sample must stay airborne; floor contact is not a clipping defect.");
                var path = (System.Windows.Shapes.Path)rig.Body.FindName("BodyPath");
                maximumBottom = Math.Max(maximumBottom, path.Data.Bounds.Bottom);
                var actual = RenderWindow(rig.Window);
                var transform = path.TransformToAncestor(rig.Window);
                var zero = transform.Transform(default(Point));
                var x = transform.Transform(new Point(1, 0));
                var y = transform.Transform(new Point(0, 1));
                var reference = new DrawingVisual();
                using (var drawing = reference.RenderOpen())
                {
                    drawing.PushTransform(new MatrixTransform(x.X - zero.X, x.Y - zero.Y,
                        y.X - zero.X, y.Y - zero.Y, zero.X, zero.Y));
                    drawing.DrawGeometry(Brushes.Black, null, path.Data);
                    drawing.Pop();
                }
                var expected = new RenderTargetBitmap(actual.PixelWidth, actual.PixelHeight,
                    actual.DpiX, actual.DpiY, PixelFormats.Pbgra32);
                expected.Render(reference);
                var desired = new byte[expected.PixelWidth * expected.PixelHeight * 4];
                var painted = new byte[desired.Length];
                expected.CopyPixels(desired, expected.PixelWidth * 4, 0);
                actual.CopyPixels(painted, actual.PixelWidth * 4, 0);
                var missing = 0;
                for (var row = 1; row < actual.PixelHeight - 1; row++)
                    for (var column = 1; column < actual.PixelWidth - 1; column++)
                    {
                        var interior = true;
                        for (var dy = -1; dy <= 1 && interior; dy++)
                            for (var dx = -1; dx <= 1; dx++)
                                if (desired[((row + dy) * actual.PixelWidth + column + dx) * 4 + 3] < 250)
                                { interior = false; break; }
                        if (interior && painted[(row * actual.PixelWidth + column) * 4 + 3] < 200) missing++;
                    }
                var clips = new List<string>();
                for (DependencyObject? node = path; node is not null; node = VisualTreeHelper.GetParent(node))
                    if (node is Visual visual && VisualTreeHelper.GetClip(visual) is { } clip)
                        clips.Add($"{node.GetType().Name}: {clip.Bounds}");
                Assert.IsTrue(missing <= 2,
                    $"Airborne downward frame {i} lost {missing} body interior pixels. " +
                    $"Native geometry={path.Data.Bounds}; body slot={rig.Body.RenderSize}; clips={string.Join("; ", clips)}");
            }
        }
        finally { rig.Driver.PointerUp(start); }
        TestContext.WriteLine($"Maximum native body geometry bottom={maximumBottom}; body slot height={rig.Body.Height}.");
    }

    private static (int SampledOpaquePixels, int ClippedOpaquePixels) CountWpfOpaquePixelsOutsideNativeRegion(
        DesktopPetDangoWindow window, BitmapSource? submittedImage = null)
    {
        var bitmap = submittedImage ?? RenderWindow(window);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var region = CreateRectRgn(0, 0, 0, 0);
        Assert.AreNotEqual((nint)0, region);
        try
        {
            var hasExtraClip = GetWindowRgn(window.Handle, region) > 0;
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
                    if (hasExtraClip && !PtInRegion(region, x, y)) clipped++;
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
        var bounds = (rig.Frame.BeltRoom is { } belt && rig.Scene.NativePetCenter is { } core
            ? DesktopPetDangoWindow.BeltBodyWorldBounds(belt, core)
            : DangoHostFrameBinding.Bounds(rig.Frame, 0)).Translate(new(0, rig.Runtime.NeutralBodyGroundOffset));
        var first = rig.ToScene(new(bounds.X, bounds.Y));
        var last = rig.ToScene(new(bounds.Right, bounds.Bottom));
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
        var bodyFirst = rig.ToScene(new(paintedBody.X - 2, paintedBody.Y - 2));
        var bodyLast = rig.ToScene(new(paintedBody.Right + 2, paintedBody.Bottom + 2));
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
        internal readonly DesktopPetDangoWindow Window;
        private static readonly Point SceneOriginPixels = new(-20000, -20000);
        internal Canvas Canvas => DesktopPetRoomFixture.Field<Canvas>(Window, "canvas");
        internal DeformableDangoVisual Body => DesktopPetRoomFixture.Field<DeformableDangoVisual>(Window, "body");
        internal DangoHatVisual Hat => DesktopPetRoomFixture.Field<DangoHatVisual>(Window, "hat");
        internal Dango.Desktop.Physics.Presentation.StackedPhysicsPresentationFrame Frame => Scene.LastAppliedFrame!;
        internal AccessoryMeshVisual[] Accessories => DesktopPetRoomFixture.Descendants<AccessoryMeshVisual>(Canvas).ToArray();
        internal LogicalPoint ToScene(LogicalPoint world) => Scene.RoomToScene(world + Scene.PresentationOffset);

        internal Fixture(bool stableViewport, DangoBodyControls? bodyControls = null)
        {
            Runtime = ProductPhysicsComposition.Default.CreateRoom();
            Runtime.ConfigureBeltRoomAppearance(DangoAppearanceConfiguration.Default.WithBody(bodyControls ?? DangoBodyControls.Default), beginRoom: true);
            var root = System.IO.Path.Combine(TestRepository.Root, "artifacts", "accessory-v1-desktop-pet", Guid.NewGuid().ToString("N"));
            var settings = new AccessorySettingsSession(root);
            var configuration = new AccessoryConfiguration([
                new("rear", "Fictional rear raster", "builtin-sprout", 64, 64)
                {
                    Layer = AccessoryLayer.Behind, BodyAnchorX = .5, BodyAnchorY = 0,
                    SizePercent = 350, ImagePivotX = .9, ImagePivotY = 1,
                    LockRotation = true, GravityInfluence = 0,
                },
                new("front", "Fictional front raster", "builtin-bowler", 64, 64)
                {
                    Layer = AccessoryLayer.Front, BodyAnchorX = .5, BodyAnchorY = .2,
                    SizePercent = 70, LockRotation = true, GravityInfluence = 0,
                    DeformationMode = AccessoryDeformationMode.SingleChain,
                    Rig = new([new(.5, .5), new(.8, .9)], .7),
                },
            ]);
            settings.Stage(configuration);
            Runtime.BindAccessories(settings);
            Runtime.SetAccessories(configuration);
            Scene.EnableDesktopPetPresentation();
            var dpi = VisualTreeHelper.GetDpi(Scene);
            Window = new(Scene, ScreenPixelsToScene, SceneToScreenPixels,
                stableViewportPixels: stableViewport
                    ? new Rect(SceneOriginPixels, new Size(Scene.Width * dpi.DpiScaleX, Scene.Height * dpi.DpiScaleY)) : null)
            { Left = SceneOriginPixels.X, Top = SceneOriginPixels.Y, Width = 1024, Height = 640 };
            Driver = new(new DesktopSceneSession(), Runtime);
            try
            {
                Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
                Runtime.Wander.Next = (_, maximum) => maximum;
                Runtime.QuietIdle.Next = (_, maximum) => maximum;
                Window.Bind(Driver);
                Window.Show();
                Window.PositionStableViewport();
                SynchronizeSceneOrigin();
                Scene.Measure(new(Scene.Width, Scene.Height));
                Scene.Arrange(new(0, 0, Scene.Width, Scene.Height));
                Scene.UpdateLayout();
                Assert.AreSame(PresentationSource.FromVisual(Window), PresentationSource.FromVisual(Scene),
                    "The fixture's validation scene and accessory body must share one native surface.");
                Scene.FrameApplied += ApplyWindow;
                Driver.Start(Scene, 1, enterWorld: true);
                DesktopPetRoomFixture.WaitUntil(() => Window.HasHealthyFrame && Driver.OwnsRuntime, Runtime,
                    diagnostic: () => Window.LastFrameRejection);
            }
            catch { Dispose(); throw; }
        }

        // Keep the screen projection independent of the pet's moving native
        // bounds; using Scene.PointToScreen as the ApplyFrame callback would
        // feed the previous window origin back into its next placement.
        private Point SceneToScreenPixels(Point point)
        {
            var dpi = VisualTreeHelper.GetDpi(Window);
            return new(SceneOriginPixels.X + point.X * dpi.DpiScaleX,
                SceneOriginPixels.Y + point.Y * dpi.DpiScaleY);
        }

        private Point ScreenPixelsToScene(Point point)
        {
            var dpi = VisualTreeHelper.GetDpi(Window);
            return new((point.X - SceneOriginPixels.X) / dpi.DpiScaleX,
                (point.Y - SceneOriginPixels.Y) / dpi.DpiScaleY);
        }

        private void SynchronizeSceneOrigin()
        {
            var actualOrigin = Window.PointToScreen(default(Point));
            var dpi = VisualTreeHelper.GetDpi(Window);
            Scene.RenderTransform = new TranslateTransform(
                (SceneOriginPixels.X - actualOrigin.X) / dpi.DpiScaleX,
                (SceneOriginPixels.Y - actualOrigin.Y) / dpi.DpiScaleY);
        }

        private void ApplyWindow()
        {
            _ = Window.ApplyFrame(Frame, Runtime);
            SynchronizeSceneOrigin();
        }

        internal void PrimeAccessoryBinding()
        {
            DangoHostFrameBinding.Apply(Body, Hat, Frame, 0, new(0, Runtime.NeutralBodyGroundOffset), runtime: Runtime);
            Assert.IsTrue(Window.ApplyFrame(Frame, Runtime), Window.LastFrameRejection);
            SynchronizeSceneOrigin();
        }

        internal void ReapplyCurrent()
        {
            Assert.IsTrue(Scene.ApplyFrame(Runtime.LastFrame!, Runtime));
            Assert.IsTrue(Window.HasHealthyFrame, Window.LastFrameRejection);
        }

        internal void WaitForNewFrame(int previousSteps, int minimumSteps = 1)
        {
            DesktopPetRoomFixture.WaitUntil(() => Frame.Bodies.Single().Diagnostics.SimulatedStepCount >= previousSteps + minimumSteps,
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
                    var scene = ToScene(new(world.X, world.Y));
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
            Runtime.Dispose();
        }
    }

    private static bool IsPaintedPixel(DesktopPetDangoWindow window, Point point)
    {
        var bitmap = RenderWindow(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var x = (int)Math.Floor(point.X * dpi.DpiScaleX);
        var y = (int)Math.Floor(point.Y * dpi.DpiScaleY);
        if (x < 0 || y < 0 || x >= bitmap.PixelWidth || y >= bitmap.PixelHeight) return false;
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel[3] > 0;
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
