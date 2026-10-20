using System.Text.Json;
using Dango.Desktop.Physics.Tuning;

namespace Dango.Physics.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DangoBeltRoomCollectionTests
{
    private static string Module
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) Assert.Inconclusive("Build and set DANGO_BELT_ROOM_MODULE; native collection coverage was not executed.");
            return path!;
        }
    }
    private static DangoBeltRoomWorld Create()
    {
        var world = new DangoBeltRoomWorld(Module,new DangoSelfPhysicsParameters
            { RadialElasticity=1.82,PerimeterElasticity=15.075,TopInwardForce=0,GroundDeformationStrength=0 },x:-8);
        Assert.IsTrue(world.SupportsFurnitureCollection);
        world.EnableFurnitureCollection(0);return world;
    }
    private static DangoFurnitureDefinition Platform(double x=3,double y=2) =>
        new(DangoFurnitureKind.StaticPlatform,new(x,y,1,.2,.4)) { CoordinateSpace=DangoFurnitureCoordinateSpace.BeltWorldMeters };
    private static DangoFurnitureDefinition Ball(double x=5,double y=4) =>
        new(DangoFurnitureKind.RoundBall,Ball:new(x,y,.4)) { CoordinateSpace=DangoFurnitureCoordinateSpace.BeltWorldMeters };
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static DangoBeltRoomFurnitureFrame Apply(DangoBeltRoomWorld world,DangoFurnitureDefinition definition,
        DangoFurnitureChangeKind kind=DangoFurnitureChangeKind.Insert,ulong target=0)
    {
        var before=world.ReadFurnitureCollectionFrame();
        var prep=world.PrepareFurnitureChange(kind,target,definition,before.SceneRevision);
        Assert.IsTrue(prep.Probe.Allowed,prep.Probe.Rejection.ToString());
        Assert.AreEqual(before.WorldGeneration,prep.Probe.WorldGeneration);
        return world.ApplyPreparedFurnitureChange(prep.Token!.Value);
    }
    [TestMethod]
    public void PublicationUsesOneCoherentMeterSnapshotAndIsDeeplyImmutable()
    {
        using var world=Create();var empty=world.ReadFurnitureCollectionFrame();
        Assert.AreEqual(1UL,empty.SceneRevision);Assert.AreEqual(0,empty.Items.Count);
        var first=Apply(world,Platform());var second=Apply(world,Ball());
        Assert.AreEqual(2,second.Items.Count);Assert.AreEqual(0,empty.Items.Count);Assert.AreEqual(1,first.Items.Count);
        var ball=second.Items.Single(i=>i.BallState is not null);
        Assert.AreEqual(5d,ball.BallState!.CenterQX);Assert.AreEqual(.4,ball.Definition.Ball!.RadiusQ);
        Assert.AreEqual(DangoFurnitureCoordinateSpace.BeltWorldMeters,ball.Definition.CoordinateSpace);
        Assert.ThrowsExactly<NotSupportedException>(()=>((IList<DangoFurnitureCollectionItem>)second.Items)[0]=ball);
        world.Step(1);var stepped=world.ReadFurnitureCollection();
        Assert.AreEqual(1UL,stepped.PhysicsStep);Assert.AreEqual(0UL,second.PhysicsStep);
        Assert.AreEqual(stepped.BeltFrame.Tick,stepped.Items.Single(i=>i.BallState is not null).BallState!.Step);
        Assert.AreEqual(stepped.SceneRevision,stepped.Items.Single(i=>i.BallState is not null).DangoContact!.SceneRevision);
    }
    [TestMethod]
    public void MixedMoveAndDeleteKeepUntargetedItemAndBodyChannels()
    {
        using var world=Create();Apply(world,Platform());var before=Apply(world,Ball());
        var ball=before.Items.Single(i=>i.BallState is not null);var platform=before.Items.Single(i=>i.BallState is null);
        var moved=Apply(world,Ball(6,3.5),DangoFurnitureChangeKind.Move,ball.InstanceId);
        Assert.AreEqual(Json(platform),Json(moved.Items.Single(i=>i.InstanceId==platform.InstanceId)));
        CollectionAssert.AreEqual(before.BeltFrame.BodySnapshot.ToArray(),moved.BeltFrame.BodySnapshot.ToArray());
        Assert.AreEqual(6d,moved.Items.Single(i=>i.InstanceId==ball.InstanceId).BallState!.CenterQX);
        var prep=world.PrepareFurnitureChange(DangoFurnitureChangeKind.Delete,platform.InstanceId,null,moved.SceneRevision);
        Assert.IsTrue(prep.Probe.Allowed);var deleted=world.ApplyPreparedFurnitureChange(prep.Token!.Value);
        Assert.AreEqual(ball.InstanceId,deleted.Items.Single().InstanceId);
    }
    [TestMethod]
    public void PermitsRejectOtherOwnersForgeryAndReuseAndCancelDoesNotMutate()
    {
        using var world=Create();using var other=Create();var before=world.ReadFurnitureCollectionFrame();
        var prep=world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert,0,Platform(),before.SceneRevision);var token=prep.Token!.Value;
        Assert.ThrowsExactly<ArgumentException>(()=>other.ApplyPreparedFurnitureChange(token));
        Assert.ThrowsExactly<ArgumentException>(()=>world.ApplyPreparedFurnitureChange(token with { PhysicsStep=99 }));
        Assert.AreEqual(-709,Assert.ThrowsExactly<DangoBeltRoomException>(()=>world.Step(1)).ErrorCode);
        world.CancelPreparedFurnitureChange(token);Assert.AreEqual(Json(before),Json(world.ReadFurnitureCollectionFrame()));
        Assert.ThrowsExactly<ArgumentException>(()=>world.ApplyPreparedFurnitureChange(token));
        var applied=Apply(world,Platform());Assert.AreEqual(1,applied.Items.Count);
        Assert.ThrowsExactly<ArgumentException>(()=>world.CancelPreparedFurnitureChange(token));
    }
    [TestMethod]
    public void StaleRevisionLegacyUnitsAndOverlapFailWithoutMutation()
    {
        using var world=Create();var before=Apply(world,Platform());var json=Json(before);
        Assert.ThrowsExactly<ArgumentException>(()=>world.ProbeFurnitureChange(DangoFurnitureChangeKind.Insert,0,
            Platform() with { CoordinateSpace=DangoFurnitureCoordinateSpace.LegacyPAndQ },before.SceneRevision));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>world.ProbeFurnitureChange((DangoFurnitureChangeKind)99,0,Platform(),before.SceneRevision));
        var stale=world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert,0,Ball(),0);
        Assert.AreEqual(DangoFurnitureChangeRejection.StaleScene,stale.Probe.Rejection);Assert.IsNull(stale.Token);
        var overlap=world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert,0,Platform(),before.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.Penetration,overlap.Probe.Rejection);Assert.IsNull(overlap.Token);
        Assert.AreEqual(json,Json(world.ReadFurnitureCollectionFrame()));
    }
    [TestMethod]
    public void ThirtyItemCapacityIsAtomic()
    {
        using var world=Create();for(var i=0;i<30;i++)Apply(world,Platform(-5+(i%10)*1.2,2+i/10));
        var before=world.ReadFurnitureCollectionFrame();
        var prep=world.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert,0,Platform(7,6),before.SceneRevision);
        Assert.AreEqual(DangoFurnitureChangeRejection.CapacityExceeded,prep.Probe.Rejection);Assert.IsNull(prep.Token);
        Assert.AreEqual(Json(before),Json(world.ReadFurnitureCollectionFrame()));
    }
    [TestMethod]
    public void CollectionThreadAndDisposedOwnerAccessFailClosed()
    {
        var world=Create();var task=Task.Run(()=>Assert.ThrowsExactly<InvalidOperationException>(()=>world.ReadFurnitureCollectionFrame()));
        task.GetAwaiter().GetResult();world.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(()=>world.ReadFurnitureCollectionFrame());
        Assert.ThrowsExactly<ObjectDisposedException>(()=>world.EnableFurnitureCollection(0));
    }
    [TestMethod]
    public void RetainedPermitCannotCrossFullyDisposedOwnerLifetime()
    {
        DangoPreparedFurnitureChange retained;
        using (var first=Create()) retained=first.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert,0,Platform(),1).Token!.Value;
        using var second=Create();
        var fresh=second.PrepareFurnitureChange(DangoFurnitureChangeKind.Insert,0,Platform(),1).Token!.Value;
        Assert.AreNotEqual(retained.WorldGeneration,fresh.WorldGeneration);
        Assert.ThrowsExactly<ArgumentException>(()=>second.ApplyPreparedFurnitureChange(retained));
        Assert.AreEqual(1,second.ApplyPreparedFurnitureChange(fresh).Items.Count);
    }
    [TestMethod]
    public void PreviousRoomModuleKeepsOldPathAndRejectsCollectionCalls()
    {
        var module=Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_NO_COLLECTION_MODULE");
        if (string.IsNullOrWhiteSpace(module) || !File.Exists(module)) Assert.Inconclusive("Set DANGO_BELT_ROOM_NO_COLLECTION_MODULE to a previous room DLL.");
        using var world=new DangoBeltRoomWorld(module!,new DangoSelfPhysicsParameters());
        Assert.IsFalse(world.SupportsFurnitureCollection);
        Assert.ThrowsExactly<NotSupportedException>(()=>world.EnableFurnitureCollection(0));
        world.Step(1);Assert.AreEqual(1UL,world.Read().Tick);
    }
    [TestMethod]
    [DataRow("DANGO_BELT_ROOM_COLLECTION_BAD_ABI_MODULE")]
    [DataRow("DANGO_BELT_ROOM_COLLECTION_BAD_SIZE_MODULE")]
    [DataRow("DANGO_BELT_ROOM_COLLECTION_BAD_IDENTITY_MODULE")]
    [DataRow("DANGO_BELT_ROOM_COLLECTION_MISSING_BALL_CAP_MODULE")]
    public void CollectionMetadataFailuresRejectBeforeWorldCreation(string variable)
    {
        var module=Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(module) || !File.Exists(module)) Assert.Inconclusive($"Set {variable} to a deliberately invalid collection DLL.");
        Assert.ThrowsExactly<NotSupportedException>(()=>new DangoBeltRoomWorld(module!,new DangoSelfPhysicsParameters()));
    }
    [TestMethod]
    public void LegacyWorldRejectsMeterDefinitions()
    {
        if (!File.Exists(PhysicsTestLibraries.Unified)) Assert.Inconclusive("Set DANGO_PHYSICS_MODULE_PATH to a built legacy module.");
        using var world=DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified);
        Assert.ThrowsExactly<ArgumentException>(()=>world.ProbeFurnitureChange(DangoFurnitureChangeKind.Insert,0,Platform(),0));
    }
}
