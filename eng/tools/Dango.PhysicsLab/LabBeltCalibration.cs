using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

internal sealed record LabBeltCalibrationParameters
{
    public double RadialStiffnessScale { get; init; }=1;
    public double RadialDampingScale { get; init; }=1;
    public double BeltStretchScale { get; init; }=1;
    public double BeltBendScale { get; init; }=1;
    public double AreaScale { get; init; }=1;
    public double ShortBoneStiffnessScale { get; init; }=1;
    public double ShortBoneDampingScale { get; init; }=1;
    public double ShapeDampingScale { get; init; }=1;
    public double OverallDamping { get; init; }
    public double GroundedBrake { get; init; }
    public double GroundedHorizontalDamping { get; init; }
    public static LabBeltCalibrationParameters Default { get; }=new();
    internal float[] ToNative()
    {
        double[] values=[RadialStiffnessScale,RadialDampingScale,BeltStretchScale,BeltBendScale,AreaScale,
            ShortBoneStiffnessScale,ShortBoneDampingScale,ShapeDampingScale,OverallDamping,GroundedBrake,GroundedHorizontalDamping];
        if(values.Any(x=>!double.IsFinite(x)||x<0||x>float.MaxValue))
            throw new ArgumentOutOfRangeException(nameof(values),"Calibration values must be finite nonnegative floats; native schema defines additional ranges.");
        return values.Select(x=>(float)x).ToArray();
    }
}

