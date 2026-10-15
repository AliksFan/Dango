using System.Xml.Linq;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DangoContourRenderingTests
{
    [TestMethod]
    public void AcceptedDesktopMappingUsesEqualHorizontalAndVerticalScales()
    {
        var origin = DangoStreamGeometryFactory.Map(new PresentationPoint(0, 0), 160, 120);
        var right = DangoStreamGeometryFactory.Map(new PresentationPoint(1, 0), 160, 120);
        var top = DangoStreamGeometryFactory.Map(new PresentationPoint(0, 1), 160, 120);

        Assert.AreEqual(80d, origin.X, 1e-10);
        Assert.AreEqual(66d, origin.Y, 1e-10);
        Assert.AreEqual(72d, right.X - origin.X, 1e-10);
        Assert.AreEqual(72d, origin.Y - top.Y, 1e-10);
    }

    [TestMethod]
    public void ValidContourBuildsOneClosedFilledStreamGeometry()
    {
        var contour = Ellipse(12);

        var result = DangoStreamGeometryFactory.TryCreate(contour, 160, 120);

        Assert.IsTrue(result.Succeeded, result.Failure.ToString());
        Assert.IsInstanceOfType<System.Windows.Media.StreamGeometry>(result.Geometry);
        var path = System.Windows.Media.PathGeometry.CreateFromGeometry(result.Geometry!);
        Assert.HasCount(1, path.Figures);
        Assert.IsTrue(path.Figures[0].IsClosed);
        Assert.IsTrue(path.Figures[0].IsFilled);
    }

    [TestMethod]
    public void RoundedSkinUsesBoundedQuadraticSegmentsRatherThanPassingThroughNodes()
    {
        var contour = Ellipse(16);
        var result = DangoStreamGeometryFactory.TryCreate(contour, 160, 120);
        Assert.IsTrue(result.Succeeded);
        var figure = System.Windows.Media.PathGeometry.CreateFromGeometry(result.Geometry!).Figures[0];
        Assert.IsTrue(figure.Segments.All(segment => segment is
            System.Windows.Media.QuadraticBezierSegment or System.Windows.Media.PolyQuadraticBezierSegment));
        var oldTop = 66 - 0.82 * 72;
        Assert.IsGreaterThan(oldTop + 0.1, result.Geometry!.Bounds.Top, "轮廓应圆滑靠近最高节点，而不是穿过尖点。");
        Assert.IsLessThan(oldTop + 2, result.Geometry.Bounds.Top, "不能通过明显缩小身体制造圆润。");
        var mapped = contour.Select(p => new System.Windows.Point(80 + p.X * 72, 66 - p.Y * 72)).ToArray();
        var polygonArea = Math.Abs(Enumerable.Range(0, 16).Sum(i =>
            mapped[i].X * mapped[(i + 1) % 16].Y - mapped[(i + 1) % 16].X * mapped[i].Y) / 2);
        Assert.IsGreaterThan(polygonArea * 0.95, result.Geometry.GetArea());
        Assert.IsLessThanOrEqualTo(polygonArea, result.Geometry.GetArea());
    }

    [TestMethod]
    public void RoundedSkinStaysInsideTheConvexSkeletonAndKeepsContinuousTangents()
    {
        var contour = Ellipse(16);
        var mapped = contour.Select(p => DangoStreamGeometryFactory.Map(p, 160, 120)).ToArray();
        var result = DangoStreamGeometryFactory.TryCreate(contour, 160, 120);
        var figure = System.Windows.Media.PathGeometry.CreateFromGeometry(result.Geometry!).Figures[0];
        var controls = figure.Segments.SelectMany(segment => segment switch
        {
            System.Windows.Media.QuadraticBezierSegment q => new[] { q.Point1, q.Point2 },
            System.Windows.Media.PolyQuadraticBezierSegment q => q.Points.ToArray(),
            _ => throw new AssertFailedException("Unexpected segment"),
        }).ToArray();
        Assert.HasCount(32, controls);
        var start = figure.StartPoint;
        for (var i = 0; i < 16; i++)
        {
            var control = controls[i * 2];
            var end = controls[i * 2 + 1];
            var nextControl = controls[((i + 1) % 16) * 2];
            Assert.IsLessThan(1e-10, ((end - control) - (nextControl - end)).Length);
            for (var sample = 0; sample <= 20; sample++)
            {
                var t = sample / 20d;
                var x = (1 - t) * (1 - t) * start.X + 2 * t * (1 - t) * control.X + t * t * end.X;
                var y = (1 - t) * (1 - t) * start.Y + 2 * t * (1 - t) * control.Y + t * t * end.Y;
                for (var edge = 0; edge < 16; edge++)
                {
                    var a = mapped[edge];
                    var b = mapped[(edge + 1) % 16];
                    var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
                    Assert.IsLessThanOrEqualTo(1e-8, cross, "曲线不得鼓出凸骨架。");
                }
            }
            start = end;
        }
    }

    [STATestMethod]
    public void TopInwardForceRendersActualSkeletonAtThreeStrengths()
    {
        var canvas = new System.Windows.Controls.Canvas
        {
            Width = 570,
            Height = 300,
            Background = System.Windows.Media.Brushes.AliceBlue,
        };
        var strengths = new[] { 0d, 0.5d, 1d };
        for (var column = 0; column < strengths.Length; column++)
        {
            using var world = new Box2DSelfPhysicsPresentationWorld(
                Dango.Desktop.Physics.Native.Box2DNativeLoader.DefaultLibraryPath,
                Dango.Desktop.Physics.Tuning.DangoSelfPhysicsParameters.CreateDefault() with { TopInwardForce = strengths[column] });
            var input = new PhysicsPresentationInput("inward-fixture", new Dango.Core.Coordinates.LogicalRect(0, 0, 160, 120),
                Dango.Core.Animation.DangoActionState.Idle, 1000, 17, "fixture-ground", new Dango.Core.Coordinates.LogicalVector(0, -1),
                0, 1, false, true, []);
            StackedPhysicsPresentationFrame? frame = null;
            for (var step = 0; step < 900; step++)
            {
                frame = world.Step(input, TimeSpan.FromSeconds(1d / 60d), DangoSelfPhysicsControlState.None);
                Assert.IsFalse(world.IsRestartBlocked);
                Assert.AreEqual(PhysicsPresentationFallbackReason.None, frame.Bodies[0].FallbackReason);
            }
            for (var row = 0; row < 2; row++)
            {
                var visual = new DeformableDangoVisual { ShowDebugGeometry = row == 1 };
                visual.ApplyFrame(frame!.Bodies[0]);
                Assert.AreEqual(DangoContourFailure.None, visual.LastContourFailure);
                System.Windows.Controls.Canvas.SetLeft(visual, 10 + column * 190);
                System.Windows.Controls.Canvas.SetTop(visual, 25 + row * 145);
                canvas.Children.Add(visual);
            }
            var label = new System.Windows.Controls.TextBlock { Text = $"内收力 {strengths[column]:F1}", FontSize = 14 };
            System.Windows.Controls.Canvas.SetLeft(label, 50 + column * 190);
            canvas.Children.Add(label);
        }
        canvas.Measure(new System.Windows.Size(570, 300));
        canvas.Arrange(new System.Windows.Rect(0, 0, 570, 300));
        canvas.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1140, 600, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = TestEvidence.Create("top-inward", "comparison.png");
        encoder.Save(stream);
    }

    [STATestMethod]
    public void ActualVisualUsesSharedDebugMappingAndRendersAComparison()
    {
        using var world = new Dango.Desktop.Physics.Presentation.Box2DSelfPhysicsPresentationWorld(
            Dango.Desktop.Physics.Native.Box2DNativeLoader.DefaultLibraryPath,
            Dango.Desktop.Physics.Tuning.DangoSelfPhysicsParameters.CreateDefault() with { TopInwardForce = 0.5 });
        var input = new PhysicsPresentationInput("render-fixture", new Dango.Core.Coordinates.LogicalRect(0, 0, 160, 120),
            Dango.Core.Animation.DangoActionState.Idle, 1000, 17, "fixture-ground", new Dango.Core.Coordinates.LogicalVector(0, -1),
            0, 1, false, true, []);
        StackedPhysicsPresentationFrame? frame = null;
        for (var i = 0; i < 600; i++)
        {
            frame = world.Step(input, TimeSpan.FromSeconds(1d / 60d), DangoSelfPhysicsControlState.None);
            Assert.IsFalse(world.IsRestartBlocked);
        }
        var bodyFrame = frame!.Bodies[0];
        var root = new System.Windows.Controls.Canvas
        {
            Width = 380,
            Height = 300,
            Background = System.Windows.Media.Brushes.AliceBlue,
        };
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                var visual = new DeformableDangoVisual { ShowDebugGeometry = row == 1 };
                visual.ApplyFrame(bodyFrame);
                Assert.AreEqual(DangoContourFailure.None, visual.LastContourFailure);
                if (column == 0)
                {
                    var points = bodyFrame.Contour.Select(p => DangoStreamGeometryFactory.Map(p, 160, 120)).ToArray();
                    var old = new System.Windows.Media.StreamGeometry();
                    using (var context = old.Open())
                    {
                        context.BeginFigure(points[0], true, true);
                        for (var i = 0; i < 16; i++)
                        {
                            var next = points[(i + 1) % 16];
                            context.BezierTo(points[i] + (next - points[(i + 15) % 16]) / 6,
                                next - (points[(i + 2) % 16] - points[i]) / 6, next, true, false);
                        }
                    }
                    ((System.Windows.Shapes.Path)visual.FindName("BodyPath")).Data = old;
                    ((System.Windows.Controls.Canvas)visual.FindName("DebugLayer")).RenderTransform =
                        new System.Windows.Media.MatrixTransform(80d / 72, 0, 0, 60d / 72, 80 - 80 * 80d / 72, 60 - 66 * 60d / 72);
                }
                else if (row == 1)
                {
                    var debug = (System.Windows.Controls.Canvas)visual.FindName("DebugLayer");
                    Assert.HasCount(49, debug.Children.Cast<System.Windows.UIElement>().ToArray());
                    var radial = (System.Windows.Shapes.Line)debug.Children[1];
                    var expectedCore = DangoStreamGeometryFactory.Map(new PresentationPoint(0, 0), 160, 120);
                    var expectedNode = DangoStreamGeometryFactory.Map(bodyFrame.Contour[0], 160, 120);
                    Assert.AreEqual(expectedCore.X, radial.X1);
                    Assert.AreEqual(expectedCore.Y, radial.Y1);
                    Assert.AreEqual(expectedNode.X, radial.X2);
                    Assert.AreEqual(expectedNode.Y, radial.Y2);
                }
                System.Windows.Controls.Canvas.SetLeft(visual, 10 + column * 190);
                System.Windows.Controls.Canvas.SetTop(visual, 25 + row * 145);
                root.Children.Add(visual);
            }
        }
        foreach (var (label, left) in new[] { ("原渲染", 50d), ("圆滑渲染", 240d) })
        {
            var text = new System.Windows.Controls.TextBlock { Text = label, FontSize = 14 };
            System.Windows.Controls.Canvas.SetLeft(text, left);
            root.Children.Add(text);
        }
        root.Measure(new System.Windows.Size(380, 300));
        root.Arrange(new System.Windows.Rect(0, 0, 380, 300));
        root.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(760, 600, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = TestEvidence.Create("render-rounding", "comparison.png");
        encoder.Save(stream);
    }

    [TestMethod]
    public void InvalidContoursAreRejectedBeforeWpfReceivesThem()
    {
        var reversed = Ellipse(12).Reverse().ToArray();
        var selfIntersecting = new[]
        {
            new PresentationPoint(-0.7, -0.7),
            new PresentationPoint(0.7, 0.7),
            new PresentationPoint(-0.7, 0.7),
            new PresentationPoint(0.7, -0.7),
        };
        var footSpike = Ellipse(12).ToArray();
        footSpike[9] = new PresentationPoint(-0.72, -0.98);

        Assert.AreEqual(DangoContourFailure.Reversed, DangoContourValidator.Validate(reversed));
        Assert.AreEqual(DangoContourFailure.SelfIntersecting, DangoContourValidator.Validate(selfIntersecting));
        Assert.AreEqual(DangoContourFailure.FootLikeSpike, DangoContourValidator.Validate(footSpike));
    }

    [TestMethod]
    public void VisualXamlUsesThreePlainLayersWithoutMouthOrEffects()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "Rendering",
            "Dango",
            "DeformableDangoVisual.xaml");
        var root = XDocument.Load(path).Root!;
        var names = root.Descendants()
            .Select(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(name => name is not null)
            .ToArray();

        CollectionAssert.Contains(names, "BodyPath");
        CollectionAssert.Contains(names, "LeftEyeLayer");
        CollectionAssert.Contains(names, "RightEyeLayer");
        CollectionAssert.Contains(names, "HatLayer");
        Assert.IsFalse(names.Any(name => name!.Contains("Mouth", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(root.Descendants().Any(element =>
            element.Name.LocalName.Contains("Effect", StringComparison.OrdinalIgnoreCase) ||
            element.Name.LocalName.Contains("Gradient", StringComparison.OrdinalIgnoreCase)));
        var body = root.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "BodyPath");
        Assert.IsNull(body.Attribute("Stroke"));
        Assert.AreEqual("#FFFFE8C2", (string?)body.Attribute("Fill"), ignoreCase: true);
    }

    [TestMethod]
    public void RendererCannotOwnOrStepAPhysicsWorld()
    {
        var rendererType = typeof(DeformableDangoVisual);
        Assert.IsFalse(rendererType.GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)
            .Any(field => typeof(IPhysicsPresentationWorld).IsAssignableFrom(field.FieldType)));
        Assert.IsFalse(rendererType.GetMethods().Any(method => method.Name == "Step"));
        Assert.IsTrue(typeof(IPhysicsPresentationSink).IsAssignableFrom(rendererType));
    }

    private static IReadOnlyList<PresentationPoint> Ellipse(int count) =>
        Enumerable.Range(0, count)
            .Select(index => (Math.PI * 2 * index) / count)
            .Select(angle => new PresentationPoint(Math.Cos(angle) * 0.72, Math.Sin(angle) * 0.82))
            .ToArray();
}
