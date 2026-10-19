using System.IO;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Tuning;
using Dango.PhysicsLab;

namespace Dango.Desktop.Tests.PhysicsLab;

[TestClass]
public sealed class LabBeltWorkerNativeEquivalenceTests
{
    private const string FrozenHash = "C455EAD0272CA847AE05B2FDCBCC4D09662A1D16886CF6CD3EDC3DAA151DDDCA";
    private const string ModuleEnvironment = "DANGO_BELT_WORKER_NATIVE_MODULE";
    private static readonly string[] Channels = ["scaffold", "curve", "stats", "diagnostics", "shape", "debug", "solver", "speed", "radial", "rear", "ledger"];
    private sealed record Phase(string Name, int Frames, double Horizontal = 0, double Down = 0, double Up = 0, bool Jump = false, bool Drag = false);
    // The approved twelve-phase fixture, including the complete cold rest phase.
    private static readonly Phase[] Phases = [new("rest", 300), new("right", 180, Horizontal: 1), new("stop-right", 180),
        new("left", 180, Horizontal: -1), new("stop-left", 180), new("S", 60, Down: 1), new("S-release", 120),
        new("W", 120, Up: 1), new("W-release", 120), new("Space", 180, Jump: true), new("drag-compress", 90, Drag: true), new("drag-release", 180)];

    [TestMethod, TestCategory("NativeIntegration")] public void Corner80_0_0() => CompareCorner(80, 0, 0);
    [TestMethod, TestCategory("NativeIntegration")] public void Corner80_0_100() => CompareCorner(80, 0, 100);
    [TestMethod, TestCategory("NativeIntegration")] public void Corner80_100_0() => CompareCorner(80, 100, 0);
    [TestMethod, TestCategory("NativeIntegration")] public void Corner80_100_100() => CompareCorner(80, 100, 100);
    [TestMethod, TestCategory("NativeIntegration")] public void Corner120_0_0() => CompareCorner(120, 0, 0);
    [TestMethod, TestCategory("NativeIntegration")] public void Corner120_0_100() => CompareCorner(120, 0, 100);
    [TestMethod, TestCategory("NativeIntegration")] public void Corner120_100_0() => CompareCorner(120, 100, 0);
    [TestMethod, TestCategory("NativeIntegration")] public void Corner120_100_100() => CompareCorner(120, 100, 100);

    [TestMethod, TestCategory("NativeIntegration")]
    public void RejectedInputPreservesCompleteFailureEvidenceAndAtomicNativeState()
    {
        var module = FrozenModule();
        var config = DangoBodyMaterial.Configure(new DangoBodyControls(80, 0, 0));
        var calibration = Calibration(config.OverallDamping);
        var worker = new LabBeltPhysicsWorker();
        try
        {
            Complete(worker.ConfigureAsync(() => new LabBeltNativeWorkerSession(module, config, true, calibration, true, true)));
            var before = Complete(worker.ReadStateAsync());
            var invalid = new LabBeltInput(Horizontal: double.NaN, DragActive: true, DragTargetX: 12, DragTargetY: -7, InputActive: true);
            var input = new LabInputSnapshot(double.NaN, 0, 0, true, false, false, false);
            var coldFailure = Complete(worker.StepAsync(input, invalid));
            // The frozen native module initializes solver_diag[0] on its first actual Step.
            // A managed input rejection before that must retain both the input and readback errors.
            Assert.IsInstanceOfType<AggregateException>(coldFailure.Error, coldFailure.Error?.ToString());
            var errors = ((AggregateException)coldFailure.Error).Flatten().InnerExceptions;
            Assert.AreEqual(2, errors.Count);
            Assert.IsInstanceOfType<ArgumentOutOfRangeException>(errors[0]);
            Assert.IsInstanceOfType<NotSupportedException>(errors[1]);
            StringAssert.Contains(errors[1].Message, "solver diagnostics schema");
            Assert.IsNull(coldFailure.SolverFailure);
            CollectionAssert.AreEqual(before["debug"], MemoryMarshal.AsBytes(coldFailure.PreState!.AsSpan()).ToArray());
            Compare(before, Complete(worker.ReadStateAsync()), "cold rejected input atomic state");
            Assert.AreEqual(0, worker.Metrics.ProducedTicks);
            var first = Complete(worker.StepAsync(default, default));
            Assert.IsNull(first.Error, first.Error?.ToString());
            before = Complete(worker.ReadStateAsync());
            var failed = Complete(worker.StepAsync(input, invalid));
            Assert.IsInstanceOfType<ArgumentOutOfRangeException>(failed.Error, failed.Error?.ToString());
            Assert.AreEqual(2, failed.Tick);
            Assert.AreEqual(worker.Epoch, failed.Epoch);
            Assert.AreEqual(LabWindow.FixedDt, failed.FixedDt);
            Assert.IsTrue(double.IsNaN(failed.Input.Horizontal));
            Assert.IsTrue(double.IsNaN(failed.ActualInput.Horizontal));
            Assert.AreEqual(invalid.DragTargetX, failed.ActualInput.DragTargetX);
            Assert.AreEqual(invalid.DragTargetY, failed.ActualInput.DragTargetY);
            Assert.IsTrue(failed.ActualInput.DragActive);
            Assert.IsNotNull(failed.PreState);
            CollectionAssert.AreEqual(before["debug"], MemoryMarshal.AsBytes(failed.PreState.AsSpan()).ToArray(), "Failure must carry the complete exact pre-state.");
            Assert.AreEqual(24, failed.SolverFailure!.Length);
            Assert.AreEqual(24, failed.Guard!.Length);
            Assert.AreEqual(12, failed.Speed!.Length);
            Assert.AreEqual(16, failed.Radial!.Length);
            Assert.AreEqual(16, failed.Rear!.Length);
            Assert.AreEqual(1024, failed.Ledger!.Length);
            Compare(before, Complete(worker.ReadStateAsync()), "rejected input atomic state");
            Assert.AreEqual(1, worker.Metrics.ProducedTicks);
        }
        finally { Complete(worker.CloseAsync()); }
    }

