using System.Diagnostics;
using System.Windows;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Rig;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryRigMechanismTests
{
    public TestContext TestContext { get; set; } = null!;
    private static AccessoryRigDefinition Curve() => new([new(.2, .2), new(.4, .2), new(.55, .3), new(.65, .5)]);
    private static AccessoryRigDefinition Vertical() => new([new(.5, .1), new(.5, .3), new(.5, .5), new(.5, .7)]);
    private static AccessoryRigPose Pose(AccessoryRigDefinition rig) => new(rig.RestNodes[0]);

    [TestMethod]
    public void InvalidCenterlinesAndParametersAreRejected()
    {
        Point[][] cases = [[], [new(.1,.1)], [new(.1,.1),new(.1,.1)], [new(double.NaN,.1),new(.2,.2)],
            [new(-.1,.1),new(.2,.2)], [new(.1,.1),new(.8,.1),new(.2,.1)],
            [new(.1,.1),new(.9,.9),new(.1,.9),new(.9,.1)], Enumerable.Range(0,18).Select(i=>new Point(.1, i/20d)).ToArray()];
        foreach (var points in cases) Assert.Throws<ArgumentException>(() => new AccessoryRigDefinition(points));
        Assert.Throws<ArgumentException>(() => new AccessoryRigSolver(Curve(), new(Softness: double.NaN)));
        Assert.Throws<ArgumentException>(() => new AccessoryRigSolver(Curve(), new(Gravity: 5)));
        // Folded parallel limbs have an equidistant nonlocal binding at the center of the image.
        var folded = new AccessoryRigDefinition([new(.2,.2),new(.8,.2),new(.8,.8),new(.2,.8)]);
        Assert.Throws<ArgumentException>(() => new AccessoryRigSkin(folded));
    }

    [TestMethod]
    public void ImmutableTopologyWeightsRestIdentityAndPivotAreExact()
    {
        var source = new[] {new Point(.213,.127),new Point(.4,.25),new Point(.6,.5),new Point(.7,.8)};
        var rig = new AccessoryRigDefinition(source); source[0] = new(0,0);
        var skin = new AccessoryRigSkin(rig); var solver = new AccessoryRigSolver(rig);
        solver.Advance(0,1d/60,Pose(rig)); var frame = skin.Deform(solver.Refresh());
        Assert.IsTrue(frame.Vertices.Count <= AccessoryRigLimits.MaximumMeshVertices);
        Assert.IsTrue(frame.TriangleIndices.Count/3 <= AccessoryRigLimits.MaximumMeshTriangles);
        CollectionAssert.AreEqual(frame.TextureCoordinates.ToArray(),frame.Vertices.ToArray());
        Assert.AreEqual(Pose(rig).Root,frame.Vertices[skin.PivotVertexIndex]);
        Assert.IsTrue(skin.Weights.All(w=>w.FirstWeight>=0 && w.FirstWeight<=1 && w.FirstWeight+w.SecondWeight==1 && Math.Abs(w.FirstBone-w.SecondBone)<=1));
        Assert.Throws<NotSupportedException>(()=>((IList<Point>)skin.TextureCoordinates)[0]=new());
        Assert.Throws<ArgumentException>(()=>skin.Deform(new AccessoryRigSolver(Vertical()).Initialized()));
    }

    [TestMethod]
    public void SoftnessZeroPreservesRestCurveUnderMovingRotatingRoot()
    {
        var rig=Curve(); var solver=new AccessoryRigSolver(rig,new(Softness:0,Gravity:4));
        for(var tick=0;tick<100;tick++)
        {
            var pose=new AccessoryRigPose(new(.2+tick*.001,.2),tick*.002,2);
            solver.Advance(tick,1d/60,pose); var frame=solver.Refresh();
            for(var i=0;i<rig.RestNodes.Count;i++) Assert.AreEqual(pose.Transform(rig.RestNodes[i],rig.RestNodes[0]),frame.Nodes[i]);
        }
    }

    [TestMethod]
    public void GravityAndActualRootMovementProduceRelativeBendingWithFixedLengths()
    {
        var rig=Curve(); var gravity=new AccessoryRigSolver(rig,new(Restoration:0,Gravity:1));
        var noGravity=new AccessoryRigSolver(rig,new(Restoration:0,Gravity:0));
        for(var t=0;t<100;t++) {gravity.Advance(t,1d/60,Pose(rig));noGravity.Advance(t,1d/60,Pose(rig));}
        Assert.IsTrue(gravity.Refresh().Nodes[^1].Y-noGravity.Refresh().Nodes[^1].Y>.1,"No-gravity implementation must fail.");
        var hanging=Vertical();var inertia=new AccessoryRigSolver(hanging,new(Restoration:0,Gravity:0));
        inertia.Advance(0,1d/60,Pose(hanging));
        for(var t=1;t<=20;t++) inertia.Advance(t,1d/60,new(new(.5+t*.008,.1)));
        var moved=inertia.Refresh(); Assert.IsTrue(moved.Nodes[^1].X < moved.Pose.Root.X-.03,"Rigid translation or scripted gravity cannot satisfy root-driven inertia.");
        Assert.IsTrue(Math.Abs((moved.Nodes[^1]-moved.Nodes[0]).Length-(hanging.RestNodes[^1]-hanging.RestNodes[0]).Length)>.001,"A whole-image transform cannot satisfy relative bending.");
        foreach(var frame in new[]{gravity.Refresh(),moved})
        {
            Assert.AreEqual(frame.Pose.Root,frame.Nodes[0]);
            for(var i=1;i<frame.Nodes.Count;i++) Assert.AreEqual(frame.Definition.BoneLengths[i-1]*frame.Pose.Scale,(frame.Nodes[i]-frame.Nodes[i-1]).Length,1e-10);
        }
    }

    [TestMethod]
    public void DampingAndRestorationHaveIndependentMeasurableEffects()
    {
        var rig=Vertical();
        AccessoryRigSolver Excited(double damping,double restoration)
        {
            var solver=new AccessoryRigSolver(rig,new(1,damping,restoration,0));solver.Advance(0,1d/60,Pose(rig));
            for(var t=1;t<=15;t++)solver.Advance(t,1d/60,new(new(.5+t*.006,.1)));
            return solver;
        }
        var undamped=Excited(0,0);var damped=Excited(25,0);
        double Travel(AccessoryRigSolver solver)
        {
            var travel=0d;var prior=solver.Refresh().Nodes[^1];
            for(var t=16;t<90;t++){solver.Advance(t,1d/60,new(new(.59,.1)));var p=solver.Refresh().Nodes[^1];travel+=(p-prior).Length;prior=p;}
            return travel;
        }
        var a=Travel(undamped);var b=Travel(damped);
        Assert.IsTrue(a>b*2,$"Damping must reduce subsequent travel: {a} versus {b}.");
        var restoring=Excited(10,12);var unrestored=Excited(10,0);
        Travel(restoring);Travel(unrestored);
        var expected=new Point(.59,.7);
        Assert.IsTrue((restoring.Refresh().Nodes[^1]-expected).Length < (unrestored.Refresh().Nodes[^1]-expected).Length*.3,"Restoration must recover the stored rest curve.");
    }

    [TestMethod]
    public void OwnerTickRefreshLongFramePauseResumeAndResetClearDynamics()
    {
        var rig=Curve();var solver=new AccessoryRigSolver(rig);solver.Advance(10,1d/60,Pose(rig));
        var before=solver.Refresh();
        Assert.IsFalse(solver.Advance(10,1d/60,new(new(.3,.3))));Assert.IsFalse(solver.Advance(9,1d/60,new(new(.3,.3))));
        for(var i=0;i<10;i++)CollectionAssert.AreEqual(before.Nodes.ToArray(),solver.Refresh().Nodes.ToArray());
        solver.Pause();Assert.IsFalse(solver.Advance(99,1,new(new(.9,.9))));
        solver.Resume(100,new(new(.7,.7)));var resumed=solver.Refresh();
        solver.Advance(101,1d/60,new(new(.7,.7)));
        var clean=new AccessoryRigSolver(rig);clean.Resume(100,new(new(.7,.7)));clean.Advance(101,1d/60,new(new(.7,.7)));
        CollectionAssert.AreEqual(clean.Refresh().Nodes.ToArray(),solver.Refresh().Nodes.ToArray());
        solver.Advance(102,10,new(new(.8,.8)));Assert.AreEqual(new Point(.8,.8),solver.Refresh().Nodes[0]);
        solver.Reset(Vertical(),new(Gravity:0),new(new(.5,.1)));Assert.AreEqual(-1L,solver.Refresh().Tick);
        Assert.AreEqual(Vertical().RestNodes[^1],solver.Refresh().Nodes[^1]);
        Assert.AreEqual(new Point(.7,.7),resumed.Nodes[0],"Previously published snapshots cannot mutate.");
    }

    [TestMethod]
    public void AspectCorrectMeshDeformsAndRecoversTheOriginalCurvedShape()
    {
        var rig=new AccessoryRigDefinition(Curve().RestNodes,2);
        var solver=new AccessoryRigSolver(rig,new(1,10,12,0));var skin=new AccessoryRigSkin(rig);
        var root=new AccessoryRigPose(rig.LocalNodes[0]);solver.Advance(0,1d/60,root);
        var rest=skin.Deform(solver.Refresh());
        for(var i=0;i<rest.Vertices.Count;i++) Assert.AreEqual(rig.ToLocal(rest.TextureCoordinates[i]),rest.Vertices[i]);
        for(var t=1;t<=20;t++)solver.Advance(t,1d/60,new(root.Root+new Vector(t*.01,0)));
        var moving=skin.Deform(solver.Refresh());
        Assert.IsTrue(Enumerable.Range(0,rest.Vertices.Count).Any(i=>Math.Abs((rest.Vertices[i]-rest.ImagePivot).Length-(moving.Vertices[i]-moving.ImagePivot).Length)>.005),"Skinning must deform the image internally.");
        var stopped=new AccessoryRigPose(root.Root+new Vector(.2,0));
        for(var t=21;t<900;t++)solver.Advance(t,1d/60,stopped);
        var recovered=solver.Refresh();
        for(var i=0;i<rig.LocalNodes.Count;i++)Assert.IsTrue((recovered.Nodes[i]-stopped.Transform(rig.LocalNodes[i],rig.LocalNodes[0])).Length<.002,"Restoration must preserve the original curved rest shape, not straighten it.");
        Assert.AreEqual(stopped.Root,skin.Deform(recovered).Vertices[skin.PivotVertexIndex]);
    }

    [TestMethod]
    public void ExtremeParametersRemainFiniteWithExactLengthsAtMaximumNodeBudget()
    {
        var rig=new AccessoryRigDefinition(Enumerable.Range(0,17).Select(i=>new Point(.213,.04+i*.056)));
        foreach(var configuration in new[]{new AccessoryRigParameters(1,0,0,4),new AccessoryRigParameters(.01,30,30,4)})
        {
            var solver=new AccessoryRigSolver(rig,configuration);
            for(var t=0;t<2000;t++)
            {
                solver.Advance(t,1d/30,new(new(.213+Math.Sin(t*.1)*.2,.04+Math.Cos(t*.11)*.1),Math.Sin(t*.03)));
                var frame=solver.Refresh();
                Assert.IsTrue(frame.Nodes.All(AccessoryRigDefinition.Finite));
                for(var i=1;i<frame.Nodes.Count;i++)Assert.AreEqual(rig.BoneLengths[i-1],(frame.Nodes[i]-frame.Nodes[i-1]).Length,1e-10);
            }
        }
    }

    [TestMethod]
    [TestCategory("E0Performance")]
    public void MaximumBudgetOneAndEightItemNoWindowMeasurement()
    {
        var rig=new AccessoryRigDefinition(Enumerable.Range(0,17).Select(i=>new Point(.213,.04+i*.056)));
        var skin=new AccessoryRigSkin(rig);
        foreach(var count in new[]{1,8})
        {
            var solvers=Enumerable.Range(0,count).Select(_=>new AccessoryRigSolver(rig)).ToArray();
            const int warmup=2000;
            for(var t=0;t<warmup;t++)foreach(var s in solvers){s.Advance(t,1d/60,new(new(.213+Math.Sin(t*.03)*.03,.04)));skin.Deform(s.Refresh());}
            const int samples=600;var times=new double[samples];AccessoryMeshFrame? last=null;var allocated=GC.GetAllocatedBytesForCurrentThread();
            for(var t=0;t<samples;t++)
            {
                var start=Stopwatch.GetTimestamp();
                foreach(var s in solvers){s.Advance(t+warmup,1d/60,new(new(.213+Math.Sin((t+warmup)*.03)*.03,.04)));last=skin.Deform(s.Refresh());}
                times[t]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            var bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;Array.Sort(times);
            Assert.IsTrue(last!.Vertices.All(AccessoryRigDefinition.Finite));
            TestContext.WriteLine($"E0 Release managed chain+snapshot+mesh {count} items: {rig.RestNodes.Count} nodes, {skin.TextureCoordinates.Count} vertices, {skin.TriangleIndices.Count/3} triangles; median={times[samples/2]:F4}ms p95={times[(int)(samples*.95)]:F4}ms max={times[^1]:F4}ms allocations={bytes/samples} bytes/frame; logical CPUs={Environment.ProcessorCount}; runtime={Environment.Version}");
            Assert.IsTrue(times[samples/2]<16.67,"Candidate median CPU budget exceeded at maximum load.");
        }
    }
}

internal static class RigTestInitialization
{
    internal static AccessoryRigSnapshot Initialized(this AccessoryRigSolver solver){solver.Advance(0,1d/60,new(new(.5,.1)));return solver.Refresh();}
}
