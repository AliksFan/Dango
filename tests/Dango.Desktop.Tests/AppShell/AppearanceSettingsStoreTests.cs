using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;
using System.Text.Json.Nodes;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class AppearanceSettingsStoreTests
{
    [TestMethod]
    public void MissingFileUsesDefaultWithoutCreatingSidecar()
    {
        var path = NewPath();

        var result = new AppearanceSettingsStore().LoadOrDefault(path);

        Assert.AreEqual("#FFE8C2", result.Settings.BodyColor.ToHex());
        Assert.AreEqual("#2B201E", result.Settings.EyeColor.ToHex());
        Assert.IsFalse(result.PreserveOriginal);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public void CurrentSchemaRoundTripsOnlyTheStrictContract()
    {
        var path = NewPath();
        var store = new AppearanceSettingsStore();
        var color = OpaqueSrgbColor.Parse("#000000");

        var eyes = new EyeLayoutParameters(139.75, -67.125, 40.25, 33.875);
        store.Save(path, new AppearanceSettings(color, color, eyes));

        Assert.AreEqual("#000000", store.Load(path).BodyColor.ToHex());
        Assert.AreEqual("#000000", store.Load(path).EyeColor.ToHex());
        Assert.AreEqual(eyes, store.Load(path).EyeLayout);
        Assert.AreEqual(new DangoAppearanceConfiguration(color, color, eyes), store.Load(path).Configuration);
        Assert.AreEqual(string.Join(Environment.NewLine,
            "{",
            "  \"appearanceSchemaVersion\": 5,",
            "  \"bodyColorHex\": \"#000000\",",
            "  \"eyeColorHex\": \"#000000\",",
            "  \"eyeSizePercent\": 139.75,",
            "  \"eyeOvalityPercent\": -67.125,",
            "  \"eyeHeightControlPercent\": 40.25,",
            "  \"eyeSpacingControlPercent\": 33.875,",
            "  \"bodySizePercent\": 100,",
            "  \"bodySoftness\": 50,",
            "  \"bodyLandingBuild\": 50,",
            "  \"bodyPhysicsModel\": \"continuous-belt-v1\"",
            "}"), File.ReadAllText(path));
    }

    [TestMethod]
    public void LegacySchemaLoadsBodyAndDefaultEyesWithoutChangingOriginalBytes()
    {
        var path = NewPath();
        const string original = "{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#000000\"}";
        Write(path, original);

        var result = new AppearanceSettingsStore().LoadOrDefault(path);

        Assert.AreEqual("#000000", result.Settings.BodyColor.ToHex());
        Assert.AreEqual("#2B201E", result.Settings.EyeColor.ToHex());
        Assert.AreEqual(EyeLayoutParameters.Default, result.Settings.EyeLayout);
        Assert.IsTrue(result.RequiresMigration);
        Assert.IsFalse(result.PreserveOriginal);
        Assert.IsNull(result.Warning);
        Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public void SchemaTwoLoadsBothColorsAndDefaultLayoutWithoutChangingOriginalBytes()
    {
        var path = NewPath();
        const string original = "{\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#010203\",\"eyeColorHex\":\"#010203\"}";
        Write(path, original);
        var bytes = File.ReadAllBytes(path);

        var result = new AppearanceSettingsStore().LoadOrDefault(path);

        Assert.AreEqual("#010203", result.Settings.BodyColor.ToHex());
        Assert.AreEqual("#010203", result.Settings.EyeColor.ToHex());
        Assert.AreEqual(EyeLayoutParameters.Default, result.Settings.EyeLayout);
        Assert.IsTrue(result.RequiresMigration);
        Assert.IsFalse(result.PreserveOriginal);
        Assert.IsNull(result.Warning);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DataRow(70d, -100d, 32d, 12d)]
    [DataRow(140d, 100d, 66d, 34d)]
    [DataRow(100d, 0d, 51d, 24.5d)]
    [DataRow(70.125d, -0.0001d, 50.75d, 24.125d)]
    public void SchemaThreeAcceptsClosedBoundsAndContinuousValues(double size, double oval, double height, double spacing)
    {
        var path = NewPath();
        var expected = new AppearanceSettings(OpaqueSrgbColor.Parse("#000000"), OpaqueSrgbColor.Parse("#000000"),
            new EyeLayoutParameters(size, oval, height, spacing));
        var store = new AppearanceSettingsStore();
        store.Save(path, expected);
        var bytes = File.ReadAllBytes(path);

        var result = store.LoadOrDefault(path);

        Assert.AreEqual(expected, result.Settings);
        Assert.IsFalse(result.RequiresMigration);
        Assert.IsFalse(result.PreserveOriginal);
        Assert.IsNull(result.Warning);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    [TestMethod]
    [DynamicData(nameof(InvalidCurrentSidecars))]
    public void InvalidSchemaThreeAndFutureVersionsProtectEntireOriginal(string original)
    {
        var path = NewPath();
        Write(path, original);
        var bytes = File.ReadAllBytes(path);

        var result = new AppearanceSettingsStore().LoadOrDefault(path);

        Assert.AreEqual(DangoAppearanceConfiguration.Default, result.Settings.Configuration);
        Assert.IsTrue(result.PreserveOriginal);
        Assert.IsFalse(result.RequiresMigration);
        Assert.IsNotNull(result.Warning);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
    }

    public static IEnumerable<object[]> InvalidCurrentSidecars()
    {
        const string valid = "{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#010203\",\"eyeColorHex\":\"#040506\",\"eyeSizePercent\":100,\"eyeOvalityPercent\":0,\"eyeHeightControlPercent\":51,\"eyeSpacingControlPercent\":24.5}";
        var controls = new[]
        {
            ("eyeSizePercent", 70d, 140d), ("eyeOvalityPercent", -100d, 100d),
            ("eyeHeightControlPercent", 32d, 66d), ("eyeSpacingControlPercent", 12d, 34d),
        };
        foreach (var (field, min, max) in controls)
        {
            var missing = JsonNode.Parse(valid)!.AsObject();
            missing.Remove(field);
            yield return [missing.ToJsonString()];
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create("100"), JsonValue.Create(true),
                new JsonObject(), new JsonArray(), JsonValue.Create(min - .001), JsonValue.Create(max + .001) })
            {
                var changed = JsonNode.Parse(valid)!.AsObject();
                changed[field] = invalid;
                yield return [changed.ToJsonString()];
            }
            yield return [valid[..^1] + $",\"{field}\":100}}"];
            var overflowing = JsonNode.Parse(valid)!.AsObject();
            overflowing[field] = 999999;
            yield return [overflowing.ToJsonString().Replace($"\"{field}\":999999", $"\"{field}\":1e999", StringComparison.Ordinal)];
        }
        foreach (var field in new[] { "appearanceSchemaVersion", "bodyColorHex", "eyeColorHex" })
        {
            var missing = JsonNode.Parse(valid)!.AsObject();
            missing.Remove(field);
            yield return [missing.ToJsonString()];
            foreach (var invalid in new JsonNode?[] { null, JsonValue.Create(true), new JsonObject(), new JsonArray() })
            {
                var changed = JsonNode.Parse(valid)!.AsObject();
                changed[field] = invalid;
                yield return [changed.ToJsonString()];
            }
        }
        foreach (var badColor in new[] { "#abcDEF", "#ABC", "#01020300", "010203", "#GG0000" })
            foreach (var field in new[] { "bodyColorHex", "eyeColorHex" })
            {
                var changed = JsonNode.Parse(valid)!.AsObject();
                changed[field] = badColor;
                yield return [changed.ToJsonString()];
            }
        yield return [valid.Replace("\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":4", StringComparison.Ordinal)];
        yield return [valid.Replace("\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":\"3\"", StringComparison.Ordinal)];
        yield return [valid.Replace("\"appearanceSchemaVersion\":3", "\"appearanceSchemaVersion\":3.5", StringComparison.Ordinal)];
        yield return [valid[..^1] + ",\"appearanceSchemaVersion\":3}"];
        yield return [valid[..^1] + ",\"bodyColorHex\":\"#010203\"}"];
        yield return [valid[..^1] + ",\"eyeColorHex\":\"#040506\"}"];
        yield return [valid[..^1] + ",\"hatPresetId\":\"sprout\"}"];
        yield return ["{\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#010203\",\"eyeColorHex\":\"#040506\",\"eyeSizePercent\":100}"];
        yield return ["{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#010203\",\"eyeHeightControlPercent\":51}"];
    }

    [TestMethod]
    [DataRow("not-json")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("\"appearance\"")]
    [DataRow("{\"appearanceSchemaVersion\":2,\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#2B201E\"}")]
    [DataRow("{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#C9E2FF\",\"bodyColorHex\":\"#DDCCF8\"}")]
    [DataRow("{\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#2B201E\",\"eyeColorHex\":\"#000000\"}")]
    [DataRow("{\"appearanceSchemaVersion\":1}")]
    [DataRow("{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#ABC\"}")]
    [DataRow("{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#C9E2FF\",\"future\":true}")]
    [DataRow("{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#2B201E\"}")]
    [DataRow("{\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#C9E2FF\"}")]
    [DataRow("{\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#BAD\"}")]
    [DataRow("{\"appearanceSchemaVersion\":2,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#2B201E\",\"future\":true}")]
    [DataRow("{\"appearanceSchemaVersion\":3,\"bodyColorHex\":\"#C9E2FF\",\"eyeColorHex\":\"#2B201E\"}")]
    public void UnsafeSidecarsUseDefaultAndPreserveOriginal(string original)
    {
        var path = NewPath();
        Write(path, original);

        var result = new AppearanceSettingsStore().LoadOrDefault(path);

        Assert.AreEqual("#FFE8C2", result.Settings.BodyColor.ToHex());
        Assert.AreEqual("#2B201E", result.Settings.EyeColor.ToHex());
        Assert.IsTrue(result.PreserveOriginal);
        Assert.IsNotNull(result.Warning);
        Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public void FailingAtomicWriteLeavesExistingBytesUntouched()
    {
        var path = NewPath();
        const string original = "{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#FFE8C2\"}";
        Write(path, original);
        var store = new AppearanceSettingsStore(new ThrowingWriter());

        Assert.ThrowsExactly<IOException>(() => store.Save(path, new AppearanceSettings(OpaqueSrgbColor.Parse("#C9E2FF"))));

        Assert.AreEqual(original, File.ReadAllText(path));
    }

    [TestMethod]
    public void SavePassesAnyOpaqueRgbPairToWriter()
    {
        var path = NewPath();
        const string original = "{\"appearanceSchemaVersion\":1,\"bodyColorHex\":\"#FFE8C2\"}";
        Write(path, original);
        var writer = new RecordingWriter();

        new AppearanceSettingsStore(writer).Save(path, new AppearanceSettings(
            OpaqueSrgbColor.Parse("#000000"), OpaqueSrgbColor.Parse("#000000")));

        Assert.AreEqual(1, writer.Calls);
        StringAssert.Contains(writer.LastContent!, "\"bodyColorHex\": \"#000000\"");
        StringAssert.Contains(writer.LastContent!, "\"eyeColorHex\": \"#000000\"");
        Assert.AreEqual(original, File.ReadAllText(path));
    }

    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs", "appearance",
        Guid.NewGuid().ToString("N"), "appearance.json");

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class ThrowingWriter : IAtomicTextWriter
    {
        public void Write(string path, string content) => throw new IOException("simulated write failure");
    }

    private sealed class RecordingWriter : IAtomicTextWriter
    {
        public int Calls { get; private set; }
        public string? LastContent { get; private set; }
        public void Write(string path, string content)
        {
            Calls++;
            LastContent = content;
        }
    }
}
