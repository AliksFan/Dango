using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class BodyMaterialNativeTests
{
    private static string Module => Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName);

    [TestMethod, TestCategory("NativeIntegration")]
    public void NativeBodyModelVersionIsRequiredIndependentlyOfTheModuleAbi()
    {
        using var world = DangoPhysicsWorld.Create(Module);
        world.RequireBodyMaterialModel(DangoBodyMaterial.NativeModelVersion);
        var error = Assert.ThrowsExactly<DangoPhysicsException>(() =>
            world.RequireBodyMaterialModel(DangoBodyMaterial.NativeModelVersion + 1));
        Assert.AreEqual(DangoPhysicsFailureKind.AbiMismatch, error.Kind);
    }

    [TestMethod, TestCategory("NativeIntegration")]
    public void DefaultFatToSlimNativeLandingMeetsTheApprovedDirectionAndRatio()
    {
        // Fixed before running: 60 Hz, sample frames 1200..1259 after a natural
        // release at the same position; no sleeping, alignment or fitted scale.
        var rows = new List<(int Build, double Width, double Height, double Area)>();
        foreach (var build in new[] { 0, 25, 50, 75, 100 })
        {
            var configuration = DangoBodyMaterial.Configure(new(100, 50, build));
            using var world = DangoPhysicsWorld.Create(Module, capacity: 1,
                configuration: DangoBodyMaterial.Configure(DangoBodyControls.Default));
            world.RequireBodyMaterialModel(DangoBodyMaterial.NativeModelVersion);
            var id = world.Add(0, 0, configuration);
            double width = 0, height = 0, area = 0;
            for (var frame = 0; frame < 1260; frame++)
            {
                var state = world.Step(TimeSpan.FromSeconds(1d / 60), [new(id)]);
                Assert.IsTrue(state.Succeeded, $"build={build}, frame={frame}, error={state.ErrorCode}");
                if (frame < 1200) continue;
                var body = state.Bodies.Single();
                var xs = Enumerable.Range(0, body.PointCount).Select(i => (double)body.Contour[2 * i]).ToArray();
                var ys = Enumerable.Range(0, body.PointCount).Select(i => (double)body.Contour[2 * i + 1]).ToArray();
                width += xs.Max() - xs.Min();
                height += ys.Max() - ys.Min();
                area += body.SignedArea;
            }
            rows.Add((build, width / 60, height / 60, area / 60));
        }
        var path = Path.Combine(TestRepository.Root, "artifacts", "face-body", "closeout", "landing-shapes.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(rows.Select(row => new
        { row.Build, row.Width, row.Height, row.Area, Ratio = row.Width / row.Height }), new JsonSerializerOptions { WriteIndented = true }));
        for (var i = 1; i < rows.Count; i++)
            Assert.IsTrue(rows[i].Width / rows[i].Height <= rows[i - 1].Width / rows[i - 1].Height * 1.01,
                $"Fat-to-slim direction reversed between {rows[i - 1].Build} and {rows[i].Build}.");
        Assert.IsTrue(rows[0].Width / rows[0].Height >= rows[^1].Width / rows[^1].Height * 1.08,
            "The fat-end aspect ratio must be at least 1.08 times the slim end.");
        Assert.IsTrue(rows[0].Width > rows[^1].Width && rows[0].Height < rows[^1].Height,
            "The effect must flatten and widen the body, rather than only shrinking it.");
    }
}
