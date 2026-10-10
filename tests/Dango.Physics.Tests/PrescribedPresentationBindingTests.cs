using System.Reflection;
using Dango.Physics;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class PrescribedPresentationBindingTests
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static DangoPrescribedPresentationStep First(DangoEntityId id) => new(7, 1, 0, 1, 1, 1,
        [new(id, false, false, null, null, new(id, 0, 3, 0, 0), new(id, 0, 3, 0, 0))], [], []);

    [TestMethod]
    public void InvalidInputDoesNotConsumeTheNextOrdinalOrMoveTheLastLegalState()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, 2);
        world.BindPrescribedEpoch(7); var id = world.Add(-10, 0); var first = First(id);
        var legal = world.StepPrescribedPresentation(first);
        Assert.IsTrue(legal.Succeeded);
        var next = first with { StepOrdinal = 2, StartSampleTime = 1, EndSampleTime = 2 };
        var invalid = new[]
        {
            next with { Epoch = 8 }, next with { StepOrdinal = 1 }, next with { StepOrdinal = 3 },
            next with { StartSampleTime = double.NaN }, next with { EndSampleTime = 3 },
            next with { StartSceneRevision = 0 }, next with { StartSceneRevision = 2, EndSceneRevision = 2 },
            next with { Bodies = [next.Bodies[0] with { End = new(id, float.NaN, 3, 0, 0) }] },
            next with { Bodies = [next.Bodies[0] with { End = new(new(id.Value + 99), 0, 3, 0, 0) }] },
            next with { Bodies = [next.Bodies[0] with { StartSupportEntityId = new(id.Value + 99) }] },
            next with { Bodies = [next.Bodies[0] with { StartFloorGrounded = true }] },
        };
        foreach (var input in invalid)
            Assert.ThrowsExactly<DangoPhysicsException>(() => world.StepPrescribedPresentation(input));
        var after = world.StepPrescribedPresentation(next);
        Assert.IsTrue(after.Succeeded);
        Assert.AreEqual(2ul, after.Trace.StepOrdinal);
        Assert.AreEqual(legal.Frame.Bodies[0].CoreX, after.Frame.Bodies[0].CoreX);
        Assert.AreEqual(legal.Frame.Bodies[0].CoreY, after.Frame.Bodies[0].CoreY);
    }

    [TestMethod]
    public void RealNativeDiagnosticRejectsCorruptedIdentityOrdinalFiniteAndUnusedSlots()
    {
        using var world = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, 2);
        world.BindPrescribedEpoch(7); var id = world.Add(-10, 0); var input = First(id);
        var actual = world.StepPrescribedPresentation(input);
        Assert.IsTrue(actual.Succeeded);
        var body = actual.Frame.Bodies.Single();
        var raw = body.Contour.Concat(new[] { body.CoreX, body.CoreY, body.SignedArea, body.CentroidX, body.CentroidY,
            body.MaximumSpeed, body.GroundContactCount, body.IsGrounded ? 1f : 0, body.SelectedDragNode, body.PointCount }).ToArray();
        var states = raw.Concat(new float[42]).ToArray(); var ids = new[] { id.Value, 0 };
        var type = typeof(DangoPhysicsWorld);
        var traceType = type.GetNestedType("NativePrescribedTrace", BindingFlags.NonPublic)!;
        var sampleType = type.GetNestedType("NativePrescribedSample", BindingFlags.NonPublic)!;
        var validate = type.GetMethod("ValidatePrescribedOutput", Fields)!;
        static void Set(object value, string name, object field) => value.GetType().GetField(name, Fields)!.SetValue(value, field);
        object Trace()
        {
            var trace = Activator.CreateInstance(traceType)!;
            Set(trace, "AbiVersion", 1u); Set(trace, "FrameSize", (uint)DangoPhysicsWorld.PrescribedPresentationTraceSize);
            Set(trace, "Epoch", actual.Trace.Epoch); Set(trace, "Ordinal", actual.Trace.StepOrdinal);
            Set(trace, "ErrorCode", actual.Trace.ErrorCode); Set(trace, "BodyCount", 1);
            Set(trace, "SubstepCount", actual.Trace.SubstepCount); Set(trace, "SampleCount", actual.Trace.Samples[0].Count);
            Set(trace, "EntityIds", (int[])ids.Clone()); Set(trace, "MaximumErrors", new[] { actual.Trace.MaximumCoreErrors[0], 0f });
            var samples = Array.CreateInstance(sampleType, 194);
            for (var index = 0; index < actual.Trace.Samples[0].Count; index++)
            {
                var sample = Activator.CreateInstance(sampleType)!; var source = actual.Trace.Samples[0][index];
                Set(sample, "Fraction", source.Fraction); Set(sample, "ActualX", source.ActualCoreX); Set(sample, "ActualY", source.ActualCoreY);
                Set(sample, "TargetX", source.TargetCoreX); Set(sample, "TargetY", source.TargetCoreY); samples.SetValue(sample, index);
            }
            Set(trace, "Samples", samples); return trace;
        }
        void Invoke(object trace, int count = 1, int[]? outputIds = null) => validate.Invoke(world,
            [input, outputIds ?? ids, states, count, actual.Frame.ContactStepCount, trace]);
        void Reject(Action<object> corrupt, int count = 1, int[]? outputIds = null)
        {
            var trace = Trace(); corrupt(trace);
            var error = Assert.ThrowsExactly<TargetInvocationException>(() => Invoke(trace, count, outputIds));
            Assert.IsInstanceOfType<DangoPhysicsException>(error.InnerException);
            Assert.AreEqual(DangoPhysicsFailureKind.AbiMismatch, ((DangoPhysicsException)error.InnerException!).Kind);
        }
        Invoke(Trace()); // The untouched diagnostic originates from the real native solver.
        Reject(trace => Set(trace, "Ordinal", 2ul));
        Reject(trace => Set(trace, "Epoch", 8ul));
        Reject(trace => Set(trace, "BodyCount", 2));
        Reject(_ => { }, count: -1); Reject(_ => { }, count: 3);
        Reject(_ => { }, outputIds: [id.Value + 99, 0]);
        Reject(trace => Set(trace, "EntityIds", new[] { id.Value + 99, 0 }));
        Reject(trace => Set(trace, "EntityIds", new[] { id.Value, id.Value }));
        Reject(trace => Set(trace, "MaximumErrors", new[] { float.NaN, 0f }));
        Reject(trace => Set(trace, "MaximumErrors", new[] { 0f, .1f }));
        Reject(trace =>
        {
            var samples = (Array)traceType.GetField("Samples", Fields)!.GetValue(trace)!;
            var sample = samples.GetValue(3)!; Set(sample, "ActualX", float.NaN); samples.SetValue(sample, 3);
        });
        Reject(trace =>
        {
            var samples = (Array)traceType.GetField("Samples", Fields)!.GetValue(trace)!;
            var sample = samples.GetValue(97)!; Set(sample, "ActualX", .1f); samples.SetValue(sample, 97);
        });
    }
}
