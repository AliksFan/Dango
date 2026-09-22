using System.Windows;
using System.Windows.Media;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Canvas = System.Windows.Controls.Canvas;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class GaitGeometryTests
{
    private static readonly DangoEntityId Entity = new(11);
    private static readonly LogicalRect Bounds = new(100, 100, 160, 120);
    private static readonly LogicalRect Viewport = new(0, 0, 1000, 800);

    [TestMethod]
    [DataRow(-.02)]
    [DataRow(.01)]
    public void BottomAnchorMatchesIndependentQuadraticExtremaInsteadOfControlPoints(double strain)
    {
        var frame = Frame();
        var gait = GaitGeometry.Create(frame, Entity, Bounds, Viewport, strain, false, 1.35);
        Assert.IsNotNull(gait, "The valid fixture must exercise an actual gait transform.");
        Assert.IsFalse(gait.IsIdentity);
        var curve = IndependentCurve(frame.Contour);
        var trueBottom = curve.Max(p => p.Y);
        var controlBottom = frame.Contour.Max(p => 66 - p.Y * 58);
        Assert.IsGreaterThan(.02, Math.Abs(controlBottom - trueBottom), "A control-point anchor must fail this fixture.");
        Assert.AreEqual(trueBottom, gait.BottomY, .02, "The oracle analytically evaluates every quadratic extremum.");
        Assert.AreEqual(trueBottom, curve.Select(gait.Map).Max(p => p.Y), .25);
        foreach (var point in curve) Assert.IsTrue(Distance(point, gait.Map(point)) <= 2);
    }

    [STATestMethod]
    public void EveryActualBodyEyeAndHatPointRespectsTheSharedLimitAtMaterialExtremes()
    {
        var seenNonIdentity = 0;
        foreach (var appearance in Enum.GetValues<HatAppearance>())
            foreach (var strain in new[] { -.02, -.008, .01 })
                foreach (var bend in new[] { -HatBodyFollow.DeformationLimit, HatBodyFollow.DeformationLimit })
                    foreach (var stretch in new[] { -HatBodyFollow.DeformationLimit, HatBodyFollow.DeformationLimit })
                    {
                        var frame = Frame();
                        frame = frame with
                        {
                            Attachments = frame.Attachments! with
                            {
                                Hat = frame.Attachments.Hat! with { Follow = new(0, bend, stretch, 0) },
                            },
                        };
                        var gait = GaitGeometry.Create(frame, Entity, Bounds, Viewport, strain, false, 1.35);
                        Assert.IsNotNull(gait, $"Valid {appearance} fixture unexpectedly lost gait.");
                        if (!gait.IsIdentity) seenNonIdentity++;
                        foreach (var point in DrawnPoints(frame, appearance))
                        {
                            var mapped = gait.Map(point);
                            Assert.IsTrue(Distance(point, mapped) <= 2, $"{appearance} material point exceeded 2 DIP.");
                            Assert.IsTrue(mapped.X >= 0 && mapped.X <= 160 && mapped.Y >= -16 && mapped.Y <= 120,
                                $"{appearance} escaped the existing overlay.");
                            Assert.IsTrue(Viewport.Contains(new LogicalPoint(mapped.X + Bounds.X, mapped.Y + Bounds.Y)));
                        }
                    }
        Assert.AreEqual(36, seenNonIdentity, "Always returning identity cannot satisfy geometry coverage.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ApproachingTheLeftEdgeReducesOnlyTheSharedTransform(bool autonomous)
    {
        var frame = Frame();
        var originalContour = frame.Contour.ToArray();
        var originalShape = frame.Attachments!.SourceShape;
        var points = IndependentCurve(frame.Contour);
        var left = points.Min(p => p.X);
        var viewport = new LogicalRect(Bounds.X + left - .20 - (autonomous ? 16 : 0), 0, 1000, 800);
        var unrestricted = GaitGeometry.Create(frame, Entity, Bounds, Viewport, -.02, autonomous, 1.35);
        var restricted = GaitGeometry.Create(frame, Entity, Bounds, viewport, -.02, autonomous, 1.35);
        Assert.IsNotNull(unrestricted);
        Assert.IsNotNull(restricted, "A small positive margin should allow a reduced gait.");
        Assert.IsTrue(restricted.ScaleY > unrestricted.ScaleY, "The edge fixture must actually trigger shared reduction.");
        Assert.IsTrue(restricted.ScaleY < 1, "A safe nonzero contribution remains possible.");
        Assert.AreEqual(1 - .5 * (restricted.ScaleY - 1), restricted.ScaleX, 1e-12);
        foreach (var p in points.Select(restricted.Map))
            Assert.IsTrue(p.X + Bounds.X >= viewport.X + (autonomous ? 16 : 0) - .01);
        CollectionAssert.AreEqual(originalContour, frame.Contour.ToArray());
        Assert.AreSame(originalShape, frame.Attachments.SourceShape);
    }

    [TestMethod]
    public void EyeEntirelyOutsideBodyIsUnsafeEvenWhenItClearsEveryBoundarySegment()
    {
        var frame = Frame();
        var old = frame.Attachments!;
        // Entire eye is outside the body but inside the 160 DIP hit rectangle.
        // Distance-to-boundary alone therefore cannot establish containment.
        var escaped = old.Face.Right with { Center = new(.43, 0), Width = .03, Height = .03, Angle = 0 };
        frame = frame with { Attachments = old with { Face = old.Face with { Right = escaped } } };
        Assert.IsNull(GaitGeometry.Create(frame, Entity, Bounds, Viewport, -.02, false, 1.35),
            "An unsafe original face must disable gait rather than preserve an out-of-body eye.");
    }

    private static PhysicsPresentationFrame Frame()
    {
        var contour = Enumerable.Range(0, 16).Select(i => Math.PI * 2 * i / 16 + .13)
            .Select(a => new PresentationPoint(.72 * Math.Cos(a) + .035 * Math.Sin(a), .80 * Math.Sin(a))).ToArray();
        var shape = AttachmentShapeGeometry.Create(contour);
        var eye = new EyeMotionSample(new(.35, -.1), .65, EyeShape.Soft, .7);
        var face = FaceLayoutSolver.Solve(shape, eye, eye with { Gaze = new(.2, .1) }, .04);
        var root = new PresentationAttachment(new(0, .8), 0);
        var pose = HatPlacementSolver.Create(shape, root)!.Fit(0)!;
        return new(true, contour, new(new(0, -.05), 0), root, PhysicsPresentationFallbackReason.None,
            new(PhysicsPresentationBackend.DeterministicFake, 0, PhysicsPresentationDiagnosticCode.FakeActive))
        {
            Attachments = new(23, 7, shape.EstimateTilt(), face)
            {
                SourceShape = shape,
                Hat = new(pose, 0, 0, -.1, .1, "Fixture"),
            },
        };
    }

    // Deliberately independent of AttachmentShapeGeometry's recursive flattening.
    private static LogicalPoint[] IndependentCurve(IReadOnlyList<PresentationPoint> contour)
    {
        LogicalPoint CanvasPoint(PresentationPoint p) => new(80 + p.X * 72, 66 - p.Y * 58);
        static LogicalPoint Mid(LogicalPoint a, LogicalPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        var result = new List<LogicalPoint>();
        for (var i = 0; i < contour.Count; i++)
        {
            var b = CanvasPoint(contour[i]);
            var a = Mid(CanvasPoint(contour[(i + contour.Count - 1) % contour.Count]), b);
            var c = Mid(b, CanvasPoint(contour[(i + 1) % contour.Count]));
            var samples = Enumerable.Range(0, 129).Select(j => j / 128d).ToList();
            foreach (var (u, v, w) in new[] { (a.X, b.X, c.X), (a.Y, b.Y, c.Y) })
            {
                var denominator = u - 2 * v + w;
                if (Math.Abs(denominator) < 1e-12) continue;
                var t = (u - v) / denominator;
                if (t > 0 && t < 1) samples.Add(t);
            }
            foreach (var t in samples)
                result.Add(new(a.X * (1 - t) * (1 - t) + 2 * b.X * t * (1 - t) + c.X * t * t,
                    a.Y * (1 - t) * (1 - t) + 2 * b.Y * t * (1 - t) + c.Y * t * t));
        }
        return result.ToArray();
    }

    private static IReadOnlyList<LogicalPoint> DrawnPoints(PhysicsPresentationFrame frame, HatAppearance appearance)
    {
        var body = new DeformableDangoVisual { ShowHat = false };
        var hat = new DangoHatVisual { Appearance = appearance, HatSize = 1.35 };
        body.ApplyFrame(frame); hat.ApplyFrame(frame);
        foreach (var visual in new FrameworkElement[] { body, hat })
        { visual.Measure(new(160, 120)); visual.Arrange(new Rect(0, 0, 160, 120)); visual.UpdateLayout(); }
        var bodyPath = (ShapePath)body.FindName("BodyPath");
        Assert.AreEqual(DangoContourFailure.None, body.LastContourFailure, "The synthetic contour must pass the original renderer.");
        Assert.AreEqual(Visibility.Visible, bodyPath.Visibility);
        Assert.IsNotNull(bodyPath.Data, "Actual body geometry is required; the fallback cannot satisfy this test.");
        Assert.AreEqual(Visibility.Collapsed, ((ShapePath)body.FindName("FallbackBodyPath")).Visibility);
        var paths = new List<(ShapePath Path, FrameworkElement Root)> { (bodyPath, body) };
        foreach (var name in new[] { "LeftEyeLayer", "RightEyeLayer" })
            paths.AddRange(((Canvas)body.FindName(name)).Children.OfType<ShapePath>().Select(p => (p, (FrameworkElement)body)));
        paths.AddRange(((Canvas)hat.FindName("HatLayer")).Children.OfType<ShapePath>().Select(p => (p, (FrameworkElement)hat)));
        var result = new List<LogicalPoint>();
        foreach (var (path, root) in paths)
        {
            Assert.IsNotNull(path.Data, "Every observed body, eye and hat path must contain actual geometry.");
            var geometry = path.Data.GetFlattenedPathGeometry(.0005, ToleranceType.Absolute);
            var transform = path.TransformToAncestor(root);
            foreach (var figure in geometry.Figures)
            {
                var points = new[] { figure.StartPoint }.Concat(figure.Segments.SelectMany(segment => segment switch
                {
                    LineSegment line => new[] { line.Point },
                    PolyLineSegment poly => poly.Points.ToArray(),
                    _ => throw new AssertFailedException("Expected independently flattened drawing paths."),
                }));
                foreach (var point in points)
                {
                    var mapped = transform.Transform(geometry.Transform?.Transform(point) ?? point);
                    result.Add(new(mapped.X, mapped.Y));
                }
            }
        }
        return result;
    }

    private static double Distance(LogicalPoint a, LogicalPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
