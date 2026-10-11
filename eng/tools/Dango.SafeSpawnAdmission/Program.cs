using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dango.Physics;
using Dango.Physics.Configuration;
using Dango.SafeSpawnAdmission.Core;

namespace Dango.SafeSpawnAdmission;

public sealed record ProbeCheck(string Name, bool Pass, string Detail);
public sealed class Evidence
{
    public int Version { get; set; } = 1;
    public string Mode { get; set; } = "";
    public string Mutation { get; set; } = "";
    public int ProcessId { get; set; } = Environment.ProcessId;
    public string SourceHash { get; set; } = "";
    public string DllHash { get; set; } = "";
    public string ParameterHash { get; set; } = "";
    public bool Pass { get; set; }
    public List<ProbeCheck> Checks { get; set; } = [];
    public NativeCounts NativeCounts { get; set; } = new();
    public AllocationEvidence Allocation { get; set; } = new();
    public RecoveryEvidence Recovery { get; set; } = new();
    public NativeProbeEvidence? ExternalNativeProbe { get; set; }
    public string RawDigest { get; set; } = "";
    public string RawDigestMeaning { get; set; } = "complete-native-first-healthy-frame; epoch excluded";
    public void Check(string name,bool pass,string detail) => Checks.Add(new(name,pass,detail));
}
public sealed class AllocationEvidence { public int Before { get; set; } public int After { get; set; } }
public sealed class RecoveryEvidence
{
    public bool Blocked { get; set; }
    public bool Ready { get; set; }
    public bool Published { get; set; }
    public bool AwaitingReady { get; set; }
    public string Reason { get; set; } = "";
    public Guid Epoch { get; set; }
    public Guid PriorEpoch { get; set; }
    public double RecordValidationToHealthyMs { get; set; }
    public ulong Tick { get; set; }
    public int[] EntityIds { get; set; } = [];
}
public sealed record DispositionEvidence(int Version, string Disposition, Guid SourceEpoch, int ProducerProcessId, string MetadataHash);
public sealed record NativeProbeEvidence(int ProcessId,int ExitCode,ulong LastSafeTick,int NativeErrorCode,int Steps,int Created,int Disposed,int Before,int After);

