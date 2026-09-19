using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class DangoPhysicsEquivalenceTests
{
    [TestMethod]
    [DataRow(1, 2)]
    [DataRow(2, 2)]
    [DataRow(2, 0)]
    [DataRow(2, 1)]
    public void ExtractedModuleMatchesPreExtractionTrajectoryExactly(int count, int layout)
    {
        var root = FindRoot();
        var module = PhysicsTestLibraries.Unified;
        var baseline = Path.Combine(root, "artifacts", "native-spike", "collision-ratio-build", "Release", "dango_collision_review.dll");
        using var old = new LegacySession(baseline, count, layout);
        using var world = DangoPhysicsWorld.Create(module);
        var ids = Enumerable.Range(0, count).Select(index => world.Add(
            index == 0 ? 0 : layout == 1 ? 0 : layout == 2 ? 4 : 2.8,
            index == 1 && layout == 1 ? 1.65 : 0)).ToArray();

        for (var frame = 0; frame < 1800; frame++)
        {
            var controls = Controls(count, layout, frame);
            var expected = old.Step(controls);
            var inputs = ids.Select((id, index) => new DangoPhysicsInput(id,
                controls[index * 6], controls[index * 6 + 1], controls[index * 6 + 2] != 0,
                controls[index * 6 + 3] != 0, controls[index * 6 + 4], controls[index * 6 + 5])).ToArray();
            var actual = world.Step(TimeSpan.FromSeconds(1d / 60), inputs);
            Assert.AreEqual(old.Error, actual.ErrorCode, $"error at frame {frame}");
            Assert.AreEqual(old.ContactSteps, actual.ContactStepCount, $"contacts at frame {frame}");
            Assert.AreEqual(count, actual.Bodies.Count, $"count at frame {frame}");
            for (var body = 0; body < count; body++)
            {
                var row = ToRaw(actual.Bodies[body]);
                for (var field = 0; field < 42; field++)
                    Assert.AreEqual(BitConverter.SingleToInt32Bits(expected[body * 42 + field]), BitConverter.SingleToInt32Bits(row[field]),
                        $"bit mismatch at frame {frame}, body {body}, field {field}");
            }
        }
    }

    private static float[] Controls(int count, int layout, int frame)
    {
        var result = new float[12];
        result[0] = frame is >= 300 and < 420 ? 1 : frame is >= 420 and < 540 ? -1 : 0;
        result[1] = frame is >= 600 and < 660 ? 1 : 0;
        result[2] = frame == 720 ? 1 : 0;
        result[3] = frame is >= 840 and < 960 ? 1 : 0;
        result[4] = -0.5f; result[5] = 1.4f;
        if (count == 2)
        {
            result[6] = layout == 0 && frame is >= 1000 and < 1180 ? -1 : 0;
            result[8] = layout == 1 && frame == 1200 ? 1 : 0;
            result[10] = 0.5f; result[11] = 1.4f;
        }
        return result;
    }

    private static float[] ToRaw(DangoBodyState body)
    {
        var row = new float[42];
        for (var index = 0; index < body.Contour.Count; index++) row[index] = body.Contour[index];
        row[32] = body.CoreX; row[33] = body.CoreY; row[34] = body.SignedArea; row[35] = body.CentroidX; row[36] = body.CentroidY;
        row[37] = body.MaximumSpeed; row[38] = body.GroundContactCount; row[39] = body.IsGrounded ? 1 : 0;
        row[40] = body.SelectedDragNode; row[41] = body.PointCount;
        return row;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dango.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class LegacySession : IDisposable
    {
        private readonly nint library;
        private readonly StepDelegate step;
        private readonly DestroyDelegate destroy;
        private nint handle;
        internal int Error { get; private set; }
        internal int ContactSteps { get; private set; }

        internal LegacySession(string path, int count, int layout)
        {
            library = NativeLibrary.Load(path);
            var create = Marshal.GetDelegateForFunctionPointer<CreateDelegate>(NativeLibrary.GetExport(library, "dango_collision_create"));
            step = Marshal.GetDelegateForFunctionPointer<StepDelegate>(NativeLibrary.GetExport(library, "dango_collision_step"));
            destroy = Marshal.GetDelegateForFunctionPointer<DestroyDelegate>(NativeLibrary.GetExport(library, "dango_collision_destroy"));
            var config = NativeConfig.From(DangoPhysicsDefaults.Current);
            var error = create(in config, count, layout, out handle);
            if (error != 0) throw new InvalidOperationException($"legacy create {error}");
        }

        internal float[] Step(float[] controls)
        {
            var output = new float[84];
            Error = step(handle, (float)TimeSpan.FromSeconds(1d / 60).TotalSeconds, controls, output, output.Length, out var contacts); ContactSteps = contacts;
            return output;
        }

        public void Dispose()
        {
            if (handle != 0) { var error = destroy(handle); handle = 0; if (error != 0) throw new InvalidOperationException($"legacy destroy {error}"); }
            NativeLibrary.Free(library);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateDelegate(in NativeConfig config, int count, int layout, out nint handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StepDelegate(nint handle, float dt, [In] float[] controls, [Out] float[] output, int capacity, out int contacts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DestroyDelegate(nint handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeConfig
    {
        public int NodeCount, SubStepCount;
        public float RadialRestLength, RadialMinimumRatio, RadialMaximumRatio, PerimeterMinimumRatio, PerimeterMaximumRatio, RadialHertz, PerimeterHertz;
        public float RadialDamping, PerimeterDamping, AreaStrength, GroundDeformationStrength, RepulsionDistance, RepulsionStrength, RepulsionMaximumForce;
        public float CoreExclusionRatio, AngleStrength, LocalAngleBalanceStrength, TopInwardForce, Gravity, CoreMass, NodeMass, DropHeight, GroundHeight, NodeRadius;
        public float Friction, Restitution, ContactHertz, LinearDamping, DragStiffness, DragDamping, DragMaximumForce, HorizontalForce, MaximumHorizontalSpeed;
        public float AirControlRatio, DownwardForce, JumpImpulse, JumpCooldownSeconds, MaximumSpeed, MinimumArea;
        public static NativeConfig From(DangoSelfPhysicsParameters v) => new()
        {
            NodeCount = v.NodeCount,
            SubStepCount = v.SubStepCount,
            RadialRestLength = (float)v.RadialRestLength,
            RadialMinimumRatio = (float)v.RadialMinimumRatio,
            RadialMaximumRatio = (float)v.RadialMaximumRatio,
            PerimeterMinimumRatio = (float)v.PerimeterMinimumRatio,
            PerimeterMaximumRatio = (float)v.PerimeterMaximumRatio,
            RadialHertz = (float)v.RadialElasticity,
            PerimeterHertz = (float)v.PerimeterElasticity,
            RadialDamping = (float)v.RadialDamping,
            PerimeterDamping = (float)v.PerimeterDamping,
            AreaStrength = (float)v.AreaPreservationStrength,
            GroundDeformationStrength = (float)v.GroundDeformationStrength,
            RepulsionDistance = (float)v.BoneRepulsionDistance,
            RepulsionStrength = (float)v.BoneRepulsionStrength,
            RepulsionMaximumForce = (float)v.BoneRepulsionMaximumForce,
            CoreExclusionRatio = (float)v.CoreExclusionRatio,
            AngleStrength = (float)v.PerimeterAngleStrength,
            LocalAngleBalanceStrength = (float)v.LocalAngleBalanceStrength,
            TopInwardForce = (float)v.TopInwardForce,
            Gravity = (float)v.Gravity,
            CoreMass = (float)v.CoreMass,
            NodeMass = (float)v.NodeMass,
            DropHeight = (float)v.DropHeight,
            GroundHeight = (float)v.GroundHeight,
            NodeRadius = (float)v.NodeRadius,
            Friction = (float)v.GroundFriction,
            Restitution = (float)v.Restitution,
            ContactHertz = (float)v.ContactHertz,
            LinearDamping = (float)v.OverallDamping,
            DragStiffness = (float)v.DragStiffness,
            DragDamping = (float)v.DragDamping,
            DragMaximumForce = (float)v.DragMaximumForce,
            HorizontalForce = (float)v.HorizontalForce,
            MaximumHorizontalSpeed = (float)v.MaximumHorizontalSpeed,
            AirControlRatio = (float)v.AirControlRatio,
            DownwardForce = (float)v.DownwardForce,
            JumpImpulse = (float)v.JumpImpulse,
            JumpCooldownSeconds = (float)(v.JumpCooldownMilliseconds / 1000),
            MaximumSpeed = (float)v.MaximumSpeed,
            MinimumArea = (float)v.MinimumArea
        };
    }
}
