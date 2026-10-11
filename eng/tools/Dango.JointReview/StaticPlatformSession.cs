using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.JointReview;

internal enum StaticPlatformScenario { WithoutPlatform, Top, Left, Right, Underside }

/// <summary>One native world, one Dango, and one optional fixed official platform.</summary>
internal sealed class StaticPlatformSession : IDisposable
{
    internal static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    internal static readonly DangoStaticPlatformDefinition OfficialPlatform = new(0, 1.2, 3.4, .35);
    private readonly DangoPhysicsWorld world;
    private bool disposed;

    internal static int OwnedWorldCount { get; private set; }

    internal StaticPlatformSession(string libraryPath, StaticPlatformScenario scenario)
    {
        world = DangoPhysicsWorld.Create(libraryPath, capacity: 1);
        try
        {
            if (scenario != StaticPlatformScenario.WithoutPlatform) world.SetStaticPlatform(OfficialPlatform);
            var anchor = scenario switch
            {
                StaticPlatformScenario.Top => (X: 0d, Y: OfficialPlatform.Top + 1),
                StaticPlatformScenario.Left => (X: OfficialPlatform.Left - 1.25, Y: OfficialPlatform.CenterY - DangoPhysicsDefaults.Current.DropHeight),
                StaticPlatformScenario.Right => (X: OfficialPlatform.Right + 1.25, Y: OfficialPlatform.CenterY - DangoPhysicsDefaults.Current.DropHeight),
                StaticPlatformScenario.Underside => (X: 0d, Y: -.1),
                _ => (X: 0d, Y: 0d),
            };
            Entity = world.Add(anchor.X, anchor.Y);
            OwnedWorldCount++;
        }
        catch { world.Dispose(); throw; }
    }

    internal DangoEntityId Entity { get; }
    internal DangoStaticPlatformDefinition? Platform => world.StaticPlatform;
    internal int Error => world.LastErrorCode;
    internal int Steps { get; private set; }
    internal DangoPhysicsFrame? LastFrame { get; private set; }

    internal DangoBodyState Step(double horizontal, bool squash, bool jump, bool drag, double targetX, double targetY)
    {
        var frame = world.Step(FixedStep, [new(Entity, horizontal, squash ? 1 : 0, jump, drag, targetX, targetY)]);
        LastFrame = frame;
        if (frame.Succeeded) Steps++;
        return frame.Bodies.Single();
    }

    internal DangoStaticPlatformContact? ReadContact() => Platform is null ? null : world.ReadStaticPlatformContact(Entity);
    internal void RemovePlatform() => world.ClearStaticPlatform();
    public void Dispose()
    {
        if (disposed) return;
        world.Dispose();
        disposed = true;
        OwnedWorldCount--;
    }
}
