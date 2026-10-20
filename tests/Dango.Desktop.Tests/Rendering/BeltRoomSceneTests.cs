using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class BeltRoomSceneTests
{
    private static string Module
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_MODULE");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) Assert.Inconclusive("Explicit independent room DLL is required.");
            return path!;
        }
    }
    private static readonly LogicalRect Bounds = new(400,500,160,120);
    private static readonly TimeSpan Dt = TimeSpan.FromSeconds(1d/60);

    [TestMethod]
    public void NativeFrameMappingIsExactAndDetachedForAllBodyControls()
    {
        using var scene = new DesktopPhysicsScene(Module,Bounds,new(120,100,0));
        using var expected = new DangoBeltRoomWorld(Module,DangoBodyMaterial.Configure(new(120,100,0)),left:-256,right:256);
        Assert.IsTrue(scene.UsesBeltRoom);
        Assert.AreEqual(1,scene.Count);
        Assert.AreEqual(1,scene.SelectedEntity.Value);
        Assert.AreEqual(Bounds.Bottom,scene.GroundY,1e-10);
        var nativePoint = new LogicalPoint(1.25,-2.5);
        var host = scene.ToHost(nativePoint);
        Assert.AreEqual(90d,host.X-Bounds.Center.X,1e-10);
        Assert.AreEqual(180d,host.Y-scene.ToHost(new(0,0)).Y,1e-10);
        Assert.AreEqual(nativePoint,scene.ToWorld(host));
        var initial = scene.BeltRoomFrame!;
        for (var i=0;i<30;i++)
        {
            scene.SetInput(new(false,true,false,false) { Stretch = i<5 });
            var frame = scene.Step(Dt);
            expected.Step(1,new(Horizontal:1,Stretch:i<5?1:0,InputActive:true));
            var raw = expected.Read();
            CollectionAssert.AreEqual(raw.Curve,scene.BeltRoomFrame!.Curve);
            var body = frame.Bodies.Single();
            Assert.AreEqual(PhysicsPresentationBackend.ContinuousBelt,body.Presentation.Diagnostics.Backend);
            Assert.AreEqual(256,body.Presentation.Contour.Count);
            Assert.AreEqual(scene.ToHost(new(raw.Stats[0],raw.Stats[1])),scene.SelectedCoreHost);
            Assert.AreEqual(scene.SelectedCoreHost.X-80,body.Bounds.X,1e-10);
            Assert.AreEqual(scene.SelectedCoreHost.Y-66,body.Bounds.Y,1e-10);
            for(var n=0;n<256;n++)
            {
                Assert.AreEqual(raw.Curve[n*2]-raw.Stats[0],body.Presentation.Contour[n].X,1e-10);
                Assert.AreEqual(raw.Curve[n*2+1]-raw.Stats[1],body.Presentation.Contour[n].Y,1e-10);
            }
        }
        Assert.AreEqual(0UL,initial.Tick);
        Assert.AreEqual(30UL,scene.BeltRoomFrame!.Tick);
    }

    [TestMethod]
    public void JumpEdgesAndDragTargetsMatchTheSingleNativeOwner()
    {
        using var scene=new DesktopPhysicsScene(Module,Bounds,DangoBodyControls.Default);
        using var reference=new DangoBeltRoomWorld(Module,DangoBodyMaterial.Configure(DangoBodyControls.Default),left:-256,right:256);
        for(var i=0;i<180;i++)
        {
            var jump=i is >=100 and <105;
            var drag=i is >=140 and <145;
            var downward=i is >=120 and <125;
            scene.SetInput(new(false,false,jump,downward));
            if(i==140) scene.BeginDrag(scene.SelectedEntity,scene.ToHost(new(.3,1.1)));
            if(i==142) scene.MoveDrag(scene.ToHost(new(.4,1.2)));
            if(i==145) scene.EndDrag();
            var target=i<142?new LogicalPoint(.3,1.1):new LogicalPoint(.4,1.2);
            scene.Step(Dt);
            reference.Step(1,new(Downward:downward?1:0,JumpPressed:i==100,DragActive:drag,
                DragTargetX:target.X,DragTargetY:target.Y,InputActive:jump||downward||drag));
            Assert.AreEqual(0,scene.LastErrorCode);
            CollectionAssert.AreEqual(reference.Read().Curve,scene.BeltRoomFrame!.Curve);
        }
    }

    [TestMethod]
    public void LegacyCapabilityModuleIsRejectedByDesktopConstructor()
    {
        var path=Environment.GetEnvironmentVariable("DANGO_BELT_ROOM_LEGACY_MODULE");
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)) Assert.Inconclusive("Explicit legacy capability-15 room module required.");
        Assert.ThrowsExactly<NotSupportedException>(()=>new DesktopPhysicsScene(path!,Bounds,DangoBodyControls.Default));
    }

    [TestMethod]
    public void BoundsPauseCancelAndRehostNeverAdvancePhysics()
    {
        using var scene = new DesktopPhysicsScene(Module,Bounds,DangoBodyControls.Default);
        scene.Step(Dt);
        var before = scene.BeltRoomFrame!;
        scene.SetHorizontalBounds(0,1000);
        var changed = scene.BeltRoomFrame!;
        Assert.AreEqual(before.Tick,changed.Tick);
        Assert.AreEqual(before.SceneRevision+1,changed.SceneRevision);
        CollectionAssert.AreEqual(before.Curve,changed.Curve);
        scene.SetHorizontalBounds(0,1000);
        Assert.AreEqual(changed.SceneRevision,scene.BeltRoomFrame!.SceneRevision);
        scene.BeginDrag(scene.SelectedEntity,scene.ToHost(new(0,1)));
        scene.CancelInput();
        scene.SetPaused(true);
        Assert.ThrowsExactly<InvalidOperationException>(()=>scene.Step(Dt));
        scene.Rehost(new(300,400));
        Assert.AreEqual(460d,scene.GroundY,1e-10);
        Assert.IsTrue(scene.RehostSelectedEntityAt(new(500,600)));
        Assert.AreEqual(660d,scene.GroundY,1e-10);
        Assert.AreEqual(before.Tick,scene.BeltRoomFrame!.Tick);
        CollectionAssert.AreEqual(changed.BodySnapshot,scene.BeltRoomFrame!.BodySnapshot);
        Assert.IsTrue(scene.TryGetHorizontalPlacement(new(-100,600),0,1000,out var safe));
        Assert.AreEqual(-(before.Stats[8]-before.Stats[0])*72d,safe.X,1e-7);
        Assert.IsFalse(scene.TryGetHorizontalPlacement(new(0,0),0,1,out _));
        scene.SetPaused(false);
        scene.ClearHorizontalBounds();
        var previousOwner = scene.BeltRoomFrame!.OwnerEpoch;
        scene.Reset();
        Assert.AreNotEqual(previousOwner,scene.BeltRoomFrame!.OwnerEpoch);
        Assert.AreEqual(0UL,scene.BeltRoomFrame.Tick);
    }

    [TestMethod]
    public void ResetAfterNonzeroBoundsSpawnsInsideTheAcceptedInterval()
    {
        using var scene = new DesktopPhysicsScene(Module,Bounds,DangoBodyControls.Default);
        scene.SetInput(new(false,true,false,false));
        for(var i=0;i<240;i++) scene.Step(Dt);
        scene.CancelInput();
        var core=scene.BeltRoomFrame!.Stats[0];
        Assert.IsGreaterThan(1.5d,core);
        var left=core-1.5d;
        var right=core+1.5d;
        scene.SetHorizontalBounds(scene.ToHost(new(left,0)).X,scene.ToHost(new(right,0)).X);
        scene.Reset();
        Assert.AreEqual((left+right)/2,scene.BeltRoomFrame!.Stats[0],1e-5);
        scene.Step(Dt);
        Assert.AreEqual(0,scene.LastErrorCode);
        Assert.AreEqual(1UL,scene.BeltRoomFrame.Tick);
    }

    [TestMethod]
    public void UnsupportedCapabilitiesAndForeignOwnershipFailExplicitly()
    {
        var scene = new DesktopPhysicsScene(Module,Bounds,DangoBodyControls.Default);
        try
        {
            Assert.IsFalse(scene.SupportsFurnitureCollection);
            Assert.IsFalse(scene.SupportsLiveFurnitureEditing);
            Assert.ThrowsExactly<NotSupportedException>(()=>scene.Add(1,0));
            Assert.ThrowsExactly<NotSupportedException>(()=>scene.SetPreviewJumpSpeedScale(2));
            Assert.ThrowsExactly<NotSupportedException>(()=>scene.SetUnboundedHorizontalPreview(true));
            Assert.ThrowsExactly<NotSupportedException>(()=>scene.ReadFurnitureFrame());
            Assert.ThrowsExactly<NotSupportedException>(()=>scene.ReadFurnitureCollectionFrame());
            Assert.IsFalse(scene.Remove(scene.SelectedEntity));
            Task.Run(()=> {
                Assert.ThrowsExactly<InvalidOperationException>(()=>scene.Step(Dt));
                Assert.ThrowsExactly<InvalidOperationException>(()=>scene.Dispose());
            }).GetAwaiter().GetResult();
        }
        finally {scene.Dispose();}
        scene.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(()=>scene.Step(Dt));
    }

    [TestMethod]
    public void GuardFailureRetainsSuccessfulPresentationAndSeparateRawDiagnostics()
    {
        using var scene = new DesktopPhysicsScene(Module,Bounds,DangoBodyControls.Default);
        scene.SetHorizontalBounds(scene.ToHost(new(-2,0)).X,scene.ToHost(new(2,0)).X);
        DesktopPhysicsSceneFrame? safe = null;
        for(var i=0;i<1200 && scene.LastErrorCode==0;i++)
        {
            safe=scene.LastFrame;
            scene.SetInput(new(false,true,false,false));
            scene.Step(Dt);
        }
        Assert.AreNotEqual(0,scene.LastErrorCode,"Expected native containment guard to reject walking outside finite bounds.");
        Assert.AreSame(safe,scene.LastFrame);
        Assert.IsTrue(scene.LastFrame!.Physics.Succeeded);
        Assert.AreEqual(scene.LastErrorCode,scene.BeltRoomFrame!.Fault);
        Assert.AreNotEqual(0d,scene.BeltRoomFrame.Diagnostics[2]);
        Assert.AreSame(safe,scene.Step(Dt));
        scene.Reset();
        Assert.AreEqual(0,scene.LastErrorCode);
    }
}
