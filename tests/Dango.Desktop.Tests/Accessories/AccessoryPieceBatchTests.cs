using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Canvas = System.Windows.Controls.Canvas;

namespace Dango.Desktop.Tests.Accessories;

[TestClass, DoNotParallelize]
public sealed class AccessoryPieceBatchTests
{
    [STATestMethod]
    [DataRow(9)]
    [DataRow(128)]
    public void LegalPieceBatchRasterizesEveryDistinctVisualBeyondTheEquippedItemLimit(int count)
    {
        var texture = Source(0).Bitmap;
        var item = new AccessoryItem("synthetic-batch", "Synthetic batch", "builtin-sprout", 64, 64);
        var frame = new AccessoryMotionSolver().Update(0, 1d / 60, new(40, 40), 8, 8, item);
        var requests = Enumerable.Range(0, count).Select(_ =>
        {
            var visual = new AccessoryMeshVisual(); visual.SetTexture(texture); return (Visual: visual, Frame: frame);
        }).ToArray();
        var result = AccessoryMeshVisual.ApplyBatch(requests);
        Assert.HasCount(0, result.Errors);
        var reference = new AccessoryMeshVisual(); reference.SetTexture(texture); reference.ApplyFrame(frame);
        var expected = Pixels(reference.CaptureBitmap()!);
        foreach (var request in requests)
        {
            Assert.AreSame(frame, request.Visual.Frame);
            CollectionAssert.AreEqual(expected, Pixels(request.Visual.CaptureBitmap()!), "Each piece must upload the same pixels as the single-frame renderer.");
        }
    }

