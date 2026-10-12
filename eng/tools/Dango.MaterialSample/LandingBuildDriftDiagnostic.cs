using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.MaterialSample;

// Passive derivative of LandingBuildLongVerification. Only this fixed diagnostic
// may retain LongDrift; no production or matrix acceptance is conferred.
internal static class LandingBuildDriftDiagnostic
{
    private const string FrozenSha = "E793C157937160C98821A344812CD87CDD25387C434968473E25FEEB86785B5C";
    private const string OldSha = "055BE2B5E6CD3816EB4D59F616577B653C00512B4750299A013D2CEBF676E2C5";
    private const string NewSha = "4388F63610C01FA5413EDA20829EA9E6F33AC93EC36A6793DCE4DCB0E4EAF757";
    private const string DepsSha = "BD1B03183480914BC8589EA4B55ACA3305679EB1E08E780C79B2FF9BF9B132D5";
    private static readonly LandingBuildControls FixedControls = new(80, 0, 0);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record FrameRecord(int Schema, string Variant, int WorldFrame, string Phase,
        long ManagedDtTicks, long ManagedDtDoubleBits, int NativeDtFloatBits,
        bool Drag, long[] ManagedControlDoubleBits, int[] NativeControlFloatBits,
        LandingLongNativeBits Native, string CaptureStatus = "returned-public-state-before-additional-guards");
    private sealed record ToolStamp(string Sha256, string DependencySetSha256, IReadOnlyDictionary<string, string> Dependencies);

