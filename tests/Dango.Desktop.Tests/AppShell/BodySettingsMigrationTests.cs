using System.Text.Json.Nodes;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.AppShell;

[TestClass]
public sealed class BodySettingsMigrationTests
{
    private static string NewPath() => Path.Combine(TestRepository.Root, "artifacts", "test-runs", "appearance", Guid.NewGuid().ToString("N"), "appearance.json");

    [TestMethod]
    public void AllVersionsReadWithoutWritingAndExplicitSaveUpgradesWithCompleteBody()
    {
        for (var schema = 1; schema <= 5; schema++)
        {
            var path = NewPath();
            var store = new AppearanceSettingsStore();
            var body = new DangoBodyControls(119.5, 95, 2);
            store.Save(path, DangoAppearanceConfiguration.Default.WithBody(body));
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json["appearanceSchemaVersion"] = schema;
            if (schema < 5) json.Remove("bodyPhysicsModel");
            if (schema < 4) foreach (var field in new[] { "bodySizePercent", "bodySoftness", "bodyLandingBuild" }) json.Remove(field);
            if (schema < 3) foreach (var field in new[] { "eyeSizePercent", "eyeOvalityPercent", "eyeHeightControlPercent", "eyeSpacingControlPercent" }) json.Remove(field);
            if (schema < 2) json.Remove("eyeColorHex");
            File.WriteAllText(path, json.ToJsonString());
            var original = File.ReadAllBytes(path);
            var result = store.LoadOrDefault(path);
            Assert.IsFalse(result.PreserveOriginal);
            Assert.AreEqual(schema < 5, result.RequiresMigration);
            Assert.AreEqual(schema < 4 ? DangoBodyControls.Default : body, result.Settings.Configuration.Body);
            Assert.AreEqual(schema == 4 ? DangoBodyMaterial.LegacyModelVersion : schema == 5 ? DangoBodyMaterial.ModelVersion : null, result.SourceBodyPhysicsModel);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
            var session = new AppearanceSettingsSession(path);
            if (schema == 4)
            {
                Assert.IsFalse(session.TrySave());
                Assert.IsTrue(session.MarkBodyApplied(body, DangoBodyMaterial.ModelVersion, session.BodyConfigurationRevision, session.BodyEpoch));
            }
            Assert.IsTrue(session.TrySave(), $"Schema {schema}: {session.StatusMessage}");
            if (schema < 5) StringAssert.Contains(File.ReadAllText(path), "continuous-belt-v1");
            Assert.AreEqual(result.Settings.Configuration.Body, store.Load(path).Configuration.Body);
        }
    }

    [TestMethod]
    public void InvalidBodyFieldsModelsFutureAndDuplicatesProtectOriginal()
    {
        var path = NewPath();
        var store = new AppearanceSettingsStore();
        store.Save(path, DangoAppearanceConfiguration.Default);
        var valid = File.ReadAllText(path);
        var invalid = new List<string> { valid.Replace("continuous-belt-v1", "future-v9"), valid.Replace("continuous-belt-v1", "body-material-v1"), valid.Replace("\"appearanceSchemaVersion\": 5", "\"appearanceSchemaVersion\": 6"), valid.TrimEnd()[..^1] + ",\"bodyPhysicsModel\":\"continuous-belt-v1\"}" };
        foreach (var field in new[] { "bodySizePercent", "bodySoftness", "bodyLandingBuild", "bodyPhysicsModel" })
        {
            var missing = JsonNode.Parse(valid)!.AsObject(); missing.Remove(field); invalid.Add(missing.ToJsonString());
            foreach (var bad in new JsonNode?[] { null, JsonValue.Create("bad"), JsonValue.Create(-1), JsonValue.Create(121), JsonValue.Create(true) })
            {
                var json = JsonNode.Parse(valid)!.AsObject(); json[field] = bad; invalid.Add(json.ToJsonString());
            }
        }
        foreach (var text in invalid)
        {
            File.WriteAllText(path, text);
            var result = store.LoadOrDefault(path);
            Assert.IsTrue(result.PreserveOriginal, text);
            Assert.IsNotNull(result.Warning);
            Assert.AreEqual(text, File.ReadAllText(path));
            Assert.IsFalse(new AppearanceSettingsSession(path).TrySave());
        }
    }

    [TestMethod]
    public void ExactBodyModelRevisionAndEpochMustBeDisplayedBeforeSaving()
    {
        var path = NewPath();
        var session = new AppearanceSettingsSession(path);
        var a = new DangoBodyControls(120, 100, 0);
        var b = new DangoBodyControls(80, 0, 100);
        session.StageBody(a);
        var old = session.BodyConfigurationRevision;
        session.StageBody(b);
        session.StageBody(a);
        Assert.IsFalse(session.MarkBodyApplied(a, DangoBodyMaterial.ModelVersion, old, session.BodyEpoch));
        session.StageBody(b);
        Assert.IsFalse(session.MarkBodyApplied(a, DangoBodyMaterial.ModelVersion, old, session.BodyEpoch));
        Assert.IsFalse(session.MarkBodyApplied(b, "unknown", session.BodyConfigurationRevision, session.BodyEpoch));
        Assert.IsFalse(session.TrySave());
        Assert.IsTrue(session.MarkBodyApplied(b, DangoBodyMaterial.ModelVersion, session.BodyConfigurationRevision, session.BodyEpoch));
        session.StageBodyPreview(OpaqueSrgbColor.Parse("#000000"));
        session.StageEyePreview(OpaqueSrgbColor.Parse("#010203"));
        session.StageEyes(new(120, 50, 40, 20));
        Assert.AreEqual(b, session.DraftAppearance.Body);
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(b, new AppearanceSettingsSession(path).SavedAppearance.Body);
        session.StageBody(a);
        var revision = session.BodyConfigurationRevision;
        var epoch = session.BodyEpoch;
        session.CancelPreview();
        Assert.IsFalse(session.MarkBodyApplied(a, DangoBodyMaterial.ModelVersion, revision, epoch));
        Assert.IsTrue(session.RequiresBodyPreview);
        Assert.IsFalse(session.TrySave());
        Assert.IsTrue(session.MarkBodyApplied(b, DangoBodyMaterial.ModelVersion, session.BodyConfigurationRevision, session.BodyEpoch));
        Assert.IsFalse(session.RequiresBodyPreview);
    }

    [TestMethod]
    public void BodySaveFailureKeepsSuccessfulApplicationForRetry()
    {
        var path = NewPath();
        var writer = new RetryWriter();
        var session = new AppearanceSettingsSession(path, new AppearanceSettingsStore(writer));
        session.StageBody(new(120, 100, 0));
        Assert.IsTrue(session.MarkBodyApplied(session.DraftAppearance.Body, DangoBodyMaterial.ModelVersion, session.BodyConfigurationRevision, session.BodyEpoch));
        Assert.IsFalse(session.TrySave());
        Assert.IsFalse(session.RequiresBodyPreview);
        writer.Fail = false;
        Assert.IsTrue(session.TrySave());
        Assert.AreEqual(session.DraftAppearance, new AppearanceSettingsSession(path).SavedAppearance);
    }

    private sealed class RetryWriter : IAtomicTextWriter
    {
        public bool Fail { get; set; } = true;
        public void Write(string path, string content)
        {
            if (Fail) throw new IOException("test write failure");
            new AtomicTextFileWriter().Write(path, content);
        }
    }
}
