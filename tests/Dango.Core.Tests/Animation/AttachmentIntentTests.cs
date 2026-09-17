using Dango.Core.Animation;

namespace Dango.Core.Tests.Animation;

[TestClass]
public sealed class AttachmentIntentTests
{
    [TestMethod]
    public void RejectsNonFiniteRangesEnumsAndDurations()
    {
        AttachmentIntent[] invalid = [new SetGaze(EyeSelection.Both, double.NaN, 0), new SetGaze((EyeSelection)4, 0, 0),
            new SetEyes(EyeSelection.Left, 1.1), new SetEyes(EyeSelection.Right, 1, (EyeShape)99),
            new Blink(EyeSelection.Both, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)),
            new PulseHat(double.PositiveInfinity), new PulseHat(0.31), new BounceHat((HatBounceDirection)9, 0.2),
            new Release(AttachmentChannel.None), new Release((AttachmentChannel)512)];
        foreach (var intent in invalid) Assert.IsFalse(intent.IsValid, intent.ToString());
        Assert.IsTrue(Blink.Default(EyeSelection.Both).IsValid);
        Assert.AreEqual(AttachmentChannel.LeftGaze | AttachmentChannel.RightGaze, new SetGaze(EyeSelection.Both, 0, 0).Channels);
    }
}
