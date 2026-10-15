using Dango.Physics;
using Dango.PhysicsLab;
using Dango.PhysicsLab.Checks;

if (args.Length == 3 && args[0] == "--arc-contracts")
{
    try { LabArcExperiments.RunContracts(args[1], args[2]); }
    catch(Exception error) { Console.Error.WriteLine(error); Environment.ExitCode=1; }
    return;
}

if (args.Length == 5 && args[0] == "--arc-experiments")
{
    try { LabArcExperiments.Run(args[1], args[2], args[3], args[4]); }
    catch(Exception error) { Console.Error.WriteLine(error); Environment.ExitCode=1; }
    return;
}

if (args.Length == 4 && args[0] == "--region-experiments")
{
    LabRegionExperiments.Run(args[1], args[2], args[3]);
    return;
}

if (args.Length == 2 && args[0] == "--ground-contracts")
{
    LabGroundExperiments.RunBaselineContracts(args[1]);
    return;
}

if (args.Length == 4 && args[0] == "--ground-experiments")
{
    LabGroundExperiments.Run(args[1], args[2], args[3]);
    return;
}

if (args.Length == 3 && args[0] == "--experiments")
{
    LabExperiments.Run(args[1], args[2]);
    return;
}

var checks = 0;
checks += LabDisplaySmoothingChecks.Run();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    checks++;
}

var motionGate = new LabDisplayMotionGate();
Check(motionGate.ShouldBypass(false,false,false,0,1d/60,.45), "near-ground proximity without actual floor contact bypasses smoothing");
Check(motionGate.ShouldBypass(true,true,false,2,1d/60,.45), "Space begins a persistent display bypass");
for (var i=0;i<40;i++) motionGate.ShouldBypass(false,false,false,1,1d/60,.45);
Check(motionGate.ShouldBypass(true,false,false,.5,1d/60,.45), "landing motion still bypasses after cooldown");
motionGate.ShouldBypass(true,false,false,0,1d/60,.45);
motionGate.ShouldBypass(true,false,false,0,1d/60,.45);
Check(!motionGate.ShouldBypass(true,false,false,0,1d/60,.45), "three settled contact frames restore smoothing after a jump");
Check(motionGate.ShouldBypass(true,false,true,0,1d/60,.45), "drag bypasses display filtering");
motionGate.Reset();
Check(!motionGate.ShouldBypass(true,false,false,0,1d/60,.45), "motion reset clears jump history");
var rotatingFilter=new LabDisplaySmoothing(); var rotatingSafe=true;
for (var tick=0;tick<720;tick++)
{
    var points=Enumerable.Range(0,16).SelectMany(i=> {
        var angle=i*2*Math.PI/16+tick*(2*Math.PI*.3/60);
        return new[] {(float)Math.Cos(angle),(float)(Math.Sin(angle)*(.6+.01*Math.Sin(tick*2*Math.PI*4/60)))};
    }).ToArray();
    var minimum=LabGroundExperiments.CurveExtent(points,1).Minimum;
    var rotating=new DangoBodyState(new(777),points,0,(float)-minimum,1,0,0,0,1,true,-1,16);
    var shown=rotatingFilter.Update(rotating,1d/60,true);
    var geometry=LabDisplayGeometry.Inspect(shown.Contour);
    rotatingSafe &= geometry.Area>0 && !geometry.SelfIntersects;
}
Check(rotatingSafe,"rotating deforming ellipse stays a positive simple sampled display contour");

var curve = LabGroundExperiments.CurveExtent(new float[] { -1,0, 0,1, 1,0, 0,-1 },1);
Check(Math.Abs(curve.Maximum-.75)<1e-12 && Math.Abs(curve.Minimum+.75)<1e-12,
    "exact curve height solves interior quadratic extrema instead of raw controls");
var highSpectrum=LabGroundExperiments.Analyze(Enumerable.Range(0,480).Select(i=>3+Math.Sin(2*Math.PI*72*i/480)).ToArray());
Check(Math.Abs(highSpectrum.FullBandRms-Math.Sqrt(.5))<1e-10 && highSpectrum.Band05To5Rms<1e-10 &&
    Math.Abs(highSpectrum.DominantHz-9)<1e-3,
    "full spectrum retains 9Hz oscillation that narrow 0.5-5Hz band excludes");
var nyquistSpectrum=LabGroundExperiments.Analyze(Enumerable.Range(0,480).Select(i=>i%2==0?1d:-1d).ToArray());
Check(Math.Abs(nyquistSpectrum.FullBandRms-1)<1e-10,
    "Nyquist energy is retained once rather than doubled or silently discarded");

