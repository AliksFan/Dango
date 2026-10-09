using System.IO;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.JointReview;

internal enum RoundBallScenario { WithoutBall, PushFromLeft, PushFromRight, BallHitsDango, BallFalls }

/// <summary>One native world; presets define birth states and never animate either entity.</summary>
internal sealed class RoundBallSession : IDisposable
{
    internal static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    internal const double LeftBound = -4;
    internal const double RightBound = 4;
    private readonly DangoPhysicsWorld world;
    private bool disposed;

    internal static int OwnedWorldCount { get; private set; }

    internal RoundBallSession(string libraryPath, RoundBallScenario scenario)
    {
        world = DangoPhysicsWorld.Create(libraryPath, capacity: 1);
        try
        {
            world.SetHorizontalBounds(LeftBound, RightBound);
            var anchorX = scenario switch
            {
                RoundBallScenario.PushFromLeft or RoundBallScenario.BallFalls => -1.2,
                RoundBallScenario.PushFromRight => 1.2,
                _ => 0,
            };
            Entity = world.Add(anchorX, 0);
            var floorCenter = DangoPhysicsDefaults.Current.GroundHeight + .4;
            if (scenario != RoundBallScenario.WithoutBall)
            {
                var center = scenario switch
                {
                    RoundBallScenario.PushFromRight => (X: -1d, Y: floorCenter),
                    RoundBallScenario.BallHitsDango => (X: .55, Y: 2.4),
                    RoundBallScenario.BallFalls => (X: 1.8, Y: 2.4),
                    _ => (X: 1d, Y: floorCenter),
                };
                world.AddRoundBall(new(center.X, center.Y));
            }
            LastFrame = world.ReadRoundBallFrame();
            ValidateOwnership(LastFrame);
            OwnedWorldCount++;
        }
        catch { world.Dispose(); throw; }
    }

    internal DangoEntityId Entity { get; }
    internal DangoRoundBallFrame LastFrame { get; private set; }
    internal int StepCalls { get; private set; }
    internal int SuccessfulSteps { get; private set; }

    internal DangoRoundBallFrame Step(double horizontal, bool squash, bool jump, bool drag, double targetX, double targetY)
    {
        StepCalls++;
        var frame = world.StepWithRoundBall(FixedStep, [new(Entity, horizontal, squash ? 1 : 0, jump, drag, targetX, targetY)]);
        ValidateOwnership(frame);
        LastFrame = frame;
        if (frame.Physics.Succeeded) SuccessfulSteps++;
        return frame;
    }

    internal DangoRoundBallFrame RemoveBall()
    {
        if (LastFrame.Ball is { } ball) world.RemoveRoundBall(ball.Id);
        return Refresh();
    }

    internal DangoRoundBallFrame Reset()
    {
        world.Reset();
        return Refresh();
    }

    private DangoRoundBallFrame Refresh()
    {
        var frame = world.ReadRoundBallFrame();
        ValidateOwnership(frame);
        LastFrame = frame;
        return frame;
    }

    private void ValidateOwnership(DangoRoundBallFrame frame)
    {
        if (frame.Physics.Bodies.Count != 1 || frame.Physics.Bodies[0].EntityId != Entity)
            throw new InvalidDataException("圆球验收场景必须只有原来的单只团子。" +
                "场景读取不能改变实体所有权。");
    }

    public void Dispose()
    {
        if (disposed) return;
        world.Dispose();
        disposed = true;
        OwnedWorldCount--;
    }
}