    private static void CompareCorner(double f0, double f1, double f2)
    {
        var module = FrozenModule();
        var config = DangoBodyMaterial.Configure(new DangoBodyControls(f0, f1, f2));
        var calibration = Calibration(config.OverallDamping);
        // No awaits: the baseline handle remains on this exact test thread for its entire life.
        using var serial = new LabBeltWorld(module, config, true, calibration, enableShapeGuard: true, enableSpeedSaturation: true, enableRadialDrive: true);
        serial.SetOriginalGroundJump(); serial.SetBounds(-256, 256);
        var worker = new LabBeltPhysicsWorker();
        try
        {
            Complete(worker.ConfigureAsync(() => new LabBeltNativeWorkerSession(module, config, true, calibration, true, true)));
            Compare(serial.ReadPerformanceState(), Complete(worker.ReadStateAsync()), "initial");
            var tick = 0;
            LabBeltStepSnapshot? retained = null;
            float[]? retainedCurve = null;
            double[]? retainedDebug = null;
            foreach (var phase in Phases)
            {
                var initial = serial.Read();
                var dragX = initial.ComX;
                var dragY = config.GroundHeight + .05 * initial.Height;
                for (var i = 0; i < phase.Frames; i++)
                {
                    var intent = new LabBeltInput(Horizontal: phase.Horizontal, Downward: phase.Down, Stretch: phase.Up,
                        JumpPressed: phase.Jump && i == 0, DragActive: phase.Drag, DragTargetX: dragX, DragTargetY: dragY,
                        InputActive: phase.Horizontal != 0 || phase.Down != 0 || phase.Up != 0 || phase.Drag || phase.Jump && i == 0);
                    var input = new LabInputSnapshot(intent.Horizontal, intent.Downward, intent.Stretch, intent.InputActive, intent.JumpPressed, false, false);
                    serial.Step(LabWindow.FixedDt, intent);
                    var snapshot = Complete(worker.StepAsync(input, intent)); tick++;
                    Assert.IsNull(snapshot.Error, $"{phase.Name}/{i}: {snapshot.Error}");
                    Assert.AreEqual(tick, snapshot.Tick);
                    Assert.AreEqual(intent, snapshot.ActualInput);
                    Assert.AreEqual(LabWindow.FixedDt, snapshot.FixedDt);
                    Compare(serial.ReadPerformanceState(), Complete(worker.ReadStateAsync()), $"{f0}/{f1}/{f2} tick {tick} {phase.Name}/{i}");
                    if (retained is null)
                    {
                        retained = snapshot;
                        retainedCurve = snapshot.Frame!.Curve.ToArray();
                        retainedDebug = snapshot.PreState!.ToArray();
                    }
                    else
                    {
                        Assert.AreNotSame(retained.Frame!.Curve, snapshot.Frame!.Curve);
                        Assert.AreNotSame(retained.PreState, snapshot.PreState);
                        CollectionAssert.AreEqual(retainedCurve!, retained.Frame.Curve, "Published geometry was mutated by a later tick.");
                        CollectionAssert.AreEqual(retainedDebug!, retained.PreState!, "Published pre-state was mutated by a later tick.");
                    }
                }
            }
            Assert.AreEqual(1890, tick);
        }
        finally { Complete(worker.CloseAsync()); }
    }

    private static void Compare(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual, string location)
    {
        CollectionAssert.AreEquivalent(Channels, expected.Keys.ToArray(), location);
        CollectionAssert.AreEquivalent(Channels, actual.Keys.ToArray(), location);
        foreach (var name in Channels)
        {
            Assert.AreEqual(expected[name].Length, actual[name].Length, $"{location}: {name} length");
            var mismatch = expected[name].AsSpan().SequenceEqual(actual[name]);
            if (!mismatch)
            {
                var offset = Enumerable.Range(0, expected[name].Length).First(i => expected[name][i] != actual[name][i]);
                Assert.Fail($"{location}: {name} byte {offset}; serial={expected[name][offset]:X2}, worker={actual[name][offset]:X2}");
            }
        }
    }

    private static LabBeltCalibrationParameters Calibration(double damping) => LabBeltCalibrationParameters.Default with
    {
        RadialStiffnessScale = 1.4, AreaScale = 1.4, BeltBendScale = 4,
        OverallDamping = damping, GroundedBrake = 35, GroundedHorizontalDamping = 60,
    };

    private static string FrozenModule()
    {
        var explicitModule = Environment.GetEnvironmentVariable(ModuleEnvironment);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "eng", "tools", "Dango.PhysicsLab"))) directory = directory.Parent;
        Assert.IsNotNull(directory, "Cannot locate the repository for the frozen native artifact.");
        var module = explicitModule ?? Path.Combine(directory.FullName, "artifacts", "physics-lab", "belt-57-memo-native", "Release", "dango_physics_lab_belt.dll");
        if (!File.Exists(module))
        {
            if (explicitModule is not null) Assert.Fail($"{ModuleEnvironment} does not exist: {module}");
            Assert.Inconclusive($"Frozen native artifact absent; set {ModuleEnvironment} to its absolute path. Expected {module}");
        }
        Assert.IsTrue(Path.IsPathFullyQualified(module), $"{ModuleEnvironment} must be an absolute path.");
        Assert.AreEqual(FrozenHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))), "The approved frozen native module changed.");
        return module;
    }

    private static T Complete<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    private static void Complete(Task task) => task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
}
