using System.IO;
using System.Windows;
using System.Windows.Controls;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Diagnostics;
using Dango.Desktop.Rendering.Visuals;
using Path = System.IO.Path;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class PhysicsLabInspectionTests
{
    [STATestMethod]
    public void InspectionReturnsEmptyBeforeFrameAndReadOnlyRenderedMeshSnapshotAfterUpdate()
    {
        using var fixture = new Fixture();
        new AccessorySettingsStore().Save(fixture.Root, new([
            new("rear-test", "Fictional rear", "builtin-top-hat", 64, 64)
            {
                Layer = AccessoryLayer.Behind, SizePercent = 65, Softness = 40,
                ConnectionStrength = 0, GravityInfluence = 100, MaxConnectionTravelPercent = 50,
            },
            new("front-test", "Fictional front", "builtin-sprout", 64, 64)
            {
                Layer = AccessoryLayer.Front, SizePercent = 80, Softness = 70,
                ConnectionStrength = 0, GravityInfluence = 100, MaxConnectionTravelPercent = 125,
                AllowFolding = true,
            },
        ]));

        using var presenter = new PhysicsLabAppearancePresenter(fixture.Settings);
        var empty = presenter.Inspect();
        Assert.AreEqual(-1L, empty.Tick);
        Assert.IsNull(empty.Body);
        Assert.IsNull(empty.Shape);
        Assert.AreEqual(0, empty.Accessories.Count);
        Assert.IsNull(empty.RuntimeError);

        presenter.Update(Frame(0));
        presenter.Update(Frame(1, 1));
        presenter.Update(Frame(2, 2));
        var first = presenter.Inspect();
        var gait = presenter.GaitState;
        var eyes = presenter.CurrentAttachments;
        var eyeDiagnostic = presenter.EyeDiagnostic;
        var meshVisuals = RenderedVisuals(presenter);

        Assert.AreEqual(2L, first.Tick);
        Assert.IsNotNull(first.Body);
        Assert.IsNotNull(first.Shape);
        Assert.AreEqual(2, first.Accessories.Count);
        Assert.IsNull(first.RuntimeError);
        Assert.AreEqual(2, meshVisuals.Count);
        Assert.IsTrue(first.Accessories.Any(item => item.Layer == AccessoryLayer.Behind));
        Assert.IsTrue(first.Accessories.Any(item => item.Layer == AccessoryLayer.Front));

        foreach (var item in first.Accessories)
        {
            var visual = meshVisuals.Single(candidate =>
                (ReferenceEquals(candidate.Parent, BehindLayer(presenter)) && item.Layer == AccessoryLayer.Behind) ||
                (ReferenceEquals(candidate.Parent, FrontLayer(presenter)) && item.Layer == AccessoryLayer.Front));
            Assert.AreSame(visual.Frame, item.Frame);
            Assert.AreEqual(2L, item.Frame.StepIndex);
            Assert.IsTrue(item.Frame.Vertices.Count > 0);
            Assert.IsTrue(item.Frame.TriangleIndices.Count > 0);
            Assert.IsTrue(item.Width > 0);
            Assert.IsTrue(item.Height > 0);
            Assert.AreEqual(item.AllowFolding, item.Frame.AllowFolding);
            Assert.IsTrue((item.Frame.ImagePivot - item.RawBodyAnchor - item.Frame.ConnectionOffset).Length < 1e-8,
                "Rendered image pivot must equal the mapped raw body anchor plus the actual mesh connection offset.");
            Assert.IsTrue(item.Frame.ConnectionOffset.Length <= item.MaximumConnectionTravelDip + 1e-8,
                "Actual connection offset must stay inside the configured size-derived travel limit.");
        }

        var repeated = presenter.Inspect();
        Assert.AreSame(first.Body, repeated.Body);
        Assert.AreSame(first.Shape, repeated.Shape);
        Assert.AreEqual(first.Tick, repeated.Tick);
        Assert.AreEqual(first.RuntimeError, repeated.RuntimeError);
        Assert.AreEqual(gait, presenter.GaitState);
        Assert.AreSame(eyes, presenter.CurrentAttachments);
        Assert.AreEqual(eyeDiagnostic, presenter.EyeDiagnostic);
        Assert.AreEqual(2, repeated.Accessories.Count);
        for (var i = 0; i < first.Accessories.Count; i++)
            Assert.AreSame(first.Accessories[i].Frame, repeated.Accessories[i].Frame);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<PhysicsLabAccessoryInspection>)repeated.Accessories).Clear());
    }

    private static List<AccessoryMeshVisual> RenderedVisuals(PhysicsLabAppearancePresenter presenter)
    {
        var view = (Canvas)presenter.View;
        var gaitLayer = (Canvas)view.Children[0];
        return ((Canvas)gaitLayer.Children[0]).Children.OfType<AccessoryMeshVisual>()
            .Concat(((Canvas)gaitLayer.Children[4]).Children.OfType<AccessoryMeshVisual>()).ToList();
    }

    private static Canvas BehindLayer(PhysicsLabAppearancePresenter presenter) =>
        (Canvas)((Canvas)((Canvas)presenter.View).Children[0]).Children[0];

    private static Canvas FrontLayer(PhysicsLabAppearancePresenter presenter) =>
        (Canvas)((Canvas)((Canvas)presenter.View).Children[0]).Children[4];

    private static PhysicsLabAppearanceFrame Frame(long tick, double x = 0) => new(tick, 1d / 60,
        Enumerable.Range(0, 256).Select(index =>
        {
            var angle = index * 2 * Math.PI / 256;
            return new PhysicsLabBoundaryPoint(x + .7 * Math.Cos(angle), .7 * Math.Sin(angle));
        }).ToArray(), x, 0, 0, 0, true, 0, false, false, false, false);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Dango-Lab-Inspection-" + Guid.NewGuid().ToString("N"));
        public string Settings => Path.Combine(Root, "settings.json");
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, true);
    }
}
