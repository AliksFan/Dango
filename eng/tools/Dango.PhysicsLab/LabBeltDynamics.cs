using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

/// <summary>Body-only finite dynamics observations; no parameter search or production changes.</summary>
public static class LabBeltDynamics
{
    private const double Dt=1d/60,StopSpeed=.02,AirGap=1e-4;
    private const int StopFrames=6,AirFrames=3;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
    private static readonly JsonSerializerOptions Compact=new(){NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals};
    private sealed record Phase(string Name,int Frames,double Horizontal=0,double Downward=0,double Stretch=0,bool Jump=false);
    private sealed record Point(double Height,double Width,double Area,double CoreY,double ComX,double ComY,double ComVx,double Gap,double Penetration,
        double ContactKEBefore,double ContactKEDelta,bool ContactKEAvailable,bool? JumpAccepted,double? ActualJumpImpulse,double MaximumSpeed);
    private sealed record PhaseResult(string Name,int Frames,Point Initial,double MeanHeight,double TailHeight,double TailWidth,double TailArea,
        double MeanAbsoluteComSpeed,double ComDistance,LabGroundExperiments.Spectrum Height,LabGroundExperiments.Spectrum Core,LabGroundExperiments.Spectrum Com,
        LabGroundExperiments.Spectrum TailHeightSpectrum,LabGroundExperiments.Spectrum TailCoreSpectrum,LabGroundExperiments.Spectrum TailComSpectrum,
        bool PenetrationPassed,bool? ContactKEPassed,double MaximumPenetration,double MaximumContactKEIncrease,
        bool? StopAchieved,int? StopFirstTick,int? StopConfirmedTick,double? StopSeconds,double? StopDistance,
        bool? JumpAccepted,double? ActualJumpImpulse,double PeakComRise,double MaximumGap,int LongestAirborneFrames,
        double? MinimumGapInLongestAirborneRun,double MaximumThreeFrameMinimumGap,bool TrulyAirbornePassed,
        double? CompressionAgainstOwnRest,object Rebound);
    private sealed record RunResult(string Label,bool Completed,string? Error,LabBeltCalibrationParameters? Calibration,bool OriginalGroundJump,
        Dictionary<string,PhaseResult> Phases);

