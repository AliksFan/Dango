using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class StackedDangoSceneRuntimeTests
{
    private static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60d);

    [TestMethod]
    public void FormalRuntimeConsumesTheUnifiedModule()
    {
        var source = File.ReadAllText(Path.Combine(TestRepository.Root,
            "src", "Dango.Desktop", "Rendering", "Dango", "StackedDangoSceneRuntime.cs"));
        StringAssert.Contains(source, "new DesktopPhysicsScene");
        Assert.IsFalse(source.Contains("new Box2DSelfPhysicsPresentationWorld", StringComparison.Ordinal));
        StringAssert.Contains(source, "public const int MinimumDangoCount = 1;");
        StringAssert.Contains(source, "public const int MaximumDangoCount = 3;");
    }

    [TestMethod]
    public void SimulationFailureLatchesTheLastValidFrameInsteadOfRespawning()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "Physics",
            "Presentation",
            "Box2DStackedContactPresentationWorld.cs");
        var source = File.ReadAllText(path);

        StringAssert.Contains(source, "restartBlocked = true;");
        StringAssert.Contains(source, "CreateLatchedFrame(inputs");
        Assert.IsFalse(
            source.Contains("ReleaseNativeWorld();\n            return CreateFallback(\n                inputs,\n                PhysicsPresentationFallbackReason.SimulationUnstable", StringComparison.Ordinal),
            "模拟异常不得在下一帧自动新建 world 并让附加团子回到出生点。");
    }

    [TestMethod]
    public void RehostTranslatesTheExistingSceneWithoutCreatingAnotherRuntime()
    {
        using var runtime = new StackedDangoSceneRuntime();
        runtime.Start();
        var primary = CreatePrimary(new LogicalRect(400, 500, 160, 120));
        StackedPhysicsPresentationFrame? before = null;
        for (var index = 0; index < 180; index++)
        {
            before = runtime.Tick(primary, FixedStep);
        }

        runtime.Stop();
        runtime.Start();
        var translated = CreatePrimary(new LogicalRect(120, 260, 160, 120));
        var after = runtime.Tick(translated, FixedStep);

        Assert.IsNotNull(before);
        Assert.AreEqual(translated.AuthoritativeBounds, after.AuthoritativeBounds[0]);
        Assert.HasCount(1, after.Bodies);
        Assert.AreSame(after, runtime.LastFrame);
    }

    [TestMethod]
    public void RepeatedStartAndRegenerationKeepWindowGroundInView()
    {
        using var runtime = new StackedDangoSceneRuntime();
        var desktop = CreatePrimary(new LogicalRect(1100, 920, 160, 120));
        var window = CreatePrimary(new LogicalRect(200, 380, 160, 120));
        runtime.Start();
        for (var i = 0; i < 300; i++)
        {
            runtime.Tick(desktop, FixedStep);
        }
        var before = runtime.LastFrame!;
        runtime.Stop();
        // Show -> Loaded -> StartMotion, then SetVisibility -> StartMotion.
        runtime.Start();
        runtime.Start();
        var after = runtime.Tick(window, FixedStep);
        Assert.IsFalse(after.ConstraintsReleased);
        Assert.AreEqual(before.RenderOffsets[0].Y, after.RenderOffsets[0].Y, 0.1);
        runtime.RegenerateDango();
        for (var i = 0; i < 300; i++)
        {
            after = runtime.Tick(window, FixedStep);
            Assert.IsFalse(after.ConstraintsReleased, $"regenerate step={i}");
        }
        Assert.IsGreaterThan(0, after.ContactCount);
        var bottom = window.AuthoritativeBounds.Center.Y + after.RenderOffsets[0].Y -
            after.Bodies[0].Contour.Min(p => p.Y) * window.AuthoritativeBounds.Height / 2;
        Assert.IsLessThanOrEqualTo(window.AuthoritativeBounds.Bottom, bottom);
        Assert.IsGreaterThan(window.AuthoritativeBounds.Y, bottom);
    }

    [TestMethod]
    public void ResizeAndTranslatedDragKeepTheSamePhysicsCoordinates()
    {
        static List<StackedPhysicsPresentationFrame> Simulate(bool resize)
        {
            using var runtime = new StackedDangoSceneRuntime();
            runtime.Start();
            var original = CreatePrimary(new LogicalRect(1100, 920, 160, 120));
            var target = resize ? CreatePrimary(new LogicalRect(200, 380, 160, 120)) : original;
            for (var i = 0; i < 300; i++)
            {
                runtime.Tick(original, FixedStep);
            }
            // Rehosting cancels existing gestures; begin a new gesture after migration.
            runtime.Tick(target, FixedStep);
            runtime.BeginPhysicalDrag(resize ? new LogicalPoint(280, 350) : new LogicalPoint(1180, 890));
            var frames = new List<StackedPhysicsPresentationFrame>();
            for (var i = 0; i < 120; i++)
            {
                frames.Add(runtime.Tick(target, FixedStep));
            }
            return frames;
        }
        // The single-body native allocation probe uses a process-wide baseline;
        // run the reference and translated world sequentially, not concurrently.
        var reference = Simulate(false);
        var resized = Simulate(true);
        for (var i = 0; i < 120; i++)
        {
            var a = reference[i];
            var b = resized[i];
            Assert.IsFalse(a.ConstraintsReleased);
            Assert.IsFalse(b.ConstraintsReleased);
            Assert.AreEqual(a.RenderOffsets[0].X, b.RenderOffsets[0].X, 0.001);
            Assert.AreEqual(a.RenderOffsets[0].Y, b.RenderOffsets[0].Y, 0.001);
        }
    }

    [TestMethod]
    public void PopulationGrowsToThreeAndRemovesSelectedEntity()
    {
        using var runtime = new StackedDangoSceneRuntime();
        runtime.Start();
        var primary = CreatePrimary(new LogicalRect(400, 500, 160, 120));

        Assert.AreEqual(1, runtime.DangoCount);
        Assert.HasCount(1, runtime.Tick(primary, FixedStep).Bodies);
        Assert.IsFalse(runtime.RemoveDango());

        Assert.IsTrue(runtime.AddDango());
        Assert.IsTrue(runtime.AddDango());
        Assert.IsFalse(runtime.AddDango());
        Assert.AreEqual(3, runtime.DangoCount);
        Assert.HasCount(3, runtime.Tick(primary, FixedStep).Bodies);
        var removed = runtime.EntityIds[1];
        runtime.SelectEntity(removed);
        Assert.IsTrue(runtime.RemoveDango());
        Assert.AreEqual(2, runtime.DangoCount);
        Assert.IsFalse(runtime.Tick(primary, FixedStep).EntityIds.Contains(removed));
    }

    private static PhysicsPresentationInput CreatePrimary(LogicalRect bounds) =>
        new(
            "primary-dango",
            bounds,
            DangoActionState.Idle,
            1_000,
            17,
            "island-grab",
            new LogicalVector(0, -1),
            0,
            1,
            false,
            true,
            []);
}