internal static class Program
{
    private const string FrozenDll = "59EC861EE748BA93E661D058EEBD01D3E8A04ABF9D24F0376BACB4D98015B6DC";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly DateTimeOffset Now = new(2026,10,11,0,0,0,TimeSpan.Zero);
    public static int Main(string[] args)
    {
        var options = new Dictionary<string,string>(StringComparer.Ordinal);
        for(int i=0;i<args.Length;i+=2)
        { if(i+1>=args.Length || !args[i].StartsWith("--",StringComparison.Ordinal)) return 2; options.Add(args[i],args[i+1]); }
        string Option(string key,string fallback="") => options.GetValueOrDefault(key,fallback);
        var e = new Evidence { Mode=Option("--mode","suite"), Mutation=Option("--mutation") };
        nint library=0;
        try
        {
            if(e.Mode=="verify") return Verify(Option("--output"));
            string dll = Path.GetFullPath(Option("--dll"));
            e.DllHash = Hash(File.ReadAllBytes(dll));
            e.ParameterHash=NativeParameterHash();
            e.SourceHash=SourceHash();
            e.Check("frozen-dll",e.DllHash==FrozenDll,e.DllHash);
            if(e.DllHash!=FrozenDll) throw new InvalidOperationException("Wrong frozen R1 binary.");
            library=NativeLibrary.Load(dll);
            var allocated=Marshal.GetDelegateForFunctionPointer<AllocatedBytes>(NativeLibrary.GetExport(library,"dango_physics_get_allocated_bytes"));
            e.Allocation.Before=allocated();
            try
            {
                switch(e.Mode)
                {
                    case "suite": Suite(dll,Option("--probe"),e); break;
                    case "planner": PlannerChecks(e); break;
                    case "produce": Produce(dll,Option("--probe"),Option("--metadata"),Option("--disposition","lost"),int.Parse(Option("--steps","60")),e); break;
                    case "consume": Consume(dll,Option("--metadata"),Option("--disposition","lost"),e); break;
                    default: throw new ArgumentException("Mode must be suite, produce, consume or verify.");
                }
            }
            finally { e.Allocation.After=allocated(); }
            e.Check("native-allocation-returned",e.Allocation.Before==e.Allocation.After,$"{e.Allocation.Before}->{e.Allocation.After}");
            e.Check("native-owner-disposed",e.NativeCounts.Created==e.NativeCounts.Disposed,$"{e.NativeCounts.Created}/{e.NativeCounts.Disposed}");
        }
        catch(Exception error) { e.Check("exception",false,error.ToString()); }
        finally { if(library!=0) NativeLibrary.Free(library); }
        e.Pass=e.Checks.Count>0 && e.Checks.All(c=>c.Pass);
        var encoded=JsonSerializer.Serialize(e,Json);
        if(Option("--output") is { Length:>0 } path) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path,encoded); }
        Console.WriteLine(encoded);
        return e.Pass?0:1;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string NativeParameterHash()
    {
        // Hash the existing marshaller's actual ABI config bytes, including
        // native float rounding, rather than a managed double JSON rendering.
        var type=typeof(DangoPhysicsWorld).GetNestedType("NativeConfiguration",BindingFlags.NonPublic)!;
        var value=type.GetMethod("From",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,[DangoPhysicsDefaults.Current])!;
        var size=Marshal.SizeOf(type); var memory=Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(value,memory,false);var bytes=new byte[size];Marshal.Copy(memory,bytes,0,size);return Hash(bytes); }
        finally {Marshal.FreeHGlobal(memory);}
    }
    private static string SourceHash()
    {
        var project=Path.GetFullPath("../../..",AppContext.BaseDirectory);
        var root=Path.GetFullPath("../../..",project);
        var files=new[]{"eng/tools/Dango.SafeSpawnAdmission","eng/tools/Dango.SafeSpawnAdmission.Core","src/Dango.Physics","eng/native/box2d"}
            .SelectMany(part=>Directory.EnumerateFiles(Path.Combine(root,part),"*",SearchOption.AllDirectories))
            .Where(path=>!path.Split(Path.DirectorySeparatorChar).Any(p=>p is "bin" or "obj"))
            .OrderBy(path=>Path.GetRelativePath(root,path),StringComparer.Ordinal);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach(var file in files) {hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root,file).Replace('\\','/')));hash.AppendData(File.ReadAllBytes(file));}
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private static NativeRecoveryOwner Fresh(string dll,NativeCounts counts,int count=2,Guid? epoch=null)
    {
        var slots=SpawnPlanner.Plan(SafeSpawnFixtures.Members(count).Select(m=>m.PetId));
        if(!SpawnPlanner.IsSafe(slots)) throw new InvalidOperationException("Unsafe spawn plan.");
        var owner=new NativeRecoveryOwner(dll,counts,epoch:epoch);
        try { foreach(var s in slots) owner.Add(s.X,s.Y); return owner; }
        catch { owner.Dispose(); throw; }
    }
    private static void SpawnTrajectory(string dll,int count,Evidence e)
    {
        using var a=Fresh(dll,e.NativeCounts,count);
        e.Check($"tick0-{count}",NativeFrameVerifier.TickZero(a.Read(),a.Ids),"zero velocity/support/cooldown and complete raw+semantic frame");
        var first=a.Step();
        e.Check($"gravity-{count}",first.Authority.Bodies.All(b=>b.CoreVelocityY<0),"native first gravity step retained");
        bool healthy=true; for(var tick=1;tick<600;tick++) healthy &= NativeFrameVerifier.Healthy(a.Step(),a.Ids);
        e.Check($"600-native-steps-{count}",healthy && a.Read().Authority.Tick==600,"every complete native raw frame validated");
        e.Check($"checkpoint-unsupported-{count}",a.CheckpointUnsupported(),"export/import rejected; retained frame unchanged");
    }
    private static void Continuity(string dll,string scene,string transition,Evidence e)
    {
        var configuration=SafeSpawnFixtures.Configuration(e.ParameterHash);
        int memberCount=scene=="airborne"?1:2;
        var store=new ConditionalRecoveryStoreFixture(SafeSpawnFixtures.OwnerId,1);
        store.TryClaim(SafeSpawnFixtures.OwnerId,1,Guid.NewGuid(),out var claim);
        var facts=SafeSpawnFixtures.Facts(RuntimeDisposition.ConfirmedFirstStart,claim!.RuntimeRevision,count:memberCount) with {OwnerId=claim.OwnerId};
        var machine=new RecoveryStateMachine(configuration,store);
        var candidate=machine.Prepare(MetadataInput.Missing(),facts,claim,Now).Candidate!;
        using var a=new NativeRecoveryOwner(dll,e.NativeCounts,epoch:candidate.Epoch);
        using var b=new NativeRecoveryOwner(dll,e.NativeCounts);
        for(int i=0;i<(scene=="airborne"?1:2);i++)
        { double x=scene=="stack"?0:i==0?-1.4:1.4,y=scene=="stack" && i==1?1.65:0; a.Add(x,y); b.Add(x,y); }
        DangoPhysicsInput Input(DangoEntityId id,int index,int tick) => new(id,
            Horizontal:scene=="push" && tick>=60 ? index==0?1:-1 : 0,
            JumpPressed:scene=="airborne" && tick==120);
        var warm=scene=="airborne"?128:180;
        for(int tick=0;tick<warm;tick++) { a.Step((id,index)=>Input(id,index,tick)); b.Step((id,index)=>Input(id,index,tick)); }
        var pre=a.Read();
        bool commissioned=machine.CommitPublication(candidate,new(candidate.Epoch,pre.Authority.Tick,pre.Authority.SceneRevision,candidate.Members,
            NativeFrameVerifier.Healthy(pre,a.Ids)),facts,Now);
        facts=facts with {Disposition=RuntimeDisposition.HealthyRetained};
        var lease=new LeaseFact(Guid.NewGuid(),Guid.NewGuid(),facts.Members[0],LeaseKind.Control,1,Now.AddMinutes(5));
        facts=facts with {Leases=[lease]};
        var binding=Guid.NewGuid(); var challenge=machine.IssueReady(binding,lease,facts,Now,TimeSpan.FromSeconds(10));
        bool bindingReady=challenge is not null && machine.ConfirmReady(challenge,facts,Now);
        bool priorIntent=machine.AcceptInput(new(machine.CurrentEpoch,binding,lease.LeaseId,lease.ConnectionId,lease.Member,
            lease.ControlGeneration,1,true,Now.AddMilliseconds(-100)),facts,Now);
        bool active=scene switch { "stack"=>pre.Authority.Bodies.Any(v=>v.SupportEntityId is not null),
            "airborne"=>pre.Authority.Bodies.Any(v=>Math.Abs(v.CoreVelocityY)>.1f && !v.FloorGrounded),
            _=>pre.Raw.ContactStepCount>0 };
        var epoch=a.Epoch; var ids=a.Ids.ToArray(); var creates=e.NativeCounts.Created;
        // Actual pure binding transition alongside retained native world.
        if(transition=="pause") {machine.PauseRetained();bindingReady &= machine.ResumeRetained(facts);}
        else
        {
            lease=lease with {ConnectionId=Guid.NewGuid(),ControlGeneration=transition=="takeover"?2ul:1ul};
            facts=facts with {FactsRevision=facts.FactsRevision+1,Leases=[lease]};
            challenge=machine.IssueReady(Guid.NewGuid(),lease,facts,Now,TimeSpan.FromSeconds(10));
            bindingReady &= challenge is not null && machine.ConfirmReady(challenge,facts,Now);
        }
        if(e.Mutation=="healthyReset") a.WrongResetForMutation();
        var clearedIntent=machine.ReadInput(binding,facts,Now);
        bool equal=NativeFrameVerifier.BitEqual(a.Read(),b.Read());
        for(int tick=0;tick<120;tick++)
        {
            var frame=a.Step(); var reference=b.Step();
            equal &= NativeFrameVerifier.BitEqual(frame,reference);
        }
        e.Check($"{scene}-{transition}-120-bits",commissioned && bindingReady && priorIntent && clearedIntent.Move==0 && !clearedIntent.Jump &&
            machine.CurrentEpoch==epoch && active && equal && a.Epoch==epoch && a.Ids.SequenceEqual(ids) &&
            e.NativeCounts.Created==creates && e.NativeCounts.Reset==0,"actual raw+semantic complete state, same effective neutral after old intent clear");
    }
    private static void CandidateFailures(string dll,Evidence e)
    {
        foreach(var failure in new[]{"secondAdd","cancel","error"})
        {
            var before=e.NativeCounts.Disposed; bool published=false; bool caught=false;
            try
            {
                using var privateOwner=new NativeRecoveryOwner(dll,e.NativeCounts,failure=="secondAdd"?1:2);
                privateOwner.Add(-1.4,0);
                if(failure=="cancel") throw new OperationCanceledException("private candidate cancelled");
                if(failure=="error") throw new InvalidOperationException("private candidate exception");
                privateOwner.Add(1.4,0); published=true;
            }
            catch(Exception ex) when(ex is OperationCanceledException or InvalidOperationException) { caught=true; }
            e.Check($"candidate-{failure}-whole-dispose",caught && !published && e.NativeCounts.Disposed==before+1,"private group never published; owner disposed");
        }
        using(var owner=Fresh(dll,e.NativeCounts))
        {
            bool crossRead=Task.Run(()=> { try{owner.Read();return false;}catch(InvalidOperationException){return true;} }).GetAwaiter().GetResult();
            bool crossDispose=Task.Run(()=> { try{owner.Dispose();return false;}catch(InvalidOperationException){return true;} }).GetAwaiter().GetResult();
            e.Check("cross-thread-rejected",crossRead && crossDispose,"foreign read and Dispose rejected before native access");
        }
        var configuration=SafeSpawnFixtures.Configuration(e.ParameterHash);
        var prior=SafeSpawnFixtures.Record(configuration,Guid.NewGuid());
        var store=new ConditionalRecoveryStoreFixture(SafeSpawnFixtures.OwnerId,1,prior);
        store.TryClaim(SafeSpawnFixtures.OwnerId,1,Guid.NewGuid(),out var claim);
        var facts=SafeSpawnFixtures.Facts(RuntimeDisposition.ConfirmedWorldLost,claim!.RuntimeRevision) with {OwnerId=claim.OwnerId};
        var machine=new RecoveryStateMachine(configuration,store);
        var candidate=machine.Prepare(MetadataInput.Existing(RecoveryRecordCodec.Encode(prior)),facts,claim,Now).Candidate!;
        var disposed=e.NativeCounts.Disposed;
        bool committed;
        using(var owner=Fresh(dll,e.NativeCounts,epoch:candidate.Epoch))
        {
            var healthy=owner.Step();
            committed=machine.CommitPublication(candidate,new(candidate.Epoch,healthy.Authority.Tick,healthy.Authority.SceneRevision,
                candidate.Members,NativeFrameVerifier.Healthy(healthy,owner.Ids)),facts with {FactsRevision=facts.FactsRevision+1,Members=SafeSpawnFixtures.Members(1)},Now);
            machine.AbortCandidate();
        }
        e.Check("native-candidate-latest-facts-race",!committed && machine.CurrentEpoch==Guid.Empty && store.LastSuccessfulRecord==prior &&
            e.NativeCounts.Disposed==disposed+1,"actual healthy two-body private candidate rejected and disposed after member/revision race");
    }
    private static void Members(string dll,Evidence e)
    {
        using var owner=new NativeRecoveryOwner(dll,e.NativeCounts);
        owner.Add(0,0); owner.Add(0,1.65);
        for(int t=0;t<180;t++) owner.Step();
        var before=owner.Read(); var top=owner.Ids[1]; var bottom=owner.Ids[0];
        e.Check("lower-actually-supports",before.Authority.Bodies.Single(b=>b.EntityId==top).SupportEntityId==bottom,"actual native support before removal");
        var survivorBefore=before.Raw.Bodies.Single(b=>b.EntityId==top); var epoch=owner.Epoch;
        owner.Remove(bottom); var removed=owner.Read();
        var survivorAfter=removed.Raw.Bodies.Single();
        bool retained=survivorBefore.CoreX==survivorAfter.CoreX && survivorBefore.CoreY==survivorAfter.CoreY &&
            survivorBefore.Contour.SequenceEqual(survivorAfter.Contour) && removed.Authority.Bodies.Single().SupportEntityId is null;
        for(int t=0;t<180;t++) owner.Step();
        e.Check("remove-lower-survivor-retained",retained && owner.Epoch==epoch && owner.Ids.Single()==top && owner.Read().Authority.Bodies.Single().FloorGrounded,"native Remove, same survivor falls naturally");
        var current=owner.Read(); var digest=NativeFrameVerifier.Digest(current); var steps=e.NativeCounts.Steps; var add=e.NativeCounts.Add;
        // Occupied current raw core rejects new member without stepping or moving survivor.
        var occupied=new SpawnSlot(SafeSpawnFixtures.PetOne,current.Raw.Bodies.Single().CoreX,0);
        var accepted=SpawnPlanner.CanAdd(occupied,current.Raw);
        e.Check("new-member-space-precheck",!accepted && digest==NativeFrameVerifier.Digest(owner.Read()) && steps==e.NativeCounts.Steps && add==e.NativeCounts.Add,"existing raw frame precheck; no Add/Step/Reset on reject");
        using var reentry=Fresh(dll,e.NativeCounts);
        reentry.Step();var removedId=reentry.Ids[1];var retainedId=reentry.Ids[0];
        reentry.Remove(removedId);var survivor=reentry.Read();var survivorDigest=NativeFrameVerifier.Digest(survivor);
        var freeSlot=new SpawnSlot(SafeSpawnFixtures.PetTwo,1.4,0);
        bool space=SpawnPlanner.CanAdd(freeSlot,survivor.Raw);
        if(!space) throw new InvalidOperationException("Known free original slot unexpectedly rejected.");
        bool precheckUnchanged=survivorDigest==NativeFrameVerifier.Digest(reentry.Read());
        reentry.Add(freeSlot.X,freeSlot.Y);
        var added=reentry.Read();var newborn=added.Authority.Bodies.Single(v=>v.EntityId!=retainedId);
        var oldBody=survivor.Raw.Bodies.Single();var sameBody=added.Raw.Bodies.Single(v=>v.EntityId==retainedId);
        var oldSemantic=survivor.Authority.Bodies.Single();var sameSemantic=added.Authority.Bodies.Single(v=>v.EntityId==retainedId);
        bool preserved=oldBody.Contour.SequenceEqual(sameBody.Contour) && oldBody.CoreX==sameBody.CoreX && oldBody.CoreY==sameBody.CoreY &&
            oldSemantic.CoreVelocityX==sameSemantic.CoreVelocityX && oldSemantic.CoreVelocityY==sameSemantic.CoreVelocityY;
        var common=reentry.Step();
        e.Check("live-member-real-reentry",space && precheckUnchanged && preserved &&
            newborn.EntityId!=removedId && newborn.CoreVelocityX==0 && newborn.CoreVelocityY==0 && newborn.SupportEntityId is null &&
            newborn.CooldownRemainingSeconds==0 && common.Authority.Bodies.Count==2 && reentry.Ids.Contains(retainedId),
            "raw precheck then actual Add at free original slot; new native ID, no old velocity/support, survivor retained");
    }
    private static NativeProbeEvidence Probe(string executable,string mutation,Evidence e,int steps=1)
    {
        if(!File.Exists(executable)) throw new ArgumentException("--probe must name compiled independent native probe.");
        var info=new ProcessStartInfo(executable) { RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false,CreateNoWindow=true };
        if(mutation.Length>0) info.ArgumentList.Add(mutation);
        if(mutation=="faultProducer") info.ArgumentList.Add(steps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var p=Process.Start(info)!;
        var stdout=p.StandardOutput.ReadToEndAsync(); var stderr=p.StandardError.ReadToEndAsync();
        p.WaitForExit(); var output=stdout.GetAwaiter().GetResult();var error=stderr.GetAwaiter().GetResult();
        using var json=JsonDocument.Parse(output);
        var root=json.RootElement;
        var result=new NativeProbeEvidence(p.Id,p.ExitCode,root.GetProperty("LastSafeTick").GetUInt64(),root.GetProperty("NativeErrorCode").GetInt32(),
            root.GetProperty("Steps").GetInt32(),root.GetProperty("Created").GetInt32(),root.GetProperty("Disposed").GetInt32(),
            root.GetProperty("Before").GetInt32(),root.GetProperty("After").GetInt32());
        e.ExternalNativeProbe=result;
        e.Check("native-node-fault-probe",p.ExitCode==0 && root.GetProperty("Pass").GetBoolean() &&
            root.GetProperty("Before").GetInt32()==root.GetProperty("After").GetInt32(),$"pid={p.Id};exit={p.ExitCode};{output.Trim()};{error.Trim()}");
        return result;
    }
    private static void Suite(string dll,string probe,Evidence e)
    {
        if(e.Mutation is "halfReady" or "skipSpawnOverlap" or "skipSpawnBounds" or "skipSpawnGround")
        { SpawnMutation(dll,e); return; }
        if(e.Mutation is "oldMove" or "oldJump") { OldInput(dll,e); return; }
        if(e.Mutation is "oldEpoch" or "noReason") { EpochMutation(dll,e); return; }
        if(e.Mutation=="faultAutoRebirth") { Probe(probe,e.Mutation,e); return; }
        if(e.Mutation.Length>0 && e.Mutation!="healthyReset")
        {
            var result=SemanticsMutationOracle.Evaluate(e.Mutation);
            e.Check(result.Name,result.OraclePassed,result.Detail); return;
        }
        PlannerChecks(e);
        SpawnTrajectory(dll,1,e); SpawnTrajectory(dll,2,e);
        foreach(var scene in new[]{"push","stack","airborne"}) foreach(var transition in new[]{"reconnect","takeover","pause"}) Continuity(dll,scene,transition,e);
        CandidateFailures(dll,e); Members(dll,e); Probe(probe,"",e);
    }
    private static void PlannerChecks(Evidence e)
    {
        var slot=new SpawnSlot(SafeSpawnFixtures.PetOne,-1.4f,0);
        var r=SpawnPlanner.EnvelopeRadius;
        e.Check("planner-left-tangent-rejected",!SpawnPlanner.IsSafe([slot],slot.X-r,13.2),"exact envelope touch to left boundary rejected");
        e.Check("planner-right-tangent-rejected",!SpawnPlanner.IsSafe([slot],-13.2,slot.X+r),"exact envelope touch to right boundary rejected");
        e.Check("planner-invalid-bounds-rejected",!SpawnPlanner.IsSafe([slot],double.NaN,13.2) && !SpawnPlanner.IsSafe([slot],-13.2,double.NaN) &&
            !SpawnPlanner.IsSafe([slot],double.PositiveInfinity,double.PositiveInfinity) && !SpawnPlanner.IsSafe([slot],1,-1),"finite ordered bounds required");
        var legal=SpawnPlanner.Plan(SafeSpawnFixtures.Members().Select(m=>m.PetId));
        e.Check("planner-group-tangent-rejected",!SpawnPlanner.IsSafe([slot,slot with {PetId=SafeSpawnFixtures.PetTwo,X=slot.X+2*r}]),"strict positive group separation required");
        var dummy=new DangoBodyState(new(1),Array.AsReadOnly(new float[32]),10,0,1,0,0,0,0,false,-1,16);
        var full=new DangoPhysicsFrame([dummy,dummy with {EntityId=new(2),CoreX=11}],0,0);
        e.Check("planner-live-capacity-rejected",!SpawnPlanner.CanAdd(legal[0],full),"third member rejected before native Add");
    }
    private static void SpawnMutation(string dll,Evidence e)
    {
        var slots=SpawnPlanner.Plan(SafeSpawnFixtures.Members().Select(m=>m.PetId));
        if(e.Mutation=="skipSpawnOverlap") slots[1]=slots[1] with {X=slots[0].X};
        if(e.Mutation=="skipSpawnBounds") slots[0]=slots[0] with {X=-13.2};
        if(e.Mutation=="skipSpawnGround") slots[0]=slots[0] with {Y=-.5};
        using var owner=new NativeRecoveryOwner(dll,e.NativeCounts);
        foreach(var slot in slots)
        {
            owner.Add(slot.X,slot.Y);
            if(e.Mutation=="halfReady")
            {
                // Wrong publication really exposes the first actual native body.
                var published=owner.Read();
                e.Check("whole-group-before-ready",published.Authority.Bodies.Count==slots.Length,"first body prematurely published"); return;
            }
        }
        e.Check("independent-envelope-before-add",SpawnPlanner.IsSafe(slots),"native Add accepted, but independent full group safety rejected bypass");
    }
    private static void OldInput(string dll,Evidence e)
    {
        using var wrong=Fresh(dll,e.NativeCounts,1); using var reference=Fresh(dll,e.NativeCounts,1);
        for(int t=0;t<90;t++){wrong.Step();reference.Step();}
        var actual=wrong.Step((id,index)=>new(id,Horizontal:e.Mutation=="oldMove"?1:0,JumpPressed:e.Mutation=="oldJump"));
        var expected=reference.Step();
        e.Check("stale-intent-not-inherited",NativeFrameVerifier.BitEqual(actual,expected),"actual old Move/Jump changes native common frame");
    }
    private static void EpochMutation(string dll,Evidence e)
    {
        Guid old; int[] oldIds;
        using(var prior=Fresh(dll,e.NativeCounts)){old=prior.Epoch;oldIds=prior.Ids.Select(v=>v.Value).ToArray();prior.Step();}
        using var next=Fresh(dll,e.NativeCounts); var actual=next.Step();
        Guid published=e.Mutation=="oldEpoch"?old:next.Epoch;
        string? reason=e.Mutation=="noReason"?null:"ConfirmedWorldLost";
        e.Check("epoch-and-reason-native-reuse",next.Ids.Select(v=>v.Value).SequenceEqual(oldIds) && published!=old && !string.IsNullOrEmpty(reason),
            $"native numeric IDs genuinely reused; publication epoch={published}, prior={old}, reason={reason}; tick={actual.Authority.Tick}");
    }
    private static void Produce(string dll,string probe,string metadata,string disposition,int steps,Evidence e)
    {
        if(steps is <1 or >10000) throw new ArgumentOutOfRangeException(nameof(steps));
        Guid epoch; ulong tick;
        bool releaseValidated=false;
        if(disposition=="fault")
        {
            // Single source: this native executable creates, runs, actually
            // faults, retains its last safe frame and destroys that same world.
            var source=Probe(probe,"faultProducer",e,steps);
            epoch=Guid.NewGuid();tick=source.LastSafeTick;
            e.Check("producer-same-native-fault-source",source.NativeErrorCode==-422 && source.LastSafeTick==(ulong)steps && source.ExitCode==0,
                $"single fault-source pid={source.ProcessId}; safeTick={source.LastSafeTick};fault={source.NativeErrorCode}; no pose exported");
        }
        else using(var owner=Fresh(dll,e.NativeCounts))
        {
            for(int t=0;t<steps;t++) owner.Step();
            epoch=owner.Epoch; tick=owner.Read().Authority.Tick;
            if(disposition=="released")
            {
                var facts=SafeSpawnFixtures.Facts(RuntimeDisposition.HealthyRetained) with {SleepEligible=true,AllLeasesAbsentSince=Now.AddSeconds(-30)};
                var permit=LeaseGate.PrepareRelease(facts,epoch,Now);
                releaseValidated=permit is not null && LeaseGate.RevalidateRelease(permit,facts,epoch,Now);
                if(!releaseValidated) throw new InvalidOperationException("Actual release transition rejected.");
            }
            e.Check("producer-actual-native-prefix",tick==(ulong)steps,"native world later disposed and producer process exits; no pose serialized");
        }
        if(disposition=="released") e.Check("actual-qualified-release",releaseValidated && e.NativeCounts.Disposed==1,"trusted fixture 30s grace and no control/observer; final LeaseGate revalidation immediately before owner Dispose");
        if(disposition is not ("lost" or "released" or "fault" or "unknown")) throw new ArgumentException("Disposition invalid.");
        var record=SafeSpawnFixtures.Record(SafeSpawnFixtures.Configuration(e.ParameterHash),epoch,tick:tick);
        var bytes=RecoveryRecordCodec.Encode(record);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(metadata))!);
        File.WriteAllBytes(metadata,bytes);
        File.WriteAllText(metadata+".disposition.json",JsonSerializer.Serialize(new DispositionEvidence(1,disposition,epoch,Environment.ProcessId,Hash(bytes)),Json));
        e.Recovery.PriorEpoch=epoch; e.Recovery.Reason=disposition; e.Recovery.Tick=tick;
        e.Check("metadata-only-bounded",bytes.Length<=65536,$"{bytes.Length} bytes; domain metadata and separate trusted fixture disposition");
    }
    private static void Consume(string dll,string metadata,string disposition,Evidence e)
    {
        var bytes=File.ReadAllBytes(metadata);
        var trusted=JsonSerializer.Deserialize<DispositionEvidence>(File.ReadAllText(metadata+".disposition.json"))!;
        if(trusted.Version!=1 || trusted.MetadataHash!=Hash(bytes) || trusted.Disposition!=disposition || trusted.ProducerProcessId==Environment.ProcessId)
            throw new InvalidOperationException("Independent trusted fixture disposition invalid.");
        var watch=Stopwatch.StartNew(); // includes record decode, claim, plan, native create and all healthy gates.
        var configuration=SafeSpawnFixtures.Configuration(e.ParameterHash);
        if(!RecoveryRecordCodec.TryDecode(bytes,configuration,out var record,out var error)) throw new InvalidOperationException(error);
        if(trusted.SourceEpoch!=record!.PlayEpoch) throw new InvalidOperationException("Trusted disposition does not identify record source epoch.");
        var store=new ConditionalRecoveryStoreFixture(SafeSpawnFixtures.OwnerId,record!.RuntimeRevision,record);
        if(!store.TryClaim(SafeSpawnFixtures.OwnerId,record.RuntimeRevision,Guid.NewGuid(),out var claim)) throw new InvalidOperationException("Claim failed.");
        var kind=disposition switch {"lost"=>RuntimeDisposition.ConfirmedWorldLost,"released"=>RuntimeDisposition.NormallyReleased,
            "fault"=>RuntimeDisposition.FaultLatched,_=>RuntimeDisposition.Unknown};
        var facts=SafeSpawnFixtures.Facts(kind,claim!.RuntimeRevision) with {OwnerId=claim.OwnerId};
        var machine=new RecoveryStateMachine(configuration,store);
        var decision=machine.Prepare(MetadataInput.Existing(bytes),facts,claim,Now);
        e.Recovery.PriorEpoch=record.PlayEpoch;
        if(decision.Candidate is null)
        {
            e.Recovery.Blocked=true; e.Recovery.Reason=disposition=="fault"?"faultBlocked":"unknownBlocked";
            e.Check("trusted-disposition-blocks-rebirth",kind is RuntimeDisposition.FaultLatched or RuntimeDisposition.Unknown &&
                e.NativeCounts.Created==0 && machine.CurrentEpoch==Guid.Empty && store.LastSuccessfulRecord==record,decision.Detail); return;
        }
        var candidate=decision.Candidate;
        using var owner=Fresh(dll,e.NativeCounts,candidate.Members.Length,candidate.Epoch);
        var zero=owner.Read(); e.Check("recover-tick0",NativeFrameVerifier.TickZero(zero,owner.Ids),"all native core velocity/support/cooldown clear");
        var first=owner.Step(); var valid=NativeFrameVerifier.Healthy(first,owner.Ids);
        bool committed=machine.CommitPublication(candidate,new(candidate.Epoch,first.Authority.Tick,first.Authority.SceneRevision,candidate.Members,valid),facts,Now);
        watch.Stop();
        e.Recovery.RecordValidationToHealthyMs=watch.Elapsed.TotalMilliseconds;
        e.Recovery.Epoch=machine.CurrentEpoch; e.Recovery.Reason=machine.PublishedReason?.ToString()??"";
        e.Recovery.Tick=first.Authority.Tick; e.Recovery.EntityIds=owner.Ids.Select(v=>v.Value).ToArray();
        e.Recovery.Published=committed; e.Recovery.AwaitingReady=machine.State==RecoveryState.AwaitReady;
        e.RawDigest=NativeFrameVerifier.Digest(first);
        e.Check("group-publication",committed && e.Recovery.AwaitingReady && machine.CurrentEpoch!=record.PlayEpoch && machine.PublishedReason is not null,"native whole group first healthy bound to new epoch and explicit reason");
        var lease=new LeaseFact(Guid.NewGuid(),Guid.NewGuid(),facts.Members[0],LeaseKind.Control,1,Now.AddMinutes(1));
        var liveFacts=facts with {Disposition=RuntimeDisposition.HealthyRetained,Leases=[lease]};
        var binding=Guid.NewGuid(); var challenge=machine.IssueReady(binding,lease,liveFacts,Now,TimeSpan.FromSeconds(10))!;
        bool oldReadyRejected=!machine.ConfirmReady(challenge with {Epoch=record.PlayEpoch},liveFacts,Now);
        bool currentReady=machine.ConfirmReady(challenge,liveFacts,Now);
        e.Recovery.Ready=currentReady; e.Recovery.AwaitingReady=machine.State==RecoveryState.AwaitReady;
        var oldIntent=new InputIntent(record.PlayEpoch,binding,lease.LeaseId,lease.ConnectionId,lease.Member,lease.ControlGeneration,1,true,Now);
        var beforeDigest=NativeFrameVerifier.Digest(owner.Read());
        bool oldInputRejected=!machine.AcceptInput(oldIntent,liveFacts,Now);
        var sample=machine.ReadInput(binding,liveFacts,Now);
        e.Check("old-epoch-ready-input-rejected",oldReadyRejected && currentReady && oldInputRejected && sample.Move==0 && !sample.Jump &&
            beforeDigest==NativeFrameVerifier.Digest(owner.Read()),"actual Core binding rejects old epoch despite newly allocated native IDs");
        e.Check("five-second-budget",watch.Elapsed<=TimeSpan.FromSeconds(5),$"record validation to first common healthy {watch.Elapsed.TotalMilliseconds:F3} ms");
        for(int t=1;t<600;t++) owner.Step();
        e.Check("recovery-600-steps",owner.Read().Authority.Tick==600,"every step passed raw contour/contact/frame gates");
    }
    private static int Verify(string path)
    {
        var e=JsonSerializer.Deserialize<Evidence>(File.ReadAllText(path),Json);
        bool pass=e is not null && e.Version==1 && e.Checks.Count>0 && e.Checks.All(c=>c.Pass) && e.Pass &&
            e.DllHash==FrozenDll && e.NativeCounts.Created==e.NativeCounts.Disposed && e.Allocation.Before==e.Allocation.After &&
            e.Recovery.RecordValidationToHealthyMs<=5000;
        Console.WriteLine(JsonSerializer.Serialize(new {Version=1,Mode="verify",Pass=pass},Json));return pass?0:1;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AllocatedBytes();
}
