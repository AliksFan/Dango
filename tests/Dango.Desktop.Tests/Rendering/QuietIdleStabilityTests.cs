using Dango.Core.Animation;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class QuietIdleStabilityTests
{
    [TestMethod]
    public void RealRestingBodyReachesApprovedStableGateAndJumpLeavesIt()
    {
        using var runtime = LightFeedbackRuntimeTests.Create();
        var previous = runtime.LastPhysicsFrame!.Bodies.Single();
        var stable = 0d;
        var reached = false;
        for (var i = 0; i < 1500; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1);
            Assert.IsNull(runtime.LastError);
            var body = runtime.LastPhysicsFrame!.Bodies.Single();
            var vx = (body.CoreX - previous.CoreX) / .02;
            var vy = (body.CoreY - previous.CoreY) / .02;
            stable = body.IsGrounded && Math.Sqrt(vx * vx + vy * vy) <= .15 ? stable + .02 : 0;
            previous = body;
            if (stable >= .5) { reached = true; break; }
        }
        Assert.IsTrue(reached, "Real physics must reach the approved 500 ms stable gate without changing parameters.");
        runtime.SetDirectionalControl(new(false, false, true, false));
        var airborne = false;
        for (var i = 0; i < 20; i++)
        {
            LightFeedbackRuntimeTests.Steps(runtime, 1);
            airborne |= !runtime.LastPhysicsFrame!.Bodies.Single().IsGrounded;
        }
        Assert.IsTrue(airborne);
        runtime.SetDirectionalControl(DirectionalControlIntent.None);
    }
}
