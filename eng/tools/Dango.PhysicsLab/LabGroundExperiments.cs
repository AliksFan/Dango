using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

/// <summary>Single-body, flat-floor A/B only. Results do not imply product acceptance.</summary>
public static class LabGroundExperiments
{
    public static void RunBaselineContracts(string module)
    {
        foreach(var jump in new[]{false,true})
        {
            var parameters=DangoBodyMaterial.Configure(DangoBodyControls.Default);
            using var old=DangoPhysicsWorld.Create(module,1,parameters);
            using var lab=DangoPhysicsWorld.Create(module,1,parameters);
            var oldId=old.Add(0,0);var labId=lab.Add(0,0);
            old.SetHorizontalBounds(-256,256);lab.SetHorizontalBounds(-256,256);
            for(var i=0;i<480;i++)
            {
                var pressed=jump && i==300;
                var aFrame=old.Step(TimeSpan.FromSeconds(Dt),[new(oldId,JumpPressed:pressed)]);
                var bFrame=lab.StepLab(Dt,[new(labId,JumpPressed:pressed,InputActive:pressed)],DangoLabDrive.Core);
                var a=aFrame.Bodies.Single();var b=bFrame.Bodies.Single();
                var aFloats=a.Contour.Concat([a.CoreX,a.CoreY,a.SignedArea,a.CentroidX,a.CentroidY,a.MaximumSpeed]).Select(BitConverter.SingleToInt32Bits);
                var bFloats=b.Contour.Concat([b.CoreX,b.CoreY,b.SignedArea,b.CentroidX,b.CentroidY,b.MaximumSpeed]).Select(BitConverter.SingleToInt32Bits);
                if(!aFrame.Succeeded || !bFrame.Succeeded || !aFloats.SequenceEqual(bFloats) ||
                    a.GroundContactCount!=b.GroundContactCount || a.IsGrounded!=b.IsGrounded ||
                    a.SelectedDragNode!=b.SelectedDragNode || a.PointCount!=b.PointCount ||
                    aFrame.ContactStepCount!=bFrame.ContactStepCount)
                    throw new InvalidOperationException($"baseline old/lab bitwise mismatch jump={jump} frame={i}");
            }
            Console.WriteLine($"PASS: default-v6 old/lab Core bitwise 480 frames jump={jump}");
        }
    }

    private static readonly double Dt = TimeSpan.FromSeconds(1d / 60).TotalSeconds;
    private sealed record Phase(string Name, int Frames, double Horizontal);
    private sealed record Sample(double CoreX, double CoreY, double ComX, double ComY,
        double Width, double Height, double RawWidth, double RawHeight, double Speed, bool Contact, double Bottom, DangoLabGroundStats? Ground);
    internal sealed record Spectrum(double RawRms, double FullBandRms, double Band05To5Rms, double DominantHz);
    private sealed record GroundTotals(double NormalImpulse, double AbsoluteTangentImpulse,
        double SummedPositionCorrectionL2, double MaximumPositionCorrection, double MinimumGap,
        double MaximumGap, double MaximumPenetrationAfterSolve, double NormalEnergyChange,
        double FrictionEnergyChange, double ShapeEnergyChange, double MaximumMomentumResidual,
        double MaximumTorqueResidual, double MaximumCurveExtremaMismatch);
    private sealed record PhaseResult(string Name, int Frames, double CoreDistance, double ComDistance,
        double AverageWidth, double AverageHeight, double AverageRawWidth, double AverageRawHeight,
        double TailMeanHeight, double TailMeanRawHeight, double EndMaximumNodeSpeed,
        double MeanSpeed, double MeanMaximumNodeSpeed, int ZeroContactFrames,
        int LongestZeroContactFrames, double MinimumBottom, Spectrum CoreY, Spectrum MassComY,
        Spectrum ExactCurveHeight, Spectrum RawControlHeight, GroundTotals? GroundTotals,
        IReadOnlyList<DangoLabGroundStats>? GroundLedger);
    private sealed record RunResult(string Label, string Sha256, string GroundModel, bool Completed,
        string? Error, IReadOnlyList<PhaseResult> Phases);

