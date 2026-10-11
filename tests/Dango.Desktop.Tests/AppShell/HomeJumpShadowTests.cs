using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.AppShell.Rooms;
using Dango.Desktop.DesktopHost;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using DrawingPath = System.Windows.Shapes.Path;
using FilePath = System.IO.Path;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class HomeJumpShadowTests
{
    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DataRow(640, 360, "sprout")]
    [DataRow(960, 540, "sprout")]
    [DataRow(1280, 720, "sprout")]
    [DataRow(360, 640, "sprout")]
    [DataRow(640, 360, "top-hat")]
    [DataRow(640, 360, "bowler")]
    [DataRow(360, 640, "top-hat")]
    [DataRow(360, 640, "bowler")]
    public void NativeJumpKeepsHomeFaceAndWardrobeHeadroomAndGroundShadow(int width, int height, string hatPreset)
    {
        using var fixture = new NativeFixture(width, height, hatPreset);
        fixture.Step(240);
        var preview = Element<FrameworkElement>(fixture.Home, "PetPreview");
        var restingPreviewTop = preview.TransformToAncestor(fixture.Home).Transform(new Point()).Y;
        var restingGround = ShadowCenter(fixture.Home);
        var entity = fixture.Runtime.EntityIds.Single();
        var evidence = new List<JumpEvidence>();

        foreach (var mode in new HomeEditor?[] { null, HomeEditor.Face, HomeEditor.Wardrobe })
        {
            if (mode is { } editor) fixture.Home.ShowEditor(editor);
            else fixture.Home.CloseEditor();
            fixture.Layout();
            Assert.AreSame(fixture.Scheduler, fixture.Runtime.FrameDriver,
                "Changing an editor must retain the same native frame owner.");
            Assert.AreEqual(restingPreviewTop,
                preview.TransformToAncestor(fixture.Home).Transform(new Point()).Y, 1,
                $"{mode?.ToString() ?? "Home"} must share the grounded preview height.");
            Assert.AreEqual(restingGround.Y, ShadowCenter(fixture.Home).Y, 1,
                "Changing an editor must keep the same shadow ground height.");
            Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single(),
                "Changing an editor must retain the same physical Dango.");
            evidence.Add(ExerciseNativeJump(fixture, mode?.ToString() ?? "Home"));
        }

        var normal = evidence[0].ApexCoreY - evidence[0].GroundedCoreY;
        var low = evidence[2].ApexCoreY - evidence[2].GroundedCoreY;
        Assert.IsTrue(low >= normal * .2 && low <= normal * .45,
            $"Wardrobe must use a genuinely lower native jump, 20–45% of the same entity's Home rise ({low / normal:P2}).");
        fixture.Home.CloseEditor();
        Assert.AreEqual(1, fixture.Runtime.PreviewJumpSpeedScale);

        using (var output = TestEvidence.Create("home-jump-shadow", $"{width}x{height}-{hatPreset}-{Guid.NewGuid():N}.json"))
            JsonSerializer.Serialize(output, evidence, new JsonSerializerOptions { WriteIndented = true });

        fixture.Home.ReleaseLiveScene();
        Assert.AreEqual(1, fixture.Runtime.PreviewJumpSpeedScale);
        Assert.AreNotEqual(Visibility.Visible, Element<Ellipse>(fixture.Home, "PetShadow").Visibility,
            "Releasing the real scene must hide its shadow with the body.");
        Assert.IsFalse(fixture.Home.OwnsLiveFrameDriver);
        Assert.AreEqual(0, fixture.Source.SubscriberCount);
    }

    [STATestMethod]
    public void ARealMissingNativeFailureCannotDisplayAGroundShadow()
    {
        using var runtime = new StackedDangoSceneRuntime("fictional-missing-home-shadow-native.dll");
        var home = new ProductHomeView { ReduceMotion = true };
        home.BindLiveScene(new DesktopSceneSession(), runtime);
        runtime.Start();
        var input = new PhysicsPresentationInput("fictional-home-shadow", new LogicalRect(0, 0, 160, 120),
            DangoActionState.Idle, 0, 1, "fictional-ground", new LogicalVector(0, -1), 0, 1, false, true, []);
        var failed = runtime.Tick(input, HostFrameClock.FixedStep);
        Assert.IsNotNull(runtime.LastError, "This gate must exercise an actual unavailable-native failure.");
        Assert.AreNotEqual(PhysicsPresentationFallbackReason.None, failed.Bodies.Single().FallbackReason);

        // Start with a stale drawing so omitting the failure cleanup cannot pass
        // merely because XAML initially collapsed the shadow.
        Element<Ellipse>(home, "PetShadow").Visibility = Visibility.Visible;
        Method("ApplyLiveFrame").Invoke(home, [failed]);
        Assert.AreNotEqual(Visibility.Visible, Element<Ellipse>(home, "PetShadow").Visibility,
            "A failed native frame must clear a previously displayed ground shadow.");
        Assert.AreEqual(Visibility.Collapsed, Element<DeformableDangoVisual>(home, "LiveDangoVisual").Visibility);
        Assert.AreEqual(Visibility.Collapsed, Element<DangoHatVisual>(home, "LiveDangoHat").Visibility);
        Assert.AreEqual(Visibility.Visible, Element<FrameworkElement>(home, "LiveSceneStatusText").Visibility);
        home.ReleaseLiveScene();
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void NativePorchRoundTripRestoresTheActualGroundedHomeBodyHeight()
    {
        using var fixture = new NativeFixture(640, 360, "sprout");
        var home = fixture.Home;
        fixture.Step(240);
        Assert.IsTrue(fixture.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded,
            "The initial home baseline must be measured after actual native landing.");
        var entity = fixture.Runtime.EntityIds.Single();
        var initialBodyBottom = RenderedBodyBottom(home);
        var initialShadowGround = ShadowCenter(home).Y;
        AssertGroundShadowIsPainted(fixture, "Home before porch");

        Element<FrameworkElement>(home, "Stage").Focus();
        Assert.IsTrue(Key(home, fixture.Window, System.Windows.Input.Key.W, true).Handled);
        Assert.IsTrue(home.IsInPorch, "W must perform the actual porch handoff.");
        fixture.Step(240);
        Assert.IsTrue(fixture.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded,
            "The porch must also reach a real grounded native frame before returning.");
        Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single());
        Assert.AreSame(fixture.Scheduler, fixture.Runtime.FrameDriver,
            "Entering the porch must retain the shared scene's frame owner.");
        Assert.AreNotEqual(Visibility.Visible, Element<Ellipse>(home, "PetShadow").Visibility,
            "The home's ground shadow must be hidden while the porch owns presentation.");

        Element<Button>(home, "BackFromPorchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsFalse(home.IsInPorch, "The working back action must return to the actual home.");
        fixture.Layout();
        fixture.Step(240);
        Assert.IsTrue(fixture.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded,
            "The returned home baseline must be measured after actual native landing.");
        Assert.AreEqual(entity, fixture.Runtime.EntityIds.Single(),
            "A porch round trip must not replace the physical entity.");
        Assert.AreSame(fixture.Scheduler, fixture.Runtime.FrameDriver,
            "Returning home must retain the shared scene's frame owner.");
        Assert.AreEqual(1, fixture.Source.SubscriberCount);
        Assert.AreEqual(Visibility.Visible, Element<Ellipse>(home, "PetShadow").Visibility);
        var returnedBodyBottom = RenderedBodyBottom(home);
        var returnedShadowGround = ShadowCenter(home).Y;
        var returnedPaintedPixels = AssertGroundShadowIsPainted(fixture, "Home after porch");
        using (var output = TestEvidence.Create("home-jump-shadow", $"640x360-porch-return-{Guid.NewGuid():N}.json"))
            JsonSerializer.Serialize(output, new
            {
                InitialBodyBottom = initialBodyBottom,
                ReturnedBodyBottom = returnedBodyBottom,
                InitialShadowGround = initialShadowGround,
                ReturnedShadowGround = returnedShadowGround,
                ReturnedPaintedPixels = returnedPaintedPixels,
                SuccessfulNativeSteps = fixture.Scheduler.Snapshot.SuccessfulSteps,
            }, new JsonSerializerOptions { WriteIndented = true });
        Assert.AreEqual(initialShadowGround, returnedShadowGround, 1,
            "The porch round trip must restore the same independent shadow ground.");
        Assert.AreEqual(initialBodyBottom, returnedBodyBottom, 1,
            $"The actual grounded body outline must return to the same home height: {initialBodyBottom:F3} -> {returnedBodyBottom:F3} DIP. " +
            "A matching PetPreview rectangle cannot hide a changed live-body projection anchor.");
    }

    private static JumpEvidence ExerciseNativeJump(NativeFixture fixture, string mode)
    {
        fixture.Step(60);
        var home = fixture.Home;
        var runtime = fixture.Runtime;
        var shadow = Element<Ellipse>(home, "PetShadow");
        var scale = Element<ScaleTransform>(home, "PetShadowScale");
        var physical = runtime.LastPhysicsFrame!.Bodies.Single();
        Assert.IsTrue(physical.IsGrounded, $"{mode}: the native body must settle before jumping.");
        Assert.AreEqual(Visibility.Visible, shadow.Visibility, $"{mode}: the healthy body must have a shadow.");
        var groundedCoreY = (double)physical.CoreY;
        var ground = ShadowCenter(home);
        var groundedScale = scale.ScaleX;
        var groundedOpacity = shadow.Opacity;
        Assert.IsTrue(groundedScale > 0 && groundedOpacity > 0,
            "The grounded shadow must have a visible, nonzero footprint.");
        var groundedBodyShadowGap = AssertGroundedBodyTouchesShadow(fixture, mode);
        var paintedShadowPixels = AssertGroundShadowIsPainted(fixture, mode);
        var contentTop = Element<FrameworkElement>(home, "HomeContent")
            .TransformToAncestor(home).Transform(new Point()).Y;
        var navigation = Element<FrameworkElement>(home, "TopNavigation");
        var navigationBottom = navigation.TransformToAncestor(home).TransformBounds(new Rect(navigation.RenderSize)).Bottom;
        var headBoundary = Math.Max(contentTop, navigationBottom) + 8;
        var minimumHead = RenderedHeadTop(home);
        var settledPetBounds = mode == "Home" ? RenderedPetBounds(home) : Rect.Empty;
        var maxCoreY = groundedCoreY;
        var apexIndex = -1;
        var apexScale = groundedScale;
        var apexOpacity = groundedOpacity;
        var wasAirborne = false;
        var airborneFrames = 0;
        var wardrobeCamera = mode == nameof(HomeEditor.Wardrobe)
            ? (RoomViewController)typeof(ProductHomeView).GetField("accessoryCamera", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!
            : null;
        var worldFloor = runtime.LocalGroundY!.Value;
        var groundedCameraTop = wardrobeCamera?.CameraY;
        var previewCanvas = Element<FrameworkElement>(home, "LiveSceneCanvas");
        var visualArea = Element<FrameworkElement>(home, "PetVisualArea");
        var canvasToVisualArea = previewCanvas.TransformToAncestor(visualArea);
        var canvasOrigin = canvasToVisualArea.Transform(new Point());
        var canvasUnitY = canvasToVisualArea.Transform(new Point(0, 1));
        var previewScaleY = canvasUnitY.Y - canvasOrigin.Y;
        Assert.IsTrue(double.IsFinite(previewScaleY) && previewScaleY > 0,
            "The viewport gate requires the actual positive WPF canvas-to-preview Y scale.");
        var viewportWorldHeight = (visualArea.ActualHeight - canvasOrigin.Y) / previewScaleY;
        var groundedMidline = wardrobeCamera is null ? 0 : wardrobeCamera.CameraY + viewportWorldHeight / (2 * wardrobeCamera.Scale);
        var groundedFloorProjection = wardrobeCamera?.Projection.WorldToScreen(new(0, worldFloor)).Y;
        var lowJumpFrames = 0;
        var highJumpFrames = 0;
        var visibleHighJumpFrames = 0;
        var maximumFloorProjection = groundedFloorProjection;
        var previousCoreY = groundedCoreY;

        Element<FrameworkElement>(home, "Stage").Focus();
        Assert.IsTrue(Key(home, fixture.Window, System.Windows.Input.Key.Space, true).Handled,
            $"{mode}: Space must reach the existing physical controller.");
        fixture.Step();
        Key(home, fixture.Window, System.Windows.Input.Key.Space, false);
        for (var i = 0; i < 180; i++)
        {
            if (i > 0) fixture.Step();
            physical = runtime.LastPhysicsFrame!.Bodies.Single();
            wasAirborne |= !physical.IsGrounded;
            if (!physical.IsGrounded) airborneFrames++;
            if (physical.CoreY > maxCoreY)
            {
                maxCoreY = physical.CoreY;
                apexIndex = i;
                apexScale = scale.ScaleX;
                apexOpacity = shadow.Opacity;
            }
            if (wardrobeCamera is null)
            {
                Assert.AreEqual(Visibility.Visible, shadow.Visibility, $"{mode}, native frame {i}: shadow disappeared.");
                var currentGround = ShadowCenter(home);
                Assert.AreEqual(ground.Y, currentGround.Y, .5,
                    $"{mode}, native frame {i}: a jumping body must not lift its ground shadow.");
                Assert.AreEqual(ground.X, currentGround.X, .5,
                    $"{mode}, native frame {i}: the anchored body must keep the shadow horizontal position.");
            }
            else
            {
                Assert.AreEqual(worldFloor, runtime.LocalGroundY!.Value, 1e-9,
                    $"Wardrobe, native frame {i}: jumping must not move the actual world floor.");
                var center = runtime.CurrentSelectedCenter!.Value;
                var floorProjection = wardrobeCamera.Projection.WorldToScreen(new(center.X, worldFloor));
                maximumFloorProjection = Math.Max(maximumFloorProjection!.Value, floorProjection.Y);
                var floorVisible = floorProjection.Y >= 0 && floorProjection.Y <= viewportWorldHeight + 1;
                Assert.AreEqual(floorVisible ? Visibility.Visible : Visibility.Collapsed, shadow.Visibility,
                    $"Wardrobe, native frame {i}: a shadow must be shown only where the real world floor projects into the viewport.");
                if (floorVisible)
                {
                    var projectedGround = previewCanvas.TransformToAncestor(home).Transform(new Point(floorProjection.X, floorProjection.Y));
                    var currentGround = ShadowCenter(home);
                    Assert.AreEqual(projectedGround.Y, currentGround.Y, .5,
                        $"Wardrobe, native frame {i}: the ground shadow must use the world-floor projection, not follow the jumping body.");
                    Assert.AreEqual(projectedGround.X, currentGround.X, .5,
                        $"Wardrobe, native frame {i}: the shadow must use the same horizontal camera projection as the body.");
                }
                var focusY = center.Y + runtime.NeutralBodyGroundOffset;
                Assert.AreEqual(.5, runtime.PreviewJumpSpeedScale);
                Assert.AreEqual(groundedCameraTop!.Value, wardrobeCamera.CameraY, 1e-6,
                    $"Wardrobe, native frame {i}: vertical camera position must remain fixed through the entire jump.");
                Assert.AreEqual(groundedFloorProjection!.Value, floorProjection.Y, 1e-6,
                    $"Wardrobe, native frame {i}: vertical floor projection must remain fixed.");
                if (physical.CoreY > groundedCoreY + .002 && physical.CoreY > previousCoreY && focusY >= groundedMidline)
                {
                    lowJumpFrames++;
                    Assert.AreEqual(groundedCameraTop!.Value, wardrobeCamera.CameraY, 1e-6,
                        $"Wardrobe, native frame {i}: the rising body below the midline must retain ground framing.");
                }
                if (!physical.IsGrounded && focusY < groundedMidline - 1e-6)
                {
                    highJumpFrames++;
                    visibleHighJumpFrames += floorVisible ? 1 : 0;
                }
                var viewportBottom = visualArea.TransformToAncestor(home).TransformBounds(new Rect(visualArea.RenderSize)).Bottom;
                Assert.IsTrue(RenderedBodyBottom(home) <= viewportBottom + 1e-6,
                    $"Wardrobe, native frame {i}: the actual painted body bottom must stay inside the viewport.");
            }
            previousCoreY = physical.CoreY;
            minimumHead = Math.Min(minimumHead, RenderedHeadTop(home));
        }

        Assert.IsTrue(wasAirborne && maxCoreY - groundedCoreY > .02,
            $"{mode}: this gate must contain a real native jump, not only a key intent ({groundedCoreY} -> {maxCoreY}).");
        Assert.IsTrue(apexScale < groundedScale - .002,
            $"{mode}: shadow must narrow at the real native apex ({groundedScale} -> {apexScale}).");
        Assert.IsTrue(apexOpacity < groundedOpacity - .002,
            $"{mode}: shadow must fade at the real native apex ({groundedOpacity} -> {apexOpacity}).");
        Assert.IsTrue(physical.IsGrounded, $"{mode}: the sampled jump must include the actual native landing.");
        Assert.AreEqual(groundedScale, scale.ScaleX, .01, $"{mode}: landing must restore the shadow footprint.");
        Assert.AreEqual(groundedOpacity, shadow.Opacity, .005, $"{mode}: landing must restore the shadow opacity.");
        Assert.IsTrue(minimumHead >= headBoundary,
            $"{mode}: the full body/hat outline reached {minimumHead:F3} DIP, above the safe boundary {headBoundary:F3} DIP.");
        if (wardrobeCamera is not null)
        {
            Assert.IsTrue(lowJumpFrames + highJumpFrames > 0, "Wardrobe: the camera gate must include real native rising frames.");
            Assert.AreEqual(groundedCameraTop!.Value, wardrobeCamera.CameraY, 1e-6,
                "Wardrobe: the actual landing must restore ground framing.");
            Assert.AreEqual(ground.Y, ShadowCenter(home).Y, .5,
                "Wardrobe: landing must return the shadow to its original screen projection.");
        }
        double? homeDoorBottom = null, homeDoorJumpClearance = null, wardrobeLeftGap = null, faceRightGap = null;
        if (mode == "Home")
        {
            var door = RenderedButtonBounds(home, "EnterRoomButton");
            homeDoorBottom = door.Bottom;
            homeDoorJumpClearance = minimumHead - door.Bottom;
            Assert.IsTrue(homeDoorJumpClearance >= 8,
                $"Home: the entire W entrance must stay above the real native jump arc by at least 8 DIP; " +
                $"button bottom {door.Bottom:F3}, highest body/hat outline {minimumHead:F3}, clearance {homeDoorJumpClearance:F3} DIP.");
            var wardrobe = RenderedButtonBounds(home, "WardrobeButton");
            var face = RenderedButtonBounds(home, "FaceButton");
            wardrobeLeftGap = settledPetBounds.Left - wardrobe.Right;
            faceRightGap = face.Left - settledPetBounds.Right;
            Assert.IsTrue(wardrobeLeftGap >= 8,
                $"Home: the entire A wardrobe entry must sit left of the actual grounded body/hat outline with 8 DIP clearance; " +
                $"entry right {wardrobe.Right:F3}, outline left {settledPetBounds.Left:F3}, gap {wardrobeLeftGap:F3} DIP.");
            Assert.IsTrue(faceRightGap >= 8,
                $"Home: the entire D face entry must sit right of the actual grounded body/hat outline with 8 DIP clearance; " +
                $"entry left {face.Left:F3}, outline right {settledPetBounds.Right:F3}, gap {faceRightGap:F3} DIP.");
        }
        return new(mode, groundedCoreY, maxCoreY, apexIndex, airborneFrames, minimumHead, headBoundary,
            ground.Y, groundedScale, apexScale, scale.ScaleX, groundedOpacity, apexOpacity, shadow.Opacity,
            paintedShadowPixels, groundedBodyShadowGap, homeDoorBottom, homeDoorJumpClearance, wardrobeLeftGap, faceRightGap,
            groundedCameraTop, wardrobeCamera is null ? null : lowJumpFrames, wardrobeCamera is null ? null : highJumpFrames,
            wardrobeCamera is null ? null : visibleHighJumpFrames, maximumFloorProjection);
    }

    private sealed record JumpEvidence(string Mode, double GroundedCoreY, double ApexCoreY, int ApexFrame,
        int AirborneFrames, double HighestBodyAndHatY, double SafeTopY, double GroundedShadowGroundY,
        double GroundedShadowScale, double ApexShadowScale, double LandedShadowScale,
        double GroundedShadowOpacity, double ApexShadowOpacity, double LandedShadowOpacity, int PaintedShadowPixels,
        double GroundedBodyShadowGap, double? HomeDoorBottom, double? HomeDoorJumpClearance,
        double? WardrobeLeftGap, double? FaceRightGap, double? GroundedCameraTop,
        int? LowNativeJumpFrames, int? HighNativeJumpFrames, int? VisibleHighNativeJumpFrames, double? MaximumWorldFloorProjectionY);

    private static double AssertGroundedBodyTouchesShadow(NativeFixture fixture, string mode)
    {
        Assert.IsTrue(fixture.Runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded,
            "Body-to-shadow contact must be measured in a real grounded native frame.");
        var bodyBottom = RenderedBodyBottom(fixture.Home);
        var shadowGround = ShadowCenter(fixture.Home).Y;
        var gap = shadowGround - bodyBottom;
        Assert.IsTrue(gap >= -1e-6 && gap <= 2,
            $"{mode}: the actual grounded body bottom must meet the fixed shadow center within 0–2 DIP; " +
            $"body bottom {bodyBottom:F3}, shadow center {shadowGround:F3}, gap {gap:F3} DIP. " +
            "The body's preview rectangle and the independent shadow must not substitute for its rendered contour.");
        return gap;
    }

    private static int AssertGroundShadowIsPainted(NativeFixture fixture, string mode)
    {
        var home = fixture.Home;
        var shadow = Element<Ellipse>(home, "PetShadow");
        var priorVisibility = shadow.Visibility;
        var nativeFrame = fixture.Runtime.LastPhysicsFrame;
        var presentationFrame = fixture.Runtime.LastFrame;
        var completedSteps = fixture.Scheduler.Snapshot.SuccessfulSteps;
        var width = (int)Math.Ceiling(home.ActualWidth);
        var height = (int)Math.Ceiling(home.ActualHeight);
        Assert.IsTrue(width > 0 && height > 0, "Pixel evidence requires an arranged product home.");
        var stride = checked(width * 4);
        var visible = new byte[checked(stride * height)];
        var hidden = new byte[visible.Length];
        Rect footprint;
        try
        {
            shadow.Visibility = Visibility.Visible;
            home.UpdateLayout();
            footprint = shadow.TransformToAncestor(home).TransformBounds(new Rect(shadow.RenderSize));
            Capture(visible);
            // Hidden retains the exact layout. The manual frame source is not
            // raised between captures, so only the shadow's drawing can differ.
            shadow.Visibility = Visibility.Hidden;
            home.UpdateLayout();
            Capture(hidden);
        }
        finally
        {
            shadow.Visibility = priorVisibility;
            home.UpdateLayout();
        }
        Assert.AreSame(nativeFrame, fixture.Runtime.LastPhysicsFrame,
            "Taking shadow pixel evidence must not advance native physics.");
        Assert.AreSame(presentationFrame, fixture.Runtime.LastFrame,
            "Taking shadow pixel evidence must not replace the successful presentation frame.");
        Assert.AreEqual(completedSteps, fixture.Scheduler.Snapshot.SuccessfulSteps);

        var left = Math.Clamp((int)Math.Floor(footprint.Left) - 1, 0, width);
        var right = Math.Clamp((int)Math.Ceiling(footprint.Right) + 1, 0, width);
        var top = Math.Clamp((int)Math.Floor(footprint.Top) - 1, 0, height);
        var bottom = Math.Clamp((int)Math.Ceiling(footprint.Bottom) + 1, 0, height);
        var paintedPixels = 0;
        for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var offset = y * stride + x * 4;
                // BGRA pixels must actually darken by at least two intensity levels
                // on average. A positive ActualSize/Visibility cannot satisfy this.
                var darkening = hidden[offset] + hidden[offset + 1] + hidden[offset + 2] -
                    visible[offset] - visible[offset + 1] - visible[offset + 2];
                if (visible[offset + 3] > 0 && darkening >= 6) paintedPixels++;
            }
        Assert.IsTrue(paintedPixels >= 8,
            $"{mode}: the ground shadow must paint at least 8 actual pixels inside {footprint}; visible/hidden captures contained {paintedPixels}. " +
            "A fully clipped ellipse must fail even when its size and Visibility appear valid.");
        return paintedPixels;

        void Capture(byte[] pixels)
        {
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(home);
            bitmap.CopyPixels(pixels, stride, 0);
        }
    }

    private sealed class NativeFixture : IDisposable
    {
        private long now;
        internal readonly StackedDangoSceneRuntime Runtime;
        internal readonly ProductHomeView Home;
        internal readonly Window Window;
        internal readonly ManualFrameSource Source = new();
        internal readonly HostFrameScheduler Scheduler;

        internal NativeFixture(int width, int height, string preset)
        {
            var module = FilePath.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
            if (!File.Exists(module)) Assert.Inconclusive("统一原生物理模块不在测试输出中；真实跳跃/阴影/顶空集成未验证。");
            Runtime = StackedDangoSceneRuntime.CreateProduct(module);
            Home = new ProductHomeView { ReduceMotion = true };
            Window = new Window
            {
                Content = Home,
                Width = width,
                Height = height,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
            };
            var prepare = Method("PrepareLiveFrame");
            var input = Method("CreateLiveInput");
            var apply = Method("ApplyLiveFrame");
            Scheduler = new HostFrameScheduler(Runtime, () => (bool)prepare.Invoke(Home, null)!,
                () => (PhysicsPresentationInput)input.Invoke(Home, null)!, frame => apply.Invoke(Home, [frame]),
                Source, () => now, TimeSpan.TicksPerSecond);
            try
            {
                Assert.IsTrue(Runtime.TryInitialize(), Runtime.LastError);
                Assert.IsTrue(Runtime.TryApplyHatPreset(preset));
                Home.BindLiveScene(new DesktopSceneSession(), Runtime);
                // The bound scheduler has not started because the view is not loaded.
                // Replace that owner before showing; no WPF driver can run alongside it.
                typeof(ProductHomeView).GetField("liveScheduler", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Home, Scheduler);
                Window.Show();
                Layout();
                Assert.IsTrue(Home.OwnsLiveFrameDriver);
                Assert.AreEqual(1, Source.SubscriberCount);
            }
            catch
            {
                Home.ReleaseLiveScene();
                Window.Close();
                Runtime.Dispose();
                throw;
            }
        }

        internal void Layout()
        {
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Window.UpdateLayout();
        }

        internal void Step(int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                var before = Scheduler.Snapshot.SuccessfulSteps;
                now += HostFrameClock.FixedStep.Ticks;
                Source.Raise(now);
                Window.UpdateLayout();
                Assert.AreEqual(before + 1, Scheduler.Snapshot.SuccessfulSteps,
                    "Each controlled callback must advance exactly one actual native step.");
                Assert.IsNull(Runtime.LastError);
                Assert.IsTrue(Runtime.LastPhysicsFrame?.Succeeded == true);
            }
        }

        public void Dispose()
        {
            Home.ReleaseLiveScene();
            Window.Close();
            Runtime.Dispose();
        }
    }

    private sealed class ManualFrameSource : IHostFrameSource
    {
        private EventHandler? handlers;
        internal int SubscriberCount => handlers?.GetInvocationList().Length ?? 0;
        public event EventHandler Rendering { add => handlers += value; remove => handlers -= value; }
        internal void Raise(long ticks)
        {
            var args = (EventArgs)Activator.CreateInstance(typeof(RenderingEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [TimeSpan.FromTicks(ticks)], null)!;
            handlers?.Invoke(this, args);
        }
    }

    private static double RenderedHeadTop(ProductHomeView home)
    {
        var body = Element<DeformableDangoVisual>(home, "LiveDangoVisual");
        var hat = Element<DangoHatVisual>(home, "LiveDangoHat");
        Assert.AreEqual(Visibility.Visible, body.Visibility);
        Assert.AreEqual(DangoContourFailure.None, body.LastContourFailure);
        var bodyPath = (DrawingPath)body.FindName("BodyPath");
        Assert.AreEqual(Visibility.Visible, bodyPath.Visibility);
        var hatLayer = (Canvas)hat.FindName("HatLayer");
        Assert.AreEqual(Visibility.Visible, hat.Visibility);
        Assert.IsTrue(hatLayer.Opacity > 0, "Headroom cannot pass by hiding the currently worn hat.");
        var paths = new[] { bodyPath }.Concat(hatLayer.Children.OfType<DrawingPath>()).ToArray();
        Assert.IsTrue(paths.Length > 1, "The safety measurement must include actual worn-hat outlines.");
        return paths.SelectMany(path => RenderedPoints(path, home)).Min(point => point.Y);
    }

    private static double RenderedBodyBottom(ProductHomeView home)
    {
        var body = Element<DeformableDangoVisual>(home, "LiveDangoVisual");
        Assert.AreEqual(Visibility.Visible, body.Visibility);
        Assert.AreEqual(DangoContourFailure.None, body.LastContourFailure);
        var path = (DrawingPath)body.FindName("BodyPath");
        Assert.AreEqual(Visibility.Visible, path.Visibility);
        return RenderedPoints(path, home).Max(point => point.Y);
    }

    private static Rect RenderedPetBounds(ProductHomeView home)
    {
        var body = Element<DeformableDangoVisual>(home, "LiveDangoVisual");
        var hat = Element<DangoHatVisual>(home, "LiveDangoHat");
        var bodyPath = (DrawingPath)body.FindName("BodyPath");
        var hatLayer = (Canvas)hat.FindName("HatLayer");
        var points = new[] { bodyPath }.Concat(hatLayer.Children.OfType<DrawingPath>())
            .SelectMany(path => RenderedPoints(path, home)).ToArray();
        Assert.IsTrue(points.Length > 0, "Home entrance separation requires the actual rendered body/hat outlines.");
        return new Rect(new Point(points.Min(point => point.X), points.Min(point => point.Y)),
            new Point(points.Max(point => point.X), points.Max(point => point.Y)));
    }

    private static Rect RenderedButtonBounds(ProductHomeView home, string name)
    {
        var button = Element<Button>(home, name);
        Assert.IsTrue(button.IsVisible && button.ActualWidth > 0 && button.ActualHeight > 0,
            $"{name} must be a visible home entry before measuring its separation from the native body.");
        return button.TransformToAncestor(home).TransformBounds(new Rect(button.RenderSize));
    }

    private static IEnumerable<Point> RenderedPoints(DrawingPath path, UIElement ancestor)
    {
        var transform = path.TransformToAncestor(ancestor);
        foreach (var figure in path.Data.GetFlattenedPathGeometry(.01, ToleranceType.Absolute).Figures)
        {
            yield return transform.Transform(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line) yield return transform.Transform(line.Point);
                else if (segment is PolyLineSegment polyline)
                    foreach (var point in polyline.Points) yield return transform.Transform(point);
            }
        }
    }

    private static Point ShadowCenter(ProductHomeView home)
    {
        var shadow = Element<Ellipse>(home, "PetShadow");
        return shadow.TransformToAncestor(home).Transform(new Point(shadow.ActualWidth / 2, shadow.ActualHeight / 2));
    }

    private static KeyEventArgs Key(ProductHomeView home, Window window, System.Windows.Input.Key key, bool down)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, key)
        { RoutedEvent = down ? Keyboard.PreviewKeyDownEvent : Keyboard.PreviewKeyUpEvent };
        home.RaiseEvent(args);
        return args;
    }

    private static T Element<T>(ProductHomeView home, string name) where T : class =>
        home.FindName(name) as T ?? throw new AssertFailedException($"Missing home presentation element: {name}");
    private static MethodInfo Method(string name) => typeof(ProductHomeView)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new AssertFailedException($"Missing real home frame method: {name}");
}
