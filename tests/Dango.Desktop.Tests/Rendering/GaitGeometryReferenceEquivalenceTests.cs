using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Tests.Rendering.Reference;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class GaitGeometryReferenceEquivalenceTests
{
    private static readonly LogicalRect Bounds = new(100, 100, 160, 120);
    private static readonly LogicalRect Viewport = new(0, 0, 1000, 800);

    [TestMethod]
    public void CompleteGeometryKeepsEveryBitOfFrozenTransformsAndClippingDecisions()
    {
        var accepted = 0; var rejected = 0; var reduced = 0;
        foreach (var shear in new[] { -.12, 0, .035, .12 })
            foreach (var parameters in new[] { EyeLayoutParameters.Default,
                new EyeLayoutParameters(120, 20, 55, 30) })
                foreach (var eyeShape in Enum.GetValues<EyeShape>())
                {
                    var frame = CreateFrame(shear, parameters, eyeShape);
                    var shape = frame.Attachments!.SourceShape!;
                    var left = shape.Boundary.Min(p => GaitGeometry.Canvas(p).X);
                    foreach (var autonomous in new[] { false, true })
                        foreach (var viewport in new[] { Viewport,
                            new LogicalRect(Bounds.X + left - .2 - (autonomous ? 16 : 0), 0, 1000, 800),
                            new LogicalRect(Bounds.X + left + .2 - (autonomous ? 16 : 0), 0, 1000, 800) })
                            foreach (var strain in new[] { -.02, -.008, 0, .01, Math.BitIncrement(.01) })
                            {
                                var expected = FrozenGaitGeometry5bc3215.Create(frame, new(11), Bounds, viewport, strain, autonomous, 1.35);
                                var actual = GaitGeometry.Create(frame, new(11), Bounds, viewport, strain, autonomous, 1.35);
                                AssertBits(expected, actual);
                                if (expected is null) rejected++;
                                else
                                {
                                    accepted++;
                                    if (Math.Abs(expected.ScaleY - 1) < Math.Abs(strain) - 1e-8) reduced++;
                                }
                            }
                }
        Assert.IsTrue(accepted > 0 && rejected > 0 && reduced > 0,
            "The frozen comparison must exercise accepted, rejected and reduced real transforms.");
    }

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    public void ActualWalkingJumpAndSquashContoursKeepFrozenGaitWithCompleteHatAndFace()
    {
        var native = Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
            Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        using var runtime = StackedDangoSceneRuntime.CreateProduct(native);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError);
        runtime.Start(); runtime.SetUnboundedHorizontalPreview(true);
        runtime.SetViewport(new(0, 0, 2160, 1280));
        var input = new PhysicsPresentationInput("fictional-gait-equivalence", new(400, 350, 160, 120),
            DangoActionState.Idle, 0, 1, "fictional-ground", new(0, 1), 0, 1, false, true, []);
        var samples = 0; var nonIdentity = 0;
        for (var index = 0; index < 900; index++)
        {
            runtime.SetDirectionalControl(index switch
            {
                >= 300 and < 480 => new(false, true, false, false),
                >= 480 and < 660 => new(true, false, false, false),
                660 => new(false, false, true, false),
                >= 810 and < 840 => new(false, false, false, true),
                _ => DirectionalControlIntent.None,
            });
            var frame = runtime.Tick(input, TimeSpan.FromSeconds(1d / 60));
            Assert.IsNull(runtime.LastError); Assert.IsNull(runtime.AttachmentError);
            Assert.IsNull(runtime.GaitError); Assert.IsTrue(runtime.LastPhysicsFrame!.Succeeded);
            if (index < 300 || index % 10 != 0) continue;
            var bounds = DangoHostFrameBinding.Bounds(frame, 0);
            var body = frame.Bodies.Single();
            Assert.IsNotNull(body.Attachments!.Hat!.Pose);
            foreach (var strain in new[] { -.02, -.008, .01 })
            {
                var viewport = new LogicalRect(bounds.X - 500, bounds.Y - 500, 1000, 1000);
                var expected = FrozenGaitGeometry5bc3215.Create(body, frame.EntityIds.Single(), bounds, viewport, strain, false, 1);
                var actual = GaitGeometry.Create(body, frame.EntityIds.Single(), bounds, viewport, strain, false, 1);
                AssertBits(expected, actual); samples++;
                if (actual is { IsIdentity: false }) nonIdentity++;
            }
        }
        Assert.AreEqual(180, samples); Assert.IsTrue(nonIdentity > 0);
    }

    private static void AssertBits(GaitPresentationFrame? expected, GaitPresentationFrame? actual)
    {
        Assert.AreEqual(expected is null, actual is null);
        if (expected is null) return;
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected.EntityId, actual.EntityId); Assert.AreEqual(expected.Generation, actual.Generation);
        Assert.AreEqual(expected.StepIndex, actual.StepIndex);
        foreach (var (left, right) in new[] { (expected.CenterX, actual.CenterX), (expected.BottomY, actual.BottomY),
            (expected.ScaleX, actual.ScaleX), (expected.ScaleY, actual.ScaleY) })
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(left), BitConverter.DoubleToInt64Bits(right));
    }

    private static PhysicsPresentationFrame CreateFrame(double shear, EyeLayoutParameters parameters, EyeShape eyeShape)
    {
        var contour = Enumerable.Range(0, 16).Select(i => Math.PI * 2 * i / 16 + .13)
            .Select(a => new PresentationPoint(.72 * Math.Cos(a) + shear * Math.Sin(a), .72 * Math.Sin(a))).ToArray();
        var shape = AttachmentShapeGeometry.Create(contour);
        var eye = new EyeMotionSample(new(.35, -.1), .65, eyeShape, .7);
        var face = FaceLayoutSolver.Solve(shape, eye, eye with { Gaze = new(.2, .1) }, .04, parameters);
        var root = new PresentationAttachment(new(0, .8), 0);
        var pose = HatPlacementSolver.Create(shape, root)!.Fit(0)!;
        return new(true, contour, new(new(0, -.05), 0), root, PhysicsPresentationFallbackReason.None,
            new(PhysicsPresentationBackend.DeterministicFake, 0, PhysicsPresentationDiagnosticCode.FakeActive))
        {
            Attachments = new(23, 7, shape.EstimateTilt(), face)
            {
                SourceShape = shape,
                Hat = new(pose, 0, 0, -.1, .1, "Fictional exact geometry fixture")
            }
        };
    }
}
