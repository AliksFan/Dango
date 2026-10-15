using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

/// <summary>Failure-preserving physical observations. No display filter or arc candidate is run.</summary>
public static class LabBeltExperiments
{
    private static readonly double Dt=TimeSpan.FromSeconds(1d/60).TotalSeconds;
    private sealed record Phase(string Name,int Frames,double Horizontal=0,double Stretch=0,double Downward=0,bool JumpFirst=false);
    private sealed record Sample(double Height,double Width,double Area,double CoreY,double ComX,double ComY,double ComVelocityX,double Speed,double Gap);
    private sealed record TailStateMetrics(int Frames,LabGroundExperiments.Spectrum Height,LabGroundExperiments.Spectrum CoreY,
        LabGroundExperiments.Spectrum ComY,double MeanAbsoluteComSpeed,double MaximumSpeed);
    private sealed record Observation(string Name,int Frames,double MeanHeight,double TailHeight,double TailWidth,double TailArea,
        double ComDistance,double MeanComSpeed,double MaximumSpeed,double MinimumGap,
        LabGroundExperiments.Spectrum Height,LabGroundExperiments.Spectrum CoreY,LabGroundExperiments.Spectrum ComY,TailStateMetrics TailWindow,float[] TailContour);
    private sealed record Result(string Label,string? Sha256,bool Completed,string? Error,IReadOnlyList<Observation> Phases);

    public static void Run(string baselineModule,string halfModule,string beltModule,string output)
    {
        Directory.CreateDirectory(output);
        var p=DangoBodyMaterial.Configure(DangoBodyControls.Default);
        var runs=new[] { Measure("old-core",baselineModule,false,false,DangoLabDrive.Core),
            Measure("old-half",halfModule,false,false,DangoLabDrive.DirectionRegion),
            Measure("belt-half",beltModule,true,true,DangoLabDrive.DirectionRegion),
            Measure("belt-half-short-bones-off-rest",beltModule,true,false,DangoLabDrive.DirectionRegion) };
        var comparisons=new[] { Compare(runs[0],runs[2]),Compare(runs[1],runs[2]) };
        var shapes=new[] { Shape(runs[1],runs[2]),Shape(runs[1],runs[3]),Shape(runs[2],runs[3]) };
        File.WriteAllText(Path.Combine(output,"belt-results.json"),JsonSerializer.Serialize(new {
            Kind="continuous-periodic-modal-belt",Parameters=p,FixedDt=Dt,Completed=runs.All(r=>r.Completed),
            DisplaySmoothing=false,Gait=false,ManualAcceptance=false,
            Geometry="belt: 256 actual Fourier curve samples, native exact extrema/area; old: quadratic B-spline sampled only for legacy contour comparison",
            Mass="belt: native integrated real COM slots 4/5, attachments have no mass; old: actual native body masses",
            Spectrum="mean removed DFT, entire 0.5 Hz through Nyquist band; no detrending or display smoothing",
            Velocity="MeanComSpeed averages absolute native horizontal COM velocity per frame; displacement is independent",
            RestWindow="TailWindow uses the final min(120, phase frames); rest spectra exclude initial drop",
            Scope="one default-material body, flat floor, half-region belt only; no belt core mode exists",
            ContactLimitations="exported contact ledger is per step/substep aggregate; per-atomic-update proof belongs to native contracts",
            Targets=new { RestHeightWidthRelativeDifference=.02,HeightRmsRatio=.7,CoreComRmsRatio=1.05,DisplacementSpeedMinimum=.9,DisplacementSpeedMaximum=1.1,AdditionalCompression=.01,MaximumPenetration=1e-4 },
            Shapes=shapes,Comparisons=comparisons,Runs=runs },new JsonSerializerOptions {WriteIndented=true}));
        if(runs.Any(r=>!r.Completed))throw new InvalidOperationException("Belt batch failed; partial traces, errors and comparisons retained.");

        Result Measure(string label,string module,bool belt,bool shortBones,DangoLabDrive drive)
        {
            var observations=new List<Observation>();string? error=null,hash=null;
            var trace=new StreamWriter(Path.Combine(output,label+"-trace.jsonl"));
            DangoPhysicsWorld? old=null;LabBeltWorld? current=null;DangoEntityId id=default;
            try
            {
                hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(module)));
                Reset();
                Observe(new("rest",300));
                if(!(belt&&!shortBones))
                {
                foreach(var phase in new[] {new Phase("startup-right",120,1),new("steady-right",480,1),new("release-right",180),
                    new("startup-left",120,-1),new("steady-left",480,-1),new("release-left",180)})Observe(phase);
                Reset();
                foreach(var phase in new[] {new Phase("action-rest",300),new("W",60,Stretch:1),new("W-release",120),new("S",60,Downward:1),
                    new("S-release",120),new("Space",120,JumpFirst:true),new("W-Space",120,Stretch:1,JumpFirst:true),
                    new("settle-before-S-Space",180),new("S-Space",120,Downward:1,JumpFirst:true),new("final-rest",180)})Observe(phase);
                }
            }
            catch(Exception e)
            {
                error=e.ToString();
                try{trace.WriteLine(JsonSerializer.Serialize(new{Error=error}));trace.Flush();}
                catch(Exception logError){error+=Environment.NewLine+"Failure evidence write failure: "+logError;}
                Console.Error.WriteLine(label+": "+error);
            }
            finally
            {
                Cleanup(current);Cleanup(old);Cleanup(trace);
            }
            return new(label,hash,error is null,error,observations);

