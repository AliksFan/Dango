using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation;

/// <summary>Synchronous room boundary; the host scheduler remains the only step owner.</summary>
internal sealed class BeltRoomSceneAdapter : IDisposable
{
    private const double Scale = 72;
    private readonly string module;
    private readonly DangoSelfPhysicsParameters configuration;
    private DangoBeltRoomWorld world;
    private LogicalPoint origin;
    private double left = -256, right = 256;
    public DangoBeltRoomFrame Frame { get; private set; }
    public DesktopPhysicsSceneFrame LastFrame { get; private set; }
    public int LastErrorCode { get; private set; }
    public LogicalPoint CoreHost => ToHost(new(Frame.Stats[0], Frame.Stats[1]));
    public double GroundY => origin.Y - configuration.GroundHeight * Scale;

    public BeltRoomSceneAdapter(string module, LogicalRect bounds, DangoBodyControls controls)
    {
        this.module = module;
        configuration = DangoBodyMaterial.Configure(controls);
        origin = new(bounds.Center.X, bounds.Bottom + configuration.GroundHeight * Scale);
        world = Create();
        Frame = world.Read();
        LastFrame = Present(Frame);
    }
    private DangoBeltRoomWorld Create()
    {
        var candidate = new DangoBeltRoomWorld(module, configuration, x: (left + right) / 2, left: left, right: right);
        try
        {
            if (!candidate.SupportsHorizontalBounds || !candidate.SupportsSolidWalls)
                throw new NotSupportedException("Desktop belt rooms require horizontal bounds and solid wall capabilities.");
            candidate.SetSolidWalls(true, candidate.Read().SceneRevision);
            return candidate;
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
    }
    public LogicalPoint ToWorld(LogicalPoint point) => new((point.X-origin.X)/Scale, (origin.Y-point.Y)/Scale);
    public LogicalPoint ToHost(LogicalPoint point) => new(origin.X+point.X*Scale, origin.Y-point.Y*Scale);
    public void SetBounds(double hostLeft, double hostRight)
    {
        var a = ToWorld(new(hostLeft, origin.Y)).X;
        var b = ToWorld(new(hostRight, origin.Y)).X;
        world.SetHorizontalBounds(a, b, Frame.SceneRevision);
        left = a; right = b; Frame = world.Read();
    }
    public void ClearBounds()
    {
        world.SetHorizontalBounds(-256, 256, Frame.SceneRevision);
        left = -256; right = 256; Frame = world.Read();
    }
    public bool TryPlacement(LogicalPoint requested, double hostLeft, double hostRight, out LogicalPoint safe)
    {
        var minimum = hostLeft - (Frame.Stats[8]-Frame.Stats[0])*Scale;
        var maximum = hostRight - (Frame.Stats[10]-Frame.Stats[0])*Scale;
        safe = minimum <= maximum ? new(Math.Clamp(requested.X, minimum, maximum), requested.Y) : default;
        return minimum <= maximum;
    }
    public void Rehost(LogicalPoint reference)
    {
        origin = new(reference.X, reference.Y + 60 + configuration.GroundHeight*Scale);
        LastFrame = Present(Frame);
    }
    public bool RehostSelected(LogicalPoint actual)
    {
        if (Frame.Platform is not null) return false;
        origin = new(actual.X-Frame.Stats[0]*Scale, actual.Y+60+configuration.GroundHeight*Scale);
        LastFrame = Present(Frame);
        return true;
    }
    public DesktopPhysicsSceneFrame Step(DangoBeltInput input)
    {
        if (LastErrorCode != 0) return LastFrame;
        try { world.Step(1, input); }
        catch (DangoBeltRoomException error)
        {
            LastErrorCode = error.ErrorCode;
            Frame = world.Read();
            return LastFrame;
        }
        Frame = world.Read();
        if (Frame.Fault != 0) { LastErrorCode = Frame.Fault; return LastFrame; }
        return LastFrame = Present(Frame);
    }
    private DesktopPhysicsSceneFrame Present(DangoBeltRoomFrame frame)
    {
        var stats = frame.Stats;
        var core = ToHost(new(stats[0],stats[1]));
        var contour = Enumerable.Range(0,256).Select(i => PresentationPoint.FromSampledBoundary(
            frame.Curve[i*2]-stats[0], frame.Curve[i*2+1]-stats[1])).ToArray();
        var relative = contour.SelectMany(p => new[] {(float)p.X,(float)p.Y}).ToArray();
        var state = new DangoBodyState(new(1), Array.AsReadOnly(relative), stats[0],stats[1],stats[7],
            stats[4]-stats[0],stats[5]-stats[1],stats[13],stats[14]!=0 && stats[9] <= configuration.GroundHeight + .003 ? (int)stats[25] : 0,stats[14]!=0,-1,256);
        var eyes = new PresentationAttachment(new(0,-.05),0);
        var top = contour.MaxBy(p=>p.Y);
        var presentation = new PhysicsPresentationFrame(true,contour,eyes,
            new(PresentationPoint.FromSampledBoundary(top.X,top.Y),0),PhysicsPresentationFallbackReason.None,
            new(PhysicsPresentationBackend.ContinuousBelt,(int)Math.Min(frame.Tick,int.MaxValue),PhysicsPresentationDiagnosticCode.ContinuousBeltActive));
        return new(new(Array.AsReadOnly(new[] {state}),0,0),
            Array.AsReadOnly(new[] { new DesktopPhysicsBody(new(1),new(core.X-80,core.Y-66,160,120),presentation) }));
    }
    public void Reset()
    {
        // Create first: a failed allocation must preserve the accepted owner/frame.
        var replacement = Create();
        world.Dispose(); world = replacement;
        Frame = world.Read(); LastErrorCode = 0; LastFrame = Present(Frame);
    }
    public void Dispose() => world.Dispose();
}
