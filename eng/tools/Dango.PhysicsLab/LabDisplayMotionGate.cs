using Dango.Physics;

namespace Dango.PhysicsLab;

/// <summary>Presentation eligibility only; never changes native input or motion.</summary>
public sealed class LabDisplayMotionGate
{
    private bool jump;
    private double sinceJump;
    private int settledContactFrames;

    public bool ShouldBypass(bool actualContact, bool jumpRequested, bool dragging,
        double coreVerticalSpeed, double dt, double cooldownSeconds)
    {
        if (jumpRequested) { jump=true; sinceJump=0; settledContactFrames=0; }
        if (jump)
        {
            sinceJump+=dt;
            settledContactFrames=actualContact && sinceJump>=cooldownSeconds && Math.Abs(coreVerticalSpeed)<.05
                ? settledContactFrames+1 : 0;
            if (settledContactFrames>=3) jump=false;
        }
        return dragging || !actualContact || jump;
    }

    public void Reset() { jump=false; sinceJump=0; settledContactFrames=0; }

    public static bool HasFloorContact(IReadOnlyList<DangoLabNode> nodes, double ground, double nodeRadius,
        double radialLength, DangoLabGroundStats? envelope)
    {
        var tolerance=1e-5*radialLength;
        return envelope is not null ? envelope.Gap<=tolerance : nodes.Skip(1).Any(n=>n.Y-nodeRadius<=ground+tolerance);
    }
}
