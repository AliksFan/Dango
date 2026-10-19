using Dango.Core.Animation;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class BeltBodyAppearancePresenterTests
{
    private static BeltBodyBoundaryPoint[] Circle(double radius = .75) => Enumerable.Range(0, 256)
        .Select(i => new BeltBodyBoundaryPoint(radius * Math.Cos(i * Math.Tau / 256), radius * Math.Sin(i * Math.Tau / 256))).ToArray();
    private static BeltBodyAppearanceFrame Frame(long tick, IReadOnlyList<BeltBodyBoundaryPoint>? boundary = null) =>
        new(tick, 1d / 60, boundary ?? Circle(), 0, 0, 0, 0, true, 0, false, false, false, false, PhysicalTick: 7);

    [STATestMethod]
    public void ExactContinuousBoundaryIsDefensiveAndUnclippedAtBothBodyScales()
    {
        var points = Circle(1.2);
        var frame = Frame(0, points);
        points[0] = new(0, 0);
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        presenter.Update(frame);
        var shape = presenter.Inspect().Shape!;
        Assert.AreEqual(256, shape.Boundary.Count);
        Assert.AreEqual(1.2 * 72 / 160, shape.Boundary[0].X, 1e-12);
        Assert.IsFalse(presenter.View.ClipToBounds);
        foreach (var scale in new[] { 1d, 1.5 })
        {
            presenter.View.RenderTransform = new ScaleTransform(scale, scale);
            var body = System.Windows.Media.VisualTreeHelper.GetChild(presenter.View, 0);
            var path = Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(body))
                .Select(i => VisualTreeHelper.GetChild(body, i)).OfType<ShapePath>().First();
            var figure = path.Data.GetFlattenedPathGeometry().Figures.Single();
            var vertices = figure.Segments.OfType<PolyLineSegment>().SelectMany(s => s.Points).ToArray();
            Assert.AreEqual(256, vertices.Distinct().Count(), "Every continuous-boundary vertex must survive rendering.");
            Assert.AreEqual(160, presenter.View.Width);
        }
    }

    [STATestMethod]
    public void SleepingPhysicalTickKeepsGeometryButAdvancesBlinkAndAccessoryStamps()
    {
        var directory = System.IO.Path.Combine(TestRepository.Root, "artifacts", "belt-presenter-tests", Guid.NewGuid().ToString("N"));
        new AccessorySettingsStore().Save(directory, new([new AccessoryItem("fictional-hat", "Fictional hat", "builtin-sprout", 64, 64)]));
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default, System.IO.Path.Combine(directory, "settings.json"));
        presenter.Update(Frame(0));
        var first = presenter.Inspect();
        Assert.HasCount(1, first.Accessories);
        Assert.IsTrue(presenter.SubmitAttachment(Blink.Default(EyeSelection.Both)));
        presenter.Update(Frame(1));
        var next = presenter.Inspect();
        Assert.AreSame(first.Shape, next.Shape);
        Assert.AreEqual(1L, next.Body!.Attachments!.StepIndex);
        Assert.AreEqual(1L, next.Accessories.Single().Frame.StepIndex);
        Assert.IsLessThan(1d, next.Body.Attachments.Face.Left.Openness);
        presenter.Update(Frame(1));
        Assert.AreSame(next.Body, presenter.Inspect().Body, "Repeated paint must not advance motion.");
        presenter.ClearAttachmentComparison();
        Assert.AreEqual(1d, presenter.Inspect().Body!.Attachments!.Face.Left.Openness);
    }

    [STATestMethod]
    public void AppearanceChangePreservesSampledBodyAndNewBodyConfiguration()
    {
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        presenter.Update(Frame(0));
        var first = presenter.Inspect();
        var configuration = new DangoAppearanceConfiguration(OpaqueSrgbColor.Parse("#112233"),
            OpaqueSrgbColor.Parse("#223344"), new EyeLayoutParameters(120), new DangoBodyControls(120, 60, 40));
        presenter.SetAppearance(configuration);
        Assert.AreSame(configuration, presenter.Appearance);
        Assert.AreSame(first.Shape, presenter.Inspect().Shape);
        Assert.AreEqual(first.Tick, presenter.Inspect().Tick);
        presenter.Update(Frame(1));
        Assert.AreSame(first.Shape, presenter.Inspect().Shape);
    }

    [TestMethod]
    public void BeltGaitAllowsAcceptedShapeRangeButKeepsTenPercentAreaGate()
    {
        var gait = new GaitController(.15, .15);
        for (var i = 0; i < 60; i++) gait.Advance(new(0, 0, true, 100, 100, 8000, 0), 1d / 60);
        Assert.IsFalse(gait.OutsideNeutralShape(114, 86, 8000));
        Assert.IsTrue(gait.OutsideNeutralShape(116, 100, 8000));
        Assert.IsTrue(gait.OutsideNeutralShape(100, 84, 8000));
        Assert.IsTrue(gait.OutsideNeutralShape(100, 100, 8880));
    }

    [STATestMethod]
    [DataRow(1d)] [DataRow(1.5)]
    public void WalkingGaitPreservesScreenDisplacementGateAt160And240Widths(double scale)
    {
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default) { GaitEnabled = true };
        var accepted = 0;
        for (var tick = 0; tick < 160; tick++)
        {
            var x = tick < 60 ? 0 : (tick - 60) * .01;
            var boundary = Circle().Select(p => new BeltBodyBoundaryPoint(p.X + x, p.Y)).ToArray();
            presenter.Update(Frame(tick, boundary) with { CoreX = x, Horizontal = tick < 60 ? 0 : 1,
                GaitContext = new(scale, 100, 100, 1000, 800) });
            Assert.IsNull(presenter.GaitState.Error);
            Assert.IsLessThanOrEqualTo(1.990001, presenter.GaitState.MaximumDisplacementDip);
            if (presenter.GaitFrame is not null) accepted++;
        }
        Assert.IsGreaterThan(0, accepted, "Safety checks must retain actual walking deformation.");
    }

    [STATestMethod]
    public void InvalidBoundaryDoesNotReplaceLastSuccessfulShape()
    {
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        presenter.Update(Frame(0));
        var first = presenter.Inspect();
        Assert.ThrowsExactly<ArgumentException>(() => presenter.Update(Frame(1, [new(0, 0), new(1, 1), new(0, 1), new(1, 0)])));
        Assert.AreSame(first.Shape, presenter.Inspect().Shape);
        Assert.AreEqual(first.Tick, presenter.Inspect().Tick);
    }
}
