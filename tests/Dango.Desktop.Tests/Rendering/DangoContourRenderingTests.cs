using System.Xml.Linq;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DangoContourRenderingTests
{
    [TestMethod]
    public void ValidContourBuildsOneClosedFilledStreamGeometry()
    {
        var contour = Ellipse(12);

        var result = DangoStreamGeometryFactory.TryCreate(contour, 160, 120);

        Assert.IsTrue(result.Succeeded, result.Failure.ToString());
        Assert.IsInstanceOfType<System.Windows.Media.StreamGeometry>(result.Geometry);
        var path = System.Windows.Media.PathGeometry.CreateFromGeometry(result.Geometry!);
        Assert.HasCount(1, path.Figures);
        Assert.IsTrue(path.Figures[0].IsClosed);
        Assert.IsTrue(path.Figures[0].IsFilled);
    }

    [TestMethod]
    public void InvalidContoursAreRejectedBeforeWpfReceivesThem()
    {
        var reversed = Ellipse(12).Reverse().ToArray();
        var selfIntersecting = new[]
        {
            new PresentationPoint(-0.7, -0.7),
            new PresentationPoint(0.7, 0.7),
            new PresentationPoint(-0.7, 0.7),
            new PresentationPoint(0.7, -0.7),
        };
        var footSpike = Ellipse(12).ToArray();
        footSpike[9] = new PresentationPoint(-0.72, -0.98);

        Assert.AreEqual(DangoContourFailure.Reversed, DangoContourValidator.Validate(reversed));
        Assert.AreEqual(DangoContourFailure.SelfIntersecting, DangoContourValidator.Validate(selfIntersecting));
        Assert.AreEqual(DangoContourFailure.FootLikeSpike, DangoContourValidator.Validate(footSpike));
    }

    [TestMethod]
    public void VisualXamlUsesThreePlainLayersWithoutMouthOrEffects()
    {
        var path = Path.Combine(
            TestRepository.Root,
            "src",
            "Dango.Desktop",
            "Rendering",
            "Dango",
            "DeformableDangoVisual.xaml");
        var root = XDocument.Load(path).Root!;
        var names = root.Descendants()
            .Select(element => (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(name => name is not null)
            .ToArray();

        CollectionAssert.Contains(names, "BodyPath");
        CollectionAssert.Contains(names, "LeftEyeLayer");
        CollectionAssert.Contains(names, "RightEyeLayer");
        CollectionAssert.Contains(names, "HatLayer");
        Assert.IsFalse(names.Any(name => name!.Contains("Mouth", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(root.Descendants().Any(element =>
            element.Name.LocalName.Contains("Effect", StringComparison.OrdinalIgnoreCase) ||
            element.Name.LocalName.Contains("Gradient", StringComparison.OrdinalIgnoreCase)));
        var body = root.Descendants().Single(element =>
            (string?)element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "BodyPath");
        Assert.IsNull(body.Attribute("Stroke"));
        Assert.AreEqual("#FFFFE8C2", (string?)body.Attribute("Fill"), ignoreCase: true);
    }

    [TestMethod]
    public void RendererCannotOwnOrStepAPhysicsWorld()
    {
        var rendererType = typeof(DeformableDangoVisual);
        Assert.IsFalse(rendererType.GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)
            .Any(field => typeof(IPhysicsPresentationWorld).IsAssignableFrom(field.FieldType)));
        Assert.IsFalse(rendererType.GetMethods().Any(method => method.Name == "Step"));
        Assert.IsTrue(typeof(IPhysicsPresentationSink).IsAssignableFrom(rendererType));
    }

    private static IReadOnlyList<PresentationPoint> Ellipse(int count) =>
        Enumerable.Range(0, count)
            .Select(index => (Math.PI * 2 * index) / count)
            .Select(angle => new PresentationPoint(Math.Cos(angle) * 0.72, Math.Sin(angle) * 0.82))
            .ToArray();
}
