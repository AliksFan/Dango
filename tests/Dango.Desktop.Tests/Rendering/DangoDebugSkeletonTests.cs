namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DangoDebugSkeletonTests
{
    [TestMethod]
    public void DebugSkeletonRestoresCompleteP2D007Topology()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "Rendering",
            "Dango",
            "DeformableDangoVisual.xaml.cs");
        var source = File.ReadAllText(path);
        var start = source.IndexOf("private void DrawDebug", StringComparison.Ordinal);
        var end = source.IndexOf("private static Transform AttachmentTransform", start, StringComparison.Ordinal);
        var debugRenderer = source[start..end];

        StringAssert.Contains(debugRenderer, "CreateRadialLine(corePoint, points[index])");
        StringAssert.Contains(debugRenderer, "CreateContourLine(points[index], points[(index + 1) % points.Length])");
        StringAssert.Contains(debugRenderer, "CreateNode(points[index])");
        StringAssert.Contains(debugRenderer, "DebugLayer.Children.Add(core)");
    }
}
