using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// A comparison report, not an acceptance command. Nonfatal threshold failures
// remain visible while both independent worlds run the same requested duration.
internal static class IdleDiagnostics
{
    private const int RequestedFrames = 36000;
    private static readonly int[] CheckpointFrames = [600, 1800, 3600, 7200, 18000, 36000];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static int MeasureProbe(string input, string output)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("原生帧绘制测量输出已存在；请提供新路径以保留既有报告。");
        using var document = JsonDocument.Parse(File.ReadAllText(input));
        var cases = new List<object>();
        foreach (var source in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = source.GetProperty("name").GetString() ?? throw new InvalidDataException("诊断 case 缺少名称。");
            if (name is not "original-default" and not "candidate-default")
                throw new InvalidDataException($"未声明显示比例的诊断 case：{name}");
            var productionMapping = name == "original-default";
            var measurements = new List<object>();
            var renderingFailures = 0; var referenceFailures = 0;
            foreach (var checkpoint in source.GetProperty("checkpoints").EnumerateArray())
            {
                var frame = checkpoint.GetProperty("frame").GetInt32();
                var row = checkpoint.GetProperty("nativeRow").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                if (row.Length != 42 || row.Any(value => !float.IsFinite(value)))
                    throw new InvalidDataException("原生检查点必须包含42个有限float。");
                var state = new DangoBodyState(new(1), row[..32], row[32], row[33], row[34], row[35], row[36], row[37],
                    (int)row[38], row[39] != 0, (int)row[40], (int)row[41]);
                if (state.PointCount != 16) throw new InvalidDataException("原生检查点节点数必须为16。");
                try
                {
                    var bounds = SampleGeometry.Create(state, productionMapping, productionMapping ? 105d / 72 : 105).Bounds;
                    var silhouette = SilhouetteMetrics.Measure(state, productionMapping);
                    if (!silhouette.MatchesReference) referenceFailures++;
                    measurements.Add(new
                    {
                        frame, managedSeconds = frame * MaterialSession.FixedStep.TotalSeconds,
                        nominalSeconds = frame / 60d, nativeFrame = state,
                        renderingSucceeded = true, matchesReference = silhouette.MatchesReference,
                        coreRelativeDisplayPixelsBounds = new Bounds(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, bounds.Width, bounds.Height),
                        silhouette,
                    });
                }
                catch (Exception error)
                {
                    renderingFailures++; referenceFailures++;
                    measurements.Add(new { frame, nativeFrame = state, renderingSucceeded = false, matchesReference = false, error = error.Message });
                }
            }
            var spanPass = source.GetProperty("spanPass").GetBoolean();
            cases.Add(new
            {
                name, variant = source.GetProperty("variant").GetInt32(), productionMapping,
                sourceCompletedFrames = source.GetProperty("frames").GetInt32(),
                sourceError = source.GetProperty("error").GetInt32(), sourceLatchedError = source.GetProperty("latchedError").GetInt32(),
                sourceDrift = source.GetProperty("drift").GetDouble(), sourceSpanPass = spanPass,
                measuredCheckpointCount = measurements.Count, renderingFailures, referenceFailures,
                rejectedAsShapeRepair = renderingFailures > 0 || referenceFailures > 0,
                rejectedByMeasuredThresholds = !spanPass || renderingFailures > 0 || referenceFailures > 0,
                spanPassCannotOverrideShapeFailures = spanPass && (renderingFailures > 0 || referenceFailures > 0),
                measurements,
            });
        }
        var report = new
        {
            kind = "native-idle-checkpoint-render-measurement",
            diagnosticOnly = true,
            limitation = "Only the actual checkpoint native rows are measured. Six checkpoint matches do not establish all-frame drawing, long-run acceptance, recovery, input behavior or an approved repair.",
            sourceFileName = Path.GetFileName(input), sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
            bodyScale = 1, displayScaleX = 105, candidateDisplayScaleY = 105, originalDisplayScaleY = 105d * 58 / 72,
            referenceThresholds = new { targetAspect = SilhouetteMetrics.ReferenceAspect, aspectTolerance = .03,
                widestHeightMinimum = .60, widestHeightMaximum = .70, meanReferenceErrorMaximum = .04,
                maxReferenceErrorMaximum = .08, bottomCoverageMinimum = .43, bottomCoverageMaximum = .57 },
            cases,
        };
        var parent = Path.GetDirectoryName(output); if (parent is not null) Directory.CreateDirectory(parent);
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, report, JsonOptions);
        return 0; // Output generation succeeded; this is not a material gate result.
    }

    internal static int Run(string library, string output, string? baselineLibrary = null)
    {
        var librarySources = MaterialLibraries.Describe(library, baselineLibrary);
        output = Path.GetFullPath(output);
        if (File.Exists(output)) throw new IOException("静置诊断输出已存在；请提供新路径以保留既有报告。");
        var parent = Path.GetDirectoryName(output);
        if (parent is not null) Directory.CreateDirectory(parent);
        var configurations = new[]
        {
            (Name: "original-default", Configuration: DangoPhysicsDefaults.Current, ProductionMapping: true,
                Library: baselineLibrary ?? library, LibraryRole: "baseline", LibrarySource: librarySources.Baseline),
            (Name: "material-default", Configuration: new MaterialControls().Configure(), ProductionMapping: false,
                Library: library, LibraryRole: "candidate", LibrarySource: librarySources.Candidate),
        };
        var trials = new List<Trial>();
        foreach (var (name, configuration, productionMapping, selectedLibrary, libraryRole, librarySource) in configurations)
        {
            var trial = Diagnose(selectedLibrary, name, configuration, productionMapping, libraryRole, librarySource);
            trials.Add(trial);
            Console.WriteLine($"{name}: frames={trial.CompletedFrames}/{RequestedFrames}, " +
                $"drift={trial.Spans.MaximumExistingGateDrift:F9}, " +
                $"issues={trial.Failures.Count}, stopped={trial.StoppedReason ?? "no"}");
        }
        var step = MaterialSession.FixedStep;
        var report = new
        {
            kind = "independent-idle-diagnostic",
            acceptance = "Diagnostic only; this command does not replace --verify or user acceptance.",
            nativeSha256 = librarySources.Candidate.Sha256,
            nativeFileName = librarySources.Candidate.FileName, librarySources,
            nativeAbiVersion = DangoPhysicsWorld.NativeAbiVersion,
            requestedFrames = RequestedFrames,
            timing = new
            {
                fixedStepTicks = step.Ticks,
                managedFixedStepSeconds = step.TotalSeconds,
                nativeFixedStepFloatSeconds = (float)step.TotalSeconds,
                nativeFixedStepFloatAsDoubleSeconds = (double)(float)step.TotalSeconds,
                managedRequestedDurationSeconds = RequestedFrames * step.TotalSeconds,
                nativeRequestedDurationSeconds = RequestedFrames * (double)(float)step.TotalSeconds,
                nominalRequestedDurationSeconds = RequestedFrames / 60d,
            },
            sampling = new
            {
                existingGate = "Zero-based frame i >= 600 and (i % 60 == 0 or i >= 35400).",
                checkpointFrames = CheckpointFrames,
                checkpointNominalSeconds = CheckpointFrames.Select(frame => frame / 60d).ToArray(),
                input = "All control inputs remain zero/false; no dragging, jumping or downward control.",
                earlyStop = "Native/managed health failure stops its world. Rendering, penetration, settling, reference and span failures are recorded without ending simulation.",
            },
            trials,
        };
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, report, JsonOptions);
        // Exit status reports diagnostic execution, never acceptance of a material.
        return trials.All(trial => trial.CompletedFrames == RequestedFrames && trial.StoppedReason is null) ? 0 : 1;
    }

    private static Trial Diagnose(string library, string name, DangoSelfPhysicsParameters configuration, bool productionMapping,
        string libraryRole, MaterialLibrarySource librarySource)
    {
        var clock = Stopwatch.StartNew();
        var failures = new Dictionary<string, Failure>();
        var samples = new List<Sample>();
        var checkpoints = new List<Checkpoint>();
        var renderedFrames = 0;
        var penetrationFailures = 0;
        var renderingFailures = 0;
        var referenceFailures = 0;
        var settlingFailures = 0;
        var completedFrames = 0;
        var globalPeakSpeed = 0d;
        string? stoppedReason = null;
        DangoBodyState? lastState = null;
        var spanTracker = new SpanTracker();

        void Record(string code, int frame, string detail)
        {
            if (!failures.ContainsKey(code))
                failures.Add(code, new(code, frame, frame * MaterialSession.FixedStep.TotalSeconds, detail));
        }

        try
        {
            using var session = new MaterialSession(library, configuration);
            var bodySize = session.BodySize;
            var displayScaleX = 105d;
            var displayScaleY = productionMapping ? displayScaleX * 58 / 72 : displayScaleX;
            for (var i = 0; i < RequestedFrames; i++)
            {
                DangoBodyState state;
                try { state = session.Step(); }
                catch (Exception error)
                {
                    completedFrames = session.Frames;
                    lastState = session.State;
                    stoppedReason = error.Message;
                    var code = error is DangoPhysicsException ? "native-call-failure" : "native-or-managed-health-failure";
                    Record(code, completedFrames, error.ToString());
                    break;
                }
                completedFrames = session.Frames;
                lastState = state;
                globalPeakSpeed = Math.Max(globalPeakSpeed, state.MaximumSpeed);
                var shape = ShapeMetrics.Measure(state);
                if (shape.LowestWorldY < configuration.GroundHeight - .002)
                {
                    penetrationFailures++;
                    Record("penetration", completedFrames, $"lowest={shape.LowestWorldY:R}, ground={configuration.GroundHeight:R}, tolerance=0.002");
                }
                Bounds? displayBounds = null;
                try
                {
                    var bounds = SampleGeometry.Create(state, productionMapping,
                        productionMapping ? displayScaleX / 72 : displayScaleX, bodySize).Bounds;
                    displayBounds = new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, bounds.Width, bounds.Height);
                    renderedFrames++;
                }
                catch (Exception error)
                {
                    renderingFailures++;
                    Record("rendering", completedFrames, error.Message);
                }

                var checkpoint = CheckpointFrames.Contains(completedFrames);
                var sampledByExistingGate = i >= 600 && (i % 60 == 0 || i >= 35400);
                if (!checkpoint && !sampledByExistingGate) continue;
                SilhouetteMetrics? silhouette = null;
                try { silhouette = SilhouetteMetrics.Measure(state, productionMapping); }
                catch (Exception error) { Record("silhouette-measurement", completedFrames, error.Message); }
                var sample = new Sample(completedFrames,
                    completedFrames * MaterialSession.FixedStep.TotalSeconds,
                    completedFrames * (double)(float)MaterialSession.FixedStep.TotalSeconds,
                    state.CoreX, state.CoreY, shape.Width, shape.Height, state.SignedArea,
                    state.MaximumSpeed, state.GroundContactCount, state.IsGrounded,
                    shape.LowestWorldY, displayBounds, silhouette);
                if (sampledByExistingGate)
                {
                    samples.Add(sample);
                    spanTracker.Add(sample);
                    if (!state.IsGrounded || state.MaximumSpeed >= .03)
                    {
                        settlingFailures++;
                        Record("settling", completedFrames, $"grounded={state.IsGrounded}, maximumSpeed={state.MaximumSpeed:R}, limit=0.03");
                    }
                    if (silhouette?.MatchesReference != true)
                    {
                        referenceFailures++;
                        Record("reference-silhouette", completedFrames, silhouette is null ? "Measurement unavailable." :
                            $"aspect={silhouette.Aspect:R}, meanError={silhouette.MeanReferenceError:R}, maxError={silhouette.MaxReferenceError:R}, widestHeight={silhouette.WidestHeight:R}, bottomCoverage={silhouette.BottomCoverage:R}");
                    }
                    if (spanTracker.MaximumExistingGateDrift >= .003)
                        Record("existing-span-threshold", completedFrames, $"span={spanTracker.MaximumExistingGateDrift:R}, limit=0.003");
                }
                if (checkpoint)
                    checkpoints.Add(new(completedFrames / 60d, sample, state));
            }
        }
        catch (Exception error)
        {
            stoppedReason = error.Message;
            Record("diagnostic-world-failure", completedFrames, error.ToString());
        }
        clock.Stop();
        return new(name, configuration, productionMapping, libraryRole, librarySource,
            productionMapping ? "X=105 px/world unit, Y=105*58/72; current production body ratio." : "X=Y=105 px/world unit; isotropic candidate body.",
            completedFrames, completedFrames * MaterialSession.FixedStep.TotalSeconds,
            completedFrames * (double)(float)MaterialSession.FixedStep.TotalSeconds,
            clock.Elapsed.TotalSeconds, renderedFrames, renderingFailures, penetrationFailures,
            referenceFailures, settlingFailures, globalPeakSpeed, stoppedReason,
            spanTracker.Summarize(), failures.Values.ToArray(), checkpoints, samples, lastState);
    }

    private sealed record Bounds(double Left, double Top, double Right, double Bottom, double Width, double Height);
    private sealed record Sample(int Frame, double ManagedSeconds, double NativeSeconds,
        double CoreX, double CoreY, double Width, double Height, double SignedArea,
        double MaximumSpeed, int GroundContactCount, bool IsGrounded, double LowestWorldY,
        Bounds? CoreRelativeDisplayPixelsBounds, SilhouetteMetrics? Silhouette);
    private sealed record Checkpoint(double NominalSeconds, Sample Metrics, DangoBodyState FullFrame);
    private sealed record Failure(string Code, int FirstFrame, double ManagedSeconds, string Detail);
    private sealed record Spans(double CoreX, double CoreY, double Width, double Height, double SignedArea,
        double DisplayWidthPixels, double DisplayHeightPixels, double MaximumExistingGateDrift);
    private sealed record Trial(string Name, DangoSelfPhysicsParameters Configuration, bool ProductionMapping,
        string LibraryRole, MaterialLibrarySource LibrarySource,
        string DisplayMapping, int CompletedFrames, double ManagedSeconds, double NativeSeconds, double WallSeconds,
        int RenderedFrames, int RenderingFailures, int PenetrationFailures, int ReferenceFailures, int SettlingFailures,
        double GlobalPeakSpeed, string? StoppedReason, Spans Spans, IReadOnlyList<Failure> Failures,
        IReadOnlyList<Checkpoint> Checkpoints, IReadOnlyList<Sample> ExistingGateSamples, DangoBodyState? LastFrame);

    private sealed class SpanTracker
    {
        private readonly double[] minimum = Enumerable.Repeat(double.PositiveInfinity, 7).ToArray();
        private readonly double[] maximum = Enumerable.Repeat(double.NegativeInfinity, 7).ToArray();
        internal double MaximumExistingGateDrift => Enumerable.Range(0, 4).Max(Span);
        private double Span(int index) => double.IsFinite(minimum[index]) && double.IsFinite(maximum[index]) ? maximum[index] - minimum[index] : 0;
        internal void Add(Sample sample)
        {
            var values = new[] { sample.CoreX, sample.CoreY, sample.Width, sample.Height, sample.SignedArea,
                sample.CoreRelativeDisplayPixelsBounds?.Width ?? double.NaN, sample.CoreRelativeDisplayPixelsBounds?.Height ?? double.NaN };
            for (var i = 0; i < values.Length; i++)
            {
                if (!double.IsFinite(values[i])) continue;
                minimum[i] = Math.Min(minimum[i], values[i]); maximum[i] = Math.Max(maximum[i], values[i]);
            }
        }
        internal Spans Summarize() => new(Span(0), Span(1), Span(2), Span(3), Span(4), Span(5), Span(6), MaximumExistingGateDrift);
    }
}
