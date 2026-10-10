using System.Runtime.InteropServices;

namespace Dango.Physics;

public sealed record DangoPrescribedPresentationBody(
    DangoEntityId EntityId, bool StartFloorGrounded, bool EndFloorGrounded,
    DangoEntityId? StartSupportEntityId, DangoEntityId? EndSupportEntityId,
    DangoPresentationTarget Start, DangoPresentationTarget End);
public sealed record DangoPrescribedPresentationPair(
    DangoEntityId EntityA, DangoEntityId EntityB, bool Touching, bool SupportsA, bool SupportsB,
    float NormalX, float NormalY, float Gap, float MinimumProjectedWidth);
public sealed record DangoPrescribedPresentationStep(
    ulong Epoch, ulong StepOrdinal, double StartSampleTime, double EndSampleTime,
    ulong StartSceneRevision, ulong EndSceneRevision,
    IReadOnlyList<DangoPrescribedPresentationBody> Bodies,
    IReadOnlyList<DangoPrescribedPresentationPair> StartPairs,
    IReadOnlyList<DangoPrescribedPresentationPair> EndPairs);
public readonly record struct DangoPrescribedCoreSample(
    float Fraction, float ActualCoreX, float ActualCoreY, float TargetCoreX, float TargetCoreY);
public sealed record DangoPrescribedPresentationTrace(
    ulong Epoch, ulong StepOrdinal, int ErrorCode, int SubstepCount,
    IReadOnlyList<DangoEntityId> BodyIds, IReadOnlyList<float> MaximumCoreErrors,
    IReadOnlyList<IReadOnlyList<DangoPrescribedCoreSample>> Samples)
{
    public bool Succeeded => ErrorCode == 0;
}
public sealed record DangoPrescribedPresentationResult(DangoPhysicsFrame Frame, DangoPrescribedPresentationTrace Trace)
{
    public bool Succeeded => Frame.Succeeded && Trace.Succeeded;
}

public sealed partial class DangoPhysicsWorld
{
    public const int PrescribedPresentationInputSize = 248;
    public const int PrescribedPresentationTraceSize = 3936;
    private ulong prescribedEpoch;
    private ulong prescribedOrdinal, prescribedEndRevision;
    private double prescribedEndTime;

    public void BindPrescribedEpoch(ulong epoch)
    {
        VerifyAccess();
        if (epoch == 0) throw new ArgumentOutOfRangeException(nameof(epoch));
        VerifyPrescribedAbi();
        var call = api.BindPrescribedEpoch ?? throw MissingLocalExport("dango_physics_world_bind_prescribed_epoch");
        var error = call(world, epoch);
        if (error != 0) throw Map(error, "绑定规定中心模式失败；须首次步进前显式绑定且不能混用世界角色。");
        prescribedEpoch = epoch;
    }