var input = new LabInput();
input.KeyDown(LabKey.Left);
input.KeyDown(LabKey.Right);
var snapshot = input.Consume();
Check(snapshot.Horizontal == 0 && snapshot.InputActive, "opposite movement is neutral but owns input");
input.KeyUp(LabKey.Right);
Check(input.Consume().Horizontal == -1, "releasing one movement key preserves the other");
input.KeyDown(LabKey.Stretch);
Check(input.Consume().Stretch == 1, "W produces stretch before the opposite key is pressed");
input.KeyDown(LabKey.Squash);
snapshot = input.Consume();
Check(snapshot.Stretch == 0 && snapshot.Downward == 0 && snapshot.InputActive && snapshot.CancelForces,
    "opposite posture is neutral and cancels previous ramp while retaining activity");
input.KeyUp(LabKey.Squash);
snapshot = input.Consume();
Check(snapshot.Stretch == 1 && snapshot.InputActive && !snapshot.CancelForces,
    "W remains held and resumes ramp after S release");
input.KeyDown(LabKey.Jump);
input.KeyDown(LabKey.Interact);
snapshot = input.Consume();
Check(snapshot.JumpPressed && snapshot.InteractPressed, "Space and F have independent edges");
input.KeyDown(LabKey.Jump, isRepeat: true);
input.KeyDown(LabKey.Interact, isRepeat: true);
snapshot = input.Consume();
Check(!snapshot.JumpPressed && !snapshot.InteractPressed, "repeat does not create new edges");
input.Cancel();
snapshot = input.Consume();
Check(!snapshot.InputActive && snapshot.CancelForces && !snapshot.JumpPressed && !snapshot.InteractPressed,
    "cancel immediately clears all actions and requests native force cancellation");
Check(!input.Consume().CancelForces, "cancel is consumed once");
input.KeyDown(LabKey.Left);
input.KeyDown(LabKey.Jump);
Check(!input.Consume().InputActive, "held keys are suppressed across cancellation");
input.KeyUp(LabKey.Left);
input.KeyDown(LabKey.Left);
Check(input.Consume().Horizontal == -1, "key-up then new press re-enables key");
input.KeyDown(LabKey.Right, modifiers: LabModifiers.Control);
Check(!input.Consume().InputActive, "modifier cancels rather than leaking movement");
input.KeyUp(LabKey.Left);
input.KeyUp(LabKey.Right);
input.Cancel();
input.ReconcileHeld([LabKey.Jump]);
input.KeyDown(LabKey.Jump);
Check(!input.Consume().JumpPressed, "activation observes a held Space without synthesizing jump");
input.ReconcileHeld([]);
input.KeyDown(LabKey.Jump);
Check(input.Consume().JumpPressed, "release outside app reconciles before fresh press");
input.KeyUp(LabKey.Jump);
input.KeyDown(LabKey.Stretch);
input.Consume();
input.KeyUp(LabKey.Stretch);
snapshot = input.Consume();
Check(!snapshot.InputActive && !snapshot.CancelForces && snapshot.Stretch == 0,
    "ordinary W release remains distinct from lifecycle cancel");
input.KeyDown(LabKey.Interact, blocked: true);
input.KeyUp(LabKey.Interact);
input.Consume();
input.KeyDown(LabKey.Interact);
Check(input.Consume().InteractPressed, "blocked key-up still releases suppression");

var nodes = new[] { new DangoLabNode(0, 0, 2, 0, 0, 3, 0), new DangoLabNode(1, 4, 6, 0, 0, 1, 0) };
var body = new DangoBodyState(new(1), new float[] { -1, 1, 1, 5 }, 0, 2, 1, 2, 3, 8, 0, false, -1, 2);
var stats = new DangoLabStats(DangoLabDrive.Mass, 0, .5f, 1, 1, 0, 0, 3, 3, 0, 0, 0, 0, 1, false, 0);
var metrics = new LabMetrics(2);
var sample = metrics.Record(body, nodes, stats);
Check(sample.CenterOfMassX == 1 && sample.CenterOfMassY == 3 && sample.ContourHeight == 4,
    "metrics retain actual mass-weighted positions and legacy control-point height");
var curvedSample = metrics.Record(body with { Contour = new float[] { -1,0, 0,1, 1,0, 0,-1 } }, nodes, stats);
Check(Math.Abs(curvedSample.ExactCurveHeight - 1.5) < 1e-12 && curvedSample.ContourHeight == 2,
    "window metrics distinguish exact drawn curve height from legacy control-point span");
