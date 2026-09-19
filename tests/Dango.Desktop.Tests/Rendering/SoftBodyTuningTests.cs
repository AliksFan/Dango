using System.Text.Json;
using System.Xml.Linq;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Native;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Tuning;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class SoftBodyTuningTests
{
    [TestMethod]
    public void DefaultsMatchUserApprovedLandingPreset()
    {
        var expected = new SoftBodyTuningParameters
        {
            RestWidth = 1.4196926032660917,
            RestHeight = 1.192026897214217,
            TopRoundness = 0.17662760166506597,
            LowerBulge = 0.2008888888888887,
            BottomFlatness = 0.1173333333333337,
            GroundCompression = 0.0925335894972781,
            DropHeight = 0.5,
            ImpactSpeed = 1.7371757925072033,
            MaximumCompression = 0.27300672430355444,
            LateralExpansion = 0.10920268972142176,
            RadialElasticity = 7.953890489913556,
            ContourElasticity = 7.0172910662823895,
            OverallDamping = 2.3439000960614784,
            ContactSoftness = 21.00672430355428,
        };

        var actual = new SoftBodyTuningParameters();

        actual.Validate();
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void JsonRoundTripsAndRejectsUnknownOrInvalidValues()
    {
        var original = new SoftBodyTuningParameters { NodeCount = 12, RestWidth = 1.66 };
        Assert.AreEqual(original, SoftBodyTuningParameters.FromJson(original.ToJson()));
        Assert.ThrowsExactly<JsonException>(() => SoftBodyTuningParameters.FromJson("{\"Unknown\":1}"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SoftBodyTuningParameters.FromJson(original.ToJson().Replace("1.66", "99")));
    }

    [TestMethod]
    public void DiscreteQualityParametersAcceptOnlyTheirSupportedIntegerChoices()
    {
        var original = new SoftBodyTuningParameters();

        var twelveNodes = original.WithNumericValue(nameof(SoftBodyTuningParameters.NodeCount), 12);
        var twoSubsteps = twelveNodes.WithNumericValue(nameof(SoftBodyTuningParameters.SubStepCount), 2);

        Assert.AreEqual(12, twelveNodes.NodeCount);
        Assert.AreEqual(2, twoSubsteps.SubStepCount);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            original.WithNumericValue(nameof(SoftBodyTuningParameters.SubStepCount), 3));
    }

    [TestMethod]
    public void TuningWindowContainsEveryApprovedControlAndMetric()
    {
        var path = Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "Rendering", "Dango", "SoftBodyPreviewWindow.xaml");
        var root = XDocument.Load(path).Root!;
        var names = root.Descendants().Select(x => (string?)x.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))).Where(x => x is not null).ToHashSet();
        string[] controls = ["ParameterPanel", "ResetDropButton", "PauseToggle", "SlowMotionToggle", "CompareToggle", "DebugToggle", "ContourOnlyToggle", "ResetDefaultsButton", "SaveButton", "LoadButton", "MetricsText"];
        foreach (var control in controls) Assert.IsTrue(names.Contains(control), $"缺少控件 {control}");
        string[] groups = ["常态轮廓", "落地动作", "果冻材质", "地面接触", "模拟质量"];
        var text = File.ReadAllText(path);
        foreach (var group in groups) StringAssert.Contains(text, group);
        StringAssert.Contains(File.ReadAllText(Path.Combine(TestRepository.Root, "src", "Dango.Desktop", "Rendering", "Dango", "SoftBodyPreviewWindow.xaml.cs")), "增大：");
    }

    [TestMethod]
    public void ConfiguredBox2DDropsFromRoundAirShapeIntoWiderGroundedShape()
    {
        var parameters = new SoftBodyTuningParameters();
        using var world = Box2DPhysicsPresentationAdapter.CreateTuning(Box2DNativeLoader.DefaultLibraryPath, parameters);
        var input = new PhysicsPresentationInput("tuning-test", new LogicalRect(0, 0, 160, 120), DangoActionState.Idle, 1, 1, "ground", new LogicalVector(0, -1), 0, 1, false, true, []);
        var first = world.Step(input, TimeSpan.FromSeconds(parameters.FixedStepSeconds));
        PhysicsPresentationFrame settled = first;
        for (var index = 0; index < 240; index++) settled = world.Step(input, TimeSpan.FromSeconds(parameters.FixedStepSeconds));

        Assert.AreEqual(PhysicsPresentationFallbackReason.None, first.FallbackReason, $"native {first.Diagnostics.NativeErrorCode}");
        Assert.AreEqual(PhysicsPresentationFallbackReason.None, settled.FallbackReason, $"native {settled.Diagnostics.NativeErrorCode}");
        Assert.IsTrue(Aspect(first.Contour) is > 0.9 and < 1.1, $"air aspect={Aspect(first.Contour)}");
        Assert.IsTrue(Aspect(settled.Contour) > 1.12, $"ground aspect={Aspect(settled.Contour)}");
    }

    private static double Aspect(IReadOnlyList<PresentationPoint> contour) =>
        (contour.Max(point => point.X) - contour.Min(point => point.X)) /
        (contour.Max(point => point.Y) - contour.Min(point => point.Y));
}