    public static void Run(string baselinePath, string candidatePath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var baseline = Measure("baseline", baselinePath, DangoLabGroundModel.DiscreteNodes);
        var candidate = Measure("candidate", candidatePath, DangoLabGroundModel.ContinuousEnvelope);
        var comparisons = new[] { "steady-right", "steady-left" }.Select(name => {
            var a = baseline.Phases.FirstOrDefault(p => p.Name == name);
            var b = candidate.Phases.FirstOrDefault(p => p.Name == name);
            var ratio = a is not null && b is not null && Math.Abs(a.ComDistance) > 1e-8
                ? (double?)(Math.Abs(b.ComDistance) / Math.Abs(a.ComDistance)) : null;
            var speedRatio = Ratio(b?.MeanSpeed,a?.MeanSpeed);
            var aRest=baseline.Phases.FirstOrDefault(p=>p.Name=="rest");
            var bRest=candidate.Phases.FirstOrDefault(p=>p.Name=="rest");
            var aCompression=Compression(a?.AverageHeight,aRest?.TailMeanHeight);
            var bCompression=Compression(b?.AverageHeight,bRest?.TailMeanHeight);
            var heightCompressionPp=(bCompression-aCompression)*100;
            var rawCompressionPp=(Compression(b?.AverageRawHeight,bRest?.TailMeanRawHeight)-Compression(a?.AverageRawHeight,aRest?.TailMeanRawHeight))*100;
            var heightRatio=Ratio(b?.ExactCurveHeight.FullBandRms,a?.ExactCurveHeight.FullBandRms);
            var coreRatio=Ratio(b?.CoreY.FullBandRms,a?.CoreY.FullBandRms);
            var comRatio=Ratio(b?.MassComY.FullBandRms,a?.MassComY.FullBandRms);
            return new { Phase = name, DistanceRatio = ratio,
                DisplacementGate = ratio is >= .9 and <= 1.1,
                SpeedRatio = speedRatio, SpeedGate = speedRatio is >= .9 and <= 1.1,
                BaselineRestReferenceExactHeight=aRest?.TailMeanHeight, CandidateRestReferenceExactHeight=bRest?.TailMeanHeight,
                BaselineCompression=aCompression,CandidateCompression=bCompression,
                MeanHeightAdditionalCompressionPercentagePoints = heightCompressionPp, MeanHeightGate = heightCompressionPp is <= 1,
                LegacyRawAdditionalCompressionPercentagePoints=rawCompressionPp, MeanExactHeightRatio=Ratio(b?.AverageHeight,a?.AverageHeight),
                ExactCurveHeightFullBandRmsRatio = heightRatio, CoreFullBandRmsRatio=coreRatio, MassComFullBandRmsRatio=comRatio,
                QuantitativeImprovementTarget = heightRatio is <= .7 && coreRatio is <= 1.05 && comRatio is <= 1.05,
                CoreRawRmsRatio = Ratio(b?.CoreY.RawRms, a?.CoreY.RawRms),
                MassComRawRmsRatio = Ratio(b?.MassComY.RawRms, a?.MassComY.RawRms),
                LegacyRawControlHeightRawRmsRatio = Ratio(b?.RawControlHeight.RawRms,a?.RawControlHeight.RawRms),
                LegacyRawControlHeightFullBandRmsRatio = Ratio(b?.RawControlHeight.FullBandRms,a?.RawControlHeight.FullBandRms),
                HeightRawRmsRatio = Ratio(b?.ExactCurveHeight.RawRms, a?.ExactCurveHeight.RawRms) };
        }).ToArray();
        var safetyPassed = baseline.Completed && candidate.Completed;
        File.WriteAllText(Path.Combine(outDir, "ground-ab-results.json"), JsonSerializer.Serialize(new {
            SchemaVersion = 1, Configuration = "window-default-v6", Controls = DangoBodyControls.Default,
            Parameters = DangoBodyMaterial.Configure(DangoBodyControls.Default), MaterialVersion = DangoBodyMaterial.ModelVersion,
            Drive = "Core", Bias = 0, Gait = false, FixedDt = Dt, FloorBounds = new[] { -256, 256 },
            Units = "all geometry in world units; RMS DIP = value * 72; display uses 120 DIP/world",
            SpectrumDefinition = "demeaned rectangular DFT; full band 0.5 Hz through Nyquist inclusive; narrow band 0.5-5 Hz; raw RMS is mean-removed unfiltered",
            PhysicalContourDefinition = "ExactCurveHeight: analytic quadratic Bezier extrema of native radial outer controls; RawControlHeight: max-min native contour controls (legacy)",
            GeometryOracle = "baseline node bottom >= floor - 2 node radii (existing oracle); candidate exact curve gap and maximum postsolve penetration <= 1e-4, control nodes may cross floor",
            GroundedDefinition = "baseline native discrete GroundContactCount>0; candidate native exact curve geometric gap<=0.035 (not proof of positive support impulse); per-frame normal impulse and gap ledger retained separately",
            ConservationOracle = "normal/friction energy after <= before + max(1e-6, abs(before)*1e-5); absolute momentum/torque residual <= 1e-5; abs tangent impulse <= mu normal impulse + 1e-5; managed/native curve extrema within 2e-4",
            Scope = "default/Core single body flat floor only; no product/manual acceptance; no endpoint or action matrix",
            SafetyPassed = safetyPassed, Safety = new { Geometry = baseline.Completed && candidate.Completed,
                DisplacementTolerance = "plus/minus 10 percent independently for each direction", Comparisons = comparisons },
            SpeedAndShapeCostPassed = comparisons.All(c=>c.DisplacementGate && c.SpeedGate && c.MeanHeightGate),
            Improvement = "evaluate raw/full-band/narrow-band RMS separately from safety; frequency migration is not improvement",
            Runs = new[] { baseline, candidate }
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (!safetyPassed) throw new InvalidOperationException("Ground A/B safety gate failed; compact evidence saved.");

        RunResult Measure(string label, string module, DangoLabGroundModel expected)
        {
            var results = new List<PhaseResult>();
            string? error = null;
            var model = "unavailable";
            var hash = "unavailable";
            try
            {
                hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module)));
                var p = DangoBodyMaterial.Configure(DangoBodyControls.Default);
                using var world = DangoPhysicsWorld.Create(module, 1, p);
                world.RequireBodyMaterialModel(1); world.RequireLab();
                model = world.LabGroundModel.ToString();
                if (world.LabGroundModel != expected) throw new InvalidOperationException($"Expected {expected}, got {model}.");
                var id = world.Add(0, 0); world.SetHorizontalBounds(-256, 256);
                var phases = new[] { new Phase("rest",300,0), new("startup-right",120,1),
                    new("steady-right",480,1), new("release-right",180,0), new("startup-left",120,-1),
                    new("steady-left",480,-1), new("release-left",180,0) };
                foreach (var phase in phases)
                {
                    var samples = new List<Sample>();
                    for (var frameIndex = 0; frameIndex < phase.Frames; frameIndex++)
                    {
                        var frame = world.StepLab(Dt, [new(id, Horizontal:phase.Horizontal, InputActive:phase.Horizontal != 0)], DangoLabDrive.Core);
                        if (!frame.Succeeded) throw new InvalidOperationException($"{phase.Name}/{frameIndex}: native {frame.ErrorCode}");
                        var b = frame.Bodies.Single(); var nodes = world.ReadLabNodes(id);
                        var ring = nodes.Where(n => n.Index != 0).ToArray();
                        var values = nodes.SelectMany(n => new[] {n.X,n.Y,n.VelocityX,n.VelocityY,n.Mass}).Concat(b.Contour);
                        if (values.Any(v => !float.IsFinite(v)) || nodes.Any(n => n.Mass <= 0))
                            throw new InvalidOperationException($"{phase.Name}/{frameIndex}: finite/mass oracle");
                        var bottom = ring.Min(n => n.Y) - p.NodeRadius;
                        var extent = ring.Max(n => Math.Sqrt(Math.Pow(n.X-b.CoreX,2)+Math.Pow(n.Y-b.CoreY,2))) + p.NodeRadius;
                        if (b.CoreX-extent < -254 || b.CoreX+extent > 254)
                            throw new InvalidOperationException($"{phase.Name}/{frameIndex}: floor edge margin");
                        // Retain baseline's established node oracle; candidate instead constrains the exact rendered curve.
                        if (expected == DangoLabGroundModel.DiscreteNodes && bottom < p.GroundHeight-2*p.NodeRadius)
                            throw new InvalidOperationException($"{phase.Name}/{frameIndex}: penetration {p.GroundHeight-bottom}");
                        var ground = world.ReadLabGroundStats(id);
                        if (expected == DangoLabGroundModel.ContinuousEnvelope && (ground is null || ground.SolverFault || ground.Gap < -1e-4 || ground.MaximumPenetrationAfterSolve > 1e-4 || ground.FloorBox2DContactCount != 0))
                            throw new InvalidOperationException($"{phase.Name}/{frameIndex}: exact curve safety oracle {ground}");
                        var curveX = CurveExtent(b.Contour,0); var curveY = CurveExtent(b.Contour,1);
                        if(ground is not null && (Math.Abs(b.CoreY+curveY.Minimum-ground.FootY)>2e-4 ||
                            Math.Abs(b.CoreY+curveY.Maximum-ground.ExactCurveMaximumY)>2e-4))
                            throw new InvalidOperationException($"{phase.Name}/{frameIndex}: managed/native exact curve extrema mismatch");
                        if(ground is not null && (
                            ground.NormalEnergyAfter-ground.NormalEnergyBefore>Math.Max(1e-6,Math.Abs(ground.NormalEnergyBefore)*1e-5) ||
                            ground.FrictionEnergyAfter-ground.FrictionEnergyBefore>Math.Max(1e-6,Math.Abs(ground.FrictionEnergyBefore)*1e-5) ||
                            Math.Abs(ground.MomentumResidualX)>1e-5 || Math.Abs(ground.MomentumResidualY)>1e-5 ||
                            Math.Abs(ground.TorqueResidual)>1e-5 ||
                            ground.AbsoluteTangentImpulse>ground.FrictionCoefficient*ground.NormalImpulse+1e-5))
                            throw new InvalidOperationException($"{phase.Name}/{frameIndex}: conservation/dissipation oracle {ground}");
                        var rawX = b.Contour.Where((_,i)=>i%2==0).Select(v=>(double)v).ToArray();
                        var rawY = b.Contour.Where((_,i)=>i%2==1).Select(v=>(double)v).ToArray();
                        var totalMass = nodes.Sum(n => (double)n.Mass);
                        samples.Add(new(b.CoreX,b.CoreY,nodes.Sum(n => n.X*(double)n.Mass)/totalMass,
                            nodes.Sum(n => n.Y*(double)n.Mass)/totalMass,
                            curveX.Maximum-curveX.Minimum,
                            curveY.Maximum-curveY.Minimum,rawX.Max()-rawX.Min(),rawY.Max()-rawY.Min(),b.MaximumSpeed,
                            ground?.Grounded ?? b.GroundContactCount>0,bottom,ground));
                    }
                    var maxGap=0; var gap=0;
                    foreach(var sample in samples) { gap=sample.Contact?0:gap+1; maxGap=Math.Max(maxGap,gap); }
                    results.Add(new(phase.Name,phase.Frames,samples[^1].CoreX-samples[0].CoreX,
                        samples[^1].ComX-samples[0].ComX,samples.Average(s=>s.Width),samples.Average(s=>s.Height),
                        samples.Average(s=>s.RawWidth),samples.Average(s=>s.RawHeight),
                        samples.TakeLast(120).Average(s=>s.Height),samples.TakeLast(120).Average(s=>s.RawHeight),samples[^1].Speed,
                        Math.Abs(samples[^1].ComX-samples[0].ComX)/((samples.Count-1)*Dt),
                        samples.Average(s=>s.Speed),samples.Count(s=>!s.Contact),maxGap,samples.Min(s=>s.Bottom),
                        Analyze(samples.Select(s=>s.CoreY).ToArray()),Analyze(samples.Select(s=>s.ComY).ToArray()),
                        Analyze(samples.Select(s=>s.Height).ToArray()),Analyze(samples.Select(s=>s.RawHeight).ToArray()),Totals(samples),
                        samples.Any(s=>s.Ground is not null)?samples.Where((_,i)=>i%30==0 || i==samples.Count-1).Select(s=>s.Ground!).ToArray():null));
                    // Small raw windows allow independent spectrum checks without full multi-case traces.
                    if (phase.Name.StartsWith("steady",StringComparison.Ordinal))
                        File.WriteAllText(Path.Combine(outDir,$"{label}-{phase.Name}-window.json"),JsonSerializer.Serialize(new {
                            Dt, Columns = new[]{"coreY","massComY","exactCurveHeight","rawControlHeight"},
                            Samples=samples.Select(s=>new[]{s.CoreY,s.ComY,s.Height,s.RawHeight}) }));
                }
            }
            catch(Exception ex) { error=ex.Message; }
            var result=new RunResult(label,hash,model,error is null,error,results);
            File.WriteAllText(Path.Combine(outDir,$"{label}-progress.json"),JsonSerializer.Serialize(result));
            return result;
        }
    }

