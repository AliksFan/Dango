using System.Runtime.InteropServices;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Physics.Presentation;

[TestClass]
public sealed class DangoInterBodyCollisionTests
{
    [TestMethod]
    public void SharedSelfWorldExportsAreAvailable()
    {
        var library = NativeLibrary.Load(Box2DNativeLoader.DefaultLibraryPath);
        try
        {
            foreach (var operation in new[] { "create", "step", "destroy" })
            {
                var name = $"dango_box2d_self_bodies_{operation}";
                Assert.IsTrue(NativeLibrary.TryGetExport(library, name, out _), name);
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [TestMethod]
    public void PopulationCanGrowToThreeAndRemoveOnlyAdditionalParticipants()
    {
        using var runtime = new StackedDangoSceneRuntime();
        Assert.AreEqual(1, runtime.DangoCount);
        Assert.IsFalse(runtime.RemoveDango());
        Assert.IsTrue(runtime.AddDango());
        Assert.AreEqual(2, runtime.DangoCount);
        Assert.IsTrue(runtime.AddDango());
        Assert.AreEqual(3, runtime.DangoCount);
        Assert.IsFalse(runtime.AddDango());
        Assert.IsTrue(runtime.RemoveDango());
        Assert.AreEqual(2, runtime.DangoCount);
        Assert.IsTrue(runtime.RemoveDango());
        Assert.AreEqual(1, runtime.DangoCount);
        Assert.IsFalse(runtime.RemoveDango());
    }
}
