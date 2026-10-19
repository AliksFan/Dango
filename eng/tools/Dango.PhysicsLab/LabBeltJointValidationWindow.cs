using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Diagnostics;
using Point = System.Windows.Point;
using Path = System.IO.Path;

namespace Dango.PhysicsLab;

/// <summary>One native eight-corner run, observed by real WPF appearance consumers on its owner thread.</summary>
internal sealed class LabBeltJointValidationWindow : Window
{
    private const double Scale = 120, DisplayScale = Scale / PhysicsLabAppearancePresenter.WorldToCanvas;
    private readonly string module, output;
    private readonly PhysicsLabAppearancePresenter appearance;
    private readonly Canvas surface = new() { ClipToBounds = false };
    private readonly TextBlock status = new() { FontSize = 14, Margin = new Thickness(0, 8, 0, 12) };
    private readonly List<CaseResult> cases = [];
    private readonly Dictionary<string, int> phaseLengths = new()
    {
        ["rest"] = 300, ["right"] = 180, ["stop-right"] = 180, ["left"] = 180, ["stop-left"] = 180,
        ["S"] = 60, ["S-release"] = 120, ["W"] = 120, ["W-release"] = 120, ["Space"] = 180,
        ["drag-compress"] = 90, ["drag-release"] = 180,
    };
    private CaseResult? current;
    private StreamWriter? trace;
    private double cameraX;
    private bool running;

    private sealed class PhaseResult
    {
        public required string Name { get; init; }
        public int Frames { get; set; }
        public int CompressedFrames { get; set; }
        public int ExtendedFrames { get; set; }
        public int HiddenEyeFrames { get; set; }
        public double MinimumStrain { get; set; }
        public double MaximumStrain { get; set; }
        public double MaximumGaitDip { get; set; }
        public double MaximumPivotError { get; set; }
        public double MaximumConnectionRatio { get; set; }
        public double MaximumBoundaryError { get; set; }
        public Dictionary<string, int> EyeSafety { get; } = [];
        public Dictionary<string, int> Suppression { get; } = [];
    }

    private sealed class CaseResult
    {
        public required string Key { get; init; }
        public int Frames { get; set; }
        public List<PhaseResult> Phases { get; } = [];
        public List<string> Errors { get; } = [];
        public List<double> ObservationMilliseconds { get; } = [];
        public List<string> Captures { get; } = [];
    }

