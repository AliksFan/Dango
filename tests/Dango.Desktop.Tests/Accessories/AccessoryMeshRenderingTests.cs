using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

// Legacy flexible-field visible-window and 16 ms budget gates are retired.
// Chain cost measurements belong to AccessoryRigRasterTests / AccessoryRigMechanismTests;
// these tests retain renderer pixels, seams, cache invalidation and batch isolation.
[TestClass]
public sealed class AccessoryMeshRenderingTests
{
    public TestContext TestContext { get; set; } = null!;

    [STATestMethod]
    public void WorkerRasterErrorsPreserveTheHostExceptionIsolationContract()
    {
        var solver = new AccessoryMotionSolver(); var frame = solver.Update(0, 1d / 60, new(250, 250), 256, 256, Item());
        var reversed = frame.TriangleIndices.ToArray(); (reversed[0], reversed[1]) = (reversed[1], reversed[0]);
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture());
        var invalid = frame with { TriangleIndices = Array.AsReadOnly(reversed), AllowFolding = false };
        Assert.ThrowsExactly<ArgumentException>(() => visual.ApplyFrame(invalid));
        Assert.ThrowsExactly<ArgumentException>(() => visual.ApplyFrame(invalid), "A failed raster must not be accepted as cached geometry.");
    }

    [STATestMethod]
    public void ShrinkingRasterErasesThePreviousMaterialWithinReusableBitmapCapacity()
    {
        var texture = Texture(); var dpi = new DpiScale(1.5, 1.5);
        var solver = new AccessoryMotionSolver(); var visual = new AccessoryMeshVisual(); visual.SetTexture(texture);
        visual.ApplyFrame(solver.Update(0, 1d / 60, new(80, 80), 256, 256, Item()), dpi);
        var large = visual.CaptureBitmap()!; var before = Pixels(large);
        var smallFrame = solver.Refresh(new(80, 80), 64, 64, Item());
        var fresh = new AccessoryMeshVisual(); fresh.SetTexture(texture); fresh.ApplyFrame(smallFrame, dpi);
        var expected = fresh.CaptureBitmap()!; var expectedPixels = Pixels(expected);
        Assert.IsTrue(before.Skip(expected.PixelHeight * large.PixelWidth).Any(p => p != 0), "Fixture must have old material outside the smaller image.");
        visual.ApplyFrame(smallFrame, dpi);
        var shrunk = visual.CaptureBitmap()!; var actual = Pixels(shrunk);
        Assert.AreEqual(large.PixelWidth, shrunk.PixelWidth); Assert.AreEqual(large.PixelHeight, shrunk.PixelHeight);
        for (var y = 0; y < shrunk.PixelHeight; y++)
            for (var x = 0; x < shrunk.PixelWidth; x++)
            {
                var pixel = y < expected.PixelHeight && x < expected.PixelWidth ? expectedPixels[y * expected.PixelWidth + x] : 0;
                Assert.AreEqual(pixel, actual[y * shrunk.PixelWidth + x], $"Shrunk material left stale pixels at {x},{y}.");
            }
    }

    [STATestMethod]
    public void ChangedUvsAndClearedFramesCannotReusePreviousRasterPixels()
    {
        var solver = new AccessoryMotionSolver(); var frame = solver.Update(0, 1d / 60, new(80, 80), 64, 64, Item());
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture()); visual.ApplyFrame(frame);
        var original = Pixels(visual.CaptureBitmap()!);
        var reflected = frame with { TextureCoordinates = Array.AsReadOnly(frame.TextureCoordinates.Select(uv => new Point(1 - uv.X, uv.Y)).ToArray()) };
        visual.ApplyFrame(reflected);
        Assert.IsFalse(original.SequenceEqual(Pixels(visual.CaptureBitmap()!)), "Changed UVs require new original-texture sampling.");
        visual.ClearFrame(); Assert.IsNull(visual.Frame);
        var batch = AccessoryMeshVisual.ApplyBatch([(visual, frame)]);
        Assert.HasCount(0, batch.Errors);
        CollectionAssert.AreEqual(original, Pixels(visual.CaptureBitmap()!));
    }

    [STATestMethod]
    public void BatchUsesTheSameCompleteSourcePixelsAndIsolatesOneReversedMesh()
    {
        var solver = new AccessoryMotionSolver(); var item = Chain(Item() with { GravityInfluence = 100, BaseAngleDegrees = 17 });
        var frame = solver.Update(0, 1d / 60, new(80, 80), 256, 256, item);
        for (var step = 1; step < 80; step++) frame = solver.Update(step, 1d / 60, new(80, 80), 256, 256, item);
        var texture = Texture(2048); var single = new AccessoryMeshVisual(); var valid = new AccessoryMeshVisual(); var invalid = new AccessoryMeshVisual();
        single.SetTexture(texture); valid.SetTexture(texture); invalid.SetTexture(texture);
        single.ApplyFrame(frame, new DpiScale(1.5, 1.5));
        var reversed = frame.TriangleIndices.ToArray(); (reversed[0], reversed[1]) = (reversed[1], reversed[0]);
        var result = AccessoryMeshVisual.ApplyBatch([(invalid, frame with { TriangleIndices = Array.AsReadOnly(reversed), AllowFolding = false }), (valid, frame)], new DpiScale(1.5, 1.5));
        Assert.HasCount(1, result.Errors); Assert.AreSame(invalid, result.Errors[0].Visual);
        Assert.IsInstanceOfType<ArgumentException>(result.Errors[0].Error); Assert.IsNull(invalid.Frame);
        CollectionAssert.AreEqual(Pixels(single.CaptureBitmap()!), Pixels(valid.CaptureBitmap()!), "Batch scheduling cannot change texture quality.");
    }

    [STATestMethod]
    public void TransparentAndSemitransparentTextureHasNoTriangleSeamsAndPixelsArePremultiplied()
    {
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture());
        var solver = new AccessoryMotionSolver();
        var item = Item();
        var frame = solver.Update(0, 1d / 60, new(64, 64), 64, 64, item);
        visual.ApplyFrame(frame);
        var bitmap = visual.CaptureBitmap()!;
        var data = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(data, bitmap.PixelWidth * 4, 0);
        Assert.AreEqual(0u, (uint)data[0] >> 24);
        // Source has a uniform alpha-128 inner square. Every diagonal/grid-edge pixel must retain it.
        for (var y = 10; y <= 55; y++)
            for (var x = 10; x <= 55; x++)
            {
                var pixel = (uint)data[y * bitmap.PixelWidth + x];
                Assert.AreEqual(128u, pixel >> 24, $"Seam at {x},{y}");
                Assert.IsTrue((pixel & 255) <= 128 && ((pixel >> 8) & 255) <= 128 && ((pixel >> 16) & 255) <= 128);
            }
        Assert.IsFalse(visual.IsHitTestVisible); Assert.IsFalse(visual.ClipToBounds);
    }

    [STATestMethod]
    public void BentRotatedSemitransparentMaterialHasNoInteriorGridHoles()
    {
        var solver = new AccessoryMotionSolver(); var item = Chain(Item() with
        { GravityInfluence = 100, ImagePivotX = 0.37, ImagePivotY = 0.41, BaseAngleDegrees = 37 });
        var frame = solver.Update(0, 1d / 60, new(180, 180), 256, 256, item);
        for (var step = 1; step < 90; step++) frame = solver.Update(step, 1d / 60, new(180, 180), 256, 256, item);
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture()); visual.ApplyFrame(frame);
        var bitmap = visual.CaptureBitmap()!; var pixels = Pixels(bitmap);
        for (var triangle = 0; triangle < frame.TriangleIndices.Count; triangle += 3)
            for (var edge = 0; edge < 3; edge++)
            {
                var a = frame.TriangleIndices[triangle + edge]; var b = frame.TriangleIndices[triangle + (edge + 1) % 3];
                var uv = new System.Windows.Point((frame.TextureCoordinates[a].X + frame.TextureCoordinates[b].X) / 2,
                    (frame.TextureCoordinates[a].Y + frame.TextureCoordinates[b].Y) / 2);
                if (uv.X is < 0.2 or > 0.8 || uv.Y is < 0.2 or > 0.8) continue;
                var point = new System.Windows.Point((frame.Vertices[a].X + frame.Vertices[b].X) / 2,
                    (frame.Vertices[a].Y + frame.Vertices[b].Y) / 2);
                var x = (int)Math.Floor((point.X - visual.DrawingBounds.Left) * bitmap.DpiX / 96);
                var y = (int)Math.Floor((point.Y - visual.DrawingBounds.Top) * bitmap.DpiY / 96);
                Assert.AreEqual(128u, (uint)pixels[y * bitmap.PixelWidth + x] >> 24, $"Bent mesh edge at {x},{y} has an alpha seam.");
            }
    }

    [STATestMethod]
    public void LegalPivotInfinitesimallyCloseToARegularGridEdgeStillRenders()
    {
        var solver = new AccessoryMotionSolver(); var item = Chain(Item() with { ImagePivotX = 1e-14, ImagePivotY = 0.25000000000001 });
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture());
        visual.ApplyFrame(solver.Update(0, 1d / 60, new(80, 80), 64, 64, item));
        Assert.IsTrue(Pixels(visual.CaptureBitmap()!).Any(p => (uint)p >> 24 == 128));
    }

    [STATestMethod]
    public void SoftTextureRendersAsAChangedImageAndTranslatedHostConsumesTheSameMeshWithoutStepping()
    {
        var solver = new AccessoryMotionSolver(); var item = Chain(Item() with { GravityInfluence = 100, ImagePivotY = 0 });
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture());
        var rest = solver.Update(0, 1d / 60, new(90, 20), 64, 64, item);
        visual.ApplyFrame(rest); var first = Pixels(visual.CaptureBitmap()!);
        var frame = rest;
        for (var i = 1; i < 120; i++) frame = solver.Update(i, 1d / 60, new(90, 20), 64, 64, item);
        visual.ApplyFrame(frame); var changed = Pixels(visual.CaptureBitmap()!);
        Assert.IsFalse(first.SequenceEqual(changed), "Texture interior must change under gravity.");
        var beforeStep = solver.Current!.StepIndex;
        var beforeDrawingSize = visual.DrawingBounds.Size;
        var moved = frame.Translated(new(10, 50)); visual.ApplyFrame(moved);
        Assert.AreEqual(beforeStep, solver.Current.StepIndex);
        CollectionAssert.AreEqual(changed, Pixels(visual.CaptureBitmap()!));
        Assert.AreEqual(beforeDrawingSize, visual.DrawingBounds.Size, "Cached capacity pixels cannot be squeezed into raw mesh bounds.");
        Assert.AreEqual(frame.ImagePivot + new Vector(10, 50), moved.ImagePivot);
        SaveReviewImage(visual, "soft-transparent-texture.png");
    }

    private static AccessoryItem Item() => new("mesh-test", "Synthetic texture", "builtin-sprout", 64, 64)
    { ImagePivotX = 0.5, ImagePivotY = 0.5, ConnectionStrength = 100, LockRotation = true };

    private static AccessoryItem Chain(AccessoryItem item) => item with
    {
        DeformationMode = AccessoryDeformationMode.SingleChain,
        Rig = new([new(item.ImagePivotX, item.ImagePivotY), new(.7, .6), new(.8, .85)], .8, 3, 2),
    };

    private static BitmapSource Texture(int edge = 64, int? height = null)
    {
        var pixelHeight = height ?? edge;
        var data = new byte[edge * pixelHeight * 4];
        for (var y = pixelHeight / 16; y < pixelHeight * 15 / 16; y++)
            for (var x = edge / 16; x < edge * 15 / 16; x++)
            {
                var index = (y * edge + x) * 4; var checker = (x / (edge / 8) + y / (edge / 8)) % 2 == 0;
                data[index] = checker ? (byte)220 : (byte)40; data[index + 1] = 120;
                data[index + 2] = checker ? (byte)40 : (byte)220; data[index + 3] = 128;
            }
        var bitmap = BitmapSource.Create(edge, pixelHeight, 96, 96, PixelFormats.Bgra32, null, data, edge * 4); bitmap.Freeze(); return bitmap;
    }

    private static int[] Pixels(BitmapSource bitmap)
    { var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0); return pixels; }

    private void SaveReviewImage(AccessoryMeshVisual visual, string name)
    {
        var host = new System.Windows.Controls.Canvas { Width = 220, Height = 220, Background = System.Windows.Media.Brushes.White };
        host.Children.Add(visual); host.Measure(new(220, 220)); host.Arrange(new(0, 0, 220, 220)); host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(220, 220, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
        SaveBitmap(bitmap, name);
    }

    private void SaveBitmap(BitmapSource bitmap, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "evidence", "accessory-a0"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream); TestContext.WriteLine(path);
    }
}
