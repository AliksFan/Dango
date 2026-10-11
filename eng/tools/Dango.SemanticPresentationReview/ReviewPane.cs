using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics.Configuration;
using Dango.Physics;
using Dango.Physics.Presentation;
using ShapePath = System.Windows.Shapes.Path;

namespace Dango.SemanticPresentationReview;

internal sealed class GateFailure(string oracle) : Exception(oracle)
{
    internal string Oracle { get; } = oracle;
}

internal sealed class ReviewPane : IDisposable
{
    private sealed record BodyVisual(DeformableDangoVisual Visual, DangoPhysicsPresentationAdapter Adapter);
    private readonly Dictionary<DangoEntityId, BodyVisual> visuals = [];
    private readonly SemanticPresentationSampler sampler = new();
    private readonly IReadOnlyList<SemanticPresentationSnapshot> publications;
    private readonly Dictionary<DangoEntityId, DangoEntityId> localIds = [];
    private readonly string mutation;
    private readonly System.Windows.Shapes.Line floor = new() { IsHitTestVisible = false, StrokeThickness = 1 };
    private int publicationCursor;
    private double latestTick;
    internal SemanticPresentationReplica Replica { get; }
    internal Canvas Scene { get; } = new() { ClipToBounds = true };
    internal Grid Surface { get; } = new();
    internal SemanticPresentedFrame? Current { get; private set; }
    internal List<SemanticPoint> ActualCenters { get; private set; } = [];
    internal int Steps { get; private set; }
    internal int Draws { get; private set; }
    internal int SupportedSteps { get; private set; }
    internal int RemovedSteps { get; private set; }
    internal double MaximumCenterError { get; private set; }
    internal double MaximumContourError { get; private set; }
    internal double MaximumPenetration { get; private set; }
    internal double MaximumSupportGap { get; private set; }
    internal double CameraBias { get; }
    private double CameraX => (Current?.Bodies.Average(body => body.Authority.CoreX) ?? 0) + CameraBias;
    private double OriginX => Scene.ActualWidth / 2 - CameraX * 80;
    private double OriginY => Scene.ActualHeight - 32 - .72 * 60;