    internal LabBeltJointValidationWindow(string module, string output, string settings)
    {
        this.module = module; this.output = output;
        appearance = new(settings) { GaitEnabled = true };
        Title = "团子·八组极值联合验证（自动）";
        Width = Math.Min(1220, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(850, SystemParameters.WorkArea.Height - 32);
        Background = LabWindow.BackgroundBrush; Foreground = LabWindow.Ink;
        FontFamily = new("Segoe UI, Microsoft YaHei");
        var root = new DockPanel { Margin = new Thickness(20) };
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "八组极值 · 身体与外观联合检查", FontSize = 20 });
        heading.Children.Add(new TextBlock { Text = "正式默认眼睛＋两件内置测试配饰 · 同一批物理帧 · 自动输入 · 不写日常设置", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });
        heading.Children.Add(status); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        surface.Background = LabWindow.BackgroundBrush;
        surface.Children.Add(appearance.View); root.Children.Add(surface); Content = root;
        appearance.View.RenderTransform = new ScaleTransform(DisplayScale, DisplayScale);
        Closed += (_, _) => { trace?.Dispose(); appearance.Dispose(); };
    }

    internal void Run()
    {
        if (running) return; running = true;
        try
        {
            UpdateLayout(); Pump();
            var physicalPassed = LabBeltGuardExperiments.Run(module, output, Observe);
            trace?.Dispose(); trace = null;
            var summaries = cases.Select(item => new
            {
                item.Key, item.Frames, item.Phases, item.Errors, item.Captures,
                BothWalkingPhases = item.Phases.Count(p => p.Name is "right" or "left") == 2 &&
                    item.Phases.Where(p => p.Name is "right" or "left").All(p => p.CompressedFrames > 0 && p.ExtendedFrames > 0),
                EyeVisibilityReviewRequired = item.Phases.Any(p => p.HiddenEyeFrames > 0),
                Complete = item.Frames == 1890 && item.Phases.Count == phaseLengths.Count &&
                    item.Phases.All(p => phaseLengths.TryGetValue(p.Name, out var expected) && p.Frames == expected),
                ObservationMilliseconds = Timing(item.ObservationMilliseconds),
            }).ToArray();
            var complete = summaries.Length == 8 && summaries.All(item => item.Complete);
            var appearancePassed = complete && summaries.All(item => item.Errors.Count == 0 && item.BothWalkingPhases);
            File.WriteAllText(Path.Combine(output, "joint-eight-corner-results.json"), JsonSerializer.Serialize(new
            {
                Passed = physicalPassed && appearancePassed, PhysicalPassed = physicalPassed, AppearancePassed = appearancePassed,
                ExpectedCases = 8, CompletedCases = summaries.Count(item => item.Complete),
                Scope = "same 80/120 x 0/100 x 0/100 native run and 12 phases; real shown WPF; default formal eyes and two fictional built-in accessories; original display gait; no OS hardware input or full-range manual acceptance",
                DisplayScale, DisplayDipPerWorld = Scale, ViewportWidth = surface.ActualWidth, ViewportHeight = surface.ActualHeight,
                Gait = new { MaximumHeightCompression = .15, MaximumWidthDeviation = .15, MaximumAreaDeviation = .10, MaximumScreenDisplacementDip = 1.99, ClipBoundary = "actual viewport" },
                PhysicalNoninterference = "complete 200-value native numerical debug snapshot and active force ledger compared around every observer call; not every diagnostic array or all native memory",
                Results = summaries,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(physicalPassed && appearancePassed ? 0 : 12);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "joint-failure.json"), JsonSerializer.Serialize(new { error = error.ToString() }));
            Application.Current.Shutdown(13);
        }
    }

    private void Observe(LabBeltValidationFrame observation)
    {
        var key = $"{observation.Controls.SizePercent:0}-{observation.Controls.Softness:0}-{observation.Controls.LandingBuild:0}";
        if (current?.Key != key)
        {
            trace?.Dispose(); trace = new(Path.Combine(output, "joint-" + key + ".jsonl"));
            current = new() { Key = key }; cases.Add(current); appearance.Reset(); cameraX = 0;
        }
        var phase = current.Phases.LastOrDefault();
        if (phase?.Name != observation.Phase)
        {
            phase = new() { Name = observation.Phase }; current.Phases.Add(phase);
        }
        var began = Stopwatch.GetTimestamp();
        var body = observation.Body;
        var input = observation.Input;
        if (Math.Abs(body.CoreX - cameraX) > 1.5) cameraX = body.CoreX - Math.Sign(body.CoreX - cameraX) * 1.5;
        var core = new Point(surface.ActualWidth / 2 + (body.CoreX - cameraX) * Scale,
            surface.ActualHeight * .78 - (body.CoreY - observation.Configuration.GroundHeight) * Scale);
        var context = new PhysicsLabGaitContext(DisplayScale, core.X - 80 * DisplayScale, core.Y - 66 * DisplayScale,
            surface.ActualWidth, surface.ActualHeight);
        Canvas.SetLeft(appearance.View, context.Left); Canvas.SetTop(appearance.View, context.Top);
        current.Frames++; phase.Frames++;
        try
        {
            var boundary = Enumerable.Range(0, body.Curve.Length / 2).Select(i => new PhysicsLabBoundaryPoint(body.Curve[2 * i], body.Curve[2 * i + 1])).ToArray();
            var frame = new PhysicsLabAppearanceFrame(observation.Tick, observation.Seconds, boundary,
                body.CoreX, body.CoreY, body.Stats[46], body.Stats[47], body.Grounded, input.Horizontal,
                input.Downward > 0, input.Stretch > 0 || body.Stats[17] > 0,
                input.JumpPressed || body.JumpAccepted, input.DragActive, context);
            appearance.Update(frame);
            Validate(observation, phase, context);
            var before = appearance.GaitState;
            var mesh = appearance.Inspect();
            appearance.Update(frame); appearance.View.InvalidateVisual();
            Check(ReferenceEquals(before, appearance.GaitState), "duplicate paint advanced gait");
            var again = appearance.Inspect();
            Check(ReferenceEquals(mesh.Body, again.Body) && mesh.Accessories.Count == again.Accessories.Count &&
                mesh.Accessories.Zip(again.Accessories).All(pair => ReferenceEquals(pair.First.Frame, pair.Second.Frame)),
                "duplicate tick advanced eyes or accessories");
            if (observation.PhaseTick % 30 == 0 || observation.PhaseTick == phaseLengths[observation.Phase] - 1)
            {
                trace!.WriteLine(JsonSerializer.Serialize(new
                {
                    observation.Tick, observation.Phase, observation.PhaseTick, context,
                    body.CoreX, body.CoreY, body.Curve, Gait = appearance.GaitState,
                    EyeSafety = mesh.Body?.Attachments?.Face.Safety,
                    Meshes = mesh.Accessories.Select(item => new { item.Layer, item.Frame.StepIndex, item.Frame.ImagePivot, item.Frame.ConnectionOffset, item.RawBodyAnchor }),
                }));
                status.Text = $"{cases.Count}/8 · {key.Replace('-', '／')} · {observation.Phase} · 帧 {observation.Tick}/1890";
                File.WriteAllText(Path.Combine(output, "joint-progress.json"), JsonSerializer.Serialize(new
                { Case = cases.Count, Key = key, observation.Tick, observation.Phase, observation.PhaseTick, Errors = current.Errors.Count }));
                Pump();
            }
            if (observation.PhaseTick == phaseLengths[observation.Phase] - 1 &&
                observation.Phase is "rest" or "right" or "left" or "S" or "W" or "Space" or "drag-compress" or "drag-release")
                Capture(key + "-" + observation.Phase);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException or System.Runtime.InteropServices.ExternalException)
        {
            if (current.Errors.Count < 12) current.Errors.Add($"{observation.Phase}/{observation.PhaseTick}: {error.Message}");
        }
        finally { current.ObservationMilliseconds.Add(Stopwatch.GetElapsedTime(began).TotalMilliseconds); }
    }

    private void Validate(LabBeltValidationFrame observation, PhaseResult phase, PhysicsLabGaitContext context)
    {
        var snapshot = appearance.Inspect();
        Check(snapshot.Tick == observation.Tick && snapshot.RuntimeError is null && snapshot.Body?.Attachments?.StepIndex == observation.Tick,
            "appearance did not consume the same successful physics tick");
        Check(snapshot.Shape is { Boundary.Count: 256 } && snapshot.Body!.IsVisible, "body shape is missing or resampled");
        var shape = snapshot.Shape!; var native = observation.Body;
        for (var i = 0; i < 256; i++)
        {
            var dx = shape.Boundary[i].X * 160 / 72 + native.CoreX - native.Curve[2 * i];
            var dy = shape.Boundary[i].Y * 160 / 72 + native.CoreY - native.Curve[2 * i + 1];
            phase.MaximumBoundaryError = Math.Max(phase.MaximumBoundaryError, Math.Sqrt(dx * dx + dy * dy));
        }
        Check(phase.MaximumBoundaryError < 1e-10, "display body differs from the successful native boundary");
        var face = snapshot.Body!.Attachments!.Face;
        Increment(phase.EyeSafety, face.Safety);
        if (!face.Left.Visible || !face.Right.Visible) phase.HiddenEyeFrames++;
        var eyes = new[] { face.Left, face.Right }.Where(eye => eye.Visible).Select(eye => EyeOutline.Create(eye).ToArray()).ToArray();
        foreach (var outline in eyes)
            foreach (var point in outline)
                Check(double.IsFinite(point.X) && double.IsFinite(point.Y) && Contains(shape.Boundary, point), "visible eye left the actual body");
        if (face.Left.Visible && face.Right.Visible)
        {
            var gap = EyeOutline.Create(face.Right).Min(p => p.X) - EyeOutline.Create(face.Left).Max(p => p.X);
            Check(gap >= 2d / 160 - 1e-10, "eyes overlap or lost their original gap");
        }
        Check(appearance.EquippedCount == 2 && appearance.RenderedAccessoryCount == 2 && snapshot.Accessories.Count == 2,
            "one or both test accessories disappeared");
        Check(snapshot.Accessories[0].Layer == AccessoryLayer.Behind && snapshot.Accessories[1].Layer == AccessoryLayer.Front,
            "front/back accessory layers were reversed");
        foreach (var item in snapshot.Accessories)
        {
            var mesh = item.Frame;
            Check(mesh.StepIndex == observation.Tick && mesh.Vertices.Count > 0 && mesh.Vertices.Count == mesh.TextureCoordinates.Count,
                "accessory mesh tick or topology differs from the body");
            Check(mesh.Vertices.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y)), "nonfinite accessory vertex");
            var pivotError = (mesh.ImagePivot - item.RawBodyAnchor - mesh.ConnectionOffset).Length;
            phase.MaximumPivotError = Math.Max(phase.MaximumPivotError, pivotError);
            Check(pivotError < 1e-7, "accessory connection detached from its actual body anchor");
            Check(mesh.ConnectionOffset.Length <= item.MaximumConnectionTravelDip + 1e-7, "accessory connection exceeded configured travel");
            phase.MaximumConnectionRatio = Math.Max(phase.MaximumConnectionRatio,
                item.MaximumConnectionTravelDip > 0 ? mesh.ConnectionOffset.Length / item.MaximumConnectionTravelDip : 0);
            Check(mesh.TriangleIndices.Count % 3 == 0, "incomplete accessory triangle");
            for (var i = 0; i < mesh.TriangleIndices.Count; i += 3)
            {
                var a = mesh.TriangleIndices[i]; var b = mesh.TriangleIndices[i + 1]; var c = mesh.TriangleIndices[i + 2];
                Check(a >= 0 && b >= 0 && c >= 0 && a < mesh.Vertices.Count && b < mesh.Vertices.Count && c < mesh.Vertices.Count, "invalid triangle index");
                var area = Cross(mesh.Vertices[a], mesh.Vertices[b], mesh.Vertices[c]);
                var uvArea = Cross(mesh.TextureCoordinates[a], mesh.TextureCoordinates[b], mesh.TextureCoordinates[c]);
                Check(item.AllowFolding || area * uvArea > 0, "nonfolding accessory triangle inverted or collapsed");
            }
        }
        var gait = appearance.GaitState;
        Check(gait.Enabled && gait.Tick == observation.Tick && gait.Error is null && gait.MaximumDisplacementDip <= 1.99 + 1e-8,
            "gait error, stale frame or excessive screen displacement");
        if (gait.DisplayedStrain != 0)
            Check(gait.HeightRatio is >= .85 && gait.WidthRatio is >= .85 and <= 1.15 && gait.AreaRatio is >= .90 and <= 1.10,
                "gait remained displayed outside the approved material-shape tolerance");
        phase.MinimumStrain = Math.Min(phase.MinimumStrain, gait.DisplayedStrain);
        phase.MaximumStrain = Math.Max(phase.MaximumStrain, gait.DisplayedStrain);
        phase.MaximumGaitDip = Math.Max(phase.MaximumGaitDip, gait.MaximumDisplacementDip);
        if (gait.DisplayedStrain < -1e-6) phase.CompressedFrames++;
        if (gait.DisplayedStrain > 1e-6) phase.ExtendedFrames++;
        if (gait.SuppressionReason is { } reason) Increment(phase.Suppression, reason);
        if (observation.Input.Downward > 0 || observation.Input.Stretch > 0 || observation.Body.Stats[17] > 0 ||
            observation.Input.DragActive || observation.Input.JumpPressed || !observation.Body.Grounded)
            Check(gait.DisplayedStrain == 0, "gait remained active during vertical/drag/jump/air suppression");
        var layer = (Canvas)((Canvas)appearance.View).Children[0];
        var all = shape.Boundary.Select(p => new Point(80 + p.X * 160, 66 - p.Y * 160))
            .Concat(eyes.SelectMany(points => points).Select(p => new Point(80 + p.X * 160, 66 - p.Y * 160)))
            .Concat(snapshot.Accessories.SelectMany(item => item.Frame.Vertices));
        foreach (var point in all)
        {
            var actual = layer.RenderTransform.Transform(point);
            var expected = appearance.GaitFrame?.Map(new LogicalPoint(point.X, point.Y)) ?? new LogicalPoint(point.X, point.Y);
            Check(Math.Abs(actual.X - expected.X) < 1e-8 && Math.Abs(actual.Y - expected.Y) < 1e-8, "body, eyes and accessories do not share the gait transform");
            Check((actual - point).Length * DisplayScale <= 1.99 + 1e-8, "actual material point exceeded gait displacement limit");
            if (appearance.GaitFrame is not null)
                Check(actual.X * DisplayScale + context.Left >= -1e-8 && actual.X * DisplayScale + context.Left <= context.ViewportWidth + 1e-8 &&
                    actual.Y * DisplayScale + context.Top >= -1e-8 && actual.Y * DisplayScale + context.Top <= context.ViewportHeight + 1e-8,
                    "gait transformed a material point outside the actual viewport");
        }
    }

    private void Capture(string name)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, "joint-" + name + ".png")); encoder.Save(file);
        current!.Captures.Add("joint-" + name + ".png"); trace!.Flush();
    }

    private static object Timing(List<double> times)
    {
        var sorted = times.Order().ToArray();
        return new { Mean = times.Count == 0 ? 0 : times.Average(), P95 = sorted.Length == 0 ? 0 : sorted[(int)Math.Ceiling((sorted.Length - 1) * .95)],
            Maximum = sorted.Length == 0 ? 0 : sorted[^1], Note = "observer+validation+occasional WPF render/dispatcher; excludes native step; not a realtime guarantee" };
    }

    private static bool Contains(IReadOnlyList<AttachmentPoint> polygon, AttachmentPoint point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i]; var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    private static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Increment(Dictionary<string, int> counts, string key) { counts.TryGetValue(key, out var value); counts[key] = value + 1; }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
