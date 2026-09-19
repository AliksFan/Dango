using System.Text.Json;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class ShortWanderPhysicsGateTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(10, -1, 1280, .1)]
    [DataRow(10, -1, 1280, .2)]
    [DataRow(10, 1, 1280, .1)]
    [DataRow(10, 1, 1280, .2)]
    [DataRow(20, -1, 1280, .1)]
    [DataRow(20, -1, 1280, .2)]
    [DataRow(20, 1, 1280, .1)]
    [DataRow(20, 1, 1280, .2)]
    [DataRow(40, -1, 1280, .1)]
    [DataRow(40, -1, 1280, .2)]
    [DataRow(40, 1, 1280, .1)]
    [DataRow(40, 1, 1280, .2)]
    [DataRow(10, -1, 3840, .1)]
    [DataRow(10, -1, 3840, .2)]
    [DataRow(10, 1, 3840, .1)]
    [DataRow(10, 1, 3840, .2)]
    [DataRow(20, -1, 3840, .1)]
    [DataRow(20, -1, 3840, .2)]
    [DataRow(20, 1, 3840, .1)]
    [DataRow(20, 1, 3840, .2)]
    [DataRow(40, -1, 3840, .1)]
    [DataRow(40, -1, 3840, .2)]
    [DataRow(40, 1, 3840, .1)]
    [DataRow(40, 1, 3840, .2)]
    public void ApprovedInputMeetsRealDistanceAndStoppingGate(int milliseconds, int direction, int screenWidth, double fraction)
    {
        using var world = DangoPhysicsWorld.Create(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName));
        var id = world.Add(0, 0);
        var dt = TimeSpan.FromMilliseconds(milliseconds);
        var seconds = dt.TotalSeconds;
        DangoBodyState Step(double input)
        {
            var frame = world.Step(dt, [new(id, Horizontal: input)]);
            Assert.IsTrue(frame.Succeeded);
            return frame.Bodies.Single();
        }
        var body = Step(0);
        var stable = 0d;
        double Speed(DangoBodyState a, DangoBodyState b) =>
            Math.Sqrt(Math.Pow((b.CoreX - a.CoreX) / seconds, 2) + Math.Pow((b.CoreY - a.CoreY) / seconds, 2));
        for (var i = 0; i < 30000 / milliseconds && stable < .5; i++)
        {
            var next = Step(0);
            stable = next.IsGrounded && Speed(body, next) <= .15 ? stable + seconds : 0;
            body = next;
        }
        Assert.IsTrue(stable >= .5, "Real resting physics must qualify before the movement probe.");
        var targetDip = Math.Clamp(screenWidth * fraction, screenWidth * .1 + 8, screenWidth * .2 - 8);
        var startX = body.CoreX;
        var driveSeconds = 0d;
        var maximumStepDip = 0d;
        var stalled = false;
        while (driveSeconds + 1e-9 < 2 + targetDip / 20 && direction * (body.CoreX - startX) * 80 < targetDip)
        {
            var next = Step(direction * .35 * Math.Min(1, (driveSeconds + seconds) / .2));
            maximumStepDip = Math.Max(maximumStepDip, Math.Abs(next.CoreX - body.CoreX) * 80);
            body = next;
            driveSeconds += seconds;
            if (!body.IsGrounded) break;
            if (driveSeconds + 1e-9 >= .5 && direction * (body.CoreX - startX) * 80 < 2)
            {
                stalled = true;
                break;
            }
        }
        var releaseX = body.CoreX;
        var maximumCoastDip = 0d;
        double? stableAt = null;
        stable = 0;
        for (var i = 1; i <= 3000 / milliseconds; i++)
        {
            var next = Step(0);
            maximumCoastDip = Math.Max(maximumCoastDip, Math.Abs(next.CoreX - releaseX) * 80);
            stable = next.IsGrounded && Speed(body, next) <= .15 ? stable + seconds : 0;
            if (stable + 1e-9 >= .5 && stableAt is null) stableAt = i * seconds;
            body = next;
        }
        var totalDip = direction * (body.CoreX - startX) * 80;
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            milliseconds,
            direction,
            targetDip,
            driveSeconds,
            stalled,
            drivenDip = direction * (releaseX - startX) * 80,
            totalDip,
            maximumCoastDip,
            maximumStepDip,
            stableAt,
        }));
        Assert.IsFalse(stalled, "Approved input did not produce 2 DIP by the 500 ms stall gate.");
        Assert.IsTrue(totalDip >= screenWidth * .1 && totalDip <= screenWidth * .2, "Final movement must cover at least one tenth of the current screen width.");
        Assert.IsTrue(stableAt is <= 1.0000001, "The body must regain continuous 500 ms stability within 1 second of release.");
        Assert.IsTrue(maximumCoastDip + maximumStepDip <= 32, "Coast plus maximum consumed step must fit the approved edge reserve.");
    }
}
