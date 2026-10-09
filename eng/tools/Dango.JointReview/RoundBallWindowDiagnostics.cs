using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;

namespace Dango.JointReview;

/// <summary>Records only this fictional native scene and this review process.</summary>
internal sealed class RoundBallWindowDiagnostics : IDisposable
{
    internal const double MinimumObservationSeconds = 600;
    internal const double MinimumActiveSeconds = 560;
    internal const int RequiredCycles = 20;
    internal const double ContactBudgetQ = 1d / 30;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string directory;
    private readonly StreamWriter observations;
    private readonly Process process = Process.GetCurrentProcess();
    private readonly Stopwatch clock = new();
    private TimeSpan lastCpu;
    private double lastSampleSeconds, nextSampleSeconds = 1, nextStateSeconds;
    private int stateSequence;
    private bool disposed;

    internal RoundBallWindowDiagnostics(string directory, string moduleHash, bool automaticObservation)
    {
        AutomaticObservation = automaticObservation;
        this.directory = Path.GetFullPath(directory);
        if (Directory.Exists(this.directory) || File.Exists(this.directory))
            throw new IOException("圆球观察必须使用不存在的新目录。");
        Directory.CreateDirectory(this.directory);
        observations = new(new FileStream(Path.Combine(this.directory, automaticObservation ? "observation.jsonl" : "window-state.jsonl"),
            FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        var assembly = Assembly.GetExecutingAssembly().Location;
        WriteNew("run-metadata.json", new
        {
            SchemaVersion = 1,
            NativeModuleSha256 = moduleHash,
            ReviewAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))),
            ProcessId = process.Id,
            EvidenceMode = automaticObservation ? "TenMinuteObservation" : "ExternalWindowCheck",
            AutomaticObservation = automaticObservation,
            FixedStepSeconds = RoundBallSession.FixedStep.TotalSeconds,
            MinimumObservationSeconds,
            MinimumActiveSeconds,
            RequiredCycles,
            ContactBudgetQ,
            EvidenceScope = "Fictional round-ball scene and its own WPF window/process; no desktop content or raw input capture",
        });
    }

    internal double ElapsedSeconds => clock.Elapsed.TotalSeconds;
    internal bool AutomaticObservation { get; }
    internal long PeakWorkingSetBytes { get; private set; }

    internal void Start()
    {
        lastCpu = process.TotalProcessorTime;
        clock.Start();
    }

    internal void Record(object state)
    {
        var seconds = ElapsedSeconds;
        if (seconds >= nextStateSeconds)
        {
            // Replace only our own generated latest-state file in the newly owned directory.
            // The rename gives external HWND verification a complete snapshot on every read.
            var temporary = Path.Combine(directory, $"state-{stateSequence++:D8}.tmp");
            using (var output = new FileStream(temporary, FileMode.CreateNew))
                JsonSerializer.Serialize(output, state, JsonOptions);
            File.Move(temporary, Path.Combine(directory, "latest-state.json"), overwrite: true);
            nextStateSeconds = seconds + .25;
        }
        if (seconds < nextSampleSeconds) return;
        process.Refresh();
        var cpu = process.TotalProcessorTime;
        var elapsed = seconds - lastSampleSeconds;
        var cpuPercent = elapsed > 0 ? (cpu - lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100 : 0;
        PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, process.WorkingSet64);
        observations.WriteLine(JsonSerializer.Serialize(new
        {
            ElapsedSeconds = seconds,
            CpuPercent = cpuPercent,
            WorkingSetBytes = process.WorkingSet64,
            State = state,
        }));
        lastCpu = cpu;
        lastSampleSeconds = seconds;
        nextSampleSeconds = seconds + 1;
    }

    internal void Complete(object summary) => WriteNew(AutomaticObservation ? "observation-summary.json" : "window-check-cleanup.json", summary);

    private void WriteNew(string name, object value)
    {
        using var output = new FileStream(Path.Combine(directory, name), FileMode.CreateNew);
        JsonSerializer.Serialize(output, value, JsonOptions);
    }

    /// <summary>Dense public-contour oracle; never calls native contact or the WPF factory.</summary>
    internal static double PaintedGapQ(DangoBodyState body, double centerQX, double centerQY, double radiusQ)
    {
        if (body.PointCount != 16 || body.Contour.Count != 32 || body.Contour.Any(value => !float.IsFinite(value)))
            throw new InvalidDataException("独立显示轮廓测量需要完整的 16 节点公开帧。");
        var maximum = body.Contour.Max(value => Math.Abs(value));
        var scale = maximum > .98 ? .98 / maximum : 1;
        const int samplesPerNode = 64;
        (double X, double Y) Sample(int sample)
        {
            var node = (sample / samplesPerNode) % 16;
            var t = (sample % samplesPerNode) / (double)samplesPerNode;
            var before = .5 * (1 - t) * (1 - t);
            var middle = .5 + t - t * t;
            var after = .5 * t * t;
            double Coordinate(int axis) => before * body.Contour[((node + 15) % 16) * 2 + axis]
                + middle * body.Contour[node * 2 + axis]
                + after * body.Contour[((node + 1) % 16) * 2 + axis];
            return ((4d / 3) * (body.CoreX + Coordinate(0) * scale * .9),
                body.CoreY + Coordinate(1) * scale * (58d / 60));
        }
        var previous = Sample(0);
        var minimumSquared = double.PositiveInfinity;
        var inside = false;
        for (var sample = 1; sample <= 16 * samplesPerNode; sample++)
        {
            var current = Sample(sample);
            var dx = current.X - previous.X;
            var dy = current.Y - previous.Y;
            var lengthSquared = dx * dx + dy * dy;
            var fraction = lengthSquared > 0 ? Math.Clamp(((centerQX - previous.X) * dx +
                (centerQY - previous.Y) * dy) / lengthSquared, 0, 1) : 0;
            var offsetX = previous.X + fraction * dx - centerQX;
            var offsetY = previous.Y + fraction * dy - centerQY;
            minimumSquared = Math.Min(minimumSquared, offsetX * offsetX + offsetY * offsetY);
            if ((previous.Y > centerQY) != (current.Y > centerQY) &&
                centerQX < previous.X + (centerQY - previous.Y) * dx / dy) inside = !inside;
            previous = current;
        }
        return (inside ? -1 : 1) * Math.Sqrt(minimumSquared) - radiusQ;
    }

    public void Dispose()
    {
        if (disposed) return;
        observations.Dispose();
        process.Dispose();
        disposed = true;
    }
}