    [STATestMethod]
    public void BatchRejectsExcessAndCrossBatchDuplicateBeforeChangingAnyVisual()
    {
        var frame = new AccessoryMotionSolver().Update(0, 1d / 60, new(40, 40), 8, 8,
            new("synthetic-limit", "Synthetic limit", "builtin-sprout", 64, 64));
        var requests = Enumerable.Range(0, 129).Select(_ => (Visual: new AccessoryMeshVisual(), Frame: frame)).ToArray();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AccessoryMeshVisual.ApplyBatch(requests));
        Assert.IsTrue(requests.All(request => request.Visual.Frame is null));
        var repeated = requests.Take(8).Append(requests[0]).ToArray();
        Assert.ThrowsExactly<ArgumentException>(() => AccessoryMeshVisual.ApplyBatch(repeated));
        Assert.IsTrue(requests.All(request => request.Visual.Frame is null), "Global duplicate validation must precede all bounded preparation batches.");
    }

    [STATestMethod]
    public void RasterFaultInALaterPreparationBatchDoesNotDropOtherPieces()
    {
        var frame = new AccessoryMotionSolver().Update(0, 1d / 60, new(40, 40), 8, 8,
            new("synthetic-fault", "Synthetic fault", "builtin-sprout", 64, 64));
        var reversed = frame.TriangleIndices.ToArray(); (reversed[0], reversed[1]) = (reversed[1], reversed[0]);
        var texture = Source(0).Bitmap;
        var requests = Enumerable.Range(0, 17).Select(index =>
        {
            var visual = new AccessoryMeshVisual(); visual.SetTexture(texture);
            return (Visual: visual, Frame: index == 8 ? frame with { TriangleIndices = reversed, AllowFolding = false } : frame);
        }).ToArray();
        var result = AccessoryMeshVisual.ApplyBatch(requests);
        Assert.HasCount(1, result.Errors); Assert.AreSame(requests[8].Visual, result.Errors[0].Visual);
        for (var index = 0; index < requests.Length; index++)
            if (index == 8) Assert.IsNull(requests[index].Visual.Frame);
            else Assert.IsTrue(Pixels(requests[index].Visual.CaptureBitmap()!).Any(pixel => pixel != 0));
        Assert.IsTrue(result.PreparationMilliseconds >= 0 && result.RasterMilliseconds >= 0 && result.UploadMilliseconds >= 0);
    }

    [STATestMethod]
    public void BeltPresenterRendersEightEquippedItemsWithSixteenPiecesEach()
    {
        var (configuration, project) = Fixture();
        using var presenter = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        presenter.SetAccessories(configuration, _ => throw new AssertFailedException("Grouped pieces cannot fall back to a flattened image."), _ => project);
        for (var tick = 0; tick < 3; tick++) presenter.Update(BodyFrame(tick));
        Assert.IsNull(presenter.AccessoryError); Assert.AreEqual(8, presenter.RenderedAccessoryCount);
        var inspected = presenter.Inspect(); Assert.HasCount(128, inspected.Accessories);
        Assert.IsTrue(inspected.Accessories.All(piece => piece.Frame.StepIndex == inspected.Tick));
        var visuals = Descendants(presenter.View).OfType<AccessoryMeshVisual>().Where(visual => visual.Frame is not null).ToArray();
        Assert.HasCount(128, visuals); Assert.IsTrue(visuals.All(visual => Pixels(visual.CaptureBitmap()!).Any(pixel => pixel != 0)));
    }

    [STATestMethod]
    public void ActualStackedRuntimeAndHostRasterizeAllLegalPiecesWithoutAnExtraOwnerStep()
    {
        var (configuration, project) = Fixture();
        var root = Path.Combine(TestRepository.Root, "artifacts", "accessory-piece-batch", Guid.NewGuid().ToString("N"));
        var session = new AccessorySettingsSession(root);
        using var runtime = StackedDangoSceneRuntime.CreateBeltRoom(Path.Combine(AppContext.BaseDirectory, "dango_belt_room.dll"));
        runtime.BindAccessories(session); runtime.SetAccessories(configuration, true, _ => null, _ => project);
        Assert.IsTrue(runtime.TryInitialize(), runtime.LastError); runtime.Start(); runtime.SetViewport(new(0, 0, 1600, 900));
        var input = new PhysicsPresentationInput("synthetic-piece-batch", new(400, 350, 160, 120), DangoActionState.Idle,
            0, 1, "synthetic-ground", new(0, 1), 0, 1, false, true, []);
        var frame = runtime.Tick(input, TimeSpan.FromSeconds(1d / 60));
        var outputs = runtime.GetAccessoryFrames(frame, 0); Assert.HasCount(128, outputs); Assert.IsNull(runtime.AccessoryError);
        var canvas = new Canvas(); var body = new DeformableDangoVisual(); var hat = new DangoHatVisual(); canvas.Children.Add(body); canvas.Children.Add(hat);
        var ownerFrame = runtime.LastBeltRoomFrame; var steps = outputs.Select(output => output.Mesh.StepIndex).ToArray();
        for (var paint = 0; paint < 2; paint++) DangoHostFrameBinding.Apply(body, hat, frame, 0, runtime: runtime);
        var visuals = Descendants(canvas).OfType<AccessoryMeshVisual>().ToArray();
        Assert.HasCount(128, visuals); Assert.IsNull(runtime.AccessoryError);
        Assert.IsTrue(visuals.All(visual => visual.Frame is not null && Pixels(visual.CaptureBitmap()!).Any(pixel => pixel != 0)));
        Assert.AreSame(ownerFrame, runtime.LastBeltRoomFrame); CollectionAssert.AreEqual(steps, runtime.GetAccessoryFrames(frame, 0).Select(output => output.Mesh.StepIndex).ToArray());
        Assert.IsFalse(Directory.Exists(root), "No-window rendering must not persist the fictional configuration."); runtime.Stop();
    }

    private static (AccessoryConfiguration Configuration, AccessoryEditorProject Project) Fixture()
    {
        var assets = Enumerable.Range(0, 16).Select(Source).ToArray();
        var layers = assets.Select((asset, index) => new AccessoryProjectLayer($"L{index}", $"Synthetic {index}", asset.AssetId, 0, 0, 8, 8)).ToArray();
        var groups = Enumerable.Range(0, 8).Select(index => new AccessorySkeletonGroup($"G{index}", $"Synthetic group {index}", null,
            [new($"R{index}", 4, 4)], new())).ToArray();
        var skeleton = new AccessorySkeletonConfiguration(new("P", 4, 4), groups,
            layers.Select((layer, index) => new AccessorySkeletonLayer(layer.Id, index % 2 == 0 ? $"G{index / 2}" : null)).ToArray());
        var project = new AccessoryEditorProject(8, 8, 1, assets[0].AssetId, layers, assets, skeleton, "Synthetic sixteen pieces",
            assets.Select((asset, index) => new AccessoryProjectRenderPiece(skeleton.Layers[index].GroupId, asset.AssetId)));
        var items = Enumerable.Range(0, 8).Select(index => new AccessoryItem($"synthetic-item-{index}", "Synthetic worn item", project.FinalAssetId, 8, 8)
        { ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = project.Revision, DeformationMode = AccessoryDeformationMode.Skeleton,
            Skeleton = skeleton, Order = index, Layer = index % 2 == 0 ? AccessoryLayer.Front : AccessoryLayer.Behind }).ToArray();
        return (new(items), project);
    }

    private static AccessoryEditorSourceAsset Source(int index)
    {
        var pixels = Enumerable.Repeat(unchecked((int)(0xff202020u + (uint)(index * 0x080301))), 64).ToArray();
        var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Pbgra32, null, pixels, 32); bitmap.Freeze();
        return AccessoryEditorSourceAsset.FromBitmap(bitmap);
    }
    private static int[] Pixels(BitmapSource bitmap)
    { var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels; }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        { var child = VisualTreeHelper.GetChild(parent, index); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static BeltBodyAppearanceFrame BodyFrame(int tick) => new(tick, 1d / 120,
        Enumerable.Range(0, 32).Select(index => new BeltBodyBoundaryPoint(Math.Cos(index * Math.Tau / 32) * .85, Math.Sin(index * Math.Tau / 32) * .65)).ToArray(),
        0, 0, .24, 0, true, 0, false, false, false, false);
}
