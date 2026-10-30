using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Canvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessoryModalPreviewProjectTests
{
    [STATestMethod]
    public void UnwornItemIsTemporarilyVisibleInDynamicPreviewWithoutChangingWearState()
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-modal-unworn", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root);
        var selected = session.DraftConfiguration.Items[0];
        session.Stage(new(session.DraftConfiguration.Items.Select(item => item.Id == selected.Id ? item with { IsEquipped = false } : item)));
        var before = session.DraftConfiguration;
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        var visible = false;
        panel.PreviewChanged += configuration =>
        {
            if (panel.ModalPreviewResult is not null)
            {
                visible = configuration.Items.Single(item => item.Id == selected.Id).IsEquipped;
                Assert.AreEqual(before, session.DraftConfiguration);
            }
        };
        panel.UnifiedEditor = (ticket, _, _, preview) => { preview(Complete(ticket, 1)); return null; };
        panel.EditSelectedImage();
        Assert.IsTrue(visible);
        Assert.AreEqual(before, session.DraftConfiguration);
        Assert.IsFalse(session.DraftConfiguration.Items.Single(item => item.Id == selected.Id).IsEquipped);
        Assert.IsFalse(Directory.Exists(root));
    }

    [STATestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnstagedModalProjectReachesBothHostsAndCancellationRestoresTheOriginal(bool hasOriginalProject)
    {
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-modal-project", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root);
        AccessoryEditResult? originalResult = null;
        if (hasOriginalProject)
        {
            var initial = session.BeginEdit(session.DraftConfiguration.Items[0].Id);
            originalResult = Complete(initial, 1);
            Assert.IsTrue(session.TryCompleteEdit(initial, originalResult));
        }
        var before = session.DraftConfiguration; var saved = session.SavedConfiguration;
        var original = before.Items[0];
        var panel = new AccessoryEditorPanel(); panel.Bind(session);
        using var belt = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        belt.SetAccessories(before, panel.ResolvePreviewImage, panel.ResolvePreviewProject);
        belt.Update(BodyFrame());
        var body = belt.Inspect().Body!;
        var frame = new StackedPhysicsPresentationFrame([body], [new LogicalRect(100, 100, 160, 120)], [default], 0, [], false, 0)
            { EntityIds = [new DangoEntityId(1)] };
        using var runtime = new StackedDangoSceneRuntime(); // Projection only: no initialization/world/window.
        runtime.BindAccessories(session); runtime.SetAccessories(before, true);
        var scene = (AccessorySceneController)typeof(StackedDangoSceneRuntime).GetField("accessoryScene", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        scene.Refresh(frame);
        var baseline = runtime.GetAccessoryFrames(frame, 0).ToArray();
        var baselineCount = hasOriginalProject ? 2 : 1;
        Assert.AreEqual(baselineCount, baseline.Length); Assert.IsNull(runtime.AccessoryError);
        var changes = 0;
        panel.PreviewChanged += configuration =>
        {
            changes++;
            belt.SetAccessories(configuration, panel.ResolvePreviewImage, panel.ResolvePreviewProject);
            runtime.SetAccessories(configuration, true, panel.ResolvePreviewImage, panel.ResolvePreviewProject);
            scene.Refresh(frame);
        };
        AccessoryEditResult? modal = null; Action<AccessoryEditResult>? late = null;
        panel.UnifiedEditor = (ticket, _, _, preview) =>
        {
            modal = Complete(ticket, 2); late = preview;
            preview(modal);
            Assert.AreSame(modal.Project, panel.ResolvePreviewProject(modal.Item));
            Assert.AreNotSame(modal.Project, panel.ResolvePreviewProject(modal.Item with { Id = "foreign-item" }));
            Assert.AreNotSame(modal.Project, panel.ResolvePreviewProject(modal.Item with { EditRevision = modal.Item.EditRevision + 1 }));
            Assert.AreNotSame(modal.Project, panel.ResolvePreviewProject(modal.Item with { ProjectId = new string('f', 64) }));
            if (originalResult is not null) Assert.AreSame(originalResult.Project, panel.ResolvePreviewProject(original));
            else Assert.IsNull(panel.ResolvePreviewProject(original));
            Assert.AreEqual(before, session.DraftConfiguration, "Trial movement must not stage its project in the outer draft.");
            Assert.AreEqual(saved, session.SavedConfiguration);
            Assert.IsNull(belt.AccessoryError); Assert.AreEqual(2, belt.Inspect().Accessories.Count);
            var stacked = runtime.GetAccessoryFrames(frame, 0);
            Assert.IsNull(runtime.AccessoryError); Assert.AreEqual(2, stacked.Count);
            for (var i = 0; i < stacked.Count; i++)
                Assert.AreSame(modal.Project.Assets[modal.Project.RenderPieces[i].AssetId].Bitmap, stacked[i].Texture);
            AssertBeltTextures(belt, modal.Project);
            Assert.IsFalse(Directory.Exists(root), "Preview must not create settings, assets or project files.");
            return null;
        };
        panel.EditSelectedImage();
        Assert.IsTrue(changes >= 2); Assert.IsNull(panel.ModalPreviewResult);
        Assert.AreEqual(before, session.DraftConfiguration); Assert.AreEqual(saved, session.SavedConfiguration);
        Assert.AreEqual(baselineCount, belt.Inspect().Accessories.Count);
        var restored = runtime.GetAccessoryFrames(frame, 0);
        Assert.AreEqual(baselineCount, restored.Count); Assert.IsNull(runtime.AccessoryError);
        for (var i = 0; i < restored.Count; i++) Assert.AreSame(baseline[i].Texture, restored[i].Texture);
        if (originalResult is not null) AssertBeltTextures(belt, originalResult.Project);
        late!(modal!);
        Assert.IsNull(panel.ModalPreviewResult); Assert.AreEqual(before, session.DraftConfiguration);
        runtime.SetAccessories(before, true); scene.Refresh(frame);
        Assert.IsNull(typeof(StackedDangoSceneRuntime).GetField("accessoryProjectOverride", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime),
            "A normal presentation assignment must release the temporary modal provider.");
        Assert.AreEqual(baselineCount, runtime.GetAccessoryFrames(frame, 0).Count);
        Assert.AreSame(body, belt.Inspect().Body, "Resolving a trial project cannot advance or replace the owner body frame.");
        Assert.IsFalse(Directory.Exists(root));
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
            sources, skeleton, "Synthetic modal project", [new("A", sources[0].AssetId), new("B", sources[1].AssetId)]);
        var item = ticket.Original with
        {
            Name = project.Name, AssetId = image.AssetId, PixelWidth = size, PixelHeight = size,
            ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = ticket.Revision,
            DeformationMode = AccessoryDeformationMode.Skeleton, Skeleton = skeleton, Rig = null,
            ImagePivotX = .25, ImagePivotY = .25, IsPendingCompletion = false,
        };
        return new(project, image, item);

        static AccessoryEditorSourceAsset Source(int[] pixels)
        {
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4); bitmap.Freeze();
            return AccessoryEditorSourceAsset.FromBitmap(bitmap);
        }
    }

    private static void AssertBeltTextures(BeltBodyAppearancePresenter belt, AccessoryEditorProject project)
    {
        var gait = (Canvas)belt.View.Children[0];
        var visuals = gait.Children.OfType<Canvas>().SelectMany(layer => layer.Children.OfType<AccessoryMeshVisual>()).ToArray();
        Assert.AreEqual(project.RenderPieces.Count, visuals.Length);
        for (var i = 0; i < visuals.Length; i++)
        {
            var expected = Pixels(project.Assets[project.RenderPieces[i].AssetId].Bitmap).First(pixel => (uint)pixel >> 24 == 255);
            Assert.IsTrue(Pixels(visuals[i].CaptureBitmap()!).Contains(expected), "Belt must rasterize the modal project's real piece texture.");
        }
        static int[] Pixels(BitmapSource bitmap)
        {
            var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels;
        }
    }

    private static BeltBodyAppearanceFrame BodyFrame() => new(0, 1d / 120,
        Enumerable.Range(0, 32).Select(i => new BeltBodyBoundaryPoint(Math.Cos(i * Math.Tau / 32) * .85,
            Math.Sin(i * Math.Tau / 32) * .65)).ToArray(), 0, 0, 0, 0, true, 0, false, false, false, false);
}