    private static GroundTotals? Totals(List<Sample> samples)
    {
        var values=samples.Select(s=>s.Ground).OfType<DangoLabGroundStats>().ToArray();
        if(values.Length==0)return null;
        return new(values.Sum(v=>(double)v.NormalImpulse),values.Sum(v=>(double)v.AbsoluteTangentImpulse),
            values.Sum(v=>(double)v.SummedPositionCorrectionL2),values.Max(v=>v.MaximumPositionCorrection),
            values.Min(v=>v.Gap),values.Max(v=>v.Gap),values.Max(v=>v.MaximumPenetrationAfterSolve),
            values.Sum(v=>(double)v.NormalEnergyAfter-v.NormalEnergyBefore),
            values.Sum(v=>(double)v.FrictionEnergyAfter-v.FrictionEnergyBefore),values.Sum(v=>(double)v.ShapeKineticEnergyDelta),
            values.Max(v=>Math.Sqrt(v.MomentumResidualX*v.MomentumResidualX+v.MomentumResidualY*v.MomentumResidualY)),
            values.Max(v=>Math.Abs(v.TorqueResidual)),samples.Max(s=>s.Ground is null?0:Math.Abs(s.Height-(s.Ground.ExactCurveMaximumY-s.Ground.FootY))));
    }

