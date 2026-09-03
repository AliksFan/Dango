namespace Dango.Desktop.Physics.Presentation;

public sealed class DeterministicFakePhysicsWorld : PhysicsPresentationWorldBase
{
    private int _stepCount;

    protected override PhysicsPresentationFrame StepActive(PhysicsPresentationInput input, TimeSpan fixedStep)
    {
        _stepCount++;
        return AuthoredPoseSampler.CreateActive(
            input,
            PhysicsPresentationBackend.DeterministicFake,
            _stepCount,
            PhysicsPresentationDiagnosticCode.FakeActive);
    }
}

internal static class AuthoredPoseSampler
{
    private const int ContourPointCount = 12;

    public static PhysicsPresentationFrame CreateActive(
        PhysicsPresentationInput input,
        PhysicsPresentationBackend backend,
        int simulatedStepCount,
        PhysicsPresentationDiagnosticCode code) =>
        new(
            true,
            CreateContour(input),
            new PresentationAttachment(new PresentationPoint(0, -0.05), 0),
            new PresentationAttachment(new PresentationPoint(0, -0.82), 0),
            PhysicsPresentationFallbackReason.None,
            new PhysicsPresentationDiagnostics(backend, simulatedStepCount, code));

    public static PhysicsPresentationFrame CreateFallback(
        PhysicsPresentationInput input,
        PhysicsPresentationFallbackReason reason,
        int nativeErrorCode = 0) =>
        new(
            reason != PhysicsPresentationFallbackReason.Hidden,
            reason == PhysicsPresentationFallbackReason.Hidden ? [] : CreateContour(input),
            new PresentationAttachment(new PresentationPoint(0, -0.05), 0),
            new PresentationAttachment(new PresentationPoint(0, -0.82), 0),
            reason,
            new PhysicsPresentationDiagnostics(
                PhysicsPresentationBackend.AuthoredFallback,
                0,
                PhysicsPresentationDiagnosticCode.Fallback,
                NativeErrorCode: nativeErrorCode));

    public static PhysicsPresentationFrame CreateSoftBodyActive(
        IReadOnlyList<PresentationPoint> contour,
        int simulatedStepCount,
        SoftBodyMetrics metrics) =>
        new(
            true,
            contour,
            new PresentationAttachment(new PresentationPoint(0, -0.05), 0),
            new PresentationAttachment(new PresentationPoint(0, -0.82), 0),
            PhysicsPresentationFallbackReason.None,
            new PhysicsPresentationDiagnostics(
                PhysicsPresentationBackend.Box2D,
                simulatedStepCount,
                PhysicsPresentationDiagnosticCode.SoftBodyActive,
                metrics));

    private static IReadOnlyList<PresentationPoint> CreateContour(PhysicsPresentationInput input)
    {
        var points = new PresentationPoint[ContourPointCount];
        var landingSquash = input.Action == Core.Animation.DangoActionState.Landing
            ? 0.08 * input.Strength
            : 0;
        var seedOffset = (Math.Abs((long)input.DeterministicSeed) % 17) * 0.0005;
        for (var index = 0; index < points.Length; index++)
        {
            var angle = (Math.PI * 2 * index) / points.Length;
            var x = Math.Cos(angle) * (0.72 + landingSquash + seedOffset);
            var y = Math.Sin(angle) * (0.82 - landingSquash);
            points[index] = new PresentationPoint(x, y);
        }

        return points;
    }
}
