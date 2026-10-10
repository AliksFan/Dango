using System.Text.Json;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.AccountSyncReview.Tests;

[TestClass]
[TestCategory("OfflineAccountSyncB1bAppearanceSamples")]
public sealed class AccountSyncB1bAppearanceSamplesTests
{
    public static IEnumerable<object[]> JourneySamples => AccountSyncB1bAppearanceSamples.All.Select(size => new object[] { size });

    [TestMethod]
    [DynamicData(nameof(JourneySamples))]
    public void EveryActualJourneySamplePassesBothFormalContracts(double size)
    {
        var eyes = new EyeLayoutParameters(size);
        var accepted = AppearanceContract.ValidateAndClone(Configuration(eyes.SizePercent));
        Assert.AreEqual(size, accepted.GetProperty("eyeSizePercent").GetDouble());
    }

    [TestMethod]
    [DataRow(149d)]
    [DataRow(155d)]
    public void FormerOutOfRangeRecoverySamplesAreRejectedByBothFormalContracts(double size)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new EyeLayoutParameters(size));
        var error = Assert.ThrowsExactly<AppearanceContractException>(() => AppearanceContract.ValidateAndClone(Configuration(size)));
        Assert.AreEqual("appearance_invalid", error.Code);
    }

    private static JsonElement Configuration(double size) => JsonSerializer.SerializeToElement(new
    {
        appearanceSchemaVersion = 3, bodyColorHex = "#AABBCC", eyeColorHex = "#DDEEFF", eyeSizePercent = size,
        eyeOvalityPercent = 0d, eyeHeightControlPercent = 51d, eyeSpacingControlPercent = 24.5d
    });
}
