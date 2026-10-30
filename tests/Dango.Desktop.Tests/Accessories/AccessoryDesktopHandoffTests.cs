using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.AppShell;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Dangos;
using Dango.Desktop.Furniture;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Accessories;

[TestClass, DoNotParallelize]
public sealed class AccessoryDesktopHandoffTests
{
    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void RoomEntryUsesAppliedWardrobeAndNeverAnUncommittedDraft(bool removeAll, bool cancelDraft)
    {
        using var data = new DataDirectory();
        var store = new DangoRosterStore(data.Root);
        var empty = store.LoadOrInitialize().Snapshot!;
        var created = store.Create(empty, 0, "虚构配饰团", DangoAppearanceConfiguration.Default, Guid.NewGuid());
        Assert.AreEqual(FurnitureCommitState.Committed, created.State, created.Warning);
        var selectedId = created.Snapshot!.SelectedId!.Value;
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        var window = Home(host); var home = window.ProductHome;
        Assert.IsFalse(window.IsVisible, "This regression must never show or activate the control center.");
        Assert.IsNull(home.BoundRuntime, "The continuous homepage must exercise its separate body runtime.");
        var session = home.BoundAccessories!;
        var seed = session.CreateBlank();
        session.Stage(new([seed with { BodyAnchorX = .23, BodyAnchorY = .31, BaseAngleDegrees = 17, GravityInfluence = 31 }]));
        var ticket = session.BeginEdit(seed.Id); var savedResult = Complete(ticket, 1);
        Assert.IsTrue(session.TryCompleteEdit(ticket, savedResult));
        var panel = Field<AccessoryEditorPanel>(home, "accessoryPanel");
        panel.ApplyDraft();
        Assert.IsFalse(session.HasPendingSave, session.StatusMessage);
        if (removeAll)
        {
            session.Stage(AccessoryConfiguration.Empty); panel.ApplyDraft();
            Assert.IsFalse(session.HasPendingSave, session.StatusMessage);
        }
        var configuration = session.SavedConfiguration;
        var reloaded = new DangoRosterStore(data.Root).LoadOrInitialize();
        Assert.IsFalse(reloaded.IsProtected, reloaded.Warning);
        Assert.AreEqual(configuration, reloaded.Snapshot!.Find(selectedId)!.Accessories,
            "The real editor Apply action must commit the selected profile's complete configuration to the roster.");
        Assert.AreEqual(configuration, reloaded.Snapshot.EffectiveAccessories(selectedId));
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(data.Root)), "Host Apply must use the roster commit point.");
        if (!removeAll)
            CollectionAssert.AreEqual(savedResult.Project.CopyBytes(), new AccessorySettingsStore().LoadProject(data.Root,
                configuration.Items.Single().ProjectId!).CopyBytes());
        var committedBytes = File.ReadAllBytes(Path.Combine(data.Root, DangoRosterStore.RosterRelativePath));

        // The same item ID now has a different pending immutable project and different piece textures.
        // The handoff must resolve the saved item, including when its outer draft is still present.
        session.Stage(new([savedResult.Item]));
        var draftTicket = session.BeginEdit(savedResult.Item.Id); var draft = Complete(draftTicket, 2);
        Assert.IsTrue(session.TryCompleteEdit(draftTicket, draft));
        Assert.AreNotEqual(savedResult.Project.ProjectId, draft.Project.ProjectId);
        if (cancelDraft) home.CancelAccessoryPreview();
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        runtime.SetAccessories(new([draft.Item]), true, _ => draft.Image.Bitmap, _ => draft.Project);
        PrepareRoom(host, home);
        Assert.IsTrue(runtime.UsesBeltRoom);
        Assert.IsTrue(runtime.AccessoriesEnabled, "A saved empty or customized list must be explicitly installed in the room runtime.");
        var controller = Field<AccessorySceneController>(runtime, "accessoryScene");
        Assert.AreEqual(configuration, Field<AccessoryConfiguration>(controller, "configuration"),
            "The runtime shared by ordinary and desktop-pet rooms must consume the saved profile, never the outer draft.");
        Assert.IsNull(Field<object?>(runtime, "accessoryProjectOverride"), "Room handoff must release the temporary modal project provider.");
        AssertRoomPieces(runtime, controller, removeAll ? null : savedResult.Project);
        CollectionAssert.AreEqual(committedBytes, File.ReadAllBytes(Path.Combine(data.Root, DangoRosterStore.RosterRelativePath)));
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetProjectPath(data.Root, draft.Project.ProjectId)));
        Assert.IsFalse(window.IsVisible);
        Assert.AreEqual(0, runtime.EntityIds.Count, "Saving and checking the presentation handoff must not initialize a room physics world.");
    }

    [STATestMethod]
    public void RoomEntryKeepsInitializedSavedHatWhenAnEmptyDraftHasNotBeenApplied()
    {
        using var data = new DataDirectory();
        File.WriteAllText(data.SettingsPath, "{\"schemaVersion\":4,\"hatPresetId\":\"top-hat\"}");
        var store = new DangoRosterStore(data.Root); var initialized = store.LoadOrInitialize().Snapshot!;
        var named = store.Rename(initialized, initialized.SelectedId!.Value, "虚构帽团", Guid.NewGuid());
        Assert.AreEqual(FurnitureCommitState.Committed, named.State, named.Warning);
        using var host = new DangoApplicationHost(settingsFile: data.SettingsPath);
        var window = Home(host); var home = window.ProductHome;
        var runtime = Field<StackedDangoSceneRuntime>(host, "stackedRuntime");
        var session = home.BoundAccessories!;
        Assert.IsFalse(session.IsCompatibilityMode, "Roster initialization already made the shared library and profile explicit.");
        var saved = session.SavedConfiguration;
        Assert.AreEqual(AccessoryConfiguration.FromLegacyHat("top-hat"), saved);
        Assert.AreEqual("builtin-top-hat", saved.Items.Single(item => item.IsEquipped).AssetId);
        var original = File.ReadAllBytes(Path.Combine(data.Root, DangoRosterStore.RosterRelativePath));
        session.Stage(AccessoryConfiguration.Empty);
        PrepareRoom(host, home);
        Assert.IsTrue(runtime.AccessoriesEnabled, "The initialized saved hat remains the explicit configuration while the empty draft is uncommitted.");
        var controller = Field<AccessorySceneController>(runtime, "accessoryScene");
        Assert.AreEqual(saved, Field<AccessoryConfiguration>(controller, "configuration"));
        Assert.AreEqual(saved, new DangoRosterStore(data.Root).LoadOrInitialize().Snapshot!.Find(initialized.SelectedId.Value)!.Accessories);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(data.Root, DangoRosterStore.RosterRelativePath)));
        Assert.IsFalse(File.Exists(AccessorySettingsStore.GetManifestPath(data.Root)));
        Assert.IsFalse(window.IsVisible);
        Assert.AreEqual(0, runtime.EntityIds.Count);
    }

    private static ControlCenterWindow Home(DangoApplicationHost host)
    {
        var window = (ControlCenterWindow)typeof(DangoApplicationHost)
            .GetMethod("GetOrCreateControlCenter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null)!;
        ProductHostTestSupport.WaitFor(() => window.ProductHome.CatalogSnapshot is not null,
            "Catalog IO must finish before the fixture can own and clean up its temporary data root.");
        return window;
    }

    private static void PrepareRoom(DangoApplicationHost host, ProductHomeView home)
    {
        // Acknowledge the fixture's body configuration to exercise the real identity lock.
        // This checks persistence and presentation composition, not visible-room admission or visual acceptance.
        var appearance = home.BoundAppearance!;
        Assert.IsTrue(appearance.MarkBodyApplied(appearance.DraftAppearance.Body, appearance.DraftAppearance.BodyPhysicsModel,
            appearance.BodyConfigurationRevision, appearance.BodyEpoch));
        object[] args = [Guid.Empty];
        Assert.IsTrue((bool)typeof(DangoApplicationHost).GetMethod("TryLockDangoForEntry", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, args)!);
        typeof(DangoApplicationHost).GetMethod("PrepareBeltRoomEntry", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host, null);
    }

    private static void AssertRoomPieces(StackedDangoSceneRuntime runtime, AccessorySceneController controller, AccessoryEditorProject? savedProject)
    {
        using var bodySource = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        bodySource.Update(new(0, 1d / 120, Enumerable.Range(0, 32).Select(i =>
            new BeltBodyBoundaryPoint(Math.Cos(i * Math.Tau / 32) * .85, Math.Sin(i * Math.Tau / 32) * .65)).ToArray(),
            0, 0, 0, 0, true, 0, false, false, false, false));
        var body = bodySource.Inspect().Body!;
        var frame = new StackedPhysicsPresentationFrame([body], [new LogicalRect(100, 100, 160, 120)], [default], 0, [], false, 0)
            { EntityIds = [new DangoEntityId(1)] };
        controller.Refresh(frame);
        Assert.IsNull(runtime.AccessoryError);
        var outputs = runtime.GetAccessoryFrames(frame, 0);
        Assert.AreEqual(savedProject?.RenderPieces.Count ?? 0, outputs.Count);
        for (var i = 0; i < outputs.Count; i++)
        {
            Assert.AreEqual(savedProject!.ProjectId, outputs[i].Item.ProjectId);
            var expected = savedProject.Assets[savedProject.RenderPieces[i].AssetId].Bitmap;
            var expectedPixels = new int[expected.PixelWidth * expected.PixelHeight]; expected.CopyPixels(expectedPixels, expected.PixelWidth * 4, 0);
            var actualPixels = new int[outputs[i].Texture.PixelWidth * outputs[i].Texture.PixelHeight];
            outputs[i].Texture.CopyPixels(actualPixels, outputs[i].Texture.PixelWidth * 4, 0);
            CollectionAssert.AreEqual(expectedPixels, actualPixels, "The room consumer must receive each saved piece's real pixels.");
        }
    }

    private static AccessoryEditResult Complete(AccessoryEditDraft ticket, int variant)
    {
        const int size = 32;
        var sources = new List<AccessoryEditorSourceAsset>(); var composite = new int[size * size];
        for (var piece = 0; piece < 2; piece++)
        {
            var pixels = new int[size * size];
            var color = unchecked((int)(0xff000000u | (uint)(40 + variant * 30) << 16 | (uint)(50 + piece * 80) << 8 | (uint)(20 + variant * 40 + piece * 20)));
            for (var y = 1; y < size - 1; y++)
            for (var x = piece == 0 ? 1 : 16; x < (piece == 0 ? 16 : size - 1); x++) pixels[y * size + x] = composite[y * size + x] = color;
            sources.Add(Source(pixels));
        }
        var image = AccessoryImageAsset.FromRenderedPng(Source(composite).CopyPngBytes());
        var jointY = variant == 1 ? 8 : 12;
        var skeleton = new AccessorySkeletonConfiguration(new("P", 8, 8),
            [new("A", "Synthetic A", null, [new("AR", 8, 8), new("AJ", 24, jointY)], new()),
                new("B", "Synthetic B", "AJ", [new("BR", 24, jointY), new("BJ", 24, 24)], new())], [new("L1", "A"), new("L2", "B")]);
        var project = new AccessoryEditorProject(size, size, ticket.Revision, image.AssetId,
            sources.Select((source, index) => new AccessoryProjectLayer($"L{index + 1}", "Synthetic layer", source.AssetId, 0, 0, size, size)),
            sources, skeleton, "Synthetic saved project", [new("A", sources[0].AssetId), new("B", sources[1].AssetId)]);
        return new(project, image, ticket.Original with
        {
            Name = project.Name, AssetId = image.AssetId, PixelWidth = size, PixelHeight = size,
            ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = ticket.Revision,
            DeformationMode = AccessoryDeformationMode.Skeleton, Skeleton = skeleton, Rig = null,
            ImagePivotX = .25, ImagePivotY = .25, IsPendingCompletion = false,
        });
        static AccessoryEditorSourceAsset Source(int[] pixels)
        {
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bitmap.Freeze();
            return AccessoryEditorSourceAsset.FromBitmap(bitmap);
        }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private sealed class DataDirectory : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "Dango-accessory-desktop-handoff", Guid.NewGuid().ToString("N"));
        internal string SettingsPath => Path.Combine(Root, "settings.json");
        internal DataDirectory() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Dango-accessory-desktop-handoff")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(Root).StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test directory boundary is invalid.");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
