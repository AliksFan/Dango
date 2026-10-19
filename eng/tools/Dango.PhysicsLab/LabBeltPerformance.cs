using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Dango.Desktop.Physics.Tuning;

namespace Dango.PhysicsLab;

/// <summary>Paired deterministic native comparison; never substitutes for the unchanged guard acceptance suite.</summary>
internal static class LabBeltPerformance
{
    private const double Dt=1d/60;
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private sealed record Phase(string Name,int Frames,double Horizontal=0,double Down=0,double Up=0,bool Jump=false,bool Drag=false);
    private static readonly Phase[] Phases=[new("rest",300),new("right",180,Horizontal:1),new("stop-right",180),
        new("left",180,Horizontal:-1),new("stop-left",180),new("S",60,Down:1),new("S-release",120),
        new("W",120,Up:1),new("W-release",120),new("Space",180,Jump:true),new("drag-compress",90,Drag:true),new("drag-release",180)];
    private static readonly string[] Buckets=["step-total","integrate","residuals","objective-matrix","objective-value",
        "guard-newton","guard-integrated","guard-ground","guard-initial","contact-total","bounds","roots",
        "cholesky","speed-certificate","frame-stats","forces"];

    public static int Run(string root,string[] args)
    {
        string? Option(string prefix)=>args.FirstOrDefault(x=>x.StartsWith(prefix,StringComparison.Ordinal))?[prefix.Length..];
        var allowed=new[]{"--baseline=","--comparison=","--output=","--corner="};
        if(args.Count(x=>x=="--belt-performance")!=1 ||
            args.Any(x=>x!="--belt-performance" && !allowed.Any(prefix=>x.StartsWith(prefix,StringComparison.Ordinal))) ||
            allowed.Any(prefix=>args.Count(x=>x.StartsWith(prefix,StringComparison.Ordinal))>1))
            throw new ArgumentException("Use --belt-performance [--baseline=<absolute dll>] [--comparison=<absolute dll>] [--output=<absolute directory>] [--corner=120,100,0].");
        string Absolute(string? value,string fallback)
        {
            if(value is null) return Path.Combine(root,fallback);
            if(!Path.IsPathFullyQualified(value)) throw new ArgumentException("Explicit paths must be absolute.");
            return Path.GetFullPath(value);
        }
        var baseline=Absolute(Option("--baseline="),"artifacts/physics-lab/belt-rear-pressure-native/Release/dango_physics_lab_belt.dll");
        var comparison=Absolute(Option("--comparison="),"artifacts/physics-lab/belt-perf-profile-native/Release/dango_physics_lab_belt.dll");
        var output=Absolute(Option("--output="),"artifacts/physics-lab/sessions/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-performance-"+Guid.NewGuid().ToString("N")[..8]);
        var corner=(Option("--corner=")??"120,100,0").Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();
        if(corner.Length!=3 || corner[0] is not (80 or 120) || corner[1] is not (0 or 100) || corner[2] is not (0 or 100))
            throw new ArgumentException("Only the eight approved 80/120 x 0/100 x 0/100 corners are supported.");
        if(Path.GetFullPath(baseline).Equals(Path.GetFullPath(comparison),StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Baseline and comparison must be independent DLL paths.");
        if(Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Performance output directory must be new or empty.");
        Directory.CreateDirectory(output);
        var controls=new DangoBodyControls(corner[0],corner[1],corner[2]);
        var config=DangoBodyMaterial.Configure(controls);
        var calibration=LabBeltCalibrationParameters.Default with {RadialStiffnessScale=1.4,AreaScale=1.4,BeltBendScale=4,
            OverallDamping=config.OverallDamping,GroundedBrake=35,GroundedHorizontalDamping=60};
        var phaseReports=new List<object>(); var tick=0; string phaseName="create"; var phaseTick=-1;
        var baselineTimes=new List<double>();var comparisonTimes=new List<double>();
        string? error=null; object? mismatch=null; bool profileAvailable=false;
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var baselineHash=Hash(baseline); var comparisonHash=Hash(comparison);
        LabBeltWorld Create(string path)
        {
            var world=new LabBeltWorld(path,config,true,calibration,enableShapeGuard:true,enableSpeedSaturation:true,enableRadialDrive:true);
            try {world.SetOriginalGroundJump();world.SetBounds(-256,256);return world;}
            catch {world.Dispose();throw;}
        }
        bool Compare(LabBeltWorld a,LabBeltWorld b)
        {
            var left=a.ReadPerformanceState();var right=b.ReadPerformanceState();
            foreach(var (name,bytes) in left)
            {
                if(bytes.AsSpan().SequenceEqual(right[name])) continue;
                var offset=Enumerable.Range(0,bytes.Length).First(i=>bytes[i]!=right[name][i]);
                mismatch=new {Tick=tick,Phase=phaseName,PhaseTick=phaseTick,Channel=name,ByteOffset=offset,
                    BaselineByte=bytes[offset],ComparisonByte=right[name][offset]};
                File.WriteAllText(Path.Combine(output,"first-mismatch.json"),JsonSerializer.Serialize(mismatch,Json));
                foreach(var (channel,data) in left) File.WriteAllBytes(Path.Combine(output,"mismatch-baseline-"+channel+".bin"),data);
                foreach(var (channel,data) in right) File.WriteAllBytes(Path.Combine(output,"mismatch-comparison-"+channel+".bin"),data);
                return false;
            }
            return true;
        }
        try
        {
            using var a=Create(baseline);using var b=Create(comparison);
            profileAvailable=b.ProfileAvailable;
            if(profileAvailable) b.ResetProfile();
            if(!Compare(a,b)) throw new InvalidOperationException("Initial native state differs bitwise.");
            using var trace=new StreamWriter(Path.Combine(output,"step-timings.jsonl"));
            foreach(var phase in Phases)
            {
                phaseName=phase.Name;phaseTick=-1;
                var initial=a.Read();var dragX=initial.ComX;var dragY=config.GroundHeight+.05*initial.Height;
                if(profileAvailable) b.ResetProfile();
                var aTimes=new List<double>();var bTimes=new List<double>();var before=profileAvailable?b.ReadProfile():null;
                for(var i=0;i<phase.Frames;i++)
                {
                    phaseTick=i;
                    var input=new LabBeltInput(Horizontal:phase.Horizontal,Downward:phase.Down,Stretch:phase.Up,
                        JumpPressed:phase.Jump&&i==0,DragActive:phase.Drag,DragTargetX:dragX,DragTargetY:dragY,
                        InputActive:phase.Horizontal!=0||phase.Down!=0||phase.Up!=0||phase.Drag||phase.Jump&&i==0);
                    // Alternate order to reduce systematic cache/order bias; reads and all I/O remain outside timing.
                    (double Ms,string? Error) Step(LabBeltWorld world)
                    {
                        var start=Stopwatch.GetTimestamp();
                        try {world.Step(Dt,input);return (Stopwatch.GetElapsedTime(start).TotalMilliseconds,null);}
                        catch(Exception e) {return (Stopwatch.GetElapsedTime(start).TotalMilliseconds,e.Message);}
                    }
                    (double Ms,string? Error) at,bt;
                    if(tick%2==0){at=Step(a);bt=Step(b);}else{bt=Step(b);at=Step(a);}
                    tick++;aTimes.Add(at.Ms);bTimes.Add(bt.Ms);baselineTimes.Add(at.Ms);comparisonTimes.Add(bt.Ms);
                    trace.WriteLine(JsonSerializer.Serialize(new{Tick=tick,Phase=phase.Name,PhaseTick=i,Input=input,
                        BaselineMilliseconds=at.Ms,ComparisonMilliseconds=bt.Ms,BaselineError=at.Error,ComparisonError=bt.Error}));
                    if(!Compare(a,b)) throw new InvalidOperationException("Native state differs bitwise.");
                    if(at.Error!=null || bt.Error!=null) throw new InvalidOperationException($"Step rejected; baseline={at.Error}; comparison={bt.Error}");
                }
                var after=profileAvailable?b.ReadProfile():null;
                phaseReports.Add(new{phase.Name,phase.Frames,Baseline=Timing(aTimes),Comparison=Timing(bTimes),
                    ProfileInclusiveBuckets=after is null?null:Buckets.Select((name,i)=>new{Name=name,Milliseconds=after[2+i]-before![2+i],Calls=after[18+i]-before![18+i]}).ToArray()});
                Console.WriteLine($"{phase.Name}: {tick}/1890 bitwise equal");
            }
        }
        catch(Exception e){error=e.ToString();}
        var passed=error is null && tick==1890;
        File.WriteAllText(Path.Combine(output,"performance-results.json"),JsonSerializer.Serialize(new{
            Kind="paired-native-belt-performance",Passed=passed,BitwiseEquivalent=passed,CompletedTicks=tick,ExpectedTicks=1890,
            Baseline=baseline,BaselineSha256=baselineHash,Comparison=comparison,ComparisonSha256=comparisonHash,
            Controls=controls,Parameters=config,Calibration=calibration,FixedDt=Dt,Substeps=4,ProfileAvailable=profileAvailable,
            Timing="Stopwatch around Step only; alternating paired order; reads/output excluded; inclusive nested profile buckets must not be summed; no warm-up omitted",
            Scope="Eight radial points; Fy 2/3; rear pressure; no gait; unchanged guard gates require separate guard runner",
            ByteComparison="MemoryMarshal.AsBytes on all scaffold/curve/stats/calibration diagnostics/shape/debug/solver/speed/radial/rear/ledger fields; signed zero and NaN payloads preserved",
            OverallTimingMilliseconds=new{Baseline=Timing(baselineTimes),Comparison=Timing(comparisonTimes)},
            Phases=phaseReports,Error=error,FirstMismatch=mismatch,PhaseAtEnd=phaseName,PhaseTick=phaseTick},Json));
        Console.WriteLine($"{(passed?"PASS":"FAIL")} belt performance: {output}");
        return passed?0:10;
    }
    private static object Timing(List<double> samples)
    {
        var sorted=samples.Order().ToArray();
        double Percentile(double p)=>sorted.Length==0?0:sorted[(int)Math.Ceiling((sorted.Length-1)*p)];
        return new{Count=samples.Count,Total=samples.Sum(),Mean=samples.Count==0?0:samples.Average(),
            P50=Percentile(.5),P95=Percentile(.95),P99=Percentile(.99),Maximum=Percentile(1)};
    }
}
