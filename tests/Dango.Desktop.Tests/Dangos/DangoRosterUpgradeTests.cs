using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.AppShell.Rooms.Storage;
using Dango.Desktop.Dangos;
using Dango.Desktop.Furniture;
using Dango.Desktop.Rendering.Visuals;
using Dango.Desktop.Verification;

namespace Dango.Desktop.Tests.Dangos;

[TestClass]
public sealed class DangoRosterUpgradeTests
{
    private string root = null!;
    private string RosterPath => Path.Combine(root, DangoRosterStore.RosterRelativePath);
    [TestInitialize] public void Initialize() => root = Path.Combine(Path.GetTempPath(), "Dango-roster-upgrade", Guid.NewGuid().ToString("N"));
    [TestCleanup] public void Cleanup()
    {
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Dango-roster-upgrade")) + Path.DirectorySeparatorChar;
        Assert.IsTrue(Path.GetFullPath(root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase));
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [TestMethod]
    public void ReleasedVersionOneFixtureKeepsThreeSlotsSharedLibraryProfilesImagesAndPixelPivots()
    {
        var original = WriteVersionOneFixture(); var old = LegacyDangoRosterDocument.Read(original).ToSnapshot();
        var originalIntent = File.ReadAllBytes(Path.Combine(root, DangoRosterStore.InitializationRelativePath));
        var oldImage = File.ReadAllBytes(AccessorySettingsStore.GetAssetPath(root, old.Library.Items[0].AssetId));
        var store = new DangoRosterStore(root); var result = store.LoadOrInitialize();
        Assert.IsFalse(result.IsProtected, result.Warning); var upgraded = result.Snapshot!;
        Assert.AreEqual(old.Revision + 1, upgraded.Revision);
        Assert.AreEqual(2, JsonNode.Parse(File.ReadAllBytes(RosterPath))!["schemaVersion"]!.GetValue<int>());
        CollectionAssert.AreEqual(original, File.ReadAllBytes(BackupPath(original)));
        CollectionAssert.AreEqual(originalIntent, File.ReadAllBytes(Path.Combine(root, DangoRosterStore.InitializationRelativePath)));
        CollectionAssert.AreEqual(oldImage, File.ReadAllBytes(AccessorySettingsStore.GetAssetPath(root, old.Library.Items[0].AssetId)));
        CollectionAssert.AreEqual(old.Slots.ToArray(), upgraded.Slots.ToArray());
        Assert.AreEqual(old.SelectedId, upgraded.SelectedId); Assert.AreEqual(old.NameSource, upgraded.NameSource);
        foreach (var before in old.Profiles)
        {
            var after = upgraded.Find(before.Id)!;
            Assert.AreEqual(before.Name, after.Name); Assert.AreEqual(before.Appearance, after.Appearance);
            Assert.AreEqual(before.RequiresBodyPreview, after.RequiresBodyPreview);
            AssertCommonItems(before.Accessories, after.Accessories);
        }
        AssertCommonItems(old.Library, upgraded.Library);
        var customProjects = upgraded.Library.Items.Concat(upgraded.Profiles.SelectMany(p => p.Accessories.Items))
            .Where(item => !AccessoryItem.IsBuiltInAsset(item.AssetId)).ToArray();
        Assert.AreEqual(3, customProjects.Select(item => item.ProjectId).Distinct().Count(), "Per-profile pixel pivots and names belong to their own immutable projects.");
        foreach (var item in customProjects)
        {
            var project = new AccessorySettingsStore().LoadProject(root, item.ProjectId!);
            Assert.AreEqual(item.ImagePivotX * item.PixelWidth, project.Skeleton.Pivot.X);
            Assert.AreEqual(item.ImagePivotY * item.PixelHeight, project.Skeleton.Pivot.Y);
            Assert.AreEqual(0, project.Skeleton.Groups.Count); Assert.IsNull(item.Rig);
            Assert.AreEqual(AccessoryDeformationMode.Rigid, item.DeformationMode);
            project.ValidateResult(item, new AccessorySettingsStore().LoadImage(root, item.AssetId));
        }
        var revision = upgraded.Revision; var savedBytes = File.ReadAllBytes(RosterPath);
        Assert.AreEqual(revision, new DangoRosterStore(root).LoadOrInitialize().Snapshot!.Revision);
        CollectionAssert.AreEqual(savedBytes, File.ReadAllBytes(RosterPath));
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(root, "dangos", "backups"), "*.json").Length);
        var selected = upgraded.SelectedId!.Value;
        var configuration = new AccessoryConfiguration(upgraded.EffectiveAccessories(selected).Items.Select(item => item with { SizePercent = 199 }));
        var write = store.SaveAccessories(upgraded, selected, configuration, null, Guid.NewGuid());
        Assert.AreEqual(FurnitureCommitState.Committed, write.State, write.Warning);
        Assert.AreEqual(199d, new DangoRosterStore(root).LoadOrInitialize().Snapshot!.Find(selected)!.Accessories.Items[0].SizePercent);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(BackupPath(original)));
    }

    [TestMethod]
    [DataRow("backup-before")]
    [DataRow("backup-truncated")]
    [DataRow("backup-conflict")]
    [DataRow("manifest-before")]
    public void BackupOrPublicationFailurePreservesOriginalAndNeverOverwritesExistingBackup(string failure)
    {
        var original = WriteVersionOneFixture(); var expectedBackup = BackupPath(original);
        if (failure == "backup-conflict")
        { Directory.CreateDirectory(Path.GetDirectoryName(expectedBackup)!); File.WriteAllText(expectedBackup, "fictional-conflicting-backup"); }
        var writer = new UpgradeWriter { FailBefore = failure == "manifest-before" };
        Action<string, byte[]>? backupWriter = failure switch
        {
            "backup-before" => (_, _) => throw new IOException("Injected backup failure"),
            "backup-truncated" => (path, bytes) => File.WriteAllBytes(path, bytes[..7]),
            _ => null,
        };
        var read = new DangoRosterStore(root, writer, backupWriter).LoadOrInitialize();
        Assert.IsTrue(read.IsProtected); Assert.IsNull(read.Snapshot);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(RosterPath));
        if (failure != "manifest-before")
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "accessory-projects")), "Backup failure cannot prepare projects.");
        if (failure == "backup-conflict") Assert.AreEqual("fictional-conflicting-backup", File.ReadAllText(expectedBackup));
        if (failure is "backup-before" or "manifest-before")
        {
            var retried = new DangoRosterStore(root).LoadOrInitialize();
            Assert.IsFalse(retried.IsProtected, retried.Warning);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(expectedBackup));
        }
        else
        {
            var beforeBackup = File.ReadAllBytes(expectedBackup);
            Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
            CollectionAssert.AreEqual(beforeBackup, File.ReadAllBytes(expectedBackup));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ThrowAfterBackupOrRosterReplacementIsObservedAndRestartDoesNotUpgradeTwice(bool roster)
    {
        var original = WriteVersionOneFixture();
        Action<string, byte[]>? backup = roster ? null : (path, bytes) =>
        { File.WriteAllBytes(path, bytes); throw new IOException("Injected after backup replacement"); };
        var read = new DangoRosterStore(root, new UpgradeWriter { ThrowAfter = roster }, backup).LoadOrInitialize();
        Assert.IsFalse(read.IsProtected, read.Warning); var revision = read.Snapshot!.Revision;
        Assert.AreEqual(revision, new DangoRosterStore(root).LoadOrInitialize().Snapshot!.Revision);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(BackupPath(original)));
    }

    [TestMethod]
    public void UnknownUpgradeProtectsSceneAndNeverReimportsOrDeletesBackup()
    {
        var original = WriteVersionOneFixture(); var store = new DangoRosterStore(root, new UpgradeWriter { CorruptInstead = true });
        Assert.IsTrue(store.LoadOrInitialize().IsProtected);
        Assert.AreEqual("fictional-unknown-write", File.ReadAllText(RosterPath));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(BackupPath(original)));
        Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
        File.WriteAllBytes(RosterPath, original); // Explicit test-only restoration, then an explicit reload.
        var restarted = new DangoRosterStore(root).LoadOrInitialize(); Assert.IsFalse(restarted.IsProtected, restarted.Warning);
        Assert.AreEqual(1, Directory.GetFiles(Path.Combine(root, "dangos", "backups"), "*.json").Length);
    }

    [TestMethod]
    public void DocumentedOfflineRollbackRestoresByteIdenticalReleasedFormatAndPreservesRetiredFields()
    {
        var original = WriteVersionOneFixture();
        Assert.IsFalse(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
        // The product is closed: preserve the new snapshot separately, restore the hash-named
        // v1 backup to dangos/roster.json, and start the prior executable (the new one upgrades again).
        var newSnapshot = Path.Combine(root, "dangos", "roster-before-offline-rollback.json");
        File.Copy(RosterPath, newSnapshot, overwrite: false);
        File.Copy(BackupPath(original), RosterPath, overwrite: true);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(RosterPath));
        var released = LegacyDangoRosterDocument.Read(File.ReadAllBytes(RosterPath));
        Assert.AreEqual(1, released.SchemaVersion); Assert.AreEqual(3, released.Profiles.Length);
        Assert.AreEqual(73d, released.Library[0].Softness); Assert.IsTrue(released.Library[0].AllowFolding);
        Assert.AreEqual(132d, released.Library[0].MaxDeformationPercent);
        Assert.AreEqual(2, JsonNode.Parse(File.ReadAllBytes(newSnapshot))!["schemaVersion"]!.GetValue<int>());
    }

    [TestMethod]
    public void UnknownMissingAndInvalidReleasedFieldsOrBadImagesCannotBecomeAnEmptyRoster()
    {
        var original = WriteVersionOneFixture();
        Action<JsonNode>[] changes = [
            json => json["library"]![0]!.AsObject().Remove("softness"),
            json => json["profiles"]![0]!["accessories"]![0]!["unknown"] = true,
            json => json["profiles"]![0]!["accessories"]![0]!["maxDeformationPercent"] = 201,
            json => json["schemaVersion"] = 99,
            json => json["slots"]![1] = Guid.NewGuid(),
        ];
        foreach (var change in changes)
        {
            var json = JsonNode.Parse(original)!; change(json); var invalid = json.ToJsonString(); File.WriteAllText(RosterPath, invalid);
            Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
            Assert.AreEqual(invalid, File.ReadAllText(RosterPath));
        }
        File.WriteAllBytes(RosterPath, original);
        var assetId = LegacyDangoRosterDocument.Read(original).Library[0].AssetId;
        File.WriteAllBytes(AccessorySettingsStore.GetAssetPath(root, assetId), [1, 2, 3]);
        Assert.IsTrue(new DangoRosterStore(root).LoadOrInitialize().IsProtected);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(RosterPath));
    }

    private string BackupPath(byte[] original) => Path.Combine(root, DangoRosterStore.GetVersionOneBackupRelativePath(original));
    private byte[] WriteVersionOneFixture()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RosterPath)!);
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 1, 2, 3, 255, 0, 0, 0, 0, 4, 5, 6, 255, 7, 8, 9, 255 }, 8); bitmap.Freeze();
        var source = AccessoryEditorSourceAsset.FromBitmap(bitmap);
        var assetPath = AccessorySettingsStore.GetAssetPath(root, source.AssetId);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!); File.WriteAllBytes(assetPath, source.CopyPngBytes());
        var fixture = File.ReadAllText(Path.Combine(TestRepository.Root, "tests", "Dango.Desktop.Tests", "Dangos", "Fixtures", "DangoRosterV1.json"));
        // The checked-in fixture uses the exact released v1 shape and fictional identities.
        // Supply its referenced synthetic PNG under the actual normalized content hash.
        var bytes = Encoding.UTF8.GetBytes(fixture.Replace(new string('a', 64), source.AssetId));
        File.WriteAllBytes(RosterPath, bytes);
        var initialization = new DangoInitialization(1, LegacyDangoRosterDocument.Read(bytes).InitializationId, null, null, null, null);
        var originalIntent = JsonNode.Parse(RoomJson.Write(initialization))!.AsObject();
        originalIntent.Remove("currentAccessoriesSha256");
        File.WriteAllText(Path.Combine(root, DangoRosterStore.InitializationRelativePath), originalIntent.ToJsonString());
        return bytes;
    }
    private static void AssertCommonItems(AccessoryConfiguration before, AccessoryConfiguration after)
    {
        Assert.AreEqual(before.Items.Count, after.Items.Count);
        for (var i = 0; i < before.Items.Count; i++)
            Assert.AreEqual(before.Items[i], after.Items[i] with { ProjectId = null, ProjectVersion = 0, Skeleton = null });
    }
    private sealed class UpgradeWriter : IAtomicTextWriter
    {
        public bool FailBefore { get; init; }
        public bool ThrowAfter { get; init; }
        public bool CorruptInstead { get; init; }
        public void Write(string path, string text)
        {
            if (FailBefore) throw new IOException("Injected before upgrade publication");
            new AtomicTextFileWriter().Write(path, CorruptInstead ? "fictional-unknown-write" : text);
            if (ThrowAfter) throw new IOException("Injected after upgrade publication");
        }
    }
}