    internal static int Run(string library, string frozenLongPath, string outputDirectory,
        string controlDirectory, string variant)
    {
        Capture? capture = null;
        FileStream? report = null;
        try
        {
            Require(variant is "old" or "candidate", "Variant must be old or candidate.");
            var source = ImportFrozen(frozenLongPath);
            VerifySourceGuards();
            var tool = Tool();
            Require(tool.DependencySetSha256 == DepsSha, "Sealed runtime dependencies differ.");
            library = Path.GetFullPath(library);
            // Retain a read-only handle for the entire trial; replacement is denied on Windows.
            using var nativePin = new FileStream(library, FileMode.Open, FileAccess.Read, FileShare.Read);
            Require(Hash(nativePin) == (variant == "old" ? OldSha : NewSha), "Native DLL hash differs.");
            outputDirectory = Path.GetFullPath(outputDirectory);
            controlDirectory = Path.GetFullPath(controlDirectory);
            ValidatePaths(library, frozenLongPath, outputDirectory, controlDirectory);
            Require(Directory.Exists(controlDirectory), "Control directory must be prepared by the runner.");
            Directory.CreateDirectory(outputDirectory);
            Require(!Directory.EnumerateFileSystemEntries(outputDirectory).Any(), "Output directory must be empty.");
            report = new FileStream(Path.Combine(outputDirectory, "report.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            capture = new Capture(source, outputDirectory, controlDirectory, variant, tool);
            capture.Admit();
            var trial = RunCore(capture, library, FixedControls, LandingBuildTable.SizeDampingPreview, LandingLongProfile.Forward);
            var completed = trial.ExecutionCompleted && capture.PersistedFrames == 42000 &&
                capture.HealthyFrames == 42000 && capture.RenderedFrames == 42000 &&
                (variant != "candidate" || capture.PrefixMatched);
            ValidateOutcome(trial.Failures.Any(f => f.Failure.Kind == LandingFailureKind.LongDrift), trial.Passed);
            var code = completed ? 0 : 2;
            if (!completed) capture.Stop("Diagnostic did not complete all original counters.");
            capture.CloseFrames();
            WriteExact(report, new
            {
                schema = 1, kind = "landing-build-finite-parallel-drift-diagnostic", variant,
                diagnosticCompleted = completed, originalThresholdsPassed = trial.Passed,
                currentS3Passed = false, manualAcceptance = false, mayResumeMatrix = false,
                nativeSha256 = variant == "old" ? OldSha : NewSha, frozenSourceSha256 = FrozenSha,
                tool, worldCreations = capture.WorldCreated ? 1 : 0,
                worldCreationAttempts = capture.WorldCreationAttempts,
                attemptedFrames = trial.AttemptedWorldFrame, sessionFrames = trial.SessionReportedFrames,
                healthyFrames = capture.HealthyFrames, renderedFrames = capture.RenderedFrames,
                lastHealthyWorldFrame = capture.LastHealthyWorldFrame,
                lastRenderedWorldFrame = capture.LastRenderedWorldFrame,
                durableStreamThroughWorldFrame = capture.DurableThroughWorldFrame,
                persistedMeaning = "Complete JSONL write and OS Flush(false); Flush(true) at each600 frames and close. Health and render are independently validated after capture.",
                persistedFrames = capture.PersistedFrames, streamSha256 = capture.StreamHash,
                failedAttemptStatus = trial.ExecutionCompleted ? null : new
                {
                    attemptedWorldFrame = trial.AttemptedWorldFrame,
                    sessionReturnedThisAttempt = trial.SessionReportedFrames == trial.AttemptedWorldFrame && trial.AttemptedWorldFrame > 0,
                    persistedThisAttempt = capture.PersistedFrames == trial.AttemptedWorldFrame && trial.AttemptedWorldFrame > 0,
                    nativeHealthyThisAttempt = capture.LastHealthyWorldFrame == trial.AttemptedWorldFrame && trial.AttemptedWorldFrame > 0,
                    renderedThisAttempt = capture.LastRenderedWorldFrame == trial.AttemptedWorldFrame && trial.AttemptedWorldFrame > 0,
                    failureNativeIsRetainedStaleState = trial.SessionReportedFrames < trial.AttemptedWorldFrame,
                },
                candidateSavedPrefixMatched = variant == "candidate" ? (bool?)capture.PrefixMatched : null,
                prefixScope = "Only saved 6000 postdrag, idle600 checkpoint, 11 gate samples, first failure, and 7201 last native/bits; unsaved prior intermediate frames are unavailable.",
                stopBoundary = "Each Step admission checks stop immediately before Step; at most one admitted in-flight frame may finish. No next Step is admitted.",
                streamSchema = "JSONL schema1; one immutable FrameRecord per returned public native state, ordered worldFrame; contour[0..31],CoreX,CoreY,SignedArea,CentroidX,CentroidY,MaximumSpeed int32 bits; managed controls x/y/horizontal/downward double bits and six actual ABI float input bits; terminal report binds SHA256 and count.",
                trial, error = capture.Error,
                interpretation = "Finite 6000+36000 fixed80/0/0 Forward observation only. LongDrift stays failed; no infinite convergence, causation, general equivalence or matrix acceptance.",
            });
            return code;
        }
        catch (Exception error)
        {
            capture?.Stop(error.ToString());
            if (capture is null) PublishStop(controlDirectory, variant, error.ToString());
            Console.Error.WriteLine(error);
            if (report is not null && report.Position == 0)
            {
                try { WriteExact(report, new { schema = 1, kind = "landing-build-finite-parallel-drift-diagnostic", variant,
                    diagnosticCompleted = false, originalThresholdsPassed = false, currentS3Passed = false,
                    worldCreations = capture?.WorldCreated == true ? 1 : 0, persistedFrames = capture?.PersistedFrames ?? 0,
                    worldCreationAttempts = capture?.WorldCreationAttempts ?? 0,
                    healthyFrames = capture?.HealthyFrames ?? 0, renderedFrames = capture?.RenderedFrames ?? 0,
                    error = error.ToString() }); }
                catch (Exception saveError) { Console.Error.WriteLine(saveError); }
            }
            return 2;
        }
        finally { capture?.Dispose(); report?.Dispose(); }
    }

    private sealed class Capture : IDisposable
    {
        private readonly LandingLongTrial reference;
        private readonly string control, variant;
        private readonly ToolStamp tool;
        private readonly string streamPath;
        private readonly FileStream frames;
        private FrameRecord? input;
        private bool closed;
        private bool persistenceAttempted;
        private int prefixSamples, prefixCheckpoints;
        private bool postMatched;
        internal bool WorldCreated, PrefixMatched;
        internal int WorldCreationAttempts;
        internal int PersistedFrames, HealthyFrames, RenderedFrames;
        internal int LastHealthyWorldFrame;
        internal int LastRenderedWorldFrame, DurableThroughWorldFrame;
        internal string? Error, StreamHash;
        internal Capture(LandingLongTrial reference, string output, string control, string variant, ToolStamp tool)
        {
            this.reference = reference; this.control = control; this.variant = variant; this.tool = tool;
            streamPath = Path.Combine(output, "frames.jsonl");
            frames = new FileStream(streamPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        internal void Admit()
        {
            CheckStop();
            Require(!File.Exists(Path.Combine(control, "start.json")), "Start barrier already exists before admission.");
            using (var claim = NewControl("claim-" + variant + ".json"))
                JsonSerializer.Serialize(claim, new { schema = 1, variant, pid = Environment.ProcessId });
            using (var ready = NewControl("ready-" + variant + ".json"))
                JsonSerializer.Serialize(ready, new { schema = 1, variant, pid = Environment.ProcessId,
                    nativeSha256 = variant == "old" ? OldSha : NewSha, frozenSourceSha256 = FrozenSha,
                    tool, worldCreations = 0, worldCreationAttempts = 0, prefixValidated = true, prefixMeaning = "frozen saved source validated before fresh replay" });
            var timer = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(control, "start.json")))
            {
                CheckStop();
                Require(timer.Elapsed < TimeSpan.FromSeconds(60), "Start barrier timed out with zero worlds.");
                Thread.Sleep(20);
            }
            using var start = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(control, "start.json")));
            var s = start.RootElement;
            Require(s.GetProperty("schema").GetInt32() == 1 &&
                s.GetProperty("variants").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "old", "candidate" }) &&
                s.GetProperty("maxWorlds").GetInt32() == 2 && s.GetProperty("maxPublicFramesPerWorld").GetInt32() == 42000 &&
                s.GetProperty("maxTotalPublicFrames").GetInt32() == 84000, "Start barrier scope differs.");
            foreach (var v in new[] { "old", "candidate" })
            {
                using var peer = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(control, "ready-" + v + ".json")));
                var p = peer.RootElement;
                Require(p.GetProperty("variant").GetString() == v && p.GetProperty("worldCreations").GetInt32() == 0 &&
                    p.GetProperty("prefixValidated").GetBoolean() && p.GetProperty("frozenSourceSha256").GetString() == FrozenSha &&
                    p.GetProperty("nativeSha256").GetString() == (v == "old" ? OldSha : NewSha) &&
                    p.GetProperty("tool").GetProperty("Sha256").GetString() == tool.Sha256 &&
                    p.GetProperty("tool").GetProperty("DependencySetSha256").GetString() == DepsSha &&
                    (v == variant || p.GetProperty("pid").GetInt32() != Environment.ProcessId), "Peer admission differs or shares process.");
            }
            CheckStop();
        }
        private FileStream NewControl(string name) => new(Path.Combine(control, name), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        private void CheckStop()
        {
            var path = Path.Combine(control, "stop.json");
            if (!File.Exists(path)) return;
            // Malformed stop is still a stop, never permission to continue.
            throw new IOException("Shared stop present: " + File.ReadAllText(path));
        }
        internal void BeforeCreate()
        {
            CheckStop();
            Require(WorldCreationAttempts == 0, "Only one constructor attempt is allowed per variant process.");
            WorldCreationAttempts++;
        }
        internal void AdmitStep() => CheckStop();
        internal void BeforeStep(int next)
        {
            Require(next is >= 1 and <= 42000, "Public frame budget exceeded.");
            Require(variant != "candidate" || next <= 7201 || PrefixMatched, "Saved candidate prefix not matched before frame7202.");
            CheckStop();
        }
        internal void SetInput(int frame, string phase, bool drag, double x, double y)
        {
            persistenceAttempted = false;
            input = new(1, variant, frame, phase, MaterialSession.FixedStep.Ticks,
                BitConverter.DoubleToInt64Bits(MaterialSession.FixedStep.TotalSeconds),
                BitConverter.SingleToInt32Bits((float)MaterialSession.FixedStep.TotalSeconds), drag,
                new[] { x, y, 0d, 0d }.Select(BitConverter.DoubleToInt64Bits).ToArray(),
                new[] { 0f, 0f, 0f, drag ? 1f : 0f, (float)x, (float)y }.Select(BitConverter.SingleToInt32Bits).ToArray(),
                new(0, [], 0, false, 0, 0));
        }
        internal void PersistFrame(DangoBodyState state)
        {
            Require(input is not null && input.WorldFrame == PersistedFrames + 1, "Public records are not contiguous.");
            persistenceAttempted = true;
            var record = input! with { Native = Bits(state) };
            // A failed public state is still preserved. Native-health guards run
            // afterwards in their original order; a broken capture stays failed.
            ValidateRecord(record, PersistedFrames + 1, variant, validateNative: false);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record);
            frames.Write(bytes); frames.WriteByte(10); frames.Flush(flushToDisk: false);
            PersistedFrames++;
            if (PersistedFrames % 600 == 0)
            {
                frames.Flush(flushToDisk: true); DurableThroughWorldFrame = PersistedFrames;
            }
        }
        internal void PersistReturnedOnThrow(MaterialSession? session)
        {
            if (!persistenceAttempted && session?.State is not null && input is not null && session.Frames == input.WorldFrame && PersistedFrames < session.Frames)
            {
                try { PersistFrame(session.State); }
                catch (Exception error) { Stop("Returned-frame persistence failed: " + error); }
            }
        }
        internal void CheckPost(DangoBodyState state)
        {
            if (variant != "candidate") return;
            Require(FrameEqual(state, reference.PostTrajectoryStart!), "Candidate postdrag frame differs from saved source.");
            postMatched = true;
        }
        internal void CheckSample(LandingLongSample sample, bool checkpoint, bool gate, DangoBodyState state)
        {
            if (variant != "candidate" || sample.WorldFrame > 7201) return;
            if (gate)
            {
                Require(prefixSamples < reference.ExistingGateSamples.Count && ExactEqual(sample, reference.ExistingGateSamples[prefixSamples]), "Candidate saved gate sample differs.");
                prefixSamples++;
            }
            if (checkpoint)
            {
                var saved = reference.Checkpoints[prefixCheckpoints];
                Require(sample.IdleFrame == saved.IdleFrame && ExactEqual(sample, saved.Metrics) && FrameEqual(state, saved.NativeFrame), "Candidate saved checkpoint differs.");
                prefixCheckpoints++;
            }
        }
        internal void OnFailure(LandingLongFailureEvidence evidence)
        {
            if (evidence.Failure.Kind != LandingFailureKind.LongDrift) { Stop(evidence.Failure.ToString()); return; }
            if (variant != "candidate") return;
            try
            {
                Require(ExactEqual(evidence, reference.FirstFailure!) && postMatched && prefixSamples == 11 && prefixCheckpoints == 1 &&
                    FrameEqual(evidence.NativeFrame!, reference.LastHealthyNativeFrame!) && ExactEqual(evidence.NativeBits!, reference.FirstFailure!.NativeBits!), "Candidate saved first drift/last native/bits differs.");
                PrefixMatched = true;
            }
            catch (Exception error) { Stop(error.ToString()); throw; }
        }
        internal void Stop(string reason) { Error ??= reason; PublishStop(control, variant, reason); }
        internal void CloseFrames()
        {
            if (closed) return;
            frames.Flush(flushToDisk: true); frames.Dispose(); closed = true;
            DurableThroughWorldFrame = PersistedFrames;
            using var stream = File.OpenRead(streamPath); StreamHash = Hash(stream);
        }
        public void Dispose() { if (!closed) { try { CloseFrames(); } catch (Exception error) { Stop(error.ToString()); } } }
    }

    private static LandingLongTrial ImportFrozen(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Require(Convert.ToHexString(SHA256.HashData(bytes)) == FrozenSha, "Frozen S3 source differs.");
        using var document = JsonDocument.Parse(bytes);
        var r = document.RootElement;
        Require(r.GetProperty("kind").GetString() == "landing-build-touching-receipt-long" &&
            r.GetProperty("outcome").GetString() == "hard-failure-stop" && r.GetProperty("trials").GetArrayLength() == 5 &&
            r.GetProperty("native").GetProperty("Sha256").GetString() == NewSha &&
            r.GetProperty("tool").GetProperty("DependencySetSha256").GetString() == DepsSha, "Frozen source identity differs.");
        var t = r.GetProperty("trials")[4].Deserialize<LandingLongTrial>(Json)!;
        Require(t.Controls == FixedControls && t.Profile == LandingLongProfile.Forward &&
            t.Configuration == FixedControls.Configure(LandingBuildTable.SizeDampingPreview) && t.RequestedDragFrames == 6000 &&
            t.RequestedIdleFrames == 36000 && t.CompletedDragFrames == 6000 && t.CompletedIdleFrames == 1201 &&
            t.AttemptedWorldFrame == 7201 && t.SessionReportedFrames == 7201 && t.RenderedFrames == 7201 &&
            t.ExistingGateSamples.Count == 11 && t.Checkpoints.Count == 1 && t.Checkpoints[0].IdleFrame == 600 &&
            !t.Passed && !t.ExecutionCompleted && t.Failure?.Kind == LandingFailureKind.LongDrift && t.Failures.Count == 1 &&
            t.FirstFailure?.FirstWorldFrame == 7201 && t.FirstFailure.FirstIdleFrame == 1201 &&
            t.FirstFailure.Phase == "post-drag-idle" && t.PostTrajectoryStart is not null && t.LastHealthyNativeFrame is not null &&
            t.FirstFailure.NativeBits is not null && t.FirstFailure.NativeFrame is not null &&
            ExactEqual(Bits(t.LastHealthyNativeFrame), t.FirstFailure.NativeBits), "Frozen saved prefix content differs.");
        return t;
    }
    private static void VerifySourceGuards()
    {
        // Source-only guard; caller additionally independently audits the actual built IL.
        var root = FindRoot();
        var sources = new Dictionary<string, string>
        {
            ["LandingBuildLongVerification.cs"] = "470A8169B6D26FB59C89788AFDA7DE5E6882073C9C6151E468FAC0710AFF4977",
            ["LandingBuildFrameValidator.cs"] = "86FECD5F84C5A8E041918C4BDDCA3D0ED72BC33D4DBD8DF8BA4DE3C3A04BE7A2",
            ["MaterialSession.cs"] = "58DD4C1DEE280EB4F64306F2E0BA74E3AFD8CE062D95D7C082381C554685B269",
        };
        foreach (var pair in sources)
            Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "eng", "tools", "Dango.MaterialSample", pair.Key)))) == pair.Value, "Original guard source changed: " + pair.Key);
    }
    private static string FindRoot()
    {
        var d = new DirectoryInfo(Environment.CurrentDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "AGENTS.md"))) d = d.Parent;
        Require(d is not null, "Run from reviewed worktree, not a detached application directory.");
        return d!.FullName;
    }
    private static void ValidatePaths(string library, string frozen, string output, string control)
    {
        static bool Contains(string parent, string child) => Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        Require(!string.Equals(output, control, StringComparison.OrdinalIgnoreCase) && !Contains(output, control) && !Contains(control, output) &&
            !Contains(output, library) && !Contains(output, frozen) && !Contains(control, library) && !Contains(control, frozen), "Input/output/control paths overlap.");
        Require(!File.Exists(output) && !File.Exists(control), "Output/control path is a file.");
    }
    private static ToolStamp Tool()
    {
        var dependencies = new[] { "Dango.Core", "Dango.Desktop", "Dango.Physics", "Dango.Protocol" }
            .ToDictionary(n => n + ".dll", n => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.Load(n).Location))));
        return new(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LandingBuildDriftDiagnostic).Assembly.Location))),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", dependencies.Select(p => p.Key + "=" + p.Value))))), dependencies);
    }
    private static void PublishStop(string control, string variant, string reason)
    {
        try
        {
            if (!Directory.Exists(control)) return;
            using var stream = new FileStream(Path.Combine(control, "stop.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            JsonSerializer.Serialize(stream, new { schema = 1, variant, pid = Environment.ProcessId, reason }); stream.Flush(true);
        }
        catch (IOException) { /* Preserve peer's first stop; inability to publish still stops this process. */ }
        catch (Exception error) { Console.Error.WriteLine("Cannot publish shared stop: " + error); }
    }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidDataException(detail); }
    private static bool FrameEqual(DangoBodyState a, DangoBodyState b) => ExactEqual(Bits(a), Bits(b));
    private static bool ExactEqual(object a, object b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b) &&
        DoubleBits(a).OrderBy(p => p.Key).SequenceEqual(DoubleBits(b).OrderBy(p => p.Key));
    private static Dictionary<string, long> DoubleBits(object value)
    {
        var result = new Dictionary<string, long>();
        void Walk(object? v, string path)
        {
            if (v is null) return;
            if (v is double number) { result.Add(path, BitConverter.DoubleToInt64Bits(number)); return; }
            if (v is string || v.GetType().IsPrimitive || v.GetType().IsEnum) return;
            if (v is System.Collections.IEnumerable list) { var i = 0; foreach (var item in list) Walk(item, path + "[" + i++ + "]"); return; }
            foreach (var p in v.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetIndexParameters().Length == 0)) Walk(p.GetValue(v), path + "." + p.Name);
        }
        Walk(value, "$data"); return result;
    }
    private static void WriteExact(Stream stream, object data)
    {
        var bits = DoubleBits(data);
        var roundtrip = bits.ToDictionary(p => p.Key, p => BitConverter.DoubleToInt64Bits(JsonSerializer.Deserialize<double>(JsonSerializer.Serialize(BitConverter.Int64BitsToDouble(p.Value)))) == p.Value);
        JsonSerializer.Serialize(stream, new { data, doubleBitsAuthority = bits, decimalRoundtripExact = roundtrip }, Json);
        stream.Flush(); if (stream is FileStream file) file.Flush(true);
    }
    private static void ValidateRecord(FrameRecord r, int expected, string variant, bool validateNative = true)
    {
        Require(r.Schema == 1 && r.Variant == variant && variant is "old" or "candidate" && r.WorldFrame == expected && expected is >= 1 and <= 42000,
            "Stream identity, order or frame budget differs.");
        var dragPhase = expected <= 6000; var i = expected - 1;
        var drag = dragPhase && i >= 600 && i % 600 < 480;
        var x = dragPhase ? 2.4 * Math.Sin(i * .12) : 0; var y = dragPhase ? .4 + 1.5 * Math.Cos(i * .17) : 0;
        Require(r.Phase == (dragPhase ? "ground-swing" : "post-drag-idle") && r.Drag == drag &&
            r.ManagedDtTicks == MaterialSession.FixedStep.Ticks && r.ManagedDtDoubleBits == BitConverter.DoubleToInt64Bits(MaterialSession.FixedStep.TotalSeconds) &&
            r.NativeDtFloatBits == BitConverter.SingleToInt32Bits((float)MaterialSession.FixedStep.TotalSeconds) &&
            r.ManagedControlDoubleBits.SequenceEqual(new[] { x, y, 0d, 0d }.Select(BitConverter.DoubleToInt64Bits)) &&
            r.NativeControlFloatBits.SequenceEqual(new[] { 0f, 0f, 0f, drag ? 1f : 0f, (float)x, (float)y }.Select(BitConverter.SingleToInt32Bits)) &&
            (!validateNative || r.Native.FloatBits.Count == 38 && r.Native.PointCount == 16 && r.Native.EntityId > 0), "Stream input/dt/native shape differs.");
    }
    private static bool MayContinue(LandingBuildControls controls, LandingLongProfile profile, LandingFailureKind kind) =>
        controls == FixedControls && profile == LandingLongProfile.Forward && kind == LandingFailureKind.LongDrift;
    private static void ValidateOutcome(bool hasRecordedDrift, bool originalThresholdsPassed) =>
        Require(!hasRecordedDrift || !originalThresholdsPassed, "Recorded LongDrift cannot be reclassified as physical pass.");
    private static void ValidateSyntheticStream(byte[] bytes, string hash, int count)
    {
        Require(Convert.ToHexString(SHA256.HashData(bytes)) == hash, "Stream terminal hash differs.");
        Require(bytes.Length > 0 && bytes[^1] == 10, "Stream terminal record is truncated.");
        var lines = Encoding.UTF8.GetString(bytes).Split('\n');
        Require(lines.Length == count + 1 && lines[^1] == "", "Stream terminal count differs.");
        for (var i = 0; i < count; i++) ValidateRecord(JsonSerializer.Deserialize<FrameRecord>(lines[i])!, i + 1, "candidate");
    }

    internal static int VerifyContract(string frozenLongPath, string output)
    {
        try
        {
            _ = ImportFrozen(frozenLongPath); VerifySourceGuards();
            var checks = new List<string>();
            void Reject(string name, Action action)
            {
                try { action(); } catch { checks.Add(name); return; }
                throw new InvalidDataException("Negative fixture accepted: " + name);
            }
            FrameRecord Fixture(int frame) => new(1, "candidate", frame, "ground-swing", MaterialSession.FixedStep.Ticks,
                BitConverter.DoubleToInt64Bits(MaterialSession.FixedStep.TotalSeconds), BitConverter.SingleToInt32Bits((float)MaterialSession.FixedStep.TotalSeconds),
                false, new[] { 2.4 * Math.Sin((frame - 1) * .12), .4 + 1.5 * Math.Cos((frame - 1) * .17), 0d, 0d }.Select(BitConverter.DoubleToInt64Bits).ToArray(),
                new[] { 0f, 0f, 0f, 0f, (float)(2.4 * Math.Sin((frame - 1) * .12)), (float)(.4 + 1.5 * Math.Cos((frame - 1) * .17)) }.Select(BitConverter.SingleToInt32Bits).ToArray(), new(1, new int[38], 0, false, -1, 16));
            var a = Fixture(1); ValidateRecord(a, 1, "candidate");
            var fixtureBytes = Encoding.UTF8.GetBytes(string.Join("\n", new[] { a, Fixture(2), Fixture(3) }.Select(v => JsonSerializer.Serialize(v))) + "\n");
            var fixtureHash = Convert.ToHexString(SHA256.HashData(fixtureBytes));
            ValidateSyntheticStream(fixtureBytes, fixtureHash, 3);
            Reject("duplicate/skipped/reordered", () => ValidateRecord(Fixture(2), 1, "candidate"));
            Reject("third variant", () => ValidateRecord(a with { Variant = "third" }, 1, "third"));
            Reject("over42000", () => ValidateRecord(a with { WorldFrame = 42001 }, 42001, "candidate"));
            Reject("dt corruption", () => ValidateRecord(a with { NativeDtFloatBits = a.NativeDtFloatBits + 1 }, 1, "candidate"));
            Reject("input bit corruption", () => ValidateRecord(a with { NativeControlFloatBits = new int[6] }, 1, "candidate"));
            Reject("truncated float stream", () => ValidateRecord(a with { Native = a.Native with { FloatBits = new int[37] } }, 1, "candidate"));
            Reject("float corruption exact", () => Require(ExactEqual(a.Native, a.Native with { FloatBits = Enumerable.Repeat(1, 38).ToArray() }), "Float mismatch"));
            var exact = BitConverter.Int64BitsToDouble(0x3E60000000000000);
            var changed = BitConverter.Int64BitsToDouble(0x3E5FFFFFFFFFFFFF);
            Reject("double1ULP exact", () => Require(ExactEqual(new { CoreY = exact }, new { CoreY = changed }), "Double mismatch"));
            Require(MayContinue(FixedControls, LandingLongProfile.Forward, LandingFailureKind.LongDrift), "Fixed drift continuation rejected.");
            Reject("wrong controls", () => Require(MayContinue(new(100, 50, 50), LandingLongProfile.Forward, LandingFailureKind.LongDrift), "Continuation controls differ"));
            Reject("other guard continuation", () => Require(MayContinue(FixedControls, LandingLongProfile.Forward, LandingFailureKind.LongSettling), "Stopping guard cannot continue"));
            Reject("mirror continuation", () => Require(MayContinue(FixedControls, LandingLongProfile.Mirror, LandingFailureKind.LongDrift), "Mirror cannot continue"));
            ValidateOutcome(true, false);
            Reject("drift reclassified passed", () => ValidateOutcome(true, true));
            Reject("hash corruption", () => ValidateSyntheticStream(fixtureBytes, new string('0', 64), 3));
            Reject("truncated stream", () => ValidateSyntheticStream(fixtureBytes[..^1], Convert.ToHexString(SHA256.HashData(fixtureBytes[..^1])), 3));
            Reject("added/missing frames", () => ValidateSyntheticStream(fixtureBytes, fixtureHash, 2));
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            WriteExact(file, new { schema = 1, kind = "landing-build-drift-zero-world-contract", worldCreations = 0, worldSteps = 0,
                passed = true, checks, sourceSha256 = FrozenSha, physicalPassed = false,
                actualDecimalSerializerRegression = !ExactEqual(new { CoreY = exact }, new { CoreY = JsonSerializer.Deserialize<double>(JsonSerializer.Serialize(exact)) }),
                authorityFixture = new { CoreY = exact }, limitation = "Synthetic schema/policy fixtures only; actual build/provenance/parallel stop and independent full stream audits required before/after worlds." });
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 2; }
    }
    internal const int DragFrames = 6000;
    internal const int IdleFrames = 36000;
    internal const int RequiredGateSamples = 1180;
    internal const double DisplayScale = 105;
    private static readonly int[] CheckpointFrames = [600, 1800, 3600, 7200, 18000, 36000];

    private static LandingLongTrial RunCore(Capture capture, string library, LandingBuildControls controls,
        LandingBuildTable table, LandingLongProfile profile,
        Action<LandingLongSample?, DangoBodyState, double>? retainedDifferenceObserver = null)
    {
        if (profile is not (LandingLongProfile.Idle or LandingLongProfile.Forward or LandingLongProfile.Mirror))
            throw new ArgumentOutOfRangeException(nameof(profile));
        var configuration = controls.Configure(table);
        var mappingVersion = LandingBuildMapping.Version(table);
        var requestedDragFrames = profile == LandingLongProfile.Idle ? 0 : DragFrames;
        var name = profile switch
        {
            LandingLongProfile.Idle => "idle",
            LandingLongProfile.Forward => "ground-swing",
            _ => "ground-swing-x-mirror",
        };
        // This only permits a complete observation. It does not approve the result,
        // check the DLL hash, or establish equivalence to the old accepted report.
        var observeDefaultForwardDrift = profile == LandingLongProfile.Forward &&
            controls.SizePercent == 80 && controls.Softness == 0 && controls.LandingBuild == 0;
        var clock = Stopwatch.StartNew();
        MaterialSession? session = null;
        var checkpoints = new List<LandingLongCheckpoint>();
        var samples = new List<LandingLongSample>();
        var failures = new List<LandingLongFailureEvidence>();
        var spans = new SpanTracker();
        int completedDragFrames = 0, completedIdleFrames = 0, attemptedWorldFrame = 0;
        int renderedFrames = 0, sessionReportedFrames = 0;
        int? attemptedIdleFrame = null;
        var phase = "creation";
        double peakSpeed = 0;
        bool continuedDefaultForwardDriftObservation = false;
        bool stopped = false;
        DangoBodyState? lastHealthy = null;
        DangoBodyState? currentNative = null;
        DangoBodyState? postTrajectoryStart = null;
        LandingFailure? failure = null;

        void RecordFailure(LandingFailureKind kind, string detail)
        {
            var item = new LandingFailure(kind, detail, attemptedWorldFrame);
            var nativeFrame = currentNative is not null && HasFiniteNativeFloats(currentNative)
                ? currentNative : null;
            failures.Add(new(item, phase, attemptedWorldFrame, attemptedIdleFrame,
                attemptedWorldFrame * MaterialSession.FixedStep.TotalSeconds,
                attemptedWorldFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds,
                nativeFrame, currentNative is null ? null : Bits(currentNative)));
            // A stopping failure takes precedence over an earlier observed drift.
            // FirstFailure and Failures retain chronological evidence separately.
            failure = item;
            capture.OnFailure(failures[^1]);
        }

        LandingObservation Advance(bool drag = false, double x = 0, double y = 0)
        {
            capture.BeforeStep(session!.Frames + 1);
            attemptedWorldFrame = session!.Frames + 1;
            capture.SetInput(attemptedWorldFrame, phase, drag, x, y);
            currentNative = null;
            capture.AdmitStep();
            var state = session.Step(drag: drag, dragX: x, dragY: y);
            currentNative = state;
            sessionReportedFrames = session.Frames;
            if (phase == "ground-swing") completedDragFrames++;
            else completedIdleFrames++;
            capture.PersistFrame(state);
            if (!HasFiniteNativeFloats(state))
                throw new LandingGateFailure(LandingFailureKind.HealthOrStop, "Native frame contains a nonfinite float.");
            lastHealthy = state;
            capture.HealthyFrames++;
            capture.LastHealthyWorldFrame = attemptedWorldFrame;
            peakSpeed = Math.Max(peakSpeed, state.MaximumSpeed);
            var observation = LandingBuildFrameValidator.Observe(session, state);
            var shape = observation.Shape;
            if (!double.IsFinite(shape.Width) || !double.IsFinite(shape.Height) ||
                !double.IsFinite(shape.Aspect) || !double.IsFinite(shape.RadialSpread) ||
                !double.IsFinite(shape.Circularity) || !double.IsFinite(shape.LowestWorldY) ||
                shape.Width <= 0 || shape.Height <= 0)
                throw new LandingGateFailure(LandingFailureKind.HealthOrStop, "Native shape metrics are nonfinite or degenerate.");
            if (!double.IsFinite(observation.RenderedWidth) || !double.IsFinite(observation.RenderedHeight) ||
                observation.RenderedWidth <= 0 || observation.RenderedHeight <= 0 ||
                !double.IsFinite(observation.MaximumCanonicalRadius))
                throw new LandingGateFailure(LandingFailureKind.InvalidRender, "Actual rendered bounds or canonical radius are nonfinite or empty.");
            renderedFrames++;
            capture.RenderedFrames++;
            capture.LastRenderedWorldFrame = attemptedWorldFrame;
            return observation;
        }

        try
        {
            capture.BeforeCreate();
            session = new(library, configuration);
            capture.WorldCreated = true;
            phase = "ground-swing";
            for (var i = 0; i < requestedDragFrames; i++)
            {
                // Exactly the existing fixed trajectory, including inactive targets.
                var active = i >= 600 && i % 600 < 480;
                var x = (profile == LandingLongProfile.Mirror ? -1 : 1) * 2.4 * Math.Sin(i * .12);
                var y = .4 + 1.5 * Math.Cos(i * .17);
                _ = Advance(active, x, y);
            }
            if (requestedDragFrames > 0)
            {
                postTrajectoryStart = lastHealthy;
                capture.CheckPost(postTrajectoryStart!);
                retainedDifferenceObserver?.Invoke(null, postTrajectoryStart!, 0);
            }
            phase = profile == LandingLongProfile.Idle ? "idle" : "post-drag-idle";
            for (var i = 0; i < IdleFrames; i++)
            {
                attemptedIdleFrame = i + 1;
                // Same session and entity; all controls are zero/false throughout.
                var observation = Advance();
                var checkpoint = CheckpointFrames.Contains(completedIdleFrames);
                var gateSample = i >= 600 && (i % 60 == 0 || i >= 35400);
                if (!checkpoint && !gateSample) continue;
                var sample = Sample(session, currentNative!, observation.Shape, completedIdleFrames);
                if (checkpoint)
                    checkpoints.Add(new(completedIdleFrames, session.Frames,
                        completedIdleFrames / 60d, sample, currentNative!));
                if (!gateSample)
                {
                    capture.CheckSample(sample, checkpoint, false, currentNative!);
                    retainedDifferenceObserver?.Invoke(sample, currentNative!, spans.MaximumExistingGateDrift);
                    continue;
                }
                samples.Add(sample);
                spans.Add(sample);
                if (!sample.IsGrounded || sample.MaximumSpeed >= .03)
                    throw new LandingGateFailure(LandingFailureKind.LongSettling,
                        $"grounded={sample.IsGrounded}, maximumSpeed={sample.MaximumSpeed:R}, limit=0.03");
                // Read-only evidence comparison. Preserve the original settling guard
                // order and reject a new difference at the existing drift boundary.
                capture.CheckSample(sample, checkpoint, true, currentNative!);
                retainedDifferenceObserver?.Invoke(sample, currentNative!, spans.MaximumExistingGateDrift);
                if (spans.MaximumExistingGateDrift >= .003 &&
                    !failures.Any(item => item.Failure.Kind == LandingFailureKind.LongDrift))
                {
                    var detail = $"core/native-contour span={spans.MaximumExistingGateDrift:R}, limit=0.003; idleFrame={completedIdleFrames}";
                    if (!observeDefaultForwardDrift || !MayContinue(controls, profile, LandingFailureKind.LongDrift))
                        throw new LandingGateFailure(LandingFailureKind.LongDrift, detail);
                    RecordFailure(LandingFailureKind.LongDrift, detail);
                    continuedDefaultForwardDriftObservation = true;
                }
            }
            if (completedDragFrames != requestedDragFrames || completedIdleFrames != IdleFrames ||
                samples.Count != RequiredGateSamples || checkpoints.Count != CheckpointFrames.Length)
                throw new LandingGateFailure(LandingFailureKind.Incomplete,
                    $"drag={completedDragFrames}/{requestedDragFrames}, idle={completedIdleFrames}/{IdleFrames}, gateSamples={samples.Count}/{RequiredGateSamples}, checkpoints={checkpoints.Count}/{CheckpointFrames.Length}");
        }
        catch (LandingGateFailure error)
        {
            stopped = true;
            currentNative ??= session?.State;
            capture.PersistReturnedOnThrow(session);
            RecordFailure(error.Kind, error.Message);
        }
        catch (Exception error)
        {
            stopped = true;
            currentNative ??= session?.State;
            capture.PersistReturnedOnThrow(session);
            RecordFailure(LandingFailureKind.HealthOrStop, error.ToString());
        }
        finally
        {
            sessionReportedFrames = session?.Frames ?? 0;
            try { session?.Dispose(); }
            catch (Exception error)
            {
                stopped = true;
                phase = "disposal";
                RecordFailure(LandingFailureKind.HealthOrStop, error.ToString());
            }
            clock.Stop();
        }
        var executionCompleted = !stopped && completedDragFrames == requestedDragFrames &&
            completedIdleFrames == IdleFrames && samples.Count == RequiredGateSamples &&
            checkpoints.Count == CheckpointFrames.Length;
        var passed = executionCompleted && failure is null && spans.MaximumExistingGateDrift < .003;
        var step = MaterialSession.FixedStep;
        var completedFrames = completedDragFrames + completedIdleFrames;
        var timing = new LandingLongTiming(step.Ticks, step.TotalSeconds, (float)step.TotalSeconds,
            IdleFrames / 60d, IdleFrames * step.TotalSeconds,
            IdleFrames * (double)(float)step.TotalSeconds,
            completedFrames * step.TotalSeconds, completedFrames * (double)(float)step.TotalSeconds);
        return new("landing-build-long-verification", profile, name, controls, mappingVersion, configuration,
            requestedDragFrames, IdleFrames, completedDragFrames, completedIdleFrames,
            attemptedWorldFrame, sessionReportedFrames, renderedFrames, samples.Count,
            executionCompleted, passed, continuedDefaultForwardDriftObservation,
            peakSpeed, clock.Elapsed.TotalSeconds, timing, spans.Summarize(), checkpoints, samples,
            postTrajectoryStart, lastHealthy, failure, failures.FirstOrDefault(), failures);
    }

    private static LandingLongSample Sample(MaterialSession session, DangoBodyState state,
        ShapeMetrics shape, int idleFrame)
    {
        try
        {
            var bounds = SampleGeometry.Create(state, productionMapping: false,
                scale: DisplayScale, bodySize: session.BodySize).Bounds;
            if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
                !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) ||
                !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
                bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException("Actual pixel bounds are nonfinite or empty.");
            return new(idleFrame, session.Frames, idleFrame * MaterialSession.FixedStep.TotalSeconds,
                idleFrame * (double)(float)MaterialSession.FixedStep.TotalSeconds,
                state.CoreX, state.CoreY, shape.Width, shape.Height, state.SignedArea,
                state.MaximumSpeed, state.GroundContactCount, state.IsGrounded, shape.LowestWorldY,
                new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, bounds.Width, bounds.Height));
        }
        catch (Exception error)
        {
            throw new LandingGateFailure(LandingFailureKind.InvalidRender, error.ToString());
        }
    }

    private static bool HasFiniteNativeFloats(DangoBodyState state) =>
        state.Contour.All(float.IsFinite) && float.IsFinite(state.CoreX) && float.IsFinite(state.CoreY) &&
        float.IsFinite(state.SignedArea) && float.IsFinite(state.CentroidX) &&
        float.IsFinite(state.CentroidY) && float.IsFinite(state.MaximumSpeed);

    private static LandingLongNativeBits Bits(DangoBodyState state) =>
        new(state.EntityId.Value, state.Contour.Concat(new[] { state.CoreX, state.CoreY,
            state.SignedArea, state.CentroidX, state.CentroidY, state.MaximumSpeed })
            .Select(BitConverter.SingleToInt32Bits).ToArray(), state.GroundContactCount,
            state.IsGrounded, state.SelectedDragNode, state.PointCount);

    private sealed class SpanTracker
    {
        private readonly double[] minimum = Enumerable.Repeat(double.PositiveInfinity, 7).ToArray();
        private readonly double[] maximum = Enumerable.Repeat(double.NegativeInfinity, 7).ToArray();
        private int count;
        internal double MaximumExistingGateDrift => count == 0 ? 0 : Enumerable.Range(0, 4).Max(Span);
        private double Span(int index) => count == 0 ? 0 : maximum[index] - minimum[index];
        internal void Add(LandingLongSample sample)
        {
            var values = new[] { sample.CoreX, sample.CoreY, sample.Width, sample.Height, sample.SignedArea,
                sample.CoreRelativeDisplayPixelsBounds.Width, sample.CoreRelativeDisplayPixelsBounds.Height };
            for (var i = 0; i < values.Length; i++)
            {
                minimum[i] = Math.Min(minimum[i], values[i]);
                maximum[i] = Math.Max(maximum[i], values[i]);
            }
            count++;
        }
        internal LandingLongSpans Summarize() =>
            new(Span(0), Span(1), Span(2), Span(3), Span(4), Span(5), Span(6), MaximumExistingGateDrift);
    }
}
