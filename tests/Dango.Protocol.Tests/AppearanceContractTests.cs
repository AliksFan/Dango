using System.Text.Json;

namespace Dango.Protocol.Tests;

[TestClass]
public sealed class AppearanceContractTests
{
    private const string Valid = """{"appearanceSchemaVersion":3,"bodyColorHex":"#Aa11ff","eyeColorHex":"#123456","eyeSizePercent":100,"eyeOvalityPercent":0,"eyeHeightControlPercent":51,"eyeSpacingControlPercent":24.5}""";

    [TestMethod]
    public void ValidCloneOutlivesDocumentAndJsonbOrderingDoesNotCauseConflict()
    {
        JsonElement clone;
        using (var doc = JsonDocument.Parse(Valid)) clone = AppearanceContract.ValidateAndClone(doc.RootElement);
        var reordered = JsonSerializer.SerializeToElement(new
        {
            eyeSpacingControlPercent = 24.5, eyeHeightControlPercent = 51.0, eyeOvalityPercent = 0,
            eyeSizePercent = 100.0, eyeColorHex = "#123456", bodyColorHex = "#aa11FF", appearanceSchemaVersion = 3
        });
        Assert.IsTrue(AppearanceContract.Equivalent(clone, reordered));
        Assert.IsFalse(AppearanceContract.Equivalent(clone, Parse(Valid.Replace("24.5", "24.6"))));
    }

    [TestMethod]
    public void UnknownDuplicateMissingSchemaAndAuthorityFieldsFailClosed()
    {
        foreach (var invalid in new[]
        {
            "null", "[]", "{}", Valid.Replace("\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":99"),
            Valid.Replace("\"bodyColorHex\":", "\"petId\":"),
            Valid.Replace("\"eyeColorHex\":\"#123456\",", "\"bodyColorHex\":\"#123456\","),
            Valid[..^1] + ",\"roomId\":\"spoof\"}"
        }) Assert.ThrowsExactly<AppearanceContractException>(() => AppearanceContract.ValidateAndClone(Parse(invalid)), invalid);
    }

    [TestMethod]
    public void NumbersColorsAndByteSizeAreBounded()
    {
        foreach (var invalid in new[]
        {
            Valid.Replace("#Aa11ff", "#Ａa11ff"), Valid.Replace("#Aa11ff", "transparent"),
            Valid.Replace("100,", "69.999,"), Valid.Replace("100,", "140.001,"),
            Valid.Replace(":0,", ":-101,"), Valid.Replace(":51,", ":66.01,"),
            Valid.Replace(":24.5", ":11.99"), Valid.Replace(":100,", ":1e999,"),
            Valid.Replace(":51,", ":\"51\","), " " + Valid.Insert(1, new string(' ', AppearanceContract.MaximumBytes))
        }) Assert.ThrowsExactly<AppearanceContractException>(() => AppearanceContract.ValidateAndClone(Parse(invalid)), invalid);
        foreach (var valid in new[] { Valid.Replace(":100,", ":70,"), Valid.Replace(":100,", ":140,"), Valid.Replace(":0,", ":-100,"), Valid.Replace(":24.5", ":34") })
            _ = AppearanceContract.ValidateAndClone(Parse(valid));
    }

    private static JsonElement Parse(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
}
