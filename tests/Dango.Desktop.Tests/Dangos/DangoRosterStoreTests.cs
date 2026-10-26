using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Dangos;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.Dangos;

[TestClass]
public sealed class DangoRosterStoreTests
{
    private string root = null!;
    private string RosterPath => Path.Combine(root, DangoRosterStore.RosterRelativePath);

    [TestInitialize]
    public void Initialize() => root = Path.Combine(Path.GetTempPath(), "Dango-roster-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Dango-roster-tests")) + Path.DirectorySeparatorChar;
        Assert.IsTrue(Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [TestMethod]
    public void NewRootHasNoManufacturedDangoAndSnapshotsAreDefensive()
    {
        var store = new DangoRosterStore(root);
        var empty = Read(store);
        Assert.AreEqual(0, empty.Profiles.Count);
        Assert.AreEqual(3, empty.Slots.Count);
        Assert.IsTrue(empty.Slots.All(id => id is null));
        Assert.IsNull(empty.SelectedId);
        Assert.IsNull(empty.LegacyDangoId);
        Assert.IsFalse(File.Exists(Path.Combine(root, "appearance.json")));
        Assert.Throws<NotSupportedException>(() => ((IList<Guid?>)empty.Slots)[0] = Guid.NewGuid());
        var created = Create(store, empty, 2, "  小栗  ");
        Assert.AreEqual("小栗", created.Profiles.Single().Name);
        Assert.IsNull(created.Slots[0]); Assert.IsNull(created.Slots[1]);
        Assert.AreEqual(created.Slots[2], created.SelectedId);
        Assert.AreEqual(0, empty.Profiles.Count);
        var reopened = Read(new DangoRosterStore(root));
        Assert.AreEqual(created.SelectedId, reopened.SelectedId);
        Assert.AreEqual(created.Revision, reopened.Revision);
    }

    [TestMethod]
    public void DeletionKeepsFixedPositionsAndRecreationUsesNewIdentity()
    {
        var store = new DangoRosterStore(root); var s = Read(store);
        s = Create(store, s, 0, "一"); var first = s.SelectedId!.Value;
        s = Create(store, s, 1, "二"); var second = s.SelectedId!.Value;
        s = Create(store, s, 2, "三"); var third = s.SelectedId!.Value;
        s = Committed(store.Delete(s, second, Guid.NewGuid()));
        CollectionAssert.AreEqual(new Guid?[] { first, null, third }, s.Slots.ToArray());
        Assert.AreEqual(third, s.SelectedId);
        s = Committed(store.Delete(s, third, Guid.NewGuid())); Assert.IsNull(s.SelectedId);
        s = Create(store, s, 1, "二"); Assert.AreNotEqual(second, s.SelectedId);
        Assert.AreEqual(first, s.Slots[0]); Assert.IsNull(s.Slots[2]);
    }

    [TestMethod]
    public void ConcurrentStoresRejectStaleExpectedAndSameOperationCannotChangePayload()
    {
        var one = new DangoRosterStore(root); var two = new DangoRosterStore(root);
        var original = Read(one); var stale = Read(two); var operation = Guid.NewGuid();
        var saved = Committed(one.Create(original, 1, "甲", DangoAppearanceConfiguration.Default, operation));
        Assert.AreEqual(FurnitureCommitState.NotCommitted,
            two.Create(stale, 0, "乙", DangoAppearanceConfiguration.Default, Guid.NewGuid()).State);
        var repeated = Committed(two.Create(original, 1, "甲", DangoAppearanceConfiguration.Default, operation));
        Assert.AreEqual(saved.SelectedId, repeated.SelectedId); Assert.AreEqual(saved.Revision, repeated.Revision);
        Assert.AreEqual(FurnitureCommitState.NotCommitted,
            one.Create(original, 1, "改名", DangoAppearanceConfiguration.Default, operation).State);
    }

    [TestMethod]
    public void NamesUseUnicodeTextElementsAndNeverCreateUnnamedOrdinaryProfiles()
    {
        var store = new DangoRosterStore(root); var s = Read(store);
        foreach (var name in new[] { "", "  ", "甲\n乙", "甲\u2028乙", "甲\ud800乙", new string('甲', 25) })
            Assert.AreEqual(FurnitureCommitState.NotCommitted, store.Create(s, 0, name, DangoAppearanceConfiguration.Default, Guid.NewGuid()).State);
        var emoji = string.Concat(Enumerable.Repeat("👩‍🔬", 24));
        s = Create(store, s, 0, emoji); Assert.AreEqual(emoji, s.Profiles.Single().Name);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.Rename(s, s.SelectedId!.Value, "\t", Guid.NewGuid()).State);
        s = Committed(store.Rename(s, s.SelectedId!.Value, " 同名 ", Guid.NewGuid()));
        s = Create(store, s, 2, "同名"); Assert.AreEqual(2, s.Profiles.Count);
    }

    [TestMethod]
    public void IndividualAppearanceAndNamePreferenceSurviveUnrelatedEdits()
    {
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲"); var a = s.SelectedId!.Value;
        s = Create(store, s, 1, "乙"); var b = s.SelectedId!.Value;
        var changed = DangoAppearanceConfiguration.Default.WithBodyColor(OpaqueSrgbColor.Parse("#AA8877"));
        s = Committed(store.SaveAppearance(s, a, changed, Guid.NewGuid()));
        s = Committed(store.SetNameSource(s, OrdinaryRoomNameSource.AccountDisplayName, Guid.NewGuid()));
        s = Committed(store.Rename(s, b, "乙改", Guid.NewGuid()));
        s = Read(new DangoRosterStore(root));
        Assert.AreEqual(changed, s.Find(a)!.Appearance);
        Assert.AreEqual(DangoAppearanceConfiguration.Default, s.Find(b)!.Appearance);
        Assert.AreEqual(OrdinaryRoomNameSource.AccountDisplayName, s.NameSource);
        s = Committed(store.Select(s, null, Guid.NewGuid())); Assert.IsNull(s.SelectedId);
    }

    [TestMethod]
    public void TemplateChangesNeverOverwriteAnotherIndividualAndUnequippedReferencesBlockDeletion()
    {
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲"); var a = s.SelectedId!.Value;
        var original = new AccessoryItem("hat", "测试帽", "builtin-top-hat", 64, 64) { SizePercent = 180, Softness = 73, IsEquipped = false };
        s = Committed(store.SaveAccessories(s, a, new([original]), null, Guid.NewGuid()));
        s = Create(store, s, 1, "乙"); var b = s.SelectedId!.Value;
        Assert.AreEqual(0, s.Find(b)!.Accessories.Items.Count);
        Assert.IsFalse(s.EffectiveAccessories(b).Items.Single().IsEquipped);
        s = Committed(store.SaveAccessories(s, b, new([original with { IsEquipped = true, SizePercent = 222 }]), null, Guid.NewGuid()));
        Assert.AreEqual(original, s.Find(a)!.Accessories.Items.Single());
        Assert.AreEqual(222, s.EffectiveAccessories(b).Items.Single().SizePercent);
        Assert.AreEqual(180, s.EffectiveAccessories(a).Items.Single().SizePercent);
        var before = File.ReadAllBytes(RosterPath);
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.SaveAccessories(s, b, AccessoryConfiguration.Empty, null, Guid.NewGuid()).State);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(RosterPath));
        s = Committed(store.Delete(s, a, Guid.NewGuid()));
        s = Committed(store.SaveAccessories(s, b, AccessoryConfiguration.Empty, null, Guid.NewGuid()));
        Assert.AreEqual(0, s.Library.Items.Count);
    }

    [TestMethod]
    public void LegacyMigrationPreservesBytesAllParametersAndStableUnnamedIdentity()
    {
        Directory.CreateDirectory(root);
        var appearancePath = Path.Combine(root, "appearance.json");
        new AppearanceSettingsStore().Save(appearancePath, DangoAppearanceConfiguration.Default);
        var node = JsonNode.Parse(File.ReadAllText(appearancePath))!.AsObject();
        node["appearanceSchemaVersion"] = 4; node.Remove("bodyPhysicsModel");
        File.WriteAllText(appearancePath, node.ToJsonString());
        var legacy = new AccessoryConfiguration([new AccessoryItem("off", "未佩戴参数", "builtin-bowler", 64, 64)
            { IsEquipped = false, SizePercent = 270, Softness = 69, AllowFolding = true, ImagePivotY = .23 }]);
        new AccessorySettingsStore().Save(root, legacy);
        var originals = new[] { "appearance.json", "accessories.json" }.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(root, f)));
        var store = new DangoRosterStore(root); var s = Read(store); var id = s.LegacyDangoId!.Value;
        Assert.AreEqual(id, s.SelectedId); Assert.AreEqual(id, s.Slots[0]); Assert.AreEqual(1, s.Profiles.Count);
        Assert.IsNull(s.Find(id)!.Name); Assert.IsTrue(s.Find(id)!.RequiresBodyPreview);
        Assert.AreEqual(legacy, s.Find(id)!.Accessories); Assert.AreEqual(legacy, s.Library);
        s = Committed(store.Rename(s, id, "旧团子", Guid.NewGuid()));
        s = Read(new DangoRosterStore(root)); Assert.AreEqual(id, s.SelectedId); Assert.IsTrue(s.Find(id)!.RequiresBodyPreview);
        s = Committed(store.SaveAppearance(s, id, s.Find(id)!.Appearance, Guid.NewGuid()));
        Assert.IsFalse(s.Find(id)!.RequiresBodyPreview);
        foreach (var pair in originals) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Path.Combine(root, pair.Key)));
        // Once the authoritative directory exists, future garbage in old files is irrelevant.
        File.WriteAllText(appearancePath, "future invalid"); Assert.IsFalse(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
    }

    [TestMethod]
    public void SettingsOnlyMigratesCompatibilityHatWithoutChangingOriginal()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{\"schemaVersion\":4,\"hatPresetId\":\"top-hat\"}");
        var original = File.ReadAllBytes(path); var s = Read(new DangoRosterStore(root));
        Assert.AreEqual(1, s.Profiles.Count); Assert.IsNull(s.Profiles.Single().Name);
        Assert.AreEqual("builtin-top-hat", s.Profiles.Single().Accessories.Items.Single(item => item.IsEquipped).AssetId);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void CompleteSupportedSettingsMigrateAndDeletingLastLegacyDangoNeverReimports()
    {
        Directory.CreateDirectory(root); var path = Path.Combine(root, "settings.json");
        new ControlCenterWindowSettingsStore().Save(path, ControlCenterWindowSettings.CreateDefault());
        var original = File.ReadAllBytes(path); var store = new DangoRosterStore(root); var s = Read(store);
        var id = s.LegacyDangoId!.Value; s = Committed(store.Delete(s, id, Guid.NewGuid()));
        Assert.AreEqual(0, s.Profiles.Count); Assert.IsTrue(s.Slots.All(slot => slot is null));
        Assert.IsNull(s.SelectedId); Assert.AreEqual(id, s.LegacyDangoId); Assert.IsTrue(s.Library.Items.Count > 0);
        s = Read(new DangoRosterStore(root)); Assert.AreEqual(0, s.Profiles.Count); Assert.AreEqual(id, s.LegacyDangoId);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void ValidNoncanonicalJsonCanBeEditedWhileCasStillChecksTheActualBytes()
    {
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲");
        File.WriteAllText(RosterPath, JsonNode.Parse(File.ReadAllText(RosterPath))!.ToJsonString());
        s = Read(store); s = Committed(store.Rename(s, s.SelectedId!.Value, "乙", Guid.NewGuid()));
        Assert.AreEqual("乙", Read(new DangoRosterStore(root)).Profiles.Single().Name);
    }

    [TestMethod]
    public void CorruptOrFutureLegacyProtectsWithoutInitialization()
    {
        Directory.CreateDirectory(root);
        foreach (var legacy in new[] { ("appearance.json", "{\"appearanceSchemaVersion\":99}"),
                     ("accessories.json", "{\"accessorySchemaVersion\":2,\"items\":[]}"),
                     ("settings.json", "{\"schemaVersion\":99}"), ("settings.json", "{\"hatPresetId\":\"sprout\",\"hatPresetId\":\"bowler\"}") })
        {
            var path = Path.Combine(root, legacy.Item1); File.WriteAllText(path, legacy.Item2);
            var result = new DangoRosterStore(root).LoadOrInitialize(); Assert.IsTrue(result.IsProtected, legacy.Item2);
            Assert.IsNull(result.Snapshot); Assert.IsFalse(File.Exists(RosterPath));
            Assert.IsFalse(File.Exists(Path.Combine(root, DangoRosterStore.InitializationRelativePath)));
            Assert.AreEqual(legacy.Item2, File.ReadAllText(path)); File.Delete(path);
        }
    }

    [TestMethod]
    public void PermanentInitializationIntentPreventsFreshAndLegacyReimportAfterPublicationLoss()
    {
        var s = Read(new DangoRosterStore(root)); File.Delete(RosterPath);
        var result = new DangoRosterStore(root).LoadOrInitialize(); Assert.IsTrue(result.IsProtected);
        Assert.IsFalse(File.Exists(RosterPath));
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"schemaVersion\":4}");
        Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
    }

    [TestMethod]
    public void FailedPublicationAfterIntentNeverRetriesInitialization()
    {
        var writer = new FaultWriter { FailRoster = true };
        var result = new DangoRosterStore(root, writer).LoadOrInitialize(); Assert.IsTrue(result.IsProtected);
        var intentPath = Path.Combine(root, DangoRosterStore.InitializationRelativePath);
        Assert.IsTrue(File.Exists(intentPath)); var intent = File.ReadAllBytes(intentPath);
        Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
        CollectionAssert.AreEqual(intent, File.ReadAllBytes(intentPath)); Assert.IsFalse(File.Exists(RosterPath));
    }

    [TestMethod]
    public void WriterThrowAfterReplacementIsObservedAsCommitted()
    {
        var initial = new DangoRosterStore(root); var s = Read(initial);
        var writer = new FaultWriter { ThrowAfterWrite = true }; var store = new DangoRosterStore(root, writer);
        var saved = Committed(store.Create(s, 0, "甲", DangoAppearanceConfiguration.Default, Guid.NewGuid()));
        Assert.AreEqual(saved.SelectedId, Read(new DangoRosterStore(root)).SelectedId);
        Assert.AreEqual(1, saved.Profiles.Count);
    }

    [TestMethod]
    public void UnknownOutcomeBlocksWritesUntilExplicitReload()
    {
        var s = Read(new DangoRosterStore(root)); var writer = new FaultWriter { CorruptInstead = true };
        var store = new DangoRosterStore(root, writer); var operation = Guid.NewGuid();
        var original = File.ReadAllBytes(RosterPath);
        Assert.AreEqual(FurnitureCommitState.Unknown, store.Create(s, 0, "甲", DangoAppearanceConfiguration.Default, operation).State);
        writer.CorruptInstead = false; File.WriteAllBytes(RosterPath, original);
        Assert.AreEqual(FurnitureCommitState.Unknown, store.Create(s, 0, "甲", DangoAppearanceConfiguration.Default, operation).State);
        s = Read(store); s = Create(store, s, 0, "甲"); Assert.AreEqual(1, s.Profiles.Count);
    }

    [TestMethod]
    public void InvalidAuthoritativeDataProtectsInsteadOfFallingBackToLegacy()
    {
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲");
        var original = File.ReadAllText(RosterPath);
        Action<JsonObject>[] changes = [
            node => node["schemaVersion"] = 2,
            node => node["unknownField"] = true,
            node => node["slots"] = new JsonArray(),
            node => node["selectedId"] = Guid.NewGuid(),
            node => node["profiles"]![0]!["name"] = null,
            node => node["profiles"]![0]!["name"] = " 首尾空白 ",
            node => node["profiles"]![0]!["appearance"] = null,
            node => node["profiles"]![0]!["accessories"] = null,
            node => node["profiles"] = new JsonArray((JsonNode?)null),
        ];
        foreach (var change in changes)
        {
            var node = JsonNode.Parse(original)!.AsObject(); change(node); var corrupt = node.ToJsonString();
            File.WriteAllText(RosterPath, corrupt); var read = new DangoRosterStore(root).LoadOrInitialize();
            Assert.IsTrue(read.IsProtected, corrupt); Assert.IsNull(read.Snapshot); Assert.AreEqual(corrupt, File.ReadAllText(RosterPath));
        }
        var duplicate = original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1");
        File.WriteAllText(RosterPath, duplicate); Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
    }

    [TestMethod]
    public void NewAssetsAreVerifiedBeforeCommitAndPreviouslyMissingAssetsRemainReferenced()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "synthetic.png");
        var pixels = new byte[4 * 4 * 4]; pixels[0] = 80; pixels[3] = 255;
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(source)) encoder.Save(output);
        var asset = AccessoryImageAsset.ImportPng(source); var item = new AccessoryItem("synthetic", "测试图片", asset.AssetId, 4, 4);
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲"); var id = s.SelectedId!.Value;
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.SaveAccessories(s, id, new([item]), null, Guid.NewGuid()).State);
        s = Committed(store.SaveAccessories(s, id, new([item]), new Dictionary<string, AccessoryImageAsset> { [asset.AssetId] = asset }, Guid.NewGuid()));
        var assetPath = AccessorySettingsStore.GetAssetPath(root, asset.AssetId); Assert.IsTrue(File.Exists(assetPath));
        File.Delete(assetPath);
        s = Committed(store.SaveAccessories(s, id, new([item with { SizePercent = 200 }]), null, Guid.NewGuid()));
        s = Committed(store.Rename(s, id, "改名", Guid.NewGuid()));
        Assert.AreEqual(asset.AssetId, Read(new DangoRosterStore(root)).Find(id)!.Accessories.Items.Single().AssetId);
        Assert.IsFalse(File.Exists(assetPath));
    }

    private static DangoRosterSnapshot Read(DangoRosterStore store)
    {
        var read = store.LoadOrInitialize(); Assert.IsFalse(read.IsProtected, read.Warning); Assert.IsNotNull(read.Snapshot); return read.Snapshot;
    }
    private static DangoRosterSnapshot Create(DangoRosterStore store, DangoRosterSnapshot expected, int slot, string name) =>
        Committed(store.Create(expected, slot, name, DangoAppearanceConfiguration.Default, Guid.NewGuid()));
    private static DangoRosterSnapshot Committed(DangoRosterWriteResult result)
    {
        Assert.AreEqual(FurnitureCommitState.Committed, result.State, result.Warning); Assert.IsNotNull(result.Snapshot); return result.Snapshot;
    }
    private sealed class FaultWriter : IAtomicTextWriter
    {
        public bool ThrowAfterWrite { get; set; }
        public bool FailRoster { get; set; }
        public bool CorruptInstead { get; set; }
        public void Write(string path, string content)
        {
            if (FailRoster && Path.GetFileName(path) == "roster.json") throw new IOException("Injected before publication");
            new AtomicTextFileWriter().Write(path, CorruptInstead ? "injected unreadable outcome" : content);
            if (ThrowAfterWrite) throw new IOException("Injected after replacement");
        }
    }
}