    public DangoPrescribedPresentationResult StepPrescribedPresentation(DangoPrescribedPresentationStep input)
    {
        VerifyAccess();
        ArgumentNullException.ThrowIfNull(input);
        VerifyPrescribedAbi();
        if (prescribedEpoch == 0 || input.Epoch != prescribedEpoch) throw Map(-421, "规定中心帧的代际未显式绑定。");
        ArgumentNullException.ThrowIfNull(input.Bodies);
        ArgumentNullException.ThrowIfNull(input.StartPairs);
        ArgumentNullException.ThrowIfNull(input.EndPairs);
        if (!double.IsFinite(input.StartSampleTime) || !double.IsFinite(input.EndSampleTime) ||
            Math.Abs(input.EndSampleTime - input.StartSampleTime - 1) > 1e-9 || input.StepOrdinal == 0 ||
            prescribedOrdinal == ulong.MaxValue || input.StepOrdinal != prescribedOrdinal + 1 ||
            input.StartSceneRevision == 0 || input.EndSceneRevision < input.StartSceneRevision ||
            prescribedOrdinal != 0 && (input.StartSampleTime != prescribedEndTime || input.StartSceneRevision != prescribedEndRevision))
            throw Map(-410, "规定中心帧的时间、序号或真实场景版本不连续。");
        if (input.Bodies.Count != entities.Count || input.Bodies.Count is < 1 or > 2 || input.StartPairs.Count > 1 || input.EndPairs.Count > 1)
            throw Map(-410, "规定中心帧必须覆盖当前全部实体及有界接触。");
        var native = new NativePrescribedStep
        {
            AbiVersion = 1, FrameSize = PrescribedPresentationInputSize, Epoch = input.Epoch, Ordinal = input.StepOrdinal,
            StartTime = input.StartSampleTime, EndTime = input.EndSampleTime,
            StartRevision = input.StartSceneRevision, EndRevision = input.EndSceneRevision,
            BodyCount = input.Bodies.Count, StartPairCount = input.StartPairs.Count, EndPairCount = input.EndPairs.Count,
            Bodies = new NativePrescribedBody[2], StartPairs = new NativeAuthorityPair[1], EndPairs = new NativeAuthorityPair[1],
        };
        var seen = new HashSet<DangoEntityId>();
        for (var b = 0; b < input.Bodies.Count; b++)
        {
            var body = input.Bodies[b];
            if (!entities.ContainsKey(body.EntityId) || !seen.Add(body.EntityId) || body.Start.EntityId != body.EntityId || body.End.EntityId != body.EntityId)
                throw Map(-412, "规定中心帧不能包含未知、重复或不一致的身份。");
            if (!Finite(body.Start.CoreX, body.Start.CoreY, body.Start.CoreVelocityX, body.Start.CoreVelocityY,
                body.End.CoreX, body.End.CoreY, body.End.CoreVelocityX, body.End.CoreVelocityY) ||
                body.StartSupportEntityId is { } startSupport && (!entities.ContainsKey(startSupport) || startSupport == body.EntityId) ||
                body.EndSupportEntityId is { } endSupport && (!entities.ContainsKey(endSupport) || endSupport == body.EntityId))
                throw Map(-410, "规定中心帧包含非有限目标或无效支撑身份。");
            native.Bodies[b] = new()
            {
                EntityId = body.EntityId.Value, StartFloor = body.StartFloorGrounded ? 1u : 0, EndFloor = body.EndFloorGrounded ? 1u : 0,
                StartSupport = body.StartSupportEntityId?.Value ?? 0, EndSupport = body.EndSupportEntityId?.Value ?? 0,
                StartX = body.Start.CoreX, StartY = body.Start.CoreY, StartVx = body.Start.CoreVelocityX, StartVy = body.Start.CoreVelocityY,
                EndX = body.End.CoreX, EndY = body.End.CoreY, EndVx = body.End.CoreVelocityX, EndVy = body.End.CoreVelocityY,
            };
        }
        ValidatePrescribedPairs(input.Bodies, input.StartPairs, end: false);
        ValidatePrescribedPairs(input.Bodies, input.EndPairs, end: true);
        static NativeAuthorityPair Pair(DangoPrescribedPresentationPair pair) => new()
        {
            EntityA = pair.EntityA.Value, EntityB = pair.EntityB.Value,
            Flags = (pair.Touching ? 1u : 0) | (pair.SupportsA ? 2u : 0) | (pair.SupportsB ? 4u : 0),
            NormalX = pair.NormalX, NormalY = pair.NormalY, Gap = pair.Gap, MinimumProjectedWidth = pair.MinimumProjectedWidth,
        };
        if (input.StartPairs.Count == 1) native.StartPairs[0] = Pair(input.StartPairs[0]);
        if (input.EndPairs.Count == 1) native.EndPairs[0] = Pair(input.EndPairs[0]);
        var trace = new NativePrescribedTrace { EntityIds = new int[2], MaximumErrors = new float[2], Samples = new NativePrescribedSample[194] };
        var ids = new int[capacity]; var states = new float[capacity * 42];
        var call = api.StepPrescribed ?? throw MissingLocalExport("dango_physics_world_step_prescribed");
        var code = call(world, ref native, PrescribedPresentationInputSize, ids, states, states.Length,
            out var count, out var contacts, ref trace, PrescribedPresentationTraceSize);
        if (code != 0) throw Map(code, "规定中心帧校验失败；整组未部分应用。");
        ValidatePrescribedOutput(input, ids, states, count, contacts, trace);
        var bodies = Enumerable.Range(0, count).Select(index =>
        {
            var offset = index * 42;
            return new DangoBodyState(new(ids[index]), states[offset..(offset + 32)], states[offset + 32], states[offset + 33],
                states[offset + 34], states[offset + 35], states[offset + 36], states[offset + 37],
                (int)states[offset + 38], states[offset + 39] != 0, (int)states[offset + 40], (int)states[offset + 41]);
        }).ToArray();
        IReadOnlyList<IReadOnlyList<DangoPrescribedCoreSample>> samples = Enumerable.Range(0, trace.BodyCount).Select(b =>
            (IReadOnlyList<DangoPrescribedCoreSample>)trace.Samples.Skip(b * 97).Take(trace.SampleCount)
                .Select(s => new DangoPrescribedCoreSample(s.Fraction, s.ActualX, s.ActualY, s.TargetX, s.TargetY)).ToArray()).ToArray();
        LastErrorCode = trace.ErrorCode;
        if (trace.ErrorCode == 0)
        {
            prescribedOrdinal = input.StepOrdinal;
            prescribedEndTime = input.EndSampleTime;
            prescribedEndRevision = input.EndSceneRevision;
        }
        return new(new(bodies, contacts, trace.ErrorCode), new(trace.Epoch, trace.Ordinal, trace.ErrorCode, trace.SubstepCount,
            trace.EntityIds.Take(trace.BodyCount).Select(id => new DangoEntityId(id)).ToArray(),
            trace.MaximumErrors.Take(trace.BodyCount).ToArray(), samples));
    }