    internal ReviewPane(string library, IReadOnlyList<SemanticPresentationSnapshot> trace, string name, double cameraBias, string mutation = "none",
        SemanticPresentationReplica? shared = null)
    {
        this.mutation = mutation; CameraBias = cameraBias;
        Replica = shared ?? new(library); Replica.BindEpoch("semantic-fixture"); sampler.BindEpoch("semantic-fixture");
        publications = trace.Where((_, index) => index % 3 == 0 || index == trace.Count - 1).ToArray();
        PushNext();
        Surface.RowDefinitions.Add(new() { Height = GridLength.Auto }); Surface.RowDefinitions.Add(new());
        var label = new TextBlock { Text = name, Margin = new Thickness(16, 10, 16, 10) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "HomeMuted"); Surface.Children.Add(label);
        Scene.SetResourceReference(Panel.BackgroundProperty, "HomeBackground"); Grid.SetRow(Scene, 1); Surface.Children.Add(Scene);
        floor.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "HomeLine"); Scene.Children.Add(floor);
    }

    private void PushNext()
    {
        var next = publications[publicationCursor++];
        if (!sampler.Push(next)) throw new GateFailure("semantic-buffer");
        latestTick = next.Tick;
    }

    internal void AdvanceTo(int tick)
    {
        while (Steps < tick)
        {
            var next = Steps + 1;
            while (latestTick < next) PushNext();
            Current = Replica.Advance(sampler.Sample(next)); Steps++;
            foreach (var body in Current.Bodies)
            {
                if (localIds.TryGetValue(body.Authority.EntityId, out var before) && before != body.LocalBody.EntityId)
                    throw new GateFailure("survivor-recreated");
                localIds[body.Authority.EntityId] = body.LocalBody.EntityId;
                if (!visuals.TryGetValue(body.Authority.EntityId, out var item))
                {
                    var visual = new DeformableDangoVisual { ShowHat = false, IsHitTestVisible = false };
                    item = new(visual, new(DangoPhysicsDefaults.Current)); visuals.Add(body.Authority.EntityId, item); Scene.Children.Add(visual);
                }
                item.Visual.ApplyFrame(item.Adapter.Present(body.LocalBody, SemanticPresentationSampler.FixedStep, DangoFacingDirection.Center), null);
                if (mutation == "fallback" && next == 120)
                {
                    ((ShapePath)item.Visual.FindName("BodyPath")).Visibility = Visibility.Collapsed;
                    ((ShapePath)item.Visual.FindName("FallbackBodyPath")).Visibility = Visibility.Visible;
                }
            }
            foreach (var removed in visuals.Keys.Except(Current.Bodies.Select(body => body.Authority.EntityId)).ToArray())
                if (mutation != "stale-visual") { Scene.Children.Remove(visuals[removed].Visual); visuals.Remove(removed); }
            if (Current.Bodies.Any(body => body.Authority.SupportEntityId is not null)) SupportedSteps++;
            if (Current.Bodies.Count == 1) RemovedSteps++;
            DrawLast(); Audit();
        }
    }

    internal void DrawLast()
    {
        if (Current is null) return;
        floor.X1 = 0; floor.X2 = Scene.ActualWidth; floor.Y1 = floor.Y2 = Scene.ActualHeight - 32;
        foreach (var body in Current.Bodies)
        {
            var visual = visuals[body.Authority.EntityId].Visual;
            Canvas.SetLeft(visual, OriginX + body.Authority.CoreX * 80 - 80);
            Canvas.SetTop(visual, OriginY - body.Authority.CoreY * 60 - (mutation == "wrong-origin" ? 60 : 66));
        }
        Scene.UpdateLayout(); Draws++;
    }

    internal void Audit()
    {
        if (Current is null) throw new GateFailure("missing-frame");
        if (!visuals.Keys.ToHashSet().SetEquals(Current.Bodies.Select(body => body.Authority.EntityId)) ||
            Scene.Children.OfType<DeformableDangoVisual>().Count() != Current.Bodies.Count)
            throw new GateFailure("visual-membership");
        var contours = new Dictionary<DangoEntityId, IReadOnlyList<SemanticPoint>>();
        ActualCenters = [];
        foreach (var body in Current.Bodies)
        {
            var visual = visuals[body.Authority.EntityId].Visual;
            var path = (ShapePath)visual.FindName("BodyPath");
            var fallback = (ShapePath)visual.FindName("FallbackBodyPath");
            if (visual.LastContourFailure != DangoContourFailure.None || path.Visibility != Visibility.Visible || fallback.Visibility == Visibility.Visible || path.Data is null)
                throw new GateFailure("actual-fallback");
            var transform = path.TransformToAncestor(Scene);
            SemanticPoint ToNative(Point point)
            {
                var painted = transform.Transform(point);
                return new((painted.X - OriginX) / 80, (OriginY - painted.Y) / 60);
            }
            var center = ToNative(new(80, 66)); ActualCenters.Add(center);
            var centerError = Math.Max(Math.Abs(center.X - body.Authority.CoreX), Math.Abs(center.Y - body.Authority.CoreY));
            MaximumCenterError = Math.Max(MaximumCenterError, centerError);
            if (!double.IsFinite(centerError) || centerError > 1e-5) throw new GateFailure("actual-authority-center");
            var actual = SampleActualPath(path.Data).Select(ToNative).ToArray();
            var contourError = actual.Zip(body.DrawnContour, (a, b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y))).Max();
            MaximumContourError = Math.Max(MaximumContourError, contourError);
            if (!double.IsFinite(contourError) || contourError > 1e-5) throw new GateFailure("actual-contour-map");
            contours.Add(body.Authority.EntityId, actual);
            if (Current.Bodies.Count == 1 && (body.Authority.SupportEntityId is not null || Current.Pairs.Count != 0 ||
                Replica.LastPrescribedInput!.StartPairs.Count != 0 || Replica.LastPrescribedInput.EndPairs.Count != 0 ||
                Replica.LastPrescribedInput.Bodies[0].StartSupportEntityId is not null || Replica.LastPrescribedInput.Bodies[0].EndSupportEntityId is not null))
                throw new GateFailure("withdrawn-support");
        }
        for (var a = 0; a < Current.Bodies.Count; a++)
            for (var b = a + 1; b < Current.Bodies.Count; b++)
            {
                var first = Current.Bodies[a]; var second = Current.Bodies[b];
                var contact = SemanticDrawnGeometry.Contact(contours[first.Authority.EntityId], contours[second.Authority.EntityId]);
                MaximumPenetration = Math.Max(MaximumPenetration, contact.PenetrationRatio);
                if (contact.PenetrationRatio > .031) throw new GateFailure("actual-display-penetration");
                if (first.Authority.SupportEntityId == second.Authority.EntityId || second.Authority.SupportEntityId == first.Authority.EntityId)
                {
                    MaximumSupportGap = Math.Max(MaximumSupportGap, contact.Gap);
                    if (contact.Gap > .008) throw new GateFailure("actual-supported-gap");
                }
            }
    }

    // Read the renderer's applied curve, not a second factory call or cached physics contour.
    private static IReadOnlyList<Point> SampleActualPath(Geometry geometry)
    {
        var path = PathGeometry.CreateFromGeometry(geometry);
        if (path.Figures.Count != 1 || !path.Figures[0].IsClosed) throw new GateFailure("actual-curve-shape");
        var figure = path.Figures[0]; var start = figure.StartPoint; var samples = new List<Point>();
        void Quadratic(Point control, Point end)
        {
            for (var index = 0; index < 8; index++)
            {
                var t = index / 8d; var u = 1 - t;
                samples.Add(new(u * u * start.X + 2 * u * t * control.X + t * t * end.X,
                    u * u * start.Y + 2 * u * t * control.Y + t * t * end.Y));
            }
            start = end;
        }
        foreach (var segment in figure.Segments)
            switch (segment)
            {
                case QuadraticBezierSegment curve: Quadratic(curve.Point1, curve.Point2); break;
                case PolyQuadraticBezierSegment curves:
                    for (var index = 0; index < curves.Points.Count; index += 2) Quadratic(curves.Points[index], curves.Points[index + 1]);
                    break;
                default: throw new GateFailure("actual-curve-kind");
            }
        if (samples.Count != 128) throw new GateFailure("actual-curve-count");
        return samples;
    }

    internal object Evidence() => new { Steps, Draws, SupportedSteps, RemovedSteps, MaximumCenterError, MaximumContourError,
        MaximumPenetration, MaximumSupportGap, CameraBias, tick = Current?.Tick, nativeOrdinal = Replica.LastNativeTrace?.StepOrdinal };

    public void Dispose() => Replica.Dispose();
}
