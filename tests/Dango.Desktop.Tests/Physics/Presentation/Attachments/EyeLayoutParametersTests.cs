using Dango.Desktop.Physics.Presentation.Attachments;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class EyeLayoutParametersTests
{
    [TestMethod]
    public void DefaultAndContinuousValuesAreRepresentedWithoutRounding()
    {
        Assert.AreEqual(new EyeLayoutParameters(100, 0, 51, 24.5), EyeLayoutParameters.Default);
        Assert.IsTrue(new EyeLayoutParameters().IsDefault);
        var value = new EyeLayoutParameters(87.125, -42.75, 48.25, 20.125);
        Assert.AreEqual(87.125, value.SizePercent);
        Assert.AreEqual(-42.75, value.OvalPercent);
        Assert.AreEqual(48.25, value.HeightControlPercent);
        Assert.AreEqual(20.125, value.SpacingControlPercent);
        Assert.IsFalse(value.IsDefault);
        _ = new EyeLayoutParameters(70, -100, 32, 12);
        _ = new EyeLayoutParameters(140, 100, 66, 34);
    }

    [TestMethod]
    public void EveryControlRejectsBothRangeSidesAndNonFiniteValues()
    {
        var bounds = new[] { (70d, 140d), (-100d, 100d), (32d, 66d), (12d, 34d) };
        for (var control = 0; control < bounds.Length; control++)
        foreach (var invalid in new[] { bounds[control].Item1 - .001, bounds[control].Item2 + .001,
                     double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var values = new[] { 100d, 0d, 51d, 24.5 };
            values[control] = invalid;
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
                new EyeLayoutParameters(values[0], values[1], values[2], values[3]));
        }
    }
}