    public static void Run(string halfModule,string calibratedV2Module,string output)
    {
        Directory.CreateDirectory(output);
        var config=DangoBodyMaterial.Configure(DangoBodyControls.Default);
        var groups=new[]{
            new[]{new Phase("movement-rest",300),new Phase("startup-right",120,Horizontal:1),new Phase("steady-right",480,Horizontal:1),new Phase("stop-right",180),
                new Phase("startup-left",120,Horizontal:-1),new Phase("steady-left",480,Horizontal:-1),new Phase("stop-left",180)},
            new[]{new Phase("compression-rest",300),new Phase("S",60,Downward:1),new Phase("S-release",180)},
            new[]{new Phase("Space-rest",300),new Phase("Space",120,Jump:true)},
            new[]{new Phase("W-Space-rest",300),new Phase("W-Space",120,Stretch:1,Jump:true)},
            new[]{new Phase("S-Space-rest",300),new Phase("S-Space",120,Downward:1,Jump:true)}};
        var runs=new List<RunResult>{Measure("old-half",halfModule,null)};
        runs.Add(Measure("belt-D-damped",calibratedV2Module,LabBeltCalibrationParameters.Default with{
            RadialStiffnessScale=1.2,AreaScale=2,OverallDamping=config.OverallDamping,GroundedBrake=35,GroundedHorizontalDamping=60}));
        runs.Add(Measure("belt-F-damped",calibratedV2Module,LabBeltCalibrationParameters.Default with{
            RadialStiffnessScale=1.4,AreaScale=1.4,BeltBendScale=4,OverallDamping=config.OverallDamping,GroundedBrake=35,GroundedHorizontalDamping=60}));
        var reference=runs[0];
        var comparisons=runs.Skip(1).Select(candidate=>new{
            candidate.Label,Rest=RestComparison(reference,candidate),Movement=new[]{"steady-right","steady-left"}.Select(name=>MovementComparison(reference,candidate,name)).ToArray(),
            Jump=new[]{"Space","W-Space","S-Space"}.Select(name=>new{Name=name,Observed=candidate.Phases.GetValueOrDefault(name),
                GatePassed=candidate.Phases.TryGetValue(name,out var phase)&&phase.JumpAccepted==true&&phase.TrulyAirbornePassed}).ToArray()}).ToArray();
        Write("dynamics-results.json",new{Kind="finite-body-dynamics",Completed=runs.All(r=>r.Completed),FixedDt=Dt,Substeps=config.SubStepCount,Parameters=config,
            ModuleHashes=new{Half=Hash(halfModule),CalibratedV2=Hash(calibratedV2Module)},
            Scope="default material; old half, damped D and damped F only; no eyes or accessories; no parameter search",
            StopDefinition="absolute true COMvx <0.02 m/s for 6 consecutive frames; first qualifying tick and confirmation tick both reported; distance is signed from release state",
            AirborneDefinition="minimum body/floor gap >1e-4 for at least3 consecutive frames; old half uses min(exact painted contour gap,node-radius gap); belt uses exact native curve gap",
            JumpDefinition="new belt opts into original whole-body grounded jump before first step; native JumpImpulse config unchanged; actual J read by native diagnostic field name; old acceptance/J unavailable in its ABI",
            Spectra="mean removed raw full-phase DFT 0.5 Hz through Nyquist, plus independent final min(120,frames) DFT; no display smoothing",
            ReboundDefinition="unsmoothed height local maxima after release; first8 positive peaks above own rest height; damping ratio is second/first amplitude when available",
            Gates=new{RestHeightWidthDifference=.02,HeightRmsRatio=.7,CoreComRmsRatio=1.05,SpeedMinimum=.9,SpeedMaximum=1.1,Penetration=1e-4,ContactKEAbsolute=1e-6,ContactKERelative=1e-5,TrueAirborneFrames=3,AdditionalCompression=.01},
            Comparisons=comparisons,Runs=runs,Acceptance="observations and individual gates only; no blanket pass or manual desktop acceptance"});

        RunResult Measure(string label,string module,LabBeltCalibrationParameters? calibration)
        {
            var phases=new Dictionary<string,PhaseResult>();string? error=null;
            using var trace=new StreamWriter(Path.Combine(output,label+"-trace.jsonl"));
            foreach(var group in groups)
            {
                LabBeltWorld? belt=null;DangoPhysicsWorld? old=null;DangoEntityId id=default;object? lastState=null;
                Dictionary<string,int>? diagnostics=null;Point? ownRest=null;DangoBodyState? legacyBody=null;
                try
                {
                    if(calibration is null)
                    {
                        old=DangoPhysicsWorld.Create(module,1,config);old.RequireLab();old.RequireBodyMaterialModel(1);
                        if(old.LabRegionDenominator!=2||old.LabGroundModel!=DangoLabGroundModel.DiscreteNodes)throw new InvalidOperationException("Expected discrete half-region reference");
                        id=old.Add(0,0);old.SetHorizontalBounds(-256,256);
                    }
                    else
                    {
                        belt=new(module,config,true,calibration);belt.SetOriginalGroundJump(true);belt.SetBounds(-256,256);
                        using var schema=JsonDocument.Parse(belt.CalibrationSchema??throw new InvalidOperationException("Missing diagnostic schema"));
                        diagnostics=schema.RootElement.GetProperty("diagnostics").EnumerateArray().Select((value,index)=>(Name:value.GetString()!,Index:index)).ToDictionary(x=>x.Name,x=>x.Index);
                        trace.WriteLine(JsonSerializer.Serialize(new{Schema=belt.CalibrationSchema,Calibration=calibration,OriginalGroundJump=belt.OriginalGroundJumpEnabled},Compact));trace.Flush();
                    }
                    foreach(var phase in group)
                    {
                        Console.WriteLine($"dynamics {label}: {phase.Name} {phase.Frames} ticks");
                        var initial=ReadPoint();var samples=new List<Point>();
                        for(var tick=0;tick<phase.Frames;tick++)
                        {
                            // Keep the preceding successful state when the native implementation rejects a step atomically.
                            _=ReadPoint();var preceding=lastState;
                            try
                            {
                                var active=phase.Horizontal!=0||phase.Downward!=0||phase.Stretch!=0||phase.Jump;
                                if(belt is not null)belt.Step(Dt,new(Horizontal:phase.Horizontal,Downward:phase.Downward,Stretch:phase.Stretch,JumpPressed:phase.Jump&&tick==0,InputActive:active));
                                else
                                {
                                    var frame=old!.StepLab(Dt,[new(id,Horizontal:phase.Horizontal,Downward:phase.Downward,Stretch:phase.Stretch,JumpPressed:phase.Jump&&tick==0,InputActive:active)],DangoLabDrive.DirectionRegion);
                                    if(!frame.Succeeded)throw new InvalidOperationException($"Legacy native step failed ({frame.ErrorCode})");
                                    legacyBody=frame.Bodies.Single();
                                }
                                var point=ReadPoint();samples.Add(point);
                                trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,state=lastState,point},Compact));trace.Flush();
                            }
                            catch(Exception e){trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,Error=e.ToString(),Preceding=preceding,LastState=lastState},Compact));trace.Flush();throw;}
                        }
                        if(phase.Name.EndsWith("-rest",StringComparison.Ordinal))ownRest=Average(samples.TakeLast(120).ToArray());
                        phases[phase.Name]=Summarize(phase,initial,samples,ownRest);
                        Write(label+"-progress.json",new{Label=label,Phase=phase.Name,Group=group[0].Name,Completed=true,Phases=phases});
                    }
                }
                catch(Exception e)
                {
                    error=(error is null?"":error+Environment.NewLine)+e;
                    trace.WriteLine(JsonSerializer.Serialize(new{FailedGroup=group[0].Name,Error=e.ToString(),LastState=lastState},Compact));trace.Flush();
                    Write(label+"-progress.json",new{Label=label,FailedGroup=group[0].Name,Completed=false,Error=error,Phases=phases});
                    Console.Error.WriteLine($"dynamics {label}/{group[0].Name}: {e.Message}; evidence retained");
                }
                finally
                {
                    try{belt?.Dispose();old?.Dispose();}catch(Exception e){error=(error??"")+" cleanup: "+e;trace.WriteLine(JsonSerializer.Serialize(new{CleanupError=e.ToString()},Compact));trace.Flush();}
                }

                Point ReadPoint()
                {
                    Point point;
                    if(belt is not null)
                    {
                        var f=belt.Read();var diag=belt.ReadDiagnostics();lastState=new{Frame=f,Diagnostics=diag};
                        double Get(string name)=>diag[diagnostics![name]];
                        point=new(f.Height,f.Width,f.Area,f.CoreY,f.ComX,f.ComY,f.Stats[43]/f.TotalMass,f.Gap,f.MaximumPenetration,
                            Get("contactKEBeforeSum"),Get("contactKEDelta"),true,Get("jumpAccepted")!=0,Get("jumpActualImpulseY"),f.MaximumSpeed);
                    }
                    else
                    {
                        var nodes=old!.ReadLabNodes(id);var ground=old.ReadLabGroundStats(id);
                        var contour=legacyBody?.Contour??nodes.Skip(1).SelectMany(n=>new[]{n.X-nodes[0].X,n.Y-nodes[0].Y}).ToArray();
                        var coreY=legacyBody?.CoreY??nodes[0].Y;
                        var mass=nodes.Sum(n=>(double)n.Mass);var cx=nodes.Sum(n=>n.X*(double)n.Mass)/mass;var cy=nodes.Sum(n=>n.Y*(double)n.Mass)/mass;
                        var vx=nodes.Sum(n=>n.VelocityX*(double)n.Mass)/mass;
                        var x=LabGroundExperiments.CurveExtent(contour,0);var y=LabGroundExperiments.CurveExtent(contour,1);var geometry=LabDisplayGeometry.Inspect(contour);
                        var gap=Math.Min(coreY+y.Minimum-config.GroundHeight,nodes.Skip(1).Min(n=>(double)n.Y)-config.NodeRadius-config.GroundHeight);
                        var before=ground is null?0:ground.NormalEnergyBefore+ground.FrictionEnergyBefore;
                        var delta=ground is null?0:ground.NormalEnergyAfter+ground.FrictionEnergyAfter-before;
                        point=new(y.Maximum-y.Minimum,x.Maximum-x.Minimum,geometry.Area,coreY,cx,cy,vx,gap,ground?.MaximumPenetrationAfterSolve??Math.Max(0,-gap),before,delta,ground is not null,null,null,legacyBody?.MaximumSpeed??nodes.Max(n=>Math.Sqrt(n.VelocityX*n.VelocityX+n.VelocityY*n.VelocityY)));
                        lastState=new{Body=legacyBody,Contour=contour,Nodes=nodes,Ground=ground,Point=point,ContactKEAvailable=ground is not null};
                    }
                    if(!double.IsFinite(point.Height+point.Width+point.Area+point.CoreY+point.ComX+point.ComY+point.ComVx+point.Gap+point.ContactKEDelta)||point.Area<=0)
                        throw new InvalidOperationException("Nonfinite or nonpositive observed geometry");
                    return point;
                }
            }
            return new(label,error is null,error,calibration,calibration is not null,phases);
        }
        void Write(string file,object data)=>File.WriteAllText(Path.Combine(output,file),JsonSerializer.Serialize(data,Json));
    }

    private static Point Average(Point[] points)=>new(points.Average(p=>p.Height),points.Average(p=>p.Width),points.Average(p=>p.Area),points.Average(p=>p.CoreY),
        points.Average(p=>p.ComX),points.Average(p=>p.ComY),points.Average(p=>p.ComVx),points.Average(p=>p.Gap),points.Max(p=>p.Penetration),0,0,false,null,null,points.Max(p=>p.MaximumSpeed));
    private static PhaseResult Summarize(Phase phase,Point initial,List<Point> samples,Point? rest)
    {
        var tail=samples.TakeLast(Math.Min(120,samples.Count)).ToArray();int? stopFirst=null,stopConfirmed=null;double? stopDistance=null;
        var consecutiveStop=0;var airborne=0;var longest=0;double runMinimum=double.PositiveInfinity;double? longestMinimum=null;double threeMinimum=0;
        for(var tick=0;tick<samples.Count;tick++)
        {
            if(Math.Abs(samples[tick].ComVx)<StopSpeed)consecutiveStop++;else consecutiveStop=0;
            if(stopFirst is null&&consecutiveStop>=StopFrames){stopFirst=tick-StopFrames+1;stopConfirmed=tick;stopDistance=samples[stopFirst.Value].ComX-initial.ComX;}
            if(samples[tick].Gap>AirGap){airborne++;runMinimum=Math.Min(runMinimum,samples[tick].Gap);if(airborne>longest){longest=airborne;longestMinimum=runMinimum;}}
            else{airborne=0;runMinimum=double.PositiveInfinity;}
            if(tick>=2)threeMinimum=Math.Max(threeMinimum,Math.Min(samples[tick].Gap,Math.Min(samples[tick-1].Gap,samples[tick-2].Gap)));
        }
        var peaks=Enumerable.Range(1,Math.Max(0,samples.Count-2)).Where(i=>samples[i].Height>samples[i-1].Height&&samples[i].Height>=samples[i+1].Height&&samples[i].Height>(rest?.Height??initial.Height))
            .Take(8).Select(i=>new{Tick=i,Seconds=(i+1)*Dt,Height=samples[i].Height,Amplitude=samples[i].Height-(rest?.Height??initial.Height)}).ToArray();
        object rebound=new{Peaks=peaks,FirstPositivePeak=peaks.FirstOrDefault(),SecondToFirstAmplitudeRatio=peaks.Length>=2?(double?)(peaks[1].Amplitude/peaks[0].Amplitude):null,
            MaximumHeight=samples.Max(p=>p.Height),MinimumHeight=samples.Min(p=>p.Height)};
        return new(phase.Name,samples.Count,initial,samples.Average(p=>p.Height),tail.Average(p=>p.Height),tail.Average(p=>p.Width),tail.Average(p=>p.Area),
            samples.Average(p=>Math.Abs(p.ComVx)),samples[^1].ComX-initial.ComX,
            Spectrum(samples.Select(p=>p.Height)),Spectrum(samples.Select(p=>p.CoreY)),Spectrum(samples.Select(p=>p.ComY)),
            Spectrum(tail.Select(p=>p.Height)),Spectrum(tail.Select(p=>p.CoreY)),Spectrum(tail.Select(p=>p.ComY)),
            samples.All(p=>p.Penetration<=1e-4&&p.Gap>=-1e-4),samples.All(p=>p.ContactKEAvailable)?samples.All(p=>p.ContactKEDelta<=1e-6+Math.Abs(p.ContactKEBefore)*1e-5):null,samples.Max(p=>p.Penetration),samples.Max(p=>p.ContactKEDelta),
            phase.Name.StartsWith("stop",StringComparison.Ordinal)?stopFirst is not null:null,
            phase.Name.StartsWith("stop",StringComparison.Ordinal)?stopFirst:null,phase.Name.StartsWith("stop",StringComparison.Ordinal)?stopConfirmed:null,
            phase.Name.StartsWith("stop",StringComparison.Ordinal)&&stopFirst is not null?(stopFirst.Value+1)*Dt:null,phase.Name.StartsWith("stop",StringComparison.Ordinal)?stopDistance:null,
            samples.Any(p=>p.JumpAccepted is not null)?samples.Any(p=>p.JumpAccepted==true):null,
            samples.Any(p=>p.ActualJumpImpulse is not null)?samples.Sum(p=>p.ActualJumpImpulse??0):null,
            samples.Max(p=>p.ComY)-initial.ComY,samples.Max(p=>p.Gap),longest,longestMinimum,threeMinimum,longest>=AirFrames,
            rest is null?null:1-samples.Average(p=>p.Height)/rest.Height,rebound);
    }
    private static LabGroundExperiments.Spectrum Spectrum(IEnumerable<double> values)=>LabGroundExperiments.Analyze(values.ToArray());
    private static object RestComparison(RunResult reference,RunResult candidate)
    {
        reference.Phases.TryGetValue("movement-rest",out var a);candidate.Phases.TryGetValue("movement-rest",out var b);
        var h=Ratio(b?.TailHeight,a?.TailHeight);var w=Ratio(b?.TailWidth,a?.TailWidth);
        return new{HeightRatio=h,WidthRatio=w,AreaRatio=Ratio(b?.TailArea,a?.TailArea),HeightWidthPassed=h is >=.98 and <=1.02&&w is >=.98 and <=1.02};
    }
    private static object MovementComparison(RunResult reference,RunResult candidate,string name)
    {
        reference.Phases.TryGetValue(name,out var a);candidate.Phases.TryGetValue(name,out var b);
        var h=Ratio(b?.Height.FullBandRms,a?.Height.FullBandRms);var c=Ratio(b?.Core.FullBandRms,a?.Core.FullBandRms);var m=Ratio(b?.Com.FullBandRms,a?.Com.FullBandRms);
        var speed=Ratio(b?.MeanAbsoluteComSpeed,a?.MeanAbsoluteComSpeed);var distance=Ratio(b is null?null:Math.Abs(b.ComDistance),a is null?null:Math.Abs(a.ComDistance));
        return new{Name=name,HeightRmsRatio=h,CoreRmsRatio=c,ComRmsRatio=m,SpeedRatio=speed,DistanceRatio=distance,
            HeightRmsPassed=h is <=.7,CoreComPassed=c is <=1.05&&m is <=1.05,SpeedDistancePassed=speed is >=.9 and <=1.1&&distance is >=.9 and <=1.1,
            ReferenceCompression=a?.CompressionAgainstOwnRest,CandidateCompression=b?.CompressionAgainstOwnRest,AdditionalCompression=b?.CompressionAgainstOwnRest-a?.CompressionAgainstOwnRest,
            AdditionalCompressionPassed=b?.CompressionAgainstOwnRest-a?.CompressionAgainstOwnRest is <=.01};
    }
    private static double? Ratio(double? a,double? b)=>b is >1e-12?a/b:null;
    private static string Hash(string module)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module)));
}
