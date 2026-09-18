using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class ProductEntryRuntimeTests
{
    private static PhysicsPresentationInput Primary => new("fixture", new LogicalRect(100, 200, 160, 120),
        DangoActionState.Idle, 1000, 17, "ground", new(0, -1), 0, 1, false, true, []);

    [TestMethod]
    public void ProductInitializesOnceAndHatChangesPreserveTheWorldAndFrame()
    {
        using var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.AreEqual(SceneInitializationState.NotStarted, runtime.InitializationState);
        Assert.AreEqual(0, runtime.EntityIds.Count);
        Assert.IsTrue(runtime.TryInitialize());
        Assert.IsTrue(runtime.AttachmentsEnabled);
        Assert.AreEqual(240, runtime.CalibrationStepCount);
        Assert.AreEqual(HatAppearance.Sprout, runtime.CurrentHatAppearance);
        Assert.AreEqual(1.35, runtime.DisplayHatSize);
        var id = runtime.EntityIds.Single();
        runtime.Start();
        var frame = runtime.Tick(Primary, TimeSpan.FromSeconds(1d / 60));
        Assert.IsNotNull(frame.Bodies[0].Attachments);
        foreach (var preset in new[] { "top-hat", "bowler", "sprout" })
        {
            Assert.IsTrue(runtime.TryApplyHatPreset(preset));
            runtime.Stop();
            Assert.IsTrue(runtime.TryInitialize());
            runtime.Start();
            Assert.AreEqual(id, runtime.EntityIds.Single());
            Assert.AreSame(frame, runtime.LastFrame);
            Assert.AreEqual(240, runtime.CalibrationStepCount);
        }
        Assert.IsFalse(runtime.TryApplyHatPreset("not-a-hat"));
        Assert.AreEqual("sprout", runtime.CurrentHatPresetId);
        runtime.RegenerateDango();
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.AreEqual("sprout", runtime.CurrentHatPresetId);
    }

    [TestMethod]
    public void MissingModuleKeepsUsableStateAndExplicitRetryCreatesOneWorld()
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "product-entry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DangoPhysicsWorld.NativeLibraryFileName);
        using var runtime = StackedDangoSceneRuntime.CreateProduct(path);
        Assert.IsFalse(runtime.TryInitialize());
        Assert.AreEqual(SceneInitializationState.Unavailable, runtime.InitializationState);
        Assert.AreEqual(0, runtime.EntityIds.Count);
        Assert.IsNotNull(runtime.LastError);
        StringAssert.Contains(runtime.CreateDiagnosticSummary(), "Unavailable");
        Assert.ThrowsExactly<InvalidOperationException>(runtime.Start);
        Assert.IsTrue(runtime.TryApplyHatPreset("bowler"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, DangoPhysicsWorld.NativeLibraryFileName), path);
        Assert.IsTrue(runtime.TryInitialize());
        Assert.IsNull(runtime.LastError);
        Assert.AreEqual(SceneInitializationState.Ready, runtime.InitializationState);
        var id = runtime.EntityIds.Single();
        Assert.IsTrue(runtime.TryInitialize());
        Assert.AreEqual(id, runtime.EntityIds.Single());
        Assert.AreEqual("bowler", runtime.CurrentHatPresetId);
    }

    [TestMethod]
    public void InvalidModuleDoesNotEscapeTheInitializationBoundary()
    {
        var directory = Path.Combine(TestRepository.Root, "artifacts", "test-runs", "product-entry", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "invalid.dll");
        File.WriteAllText(path, "fictional invalid module");
        using var runtime = StackedDangoSceneRuntime.CreateProduct(path);
        Assert.IsFalse(runtime.TryInitialize());
        Assert.AreEqual(SceneInitializationState.Unavailable, runtime.InitializationState);
        Assert.AreEqual(0, runtime.EntityIds.Count);
        Assert.IsFalse(runtime.AttachmentsEnabled);
    }
}