    public static (double Minimum, double Maximum) CurveExtent(IReadOnlyList<float> contour, int axis)
    {
        var n=contour.Count/2; double minimum=double.PositiveInfinity,maximum=double.NegativeInfinity;
        for(var i=0;i<n;i++)
        {
            var previous=(double)contour[2*((i+n-1)%n)+axis];
            var control=(double)contour[2*i+axis];
            var next=(double)contour[2*((i+1)%n)+axis];
            var a=(previous+control)/2; var c=(control+next)/2;
            Include(a); Include(c);
            var denominator=a-2*control+c;
            if(Math.Abs(denominator)>1e-15)
            {
                var t=(a-control)/denominator;
                if(t>0 && t<1) Include((1-t)*(1-t)*a+2*(1-t)*t*control+t*t*c);
            }
        }
        return(minimum,maximum);
        void Include(double value) {minimum=Math.Min(minimum,value);maximum=Math.Max(maximum,value);}
    }

    private static double? Compression(double? mean,double? rest) => mean is not null && rest is > 0 ? Math.Max(0,1-mean.Value/rest.Value) : null;
    private static double? Ratio(double? value,double? baseline) => baseline is > 0 ? value / baseline : null;
    internal static Spectrum Analyze(double[] values)
    {
        var mean=values.Average(); var n=values.Length;
        var raw=Math.Sqrt(values.Average(v=>(v-mean)*(v-mean)));
        double full=0,narrow=0,dominantPower=0,dominantHz=0;
        for(var k=1;k<=n/2;k++)
        {
            var hz=k/(n*Dt); if(hz<.5) continue;
            double re=0,im=0;
            for(var j=0;j<n;j++) { var angle=2*Math.PI*k*j/n; re+=(values[j]-mean)*Math.Cos(angle); im+=(values[j]-mean)*Math.Sin(angle); }
            var power=(k*2==n?1:2)*(re*re+im*im)/(n*n);
            full+=power; if(hz<=5) narrow+=power;
            if(power>dominantPower) { dominantPower=power; dominantHz=hz; }
        }
        return new(raw,Math.Sqrt(full),Math.Sqrt(narrow),dominantHz);
    }
}
