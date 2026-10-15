using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.PhysicsLab;

/// <summary>Independent default-material observations of drive and display changes.</summary>
public static class LabRegionExperiments
{
    private static readonly double Dt = TimeSpan.FromSeconds(1d / 60).TotalSeconds;
    private sealed record Phase(string Name, int Frames, double Horizontal = 0, double Up = 0,
        double Down = 0, bool Jump = false);
    private sealed record Sample(double PhysicalHeight, double DisplayHeight, double PhysicalCoreY,
        double DisplayCoreY, double ComY, double ComX, double BottomError, double WidthError, double PhysicalArea, double DisplayArea);

    public static void Run(string baselineModule, string regionModule, string output)
    {
        Directory.CreateDirectory(output);
        var runs = new List<object>();
        var failures = new List<string>();
        var config = DangoBodyMaterial.Configure(DangoBodyControls.Default);
        var phases = new[]
        {
            new Phase("rest",300), new Phase("startup-right",120,1), new Phase("steady-right",480,1),
            new Phase("release-right",180), new Phase("startup-left",120,-1), new Phase("steady-left",480,-1),
            new Phase("release-left",180), new Phase("W",90,Up:1), new Phase("W-release",120),
            new Phase("S",90,Down:1), new Phase("S-release",120),
            new Phase("W-before-jump",18,Up:1), new Phase("Space-with-W",1,Up:1,Jump:true),
            new Phase("W-after-jump",18,Up:1), new Phase("final-release",120),
        };
        foreach (var mode in new[] { DangoLabDrive.Core, DangoLabDrive.DirectionRegion, DangoLabDrive.DirectionPoint })
        {
            var module = mode == DangoLabDrive.Core ? baselineModule : regionModule;
            var summaries = new List<object>();
            string? failure = null;
            try
            {
                using var world = DangoPhysicsWorld.Create(module, 1, config);
                world.RequireLab(); world.RequireBodyMaterialModel(1);
                var id = world.Add(0,0); world.SetHorizontalBounds(-256,256);
                var smoother = new LabDisplaySmoothing();
                var motion = new LabDisplayMotionGate();
                foreach (var phase in phases)
                {
                    var samples = new List<Sample>();
                    var active = phase.Horizontal != 0 || phase.Up != 0 || phase.Down != 0 || phase.Jump;
                    for (var i=0;i<phase.Frames;i++)
                    {
                        var frame = world.StepLab(Dt, [new(id,Horizontal:phase.Horizontal,Downward:phase.Down,
                            Stretch:phase.Up,JumpPressed:phase.Jump && i==0,InputActive:active)],mode);
                        if (!frame.Succeeded) throw new InvalidOperationException($"{phase.Name}/{i}: native {frame.ErrorCode}");
                        var raw = frame.Bodies.Single();
                        var before = Bits(raw);
                        var nodes = world.ReadLabNodes(id);
                        if (nodes.Any(n=>!float.IsFinite(n.X+n.Y+n.Mass+n.VelocityX+n.VelocityY) || n.Mass<=0) ||
                            nodes.Min(n=>n.X)<-254 || nodes.Max(n=>n.X)>254 ||
                            nodes.Skip(1).Min(n=>n.Y-config.NodeRadius)<config.GroundHeight-2*config.NodeRadius)
                            throw new InvalidOperationException($"{phase.Name}/{i}: finite/floor/edge oracle");
                        var shown = smoother.Update(raw,Dt,true,motion.ShouldBypass(LabDisplayMotionGate.HasFloorContact(nodes,
                            config.GroundHeight,config.NodeRadius,config.RadialRestLength,null),phase.Jump && i==0,false,
                            nodes[0].VelocityY,Dt,config.JumpCooldownMilliseconds/1000));
                        if (!before.SequenceEqual(Bits(raw))) throw new InvalidOperationException("Display helper changed raw physics snapshot.");
                        if (mode>=DangoLabDrive.DirectionRegion && world.ReadLabRegionStats(id) is null)
                            throw new InvalidOperationException("Directional force diagnostics missing.");
                        var physicalY = LabGroundExperiments.CurveExtent(raw.Contour,1);
                        var displayY = LabGroundExperiments.CurveExtent(shown.Contour,1);
                        var physicalX = LabGroundExperiments.CurveExtent(raw.Contour,0);
                        var displayX = LabGroundExperiments.CurveExtent(shown.Contour,0);
                        var rawGeometry=LabDisplayGeometry.Inspect(raw.Contour); var shownGeometry=LabDisplayGeometry.Inspect(shown.Contour);
                        if (shownGeometry.SelfIntersects || shownGeometry.Area<=0 || !double.IsFinite(shownGeometry.Area))
                            throw new InvalidOperationException($"{phase.Name}/{i}: displayed sampled curve folds or loses positive area");
                        var mass = nodes.Sum(n=>(double)n.Mass);
                        samples.Add(new(physicalY.Maximum-physicalY.Minimum,displayY.Maximum-displayY.Minimum,
                            raw.CoreY,shown.CoreY,nodes.Sum(n=>n.Y*(double)n.Mass)/mass,nodes.Sum(n=>n.X*(double)n.Mass)/mass,
                            Math.Abs(raw.CoreY+physicalY.Minimum-shown.CoreY-displayY.Minimum),
                            Math.Abs(physicalX.Maximum-physicalX.Minimum-displayX.Maximum+displayX.Minimum),rawGeometry.Area,shownGeometry.Area));
                    }
                    var rawHeight = LabGroundExperiments.Analyze(samples.Select(s=>s.PhysicalHeight).ToArray());
                    var shownHeight = LabGroundExperiments.Analyze(samples.Select(s=>s.DisplayHeight).ToArray());
                    var ratio = rawHeight.FullBandRms>1e-12 ? shownHeight.FullBandRms/rawHeight.FullBandRms : (double?)null;
                    var meanHeightRatio = samples.Average(s=>s.DisplayHeight)/samples.Average(s=>s.PhysicalHeight);
                    var meanAreaRatio = samples.Average(s=>s.DisplayArea)/samples.Average(s=>s.PhysicalArea);
                    summaries.Add(new
                    {
                        phase.Name, phase.Frames, PhysicalHeight=rawHeight, DisplayHeight=shownHeight,
                        HeightFullBandRatio=ratio, DisplayReductionTarget=ratio is not null && ratio<=.70,
                        PhysicalCoreY=LabGroundExperiments.Analyze(samples.Select(s=>s.PhysicalCoreY).ToArray()),
                        DisplayCoreY=LabGroundExperiments.Analyze(samples.Select(s=>s.DisplayCoreY).ToArray()),
                        PhysicalComY=LabGroundExperiments.Analyze(samples.Select(s=>s.ComY).ToArray()),
                        ComDisplacement=samples[^1].ComX-samples[0].ComX,
                        MeanDisplayHeightRatio=meanHeightRatio,
                        MeanDisplaySampledAreaRatio=meanAreaRatio,
                        MaximumWorldBottomError=samples.Max(s=>s.BottomError), MaximumWidthError=samples.Max(s=>s.WidthError),
                        DisplayShapeDiagnostic=Math.Abs(meanHeightRatio-1)<=.01 && Math.Abs(meanAreaRatio-1)<=.01 && samples.Max(s=>s.BottomError)<=2e-6 && samples.Max(s=>s.WidthError)<=1e-9,
                    });
                }
            }
            catch (Exception error) { failure=error.ToString(); failures.Add(mode+": "+error.Message); }
            runs.Add(new { Mode=mode.ToString(), Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),
                Completed=failure is null, Error=failure, Phases=summaries });
            Save();
        }
        if (failures.Count>0) throw new InvalidOperationException("Region experiment failed: "+string.Join("; ",failures));

