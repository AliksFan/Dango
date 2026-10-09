using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Point = System.Windows.Point;

namespace Dango.Desktop.Tests.Accessories;

/// <summary>Diagnostic replay attribution; timing values are evidence, never a performance pass gate.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AccessoryMovementAttributionTests
{
    private const int WarmupCount = 50;
    private const int SampleCount = 100;
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);
    private static readonly double Seconds = Step.TotalSeconds;
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    [TestCategory("NativeIntegration")]
    [TestCategory("Attribution")]
    public void RecordedRealNativeIdleAndHeldContoursAttributeEachManagedAttachmentStage()
    {
        var nativePath = Environment.GetEnvironmentVariable("DANGO_PHYSICS_MODULE_PATH") ??
            Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);
        var nativeHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath)));
        using var scene = new DesktopPhysicsScene(nativePath, new LogicalRect(400, 350, 160, 120));
        scene.SetUnboundedHorizontalPreview(true);
        for (var index = 0; index < 300; index++)
            Assert.IsTrue(scene.Step(Step).Physics.Succeeded, $"Native settling failed at {index}.");
        var baseline = scene.LastFrame!.Bodies.Single().Presentation;
        var snapshots = new List<Snapshot>();
        Capture("idle", DirectionalControlIntent.None, DangoFacingDirection.Center);
        Capture("D", new(false, true, false, false), DangoFacingDirection.Right);
        Capture("A", new(true, false, false, false), DangoFacingDirection.Left);

        var stages = new Dictionary<string, double[]> { ["NativeSceneStep"] = snapshots.Select(s => s.NativeMilliseconds).ToArray() };
        Measure("AttachmentShapeGeometry.Create", sample =>
            sample.Shape = AttachmentShapeGeometry.Create(sample.Body.Contour));
        var tilt = new SpringState();
        foreach (var sample in snapshots)
        {
            tilt = DampedSpring.AdvanceBounded(tilt, sample.Shape.EstimateTilt().Angle,
                4, .9, Seconds, -Math.PI / 18, Math.PI / 18);
            sample.Tilt = tilt.Position;
        }

        var hat = new HatMotionController(baseline);
        var prepareCosts = new double[snapshots.Count];
        var advanceCosts = new double[snapshots.Count];
        var currentCosts = new double[snapshots.Count];
        HatMotionFrame? lastHat = null;
        Measure("Hat.PrepareObserveAdvanceCurrent", sample =>
        {
            var started = Stopwatch.GetTimestamp();
            hat.Prepare(sample.Shape, sample.Body.Hat);
            prepareCosts[sample.Ordinal] = Elapsed(started);
            started = Stopwatch.GetTimestamp();
            hat.Observe(sample.Motion, sample.Shape.Height / sample.Shape.Width, Seconds);
            hat.Advance(Seconds, sample.Tilt, 0);
            advanceCosts[sample.Ordinal] = Elapsed(started);
            started = Stopwatch.GetTimestamp();
            lastHat = hat.Current;
            sample.Hat = lastHat;
            currentCosts[sample.Ordinal] = Elapsed(started);
        });
        stages.Add("Hat.Prepare", prepareCosts);
        stages.Add("Hat.ObserveAdvance", advanceCosts);
        stages.Add("Hat.Current", currentCosts);
        Assert.IsNotNull(lastHat);
        Assert.IsTrue(double.IsFinite(lastHat.S));

        Measure("FaceLayoutSolver.Solve", sample =>
        {
            var leftX = sample.Facing == DangoFacingDirection.Right ? 1 : sample.Facing == DangoFacingDirection.Left ? -.75 : 0;
            var rightX = sample.Facing == DangoFacingDirection.Right ? .75 : sample.Facing == DangoFacingDirection.Left ? -1 : 0;
            sample.Face = FaceLayoutSolver.Solve(sample.Shape,
                new(new(leftX, 0), 1, EyeShape.Round, 0),
                new(new(rightX, 0), 1, EyeShape.Round, 0),
                -sample.Tilt, EyeLayoutParameters.Default, horizontalRange: 7);
        });
        Assert.IsTrue(snapshots.All(sample => sample.Face is not null));
        Measure("GaitGeometry.CreateWithCompleteHatAndFace", sample =>
        {
            var attached = sample.Body with
            {
                Attachments = new(sample.Ordinal + 1, 1, sample.Shape.EstimateTilt(), sample.Face!)
                { SourceShape = sample.Shape, Hat = sample.Hat },
            };
            sample.Gait = GaitGeometry.Create(attached, new(1), sample.Bounds,
                new(sample.Bounds.X - 1000, sample.Bounds.Y - 1000, 2000, 2000), .008, false, 1);
        });
        Assert.IsTrue(snapshots.Any(sample => sample.Gait is not null),
            "The attribution must actually execute the complete gait geometry path.");

        var item = new AccessoryItem("fictional-held-leaf", "Fictional held movement leaf", "builtin-sprout", 64, 64)
        {
            BodyAnchorX = .228,
            BodyAnchorY = .756,
            Softness = 84.46,
            ConnectionStrength = 0,
            RotationDamping = 0,
            LockRotation = false,
            ReturnStrength = 60,
            MaxSwingDegrees = 178,
            LimitSwing = true,
            GravityInfluence = 20,
        };
        // Supply the already computed shared shape, as the normal attachment frame does.
        // Mapper time is separate from the material solver and never counted twice.
        Measure("AccessoryBodyMapper.MapWithSharedShape", sample =>
        {
            var attached = sample.Body with
            {
                Attachments = new(sample.Ordinal + 1, 1, sample.Shape.EstimateTilt(), sample.Face!) { SourceShape = sample.Shape },
            };
            var local = AccessoryBodyMapper.Map(attached, null, item.BodyAnchorX, item.BodyAnchorY);
            sample.Anchor = new(sample.Bounds.X + local.X, sample.Bounds.Y + local.Y);
        });
        var accessory = new AccessoryMotionSolver();
        AccessoryMeshFrame? lastMesh = null;
        Measure("AccessoryMotionSolver.Update", sample =>
            lastMesh = accessory.Update(sample.Ordinal + 1, Seconds, sample.Anchor, 64, 64, item));
        Assert.IsNotNull(lastMesh);
        Assert.AreEqual((long)snapshots.Count, lastMesh.StepIndex);
        Assert.IsTrue(lastMesh.Vertices.All(point => double.IsFinite(point.X) && double.IsFinite(point.Y)));

        // Both full controllers consume precisely the same captured native sequence.
        // This comparison changes only enableHatMotion, without editing product behavior.
        ReplayFullController(false);
        ReplayFullController(true);
        var phases = snapshots.GroupBy(sample => sample.Phase).Select(group =>
        {
            var measured = group.Where(sample => sample.Index >= WarmupCount).ToArray();
            Assert.HasCount(SampleCount, measured);
            return new
            {
                phase = group.Key,
                warmupCount = WarmupCount,
                sampleCount = measured.Length,
                worldXStart = group.First().Bounds.Center.X,
                worldXEnd = group.Last().Bounds.Center.X,
                boundaryPointCountMin = measured.Min(sample => sample.Shape.Boundary.Count),
                boundaryPointCountMax = measured.Max(sample => sample.Shape.Boundary.Count),
                stages = stages.ToDictionary(entry => entry.Key,
                    entry => Summarize(measured.Select(sample => entry.Value[sample.Ordinal]).ToArray())),
            };
        }).ToArray();
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-movement-attribution", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var output = Path.Combine(root, "attribution.json");
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            kind = "recorded-real-native-contour-managed-stage-attribution",
            nativeSha256 = nativeHash,
            fixedStepSeconds = Seconds,
            settlingSteps = 300,
            nativeFramesCaptured = snapshots.Count,
            timingBudgetEnforced = false,
            notes = new[]
            {
                "Synchronous contour replay does not measure visible application FPS or manual acceptance.",
                "Each stage has 50 chronological warmup frames and 100 measured frames per phase.",
                "Face-only replay uses default eyes, settled horizontal facing gaze, zero vertical gaze and production tilt spring.",
                "Full controller comparison includes actual default eye springs and identical continuous native core motion.",
                "Accessory mapper uses the precomputed shared contour shape; its wrapper allocation and coordinate translation are included.",
                "Hat composite timing includes the small timestamp cost of its three nested substage measurements.",
            },
            phases,
        }, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.WriteLine(output);
        foreach (var phase in phases)
            foreach (var stage in phase.stages)
                TestContext.WriteLine($"{phase.phase} {stage.Key}: mean={stage.Value.MeanMilliseconds:F4} ms, p95={stage.Value.P95Milliseconds:F4} ms, max={stage.Value.MaximumMilliseconds:F4} ms");

        void Capture(string phase, DirectionalControlIntent intent, DangoFacingDirection facing)
        {
            scene.SetInput(intent);
            for (var index = 0; index < WarmupCount + SampleCount; index++)
            {
                var started = Stopwatch.GetTimestamp();
                var frame = scene.Step(Step);
                var milliseconds = Elapsed(started);
                Assert.IsTrue(frame.Physics.Succeeded, $"Native {phase} capture failed at {index}.");
                var body = frame.Bodies.Single();
                var physical = frame.Physics.Bodies.Single();
                Assert.AreEqual(PhysicsPresentationFallbackReason.None, body.Presentation.FallbackReason);
                Assert.IsTrue(body.Presentation.IsVisible);
                snapshots.Add(new(snapshots.Count, phase, index, body.Presentation, body.Bounds,
                    new(scene.ContinuousCoreX(physical), physical.CoreY), facing, milliseconds));
            }
            var captured = snapshots.Where(sample => sample.Phase == phase).ToArray();
            if (intent.Right) Assert.IsTrue(captured[^1].Bounds.Center.X > captured[0].Bounds.Center.X,
                "Held D attribution must capture actual native rightward movement.");
            if (intent.Left) Assert.IsTrue(captured[^1].Bounds.Center.X < captured[0].Bounds.Center.X,
                "Held A attribution must capture actual native leftward movement.");
        }

        void Measure(string name, Action<Snapshot> operation)
        {
            var costs = new double[snapshots.Count];
            foreach (var sample in snapshots)
            {
                var started = Stopwatch.GetTimestamp();
                operation(sample);
                costs[sample.Ordinal] = Elapsed(started);
            }
            stages.Add(name, costs);
        }

        void ReplayFullController(bool enableHatMotion)
        {
            using var router = new AttachmentCommandRouter(enableHatMotion);
            var target = router.Add(new DangoEntityId(1));
            var controller = new AttachmentMotionController(router, target, baseline);
            Measure($"AttachmentMotionController.Update.Hat{(enableHatMotion ? "On" : "Off")}", sample =>
                controller.Update(sample.Body, sample.Ordinal + 1, Seconds, sample.Facing, sample.Motion));
            Assert.IsNotNull(controller.Current);
            Assert.AreEqual((long)snapshots.Count, controller.Current.StepIndex);
        }
    }

    private static double Elapsed(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    private static TimingSummary Summarize(double[] values)
    {
        Array.Sort(values);
        return new(values.Length, values.Average(), values[(int)Math.Ceiling(values.Length * .5) - 1],
            values[(int)Math.Ceiling(values.Length * .95) - 1], values[^1]);
    }
    private sealed record TimingSummary(int Count, double MeanMilliseconds, double P50Milliseconds,
        double P95Milliseconds, double MaximumMilliseconds);
    private sealed class Snapshot(int ordinal, string phase, int index, PhysicsPresentationFrame body,
        LogicalRect bounds, HatBodySample motion, DangoFacingDirection facing, double nativeMilliseconds)
    {
        public int Ordinal { get; } = ordinal;
        public string Phase { get; } = phase;
        public int Index { get; } = index;
        public PhysicsPresentationFrame Body { get; } = body;
        public LogicalRect Bounds { get; } = bounds;
        public HatBodySample Motion { get; } = motion;
        public DangoFacingDirection Facing { get; } = facing;
        public double NativeMilliseconds { get; } = nativeMilliseconds;
        public AttachmentShapeGeometry Shape { get; set; } = null!;
        public double Tilt { get; set; }
        public FaceLayout? Face { get; set; }
        public HatMotionFrame? Hat { get; set; }
        public GaitPresentationFrame? Gait { get; set; }
        public Point Anchor { get; set; }
    }
}
