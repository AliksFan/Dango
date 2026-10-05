using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

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

        store.Save(path, new AppearanceSettings(color, color));

        Assert.AreEqual("#000000", store.Load(path).BodyColor.ToHex());
        Assert.AreEqual("#000000", store.Load(path).EyeColor.ToHex());
        Assert.AreEqual(string.Join(Environment.NewLine,
            "{",
            "  \"appearanceSchemaVersion\": 2,",
            "  \"bodyColorHex\": \"#000000\",",
            "  \"eyeColorHex\": \"#000000\"",
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
        Assert.IsTrue(result.RequiresMigration);
        Assert.IsFalse(result.PreserveOriginal);
        Assert.IsNull(result.Warning);
        Assert.AreEqual(original, File.ReadAllText(path));
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
