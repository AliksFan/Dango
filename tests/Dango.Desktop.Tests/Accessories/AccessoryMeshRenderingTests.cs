using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dango.Desktop.Accessories;

namespace Dango.Desktop.Tests.Accessories;

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
        var invalid = frame with { TriangleIndices = Array.AsReadOnly(reversed) };
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
    [TestCategory("Performance")]
    public void ActualVisibleWindowRendersEightFlexibleAccessoriesWithMeasuredDpiAndFrameCadence()
    {
        var texture = Texture(2048);
        var solvers = Enumerable.Range(0, 8).Select(_ => new AccessoryMotionSolver()).ToArray();
        var visuals = Enumerable.Range(0, 8).Select(_ => new AccessoryMeshVisual()).ToArray();
        var canvas = new System.Windows.Controls.Canvas { Background = System.Windows.Media.Brushes.White, ClipToBounds = false };
        foreach (var visual in visuals) { visual.SetTexture(texture); canvas.Children.Add(visual); }
        var work = SystemParameters.WorkArea;
        var window = new Window
        {
            Title = "DIY 配饰 A0 — 实际 WPF 8 件柔性纹理",
            Width = Math.Max(320, Math.Min(1100, work.Width - 40)),
            Height = Math.Max(320, Math.Min(620, work.Height - 60)),
            Left = work.Left + 20,
            Top = work.Top + 20,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Content = canvas,
        };
        var item = Item() with
        {
            Softness = 100,
            ConnectionStrength = 10,
            GravityInfluence = 100,
            LockRotation = false,
            ReturnStrength = 80,
            RotationDamping = 70,
            ImagePivotX = 0.4,
            ImagePivotY = 0.4
        };
        var pump = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(15) };
        var costs = new List<double>(); var intervals = new List<double>();
        var rasterCosts = new List<double>(); var uploadCosts = new List<double>();
        TimeSpan? previousTime = null; TimeSpan firstTime = default;
        Exception? callbackError = null; var step = 0; var longIntervals = 0;
        void Render(object? sender, EventArgs args)
        {
            try
            {
                var current = ((RenderingEventArgs)args).RenderingTime;
                if (previousTime == current) return;
                if (previousTime is null) firstTime = current;
                var interval = previousTime is { } previous ? (current - previous).TotalSeconds : 1d / 60;
                previousTime = current;
                if (interval > 0.1) longIntervals++;
                var seconds = Math.Clamp(interval, 1d / 1000, 0.1);
                var time = (current - firstTime).TotalSeconds;
                var marginX = Math.Min(225, Math.Max(20, canvas.ActualWidth / 2 - 10));
                var marginY = Math.Min(225, Math.Max(20, canvas.ActualHeight / 2 - 10));
                var watch = Stopwatch.StartNew();
                var requests = new (AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)[8];
                for (var i = 0; i < 8; i++)
                {
                    var x = marginX + i % 4 * Math.Max(0, canvas.ActualWidth - 2 * marginX) / 3;
                    var y = marginY + i / 4 * Math.Max(0, canvas.ActualHeight - 2 * marginY);
                    var frame = solvers[i].Update(step, seconds,
                        new(x + 24 * Math.Sin(time * 9 + i), y + 24 * Math.Cos(time * 11 + i)), 256, 256, item);
                    // No DPI override: this is the DPI of the visible window's real WPF surface.
                    requests[i] = (visuals[i], frame);
                }
                var batch = AccessoryMeshVisual.ApplyBatch(requests);
                Assert.HasCount(0, batch.Errors);
                watch.Stop();
                if (step >= 30)
                {
                    costs.Add(watch.Elapsed.TotalMilliseconds); intervals.Add(interval * 1000);
                    rasterCosts.Add(batch.RasterMilliseconds); uploadCosts.Add(batch.UploadMilliseconds);
                }
                step++;
                if (costs.Count >= 180) pump.Continue = false;
            }
            catch (Exception error) { callbackError = error; pump.Continue = false; }
        }
        void TimedOut(object? sender, EventArgs args) => pump.Continue = false;
        try
        {
            window.Show(); window.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(window);
            TestContext.WriteLine($"Actual visible WPF Window DPI: {dpi.DpiScaleX:P0} x {dpi.DpiScaleY:P0}; client {canvas.ActualWidth:F0} x {canvas.ActualHeight:F0} DIP; RenderCapability tier {RenderCapability.Tier >> 16}.");
            timeout.Tick += TimedOut; timeout.Start(); CompositionTarget.Rendering += Render;
            Dispatcher.PushFrame(pump);
            if (callbackError is not null) throw new AssertFailedException("Live WPF rendering callback failed.", callbackError);
            Assert.HasCount(180, costs, "Visible window did not complete its full measured rendering interval.");
            costs.Sort(); var sortedIntervals = intervals.Order().ToArray();
            var p95 = costs[(int)(costs.Count * 0.95)];
            var fps = 1000 / intervals.Average();
            TestContext.WriteLine($"Actual WPF 8 x 2048px source / 256 DIP dynamic flexible textures: solve+raster median {costs[costs.Count / 2]:F2} ms, p95 {p95:F2} ms, max {costs[^1]:F2} ms.");
            TestContext.WriteLine($"CompositionTarget.Rendering callback interval median {sortedIntervals[sortedIntervals.Length / 2]:F2} ms, p95 {sortedIntervals[(int)(sortedIntervals.Length * 0.95)]:F2} ms; measured FPS {fps:F2}; >100 ms gaps {longIntervals}.");
            var sortedRaster = rasterCosts.Order().ToArray(); var sortedUpload = uploadCosts.Order().ToArray();
            TestContext.WriteLine($"Live phase costs: raster median {sortedRaster[90]:F2} / p95 {sortedRaster[171]:F2} ms; WPF bitmap upload median {sortedUpload[90]:F2} / p95 {sortedUpload[171]:F2} ms; other (solver+frame validation) average {costs.Average() - rasterCosts.Average() - uploadCosts.Average():F2} ms.");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(canvas.ActualWidth * dpi.DpiScaleX),
                (int)Math.Ceiling(canvas.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(canvas);
            SaveBitmap(bitmap, "actual-visible-eight-accessories.png");
            Assert.IsTrue(p95 < 16, $"Actual visible-window solve/raster exceeds 16 ms at measured DPI: {p95:F2} ms.");
        }
        finally
        {
            CompositionTarget.Rendering -= Render; timeout.Tick -= TimedOut; timeout.Stop(); window.Close();
        }
    }

    [STATestMethod]
    public void BatchUsesTheSameCompleteSourcePixelsAndIsolatesOneReversedMesh()
    {
        var solver = new AccessoryMotionSolver(); var item = Item() with { Softness = 100, GravityInfluence = 100, BaseAngleDegrees = 17 };
        var frame = solver.Update(0, 1d / 60, new(80, 80), 256, 256, item);
        for (var step = 1; step < 80; step++) frame = solver.Update(step, 1d / 60, new(80, 80), 256, 256, item);
        var texture = Texture(2048); var single = new AccessoryMeshVisual(); var valid = new AccessoryMeshVisual(); var invalid = new AccessoryMeshVisual();
        single.SetTexture(texture); valid.SetTexture(texture); invalid.SetTexture(texture);
        single.ApplyFrame(frame, new DpiScale(1.5, 1.5));
        var reversed = frame.TriangleIndices.ToArray(); (reversed[0], reversed[1]) = (reversed[1], reversed[0]);
        var result = AccessoryMeshVisual.ApplyBatch([(invalid, frame with { TriangleIndices = Array.AsReadOnly(reversed) }), (valid, frame)], new DpiScale(1.5, 1.5));
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
        var solver = new AccessoryMotionSolver(); var item = Item() with
        { Softness = 100, GravityInfluence = 100, ImagePivotX = 0.37, ImagePivotY = 0.41, BaseAngleDegrees = 37 };
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
        var solver = new AccessoryMotionSolver(); var item = Item() with { ImagePivotX = 1e-14, ImagePivotY = 0.25000000000001, Softness = 100 };
        var visual = new AccessoryMeshVisual(); visual.SetTexture(Texture());
        visual.ApplyFrame(solver.Update(0, 1d / 60, new(80, 80), 64, 64, item));
        Assert.IsTrue(Pixels(visual.CaptureBitmap()!).Any(p => (uint)p >> 24 == 128));
    }

    [STATestMethod]
    public void SoftTextureRendersAsAChangedImageAndTranslatedHostConsumesTheSameMeshWithoutStepping()
    {
        var solver = new AccessoryMotionSolver(); var item = Item() with { Softness = 100, GravityInfluence = 100, ImagePivotY = 0 };
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

    [STATestMethod]
    [DataRow(1.0, 64)]
    [DataRow(1.5, 64)]
    [DataRow(1.5, 2048)]
    [DataRow(1.5, 4096)]
    [TestCategory("Performance")]
    public void EightLargeFlexibleTexturesFitTheMeasuredLocalRasterBudget(double pixelsPerDip, int sourceEdge)
    {
        var sourceHeight = Math.Min(sourceEdge, (int)(AccessoryImageAsset.MaximumPixels / sourceEdge));
        var texture = Texture(sourceEdge, sourceHeight); var solvers = Enumerable.Range(0, 8).Select(_ => new AccessoryMotionSolver()).ToArray();
        var visuals = Enumerable.Range(0, 8).Select(_ => new AccessoryMeshVisual()).ToArray();
        foreach (var visual in visuals) visual.SetTexture(texture);
        var item = Item() with
        {
            Softness = 100,
            ConnectionStrength = 10,
            GravityInfluence = 100,
            LockRotation = false,
            ReturnStrength = 80,
            RotationDamping = 70,
            ImagePivotX = 0.1,
            ImagePivotY = 0.1
        };
        var samples = new List<double>();
        var raster = new List<double>(); var upload = new List<double>();
        for (var step = 0; step < 160; step++)
        {
            var watch = Stopwatch.StartNew();
            var requests = new (AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)[8];
            for (var i = 0; i < 8; i++)
            {
                var time = step / 60d;
                var frame = solvers[i].Update(step, 1d / 60,
                    new(250 + 50 * Math.Sin(time * 7 + i), 250 + 40 * Math.Cos(time * 9 + i)), 256, 256, item);
                requests[i] = (visuals[i], frame);
            }
            var batch = AccessoryMeshVisual.ApplyBatch(requests, new DpiScale(pixelsPerDip, pixelsPerDip));
            Assert.HasCount(0, batch.Errors);
            watch.Stop(); if (step >= 20)
            { samples.Add(watch.Elapsed.TotalMilliseconds); raster.Add(batch.RasterMilliseconds); upload.Add(batch.UploadMilliseconds); }
        }
        samples.Sort(); var p95 = samples[(int)(samples.Count * 0.95)];
        TestContext.WriteLine($"8 x 256 DIP flexible mesh solve + raster, {sourceEdge} x {sourceHeight}px source at {pixelsPerDip:P0} DPI equivalent: median {samples[samples.Count / 2]:F2} ms; p95 {p95:F2} ms; max {samples[^1]:F2} ms; CPU workers <= {Math.Min(Environment.ProcessorCount, AccessoryMeshVisual.MaximumRasterWorkers)} per raster.");
        TestContext.WriteLine($"Average raster {raster.Average():F2} ms; bitmap upload {upload.Average():F2} ms.");
        Assert.IsTrue(p95 < 16, $"8-texture CPU solve/raster exceeds 16 ms: p95={p95:F2} ms; do not silently lower softness or density.");
        foreach (var visual in visuals) Assert.IsNotNull(visual.CaptureBitmap());
    }

    private static AccessoryItem Item() => new("mesh-test", "Synthetic texture", "builtin-sprout", 64, 64)
    { ImagePivotX = 0.5, ImagePivotY = 0.5, ConnectionStrength = 100, LockRotation = true };

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
