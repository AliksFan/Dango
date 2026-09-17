using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Tuning;

namespace Dango.JointReview;

internal sealed class CollisionSession : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Create(in NativeSelfPhysicsConfiguration config, int count, int layout, out nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StepNative(nint handle, float dt, [In] float[] controls, [Out] float[] output, int capacity, out int contacts);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Destroy(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DisableForTest(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ContractTests(nint handle, int broken);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InteractionFlags(nint handle, int flags);
    private readonly nint library;
    private readonly StepNative step;
    private readonly Destroy destroy;
    private nint handle;
    internal int Count { get; }
    internal int Error { get; private set; }
    internal int ContactSteps { get; private set; }
    internal void DisableContactForTest() => Marshal.GetDelegateForFunctionPointer<DisableForTest>(NativeLibrary.GetExport(library, "dango_collision_disable_for_test"))(handle);
    internal void DisableDampingForTest() => Marshal.GetDelegateForFunctionPointer<DisableForTest>(NativeLibrary.GetExport(library, "dango_collision_disable_damping_for_test"))(handle);
    internal int VerifyContracts(bool broken) => Marshal.GetDelegateForFunctionPointer<ContractTests>(NativeLibrary.GetExport(library, "dango_collision_contract_tests"))(handle, broken ? 1 : 0);
    internal void SetInteractionTestFlags(int flags) => Marshal.GetDelegateForFunctionPointer<InteractionFlags>(NativeLibrary.GetExport(library, "dango_collision_test_interaction_flags"))(handle, flags);

    internal CollisionSession(string path, int count = 2, int layout = 0)
    {
        library = NativeLibrary.Load(Path.GetFullPath(path));
        try
        {
            var create = Marshal.GetDelegateForFunctionPointer<Create>(NativeLibrary.GetExport(library, "dango_collision_create"));
            step = Marshal.GetDelegateForFunctionPointer<StepNative>(NativeLibrary.GetExport(library, "dango_collision_step"));
            destroy = Marshal.GetDelegateForFunctionPointer<Destroy>(NativeLibrary.GetExport(library, "dango_collision_destroy"));
            var config = NativeSelfPhysicsConfiguration.From(DangoSelfPhysicsParameters.CreateDefault());
            var error = create(in config, count, layout, out handle);
            if (error != 0) throw new InvalidDataException($"collision create {error}");
            Count = count;
        }
        catch { NativeLibrary.Free(library); throw; }
    }

    internal Box2DSelfSoftBodyNativeFrame[] Step(float[] controls)
    {
        ObjectDisposedException.ThrowIf(handle == 0, this);
        if (controls.Length != 12) throw new ArgumentException("Two six-field controls required.", nameof(controls));
        var output = new float[84];
        Error = step(handle, (float)BaselineWindow.FixedStep.TotalSeconds, controls, output, output.Length, out var contacts);
        ContactSteps = contacts;
        if (output.Any(v => !float.IsFinite(v))) throw new InvalidDataException("Non-finite collision snapshot");
        return Enumerable.Range(0, Count).Select(r =>
        {
            var k = r * 42;
            return new Box2DSelfSoftBodyNativeFrame(Error == 0, Error, output[k..(k + 32)], (int)output[k + 41],
                (int)output[k + 38], output[k + 34], output[k + 35], output[k + 36], output[k + 32], output[k + 33],
                output[k + 37], output[k + 39] != 0, (int)output[k + 40]);
        }).ToArray();
    }

    public void Dispose()
    {
        if (handle == 0) return;
        var error = destroy(handle); handle = 0; NativeLibrary.Free(library);
        if (error != 0) throw new InvalidDataException($"collision cleanup {error}");
    }
}

internal static class CollisionVerification
{
    private static double VisibleOverlap(Box2DSelfSoftBodyNativeFrame[] frames)
    {
        var geometry = frames.Select(f =>
        {
            var scale = Math.Min(1, .98 / f.Points.Max(v => Math.Abs(v)));
            var points = Enumerable.Range(0, 16).Select(i => new Dango.Desktop.Physics.Presentation.PresentationPoint(f.Points[2 * i] * scale, f.Points[2 * i + 1] * scale)).ToArray();
            var result = Dango.Desktop.Rendering.Visuals.DangoStreamGeometryFactory.TryCreate(points, 160, 120);
            if (!result.Succeeded || result.Geometry is null) throw new InvalidDataException("Invalid contour in overlap measurement");
            var g = result.Geometry.Clone();
            g.Transform = new System.Windows.Media.TranslateTransform(f.CoreX * 80 - 80, -f.CoreY * 60 - 66);
            return g;
        }).ToArray();
        return System.Windows.Media.Geometry.Combine(geometry[0], geometry[1], System.Windows.Media.GeometryCombineMode.Intersect, null, .01,
            System.Windows.Media.ToleranceType.Absolute).GetArea(.01, System.Windows.Media.ToleranceType.Absolute);
    }
    internal static double VisibleGap(Box2DSelfSoftBodyNativeFrame[] frames)
    {
        var curves = frames.Select(f =>
        {
            var scale = Math.Min(1, .98 / f.Points.Max(v => Math.Abs(v)));
            return Enumerable.Range(0, 128).Select(s =>
            {
                int i = s / 8; double t = (s % 8) / 8d;
                double a = .5 * (1 - t) * (1 - t), b = .5 + t - t * t, c = .5 * t * t;
                double X(int n) => f.Points[(n % 16) * 2];
                double Y(int n) => f.Points[(n % 16) * 2 + 1];
                return (X: f.CoreX * 80d + 72 * scale * (a * X(i + 15) + b * X(i) + c * X(i + 1)),
                    Y: f.CoreY * 60d + 58 * scale * (a * Y(i + 15) + b * Y(i) + c * Y(i + 1)));
            }).ToArray();
        }).ToArray();
        double minimum = double.PositiveInfinity;
        for (int r = 0; r < 2; r++) foreach (var p in curves[r]) for (int j = 0; j < 128; j++)
        {
            var a = curves[1 - r][j]; var b = curves[1 - r][(j + 1) % 128];
            double x = b.X - a.X, y = b.Y - a.Y;
            double t = Math.Clamp(((p.X - a.X) * x + (p.Y - a.Y) * y) / (x * x + y * y), 0, 1);
            minimum = Math.Min(minimum, Math.Sqrt(Math.Pow(p.X - a.X - t * x, 2) + Math.Pow(p.Y - a.Y - t * y, 2)));
        }
        return minimum;
    }
    internal static void Run(string path, string output, bool broken = false)
    {
        Directory.CreateDirectory(output);
        var notes = new List<object>();
        var failures = 0;
        using (var contracts = new CollisionSession(path, 2))
        {
            var negative = contracts.VerifyContracts(true); var positive = contracts.VerifyContracts(false);
            notes.Add(new { Test = "passive-spin-and-display-gradient", NegativeFailures = negative, Failures = positive });
            if (negative <= 0 || positive != 0) failures++;
        }
        var config = DangoSelfPhysicsParameters.CreateDefault();
        if (!Box2DNativeLoader.TryLoad(Box2DNativeLoader.DefaultLibraryPath, Box2DNativeLoader.PinnedRequirements, out var originalLibrary, out _))
            throw new InvalidDataException("Original DLL unavailable");
        using (originalLibrary)
        {
            foreach (var count in new[] { 1, 2 })
            {
                if (!originalLibrary!.TryCreateSelfSoftBody(config, out var original, out _)) throw new InvalidDataException("Original create failed");
                using (original)
                using (var candidate = new CollisionSession(path, count, 2))
                {
                    candidate.DisableDampingForTest();
                    candidate.SetInteractionTestFlags(3);
                    var maxError = 0d;
                    for (int i = 0; i < 1800; i++)
                    {
                        // No inter-body contact: all original actions, with the second rig far away.
                        var c = new float[12];
                        c[0] = i is >= 600 and < 660 ? -1 : i is >= 720 and < 780 ? 1 : 0;
                        c[1] = i is >= 900 and < 960 ? 1 : 0; c[2] = i == 1000 ? 1 : 0;
                        c[3] = i is >= 1100 and < 1300 ? 1 : 0;
                        c[4] = -.5f; c[5] = 1.4f;
                        var expected = original!.Step(BaselineWindow.FixedStep, config, 0, 0, c[0], c[1], c[2] == 1, c[3] == 1, c[4], c[5]);
                        var actual = candidate.Step(c)[0];
                        if (!expected.Succeeded || !actual.Succeeded) { failures++; break; }
                        var deltas = actual.Points.Zip(expected.Points, (a, b) => Math.Abs((double)a - b))
                            .Append(Math.Abs(actual.CoreX - expected.CoreX)).Append(Math.Abs(actual.CoreY - expected.CoreY))
                            .Append(Math.Abs(actual.MaximumSpeed - expected.MaximumSpeed));
                        maxError = Math.Max(maxError, deltas.Max());
                    }
                    var passed = candidate.Error == 0 && candidate.ContactSteps == 0 && maxError <= 1e-5;
                    if (!passed) failures++;
                    notes.Add(new { Test = "original-no-contact", PassiveSpinEnabled = false, EnhancedJumpEnabled = false, ContactSoftnessEnabled = false, Count = count, Frames = 1800, MaxError = maxError, candidate.Error, candidate.ContactSteps, Passed = passed });
                }
            }
        }
        foreach (var layout in new[] { 0, 1 })
        {
            using var candidate = new CollisionSession(path, 2, layout);
            if (broken) candidate.DisableContactForTest();
            Box2DSelfSoftBodyNativeFrame[]? frame = null;
            int i = 0; int releaseContacts = 0; float maximumSecondX = layout == 0 ? 2.8f : 0;
            Box2DSelfSoftBodyNativeFrame[]? touching = null;
            double maxOverlap = 0, maxOverlapRatio = 0;
            for (; i < 1800; i++)
            {
                var controls = new float[12];
                if (layout == 0) controls[0] = i is >= 600 and < 780 ? 1 : i is >= 780 and < 1020 ? -1 : 0;
                frame = candidate.Step(controls);
                if (candidate.ContactSteps > 0 && touching is null && candidate.Error == 0) touching = frame;
                maximumSecondX = Math.Max(maximumSecondX, frame[1].CoreX);
                if (i == 1300) releaseContacts = candidate.ContactSteps;
                if (candidate.Error != 0) break;
                if (i % 10 == 0)
                {
                    var contact = CushionVerification.Contact(frame);
                    double area = VisibleOverlap(frame);
                    maxOverlap = Math.Max(maxOverlap, area);
                    maxOverlapRatio = Math.Max(maxOverlapRatio, -contact.Ratio);
                }
            }
            var gap = touching is null ? (double?)null : VisibleGap(touching);
            var passed = candidate.Error == 0 && candidate.ContactSteps > 0 && gap <= 1 && maxOverlapRatio <= .031 &&
                (layout == 1 || (maximumSecondX <= 2.9f && releaseContacts == candidate.ContactSteps));
            if (!passed) failures++;
            notes.Add(new
            {
                Test = layout == 0 ? "side-push-and-release" : "natural-stack-exploration",
                Frames = i,
                candidate.Error,
                candidate.ContactSteps,
                MaximumSecondX = maximumSecondX,
                FirstContactGapPixels = gap,
                MaximumSampledOverlapPixelsSquared = maxOverlap,
                MaximumOverlapRatio = maxOverlapRatio,
                Passed = passed
            });
            if (frame is not null && candidate.Error == 0 && layout == 0)
                CollisionWindow.SaveSnapshot(path, frame, Path.Combine(output, "collision.png"));
            if (touching is not null) CollisionWindow.SaveSnapshot(path, touching, Path.Combine(output, $"contact-layout-{layout}.png"));
        }
        using var report = new FileStream(Path.Combine(output, "verification.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(report, new { Failures = failures, Tests = notes }, new JsonSerializerOptions { WriteIndented = true });
        if (failures > 0) throw new InvalidDataException($"Collision verification failures={failures}; see verification.json");
    }
}