    private static bool Finite(params float[] values) => values.All(float.IsFinite);

    private static void ValidatePrescribedPairs(IReadOnlyList<DangoPrescribedPresentationBody> bodies,
        IReadOnlyList<DangoPrescribedPresentationPair> pairs, bool end)
    {
        static void Invalid() => throw Map(-410, "规定中心接触违反有限值、身份、方向或支撑合同。");
        DangoEntityId? Support(DangoPrescribedPresentationBody body) => end ? body.EndSupportEntityId : body.StartSupportEntityId;
        if (pairs.Count == 0)
        {
            if (bodies.Any(body => Support(body) is not null)) Invalid();
            return;
        }
        var pair = pairs[0];
        var a = bodies.SingleOrDefault(body => body.EntityId == pair.EntityA);
        var b = bodies.SingleOrDefault(body => body.EntityId == pair.EntityB);
        if (a is null || b is null || a.EntityId == b.EntityId || !pair.Touching || pair.SupportsA && pair.SupportsB ||
            !Finite(pair.NormalX, pair.NormalY, pair.Gap, pair.MinimumProjectedWidth) ||
            Math.Abs(pair.NormalX * pair.NormalX + pair.NormalY * pair.NormalY - 1) > .0002 ||
            pair.Gap > .008f || pair.MinimumProjectedWidth <= 0) Invalid();
        if (a is null || b is null) return;
        var pa = end ? a.End : a.Start; var pb = end ? b.End : b.Start;
        if ((pb.CoreX - pa.CoreX) * pair.NormalX + (pb.CoreY - pa.CoreY) * pair.NormalY <= 0 ||
            pair.SupportsA != (Support(a) == b.EntityId) || pair.SupportsB != (Support(b) == a.EntityId) ||
            pair.SupportsA && (-pair.NormalY < .7f || pa.CoreY <= pb.CoreY + .4f) ||
            pair.SupportsB && (pair.NormalY < .7f || pb.CoreY <= pa.CoreY + .4f)) Invalid();
    }

