using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class DangoPhysicsWorldTests
{
    private static string ModulePath => PhysicsTestLibraries.Unified;

    [TestMethod]
    public void DefaultsAreVersionedAndFreezeAcceptedShape()
    {
        var current = DangoPhysicsDefaults.Current;
        Assert.AreEqual(16, current.NodeCount);
        Assert.AreEqual(0.5, current.TopInwardForce);
        Assert.AreEqual(4, current.SubStepCount);
    }

    [TestMethod]
    public void EmptyWorldSupportsRealAddRemoveAndRejectsStaleOrOverflowIds()
    {
        using var world = DangoPhysicsWorld.Create(ModulePath, capacity: 2);
        Assert.AreEqual(0, world.Count);
        Assert.AreEqual(0, world.Step(TimeSpan.FromSeconds(1d / 60), []).Bodies.Count);
        var first = world.Add(0, 0);
        var second = world.Add(2.8, 0);
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.Add(4, 0));
        var before = world.Step(TimeSpan.FromSeconds(1d / 60), [new(first), new(second)]);
        var secondX = before.Bodies.Single(body => body.EntityId == second).CoreX;
        world.Remove(first);
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.Step(TimeSpan.FromSeconds(1d / 60), [new(first)]));
        var after = world.Step(TimeSpan.FromSeconds(1d / 60), [new(second)]);
        Assert.AreEqual(secondX, after.Bodies.Single().CoreX, 0.05f, "Removing one rig must not rebuild the other world state.");
        var replacement = world.Add(0, 0);
        Assert.AreNotEqual(first, replacement);
        Assert.ThrowsExactly<DangoPhysicsException>(() => world.Remove(first));
    }

    [TestMethod]
    public void ResetPreservesIdsAndDisposeIsIdempotent()
    {
        var world = DangoPhysicsWorld.Create(ModulePath);
        var id = world.Add(0, 0);
        for (var i = 0; i < 20; i++) world.Step(TimeSpan.FromSeconds(1d / 60), [new(id, Horizontal: 1)]);
        world.Reset();
        var frame = world.Step(TimeSpan.FromSeconds(1d / 60), [new(id)]);
        Assert.AreEqual(id, frame.Bodies.Single().EntityId);
        world.Dispose();
        world.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => world.Reset());
    }

    [TestMethod]
    public void MultipleWorldsCanOverlapWithoutProcessGlobalLeakFalsePositive()
    {
        using var first = DangoPhysicsWorld.Create(ModulePath);
        using var second = DangoPhysicsWorld.Create(ModulePath);
        var firstId = first.Add(0, 0);
        var secondId = second.Add(0, 0);
        Assert.IsTrue(first.Step(TimeSpan.FromSeconds(1d / 60), [new(firstId)]).Succeeded);
        Assert.IsTrue(second.Step(TimeSpan.FromSeconds(1d / 60), [new(secondId)]).Succeeded);
    }

    [TestMethod]
    public void WorldRejectsAccessFromAnotherThread()
    {
        using var world = DangoPhysicsWorld.Create(ModulePath);
        var error = Task.Run(() => Assert.ThrowsExactly<InvalidOperationException>(() => world.Reset())).GetAwaiter().GetResult();
        StringAssert.Contains(error.Message, "创建它的线程");
    }

    [TestMethod]
    public void MissingOrLegacyLibraryProducesRecognizableLoadFailure()
    {
        var missing = Assert.ThrowsExactly<DangoPhysicsException>(() => DangoPhysicsWorld.Create(Path.Combine(FindRoot(), "missing-module.dll")));
        Assert.AreEqual(DangoPhysicsFailureKind.MissingLibrary, missing.Kind);
        var formal = Path.Combine(FindRoot(), "src", "Dango.Desktop", "runtimes", "win-x64", "native", "dango_box2d.dll");
        var legacy = Assert.ThrowsExactly<DangoPhysicsException>(() => DangoPhysicsWorld.Create(formal));
        Assert.AreEqual(DangoPhysicsFailureKind.MissingExport, legacy.Kind);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dango.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
