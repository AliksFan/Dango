using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class DangoAppearanceConfigurationTests
{
    [TestMethod]
    public void DefaultKeepsAcceptedColorsAndLegacyEyeControls()
    {
        var value = DangoAppearanceConfiguration.Default;
        Assert.AreEqual("#FFE8C2", value.BodyColor.ToHex());
        Assert.AreEqual("#2B201E", value.EyeColor.ToHex());
        Assert.AreEqual(EyeLayoutParameters.Default, value.EyeLayout);
    }

    [TestMethod]
    public void ExactBlackAndEqualColorsAreAllowedWithIndependentEyeLayout()
    {
        var black = OpaqueSrgbColor.Parse("#000000");
        var eyes = new EyeLayoutParameters(140, 100, 32, 12);
        var value = new DangoAppearanceConfiguration(black, black, eyes);
        Assert.AreEqual(black, value.BodyColor);
        Assert.AreEqual(black, value.EyeColor);
        Assert.AreSame(eyes, value.EyeLayout);
        Assert.ThrowsExactly<ArgumentNullException>(() => new DangoAppearanceConfiguration(black, black, null!));
    }
}