    private void ValidatePrescribedOutput(DangoPrescribedPresentationStep input, int[] ids, float[] states,
        int count, int contacts, NativePrescribedTrace trace)
    {
        static void Invalid() => throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch,
            "规定中心输出违反固定 ABI、有限值或整组身份合同。");
        if (count < 0 || count > capacity || count != entities.Count || contacts < 0 ||
            trace.AbiVersion != 1 || trace.FrameSize != PrescribedPresentationTraceSize || trace.Epoch != prescribedEpoch ||
            trace.Ordinal != input.StepOrdinal || trace.BodyCount != count || trace.SubstepCount is < 0 or > 96 ||
            trace.SampleCount is < 1 or > 97 || trace.SampleCount != trace.SubstepCount + 1 ||
            trace.ErrorCode == 0 && trace.SubstepCount == 0) Invalid();
        var stateIdentities = ids.Take(count).Select(id => new DangoEntityId(id)).ToHashSet();
        if (stateIdentities.Count != count || stateIdentities.Any(id => !entities.ContainsKey(id)) ||
            trace.EntityIds.Take(count).Distinct().Count() != count) Invalid();
        for (var body = 0; body < count; body++)
        {
            if (trace.EntityIds[body] != input.Bodies[body].EntityId.Value || !float.IsFinite(trace.MaximumErrors[body]) ||
                trace.MaximumErrors[body] < 0) Invalid();
            for (var value = 0; value < 42; value++) if (!float.IsFinite(states[body * 42 + value])) Invalid();
            var offset = body * 42;
            if (states[offset + 41] != 16 || states[offset + 39] is not (0 or 1) || states[offset + 38] < 0 ||
                states[offset + 38] != MathF.Truncate(states[offset + 38]) || states[offset + 40] < -1 ||
                states[offset + 40] > 15 || states[offset + 40] != MathF.Truncate(states[offset + 40])) Invalid();
            var previous = -1f;
            for (var sample = 0; sample < trace.SampleCount; sample++)
            {
                var value = trace.Samples[body * 97 + sample];
                if (!Finite(value.Fraction, value.ActualX, value.ActualY, value.TargetX, value.TargetY) ||
                    value.Fraction is < 0 or > 1 || value.Fraction <= previous || sample == 0 && value.Fraction != 0) Invalid();
                previous = value.Fraction;
            }
            if (trace.ErrorCode == 0 && previous != 1) Invalid();
        }
        for (var body = 0; body < 2; body++)
        {
            if (body >= count && (trace.EntityIds[body] != 0 || trace.MaximumErrors[body] != 0)) Invalid();
            var used = body < count ? trace.SampleCount : 0;
            for (var sample = used; sample < 97; sample++)
            {
                var value = trace.Samples[body * 97 + sample];
                if (value.Fraction != 0 || value.ActualX != 0 || value.ActualY != 0 || value.TargetX != 0 || value.TargetY != 0) Invalid();
            }
        }
    }

    private void VerifyPrescribedAbi()
    {
        if (api.ReadPrescribedAbi?.Invoke() != 1 || api.ReadPrescribedStepSize?.Invoke() != PrescribedPresentationInputSize ||
            api.ReadPrescribedTraceSize?.Invoke() != PrescribedPresentationTraceSize ||
            Marshal.SizeOf<NativePrescribedStep>() != PrescribedPresentationInputSize ||
            Marshal.SizeOf<NativePrescribedTrace>() != PrescribedPresentationTraceSize)
            throw new DangoPhysicsException(DangoPhysicsFailureKind.AbiMismatch, "模块不支持匹配的规定中心可选 ABI。");
    }

    private sealed partial class NativeApi
    {
        internal GetIntegerDelegate? ReadPrescribedAbi, ReadPrescribedStepSize, ReadPrescribedTraceSize;
        internal BindPrescribedDelegate? BindPrescribedEpoch;
        internal StepPrescribedDelegate? StepPrescribed;
        private void LoadPrescribedExports()
        {
            ReadPrescribedAbi = GetOptional<GetIntegerDelegate>(library, "dango_physics_get_prescribed_abi_version");
            ReadPrescribedStepSize = GetOptional<GetIntegerDelegate>(library, "dango_physics_get_prescribed_step_size");
            ReadPrescribedTraceSize = GetOptional<GetIntegerDelegate>(library, "dango_physics_get_prescribed_trace_size");
            BindPrescribedEpoch = GetOptional<BindPrescribedDelegate>(library, "dango_physics_world_bind_prescribed_epoch");
            StepPrescribed = GetOptional<StepPrescribedDelegate>(library, "dango_physics_world_step_prescribed");
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int BindPrescribedDelegate(nint handle, ulong epoch);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int StepPrescribedDelegate(nint handle, ref NativePrescribedStep input, int inputSize,
            [Out] int[] ids, [Out] float[] states, int capacity, out int count, out int contacts,
            ref NativePrescribedTrace trace, int traceSize);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePrescribedBody
    {
        public int EntityId;
        public uint StartFloor, EndFloor;
        public int StartSupport, EndSupport, Reserved;
        public float StartX, StartY, StartVx, StartVy, EndX, EndY, EndVx, EndVy;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePrescribedStep
    {
        public uint AbiVersion, FrameSize;
        public ulong Epoch, Ordinal;
        public double StartTime, EndTime;
        public ulong StartRevision, EndRevision;
        public int BodyCount, StartPairCount, EndPairCount, Reserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public NativePrescribedBody[] Bodies;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public NativeAuthorityPair[] StartPairs;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)] public NativeAuthorityPair[] EndPairs;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePrescribedSample
    {
        public float Fraction, ActualX, ActualY, TargetX, TargetY;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePrescribedTrace
    {
        public uint AbiVersion, FrameSize;
        public ulong Epoch, Ordinal;
        public int ErrorCode, BodyCount, SubstepCount, SampleCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public int[] EntityIds;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)] public float[] MaximumErrors;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 194)] public NativePrescribedSample[] Samples;
    }
}
