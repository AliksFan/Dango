using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class OpaqueSrgbColorTests
{
    [TestMethod]
    public void OpaqueSrgbColorAcceptsOnlyCanonicalUppercaseHexAndFormatsItExactly()
    {
        var color = OpaqueSrgbColor.Parse("#C9E2FF");

        Assert.AreEqual((byte)0xC9, color.R);
        Assert.AreEqual((byte)0xE2, color.G);
        Assert.AreEqual((byte)0xFF, color.B);
        Assert.AreEqual("#C9E2FF", color.ToHex());
        foreach (var invalid in new[] { "#c9e2ff", "C9E2FF", "#FFF", "#FFC9E2FF", "red", "#C9E2FG", "#C9E2FF " })
            Assert.ThrowsExactly<FormatException>(() => OpaqueSrgbColor.Parse(invalid));
    }

    [TestMethod]
    public void EveryOpaqueRgbIncludingBlackWhiteAndPrimaryColorsIsRepresentedExactly()
    {
        foreach (var hex in new[] { "#000000", "#FFFFFF", "#FF0000", "#00FF00", "#0000FF", "#2B201E" })
            Assert.AreEqual(hex, OpaqueSrgbColor.Parse(hex).ToHex());

        var random = new Random(20260923);
        for (var index = 0; index < 200; index++)
        {
            var selected = new OpaqueSrgbColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            Assert.AreEqual(selected, OpaqueSrgbColor.Parse(selected.ToHex()));
        }
    }

    [TestMethod]
    public void DefaultMainColorStaysUnchanged()
    {
        Assert.AreEqual("#FFE8C2", OpaqueSrgbColor.Default.ToHex());
    }
}