        void Save() => File.WriteAllText(Path.Combine(output,"region-display-results.json"),JsonSerializer.Serialize(new
        {
            Kind="isolated-region-drive-and-display", Configuration=config, FixedDt=Dt, Gait=false,
            FloorBounds=new[] {-256,256}, DisplayTimeConstantSeconds=LabDisplaySmoothing.TimeConstantSeconds,
            RegionDefinition="outer third of exact current curve AABB; 16 ring nodes only; world A/D side; W/S top; point nearest region center",
            DisplayDefinition="Y low-pass above current exact world bottom; X/width unchanged; bypass airborne/Space/drag; no physics writes",
            Comparison="Each drive has independent raw and filtered display observations of the same physics trajectory; changing drive is a physical experiment",
            Spectrum="demeaned rectangular DFT 0.5Hz through Nyquist; display 30% RMS reduction and mean height within 1% are diagnostic targets, not product acceptance",
            AreaAndLoops="8 samples per quadratic segment (128 total), approximate signed area and strict segment crossings; does not prove exact curve topology",
            Completed=runs.Count==3, SafetyPassed=runs.Count==3 && failures.Count==0, Failures=failures, Runs=runs, ManualAcceptance=false,
        },new JsonSerializerOptions {WriteIndented=true}));
    }

    private static int[] Bits(DangoBodyState body) => body.Contour.Concat([body.CoreX,body.CoreY,body.SignedArea,
        body.CentroidX,body.CentroidY,body.MaximumSpeed]).Select(BitConverter.SingleToInt32Bits).ToArray();
}