metrics.Clear(); metrics.Record(body, nodes, stats);
metrics.Record(body, nodes, stats);
sample = metrics.Record(body, nodes, stats);
Check(sample.ConsecutiveNoContactFrames == 3 && metrics.Samples.Count == 2 && metrics.Samples[0].Tick == 2,
    "bounded sampling retains latest ticks and consecutive contact gaps");
sample = metrics.Record(body with { GroundContactCount = 1 }, nodes, stats);
Check(sample.ConsecutiveNoContactFrames == 0, "real contact resets airborne counter");
metrics.Clear();
Check(metrics.TotalSamples == 0 && metrics.Samples.Count == 0, "new scene clears metric history");

if (args.Length > 0)
{
    using var world = DangoPhysicsWorld.Create(args[0], capacity: 1);
    var id = world.Add(0, 0);
    world.RequireLab();
    Check(world.LabGroundModel == DangoLabGroundModel.DiscreteNodes && world.ReadLabGroundStats(id) is null,
        "old lab DLL lacks optional ground exports and explicitly remains discrete");
    Check(world.ReadLabRegionStats(id) is null, "old lab module has no directional region diagnostics");
    Check(world.ReadLabArcStats(id) is null && world.LabRegionDenominator == 3,
        "old lab module has no arc capability and is not mislabelled as half-region");
    var regionRejected = false;
    try { world.StepLab(1d/60,[new(id)],DangoLabDrive.DirectionRegion); }
    catch (DangoPhysicsException error) when (error.Kind == DangoPhysicsFailureKind.AbiMismatch) { regionRejected=true; }
    Check(regionRejected, "old lab module explicitly rejects new directional modes");
    var frame = world.StepLab(1d / 60, [new(id)] , DangoLabDrive.Core);
    Check(frame.Succeeded && frame.Bodies.Count == 1, "real DLL lab step succeeds");
    var actualNodes = world.ReadLabNodes(id);
    Check(actualNodes.Count == 17 && actualNodes.All(n => n.Mass > 0), "real DLL exposes 17 actual positive masses");
    world.StepLab(1d / 60, [new(id, Stretch: 1, InputActive: true)], DangoLabDrive.Mass);
    Check(world.ReadLabStats(id).StretchRamp > 0, "real W starts native ramp");
    world.StepLab(1d / 60, [new(id)], DangoLabDrive.Mass);
    Check(world.ReadLabStats(id).StretchRamp > 0, "ordinary release keeps native decay");
    world.StepLab(1d / 60, [new(id, CancelForces: true)], DangoLabDrive.Mass);
    Check(world.ReadLabStats(id).StretchRamp == 0, "lifecycle cancel clears native ramp immediately");
    var rejected = false;
    try { world.StepLab(1d / 60, [new(id, Stretch: double.NaN)], DangoLabDrive.Core); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "managed boundary rejects non-finite stretch");
    rejected = false;
    try { world.StepLab(1d / 60, [], DangoLabDrive.Core); }
    catch (DangoPhysicsException error) when (error.Kind == DangoPhysicsFailureKind.InvalidEntity) { rejected = true; }
    Check(rejected, "lab inputs cannot silently omit a body");
    rejected = false;
    try { world.StepLab(1d / 60, [new(id)], (DangoLabDrive)42); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected, "unknown drive mode is rejected before native call");
    var threadError = Task.Run(() => {
        try { world.RequireLab(); return false; }
        catch (InvalidOperationException) { return true; }
    }).GetAwaiter().GetResult();
    Check(threadError, "lab methods retain creating-thread ownership");
}
if (args.Length > 1)
{
    using var legacy = DangoPhysicsWorld.Create(args[1], capacity: 1);
    var id = legacy.Add(0, 0);
    Check(legacy.LabGroundModel == DangoLabGroundModel.DiscreteNodes,
        "ordinary legacy world reads absent optional ground model without laboratory opt-in");
    Check(legacy.Step(TimeSpan.FromSeconds(1d / 60), [new(id)]).Succeeded,
        "legacy production Step does not require laboratory exports");
    var rejected = false;
    try { legacy.RequireLab(); }
    catch (DangoPhysicsException error) when (error.Kind == DangoPhysicsFailureKind.MissingExport) { rejected = true; }
    Check(rejected, "laboratory capability fails explicitly on legacy DLL");
    Check(legacy.Step(TimeSpan.FromSeconds(1d / 60), [new(id)]).Succeeded,
        "failed lab opt-in leaves ordinary legacy world usable");
}
Console.WriteLine($"{checks} checks passed.");