            void Cleanup(IDisposable? world)
            {
                try{world?.Dispose();}
                catch(Exception cleanupError)
                {
                    var detail="Cleanup failure: "+cleanupError;
                    error=error is null?detail:error+Environment.NewLine+detail;
                    Console.Error.WriteLine(label+": "+detail);
                    try{if(!ReferenceEquals(world,trace)){trace.WriteLine(JsonSerializer.Serialize(new{CleanupError=cleanupError.ToString()}));trace.Flush();}}
                    catch(Exception logError){error+=Environment.NewLine+"Cleanup evidence write failure: "+logError;}
                }
            }
            void Reset()
            {
                current?.Dispose();current=null;old?.Dispose();old=null;
                if(belt){current=new(module,p,shortBones);current.SetBounds(-256,256);}
                else
                {
                    old=DangoPhysicsWorld.Create(module,1,p);old.RequireLab();old.RequireBodyMaterialModel(1);
                    if(old.LabGroundModel!=DangoLabGroundModel.DiscreteNodes)throw new InvalidOperationException("Expected discrete legacy module");
                    if(drive==DangoLabDrive.DirectionRegion&&old.LabRegionDenominator!=2)throw new InvalidOperationException("Expected half-region module");
                    id=old.Add(0,0);old.SetHorizontalBounds(-256,256);
                }
            }
            void Observe(Phase phase)
            {
                var samples=new List<Sample>();float[] tail=[];
                for(var tick=0;tick<phase.Frames;tick++)
                {
                    var jump=phase.JumpFirst&&tick==0;var active=phase.Horizontal!=0||phase.Stretch!=0||phase.Downward!=0||phase.JumpFirst;
                    Sample sample;
                    if(current is not null)
                    {
                        // Log the pre-step state as well, so an atomically rejected native step leaves useful evidence.
                        var before=current.Read();
                        try {current.Step(Dt,new(phase.Horizontal,phase.Stretch,phase.Downward,jump,InputActive:active));}
                        catch(Exception e){trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,rejected=e.ToString(),before}));trace.Flush();throw;}
                        var f=current.Read();tail=Center(f.Curve,f.ComX,f.ComY);
                        trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,frame=f}));
                        if(f.TotalMass<=0||f.Area<=0||f.Stats[39]!=0||f.Stats[37]!=1||f.ShortBones!=shortBones)
                            throw new InvalidOperationException($"{phase.Name}/{tick}: model facts");
                        if(f.MaximumPenetration>1e-4||f.Gap < -1e-4||f.MaximumSpeed>p.MaximumSpeed+.0002)
                            throw new InvalidOperationException($"{phase.Name}/{tick}: penetration/speed");
                        if(f.ContactEnergyChange>1e-6+Math.Abs(f.Stats[26])*1e-5||f.Stats[33]<-1e-8||f.Stats[53]>1e-5||f.Stats[30]>2e-6)
                            throw new InvalidOperationException($"{phase.Name}/{tick}: dissipation/friction/convergence");
                        if(f.MinimumX < -256||f.MaximumX>256||SelfIntersects(f.Curve))throw new InvalidOperationException($"{phase.Name}/{tick}: bounds/topology");
                        sample=new(f.Height,f.Width,f.Area,f.CoreY,f.ComX,f.ComY,f.Stats[43]/f.TotalMass,f.MaximumSpeed,f.Gap);
                    }
                    else
                    {
                        var frame=old!.StepLab(Dt,[new(id,Horizontal:phase.Horizontal,Stretch:phase.Stretch,Downward:phase.Downward,
                            JumpPressed:jump,InputActive:active)],drive);
                        if(!frame.Succeeded)throw new InvalidOperationException($"{phase.Name}/{tick}: legacy native {frame.ErrorCode}");
                        var b=frame.Bodies.Single();var nodes=old.ReadLabNodes(id);
                        if(nodes.Any(n=>!float.IsFinite(n.X+n.Y+n.Mass)||n.Mass<=0))throw new InvalidOperationException("Legacy mass/nonfinite state");
                        var mass=nodes.Sum(n=>(double)n.Mass);var cx=nodes.Sum(n=>n.X*(double)n.Mass)/mass;var cy=nodes.Sum(n=>n.Y*(double)n.Mass)/mass;
                        var comVx=nodes.Sum(n=>n.VelocityX*(double)n.Mass)/mass;
                        var x=LabGroundExperiments.CurveExtent(b.Contour,0);var y=LabGroundExperiments.CurveExtent(b.Contour,1);
                        var geometry=LabDisplayGeometry.Inspect(b.Contour);
                        if(nodes.Skip(1).Min(n=>n.Y)-p.NodeRadius<p.GroundHeight-2*p.NodeRadius || b.CoreX+x.Minimum < -256 || b.CoreX+x.Maximum > 256)throw new InvalidOperationException("Legacy penetration/bounds");
                        if(geometry.Area<=0||geometry.SelfIntersects||b.MaximumSpeed>p.MaximumSpeed+.0002)throw new InvalidOperationException("Legacy topology/speed");
                        tail=Center(LegacyCurve(b.Contour,b.CoreX,b.CoreY),cx,cy);
                        sample=new(y.Maximum-y.Minimum,x.Maximum-x.Minimum,geometry.Area,b.CoreY,cx,cy,comVx,b.MaximumSpeed,b.CoreY+y.Minimum-p.GroundHeight);
                        trace.WriteLine(JsonSerializer.Serialize(new{phase=phase.Name,tick,sample,nodes,contour=b.Contour}));
                    }
                    samples.Add(sample);if(tick%30==0)trace.Flush();
                }
                trace.Flush();var last=samples.TakeLast(Math.Min(120,samples.Count)).ToArray();
                var distance=samples[^1].ComX-samples[0].ComX;
                observations.Add(new(phase.Name,samples.Count,samples.Average(s=>s.Height),last.Average(s=>s.Height),last.Average(s=>s.Width),last.Average(s=>s.Area),
                    distance,samples.Average(s=>Math.Abs(s.ComVelocityX)),samples.Max(s=>s.Speed),samples.Min(s=>s.Gap),
                    LabGroundExperiments.Analyze(samples.Select(s=>s.Height).ToArray()),LabGroundExperiments.Analyze(samples.Select(s=>s.CoreY).ToArray()),
                    LabGroundExperiments.Analyze(samples.Select(s=>s.ComY).ToArray()),
                    new TailStateMetrics(last.Length,LabGroundExperiments.Analyze(last.Select(s=>s.Height).ToArray()),
                        LabGroundExperiments.Analyze(last.Select(s=>s.CoreY).ToArray()),LabGroundExperiments.Analyze(last.Select(s=>s.ComY).ToArray()),
                        last.Average(s=>Math.Abs(s.ComVelocityX)),last.Max(s=>s.Speed)),tail));
                File.WriteAllText(Path.Combine(output,label+"-progress.json"),JsonSerializer.Serialize(observations));
                Console.WriteLine($"{label}: {phase.Name} {samples.Count} frames recorded");
            }
        }
    }

    private static double? Ratio(double? a,double? b)=>b is >1e-12?a/b:null;
    private static object Compare(Result baseline,Result candidate)
    {
        var ar=baseline.Phases.FirstOrDefault(p=>p.Name=="rest");var br=candidate.Phases.FirstOrDefault(p=>p.Name=="rest");
        return new {Baseline=baseline.Label,Candidate=candidate.Label,Phases=new[]{"steady-right","steady-left"}.Select(name=> {
            var a=baseline.Phases.FirstOrDefault(p=>p.Name==name);var b=candidate.Phases.FirstOrDefault(p=>p.Name==name);
            var hr=Ratio(b?.Height.FullBandRms,a?.Height.FullBandRms);var cr=Ratio(b?.CoreY.FullBandRms,a?.CoreY.FullBandRms);var mr=Ratio(b?.ComY.FullBandRms,a?.ComY.FullBandRms);
            var dr=Ratio(b is null?null:Math.Abs(b.ComDistance),a is null?null:Math.Abs(a.ComDistance));var sr=Ratio(b?.MeanComSpeed,a?.MeanComSpeed);
            double? compression=a is not null&&b is not null&&ar is not null&&br is not null ? Math.Max(0,1-b.MeanHeight/br.TailHeight)-Math.Max(0,1-a.MeanHeight/ar.TailHeight):null;
            return new{Name=name,HeightRmsRatio=hr,CoreRmsRatio=cr,ComRmsRatio=mr,DistanceRatio=dr,SpeedRatio=sr,AdditionalCompression=compression,
                QuantitativeImprovementTarget=hr is <=.7&&cr is <=1.05&&mr is <=1.05,SpeedAndShapeCostPassed=dr is >=.9 and <=1.1&&sr is >=.9 and <=1.1&&compression is <=.01};
        }).ToArray()};
    }
    private static object Shape(Result baseline,Result candidate)
    {
        var a=baseline.Phases.FirstOrDefault(p=>p.Name=="rest");var b=candidate.Phases.FirstOrDefault(p=>p.Name=="rest");
        var h=Ratio(b?.TailHeight,a?.TailHeight);var w=Ratio(b?.TailWidth,a?.TailWidth);
        return new{Baseline=baseline.Label,Candidate=candidate.Label,HeightRatio=h,WidthRatio=w,AreaRatio=Ratio(b?.TailArea,a?.TailArea),
            RestHeightWidthTarget=h is >=.98 and <=1.02&&w is >=.98 and <=1.02,
            ComAlignedSymmetricContourDistance=a is null||b is null?(double?)null:ContourDistance(a.TailContour,b.TailContour),
            LocalContourThreshold="reported separately; no approved numerical pass threshold"};
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
    private static bool SelfIntersects(float[] p)
    {
        var n=p.Length/2;
        double Cross(int a,int b,int c)=>(p[2*b]-p[2*a])*(double)(p[2*c+1]-p[2*a+1])-(p[2*b+1]-p[2*a+1])*(double)(p[2*c]-p[2*a]);
        for(var i=0;i<n;i++)for(var j=i+2;j<n;j++){if(i==0&&j==n-1)continue;var a=(i+1)%n;var b=(j+1)%n;
            if(Cross(i,a,j)*Cross(i,a,b)<-1e-14&&Cross(j,b,i)*Cross(j,b,a)<-1e-14)return true;}
        return false;
    }
}
