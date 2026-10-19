using Dango.Desktop.Physics.Tuning;

namespace Dango.Physics.Tests;

[TestClass]
[DoNotParallelize]
public sealed class DangoBeltWorldTests
{
    private static string Module
    {
        get
        {
            var value=Environment.GetEnvironmentVariable("DANGO_BELT_MODULE");
            if(string.IsNullOrWhiteSpace(value)||!File.Exists(value))
                Assert.Inconclusive("Set DANGO_BELT_MODULE to the independently built product belt DLL.");
            return value!;
        }
    }
    private static DangoSelfPhysicsParameters Config => new()
    { RadialElasticity=1.82,PerimeterElasticity=15.075,TopInwardForce=0,GroundDeformationStrength=0 };

    [TestMethod]
    public void ProductHasFrozenIdentityAndSuccessfulTickSurvivesMaterialChange()
    {
        using var world=new DangoBeltWorld(Module,Config);
        Assert.AreEqual(1.4,world.Calibration.RadialStiffnessScale);
        Assert.AreEqual(Config.OverallDamping,world.Calibration.OverallDamping);
        Assert.IsTrue(world.ShapeGuardEnabled&&world.SpeedSaturationEnabled&&world.RadialDriveEnabled&&world.RearDriveEnabled&&world.OriginalGroundJumpEnabled);
        world.Step(DangoBeltWorld.FixedDt,default);
        var before=world.ReadDebugState();
        var config=Config with {RadialElasticity=Config.RadialElasticity*.8};
        world.Reconfigure(config);
        var after=world.ReadDebugState();
        CollectionAssert.AreEqual(before[2..74],after[2..74],"Material edits must preserve q/v.");
        Assert.AreEqual(before[139],after[139],"Configuration is not a new physical tick.");
        Assert.AreEqual(config,world.Configuration);
        world.Step(DangoBeltWorld.FixedDt,default);
        Assert.AreEqual(before[139]+1,world.ReadDebugState()[139]);
    }

    [TestMethod]
    public void RejectedConfigurationPreservesPhysicalAndManagedStateAndCanContinue()
    {
        using var world=new DangoBeltWorld(Module,Config);
        world.Step(DangoBeltWorld.FixedDt,default);
        var before=world.ReadDebugState();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>world.Reconfigure(Config with {CoreMass=double.NaN}));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(()=>world.Reconfigure(Config,new(){AreaScale=-1}));
        Assert.ThrowsExactly<InvalidOperationException>(()=>world.Reconfigure(Config with {GroundHeight=-.8}));
        CollectionAssert.AreEqual(before,world.ReadDebugState());
        Assert.AreEqual(Config,world.Configuration);
        world.Step(DangoBeltWorld.FixedDt,default);
        Assert.AreEqual(before[139]+1,world.ReadDebugState()[139]);
    }

    [TestMethod]
    public void GeometryResizePreservesFootAndNativeOwnerAndRejectsForeignThread()
    {
        using var world=new DangoBeltWorld(Module,Config);
        world.Step(DangoBeltWorld.FixedDt,new(Horizontal:1));
        var before=world.Read();
        var bigger=Config with {RadialRestLength=Config.RadialRestLength*1.2,NodeRadius=Config.NodeRadius*1.2,
            CoreMass=Config.CoreMass*1.44,NodeMass=Config.NodeMass*1.44,MinimumArea=Config.MinimumArea*1.44};
        world.Reconfigure(bigger);
        var after=world.Read();
        Assert.AreEqual(before.MinimumY,after.MinimumY,1e-6);
        Assert.AreEqual(before.Width*1.2,after.Width,1e-6);
        Assert.AreEqual(before.TotalMass*1.44,after.TotalMass,1e-6);
        var rejected=Task.Run(()=>Assert.ThrowsExactly<InvalidOperationException>(()=>world.Read()));
        rejected.GetAwaiter().GetResult();
        world.Step(DangoBeltWorld.FixedDt,default);
    }

    [TestMethod]
    public void FrozenLabAbiCannotMasqueradeAsProductIdentity()
    {
        var frozen=Environment.GetEnvironmentVariable("DANGO_BELT_FROZEN_MODULE");
        if(string.IsNullOrWhiteSpace(frozen)||!File.Exists(frozen))Assert.Inconclusive("Set DANGO_BELT_FROZEN_MODULE to the frozen C455 lab DLL.");
        Assert.ThrowsExactly<EntryPointNotFoundException>(()=>new DangoBeltWorld(frozen!,Config));
    }

    [TestMethod]
    public void SeparateManagedOwnersSerializeTheInheritedNativeHandleRegistry()
    {
        var module=Module;
        float[] Run()
        {
            using var world=new DangoBeltWorld(module,Config);
            for(var i=0;i<12;i++)world.Step(DangoBeltWorld.FixedDt,new(Horizontal:1));
            return world.Read().Curve;
        }
        var first=Task.Run(Run);var second=Task.Run(Run);
        Task.WhenAll(first,second).GetAwaiter().GetResult();
        CollectionAssert.AreEqual(first.Result,second.Result);
    }
}
