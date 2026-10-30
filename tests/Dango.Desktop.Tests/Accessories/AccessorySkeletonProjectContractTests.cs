using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessorySkeletonProjectContractTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [TestMethod]
    public void SkeletonValueEqualitySurvivesIndependentCopiesAndDetectsEveryPersistedField()
    {
        var original = GroupedProject().Skeleton;
        var copy = original.Snapshot();
        Assert.AreNotSame(original.Groups, copy.Groups);
        Assert.AreEqual(original, copy);
        Assert.AreEqual(original.GetHashCode(), copy.GetHashCode());
        var item = new AccessoryItem("fictional-equality", "Synthetic", "builtin-sprout", 64, 64) { Skeleton = original };
        Assert.AreEqual(item, item with { Skeleton = copy });
        var group = original.Groups[0];
        AccessorySkeletonGroup[] changedGroups =
        [
            group with { Id = "changed" }, group with { Name = "changed" }, group with { TargetId = "changed" },
            group with { Parameters = group.Parameters with { Gravity = 1 } },
            group with { Nodes = group.Nodes.Select((n, i) => i == 0 ? n with { X = n.X + .1 } : n).ToArray() },
            group with { Nodes = group.Nodes.Select((n, i) => i == 0 ? n with { Id = "changed" } : n).ToArray() },
            group with { Nodes = group.Nodes.Reverse().ToArray() }
        ];
        foreach (var changed in changedGroups)
            Assert.AreNotEqual(original, original with { Groups = [changed, .. original.Groups.Skip(1)] });
        Assert.AreNotEqual(original, original with { Version = 2 });
        Assert.AreNotEqual(original, original with { Pivot = original.Pivot with { Y = original.Pivot.Y + .1 } });
        Assert.AreNotEqual(original, original with { Groups = original.Groups.Reverse().ToArray() });
        Assert.AreNotEqual(original, original with { Layers = original.Layers.Select(l => l with { GroupId = "changed" }).ToArray() });
        Assert.AreNotEqual(original, original with { Layers = original.Layers.Reverse().ToArray() });
    }

    // Reuse only the archived raster/vector content, explicitly authoring a version-two envelope.
    internal static JsonObject UpgradeFixture()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(TestRepository.Root,
            "tests", "Dango.Desktop.Tests", "Accessories", "Fixtures", "AccessoryBrowserProjectV1.json")))!.AsObject();
        var project = fixture["project"]!.AsObject();
        project["schemaVersion"] = 2; project["name"] = "虚构编辑名称";
        project["skeleton"] = JsonSerializer.SerializeToNode(new AccessorySkeletonConfiguration(new("P", .5, .5), [], []), Options);
        project["renderPieces"] = new JsonArray(new JsonObject { ["groupId"] = null, ["assetId"] = "asset-1" });
        RefreshUngroupedLayers(project);
        return fixture;
    }

    internal static void RefreshUngroupedLayers(JsonObject project)
    {
        // These typed-interchange tests have no group assignments. New skeleton tests below do not use this helper.
        if (project["skeleton"]?["groups"] is JsonArray groups && groups.Count == 0 && project["layers"] is JsonArray layers)
            project["skeleton"]!["layers"] = new JsonArray(layers.Select(l => (JsonNode)new JsonObject
                { ["id"] = l!["id"]!.GetValue<int>().ToString(System.Globalization.CultureInfo.InvariantCulture), ["groupId"] = null }).ToArray());
    }

    private static AccessoryEditResult Import(JsonObject fixture, AccessoryItem? original = null)
    {
        original ??= new("fictional-item", "原名", new string('a', 64), 2, 2) { IsPendingCompletion = true };
        var draft = new AccessoryEditDraft("fictional-session", 1, original.Id, 7, original);
        return new AccessoryBrowserProjectAdapter().ReadResult(JsonSerializer.SerializeToElement(fixture["project"]),
            JsonSerializer.SerializeToElement(fixture["png"]), draft, original);
    }

    private static AccessoryEditorProject GroupedProject()
    {
        var imported = Import(UpgradeFixture());
        var source = imported.Project.Assets.Values.Single();
        var layers = Enumerable.Range(1, 3).Select(i => new AccessoryProjectLayer("layer-" + i, "层 " + i,
            source.AssetId, 0, 0, 2, 2)).ToArray();
        var skeleton = new AccessorySkeletonConfiguration(new("P", 1, 1),
            [new("A", "主组", null, [new("AR", 0, 0), new("AJ", 1, 0)], new()),
             new("B", "空组根", "AJ", [new("BR", 1, 0)], new()),
             new("C", "自由空组", null, [new("CR", 0, 1)], new())],
            [new("layer-1", "A"), new("layer-2", null), new("layer-3", "B")]);
        return new(2, 2, 7, imported.Image.AssetId, layers, [source], skeleton, "分组工程",
            [new("A", source.AssetId), new(null, source.AssetId), new("B", source.AssetId)]);
    }

    [TestMethod]
    public void MultipleGroupsEmptyAndRootOnlyGroupsAndUngroupedBlocksReopenWithStableIdentities()
    {
        var project = GroupedProject();
        var native = AccessoryEditorProject.Read(project.CopyBytes(), project.ProjectId);
        Assert.AreEqual(2, native.Version); Assert.AreEqual("分组工程", native.Name);
        Assert.AreEqual(3, native.Skeleton.Groups.Count); Assert.AreEqual(1, native.Skeleton.Groups[1].Nodes.Count);
        Assert.AreEqual("AJ", native.Skeleton.Groups[1].TargetId);
        CollectionAssert.AreEqual(new string?[] { "A", null, "B" }, native.RenderPieces.Select(p => p.GroupId).ToArray());
        var adapter = new AccessoryBrowserProjectAdapter();
        var browser = adapter.CreateOpenProject(native);
        Assert.AreEqual("1", browser.GetProperty("skeleton").GetProperty("layers")[0].GetProperty("id").GetString());
        var fixture = UpgradeFixture(); fixture["project"] = JsonNode.Parse(browser.GetRawText());
        var original = new AccessoryItem("fictional-item", "原名", project.FinalAssetId, 2, 2);
        var result = adapter.ReadResult(JsonSerializer.SerializeToElement(fixture["project"]),
            JsonSerializer.SerializeToElement(fixture["png"]), new("session", 1, original.Id, 7, original), original);
        CollectionAssert.AreEqual(native.Layers.Select(l => l.Id).ToArray(), result.Project.Layers.Select(l => l.Id).ToArray());
        Assert.IsTrue(AccessoryConfiguration.SkeletonEquals(native.Skeleton, result.Item.Skeleton));
    }

    [TestMethod]
    public void BrowserGeometryAndNameCannotOverwriteOuterWearingParameters()
    {
        var original = new AccessoryItem("fictional-item", "原名", new string('a', 64), 2, 2)
        { SizePercent = 180, BaseAngleDegrees = 27, BodyAnchorX = .2, BodyAnchorY = .8,
          GravityInfluence = 73, Layer = AccessoryLayer.Behind, Order = 4, ConnectionStrength = 39 };
        var result = Import(UpgradeFixture(), original);
        Assert.AreEqual("虚构编辑名称", result.Item.Name); Assert.AreEqual(.25, result.Item.ImagePivotX);
        Assert.AreEqual(original with { Name = result.Item.Name, AssetId = result.Item.AssetId,
            PixelWidth = result.Item.PixelWidth, PixelHeight = result.Item.PixelHeight, ImagePivotX = .25, ImagePivotY = .25,
            ProjectId = result.Project.ProjectId, ProjectVersion = 2, EditRevision = 7,
            DeformationMode = AccessoryDeformationMode.Skeleton, Skeleton = result.Item.Skeleton }, result.Item);
        var malicious = UpgradeFixture(); malicious["project"]!["sizePercent"] = 99;
        Assert.ThrowsExactly<InvalidDataException>(() => Import(malicious, original));
    }

    [TestMethod]
    public void BadTargetsSeparatedAttachedRootsAndCyclesRejectBeforeBecomingProjects()
    {
        var project = GroupedProject();
        foreach (var mode in new[] { "missing", "separated", "cycle" })
        {
            var groups = project.Skeleton.Groups.ToArray();
            groups[1] = mode switch
            {
                "missing" => groups[1] with { TargetId = "unknown" },
                "separated" => groups[1] with { Nodes = [new("BR", 1, 1)] },
                _ => groups[1] with { TargetId = "AR", Nodes = [new("BR", 0, 0)] }
            };
            if (mode == "cycle") groups[0] = groups[0] with { TargetId = "BR" };
            Assert.ThrowsExactly<ArgumentException>(() => new AccessoryEditorProject(2, 2, 7, project.FinalAssetId,
                project.Layers, project.Assets.Values, project.Skeleton with { Groups = groups }, project.Name, project.RenderPieces));
        }
    }

    [TestMethod]
    public void DerivedOnlyRenderAssetsAreBoundedAndVersionOneCannotBeReinterpreted()
    {
        var project = GroupedProject();
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 1, 2, 3, 255, 0, 0, 0, 0, 4, 5, 6, 255, 0, 0, 0, 0 }, 8);
        bitmap.Freeze(); var derived = AccessoryEditorSourceAsset.FromBitmap(bitmap);
        var pieces = project.RenderPieces.Select(p => p with { AssetId = derived.AssetId }).ToArray();
        var withDerived = new AccessoryEditorProject(2, 2, 7, project.FinalAssetId, project.Layers,
            project.Assets.Values.Append(derived), project.Skeleton, project.Name, pieces);
        var reopened = AccessoryEditorProject.Read(withDerived.CopyBytes());
        Assert.AreEqual(2, reopened.Assets.Count); Assert.IsFalse(reopened.Layers.Any(l => l.AssetId == derived.AssetId));
        var bad = JsonNode.Parse(project.CopyBytes())!; bad["version"] = 1; bad["engine"] = "minipaint-v1";
        Assert.ThrowsExactly<InvalidDataException>(() => AccessoryEditorProject.Read(JsonSerializer.SerializeToUtf8Bytes(bad)));
        var oldBrowser = UpgradeFixture(); oldBrowser["project"]!["schemaVersion"] = 1;
        Assert.ThrowsExactly<InvalidDataException>(() => Import(oldBrowser));
        Assert.ThrowsExactly<InvalidDataException>(() => new AccessoryEditorProject(2, 2, 7, project.FinalAssetId,
            project.Layers, project.Assets.Values, project.Skeleton, project.Name,
            [new("A", new string('b', 64))]));
    }

    [TestMethod]
    public void ManifestThreeRoundTripsSkeletonAndImmutableProjectReferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "dango-skeleton-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = Import(UpgradeFixture()); var store = new AccessorySettingsStore();
            var configuration = new AccessoryConfiguration([result.Item]);
            store.Save(root, configuration, new Dictionary<string, AccessoryImageAsset> { [result.Image.AssetId] = result.Image },
                new Dictionary<string, AccessoryEditorProject> { [result.Project.ProjectId] = result.Project });
            var loaded = store.Load(root);
            Assert.AreEqual(AccessorySettingsLoadState.Valid, loaded.State);
            Assert.IsTrue(configuration.Equals(loaded.Configuration));
            Assert.AreEqual(configuration.GetHashCode(), loaded.Configuration.GetHashCode());
            Assert.IsTrue(AccessoryConfiguration.SkeletonEquals(result.Item.Skeleton, loaded.Configuration.Items[0].Skeleton));
            var stored = store.LoadProject(root, result.Project.ProjectId);
            stored.ValidateResult(loaded.Configuration.Items[0], store.LoadImage(root, result.Image.AssetId));
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(AccessorySettingsStore.GetManifestPath(root)));
            Assert.AreEqual(3, manifest.RootElement.GetProperty("accessorySchemaVersion").GetInt32());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void SavedAndUnappliedDraftWithTheSameItemIdResolveTheirOwnImmutableProjects()
    {
        var root = Path.Combine(Path.GetTempPath(), "dango-saved-draft-projects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = GroupedProject(); var image = Import(UpgradeFixture()).Image;
            var saved = new AccessoryItem("fictional-same-id", first.Name, image.AssetId, 2, 2)
            {
                ProjectId = first.ProjectId, ProjectVersion = first.Version, EditRevision = first.Revision,
                Skeleton = first.Skeleton, DeformationMode = AccessoryDeformationMode.Skeleton,
                ImagePivotX = first.Skeleton.Pivot.X / 2, ImagePivotY = first.Skeleton.Pivot.Y / 2,
            };
            var store = new AccessorySettingsStore();
            store.Save(root, new([saved]), new Dictionary<string, AccessoryImageAsset> { [image.AssetId] = image },
                new Dictionary<string, AccessoryEditorProject> { [first.ProjectId] = first });
            var session = new AccessorySettingsSession(root);
            var savedSnapshot = session.SavedConfiguration.Items.Single();
            var ticket = session.BeginEdit(savedSnapshot.Id);
            var changedSkeleton = first.Skeleton with { Pivot = first.Skeleton.Pivot with { X = .5 } };
            var second = new AccessoryEditorProject(2, 2, ticket.Revision, image.AssetId, first.Layers, first.Assets.Values,
                changedSkeleton, "未应用的第二版", first.RenderPieces);
            var changedItem = ticket.Original with
            {
                Name = second.Name, Skeleton = second.Skeleton, ImagePivotX = .25,
                ProjectId = second.ProjectId, ProjectVersion = second.Version, EditRevision = second.Revision,
            };
            Assert.IsTrue(session.TryCompleteEdit(ticket, new(second, image, changedItem)));
            var draftSnapshot = session.DraftConfiguration.Items.Single();
            Assert.AreEqual(savedSnapshot.Id, draftSnapshot.Id);
            Assert.AreNotEqual(first.ProjectId, second.ProjectId);
            var savedProject = session.TryGetProject(savedSnapshot);
            var draftProject = session.TryGetProject(draftSnapshot);
            Assert.IsNotNull(savedProject); Assert.IsNotNull(draftProject);
            Assert.AreEqual(first.ProjectId, savedProject.ProjectId); Assert.AreEqual(first.Revision, savedProject.Revision);
            Assert.AreEqual(first.Name, savedProject.Name);
            Assert.AreEqual(second.ProjectId, draftProject.ProjectId); Assert.AreEqual(ticket.Revision, draftProject.Revision);
            Assert.AreEqual(second.Name, draftProject.Name);
            Assert.AreEqual(second.ProjectId, session.TryGetProject(savedSnapshot.Id)!.ProjectId,
                "The legacy string overload intentionally resolves the current draft version.");
            Assert.AreEqual(first.ProjectId, new AccessorySettingsSession(root).SavedConfiguration.Items.Single().ProjectId,
                "Preparing the second project must leave the saved manifest pointing at the first version.");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void LegacyManifestIsProtectedAndGroupedManifestCannotDropItsSkeleton()
    {
        var root = Path.Combine(Path.GetTempPath(), "dango-skeleton-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new AccessorySettingsStore();
            var path = AccessorySettingsStore.GetManifestPath(root);
            File.WriteAllText(path, "{\"accessorySchemaVersion\":2,\"items\":[]}");
            var before = File.ReadAllBytes(path);
            Assert.AreEqual(AccessorySettingsLoadState.Protected, store.Load(root).State);
            Assert.ThrowsExactly<InvalidDataException>(() => store.Save(root, AccessoryConfiguration.Empty));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            var project = GroupedProject(); var imported = Import(UpgradeFixture());
            var forged = imported.Item with { ProjectId = project.ProjectId, Skeleton = null, DeformationMode = AccessoryDeformationMode.Rigid };
            Assert.ThrowsExactly<InvalidDataException>(() => project.ValidateResult(forged, imported.Image));
        }
        finally { Directory.Delete(root, true); }
    }
}