/// <summary>Finite calibration diagnostics. Runs no search and changes no material, time step, or production setting.</summary>
public static class LabBeltCalibration
{
    private const double Dt=1d/60;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
    private static readonly JsonSerializerOptions EvidenceJson=new(){NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
    private sealed record Phase(string Name,int Frames,double Horizontal=0,double Downward=0,bool JumpFirst=false);
    private sealed record Sample(double Height,double Width,double Area,double CoreX,double CoreY,double ComX,double ComY,double VelocityX,double MaximumSpeed,float[] Contour);
    private sealed record Metrics(int Frames,double Height,double Width,double Area,double CoreY,double ComY,double MeanAbsoluteComSpeed,double ComDistance,
        LabGroundExperiments.Spectrum HeightSpectrum,LabGroundExperiments.Spectrum CoreSpectrum,LabGroundExperiments.Spectrum ComSpectrum,
        LabGroundExperiments.Spectrum TailHeightSpectrum,LabGroundExperiments.Spectrum TailCoreSpectrum,LabGroundExperiments.Spectrum TailComSpectrum,float[] Contour);
    private sealed record EquivalenceResult(int Frames,bool Completed,bool BitwiseEqual,double MaximumAbsoluteDifference,double Tolerance,string? Error);
    private sealed record CaseResult(string Label,bool Completed,string? Error,LabBeltCalibrationParameters? Parameters,Dictionary<string,Metrics> Phases);

    public static void Run(string baselineHalfModule,string beforeBeltModule,string calibratedBeltModule,string output)
    {
        Directory.CreateDirectory(output);
        var config=DangoBodyMaterial.Configure(DangoBodyControls.Default);
        var results=new List<CaseResult>();
        var scenarios=new[]{new[]{new Phase("rest",300)},
            new[]{new Phase("rest-before-S-025",300),new Phase("S-025",60,Downward:.25),new Phase("unload-025",120)},
            new[]{new Phase("rest-before-S-050",300),new Phase("S-050",60,Downward:.5),new Phase("unload-050",120)},
            new[]{new Phase("rest-before-S-100",300),new Phase("S-100",60,Downward:1),new Phase("unload-100",120)},
            new[]{new Phase("rest-before-jump",300),new Phase("jump",120,JumpFirst:true)},
            new[]{new Phase("rest-before-walk",300),new Phase("walk",480,Horizontal:1),new Phase("stop",180)}};
        var reference=Measure("old-half-reference",baselineHalfModule,true,null,scenarios);
        var before=Measure("belt-before-default",beforeBeltModule,false,null,scenarios);
        var current=Measure("belt-calibrated-default",calibratedBeltModule,false,LabBeltCalibrationParameters.Default,scenarios);
        var equivalence=CheckEquivalence();
        var candidates=new List<object>();
        foreach(var name in new[]{"radial","area","stretch","bend","short-bone"})
        foreach(var scale in new[]{.8,1.2})
        {
            var p=name switch {
                "radial"=>LabBeltCalibrationParameters.Default with{RadialStiffnessScale=scale},
                "area"=>LabBeltCalibrationParameters.Default with{AreaScale=scale},
                "stretch"=>LabBeltCalibrationParameters.Default with{BeltStretchScale=scale},
                "bend"=>LabBeltCalibrationParameters.Default with{BeltBendScale=scale},
                _=>LabBeltCalibrationParameters.Default with{ShortBoneStiffnessScale=scale}};
            var candidate=Measure($"rest-{name}-{scale:0.0}",calibratedBeltModule,false,p,[scenarios[0]]);
            candidates.Add(new{candidate.Label,Scale=scale,Parameter=name,AgainstOldHalf=Shape(reference,candidate),AgainstBeltDefault=Shape(current,candidate)});
        }
        Write("calibration-results.json",new{
            Kind="finite-belt-calibration-diagnostic",FixedDt=Dt,Substeps=config.SubStepCount,Parameters=config,
            ModuleHashes=new{OldHalf=Hash(baselineHalfModule),Before=Hash(beforeBeltModule),Calibrated=Hash(calibratedBeltModule)},
            Completed=results.All(r=>r.Completed)&&equivalence.Completed&&equivalence.BitwiseEqual,Equivalence=equivalence,
            Scope="11 rest candidates including default; 10 one-at-a-time elastic +/-20% variants; no combinations or damping search",
            RestWindow="300 ticks, final 120 for shape and Tail spectra; motion gates use full moving phase spectra; COM aligned 256 point contour; no display smoothing",
            LocalContourDefinition="8 angular sectors starting at -pi, counterclockwise; pooled symmetric nearest-segment RMS distances in world units",
            DeformationDefinition="Each S load starts with a fresh world and 300 rest ticks; each unload follows only that load",
            Gates=new{RestHeightWidthRelativeDifference=.02,HeightRmsRatio=.7,CoreComRmsRatio=1.05,SpeedMinimum=.9,SpeedMaximum=1.1},
            DefaultRest=Shape(reference,current),Movement=Motion(reference,current),Deformation=Deformation(reference,current),Sensitivity=candidates,Runs=results,
            Acceptance="diagnostic only; no automatic tuning, no 30/120 Hz runs, no desktop or manual acceptance"});

        CaseResult Measure(string label,string module,bool legacy,LabBeltCalibrationParameters? parameters,Phase[][] groups)
        {
            var result=MeasureCase(label,module,legacy,parameters,groups,config,output);
            results.Add(result);return result;
        }

        EquivalenceResult CheckEquivalence()
        {
            var frames=0;var exact=true;double maximumAbsoluteDifference=0;string? error=null;object? lastState=null;
            using var trace=new StreamWriter(Path.Combine(output,"default-equivalence.jsonl"));
            try
            {
                foreach(var group in scenarios)
                {
                    using var a=new LabBeltWorld(beforeBeltModule,config);using var b=new LabBeltWorld(calibratedBeltModule,config,true,LabBeltCalibrationParameters.Default);
                    a.SetBounds(-256,256);b.SetBounds(-256,256);
                    foreach(var phase in group)
                    {
                        Console.WriteLine($"calibration default-equivalence: {phase.Name}");
                        for(var tick=0;tick<phase.Frames;tick++)
                        {
                            var input=new LabBeltInput(Horizontal:phase.Horizontal,Downward:phase.Downward,JumpPressed:phase.JumpFirst&&tick==0,InputActive:phase.Horizontal!=0||phase.Downward!=0||phase.JumpFirst);
                            lastState=new{Phase=phase.Name,Tick=tick,Before=a.Read(),After=b.Read(),Diagnostics=TryDiagnostics(b)};
                            a.Step(Dt,input);b.Step(Dt,input);var x=a.Read();var y=b.Read();
                            var av=x.Scaffold.Concat(x.Curve).Concat(x.Stats).ToArray();var bv=y.Scaffold.Concat(y.Curve).Concat(y.Stats).ToArray();
                            for(var i=0;i<av.Length;i++){exact&=BitConverter.SingleToInt32Bits(av[i])==BitConverter.SingleToInt32Bits(bv[i]);maximumAbsoluteDifference=Math.Max(maximumAbsoluteDifference,Math.Abs((double)av[i]-bv[i]));}
                            frames++;
                            if(!exact){trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,Before=x,After=y,Diagnostics=TryDiagnostics(b)},EvidenceJson));trace.Flush();
                                throw new InvalidOperationException("Default compatibility lost bitwise equality; no unapproved tolerance applied.");}
                        }
                        Write("default-equivalence-progress.json",new{Phase=phase.Name,Frames=frames,BitwiseEqual=exact,MaximumAbsoluteDifference=maximumAbsoluteDifference});
                    }
                }
            }
            catch(Exception e){error=e.ToString();trace.WriteLine(JsonSerializer.Serialize(new{Error=error,LastState=lastState},EvidenceJson));}
            return new(frames,error is null,exact&&error is null,maximumAbsoluteDifference,0,error);
        }
        void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,Json));
    }

    public static void RunCandidates(string halfModule,string beforeBeltModule,string calibratedModule,string output,string referenceResultsPath)
        =>RunStaticCandidates(halfModule,beforeBeltModule,calibratedModule,output,referenceResultsPath,[
            ("candidate-A-radial1.4-area1.2",LabBeltCalibrationParameters.Default with{RadialStiffnessScale=1.4,AreaScale=1.2}),
            ("candidate-B-radial1.5-area1.2",LabBeltCalibrationParameters.Default with{RadialStiffnessScale=1.5,AreaScale=1.2}),
            ("candidate-C-radial1.4-area1.4",LabBeltCalibrationParameters.Default with{RadialStiffnessScale=1.4,AreaScale=1.4})],
            "A radial1.4 area1.2; B radial1.5 area1.2; C radial1.4 area1.4; all other scales1 and resistance0; static only");

    public static void RunRefinement(string halfModule,string beforeBeltModule,string calibratedModule,string output,string referenceResultsPath)
        =>RunStaticCandidates(halfModule,beforeBeltModule,calibratedModule,output,referenceResultsPath,[
            ("probe-D-radial1.2-area2.0",LabBeltCalibrationParameters.Default with{RadialStiffnessScale=1.2,AreaScale=2}),
            ("probe-E-radial1.4-area1.4-stretch2.0",LabBeltCalibrationParameters.Default with{RadialStiffnessScale=1.4,AreaScale=1.4,BeltStretchScale=2}),
            ("probe-F-radial1.4-area1.4-bend4.0",LabBeltCalibrationParameters.Default with{RadialStiffnessScale=1.4,AreaScale=1.4,BeltBendScale=4})],
            "D radial1.2 area2.0 checks radial/area rest balance and compression; E uses C plus stretch2; F uses C plus bend4; all other scales1 and resistance0; static only");

    private static void RunStaticCandidates(string halfModule,string beforeBeltModule,string calibratedModule,string output,string referenceResultsPath,
        IReadOnlyList<(string Label,LabBeltCalibrationParameters Parameters)> parameters,string scope)
    {
        // Callers supply only explicitly authorized probes; this method does not enumerate or search parameters.
        using var referenceDocument=JsonDocument.Parse(File.ReadAllText(referenceResultsPath));
        var document=referenceDocument.RootElement;
        var hashes=document.GetProperty("ModuleHashes");
        if(hashes.GetProperty("OldHalf").GetString()!=Hash(halfModule)||hashes.GetProperty("Before").GetString()!=Hash(beforeBeltModule)||
            hashes.GetProperty("Calibrated").GetString()!=Hash(calibratedModule))
            throw new InvalidOperationException("Candidate modules differ from the recorded first-batch reference; default compatibility must be established for those exact modules first.");
        var referenceElement=document.GetProperty("Runs").EnumerateArray().Single(r=>r.GetProperty("Label").GetString()=="old-half-reference");
        var reference=JsonSerializer.Deserialize<CaseResult>(referenceElement.GetRawText(),Json)
            ??throw new InvalidOperationException("The first-batch old-half reference could not be loaded.");
        if(!reference.Completed)throw new InvalidOperationException("The first-batch old-half reference did not complete.");
        Directory.CreateDirectory(output);
        var config=DangoBodyMaterial.Configure(DangoBodyControls.Default);
        var groups=new[]{new[]{new Phase("rest",300)},
            new[]{new Phase("rest-before-S-025",300),new Phase("S-025",120,Downward:.25),new Phase("unload-025",180)},
            new[]{new Phase("rest-before-S-050",300),new Phase("S-050",120,Downward:.5),new Phase("unload-050",180)},
            new[]{new Phase("rest-before-S-100",300),new Phase("S-100",120,Downward:1),new Phase("unload-100",180)}};
        var windows=reference.Phases.ToDictionary(p=>p.Key,p=>p.Value.Frames);
        var results=new List<CaseResult>();
        foreach(var candidate in parameters)
        {
            var result=MeasureCase(candidate.Label,calibratedModule,false,candidate.Parameters,groups,config,output,windows);
            results.Add(result);
            WriteOutput(output,"candidate-progress.json",new{CompletedCandidates=results.Count,TotalCandidates=parameters.Count,Latest=result.Label,LatestCompleted=result.Completed});
        }
        var comparisons=results.Select(candidate=>
        {
            var matchedPhases=new Dictionary<string,Metrics>(candidate.Phases);
            foreach(var phase in candidate.Phases.Where(p=>p.Key.EndsWith("-reference-window",StringComparison.Ordinal)))
                matchedPhases[phase.Key[..^"-reference-window".Length]]=phase.Value;
            var matched=candidate with{Phases=matchedPhases};
            return new{candidate.Label,Rest=Shape(reference,candidate),ExtendedLoadUnload=Deformation(reference,candidate),
                ReferenceDurationWindows=Deformation(reference,matched)};
        }).ToArray();
        WriteOutput(output,"candidate-results.json",new{
            Kind="three-authorized-static-belt-candidates",Completed=results.All(r=>r.Completed),FixedDt=Dt,Substeps=config.SubStepCount,Parameters=config,
            ReferenceResultsPath=Path.GetFullPath(referenceResultsPath),ReferenceResultsSha256=Hash(referenceResultsPath),
            ModuleHashes=new{OldHalf=Hash(halfModule),Before=Hash(beforeBeltModule),Calibrated=Hash(calibratedModule)},
            Scope=scope,
            DeformationDefinition="Every load starts from fresh rest300; candidate load120/unload180, old reference load60/unload120",
            ComparisonLimit="ReferenceDurationWindows uses first60 candidate load and first120 unload; candidate unload follows120 load versus60 in old reference, so unload differences include load-duration effects",
            RestWindow="last120 of rest300; load120 whole window and final120 unload; reference-duration windows summarized separately",
            LocalContourDefinition="8 angular sectors starting at -pi, counterclockwise; pooled symmetric nearest-segment RMS distances in world units",
            Gates=new{RestHeightWidthRelativeDifference=.02,HeightRmsRatio=.7,CoreComRmsRatio=1.05,SpeedMinimum=.9,SpeedMaximum=1.1},
            Movement="not measured; no movement gate claimed",Jump="not measured; no transfer changes",Comparisons=comparisons,Runs=results,
            Acceptance="diagnostic only; no automatic tuning or manual acceptance"});
    }

    private static CaseResult MeasureCase(string label,string module,bool legacy,LabBeltCalibrationParameters? parameters,Phase[][] groups,
        DangoSelfPhysicsParameters config,string output,IReadOnlyDictionary<string,int>? referenceWindows=null)
        {
            var phases=new Dictionary<string,Metrics>();string? error=null;
            using var trace=new StreamWriter(Path.Combine(output,label+"-trace.jsonl"));
            foreach(var group in groups)
            {
                LabBeltWorld? belt=null;DangoPhysicsWorld? old=null;DangoEntityId id=default;object? lastState=null;
                try
                {
                    if(legacy){old=DangoPhysicsWorld.Create(module,1,config);old.RequireLab();old.RequireBodyMaterialModel(1);
                        if(old.LabRegionDenominator!=2||old.LabGroundModel!=DangoLabGroundModel.DiscreteNodes)throw new InvalidOperationException("Expected discrete half-region reference");
                        id=old.Add(0,0);old.SetHorizontalBounds(-256,256);}
                    else{belt=new(module,config,true,parameters);belt.SetBounds(-256,256);trace.WriteLine(JsonSerializer.Serialize(new{Schema=belt.CalibrationSchema},EvidenceJson));}
                    foreach(var phase in group)
                    {
                        Console.WriteLine($"calibration {label}: {phase.Name} starting ({phase.Frames} ticks)");
                        var samples=new List<Sample>();
                        for(var tick=0;tick<phase.Frames;tick++)
                        {
                            var jump=phase.JumpFirst&&tick==0;var active=phase.Horizontal!=0||phase.Downward!=0||phase.JumpFirst;
                            Sample sample;
                            if(belt is not null)
                            {
                                var beforeFrame=belt.Read();lastState=new{Frame=beforeFrame,Diagnostics=TryDiagnostics(belt)};
                                belt.Step(Dt,new(Horizontal:phase.Horizontal,Downward:phase.Downward,JumpPressed:jump,InputActive:active));
                                var f=belt.Read();sample=new(f.Height,f.Width,f.Area,f.CoreX,f.CoreY,f.ComX,f.ComY,f.Stats[43]/f.TotalMass,f.MaximumSpeed,Center(f.Curve,f.ComX,f.ComY));
                                lastState=new{Frame=f,Diagnostics=TryDiagnostics(belt)};
                                trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,state=lastState},EvidenceJson));
                                if(f.TotalMass<=0||f.Area<=0||f.MaximumPenetration>1e-4||f.Gap< -1e-4||f.MaximumSpeed>config.MaximumSpeed+.0002)
                                    throw new InvalidOperationException($"{phase.Name}/{tick}: mass/area/penetration/speed diagnostic failed");
                            }
                            else
                            {
                                var frame=old!.StepLab(Dt,[new(id,Horizontal:phase.Horizontal,Downward:phase.Downward,JumpPressed:jump,InputActive:active)],DangoLabDrive.DirectionRegion);
                                if(!frame.Succeeded)throw new InvalidOperationException($"{phase.Name}/{tick}: legacy native {frame.ErrorCode}");
                                var b=frame.Bodies.Single();var nodes=old.ReadLabNodes(id);var mass=nodes.Sum(n=>(double)n.Mass);
                                var cx=nodes.Sum(n=>n.X*(double)n.Mass)/mass;var cy=nodes.Sum(n=>n.Y*(double)n.Mass)/mass;
                                var vx=nodes.Sum(n=>n.VelocityX*(double)n.Mass)/mass;
                                var x=LabGroundExperiments.CurveExtent(b.Contour,0);var y=LabGroundExperiments.CurveExtent(b.Contour,1);var geometry=LabDisplayGeometry.Inspect(b.Contour);
                                sample=new(y.Maximum-y.Minimum,x.Maximum-x.Minimum,geometry.Area,b.CoreX,b.CoreY,cx,cy,vx,b.MaximumSpeed,Center(LegacyCurve(b.Contour,b.CoreX,b.CoreY),cx,cy));
                                lastState=new{sample,nodes,contour=b.Contour};trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,state=lastState},EvidenceJson));
                            }
                            if(!double.IsFinite(sample.Height+sample.Width+sample.Area+sample.CoreY+sample.ComY+sample.VelocityX))throw new InvalidOperationException("Nonfinite observation");
                            samples.Add(sample);
                            if(referenceWindows is not null&&referenceWindows.TryGetValue(phase.Name,out var referenceFrames)&&
                                referenceFrames==samples.Count&&phase.Frames>referenceFrames)
                                phases[phase.Name+"-reference-window"]=Summarize(samples);
                        }
                        phases[phase.Name]=Summarize(samples);trace.Flush();
                        WriteOutput(output,label+"-progress.json",new{Label=label,Phase=phase.Name,Completed=true,Phases=phases});
                    }
                }
                catch(Exception e)
                {
                    error=(error is null?"":error+Environment.NewLine)+e;
                    trace.WriteLine(JsonSerializer.Serialize(new{FailedGroup=group[0].Name,Error=e.ToString(),LastState=lastState,Diagnostics=belt is null?null:TryDiagnostics(belt)},EvidenceJson));trace.Flush();
                    WriteOutput(output,label+"-progress.json",new{Label=label,FailedGroup=group[0].Name,Completed=false,Error=error,Phases=phases});
                    Console.Error.WriteLine($"calibration {label}/{group[0].Name}: failed; evidence retained: {e.Message}");
                }
                finally
                {
                    try{belt?.Dispose();old?.Dispose();}catch(Exception e){error=(error??"")+" cleanup: "+e;trace.WriteLine(JsonSerializer.Serialize(new{CleanupError=e.ToString()},EvidenceJson));}
                }
            }
            return new CaseResult(label,error is null,error,parameters,phases);
        }

    private static void WriteOutput(string output,string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonSerializer.Serialize(value,Json));

    private static string Hash(string path){try{return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));}catch(Exception e){return "unavailable: "+e.Message;}}
    private static object TryDiagnostics(LabBeltWorld world){try{return world.ReadDiagnostics();}catch(Exception e){return new{Unavailable=e.Message};}}
    private static Metrics Summarize(List<Sample> samples)
    {
        var tail=samples.TakeLast(Math.Min(120,samples.Count)).ToArray();
        return new(samples.Count,tail.Average(s=>s.Height),tail.Average(s=>s.Width),tail.Average(s=>s.Area),tail.Average(s=>s.CoreY),tail.Average(s=>s.ComY),
            samples.Average(s=>Math.Abs(s.VelocityX)),samples[^1].ComX-samples[0].ComX,
            LabGroundExperiments.Analyze(samples.Select(s=>s.Height).ToArray()),LabGroundExperiments.Analyze(samples.Select(s=>s.CoreY).ToArray()),LabGroundExperiments.Analyze(samples.Select(s=>s.ComY).ToArray()),
            LabGroundExperiments.Analyze(tail.Select(s=>s.Height).ToArray()),LabGroundExperiments.Analyze(tail.Select(s=>s.CoreY).ToArray()),LabGroundExperiments.Analyze(tail.Select(s=>s.ComY).ToArray()),
            Enumerable.Range(0,512).Select(i=>(float)tail.Average(s=>s.Contour[i])).ToArray());
    }
    private static double? Ratio(double? value,double? reference)=>reference is >1e-12?value/reference:null;
    private static object Shape(CaseResult reference,CaseResult candidate)
    {
        reference.Phases.TryGetValue("rest",out var a);candidate.Phases.TryGetValue("rest",out var b);var h=Ratio(b?.Height,a?.Height);var w=Ratio(b?.Width,a?.Width);
        return new{HeightRatio=h,WidthRatio=w,AreaRatio=Ratio(b?.Area,a?.Area),CoreHeightDifference=b?.CoreY-a?.CoreY,
            RestHeightWidthPassed=h is >=.98 and <=1.02&&w is >=.98 and <=1.02,
            ComAlignedContourDistance=a is null||b is null?(double?)null:ContourDistance(a.Contour,b.Contour),
            LocalContourDistance=a is null||b is null?null:LocalContourDistance(a.Contour,b.Contour),
            LocalContourGate="reported independently; no approved threshold"};
    }
    private static object Motion(CaseResult reference,CaseResult candidate)
    {
        reference.Phases.TryGetValue("walk",out var a);candidate.Phases.TryGetValue("walk",out var b);
        var h=Ratio(b?.HeightSpectrum.FullBandRms,a?.HeightSpectrum.FullBandRms);var c=Ratio(b?.CoreSpectrum.FullBandRms,a?.CoreSpectrum.FullBandRms);var m=Ratio(b?.ComSpectrum.FullBandRms,a?.ComSpectrum.FullBandRms);
        var s=Ratio(b?.MeanAbsoluteComSpeed,a?.MeanAbsoluteComSpeed);var d=Ratio(b is null?null:Math.Abs(b.ComDistance),a is null?null:Math.Abs(a.ComDistance));
        return new{HeightRmsRatio=h,CoreRmsRatio=c,ComRmsRatio=m,SpeedRatio=s,DisplacementRatio=d,OscillationTargetPassed=h is <=.7&&c is <=1.05&&m is <=1.05,SpeedTargetPassed=s is >=.9 and <=1.1&&d is >=.9 and <=1.1};
    }
    private static object[] Deformation(CaseResult reference,CaseResult candidate)
    {
        return new[]{"025","050","100"}.Select(level=>
        {
            reference.Phases.TryGetValue("rest-before-S-"+level,out var ar);candidate.Phases.TryGetValue("rest-before-S-"+level,out var br);
            reference.Phases.TryGetValue("S-"+level,out var a);candidate.Phases.TryGetValue("S-"+level,out var b);
            reference.Phases.TryGetValue("unload-"+level,out var au);candidate.Phases.TryGetValue("unload-"+level,out var bu);
            var ac=1-Ratio(a?.Height,ar?.Height);var bc=1-Ratio(b?.Height,br?.Height);
            return (object)new{Load=double.Parse(level,System.Globalization.CultureInfo.InvariantCulture)/100,
                ReferenceCompression=ac,CandidateCompression=bc,AdditionalCompression=bc-ac,
                ReferenceLoadFrames=a?.Frames,CandidateLoadFrames=b?.Frames,ReferenceUnloadFrames=au?.Frames,CandidateUnloadFrames=bu?.Frames,
                ReferenceUnloadHeightRatio=Ratio(au?.Height,ar?.Height),CandidateUnloadHeightRatio=Ratio(bu?.Height,br?.Height),
                ReferenceLoadAreaRatio=Ratio(a?.Area,ar?.Area),CandidateLoadAreaRatio=Ratio(b?.Area,br?.Area),
                LoadContourDistance=a is null||b is null?(double?)null:ContourDistance(a.Contour,b.Contour),
                Gate="compression and unload effects reported separately; not a tuning objective"};
        }).ToArray();
    }
    private static double[] LocalContourDistance(float[] a,float[] b)
    {
        // Angular sectors in the shared COM frame; nearest-segment distance avoids sample-index correspondence assumptions.
        var sums=new double[8];var counts=new int[8];
        Accumulate(a,b);Accumulate(b,a);
        return Enumerable.Range(0,8).Select(i=>counts[i]==0?0:Math.Sqrt(sums[i]/counts[i])).ToArray();
        void Accumulate(float[] p,float[] q)
        {
            for(var i=0;i<p.Length;i+=2)
            {
                double minimum=double.PositiveInfinity;
                for(var j=0;j<q.Length;j+=2){var k=(j+2)%q.Length;var dx=q[k]-q[j];var dy=q[k+1]-q[j+1];var d=dx*dx+dy*dy;
                    var t=d>0?Math.Clamp(((p[i]-q[j])*dx+(p[i+1]-q[j+1])*dy)/d,0,1):0;
                    var x=p[i]-q[j]-t*dx;var y=p[i+1]-q[j+1]-t*dy;minimum=Math.Min(minimum,x*x+y*y);}
                var sector=Math.Min(7,(int)((Math.Atan2(p[i+1],p[i])+Math.PI)/(2*Math.PI)*8));sums[sector]+=minimum;counts[sector]++;
            }
        }
    }
    private static float[] Center(float[] curve,double x,double y)=>curve.Select((v,i)=>(float)(v-(i%2==0?x:y))).ToArray();
    private static float[] LegacyCurve(IReadOnlyList<float> controls,double x,double y)
    {
        var n=controls.Count/2;var result=new float[512];
        for(var j=0;j<256;j++){var u=j*n/256d;var i=(int)u;var t=u-i;var a=(i+n-1)%n;var c=(i+1)%n;
            for(var axis=0;axis<2;axis++)result[2*j+axis]=(float)(.5*(1-t)*(1-t)*controls[2*a+axis]+(.5+t-t*t)*controls[2*i+axis]+.5*t*t*controls[2*c+axis]+(axis==0?x:y));}
        return result;
    }
    private static double ContourDistance(float[] a,float[] b)
    {
        double Directed(float[] p,float[] q){double sum=0;for(var i=0;i<p.Length;i+=2){double minimum=double.PositiveInfinity;for(var j=0;j<q.Length;j+=2){var k=(j+2)%q.Length;var dx=q[k]-q[j];var dy=q[k+1]-q[j+1];var d=dx*dx+dy*dy;
            var t=d>0?Math.Clamp(((p[i]-q[j])*dx+(p[i+1]-q[j+1])*dy)/d,0,1):0;var x=p[i]-q[j]-t*dx;var y=p[i+1]-q[j+1]-t*dy;minimum=Math.Min(minimum,x*x+y*y);}sum+=minimum;}return Math.Sqrt(sum/(p.Length/2));}
        return Math.Max(Directed(a,b),Directed(b,a));
    }
}
