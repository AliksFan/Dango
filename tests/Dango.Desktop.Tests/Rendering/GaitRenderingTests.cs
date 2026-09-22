using System.Windows;
using System.Windows.Media;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Path = System.Windows.Shapes.Path;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class GaitRenderingTests
{
    [STATestMethod]
    [DataRow(HatAppearance.Sprout)] [DataRow(HatAppearance.TopHat)] [DataRow(HatAppearance.Bowler)]
    public void BodyEyesAndIndependentHatApplyOneSharedTransformWhileRootAndDebugStayPut(HatAppearance appearance)
    {
        using var runtime = GaitRuntimeTests.Ready();
        var frame = runtime.LastFrame!.Bodies[0];
        var attachment = frame.Attachments!;
        var gait = new GaitPresentationFrame(runtime.EntityIds[0], attachment.Generation, attachment.StepIndex, 80, 110, 1.01, .98);
        var body = new DeformableDangoVisual { ShowHat = false, ShowDebugGeometry = true, Appearance = appearance };
        var hat = new DangoHatVisual { HatSize = 1.35, Appearance = appearance };
        body.ApplyFrame(frame); hat.ApplyFrame(frame);
        Layout(body); Layout(hat);
        var bodyPath = (Path)body.FindName("BodyPath");
        var eye = (Path)((System.Windows.Controls.Canvas)body.FindName("LeftEyeLayer")).Children[0];
        var leaf = (Path)((System.Windows.Controls.Canvas)hat.FindName("HatLayer")).Children[0];
        var probe = new System.Windows.Point(80, 40);
        var oldBody = bodyPath.TransformToAncestor(body).Transform(probe);
        var oldEye = eye.TransformToAncestor(body).Transform(probe);
        var oldHat = leaf.TransformToAncestor(hat).Transform(probe);
        var debug = (UIElement)body.FindName("DebugLayer");
        var oldDebug = debug.TransformToAncestor(body).Transform(probe);
        body.ApplyFrame(frame, gait); hat.ApplyFrame(frame, gait);
        Layout(body); Layout(hat);
        eye = (Path)((System.Windows.Controls.Canvas)body.FindName("LeftEyeLayer")).Children[0];
        AssertMapped(oldBody, bodyPath.TransformToAncestor(body).Transform(probe), gait);
        AssertMapped(oldEye, eye.TransformToAncestor(body).Transform(probe), gait);
        AssertMapped(oldHat, leaf.TransformToAncestor(hat).Transform(probe), gait);
        Assert.AreEqual(oldDebug, debug.TransformToAncestor(body).Transform(probe));
        Assert.IsTrue(body.RenderTransform.Value.IsIdentity);
        body.ApplyFrame(frame, null); hat.ApplyFrame(frame, null);
        Layout(body); Layout(hat);
        Assert.AreEqual(oldBody, bodyPath.TransformToAncestor(body).Transform(probe));
        Assert.AreEqual(oldHat, leaf.TransformToAncestor(hat).Transform(probe));
    }

    [STATestMethod]
    [DataRow(HatAppearance.Sprout)] [DataRow(HatAppearance.TopHat)] [DataRow(HatAppearance.Bowler)]
    public void ActualHatOutlinesStayWithinTwoDipAndBothHostHitPoliciesKeepTheirOriginalRectangle(HatAppearance appearance)
    {
        using var runtime = GaitRuntimeTests.Ready();
        var stacked = runtime.LastFrame!;
        var frame = stacked.Bodies[0];
        var bounds = DangoHostFrameBinding.Bounds(stacked, 0);
        var body = new DeformableDangoVisual { ShowHat = false, Appearance = appearance };
        var hat = new DangoHatVisual { Appearance = appearance, HatSize = 1.35 };
        var canvas = new System.Windows.Controls.Canvas { Width = 1600, Height = 900, Background = Brushes.Transparent };
        canvas.Children.Add(body); canvas.Children.Add(hat);
        var fixture = new System.Windows.Window { Content = canvas, Width = 1620, Height = 950, ShowInTaskbar = false,
            ShowActivated = false, Opacity = 0, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        try
        {
        fixture.Show(); fixture.UpdateLayout();
        var probes = new[] { new Point(-.1, 60), new Point(.1, 60), new Point(159.9, 60), new Point(160.1, 60),
            new Point(80, -.1), new Point(80, .1), new Point(80, 119.9), new Point(80, 120.1), new Point(80, 60) };
        foreach (var strain in new[] { -.02, .01 })
        {
            DangoHostFrameBinding.Apply(body, hat, stacked with { Gait = null }, 0, appearance: appearance, hatSize: 1.35);
            fixture.UpdateLayout();
            var originalPlacement = body.TransformToAncestor(canvas).Transform(new Point(0, 0));
            var hits = probes.Select(p => IsBodyHit(canvas.InputHitTest(new(p.X + bounds.X, p.Y + bounds.Y)), body)).ToArray();
            Assert.IsTrue(hits[^1], "A real WPF body hit must exist before comparing gait.");
            var paths = ((System.Windows.Controls.Canvas)hat.FindName("HatLayer")).Children.Cast<Path>().ToArray();
            var original = paths.Select(p => RenderedPoints(p, hat)).ToArray();
            var gait = GaitGeometry.Create(frame, stacked.EntityIds[0], bounds, new(0, 0, 1600, 900), strain, false, 1.35);
            Assert.IsNotNull(gait);
            var walking = stacked with { Gait = gait };
            DangoHostFrameBinding.Apply(body, hat, walking, 0, appearance: appearance, hatSize: 1.35);
            canvas.UpdateLayout();
            paths = ((System.Windows.Controls.Canvas)hat.FindName("HatLayer")).Children.Cast<Path>().ToArray();
            for (var i = 0; i < paths.Length; i++)
            {
                var current = RenderedPoints(paths[i], hat);
                Assert.AreEqual(original[i].Length, current.Length);
                for (var j = 0; j < current.Length; j++)
                {
                    AssertMapped(original[i][j], current[j], gait);
                    Assert.IsTrue((current[j] - original[i][j]).Length <= 2);
                    Assert.IsTrue(current[j].X >= 0 && current[j].X <= 160 && current[j].Y >= -16 && current[j].Y <= 120);
                }
            }
            for (var i = 0; i < probes.Length; i++)
            {
                var p = new LogicalPoint(probes[i].X + bounds.X, probes[i].Y + bounds.Y);
                Assert.AreEqual(hits[i], IsBodyHit(canvas.InputHitTest(new(p.X, p.Y)), body),
                    $"Probe {probes[i]}, hit {canvas.InputHitTest(new(p.X, p.Y))?.GetType().Name}, root {body.RenderSize}, strain {strain}, position {originalPlacement} -> {body.TransformToAncestor(canvas).Transform(new Point(0, 0))}");
                Assert.AreEqual(DangoHostFrameBinding.HitTest(stacked, p), DangoHostFrameBinding.HitTest(walking, p));
            }
            // Frame identity is attached to the presented source, never to the latest runtime epoch.
            DangoHostFrameBinding.Apply(body, hat, walking with { Gait = gait with { StepIndex = gait.StepIndex + 1 } }, 0);
            Assert.IsTrue(((UIElement)body.FindName("GaitBodyLayer")).RenderTransform.Value.IsIdentity);
            Assert.IsTrue(((UIElement)hat.FindName("GaitLayer")).RenderTransform.Value.IsIdentity);
        }
        }
        finally { fixture.Close(); }
    }

    private static Point[] RenderedPoints(Path path, UIElement ancestor)
    {
        var transform = path.TransformToAncestor(ancestor);
        return path.Data.GetFlattenedPathGeometry(.001, ToleranceType.Absolute).Figures
            .SelectMany(f => new[] { f.StartPoint }.Concat(f.Segments.OfType<PolyLineSegment>().SelectMany(s => s.Points)))
            .Select(transform.Transform).ToArray();
    }
    private static bool IsBodyHit(IInputElement? hit, DeformableDangoVisual body) =>
        hit is DependencyObject element && (ReferenceEquals(element, body) || body.IsAncestorOf(element));
    private static void Layout(FrameworkElement element) { element.Measure(new(160, 120)); element.Arrange(new(0, 0, 160, 120)); element.UpdateLayout(); }
    private static void AssertMapped(System.Windows.Point before, System.Windows.Point after, GaitPresentationFrame gait)
    {
        var expected = gait.Map(new(before.X, before.Y));
        Assert.AreEqual(expected.X, after.X, 1e-8);
        Assert.AreEqual(expected.Y, after.Y, 1e-8);
    }
}
