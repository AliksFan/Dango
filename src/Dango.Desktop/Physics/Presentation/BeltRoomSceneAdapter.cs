using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Physics.Belt;
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
    private readonly BeltSleepPolicy sleep = new();
    private double? presentationScale;
    private double actionJumpSpeedScale = 1;
    public bool SleepEnabled { get; set; } = true;
    public bool IsSleeping { get; private set; }
    public long NativeStepCalls { get; private set; }
    // 0.0005 m at this supported ceiling is at most 0.15 DIP in any direction.
    internal const double MaximumSleepMetresToDip = 300;
    public DangoBeltRoomFrame Frame { get; private set; }
    private DangoBeltRoomFurnitureFrame? furnitureFrame;
    public bool SupportsFurnitureCollection => world.SupportsFurnitureCollection;
    public DesktopPhysicsSceneFrame LastFrame { get; private set; }
    public int LastErrorCode { get; private set; }
    internal bool SupportsMuscleActuation => world.SupportsMuscleActuation;
    internal DangoMuscleDiagnostics ReadMuscleDiagnostics() => world.ReadMuscleDiagnostics();
    public LogicalPoint CoreHost => ToHost(new(Frame.Stats[0], Frame.Stats[1]));
    public double GroundY => origin.Y - configuration.GroundHeight * Scale;

    public BeltRoomSceneAdapter(string module, LogicalRect bounds, DangoBodyControls controls)
    {
        this.module = module;
        configuration = DangoBodyMaterial.Configure(controls);
        origin = new(bounds.Center.X, bounds.Bottom + configuration.GroundHeight * Scale);
        world = Create();
        try
        {
            Frame = Refresh();
            LastFrame = Present(Frame);
        }
        catch { world.Dispose(); throw; }
    }
    private DangoBeltRoomWorld Create()
    {
        var candidate = new DangoBeltRoomWorld(module, configuration, x: (left + right) / 2, left: left, right: right);
        try
        {
            if (!candidate.SupportsHorizontalBounds || !candidate.SupportsSolidWalls)
                throw new NotSupportedException("Desktop belt rooms require horizontal bounds and solid wall capabilities.");
            candidate.SetSolidWalls(true, candidate.Read().SceneRevision);
            if (actionJumpSpeedScale != 1) candidate.SetPreviewJumpSpeedScale(actionJumpSpeedScale);
            if (candidate.SupportsFurnitureCollection)
                candidate.EnableFurnitureCollection(candidate.Read().SceneRevision);
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
    internal void SetActionJumpSpeedScale(double scale)
    {
        world.SetPreviewJumpSpeedScale(scale);
        actionJumpSpeedScale = scale;
        Wake();
    }
    public void ConfigureSleepPresentationScale(double? roomToDip)
    {
        if (roomToDip is { } value && (!double.IsFinite(value) || value <= 0))
            throw new ArgumentOutOfRangeException(nameof(roomToDip));
        if (presentationScale == roomToDip) return;
        presentationScale = roomToDip;
        Wake();
    }
    public void Wake() { IsSleeping = false; sleep.Reset(); }
    private DangoBeltRoomFrame Refresh()
    {
        if (!world.SupportsFurnitureCollection) return world.Read();
        furnitureFrame = world.ReadFurnitureCollectionFrame();
        return furnitureFrame.BeltFrame;
    }
    public DangoFurnitureCollectionFrame ReadFurnitureCollectionFrame()
    {
        if (!SupportsFurnitureCollection) throw new NotSupportedException("This belt room module has no furniture collection capability.");
        Frame = Refresh();
        var collection = furnitureFrame!;
        return new(collection.WorldGeneration, Frame.SceneRevision, Frame.Tick, collection.LastAppliedToken,
            Present(Frame).Physics with { ErrorCode = Frame.Fault }, Array.AsReadOnly(Array.Empty<DangoFurnitureBodyDiagnostics>()), collection.Items)
        { CoordinateSpace = DangoFurnitureCoordinateSpace.BeltWorldMeters };
    }
    public DangoFurnitureProbe ProbeFurnitureChange(DangoFurnitureChangeKind kind, ulong instance,
        DangoFurnitureDefinition? candidate, ulong revision) => world.ProbeFurnitureChange(kind, instance, candidate, revision);
    public DangoFurniturePreparation PrepareFurnitureChange(DangoFurnitureChangeKind kind, ulong instance,
        DangoFurnitureDefinition? candidate, ulong revision)
    {
        Wake();
        return world.PrepareFurnitureChange(kind, instance, candidate, revision);
    }
    public DangoFurnitureCollectionFrame ApplyPreparedFurnitureChange(DangoPreparedFurnitureChange token)
    {
        world.ApplyPreparedFurnitureChange(token);
        Wake();
        var frame = ReadFurnitureCollectionFrame();
        LastFrame = Present(Frame);
        return frame;
    }
    public void CancelPreparedFurnitureChange(DangoPreparedFurnitureChange token) { world.CancelPreparedFurnitureChange(token); Wake(); }
    public void SetBounds(double hostLeft, double hostRight)
    {
        var a = ToWorld(new(hostLeft, origin.Y)).X;
        var b = ToWorld(new(hostRight, origin.Y)).X;
        if (a != left || b != right) Wake();
        world.SetHorizontalBounds(a, b, Frame.SceneRevision);
        left = a; right = b; Frame = Refresh();
    }
    public void ClearBounds()
    {
        Wake();
        world.SetHorizontalBounds(-256, 256, Frame.SceneRevision);
        left = -256; right = 256; Frame = Refresh();
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
        Wake();
        origin = new(reference.X, reference.Y + 60 + configuration.GroundHeight*Scale);
        LastFrame = Present(Frame);
    }
    public bool RehostSelected(LogicalPoint actual)
    {
        if (Frame.Platform is not null || furnitureFrame?.Items.Count > 0) return false;
        Wake();
        origin = new(actual.X-Frame.Stats[0]*Scale, actual.Y+60+configuration.GroundHeight*Scale);
        LastFrame = Present(Frame);
        return true;
    }
    public DesktopPhysicsSceneFrame Step(DangoBeltInput input)
    {
        if (LastErrorCode != 0) return LastFrame;
        var eligible = SleepEnabled && presentationScale is { } scale && scale * Scale <= MaximumSleepMetresToDip &&
            furnitureFrame?.Items.Any(item => item.Definition.Kind == DangoFurnitureKind.RoundBall) != true;
        if (!eligible || BeltSleepPolicy.Active(input)) Wake();
        if (IsSleeping) return LastFrame;
        try { NativeStepCalls++; world.Step(1, input); }
        catch (DangoBeltRoomException error)
        {
            LastErrorCode = error.ErrorCode;
            Frame = Refresh();
            return LastFrame;
        }
        Frame = Refresh();
        if (Frame.Fault != 0) { LastErrorCode = Frame.Fault; return LastFrame; }
        if (eligible)
            IsSleeping = sleep.Observe(new(Frame.Scaffold.ToArray(), Frame.Curve.ToArray(), Frame.Stats.ToArray()),
                Frame.BodySnapshot.ToArray(), input, true);
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
        DangoBeltRoomFurnitureFrame? nextCollection;
        DangoBeltRoomFrame nextFrame;
        DesktopPhysicsSceneFrame nextPresentation;
        try
        {
            nextCollection = replacement.SupportsFurnitureCollection ? replacement.ReadFurnitureCollectionFrame() : null;
            nextFrame = nextCollection?.BeltFrame ?? replacement.Read();
            nextPresentation = Present(nextFrame);
        }
        catch { replacement.Dispose(); throw; }
        var previous = world;
        world = replacement;
        furnitureFrame = nextCollection;
        Frame = nextFrame;
        LastFrame = nextPresentation;
        Wake();
        LastErrorCode = 0;
        previous.Dispose();
    }
    public void Dispose() => world.Dispose();
}
