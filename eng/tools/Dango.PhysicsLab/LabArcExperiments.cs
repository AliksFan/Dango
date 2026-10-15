using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

/// <summary>Default-material physical comparisons. Display filtering is deliberately absent.</summary>
public static class LabArcExperiments
{
    private static readonly double Dt = TimeSpan.FromSeconds(1d / 60).TotalSeconds;
    public static void RunContracts(string module, string output)
    {
        Directory.CreateDirectory(output);
        var p=DangoBodyMaterial.Configure(DangoBodyControls.Default);
        using var world=DangoPhysicsWorld.Create(module,1,p);
        world.RequireLab();
        if (world.LabGroundModel!=DangoLabGroundModel.ElasticArcs || world.LabRegionDenominator!=2)
            throw new InvalidOperationException("arc/half capabilities");
        var id=world.Add(0,0);world.SetHorizontalBounds(-256,256);
        using var log=new StreamWriter(Path.Combine(output,"arc-contract-trace.jsonl"));
        for(var i=0;i<420;i++)
        {
            var frame=world.StepLab(Dt,[new(id,Horizontal:i is >=300 and <330?1:0,
                Stretch:i is >=330 and <360?1:0,Downward:i is >=390?1:0,InputActive:i>=300)],DangoLabDrive.DirectionRegion);
            var arc=world.ReadLabArcStats(id);var ground=world.ReadLabGroundStats(id);var region=world.ReadLabRegionStats(id);
            log.WriteLine(JsonSerializer.Serialize(new { tick=i,succeeded=frame.Succeeded,arc,ground,region }));log.Flush();
            if (!frame.Succeeded || arc is null || ground is null || arc.Fault || ground.SolverFault || ground.SchemaVersion!=2 ||
                ground.FloorBox2DContactCount!=0 || Math.Abs(ground.FrictionCoefficient-p.GroundFriction)>1e-6 ||
                ground.AbsoluteTangentImpulse>ground.FrictionCoefficient*ground.NormalImpulse+1e-5 ||
                Math.Abs(ground.MomentumResidualX)>1e-5 || Math.Abs(ground.MomentumResidualY)>1e-5 || Math.Abs(ground.TorqueResidual)>1e-5)
                throw new InvalidOperationException($"arc exported contact contract at {i}: {JsonSerializer.Serialize(new{arc,ground})}");
        }
        var before=world.ReadLabNodes(id);var rejected=false;
        try { world.StepLab(Dt,[new(id)],DangoLabDrive.DirectionPoint); }
        catch(ArgumentOutOfRangeException) { rejected=true; }
        if(!rejected || !before.SequenceEqual(world.ReadLabNodes(id)))
            throw new InvalidOperationException("removed point mode must reject without advancing physics");
        Console.WriteLine("PASS: arc optional exports, half capability, schema, 420 real frames, contact ledger, point rejection atomicity");
    }

    private sealed record Sample(double Height, double Width, double CoreY, double ComX, double ComY,
        double MaximumSpeed, double Area, double Gap, DangoLabArcStats? Arc);
    private sealed record Phase(string Name, int Frames, double Horizontal = 0, double Stretch = 0,
        double Downward = 0, bool JumpFirst = false);
    private sealed record Observation(string Name, double MeanHeight, double TailHeight, double MeanWidth,
        double MeanArea, double ComDistance, double MeanComSpeed, double MaximumSpeed,
        LabGroundExperiments.Spectrum Height, LabGroundExperiments.Spectrum CoreY,
        LabGroundExperiments.Spectrum ComY, double MinimumGap, int MaximumActiveContacts,
        double MaximumContactEnergyIncrease, double MaximumPenetration, double MaximumInternalForce,
        double MaximumInternalTorque, double MaximumDampingPower, double PositionRepairArcPotentialEnergyChange,
        IReadOnlyList<float> TailContour, double TailCoreY, IReadOnlyList<double[]> Window);
    private sealed record RunResult(string Label, string Sha256, DangoLabGroundModel Model, DangoLabDrive Drive,
        bool Completed, string? Error, IReadOnlyList<Observation> Phases);

