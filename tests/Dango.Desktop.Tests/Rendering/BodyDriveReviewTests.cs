using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

/// <summary>D1 opt-in, real product Runtime replay. No window, installation or product DLL replacement.</summary>
[TestClass]
public sealed class BodyDriveReviewTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1d / 60);
    private static readonly PhysicsPresentationInput Input = new("drive-fictional", new(7920, 390, 160, 120),
        DangoActionState.Idle, 0, 1, "ground", new(0, 1), 0, 1, false, true, []);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [TestMethod]
    public void StrictSupportRejectsShiftedLossAndLongerContactOutageEvenWithEqualTotals()
    {
        var baseline = new[] { new SupportPoint(false, 0), new SupportPoint(true, 1), new SupportPoint(false, 0), new SupportPoint(true, 1) };
        var shifted = new[] { new SupportPoint(true, 1), new SupportPoint(false, 0), new SupportPoint(false, 0), new SupportPoint(true, 1) };
        var gates = new List<Gate>();
        AddSupportGates("synthetic", baseline, shifted, gates);
        Assert.IsFalse(gates.Single(g => g.Name.EndsWith("/ground-support", StringComparison.Ordinal)).Passed);
        Assert.IsTrue(gates.Single(g => g.Name.EndsWith("/zero-contact-frames", StringComparison.Ordinal)).Passed);
        Assert.IsFalse(gates.Single(g => g.Name.EndsWith("/longest-zero-contact-run", StringComparison.Ordinal)).Passed);
        gates.Clear();
        AddSupportGates("identical", baseline, baseline, gates);
        Assert.IsTrue(gates.All(g => g.Passed));
        gates.Clear();
        AddSupportGates("additional-contact-loss", baseline,
            new[] { new SupportPoint(false, 0), new SupportPoint(true, 0), new SupportPoint(false, 0), new SupportPoint(true, 1) }, gates);
        Assert.IsFalse(gates.Single(g => g.Name.EndsWith("/zero-contact-frames", StringComparison.Ordinal)).Passed);
    }

    [TestMethod]
    [TestCategory("BodyDriveReview")]
    [DoNotParallelize]
    public void ExistingRuntimeTracesRejudgeStrictSupportWithoutPhysicsReplay()
    {
        if (Environment.GetEnvironmentVariable("DANGO_DRIVE_REVIEW_SUPPORT") != "1")
        {
            Assert.Inconclusive("Opt-in support rejudge requires DANGO_DRIVE_REVIEW_SUPPORT=1 after the complete replay finishes.");
            return;
        }
        var output = Path.Combine(RepositoryRoot(), "artifacts", "body-drive", "d1");
        Directory.CreateDirectory(output);
        var gates = new List<Gate>();
        foreach (var configuration in Configurations())
        foreach (var sequence in Sequences())
        {
            var label = $"{configuration.Name}/{sequence.Name}";
            try
            {
                var expectedFrames = sequence.Segments.Sum(s => s.Count);
                var baseline = ReadSupportTrace(Path.Combine(output, $"{configuration.Name}-{sequence.Name}-P00.jsonl.gz"), expectedFrames);
                var candidate = ReadSupportTrace(Path.Combine(output, $"{configuration.Name}-{sequence.Name}-P10.jsonl.gz"), expectedFrames);
                gates.Add(new(label + "/trace-integrity", true, $"P00/P10 each contain {expectedFrames} ordered frames"));
                foreach (var segment in sequence.Segments.Where(s => s.Direction != 0 && s.Count == 600))
                    AddSupportGates(label + (segment.Direction > 0 ? "/R" : "/L"),
                        baseline.Skip(segment.Start + 120).Take(480).ToArray(),
                        candidate.Skip(segment.Start + 120).Take(480).ToArray(), gates);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or UnauthorizedAccessException)
            {
                gates.Add(new(label + "/trace-integrity", false, error.Message));
            }
        }
        File.WriteAllText(Path.Combine(output, "support-results.json"), JsonSerializer.Serialize(new
        {
            Sampling = "Each sustained direction: relative frames 120..599, 480 points; Grounded and Contacts from native body state",
            Gates = gates
        }, Json));
        var failed = gates.Where(g => !g.Passed).ToArray();
        Assert.AreEqual(0, failed.Length, string.Join(Environment.NewLine, failed.Select(g => g.Name + ": " + g.Detail)));
    }

    [TestMethod]
    public void FrozenBandRmsUsesDemeanRectangularWindowAndParsevalNormalization()
    {
        var sine = Enumerable.Range(0, 480).Select(i => 7 + 3 * Math.Sin(2 * Math.PI * 8 * i / 480)).ToArray();
        Assert.AreEqual(3 / Math.Sqrt(2), BandRms(sine), 1e-11);
        Assert.AreEqual(0, BandRms(Enumerable.Repeat(7d, 480).ToArray()), 1e-12);
        var outside = Enumerable.Range(0, 480).Select(i => Math.Sin(2 * Math.PI * 48 * i / 480)).ToArray();
        Assert.AreEqual(0, BandRms(outside), 1e-11);
    }

    [TestMethod]
    [TestCategory("BodyDriveReview")]
    [DoNotParallelize]
    public void RealRuntimeTwoByTwoExportsEveryFrameBeforeJudgingTheFrozenGates()
    {
        var c0 = Environment.GetEnvironmentVariable("DANGO_DRIVE_REVIEW_C0");
        var c1 = Environment.GetEnvironmentVariable("DANGO_DRIVE_REVIEW_C1");
        var f5 = Environment.GetEnvironmentVariable("DANGO_DRIVE_REVIEW_F5");
        if (string.IsNullOrWhiteSpace(c0) || string.IsNullOrWhiteSpace(c1) || string.IsNullOrWhiteSpace(f5))
        {
            Assert.Inconclusive("Opt-in review requires DANGO_DRIVE_REVIEW_C0, C1 and F5 explicit DLL paths.");
            return;
        }
        foreach (var path in new[] { c0, c1, f5 }) Assert.IsTrue(File.Exists(path), $"Missing review DLL: {path}");
        var output = Path.Combine(RepositoryRoot(), "artifacts", "body-drive", "d1");
        Directory.CreateDirectory(output);
        var gates = new List<Gate>();
        var comparisons = new List<object>();
        var configurations = Configurations().ToArray();
        File.WriteAllText(Path.Combine(output, "runtime-manifest.json"), JsonSerializer.Serialize(new
        {
            Algorithm = "D0: demean only; rectangular N480; k4..40; sqrt(2*sum(absDFT^2)/N^2)",
            FixedStepTicks = Step.Ticks, NativeStep = (float)Step.TotalSeconds, UnitScale = 72,
            Viewport = new LogicalRect(0, 0, 16000, 900), Input,
            MaterialVersion = DangoBodyMaterial.ModelVersion,
            Modules = new[] { Module("C0", c0), Module("C1", c1), Module("F5", f5) },
            Configurations = configurations.Select(c => new { c.Name, c.Body, Parameters = DangoBodyMaterial.Configure(c.Body) }),
            Host = "Single synchronous 60Hz owner; unbounded preview; no quiet-idle host; no autonomous input; original gait eligibility retained",
            Camera = "No camera transform; raw scene origin and rolling X offset recorded every frame",
            NativeDiagnostic = "17 points core then 16 nodes; stride6 x,y,vx,vy,mass,actualContactCount; local world coordinates",
            ReviewLimits = "These numeric gates do not replace existing shape, accessory, jump/drag, autonomous-short-walk or manual gates"
        }, Json));

        foreach (var configuration in configurations)
        foreach (var sequence in Sequences())
        {
            var historical = Replay(f5, false, false, configuration.Body, sequence,
                Path.Combine(output, $"{configuration.Name}-{sequence.Name}-F5.jsonl"));
            var p00 = Replay(c0, false, true, configuration.Body, sequence,
                Path.Combine(output, $"{configuration.Name}-{sequence.Name}-P00.jsonl"));
            var p01 = Replay(c0, true, true, configuration.Body, sequence,
                Path.Combine(output, $"{configuration.Name}-{sequence.Name}-P01.jsonl"));
            var p10 = Replay(c1, false, true, configuration.Body, sequence,
                Path.Combine(output, $"{configuration.Name}-{sequence.Name}-P10.jsonl"));
            var p11 = Replay(c1, true, true, configuration.Body, sequence,
                Path.Combine(output, $"{configuration.Name}-{sequence.Name}-P11.jsonl"));
            var label = $"{configuration.Name}/{sequence.Name}";
            foreach (var (name, run) in new[] { ("F5", historical), ("P00", p00), ("P01", p01), ("P10", p10), ("P11", p11) })
                gates.Add(new($"{label}/{name}/health", run.Failures.Count == 0, string.Join("; ", run.Failures)));
            EqualNative(label + "/C0-F5", historical, p00, gates);
            EqualNative(label + "/P00-P01", p00, p01, gates);
            EqualNative(label + "/P10-P11", p10, p11, gates);
            if (sequence.Name.StartsWith("short", StringComparison.Ordinal))
                comparisons.Add(new { Configuration = configuration.Name, Sequence = sequence.Name,
                    C0 = Reversal(p00, sequence), C1 = Reversal(p10, sequence) });
            foreach (var segment in sequence.Segments.Where(s => s.Direction != 0 && s.Count == 600))
            {
                if (new[] { p00, p01, p10, p11 }.Any(r => r.Samples.Count < segment.Start + 780)) continue;
                var a = Measure(p00, segment); var b = Measure(p10, segment);
                var displayA = Measure(p01, segment); var displayB = Measure(p11, segment);
                var prefix = label + (segment.Direction > 0 ? "/R" : "/L");
                // Near-zero baselines are explicitly separate absolute values, never hidden by a ratio.
                RelativeGate(prefix + "/speed", a.SpeedDipPerSecond, b.SpeedDipPerSecond, gates);
                RelativeGate(prefix + "/displacement", a.DisplacementDip, b.DisplacementDip, gates);
                gates.Add(new(prefix + "/stop-distance", b.StopDistanceDip <= a.StopDistanceDip + Math.Max(a.StopDistanceDip * .1, 1),
                    $"C0={a.StopDistanceDip:R}, C1={b.StopDistanceDip:R} DIP"));
                var limit = configuration.Body.IsDefault ? .8 : 1.05;
                gates.Add(new(prefix + "/native-height-band", b.HeightBandRms <= a.HeightBandRms * limit,
                    $"C0={a.HeightBandRms:R}, C1={b.HeightBandRms:R}, multiplier={limit:R}"));
                gates.Add(new(prefix + "/compression", b.CompressionFraction <= a.CompressionFraction + .01,
                    $"C0={a.CompressionFraction:R}, C1={b.CompressionFraction:R}"));
                gates.Add(new(prefix + "/display-height-band", displayB.DisplayHeightBandRms <= displayA.DisplayHeightBandRms,
                    $"P01={displayA.DisplayHeightBandRms:R}, P11={displayB.DisplayHeightBandRms:R}"));
                gates.Add(new(prefix + "/visible-gait", displayA.MinimumScaleY < .997 && displayA.MaximumScaleY > 1.002 &&
                    displayB.MinimumScaleY < .997 && displayB.MaximumScaleY > 1.002,
                    $"P01=[{displayA.MinimumScaleY:R},{displayA.MaximumScaleY:R}], P11=[{displayB.MinimumScaleY:R},{displayB.MaximumScaleY:R}]"));
                AddSupportGates(prefix,
                    p00.Samples.Skip(segment.Start + 120).Take(480).Select(s => new SupportPoint(s.Grounded, s.Contacts)).ToArray(),
                    p10.Samples.Skip(segment.Start + 120).Take(480).Select(s => new SupportPoint(s.Grounded, s.Contacts)).ToArray(), gates);
                comparisons.Add(new { Configuration = configuration.Name, Sequence = sequence.Name, segment.Direction, C0 = a, C1 = b,
                    P01 = displayA, P11 = displayB });
            }
        }
        // Evidence is durable even when the candidate is rejected; do not relax the frozen assertions.
        File.WriteAllText(Path.Combine(output, "runtime-results.json"), JsonSerializer.Serialize(new
        {
            Comparisons = comparisons, Gates = gates,
            RemainingGates = new[] { "force/torque capture", "original short-walk 7.5s/200DIP", "jump/drag regression", "original shape/eye/hat/viewport gates", "Windows/manual acceptance" }
        }, Json));
        var failed = gates.Where(g => !g.Passed).ToArray();
        Assert.AreEqual(0, failed.Length, string.Join(Environment.NewLine, failed.Select(g => g.Name + ": " + g.Detail)));
    }

    private static Run Replay(string path, bool gait, bool diagnostics, DangoBodyControls controls, Sequence sequence, string destination)
    {
        var result = new Run();
        using var file = File.Create(destination + ".gz");
        using var compressed = new GZipStream(file, CompressionLevel.Fastest);
        using var writer = new StreamWriter(compressed, new UTF8Encoding(false));
        using var runtime = StackedDangoSceneRuntime.CreateProduct(path);
        if (!runtime.TryInitialize()) { result.Failures.Add("Initialize: " + runtime.LastError); return result; }
        runtime.Start();
        runtime.SetViewport(new(0, 0, 16000, 900));
        runtime.SetUnboundedHorizontalPreview(true);
        if (!runtime.TryApplyBody(controls)) { result.Failures.Add("Apply: " + runtime.BodyPreviewError); return result; }
        runtime.GaitEnabled = gait;
        // No quiet-idle host: WanderEligible is false. Existing unbounded-preview gait eligibility is true.
        runtime.Wander.Next = (minimum, _) => minimum;
        using var native = diagnostics ? new DiagnosticReader(path, runtime) : null;
        var parameters = DangoBodyMaterial.Configure(controls);
        foreach (var segment in sequence.Segments)
        for (var i = 0; i < segment.Count; i++)
        {
            var index = result.Samples.Count;
            var intent = new DirectionalControlIntent(segment.Direction < 0, segment.Direction > 0, false, false);
            runtime.SetDirectionalControl(intent);
            var frame = runtime.Tick(Input, Step);
            var physics = runtime.LastPhysicsFrame;
            if (physics is null || physics.Bodies.Count != 1)
            { result.Failures.Add($"Frame {index}: missing native body"); return result; }
            var body = physics.Bodies[0];
            var scene = (DesktopPhysicsScene)Field(runtime, "scene");
            var origin = scene.ToHost(new(0, 0));
            var offset = (double)Field(scene, "horizontalCoreOffset");
            var points = native?.Read(body.EntityId.Value);
            var xs = body.Contour.Where((_, n) => n % 2 == 0).Select(x => (double)x).ToArray();
            var ys = body.Contour.Where((_, n) => n % 2 == 1).Select(x => (double)x).ToArray();
            var height = ys.Max() - ys.Min();
            var scaleY = frame.Gait?.ScaleY ?? 1;
            var attachment = frame.Bodies[0].Attachments;
            var contour = attachment?.SourceShape?.Boundary.Select(GaitGeometry.Canvas).ToArray() ?? [];
            var mapped = contour.Select(p => frame.Gait?.Map(p) ?? p).ToArray();
            var size = controls.SizePercent / 100;
            var displayHeight = mapped.Length > 0 ? (mapped.Max(p => p.Y) - mapped.Min(p => p.Y)) * size : height * 72 * scaleY;
            var mass = points is null ? 0 : Enumerable.Range(0, 17).Sum(n => (double)points[n * 6 + 4]);
            double Weighted(int component) => points is null ? double.NaN :
                Enumerable.Range(0, 17).Sum(n => (double)points[n * 6 + component] * points[n * 6 + 4]) / mass;
            var groundNodes = points is null ? [] : Enumerable.Range(1, 16).Where(n => points[n * 6 + 5] > 0).ToArray();
            var nearGroundNodes = points is null ? [] : Enumerable.Range(1, 16)
                .Where(n => points[n * 6 + 1] - parameters.NodeRadius <= parameters.GroundHeight + .035).ToArray();
            var sample = new Sample(index, segment.Direction, body.CoreX + offset, body.CoreY, height, xs.Max() - xs.Min(),
                scaleY, displayHeight, body.IsGrounded, body.GroundContactCount, SnapshotHash(physics),
                points is null ? null : FloatHash(points), points is null ? null : Weighted(0) + offset,
                points is null ? null : Weighted(2), groundNodes,
                points is null || groundNodes.Length == 0 ? 0 : groundNodes.Average(n => Math.Abs((double)points[n * 6 + 2])));
            result.Samples.Add(sample);
            writer.WriteLine(JsonSerializer.Serialize(new
            {
                sample, SegmentFrame = i, intent, Native = physics, Points = points, NativeFrameHash = sample.NativeHash,
                ContinuousCoreX = sample.CoreX, Origin = origin, HorizontalCoreOffset = offset,
                MassCenter = points is null ? null : new[] { Weighted(0) + offset, Weighted(1), Weighted(2), Weighted(3), mass },
                GroundNodes = groundNodes, GeometricallyNearGroundNodes = nearGroundNodes,
                ContactTangentialVelocities = points is null ? [] : groundNodes.Select(n => points[n * 6 + 2]).ToArray(),
                NativeBounds = new { Left = body.CoreX + xs.Min(), Right = body.CoreX + xs.Max(), Bottom = body.CoreY + ys.Min(), Top = body.CoreY + ys.Max() },
                DisplayContourTemplate = mapped, OriginalContourTemplate = contour,
                HostBounds = frame.AuthoritativeBounds, RenderOffsets = frame.RenderOffsets, Gait = frame.Gait, Attachments = attachment,
                EyesAfterGait = attachment is null ? null : new[] { attachment.Face.Left, attachment.Face.Right }
                    .Select(eye => EyeOutline.Create(eye).Select(GaitGeometry.Canvas).Select(p => frame.Gait?.Map(p) ?? p).ToArray()).ToArray(),
                Hat = attachment?.Hat, CameraTranslation = new LogicalPoint(0, 0),
                runtime.LastError, runtime.AttachmentError, runtime.GaitError, runtime.LayoutWarning
            }, Json));
            if (runtime.LastError is not null || !physics.Succeeded) result.Failures.Add($"Frame {index}: native {physics.ErrorCode}: {runtime.LastError}");
            if (runtime.AttachmentError is not null || runtime.GaitError is not null) result.Failures.Add($"Frame {index}: attachment/gait fault");
            if (runtime.Wander.Active) result.Failures.Add($"Frame {index}: autonomous input contamination");
            if (body.Contour.Any(v => !float.IsFinite(v)) || !float.IsFinite(body.SignedArea) || height <= 0 ||
                Math.Abs(body.SignedArea) < parameters.MinimumArea || body.MaximumSpeed > parameters.MaximumSpeed + 1e-4)
                result.Failures.Add($"Frame {index}: nonfinite/area/speed/geometry health gate");
            if (points is not null && (points.Any(v => !float.IsFinite(v)) || mass <= 0))
                result.Failures.Add($"Frame {index}: diagnostic nonfinite/mass health gate");
            if (contour.Length == 0) result.Failures.Add($"Frame {index}: missing presented contour");
            if (runtime.LastError is not null || !physics.Succeeded) return result;
        }
        return result;
    }

    private static Metrics Measure(Run run, Segment segment)
    {
        var samples = run.Samples.Skip(segment.Start + 120).Take(480).ToArray();
        var neutralHeight = run.Samples.Skip(240).Take(60).Average(s => s.Height);
        var end = run.Samples[segment.Start + 599];
        var start = run.Samples[segment.Start - 1];
        var release = run.Samples.Skip(segment.Start + 600).Take(180).ToArray();
        return new(
            segment.Direction * (end.CoreX - run.Samples[segment.Start + 119].CoreX) * 72 / (480 * Step.TotalSeconds),
            segment.Direction * (end.CoreX - start.CoreX) * 72,
            release.Max(s => Math.Abs(s.CoreX - end.CoreX)) * 72,
            BandRms(samples.Select(s => s.Height).ToArray()), samples.Average(s => s.Height), neutralHeight,
            Math.Max(0, (neutralHeight - samples.Average(s => s.Height)) / neutralHeight),
            BandRms(samples.Select(s => s.DisplayHeightDip).ToArray()), samples.Min(s => s.ScaleY), samples.Max(s => s.ScaleY),
            samples.Count(s => !s.Grounded), samples.Count(s => s.Contacts == 0), LongestZeroContactRun(samples),
            samples.Average(s => (s.MassVelocityX ?? 0) * segment.Direction * 72),
            samples.Average(s => s.ContactSlipSpeed) * 72,
            samples.Zip(samples.Skip(1)).Count(pair => !pair.First.GroundNodes.SequenceEqual(pair.Second.GroundNodes)));
    }

    private static object Reversal(Run run, Sequence sequence)
    {
        var reverse = sequence.Segments[2];
        if (run.Samples.Count < reverse.Start + reverse.Count + 180) return new { Incomplete = true };
        var before = run.Samples[reverse.Start - 1];
        var after = run.Samples.Skip(reverse.Start).Take(reverse.Count).ToArray();
        var previous = before;
        var response = -1;
        foreach (var sample in after)
        {
            if (response < 0 && reverse.Direction * (sample.CoreX - previous.CoreX) > 0) response = sample.Frame - reverse.Start;
            previous = sample;
        }
        var end = after[^1];
        return new
        {
            Incomplete = false, reverse.Direction, FirstReverseDisplacementDip = (after[0].CoreX - before.CoreX) * 72,
            ResponseFrame = response, ReverseDisplacementDip = reverse.Direction * (end.CoreX - before.CoreX) * 72,
            StopDistanceDip = run.Samples.Skip(reverse.Start + reverse.Count).Take(180).Max(s => Math.Abs(s.CoreX - end.CoreX)) * 72
        };
    }

    internal static double BandRms(double[] values)
    {
        if (values.Length != 480) throw new ArgumentException("D0 requires exactly 480 samples.");
        var mean = values.Average();
        var energy = 0d;
        for (var k = 4; k <= 40; k++)
        {
            double real = 0, imaginary = 0;
            for (var n = 0; n < 480; n++)
            {
                var angle = 2 * Math.PI * k * n / 480;
                real += (values[n] - mean) * Math.Cos(angle);
                imaginary -= (values[n] - mean) * Math.Sin(angle);
            }
            energy += real * real + imaginary * imaginary;
        }
        return Math.Sqrt(2 * energy / (480d * 480));
    }

    private static int LongestZeroContactRun(Sample[] samples)
    {
        int longest = 0, current = 0;
        foreach (var sample in samples) { current = sample.Contacts == 0 ? current + 1 : 0; longest = Math.Max(longest, current); }
        return longest;
    }

    private static void AddSupportGates(string prefix, SupportPoint[] baseline, SupportPoint[] candidate, List<Gate> gates)
    {
        if (baseline.Length == 0 || baseline.Length != candidate.Length)
            throw new InvalidDataException("Support comparison requires equal nonempty paired windows.");
        var addedLoss = Enumerable.Range(0, baseline.Length).Where(i => baseline[i].Grounded && !candidate[i].Grounded).ToArray();
        gates.Add(new(prefix + "/ground-support", addedLoss.Length == 0,
            $"new unsupported relative-window frames={string.Join(",", addedLoss)}; count={addedLoss.Length}"));
        var baselineZero = baseline.Count(s => s.Contacts == 0);
        var candidateZero = candidate.Count(s => s.Contacts == 0);
        gates.Add(new(prefix + "/zero-contact-frames", candidateZero <= baselineZero,
            $"C0={baselineZero}, C1={candidateZero}"));
        int Longest(SupportPoint[] points)
        {
            int longest = 0, current = 0;
            foreach (var point in points) { current = point.Contacts == 0 ? current + 1 : 0; longest = Math.Max(longest, current); }
            return longest;
        }
        var baselineLongest = Longest(baseline); var candidateLongest = Longest(candidate);
        gates.Add(new(prefix + "/longest-zero-contact-run", candidateLongest <= baselineLongest,
            $"C0={baselineLongest}, C1={candidateLongest}"));
    }

    private static SupportPoint[] ReadSupportTrace(string path, int expectedFrames)
    {
        using var file = File.OpenRead(path);
        using var compressed = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(compressed, Encoding.UTF8);
        var points = new List<SupportPoint>();
        while (reader.ReadLine() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var sample = document.RootElement.GetProperty("sample");
            if (sample.GetProperty("Frame").GetInt32() != points.Count)
                throw new InvalidDataException($"{Path.GetFileName(path)}: unordered or missing frame {points.Count}.");
            var contacts = sample.GetProperty("Contacts").GetInt32();
            if (contacts < 0) throw new InvalidDataException($"{Path.GetFileName(path)}: negative contact count at frame {points.Count}.");
            points.Add(new(sample.GetProperty("Grounded").GetBoolean(), contacts));
        }
        if (points.Count != expectedFrames)
            throw new InvalidDataException($"{Path.GetFileName(path)}: expected {expectedFrames} frames, found {points.Count}.");
        return points.ToArray();
    }

    private static void RelativeGate(string name, double baseline, double candidate, List<Gate> gates)
    {
        var nearZero = Math.Abs(baseline) <= 1e-9;
        gates.Add(new(name, !nearZero && baseline > 0 && candidate >= .9 * baseline && candidate <= 1.1 * baseline,
            $"C0={baseline:R}, C1={candidate:R}, delta={candidate - baseline:R}" +
            (nearZero ? "; near-zero baseline: ratio undefined, absolute values retained, no automatic pass" : "; ratio=" + (candidate / baseline).ToString("R"))));
    }

    private static void EqualNative(string name, Run left, Run right, List<Gate> gates)
    {
        var mismatch = Enumerable.Range(0, Math.Min(left.Samples.Count, right.Samples.Count))
            .FirstOrDefault(i => left.Samples[i].NativeHash != right.Samples[i].NativeHash ||
                (left.Samples[i].DiagnosticHash is { } a && right.Samples[i].DiagnosticHash is { } b && a != b), -1);
        gates.Add(new(name, left.Samples.Count == right.Samples.Count && mismatch < 0,
            $"frames={left.Samples.Count}/{right.Samples.Count}; first bit mismatch={mismatch}"));
    }

    private static string SnapshotHash(DangoPhysicsFrame frame)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        writer.Write(frame.ErrorCode); writer.Write(frame.ContactStepCount); writer.Write(frame.Bodies.Count);
        foreach (var b in frame.Bodies)
        {
            writer.Write(b.EntityId.Value); writer.Write(b.CoreX); writer.Write(b.CoreY); writer.Write(b.SignedArea);
            writer.Write(b.CentroidX); writer.Write(b.CentroidY); writer.Write(b.MaximumSpeed);
            writer.Write(b.GroundContactCount); writer.Write(b.IsGrounded); writer.Write(b.SelectedDragNode); writer.Write(b.PointCount);
            writer.Write(b.Contour.Count); foreach (var v in b.Contour) writer.Write(v);
        }
        writer.Flush(); return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static string FloatHash(float[] values)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        foreach (var value in values) writer.Write(value);
        writer.Flush(); return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    private static IEnumerable<(string Name, DangoBodyControls Body)> Configurations()
    {
        yield return ("default", DangoBodyControls.Default);
        foreach (var size in new[] { 80, 120 }) foreach (var softness in new[] { 0, 100 }) foreach (var build in new[] { 0, 50, 100 })
            yield return ($"{size}-{softness}-{build}", new(size, softness, build));
    }

    private static IEnumerable<Sequence> Sequences()
    {
        foreach (var direction in new[] { 1, -1 })
        {
            yield return Make("long-" + direction, (0, 300), (direction, 600), (0, 180), (-direction, 600), (0, 180));
            yield return Make("short-reverse-" + direction, (0, 300), (direction, 30), (-direction, 30), (0, 180));
        }
    }
    private static Sequence Make(string name, params (int Direction, int Count)[] parts)
    {
        var start = 0; var segments = new List<Segment>();
        foreach (var p in parts) { segments.Add(new(start, p.Direction, p.Count)); start += p.Count; }
        return new(name, segments);
    }
    private static object Module(string name, string path) => new { Name = name, File = Path.GetFileName(path), Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    private static object Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(directory.FullName, "src"))) return directory.FullName;
        throw new InvalidOperationException("Cannot locate this worktree; refusing to write review evidence elsewhere.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ReadDiagnostic(nint world, int entity, [Out] float[] samples, int capacity);
    private sealed class DiagnosticReader : IDisposable
    {
        private readonly nint library;
        private readonly nint world;
        private readonly ReadDiagnostic read;
        internal DiagnosticReader(string path, StackedDangoSceneRuntime runtime)
        {
            library = NativeLibrary.Load(path);
            read = Marshal.GetDelegateForFunctionPointer<ReadDiagnostic>(NativeLibrary.GetExport(library, "dango_physics_world_get_drive_review"));
            world = (nint)Field(Field(Field(runtime, "scene"), "world"), "world");
        }
        internal float[] Read(int id)
        {
            var values = new float[102];
            var error = read(world, id, values, values.Length);
            if (error != 0) throw new InvalidOperationException($"D1 diagnostic export failed: {error}");
            return values;
        }
        public void Dispose() => NativeLibrary.Free(library);
    }

    private sealed record Segment(int Start, int Direction, int Count);
    private sealed record Sequence(string Name, List<Segment> Segments);
    private sealed record Sample(int Frame, int Direction, double CoreX, double CoreY, double Height, double Width,
        double ScaleY, double DisplayHeightDip, bool Grounded, int Contacts, string NativeHash, string? DiagnosticHash,
        double? MassCenterX, double? MassVelocityX, int[] GroundNodes, double ContactSlipSpeed);
    private sealed class Run { internal List<Sample> Samples { get; } = []; internal List<string> Failures { get; } = []; }
    private sealed record Gate(string Name, bool Passed, string Detail);
    private sealed record SupportPoint(bool Grounded, int Contacts);
    private sealed record Metrics(double SpeedDipPerSecond, double DisplacementDip, double StopDistanceDip,
        double HeightBandRms, double MeanHeight, double NeutralHeight, double CompressionFraction,
        double DisplayHeightBandRms, double MinimumScaleY, double MaximumScaleY, int UnsupportedFrames,
        int ZeroContactFrames, int LongestZeroContactRun, double MassSpeedDipPerSecond, double MeanContactSlipSpeedDipPerSecond,
        int GroundNodeSetChanges);
}
