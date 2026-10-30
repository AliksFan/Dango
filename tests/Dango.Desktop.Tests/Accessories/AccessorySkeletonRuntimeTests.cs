using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.Accessories.Editing;
using Dango.Desktop.Accessories.Rig;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;

namespace Dango.Desktop.Tests.Accessories;

[TestClass]
public sealed class AccessorySkeletonRuntimeTests
{
    private const double Dt = 1d / 120;
    private static readonly Point Anchor = new(100, 100);

    private static (AccessoryItem Item, AccessoryEditorProject Project) Fixture(double groupGravity = 10)
    {
        var assets = Enumerable.Range(0, 4).Select(index =>
        {
            var pixels = Enumerable.Range(0, 10000).SelectMany(_ => new byte[] { (byte)(20 + index * 40), (byte)(150 - index * 20), 100, 128 }).ToArray();
            var bitmap = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, pixels, 400);
            bitmap.Freeze(); return AccessoryEditorSourceAsset.FromBitmap(bitmap);
        }).ToArray();
        var layers = assets.Select((asset, index) => new AccessoryProjectLayer($"L{index}", $"Synthetic layer {index}", asset.AssetId, 0, 0, 100, 100)).ToArray();
        string?[] groupIds = ["A", null, "B", "C"];
        var skeleton = new AccessorySkeletonConfiguration(new("P", 10, 50),
            [new("A", "Synthetic A", null, [new("AR", 10, 50), new("A1", 40, 50)], new(Bend: 1, Restoration: 0)),
                new("B", "Synthetic B", "A1", [new("BR", 40, 50), new("B1", 40, 80)], new(Bend: 1, Restoration: 0, Gravity: groupGravity)),
                new("C", "Synthetic root only", "A1", [new("CR", 40, 50)], new())],
            layers.Select((layer, index) => new AccessorySkeletonLayer(layer.Id, groupIds[index])).ToArray());
        var project = new AccessoryEditorProject(100, 100, 1, assets[0].AssetId, layers, assets, skeleton, "Synthetic runtime",
            assets.Select((asset, index) => new AccessoryProjectRenderPiece(groupIds[index], asset.AssetId)));
        var item = new AccessoryItem("synthetic-runtime", project.Name, project.FinalAssetId, 100, 100)
        {
            DeformationMode = AccessoryDeformationMode.Skeleton, Skeleton = skeleton,
            ProjectId = project.ProjectId, ProjectVersion = project.Version, EditRevision = project.Revision,
            ImagePivotX = .1, ImagePivotY = .5, ConnectionStrength = 100, LockRotation = true
        };
        return (item, project);
    }

    [TestMethod]
    public void PolylineSkinAcceptsCrossingsHairpinsAndShortBonesAndPreservesRectangularRestImage()
    {
        Point[][] shapes =
        [
            [new(10, 10), new(190, 90), new(10, 90), new(190, 10)],
            [new(10, 10), new(190, 10), new(20, 10)],
            [new(10, 10), new(10.00001, 10), new(20, 20)]
        ];
        foreach (var shape in shapes)
        {
            var configuration = new AccessorySkeletonConfiguration(new("P", 25, 30),
                [new("A", "Synthetic shape", null, shape.Select((point, index) => new AccessorySkeletonNode($"N{index}", point.X, point.Y)).ToArray(), new())], []);
            var solver = new AccessorySkeletonSolver(configuration, 200, 100);
            solver.Resume(0, new(new(3, 4), .3, 20));
            var skin = new AccessorySkeletonSkin(configuration, 200, 100, "A");
            var frame = skin.Deform(solver.Refresh());
            Assert.AreEqual(256, frame.Vertices.Count); Assert.AreEqual(450, frame.TriangleIndices.Count / 3);
            Assert.IsTrue(skin.Weights.All(weight => weight.FirstWeight >= 0 && weight.FirstWeight <= 1 && weight.FirstWeight + weight.SecondWeight == 1));
            for (var i = 0; i < frame.Vertices.Count; i++)
            {
                var uv = frame.TextureCoordinates[i];
                var offset = new Vector(uv.X * 2 - .25, uv.Y - .3) * 20;
                var expected = new Point(3, 4) + new Vector(offset.X * Math.Cos(.3) - offset.Y * Math.Sin(.3), offset.X * Math.Sin(.3) + offset.Y * Math.Cos(.3));
                Assert.IsTrue((frame.Vertices[i] - expected).Length < 1e-7, "Binding must preserve the complete image rest shape even near short or crossed segments.");
            }
        }
    }

    [TestMethod]
    public void SkinMapsAxialStretchAndRootOnlyFrameWithoutScalingNormalDistanceAgain()
    {
        var configuration = new AccessorySkeletonConfiguration(new("P", 50, 50),
            [new("A", "Synthetic stretch", null, [new("AR", 25, 50), new("A1", 75, 50)], new(Elasticity: .5)),
                new("B", "Synthetic root", "A1", [new("BR", 75, 50)], new())], []);
        var solver = new AccessorySkeletonSolver(configuration, 100, 100); solver.Resume(0, new(Anchor, 0, 100));
        var source = solver.Refresh();
        var snapshot = new AccessorySkeletonSnapshot(1, source.Pose, source.Configuration,
            new Dictionary<string, IReadOnlyList<Point>> { ["A"] = [new(75, 100), new(150, 100)], ["B"] = [new(150, 100)] },
            new Dictionary<string, AccessoryRigPose> { ["A"] = new(new(75, 100), 0, 100), ["B"] = new(new(150, 100), Math.PI / 2, 100) });
        var stretch = new AccessorySkeletonSkin(configuration, 100, 100, "A").Deform(snapshot);
        Assert.AreEqual(new Point(37.5, 50), stretch.Vertices[0]);
        Assert.AreEqual(new Point(187.5, 150), stretch.Vertices[^1]);
        var root = new AccessorySkeletonSkin(configuration, 100, 100, "B").Deform(snapshot);
        Assert.AreEqual(200, root.Vertices[0].X, 1e-10); Assert.AreEqual(25, root.Vertices[0].Y, 1e-10);
        var ungrouped = new AccessorySkeletonSkin(configuration, 100, 100, null).Deform(snapshot);
        Assert.AreEqual(new Point(50, 50), ungrouped.Vertices[0]);
    }

    [STATestMethod]
    public void PiecesShareOneOwnerStepAndRefreshCannotChangeTheFollowingMotion()
    {
        var (item, project) = Fixture();
        var painted = new AccessoryMotionSolver(); var control = new AccessoryMotionSolver();
        for (var tick = 0; tick < 20; tick++)
        {
            painted.UpdatePieces(tick, Dt, Anchor + new Vector(tick * .3, 0), 64, 64, item, project);
            control.UpdatePieces(tick, Dt, Anchor + new Vector(tick * .3, 0), 64, 64, item, project);
        }
        var before = painted.ReadSkeletonSnapshot()!;
        Assert.AreEqual(19L, before.Tick); Assert.AreEqual(4, painted.CurrentPieces.Count);
        Assert.IsTrue(painted.CurrentPieces.All(piece => piece.StepIndex == before.Tick));
        Assert.IsTrue(painted.CurrentPieces.Sum(piece => piece.Vertices.Count) <= 4096);
        Assert.IsTrue(painted.CurrentPieces.Sum(piece => piece.TriangleIndices.Count / 3) <= 7200);
        var prior = painted.CurrentPieces;
        for (var paint = 0; paint < 10; paint++)
        {
            var projection = painted.RefreshPieces(new(205.7, 200), 64, 64, item, project);
            AssertMesh(prior[0].Translated(new(100, 100)), projection[0]);
            Assert.AreEqual(before.Tick, painted.ReadSkeletonSnapshot()!.Tick);
            CollectionAssert.AreEqual(before.Groups["A"].ToArray(), painted.ReadSkeletonSnapshot()!.Groups["A"].ToArray());
        }
        var a = painted.UpdatePieces(20, Dt, new(106, 100), 64, 64, item, project);
        var b = control.UpdatePieces(20, Dt, new(106, 100), 64, 64, item, project);
        for (var i = 0; i < a.Count; i++) AssertMesh(a[i], b[i]);
        var repeated = painted.UpdatePieces(20, Dt, new(900, 900), 64, 64, item, project);
        Assert.AreSame(a, repeated, "Several consumers or pieces cannot advance a successful owner tick twice.");
    }

    [STATestMethod]
    public void GroupGravityAndOuterGravityActOnIndependentDegreesOfFreedom()
    {
        var (groupItem, groupProject) = Fixture(20); var (zeroItem, zeroProject) = Fixture(0);
        var group = new AccessoryMotionSolver(); var outer = new AccessoryMotionSolver(); var zero = new AccessoryMotionSolver();
        var outerItem = zeroItem with { ConnectionStrength = 0, GravityInfluence = 100 };
        for (var tick = 0; tick < 80; tick++)
        {
            group.UpdatePieces(tick, Dt, Anchor, 64, 64, groupItem, groupProject);
            outer.UpdatePieces(tick, Dt, Anchor, 64, 64, outerItem, zeroProject);
            zero.UpdatePieces(tick, Dt, Anchor, 64, 64, zeroItem, zeroProject);
        }
        Assert.AreEqual(Anchor, group.CurrentPieces[0].ImagePivot);
        Assert.AreEqual(default(Vector), group.CurrentPieces[0].ConnectionOffset);
        Assert.IsTrue(outer.CurrentPieces[0].ConnectionOffset.Y > 1);
        Assert.IsTrue((group.ReadSkeletonSnapshot()!.Groups["A"][1] - zero.ReadSkeletonSnapshot()!.Groups["A"][1]).Length > 1,
            "A group's force must still transfer to the connected upstream group without moving external P.");
        var ungrouped = group.CurrentPieces[1];
        Assert.AreEqual(new Point(93.6, 68), ungrouped.Vertices[0]);
    }

    [STATestMethod]
    public void MissingProjectAndMismatchedRevisionCannotFallbackToFlattenedPng()
    {
        var (item, project) = Fixture(); var solver = new AccessoryMotionSolver();
        Assert.ThrowsExactly<InvalidOperationException>(() => solver.RefreshPieces(Anchor, 64, 64, item));
        Assert.ThrowsExactly<ArgumentException>(() => solver.RefreshPieces(Anchor, 64, 64, item with { EditRevision = 2 }, project));
        Assert.ThrowsExactly<ArgumentException>(() => solver.RefreshPieces(Anchor, 64, 64, item with { Skeleton = item.Skeleton! with { Pivot = new("P", 20, 50) } }, project));
        Assert.IsNull(solver.ReadSkeletonSnapshot());
    }

    [STATestMethod]
    public void SixteenPiecesStayAtTheTotalVertexAndTriangleBudget()
    {
        var (original, sourceProject) = Fixture();
        var asset = sourceProject.Assets.Values.First();
        var layers = Enumerable.Range(0, 16).Select(index => new AccessoryProjectLayer($"L{index}", "Synthetic layer", asset.AssetId, 0, 0, 100, 100)).ToArray();
        string? GroupId(int index) => index % 2 == 0 ? $"G{index / 2}" : null;
        var configuration = new AccessorySkeletonConfiguration(new("P", 10, 50),
            Enumerable.Range(0, 8).Select(index => new AccessorySkeletonGroup($"G{index}", "Synthetic root", null,
                [new($"R{index}", 10 + index, 50)], new())).ToArray(),
            layers.Select((layer, index) => new AccessorySkeletonLayer(layer.Id, GroupId(index))).ToArray());
        var project = new AccessoryEditorProject(100, 100, 1, asset.AssetId, layers, [asset], configuration, original.Name,
            layers.Select((_, index) => new AccessoryProjectRenderPiece(GroupId(index), asset.AssetId)));
        var item = original with { AssetId = asset.AssetId, ProjectId = project.ProjectId, Skeleton = configuration };
        var meshes = new AccessoryMotionSolver().RefreshPieces(Anchor, 64, 64, item, project);
        Assert.AreEqual(16, meshes.Count); Assert.AreEqual(4096, meshes.Sum(piece => piece.Vertices.Count));
        Assert.AreEqual(7200, meshes.Sum(piece => piece.TriangleIndices.Count / 3));
    }

    [STATestMethod]
    public void PauseAndLongOwnerFramesRebaseAllPiecesWithoutHiddenElapsedTime()
    {
        var (item, project) = Fixture(); var solver = new AccessoryMotionSolver();
        for (var tick = 0; tick < 20; tick++) solver.UpdatePieces(tick, Dt, Anchor, 64, 64, item, project);
        solver.Paused = true; solver.RefreshPieces(new(200, 200), 64, 64, item, project); solver.Paused = false;
        var resumed = solver.UpdatePieces(20, Dt, new(200, 200), 64, 64, item, project);
        var fresh = new AccessoryMotionSolver().UpdatePieces(20, Dt, new(200, 200), 64, 64, item, project);
        for (var i = 0; i < resumed.Count; i++) AssertMesh(fresh[i], resumed[i]);
        var longFrame = solver.UpdatePieces(21, .08, Anchor, 64, 64, item, project);
        fresh = new AccessoryMotionSolver().UpdatePieces(21, Dt, Anchor, 64, 64, item, project);
        for (var i = 0; i < longFrame.Count; i++) AssertMesh(fresh[i], longFrame[i]);
    }

    [STATestMethod]
    public void StackedConsumerKeepsPieceTextureOrderAndReadsOneOwnerSnapshot()
    {
        var (item, project) = Fixture();
        using var source = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        source.Update(BodyFrame(0)); var body = source.Inspect().Body!;
        StackedPhysicsPresentationFrame Frame(int tick) => new([body], [new LogicalRect(100 + tick * .2, 100, 160, 120)], [default], 0, [], false, 0)
            { EntityIds = [new DangoEntityId(1)] };
        var projectReads = 0;
        var scene = new AccessorySceneController(_ => throw new AssertFailedException("A skeleton cannot read the flattened image as its render source."),
            candidate => { projectReads++; return candidate.Id == item.Id ? project : null; });
        scene.SetConfiguration(new([item]), true, Frame(0));
        for (var tick = 0; tick < 10; tick++) scene.Advance(Frame(tick), Dt);
        Assert.IsNull(scene.Error); Assert.AreEqual(4, scene.Frames.Count);
        for (var i = 0; i < scene.Frames.Count; i++)
            Assert.AreSame(project.Assets[project.RenderPieces[i].AssetId].Bitmap, scene.Frames[i].Texture);
        var before = scene.Frames.ToArray(); scene.Refresh(Frame(9));
        Assert.AreEqual(1, projectReads, "Successful owner frames and paints must reuse the decoded immutable project.");
        for (var i = 0; i < before.Length; i++) AssertMesh(before[i].Mesh, scene.Frames[i].Mesh);
        Assert.AreSame(body, Frame(9).Bodies[0]);
        var missing = new AccessorySceneController(_ => project.Assets.Values.First().Bitmap);
        missing.SetConfiguration(new([item]), true, Frame(0));
        Assert.IsNotNull(missing.Error); Assert.AreEqual(0, missing.Frames.Count);

        var ready = false; var retryReads = 0;
        var retry = new AccessorySceneController(_ => null, _ => { retryReads++; return ready ? project : null; });
        retry.SetConfiguration(new([item]), true, Frame(0));
        for (var tick = 1; tick < 5; tick++) retry.Advance(Frame(tick), Dt);
        Assert.AreEqual(1, retryReads, "Unavailable projects must not be read repeatedly on each frame.");
        ready = true; retry.SetConfiguration(new([item]), true, Frame(5));
        Assert.AreEqual(2, retryReads); Assert.IsNull(retry.Error); Assert.AreEqual(4, retry.Frames.Count);
    }

    [STATestMethod]
    public void BeltConsumerRendersSeveralPiecesPerItemWithoutMultipleBodyOrSkeletonSteps()
    {
        var (item, project) = Fixture(); var configuration = new AccessoryConfiguration([item]);
        using var rendered = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        using var bodyOnly = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        using var missing = new BeltBodyAppearancePresenter(DangoAppearanceConfiguration.Default);
        missing.SetAccessories(configuration, _ => project.Assets.Values.First().Bitmap);
        Assert.IsNotNull(missing.AccessoryError); Assert.AreEqual(0, missing.RenderedAccessoryCount);
        var projectReads = 0;
        AccessoryEditorProject? Project(AccessoryItem _) { projectReads++; return project; }
        rendered.SetAccessories(configuration, _ => throw new AssertFailedException("Do not flatten a grouped render."), Project);
        for (var tick = 0; tick < 10; tick++) { rendered.Update(BodyFrame(tick)); bodyOnly.Update(BodyFrame(tick)); }
        var before = rendered.Inspect();
        Assert.IsNull(rendered.AccessoryError); Assert.AreEqual(1, rendered.RenderedAccessoryCount);
        Assert.AreEqual(4, before.Accessories.Count);
        CollectionAssert.AreEqual(bodyOnly.Inspect().Shape!.Boundary.ToArray(), before.Shape!.Boundary.ToArray());
        rendered.Update(BodyFrame(9));
        rendered.SetAccessories(configuration, _ => null, Project);
        var refreshed = rendered.Inspect();
        Assert.AreSame(before.Body, refreshed.Body); Assert.AreEqual(before.Tick, refreshed.Tick);
        Assert.AreEqual(1, projectReads, "A stable wearing refresh must retain its immutable project and textures.");
        for (var i = 0; i < before.Accessories.Count; i++) AssertMesh(before.Accessories[i].Frame, refreshed.Accessories[i].Frame);
    }

    [STATestMethod]
    public void HostBindingRasterizesDistinctPieceVisualsInTransparentDrawingOrder()
    {
        var (item, project) = Fixture(); var motion = new AccessoryMotionSolver();
        for (var tick = 0; tick < 12; tick++) motion.UpdatePieces(tick, Dt, Anchor, 64, 64, item, project);
        var outputs = project.RenderPieces.Select((piece, index) =>
            new AccessoryVisualFrame(item, project.Assets[piece.AssetId].Bitmap, motion.CurrentPieces[index]) { PieceIndex = index }).ToArray();
        var parent = new System.Windows.Controls.Canvas(); var body = new DeformableDangoVisual(); parent.Children.Add(body);
        var errors = new List<string?>(); var bounds = new LogicalRect(100, 100, 160, 120);
        AccessoryHostBinding.ApplyFrames(body, outputs, bounds, default, errors.Add);
        Assert.AreEqual(0, errors.Count, "ApplyBatch must receive each distinct piece visual exactly once.");
        var layer = parent.Children.OfType<System.Windows.Controls.Canvas>().Single(canvas => canvas.Children.Count > 0);
        var visuals = layer.Children.OfType<AccessoryMeshVisual>().ToArray();
        Assert.AreEqual(4, visuals.Length); Assert.AreEqual(4, visuals.Distinct().Count());
        var palette = visuals.Select(FirstNonzeroPixel).ToArray();
        Assert.AreEqual(4, palette.Distinct().Count(), "Each piece must rasterize its own original texture.");
        for (var i = 0; i < visuals.Length; i++) AssertMesh(outputs[i].Mesh.Translated(new(-100, -100)), visuals[i].Frame!);
        AccessoryHostBinding.ApplyFrames(body, outputs.Reverse().ToArray(), bounds, default, errors.Add);
        var reordered = layer.Children.OfType<AccessoryMeshVisual>().ToArray();
        CollectionAssert.AreEqual(visuals.Reverse().ToArray(), reordered);
        CollectionAssert.AreEqual(palette.Reverse().ToArray(), reordered.Select(FirstNonzeroPixel).ToArray());
        AccessoryHostBinding.ApplyFrames(body, [outputs[0], outputs[2], outputs[3]], bounds, default, errors.Add);
        Assert.AreEqual(3, layer.Children.Count, "Removed pieces must leave no old visual in the transparency stack.");
        Assert.AreEqual(11L, motion.ReadSkeletonSnapshot()!.Tick, "Host painting cannot advance any skeleton state.");
        Assert.ThrowsExactly<ArgumentException>(() => AccessoryHostBinding.ApplyFrames(body, [outputs[0], outputs[0]], bounds, default, errors.Add));

        static int FirstNonzeroPixel(AccessoryMeshVisual visual)
        {
            var bitmap = visual.CaptureBitmap(); Assert.IsNotNull(bitmap);
            var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            return pixels.First(pixel => pixel != 0);
        }
    }

    private static BeltBodyAppearanceFrame BodyFrame(int tick) => new(tick, Dt,
        Enumerable.Range(0, 32).Select(i =>
        {
            var angle = i * Math.Tau / 32;
            return new BeltBodyBoundaryPoint(tick * .002 + Math.Cos(angle) * .85, Math.Sin(angle) * .65);
        }).ToArray(), tick * .002, 0, .24, 0, true, 0, false, false, false, false);

    private static void AssertMesh(AccessoryMeshFrame expected, AccessoryMeshFrame actual)
    {
        Assert.AreEqual(expected.StepIndex, actual.StepIndex);
        Assert.AreEqual(expected.Vertices.Count, actual.Vertices.Count);
        for (var i = 0; i < expected.Vertices.Count; i++) Assert.IsTrue((expected.Vertices[i] - actual.Vertices[i]).Length < 1e-8);
        CollectionAssert.AreEqual(expected.TextureCoordinates.ToArray(), actual.TextureCoordinates.ToArray());
    }
}
