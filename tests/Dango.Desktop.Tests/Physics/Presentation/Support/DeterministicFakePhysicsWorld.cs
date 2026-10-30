using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Tests.Physics.Presentation.Support;

internal sealed class DeterministicFakePhysicsWorld : PhysicsPresentationWorldBase
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
