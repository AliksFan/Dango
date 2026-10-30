using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
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
        var original = new AccessoryItem("hat", "测试帽", "builtin-top-hat", 64, 64) { SizePercent = 180, GravityInfluence = 73, IsEquipped = false };
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
            { IsEquipped = false, SizePercent = 270, GravityInfluence = 69, LockRotation = false, ImagePivotY = .23 }]);
        new AccessorySettingsStore().Save(root, legacy);
        var originals = new[] { "appearance.json", "accessories-v2.json" }.ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(root, f)));
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
    [DataRow(false)]
    [DataRow(true)]
    public void LegacyOnlyAccessoryLibraryPreservesSourcesAndAllowsRosterEdits(bool includeAppearance)
    {
        Directory.CreateDirectory(root);
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(TestRepository.Root, "tests", "Dango.Desktop.Tests",
            "Dangos", "Fixtures", "DangoRosterV1.json")))!;
        var legacyPath = AccessorySettingsStore.GetLegacyManifestPath(root);
        File.WriteAllText(legacyPath, new JsonObject
        {
            ["accessorySchemaVersion"] = 1,
            ["items"] = fixture["library"]!.DeepClone(),
        }.ToJsonString());
        var original = File.ReadAllBytes(legacyPath);
        // Retired source images must not be decoded, converted, or removed during initialization.
        var imagePath = AccessorySettingsStore.GetAssetPath(root, fixture["library"]![0]!["assetId"]!.GetValue<string>());
        Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
        byte[] imageBytes = [1, 2, 3, 4]; File.WriteAllBytes(imagePath, imageBytes);
        var appearance = DangoAppearanceConfiguration.Default.WithBodyColor(OpaqueSrgbColor.Parse("#AA8877"));
        if (includeAppearance)
        {
            new AppearanceSettingsStore().Save(Path.Combine(root, "appearance.json"), appearance);
            new ControlCenterWindowSettingsStore().Save(Path.Combine(root, "settings.json"), ControlCenterWindowSettings.CreateDefault());
        }
        var sourceBytes = new[] { "appearance.json", "settings.json" }.Where(f => File.Exists(Path.Combine(root, f)))
            .ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(root, f)));
        var store = new DangoRosterStore(root); var loaded = store.LoadOrInitialize();
        Assert.IsFalse(loaded.IsProtected, loaded.Warning); Assert.IsNotNull(loaded.Snapshot);
        StringAssert.Contains(loaded.Warning!, "发现旧版配饰库，本版未载入");
        var s = loaded.Snapshot; var legacyId = s.SelectedId!.Value;
        Assert.AreEqual(legacyId, s.Slots[0]); Assert.AreEqual(1, s.Profiles.Count);
        Assert.AreEqual(includeAppearance ? appearance : DangoAppearanceConfiguration.Default, s.Find(legacyId)!.Appearance);
        Assert.AreEqual(0, s.Library.Items.Count); Assert.AreEqual(0, s.Find(legacyId)!.Accessories.Items.Count);
        s = Committed(store.Rename(s, legacyId, "原团子", Guid.NewGuid()));
        s = Create(store, s, 1, "新团子"); var createdId = s.SelectedId!.Value;
        var accessories = AccessoryConfiguration.FromLegacyHat("bowler");
        s = Committed(store.SaveAccessories(s, createdId, accessories, null, Guid.NewGuid()));
        var reopened = new DangoRosterStore(root).LoadOrInitialize();
        Assert.IsFalse(reopened.IsProtected, reopened.Warning); Assert.IsNotNull(reopened.Snapshot);
        StringAssert.Contains(reopened.Warning!, "发现旧版配饰库，本版未载入");
        Assert.AreEqual(s.Revision, reopened.Snapshot.Revision); Assert.AreEqual(createdId, reopened.Snapshot.SelectedId);
        Assert.AreEqual("原团子", reopened.Snapshot.Find(legacyId)!.Name);
        Assert.AreEqual(accessories, reopened.Snapshot.Find(createdId)!.Accessories);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(legacyPath));
        CollectionAssert.AreEqual(imageBytes, File.ReadAllBytes(imagePath));
        foreach (var pair in sourceBytes) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Path.Combine(root, pair.Key)));
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(root)));
    }

    [TestMethod]
    [DataRow("invalid json")]
    [DataRow("{\"accessorySchemaVersion\":99,\"items\":[]}")]
    [DataRow("{\"accessorySchemaVersion\":1,\"items\":{}}")]
    [DataRow("{\"accessorySchemaVersion\":1,\"items\":[],\"future\":true}")]
    [DataRow("{\"accessorySchemaVersion\":1,\"items\":[],\"items\":[]}")]
    public void UnrecognizedLegacyAccessoryEnvelopeStillProtectsWithoutInitialization(string content)
    {
        Directory.CreateDirectory(root);
        var path = AccessorySettingsStore.GetLegacyManifestPath(root); File.WriteAllText(path, content);
        var loaded = new DangoRosterStore(root).LoadOrInitialize();
        Assert.IsTrue(loaded.IsProtected); Assert.IsNull(loaded.Snapshot);
        Assert.IsFalse(File.Exists(RosterPath));
        Assert.IsFalse(File.Exists(Path.Combine(root, DangoRosterStore.InitializationRelativePath)));
        Assert.AreEqual(content, File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow("invalid json")]
    [DataRow("{\"accessorySchemaVersion\":99,\"items\":[]}")]
    public void InvalidCurrentAccessoryLibraryCannotFallBackToLegacyLibrary(string content)
    {
        Directory.CreateDirectory(root);
        var legacyPath = AccessorySettingsStore.GetLegacyManifestPath(root);
        File.WriteAllText(legacyPath, "{\"accessorySchemaVersion\":1,\"items\":[]}");
        var original = File.ReadAllBytes(legacyPath);
        var currentPath = AccessorySettingsStore.GetManifestPath(root); File.WriteAllText(currentPath, content);
        var loaded = new DangoRosterStore(root).LoadOrInitialize();
        Assert.IsTrue(loaded.IsProtected); Assert.IsNull(loaded.Snapshot);
        Assert.IsFalse(File.Exists(RosterPath));
        Assert.IsFalse(File.Exists(Path.Combine(root, DangoRosterStore.InitializationRelativePath)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(legacyPath));
        Assert.AreEqual(content, File.ReadAllText(currentPath));
    }

    [TestMethod]
    public void CurrentAccessoryLibraryTakesPrecedenceOverUnloadedLegacyLibrary()
    {
        Directory.CreateDirectory(root);
        var legacyPath = AccessorySettingsStore.GetLegacyManifestPath(root);
        const string legacy = "{\"accessorySchemaVersion\":1,\"items\":[]}";
        File.WriteAllText(legacyPath, legacy);
        var current = AccessoryConfiguration.FromLegacyHat("top-hat");
        new AccessorySettingsStore().Save(root, current);
        var loaded = new DangoRosterStore(root).LoadOrInitialize();
        Assert.IsFalse(loaded.IsProtected, loaded.Warning); Assert.IsNotNull(loaded.Snapshot);
        Assert.IsNull(loaded.Warning); Assert.AreEqual(current, loaded.Snapshot.Profiles.Single().Accessories);
        Assert.AreEqual(legacy, File.ReadAllText(legacyPath));
        Assert.IsNull(new DangoRosterStore(root).LoadOrInitialize().Warning);
    }

    [TestMethod]
    public void LegacyAppearanceChangesWarnWithoutOverwritingNewIndividualsEvenWhenSourceDidNotExistInitially()
    {
        foreach (var hasLegacySource in new[] { false, true })
        {
            var caseRoot = Path.Combine(root, hasLegacySource ? "migrated" : "fresh");
            Directory.CreateDirectory(caseRoot);
            var legacyPath = Path.Combine(caseRoot, "appearance.json");
            if (hasLegacySource) new AppearanceSettingsStore().Save(legacyPath, DangoAppearanceConfiguration.Default);
            var store = new DangoRosterStore(caseRoot); var s = Read(store);
            s = hasLegacySource ? Committed(store.Rename(s, s.SelectedId!.Value, "甲", Guid.NewGuid())) : Create(store, s, 0, "甲");
            var a = s.SelectedId!.Value;
            var aAppearance = DangoAppearanceConfiguration.Default.WithBodyColor(OpaqueSrgbColor.Parse("#AA8877"));
            s = Committed(store.SaveAppearance(s, a, aAppearance, Guid.NewGuid()));
            s = Create(store, s, 1, "乙"); var b = s.SelectedId!.Value;
            var before = File.ReadAllBytes(Path.Combine(caseRoot, DangoRosterStore.RosterRelativePath));
            new AppearanceSettingsStore().Save(legacyPath,
                DangoAppearanceConfiguration.Default.WithBodyColor(OpaqueSrgbColor.Parse("#112233")));
            new AccessorySettingsStore().Save(caseRoot, AccessoryConfiguration.FromLegacyHat("bowler"));
            var read = new DangoRosterStore(caseRoot).LoadOrInitialize();
            Assert.IsFalse(read.IsProtected); Assert.IsNotNull(read.Snapshot);
            StringAssert.Contains(read.Warning!, "外观／配饰"); StringAssert.Contains(read.Warning!, "尚未合并");
            Assert.AreEqual(aAppearance, read.Snapshot.Find(a)!.Appearance);
            Assert.AreEqual(DangoAppearanceConfiguration.Default, read.Snapshot.Find(b)!.Appearance);
            Assert.AreEqual(2, read.Snapshot.Profiles.Count);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path.Combine(caseRoot, DangoRosterStore.RosterRelativePath)));
            using var lockedSource = new FileStream(legacyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var whileUnreadable = new DangoRosterStore(caseRoot).LoadOrInitialize();
            Assert.IsFalse(whileUnreadable.IsProtected); Assert.IsNotNull(whileUnreadable.Snapshot);
            StringAssert.Contains(whileUnreadable.Warning!, "来源暂不可读");
            Assert.AreEqual(aAppearance, whileUnreadable.Snapshot.Find(a)!.Appearance);
        }
    }

    [TestMethod]
    public void UnchangedLegacySourcesHaveNoWarningAndNormalDeviceSettingsSavesDoNotReportConflicts()
    {
        Directory.CreateDirectory(root);
        new AppearanceSettingsStore().Save(Path.Combine(root, "appearance.json"), DangoAppearanceConfiguration.Default);
        new AccessorySettingsStore().Save(root, AccessoryConfiguration.FromLegacyHat("top-hat"));
        var settingsPath = Path.Combine(root, "settings.json");
        var settingsStore = new ControlCenterWindowSettingsStore();
        settingsStore.Save(settingsPath, ControlCenterWindowSettings.CreateDefault());
        var store = new DangoRosterStore(root); var s = Read(store);
        Assert.IsNull(store.LoadOrInitialize().Warning);
        var rosterBefore = File.ReadAllBytes(RosterPath);
        settingsStore.Save(settingsPath, ControlCenterWindowSettings.CreateDefault() with { Width = 1234, Height = 789 });
        var read = new DangoRosterStore(root).LoadOrInitialize();
        Assert.IsFalse(read.IsProtected); Assert.IsNull(read.Warning); Assert.IsNotNull(read.Snapshot);
        Assert.AreEqual(s.SelectedId, read.Snapshot.SelectedId);
        CollectionAssert.AreEqual(rosterBefore, File.ReadAllBytes(RosterPath));
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
    public void FailedWritePreservesErrorTypeAndHResultWithoutExposingExceptionMessage()
    {
        var initial = new DangoRosterStore(root); var s = Read(initial);
        var before = File.ReadAllBytes(RosterPath);
        var store = new DangoRosterStore(root, new BeforeWriteFailureWriter());
        var result = store.Create(s, 0, "甲", DangoAppearanceConfiguration.Default, Guid.NewGuid());
        Assert.AreEqual(FurnitureCommitState.NotCommitted, result.State);
        StringAssert.Contains(result.Warning!, "IOException");
        StringAssert.Contains(result.Warning!, "HRESULT 0x80070020");
        Assert.IsFalse(result.Warning!.Contains("synthetic private location", StringComparison.Ordinal));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(RosterPath));
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
            node => node["schemaVersion"] = 99,
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
        var duplicate = original.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 2, \"schemaVersion\": 2");
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
        var asset = AccessoryImageAsset.ImportPng(source); var project = AccessoryEditorProject.FromImage(asset);
        var item = new AccessoryItem("synthetic", "测试图片", asset.AssetId, 4, 4)
        { ProjectId = project.ProjectId, ProjectVersion = project.Version };
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲"); var id = s.SelectedId!.Value;
        Assert.AreEqual(FurnitureCommitState.NotCommitted, store.SaveAccessories(s, id, new([item]), null, Guid.NewGuid()).State);
        s = Committed(store.SaveAccessories(s, id, new([item]), new Dictionary<string, AccessoryImageAsset> { [asset.AssetId] = asset }, Guid.NewGuid(),
            new Dictionary<string, AccessoryEditorProject> { [project.ProjectId] = project }));
        var assetPath = AccessorySettingsStore.GetAssetPath(root, asset.AssetId); Assert.IsTrue(File.Exists(assetPath));
        File.Delete(assetPath);
        s = Committed(store.SaveAccessories(s, id, new([item with { SizePercent = 200 }]), null, Guid.NewGuid()));
        s = Committed(store.Rename(s, id, "改名", Guid.NewGuid()));
        Assert.AreEqual(asset.AssetId, Read(new DangoRosterStore(root)).Find(id)!.Accessories.Items.Single().AssetId);
        Assert.IsFalse(File.Exists(assetPath));
    }

    [TestMethod]
    public void OldCurrentLibraryIsProtectedWithoutRewritingOrInitializing()
    {
        var otherRoot = Path.Combine(root, "old-library"); Directory.CreateDirectory(otherRoot);
        var path = AccessorySettingsStore.GetManifestPath(otherRoot);
        const string oldLibrary = "{\"accessorySchemaVersion\":2,\"items\":[]}";
        File.WriteAllText(path, oldLibrary);
        Assert.IsTrue(new DangoRosterStore(otherRoot).LoadOrInitialize().IsProtected);
        Assert.IsFalse(File.Exists(Path.Combine(otherRoot, DangoRosterStore.InitializationRelativePath)));
        Assert.AreEqual(oldLibrary, File.ReadAllText(path));
    }

    [TestMethod]
    public void CurrentAccessorySnapshotProtectsMissingUnknownAndNestedFields()
    {
        var store = new DangoRosterStore(root); var snapshot = Create(store, Read(store), 0, "甲");
        snapshot = Committed(store.SaveAccessories(snapshot, snapshot.SelectedId!.Value,
            new([new("hat", "虚构配饰", "builtin-sprout", 64, 64)]), null, Guid.NewGuid()));
        var text = File.ReadAllText(RosterPath);
        foreach (var inProfile in new[] { false, true })
        foreach (var field in new[] { "projectId", "projectVersion", "editRevision", "deformationMode", "rig", "skeleton" })
        {
            var json = JsonNode.Parse(text)!;
            var item = (inProfile ? json["profiles"]![0]!["accessories"]![0] : json["library"]![0])!.AsObject();
            item.Remove(field);
            File.WriteAllText(RosterPath, json.ToJsonString());
            Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected, field);
        }
        foreach (var field in new[] { "softness", "unknownField" })
        {
            var json = JsonNode.Parse(text)!; json["library"]![0]![field] = 42;
            File.WriteAllText(RosterPath, json.ToJsonString());
            Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected, field);
        }
    }

    [TestMethod]
    public void MultiAssetProjectsRoundTripAndOldProfileProjectsSurviveSharedTemplateReplacement()
    {
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲"); var a = s.SelectedId!.Value;
        var (item, image, project) = ProjectFixture();
        s = Committed(store.SaveAccessories(s, a, new([item]), new Dictionary<string, AccessoryImageAsset> { [image.AssetId] = image },
            Guid.NewGuid(), new Dictionary<string, AccessoryEditorProject> { [project.ProjectId] = project }));
        s = Create(store, s, 1, "乙"); var b = s.SelectedId!.Value;
        var updatedProject = new AccessoryEditorProject(2, 2, 8, image.AssetId, project.Layers, project.Assets.Values,
            project.Skeleton, "第二版", project.RenderPieces);
        var updated = item with { Name = updatedProject.Name, ProjectId = updatedProject.ProjectId, EditRevision = 8, SizePercent = 211 };
        s = Committed(store.SaveAccessories(s, b, new([updated]), null, Guid.NewGuid(),
            new Dictionary<string, AccessoryEditorProject> { [updatedProject.ProjectId] = updatedProject }));
        var beforeInvalid = File.ReadAllBytes(RosterPath);
        Assert.AreEqual(FurnitureCommitState.NotCommitted,
            store.SaveAccessories(s, b, new([updated with { ImagePivotX = .25, Rig = null }]), null, Guid.NewGuid()).State);
        CollectionAssert.AreEqual(beforeInvalid, File.ReadAllBytes(RosterPath));
        var reopened = Read(new DangoRosterStore(root));
        Assert.AreEqual(item, reopened.Find(a)!.Accessories.Items.Single());
        Assert.AreEqual(updated, reopened.Find(b)!.Accessories.Items.Single());
        Assert.AreEqual(updated with { IsEquipped = false }, reopened.Library.Items.Single());
        var assets = new AccessorySettingsStore();
        CollectionAssert.AreEqual(project.CopyBytes(), assets.LoadProject(root, item.ProjectId!).CopyBytes());
        CollectionAssert.AreEqual(updatedProject.CopyBytes(), assets.LoadProject(root, updated.ProjectId!).CopyBytes());
        Assert.AreEqual(2, assets.LoadProject(root, updated.ProjectId!).Assets.Count);
        Assert.IsTrue(assets.LoadProject(root, updated.ProjectId!).Layers[1].FlipX);
        Assert.IsNotNull(assets.LoadProject(root, updated.ProjectId!).Layers[0].RestoreAssetId);
        // A missing project remains isolated and referenced during unrelated saves, including a different profile's edits.
        File.WriteAllText(AccessorySettingsStore.GetProjectPath(root, item.ProjectId!), "fictional-corrupt-project");
        s = Committed(store.SaveAccessories(reopened, b, new([updated with { SizePercent = 213 }]), null, Guid.NewGuid()));
        s = Committed(store.Rename(s, a, "甲改", Guid.NewGuid()));
        Assert.AreEqual(item.ProjectId, Read(store).Find(a)!.Accessories.Items.Single().ProjectId);
        Assert.AreEqual("fictional-corrupt-project", File.ReadAllText(AccessorySettingsStore.GetProjectPath(root, item.ProjectId!)));
    }

    [TestMethod]
    [DataRow("project")]
    [DataRow("manifest")]
    [DataRow("after-replace")]
    public void ProjectOrManifestFailureCannotPartiallyPublishAndCanRetry(string failure)
    {
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲"); var id = s.SelectedId!.Value;
        var before = File.ReadAllBytes(RosterPath); var (item, image, project) = ProjectFixture();
        var projectPath = AccessorySettingsStore.GetProjectPath(root, project.ProjectId);
        var writer = new FaultWriter { FailRoster = failure == "manifest", ThrowAfterWrite = failure == "after-replace" };
        store = new DangoRosterStore(root, writer);
        if (failure == "project") Directory.CreateDirectory(projectPath);
        var images = new Dictionary<string, AccessoryImageAsset> { [image.AssetId] = image };
        var projects = new Dictionary<string, AccessoryEditorProject> { [project.ProjectId] = project };
        var operation = Guid.NewGuid();
        var result = store.SaveAccessories(s, id, new([item]), images, operation, projects);
        if (failure == "after-replace")
        {
            var committed = Committed(result);
            Assert.AreEqual(committed.Revision, Committed(store.SaveAccessories(s, id, new([item]), images, operation, projects)).Revision);
            s = committed;
        }
        else
        {
            Assert.AreEqual(FurnitureCommitState.NotCommitted, result.State);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(RosterPath));
            Assert.AreEqual(0, Read(store).Find(id)!.Accessories.Items.Count);
            if (failure == "project") Directory.Delete(projectPath);
            writer.FailRoster = false;
            s = Committed(store.SaveAccessories(s, id, new([item]), images, operation, projects));
        }
        CollectionAssert.AreEqual(project.CopyBytes(), new AccessorySettingsStore().LoadProject(root, project.ProjectId).CopyBytes());
        Assert.AreEqual(item, Read(store).Find(id)!.Accessories.Items.Single());
    }

    [TestMethod]
    public void NestedSkeletonAndRigFieldsCannotSilentlyDefaultInRosterSnapshots()
    {
        var store = new DangoRosterStore(root); var s = Create(store, Read(store), 0, "甲");
        var (item, image, project) = ProjectFixture();
        s = Committed(store.SaveAccessories(s, s.SelectedId!.Value, new([item]),
            new Dictionary<string, AccessoryImageAsset> { [image.AssetId] = image }, Guid.NewGuid(),
            new Dictionary<string, AccessoryEditorProject> { [project.ProjectId] = project }));
        var original = File.ReadAllText(RosterPath);
        Action<JsonNode>[] changes = [
            entry => entry["skeleton"]!["groups"]![0]!["parameters"]!.AsObject().Remove("bend"),
            entry => entry["skeleton"]!["pivot"]!["unknown"] = true,
            entry => entry["skeleton"]!["version"] = 99,
            entry => entry["rig"]!.AsObject().Remove("damping"),
            entry => entry["rig"]!["nodes"]![0]!["unknown"] = true,
        ];
        foreach (var inProfile in new[] { false, true })
        foreach (var change in changes)
        {
            var json = JsonNode.Parse(original)!;
            var entry = inProfile ? json["profiles"]![0]!["accessories"]![0]! : json["library"]![0]!;
            change(entry); var corrupt = json.ToJsonString(); File.WriteAllText(RosterPath, corrupt);
            Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
            Assert.AreEqual(corrupt, File.ReadAllText(RosterPath));
        }
    }

    private static (AccessoryItem Item, AccessoryImageAsset Image, AccessoryEditorProject Project) ProjectFixture()
    {
        var fixture = Accessories.AccessorySkeletonProjectContractTests.UpgradeFixture();
        var original = new AccessoryItem("shared", "原名", new string('a', 64), 2, 2) { IsPendingCompletion = true };
        var imported = new AccessoryBrowserProjectAdapter().ReadResult(
            System.Text.Json.JsonSerializer.SerializeToElement(fixture["project"]),
            System.Text.Json.JsonSerializer.SerializeToElement(fixture["png"]), new("fictional", 1, original.Id, 7, original), original);
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 1, 2, 3, 255, 4, 5, 6, 0, 7, 8, 9, 255, 10, 11, 12, 255 }, 8); bitmap.Freeze();
        var second = AccessoryEditorSourceAsset.FromBitmap(bitmap); var first = imported.Project.Assets.Values.Single();
        AccessoryProjectLayer[] layers = [new("one", "层一", first.AssetId, 0, 0, 2, 2, RestoreAssetId: second.AssetId),
            new("two", "层二", second.AssetId, 0, 0, 2, 2, FlipX: true, OriginalWidth: 2, OriginalHeight: 2)];
        var skeleton = new AccessorySkeletonConfiguration(new("P", 1, 1),
            [new("A", "组甲", null, [new("AR", 0, 0), new("AJ", 1, 0)], new()),
             new("B", "组乙", "AJ", [new("BR", 1, 0), new("BJ", 1, 1)], new(Elasticity: .2))],
            [new("one", "A"), new("two", "B")]);
        var project = new AccessoryEditorProject(2, 2, 7, imported.Image.AssetId, layers, [first, second], skeleton,
            "虚构多资源", [new("A", first.AssetId), new("B", second.AssetId)]);
        return (imported.Item with { Name = project.Name, ProjectId = project.ProjectId, Skeleton = project.Skeleton,
            ImagePivotX = .5, ImagePivotY = .5, Rig = new([new(.5, .5), new(.75, .75)], .25, 7, 8) }, imported.Image, project);
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
    private sealed class BeforeWriteFailureWriter : IAtomicTextWriter
    {
        public void Write(string path, string content) =>
            throw new IOException("Injected failure at synthetic private location", unchecked((int)0x80070020));
    }
}