    public static void Run(string baselineModule, string halfModule, string arcModule, string output)
    {
        Directory.CreateDirectory(output);
        var p = DangoBodyMaterial.Configure(DangoBodyControls.Default);
        var runs = new[] {
            Measure("old-core", baselineModule, DangoLabGroundModel.DiscreteNodes, DangoLabDrive.Core),
            Measure("old-half", halfModule, DangoLabGroundModel.DiscreteNodes, DangoLabDrive.DirectionRegion),
            Measure("arcs-core", arcModule, DangoLabGroundModel.ElasticArcs, DangoLabDrive.Core),
            Measure("arcs-half", arcModule, DangoLabGroundModel.ElasticArcs, DangoLabDrive.DirectionRegion),
        };
        var comparisons = new[] { Compare(runs[0], runs[2]), Compare(runs[1], runs[3]) };
        File.WriteAllText(Path.Combine(output, "arc-results.json"), JsonSerializer.Serialize(new {
            Kind = "isolated-elastic-spline-arcs", Completed = runs.All(r => r.Completed),
            SafetyPassed = runs.All(r => r.Completed), Parameters = p, FixedDt = Dt,
            Gait = false, DisplaySmoothing = false, RegionDenominator = 2,
            Scope = "default material, one body, flat floor, walking and W/S/Space; not endpoint or product acceptance",
            Shape = "same quadratic spline for raw display/material/contact; area uses 128-point quadrature approximation",
            Spectrum = "mean-removed DFT, full 0.5Hz through Nyquist; physical height/Core/COM reported separately",
            Comparisons = comparisons, Runs = runs,
            ManualAcceptance = false,
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (runs.Any(r => !r.Completed)) throw new InvalidOperationException("Elastic arc safety run failed; evidence retained.");

        RunResult Measure(string label, string module, DangoLabGroundModel expected, DangoLabDrive drive)
        {
            var observations = new List<Observation>(); string? error = null;
            try
            {
                using var world = DangoPhysicsWorld.Create(module, 1, p);
                world.RequireLab(); world.RequireBodyMaterialModel(1);
                if (world.LabGroundModel != expected) throw new InvalidOperationException("ground model capability mismatch");
                if (drive == DangoLabDrive.DirectionRegion && world.LabRegionDenominator != 2)
                    throw new InvalidOperationException("half-region capability mismatch");
                var id = world.Add(0, 0); world.SetHorizontalBounds(-256, 256);
                var phases = new[] { new Phase("rest", 300), new("startup-right",120,1),
                    new("steady-right",480,1), new("release-right",180), new("startup-left",120,-1),
                    new("steady-left",480,-1), new("release-left",180) };
                foreach (var phase in phases) Observe(phase);
                // Reset before actions so their reference does not depend on the walking end pose.
                world.Remove(id); id = world.Add(0, 0);
                foreach (var phase in new[] { new Phase("action-rest",300), new("W",60,Stretch:1),
                    new("W-release",120), new("S",60,Downward:1), new("S-release",120),
                    new("Space",120,JumpFirst:true), new("W-Space",120,Stretch:1,JumpFirst:true),
                    new("settle-before-S-Space",180), new("S-Space",120,Downward:1,JumpFirst:true), new("final-rest",180) }) Observe(phase);

                void Observe(Phase phase)
                {
                    var samples = new List<Sample>();
                    float[] tailContour=[];
                    for (var i=0;i<phase.Frames;i++)
                    {
                        var frame = world.StepLab(Dt,[new(id,Horizontal:phase.Horizontal,Stretch:phase.Stretch,
                            Downward:phase.Downward,JumpPressed:phase.JumpFirst && i==0,
                            InputActive:phase.Horizontal!=0 || phase.Stretch!=0 || phase.Downward!=0 || phase.JumpFirst)],drive);
                        if (!frame.Succeeded) throw new InvalidOperationException($"{phase.Name}/{i}: native {frame.ErrorCode}");
                        var body=frame.Bodies.Single(); var nodes=world.ReadLabNodes(id);
                        tailContour=body.Contour.ToArray();
                        if (nodes.Any(n=>!float.IsFinite(n.X+n.Y+n.VelocityX+n.VelocityY+n.Mass) || n.Mass<=0))
                            throw new InvalidOperationException($"{phase.Name}/{i}: node/mass safety");
                        var x=LabGroundExperiments.CurveExtent(body.Contour,0);
                        var y=LabGroundExperiments.CurveExtent(body.Contour,1);
                        if (body.CoreX+x.Minimum < -254 || body.CoreX+x.Maximum>254)
                            throw new InvalidOperationException($"{phase.Name}/{i}: floor bounds");
                        var geometry=LabDisplayGeometry.Inspect(body.Contour);
                        if (geometry.Area<=0 || geometry.SelfIntersects)
                            throw new InvalidOperationException($"{phase.Name}/{i}: sampled curve topology");
                        if (body.MaximumSpeed > p.MaximumSpeed + .0002)
                            throw new InvalidOperationException($"{phase.Name}/{i}: global speed limit");
                        if (expected==DangoLabGroundModel.DiscreteNodes && nodes.Skip(1).Min(n=>n.Y)-p.NodeRadius < p.GroundHeight-2*p.NodeRadius)
                            throw new InvalidOperationException($"{phase.Name}/{i}: baseline ground penetration");
                        var ground=world.ReadLabGroundStats(id); var arc=world.ReadLabArcStats(id);
                        if (expected==DangoLabGroundModel.ElasticArcs)
                        {
                            if (arc is null || ground is null || arc.Fault || ground.SolverFault ||
                                arc.MaximumPenetration>1e-4 || ground.MaximumPenetrationAfterSolve>1e-4 ||
                                body.CoreY+y.Minimum<p.GroundHeight-1e-4 || ground.FloorBox2DContactCount!=0)
                                throw new InvalidOperationException($"{phase.Name}/{i}: arc contact geometry/fault");
                            if (arc.DampingPower>1e-5 || arc.ContactKineticEnergyChange>Math.Max(1e-6,Math.Abs(arc.ContactKineticEnergyBefore)*1e-5))
                                throw new InvalidOperationException($"{phase.Name}/{i}: material/contact dissipation");
                            if (arc.MaximumFrictionConeViolation>1e-5)
                                throw new InvalidOperationException($"{phase.Name}/{i}: contact block friction cone");
                            if (Math.Abs(body.CoreY+y.Minimum-ground.FootY)>2e-4 || Math.Abs(body.CoreY+y.Maximum-ground.ExactCurveMaximumY)>2e-4)
                                throw new InvalidOperationException($"{phase.Name}/{i}: native/display geometry mismatch");
                            if (Math.Abs(ground.MomentumResidualX)>1e-5 || Math.Abs(ground.MomentumResidualY)>1e-5 ||
                                Math.Abs(ground.TorqueResidual)>1e-5 || ground.AbsoluteTangentImpulse>ground.FrictionCoefficient*ground.NormalImpulse+1e-5)
                                throw new InvalidOperationException($"{phase.Name}/{i}: contact impulse/momentum ledger");
                        }
                        var mass=nodes.Sum(n=>(double)n.Mass);
                        samples.Add(new(y.Maximum-y.Minimum,x.Maximum-x.Minimum,body.CoreY,
                            nodes.Sum(n=>n.X*(double)n.Mass)/mass,nodes.Sum(n=>n.Y*(double)n.Mass)/mass,
                            body.MaximumSpeed,geometry.Area,body.CoreY+y.Minimum-p.GroundHeight,arc));
                    }
                    var arcs=samples.Select(s=>s.Arc).OfType<DangoLabArcStats>().ToArray();
                    var observation=new Observation(phase.Name,samples.Average(s=>s.Height),samples.TakeLast(Math.Min(120,samples.Count)).Average(s=>s.Height),
                        samples.Average(s=>s.Width),samples.Average(s=>s.Area),samples[^1].ComX-samples[0].ComX,
                        Math.Abs(samples[^1].ComX-samples[0].ComX)/((samples.Count-1)*Dt),samples.Max(s=>s.MaximumSpeed),
                        LabGroundExperiments.Analyze(samples.Select(s=>s.Height).ToArray()),
                        LabGroundExperiments.Analyze(samples.Select(s=>s.CoreY).ToArray()),
                        LabGroundExperiments.Analyze(samples.Select(s=>s.ComY).ToArray()),samples.Min(s=>s.Gap),
                        arcs.Length==0?0:arcs.Max(a=>a.ActiveContacts),arcs.Length==0?0:arcs.Max(a=>a.ContactKineticEnergyChange),
                        arcs.Length==0?0:arcs.Max(a=>a.MaximumPenetration),
                        arcs.Length==0?0:arcs.Max(a=>Math.Sqrt(a.InternalForceX*a.InternalForceX+a.InternalForceY*a.InternalForceY)),
                        arcs.Length==0?0:arcs.Max(a=>Math.Abs(a.InternalTorque)),arcs.Length==0?0:arcs.Max(a=>a.DampingPower),
                        arcs.Sum(a=>(double)a.PositionRepairArcPotentialEnergyChange),tailContour,samples[^1].CoreY,
                        samples.Select(s=>new[]{s.Height,s.CoreY,s.ComY,s.ComX,s.Width,s.Area,s.Gap}).ToArray());
                    observations.Add(observation);
                    File.WriteAllText(Path.Combine(output,label+"-progress.json"),JsonSerializer.Serialize(observations));
                    Console.WriteLine($"{label}: {phase.Name} passed safety ({phase.Frames} frames)");
                }
            }
            catch(Exception e) { error=e.ToString(); Console.Error.WriteLine(label+": "+error); }
            return new(label,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module))),expected,drive,error is null,error,observations);
        }

        object Compare(RunResult baseline, RunResult candidate)
        {
            var phases=new[]{"steady-right","steady-left"}.Select(name=> {
                var a=baseline.Phases.FirstOrDefault(p=>p.Name==name);var b=candidate.Phases.FirstOrDefault(p=>p.Name==name);
                var ar=baseline.Phases.FirstOrDefault(p=>p.Name=="rest");var br=candidate.Phases.FirstOrDefault(p=>p.Name=="rest");
                double? Ratio(double? x,double? y)=>y is >1e-12?x/y:null;
                var hr=Ratio(b?.Height.FullBandRms,a?.Height.FullBandRms);
                var cr=Ratio(b?.CoreY.FullBandRms,a?.CoreY.FullBandRms);
                var mr=Ratio(b?.ComY.FullBandRms,a?.ComY.FullBandRms);
                var dr=Ratio(b is null?null:Math.Abs(b.ComDistance),a is null?null:Math.Abs(a.ComDistance));
                var sr=Ratio(b?.MeanComSpeed,a?.MeanComSpeed);
                var compression=a is not null && b is not null && ar is not null && br is not null
                    ?(double?)(Math.Max(0,1-b.MeanHeight/br.TailHeight)-Math.Max(0,1-a.MeanHeight/ar.TailHeight)):null;
                return new { Name=name, HeightRmsRatio=hr,CoreRmsRatio=cr,ComRmsRatio=mr,DistanceRatio=dr,SpeedRatio=sr,
                    AdditionalCompression=compression,MeanHeightRatio=Ratio(b?.MeanHeight,a?.MeanHeight),
                    RestHeightRatio=Ratio(br?.TailHeight,ar?.TailHeight),MeanAreaRatio=Ratio(b?.MeanArea,a?.MeanArea),
                    QuantitativeImprovementTarget=hr is <=.7 && cr is <=1.05 && mr is <=1.05,
                    SpeedAndShapeCostPassed=dr is >=.9 and <=1.1 && sr is >=.9 and <=1.1 && compression is <=.01 };
            }).ToArray();
            return new { Baseline=baseline.Label,Candidate=candidate.Label,Phases=phases };
        }
    }
}
