using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class SingleDangoMotionRuntimeTests
{
    [TestMethod]
    public void StopHostLossAndDisposeReleaseTheOwnedPhysicsWorld()
    {
        var worlds = new List<TrackingWorld>();
        using var runtime = new SingleDangoMotionRuntime(
            () => { var world = new TrackingWorld(); worlds.Add(world); return world; },
            new TrackingSink());

        runtime.Start();
        runtime.Tick(Input(), TimeSpan.FromSeconds(1d / 60d));
        runtime.Stop();
        Assert.IsTrue(worlds[0].Disposed);
        Assert.IsFalse(runtime.IsRunning);

        runtime.Start();
        runtime.NotifyHostUnavailable();
        Assert.IsTrue(worlds[1].Disposed);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.Tick(Input(), TimeSpan.FromSeconds(1d / 60d)));
    }

    private static PhysicsPresentationInput Input() => new(
        "runtime-test", new LogicalRect(0, 0, 100, 100), DangoActionState.Idle, 0, 1,
        "ground", new LogicalVector(0, -1), 0, 1, false, true, []);

    private sealed class TrackingSink : IPhysicsPresentationSink
    {
        public void ApplyFrame(PhysicsPresentationFrame frame) { }
    }

    private sealed class TrackingWorld : IPhysicsPresentationWorld
    {
        public PhysicsPresentationWorldState State => Disposed ? PhysicsPresentationWorldState.Disposed : PhysicsPresentationWorldState.Active;
        public bool Disposed { get; private set; }
        public PhysicsPresentationFrame Step(PhysicsPresentationInput input, TimeSpan fixedStep) => new(
            true,
            [
                new PresentationPoint(-0.5, -0.5),
                new PresentationPoint(0.5, -0.5),
                new PresentationPoint(0, 0.5),
            ],
            new PresentationAttachment(new PresentationPoint(0, 0), 0),
            new PresentationAttachment(new PresentationPoint(0, 0.5), 0),
            PhysicsPresentationFallbackReason.None,
            new PhysicsPresentationDiagnostics(
                PhysicsPresentationBackend.DeterministicFake,
                1,
                PhysicsPresentationDiagnosticCode.FakeActive));
        public void Dispose() => Disposed = true;
    }
}
