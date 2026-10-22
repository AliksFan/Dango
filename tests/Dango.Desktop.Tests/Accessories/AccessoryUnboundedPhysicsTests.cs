using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryUnboundedPhysicsTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(.02);
    private static readonly LogicalRect Bounds = new(400, 350, 160, 120);
    private static string NativePath => Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
        Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
    private static PhysicsPresentationInput Input(LogicalRect bounds) => new("fictional-unbounded-preview", bounds,
        DangoActionState.Idle, 0, 1, "fictional-ground", new(0, 1), 0, 1, false, true, []);

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void RealBodyWalksBeyondBothOriginalFloorEdgesWithoutResettingShapeVelocityOrIdentity()
    {
        using var rolling = new DesktopPhysicsScene(NativePath, Bounds);
        using var reference = new DesktopPhysicsScene(NativePath, Bounds);
        rolling.SetUnboundedHorizontalPreview(true);
        // A wide physical control floor is independent of the rolling-origin
        // implementation and remains far from all positions used by this gate.
        reference.SetHorizontalBounds(Bounds.Center.X - 8000, Bounds.Center.X + 8000);
        for (var step = 0; step < 300; step++) { rolling.Step(Step); reference.Step(Step); }
        var entity = rolling.SelectedEntity;
        var floor = rolling.GroundY;
        var start = rolling.LastFrame!.Bodies.Single().Bounds.Center.X;
        var previous = start;
        var sawRightBeyondOriginalFloor = false;
        var sawLeftBeyondOriginalFloor = false;
        var grounded = 0;
        for (var index = 0; index < 4600; index++)
        {
            var command = index < 1600 ? new DirectionalControlIntent(false, true, false, false) : new(true, false, false, false);
            rolling.SetInput(command); reference.SetInput(command);
            var rebases = rolling.HorizontalRebaseCount;
            var frame = rolling.Step(Step); var control = reference.Step(Step);
            Assert.IsTrue(frame.Physics.Succeeded, $"native failure at {index}");
            Assert.IsTrue(control.Physics.Succeeded, $"control failure at {index}");
            var body = frame.Physics.Bodies.Single(); var controlBody = control.Physics.Bodies.Single();
            var center = frame.Bodies.Single().Bounds.Center;
            Assert.AreEqual(entity, body.EntityId);
            Assert.AreEqual(floor, rolling.GroundY, 1e-9);
            Assert.IsTrue(center.Y < floor, $"Body fell through the finite native floor at {index}.");
            Assert.IsTrue(Math.Abs(center.X - previous) < 4, $"Visible teleport at {index}.");
            Assert.AreEqual(301 + index, frame.Bodies.Single().Presentation.Diagnostics.SimulatedStepCount,
                "Rolling the origin must neither reset the presenter nor add a body step.");
            grounded += body.IsGrounded ? 1 : 0;
            sawRightBeyondOriginalFloor |= center.X > start + 800;
            sawLeftBeyondOriginalFloor |= center.X < start - 800;
            if (rolling.HorizontalRebaseCount != rebases)
            {
                Assert.IsTrue(body.MaximumSpeed > .1, "Rebasing must preserve actual walking velocity.");
                Assert.AreEqual(controlBody.MaximumSpeed, body.MaximumSpeed, .12,
                    "The rolling floor must not reset the rig's velocity.");
                Assert.AreEqual(controlBody.SignedArea, body.SignedArea, .08,
                    "The rolling floor must retain the moving body's shape.");
                for (var point = 0; point < body.Contour.Count; point++)
                    Assert.AreEqual(controlBody.Contour[point], body.Contour[point], .08, $"shape point {point} at {index}");
            }
            previous = center.X;
        }
        Assert.IsTrue(sawRightBeyondOriginalFloor && sawLeftBeyondOriginalFloor,
            "The gate must cross both original +/-8-physics-unit floor edges, not just hide a wall.");
        Assert.IsTrue(rolling.HorizontalRebaseCount >= 6, "The gate must exercise repeated origin rolls in both directions.");
        Assert.IsTrue(grounded > 4500, "Walking must retain native ground contacts throughout the traversal.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void RollingPreviewKeepsGaitAndElasticAccessoryMotionContinuousWithOneStepPerTick()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-verification", "unbounded", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var item = new AccessoryItem("fictional-elastic", "Fictional elastic ornament", "builtin-sprout", 64, 64)
        {
            Softness = 65,
            ConnectionStrength = 70,
            GravityInfluence = 0,
            LockRotation = false,
            BodyAnchorX = .5,
            BodyAnchorY = .25,
        };
        var configuration = new AccessoryConfiguration([item]);
        var session = new AccessorySettingsSession(root); session.Stage(configuration);
        using var runtime = StackedDangoSceneRuntime.CreateProduct(NativePath);
        runtime.BindAccessories(session); runtime.SetAccessories(configuration);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.Start(); runtime.SetUnboundedHorizontalPreview(true);
        runtime.SetViewport(new(0, 0, 480, 360));
        var owner = new object(); runtime.AttachQuietIdleHost(owner, false); runtime.UpdateWanderScreenWidth(owner, null);
        var input = Input(Bounds);
        for (var index = 0; index < 300; index++) runtime.Tick(input, Step);
        var entity = runtime.SelectedEntity;
        var previousCenter = runtime.CurrentSelectedCenter!.Value;
        var previousMesh = runtime.GetAccessoryFrames(runtime.LastFrame!, 0).Single().Mesh;
        var previousPhase = runtime.Gait.Phase;
        var rebaseChecks = 0;
        runtime.SetDirectionalControl(new(false, true, false, false));
        for (var index = 0; index < 1800; index++)
        {
            var rebases = runtime.HorizontalRebaseCount;
            var frame = runtime.Tick(input, Step);
            Assert.IsNull(runtime.LastError); Assert.IsNull(runtime.AccessoryError); Assert.IsNull(runtime.GaitError);
            Assert.AreEqual(entity, runtime.SelectedEntity);
            var center = runtime.CurrentSelectedCenter!.Value;
            var mesh = runtime.GetAccessoryFrames(frame, 0).Single().Mesh;
            Assert.AreEqual(301 + index, frame.Bodies[0].Diagnostics.SimulatedStepCount);
            Assert.AreEqual((long)(301 + index), mesh.StepIndex, "A rolling origin must not advance accessories separately.");
            Assert.IsTrue(Math.Abs(center.X - previousCenter.X) < 4, "Host-world coordinates must stay continuous.");
            if (runtime.HorizontalRebaseCount != rebases)
            {
                rebaseChecks++;
                var contourShape = AttachmentShapeGeometry.Create(frame.Bodies[0].Contour);
                var attachmentShape = frame.Bodies[0].Attachments!.SourceShape!;
                CollectionAssert.AreEqual(contourShape.Boundary.ToArray(), attachmentShape.Boundary.ToArray(),
                    "Continuous motion sampling must not mix the native core origin into relative contour geometry.");
                var bodyBounds = DangoHostFrameBinding.Bounds(frame, 0);
                var localAnchor = AccessoryBodyMapper.Map(frame.Bodies[0], frame.Gait, item.BodyAnchorX, item.BodyAnchorY);
                var expectedAnchor = new Point(bodyBounds.X + localAnchor.X, bodyBounds.Y + localAnchor.Y);
                Assert.IsTrue((mesh.ImagePivot - mesh.ConnectionOffset - expectedAnchor).Length < 1e-7,
                    "The accessory root must still bind to the actual body contour after a roll.");
                Assert.IsTrue((mesh.ImagePivot - previousMesh.ImagePivot).Length < 5,
                    "The actual accessory anchor must not jump at a native origin roll.");
                Assert.IsTrue((mesh.ConnectionOffset - previousMesh.ConnectionOffset).Length < 1,
                    "Elastic motion must not receive an artificial rebase acceleration.");
                var phaseAdvance = (runtime.Gait.Phase - previousPhase + 1) % 1;
                Assert.IsTrue(phaseAdvance > 0 && phaseAdvance <= .041,
                    $"A walking gait must keep its displacement phase across a roll: {phaseAdvance}.");
            }
            previousCenter = center; previousMesh = mesh; previousPhase = runtime.Gait.Phase;
        }
        Assert.IsTrue(rebaseChecks >= 3);
        Assert.IsTrue(runtime.CurrentSelectedCenter!.Value.X > Bounds.Center.X + 800);
        // Repeated rendering/refresh of an existing successful frame is read-only.
        var last = runtime.LastFrame!; var physical = runtime.LastPhysicsFrame;
        var accessoryStep = runtime.GetAccessoryFrames(last, 0).Single().Mesh.StepIndex;
        runtime.SetAccessories(configuration);
        Assert.AreSame(physical, runtime.LastPhysicsFrame);
        Assert.AreEqual(accessoryStep, runtime.GetAccessoryFrames(last, 0).Single().Mesh.StepIndex);

        var homeBounds = new LogicalRect(80, 240, 160, 120);
        runtime.SetDirectionalControl(DirectionalControlIntent.None);
        runtime.SetUnboundedHorizontalPreview(false);
        Assert.IsTrue(runtime.RehostSelectedEntityAt(homeBounds.Center)); runtime.ClearHorizontalBounds();
        var returned = runtime.Tick(Input(homeBounds), Step);
        Assert.IsFalse(runtime.UnboundedHorizontalPreview);
        Assert.AreEqual(entity, runtime.SelectedEntity);
        Assert.IsTrue(Math.Abs(DangoHostFrameBinding.Bounds(returned, 0).Center.X - homeBounds.Center.X) < 8,
            "Home handoff must remove the accumulated preview origin.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("LegacyModuleIntegration")]
    public void PreviewCapabilityIsExplicitAndCannotMixWithRoomWalls()
    {
        using var scene = new DesktopPhysicsScene(NativePath, Bounds);
        Assert.IsFalse(scene.UnboundedHorizontalPreview);
        scene.SetUnboundedHorizontalPreview(true);
        Assert.ThrowsExactly<InvalidOperationException>(() => scene.SetHorizontalBounds(0, 2160));
        scene.SetUnboundedHorizontalPreview(false);
        scene.SetHorizontalBounds(0, 2160);
        Assert.IsTrue(scene.Step(Step).Physics.Succeeded);
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [DoNotParallelize]
    public void ColdHomeCanOpenWardrobeBeforeItsFirstNativeFrameAndFinishPreparation()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-verification", "unbounded-cold-home", Guid.NewGuid().ToString("N"));
        using var host = new DangoApplicationHost(settingsFile: Path.Combine(root, "settings.json"));
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        Assert.AreEqual(0, runtime.EntityIds.Count);
        var window = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        var home = window.ProductHome;
        Assert.IsFalse(home.IsLoaded);
        Assert.IsNull(home.DisplayedBeltSnapshot, "The cold fixture must begin before an acknowledged native body frame.");
        var worker = home.BoundBeltBody;
        home.ShowEditor(HomeEditor.Wardrobe);
        Assert.IsNull(home.DisplayedBeltSnapshot, "Opening the hidden editor cannot manufacture a displayed frame.");
        host.ShowControlCenter();
        Dango.Tests.Support.ProductHostTestSupport.WaitForHome(host);
        Assert.IsTrue(home.IsEditing); Assert.AreEqual(HomeEditor.Wardrobe, home.SelectedEditor);
        Assert.AreSame(worker, home.BoundBeltBody);
        Assert.IsTrue(worker!.Metrics.Running);
        Assert.IsNotNull(home.BoundBeltPresenter!.Inspect().Body);
        Assert.IsNull(home.BoundBeltPresenter.RuntimeError);
        Assert.IsNull(home.BoundRuntime, "The cold-start path cannot revive the retired product world.");
        Assert.IsTrue(home.BoundBeltPresenter.View.IsVisible);
        Assert.AreEqual(0, runtime.EntityIds.Count, "Wardrobe startup must leave the independent room world deferred.");
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
}
