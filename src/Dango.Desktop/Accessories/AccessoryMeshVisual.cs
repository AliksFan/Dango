using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories;

public sealed record AccessoryBatchRenderResult(
    IReadOnlyList<(AccessoryMeshVisual Visual, Exception Error)> Errors,
    double PreparationMilliseconds, double RasterMilliseconds, double UploadMilliseconds);

/// <summary>
/// Consumes immutable attachment meshes and samples the complete original texture into one
/// premultiplied-alpha bitmap. Triangle edges write pixels instead of compositing clips.
/// This element never advances attachment or body motion.
/// </summary>
public sealed class AccessoryMeshVisual : FrameworkElement
{
    public const int MaximumRasterWorkers = 8;
    private sealed class RasterWork(AccessoryMeshVisual owner, AccessoryMeshFrame frame,
        int width, int height, double scaleX, double scaleY, int stride)
    {
        public readonly AccessoryMeshVisual Owner = owner;
        public readonly AccessoryMeshFrame Frame = frame;
        public readonly int Width = width, Height = height, Stride = stride;
        public readonly double ScaleX = scaleX, ScaleY = scaleY;
        public Exception? Error;
    }
    private BitmapSource? source;
    private int[] sourcePixels = [];
    private int sourceWidth, sourceHeight;
    private WriteableBitmap? bitmap;
    private int[] pixels = [];
    private Point[] lastRasterVertices = [];
    private double lastScaleX, lastScaleY;
    private int lastRasterWidth, lastRasterHeight;
    private int uploadedWidth, uploadedHeight;
    private IReadOnlyList<Point>? lastUvs;
    private IReadOnlyList<int>? lastIndices;
    private AccessoryMeshFrame? frame;
    private Rect drawingBounds;
    private DpiScale? rasterDpiOverride;

    public AccessoryMeshVisual()
    {
        Width = 160; Height = 120; IsHitTestVisible = false; ClipToBounds = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
    }
    public AccessoryMeshFrame? Frame => frame;
    public Rect DrawingBounds => drawingBounds;
    public double LastRasterMilliseconds { get; private set; }
    public double LastUploadMilliseconds { get; private set; }

    public void SetTexture(BitmapSource texture)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(texture);
        if (ReferenceEquals(source, texture)) return;
        AccessoryImageAsset.ValidateDimensions(texture.PixelWidth, texture.PixelHeight);
        source = texture; sourceWidth = texture.PixelWidth; sourceHeight = texture.PixelHeight;
        var converted = texture.Format == PixelFormats.Pbgra32 ? texture : new FormatConvertedBitmap(texture, PixelFormats.Pbgra32, null, 0);
        sourcePixels = new int[checked(sourceWidth * sourceHeight)];
        converted.CopyPixels(sourcePixels, sourceWidth * 4, 0);
        lastRasterVertices = [];
        if (frame is not null) Rasterize(frame);
        InvalidateVisual();
    }

    public void ApplyFrame(AccessoryMeshFrame value, DpiScale? rasterDpi = null)
    {
        AssignFrame(value, rasterDpi);
        if (source is not null) Rasterize(value);
        InvalidateVisual();
    }

    /// <summary>
    /// Prepares/uploads on the visual owner thread; calculates independent complete-source textures
    /// in one bounded parallel batch. Each ordinary accessory fault is isolated and returned.
    /// </summary>
    public static AccessoryBatchRenderResult ApplyBatch(
        IReadOnlyList<(AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)> requests, DpiScale? rasterDpi = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count > 8) throw new ArgumentOutOfRangeException(nameof(requests));
        var failures = new List<(AccessoryMeshVisual Visual, Exception Error)>();
        var work = new List<RasterWork>(); var unique = new HashSet<AccessoryMeshVisual>();
        var prepareStart = Stopwatch.GetTimestamp();
        foreach (var (visual, value) in requests)
        {
            ArgumentNullException.ThrowIfNull(visual); visual.VerifyAccess();
            if (!unique.Add(visual)) throw new ArgumentException("配饰批量画面不能重复包含同一 Visual。", nameof(requests));
            try
            {
                visual.AssignFrame(value, rasterDpi);
                if (visual.source is not null && visual.PrepareRaster(value) is { } pending) work.Add(pending);
            }
            catch (Exception error) when (IsIsolatedFault(error))
            { visual.ClearFrame(); failures.Add((visual, error)); }
        }
        var prepareMs = Stopwatch.GetElapsedTime(prepareStart).TotalMilliseconds;
        var rasterStart = Stopwatch.GetTimestamp();
        void Calculate(RasterWork pending)
        {
            var start = Stopwatch.GetTimestamp();
            try { pending.Owner.RasterBand(pending, 0, 1); }
            catch (Exception error) when (IsIsolatedFault(error)) { pending.Error = error; }
            pending.Owner.LastRasterMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        if (work.Count == 1) Calculate(work[0]);
        else if (work.Count > 1)
            Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, MaximumRasterWorkers) }, Calculate);
        var rasterMs = Stopwatch.GetElapsedTime(rasterStart).TotalMilliseconds;
        var uploadStart = Stopwatch.GetTimestamp();
        foreach (var pending in work)
        {
            var visual = pending.Owner;
            if (pending.Error is { } fault)
            { visual.ClearFrame(); failures.Add((visual, fault)); continue; }
            try { visual.Upload(); }
            catch (Exception error) when (IsIsolatedFault(error))
            { visual.ClearFrame(); failures.Add((visual, error)); }
        }
        foreach (var (visual, _) in requests) visual.InvalidateVisual();
        return new(failures.AsReadOnly(), prepareMs, rasterMs, Stopwatch.GetElapsedTime(uploadStart).TotalMilliseconds);
    }

    public void ClearFrame()
    { VerifyAccess(); frame = null; lastRasterVertices = []; InvalidateVisual(); }

    public BitmapSource? CaptureBitmap()
    {
        VerifyAccess();
        if (bitmap is null) return null;
        var copy = bitmap.Clone(); copy.Freeze(); return copy;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (frame is not null && bitmap is not null) drawingContext.DrawImage(bitmap, drawingBounds);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        lastRasterVertices = [];
        if (frame is not null && source is not null) Rasterize(frame);
        InvalidateVisual();
    }

    private void AssignFrame(AccessoryMeshFrame value, DpiScale? rasterDpi)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(value); ValidateFrame(value);
        if (rasterDpi is { } requested && (!double.IsFinite(requested.DpiScaleX) || !double.IsFinite(requested.DpiScaleY) ||
            requested.DpiScaleX is < 1 or > 4 || requested.DpiScaleY is < 1 or > 4))
            throw new ArgumentOutOfRangeException(nameof(rasterDpi));
        rasterDpiOverride = rasterDpi; frame = value;
    }

    private RasterWork? PrepareRaster(AccessoryMeshFrame value)
    {
        var dpi = rasterDpiOverride ?? VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX; var scaleY = dpi.DpiScaleY;
        var width = checked((int)Math.Ceiling(value.Bounds.Width * scaleX) + 2);
        var height = checked((int)Math.Ceiling(value.Bounds.Height * scaleY) + 2);
        if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(value), "配饰显示纹理超出资源边界。");
        drawingBounds = new(value.Bounds.Left - 1 / scaleX, value.Bounds.Top - 1 / scaleY, width / scaleX, height / scaleY);
        if (bitmap is not null && lastScaleX == scaleX && lastScaleY == scaleY)
        { drawingBounds.Width = bitmap.PixelWidth / scaleX; drawingBounds.Height = bitmap.PixelHeight / scaleY; }
        if (bitmap is not null && lastRasterWidth == width && lastRasterHeight == height &&
            lastScaleX == scaleX && lastScaleY == scaleY && lastRasterVertices.Length == value.Vertices.Count &&
            ReferenceEquals(lastUvs, value.TextureCoordinates) && ReferenceEquals(lastIndices, value.TriangleIndices) &&
            Enumerable.Range(0, value.Vertices.Count).All(i =>
                Math.Abs(lastRasterVertices[i].X - (value.Vertices[i].X - value.Bounds.Left)) < 1e-7 &&
                Math.Abs(lastRasterVertices[i].Y - (value.Vertices[i].Y - value.Bounds.Top)) < 1e-7))
        { LastRasterMilliseconds = LastUploadMilliseconds = 0; return null; }
        if (bitmap is null || bitmap.PixelWidth < width || bitmap.PixelHeight < height || lastScaleX != scaleX || lastScaleY != scaleY)
        {
            var capacityWidth = Capacity(width); var capacityHeight = Capacity(height);
            bitmap = new(capacityWidth, capacityHeight, 96 * scaleX, 96 * scaleY, PixelFormats.Pbgra32, null);
            pixels = new int[checked(capacityWidth * capacityHeight)];
            uploadedWidth = uploadedHeight = 0;
        }
        else
        {
            var clearWidth = Math.Max(width, uploadedWidth); var clearHeight = Math.Max(height, uploadedHeight);
            for (var y = 0; y < clearHeight; y++) Array.Clear(pixels, y * bitmap.PixelWidth, clearWidth);
        }
        drawingBounds.Width = bitmap.PixelWidth / scaleX; drawingBounds.Height = bitmap.PixelHeight / scaleY;
        lastRasterVertices = value.Vertices.Select(p => new Point(p.X - value.Bounds.Left, p.Y - value.Bounds.Top)).ToArray();
        lastScaleX = scaleX; lastScaleY = scaleY; lastRasterWidth = width; lastRasterHeight = height;
        lastUvs = value.TextureCoordinates; lastIndices = value.TriangleIndices;
        return new(this, value, width, height, scaleX, scaleY, bitmap.PixelWidth);
    }

    private void Rasterize(AccessoryMeshFrame value)
    {
        var start = Stopwatch.GetTimestamp();
        var pending = PrepareRaster(value);
        if (pending is null) return;
        try
        {
            var workers = pending.Width * pending.Height >= 40_000 ? Math.Min(Environment.ProcessorCount, MaximumRasterWorkers) : 1;
            if (workers == 1) RasterBand(pending, 0, 1);
            else
            {
                try { Parallel.For(0, workers, band => RasterBand(pending, band, workers)); }
                catch (AggregateException error) { ExceptionDispatchInfo.Capture(error.Flatten().InnerExceptions[0]).Throw(); }
            }
            LastRasterMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Upload();
        }
        catch
        {
            // Prepared geometry becomes a valid translation cache only after a successful upload.
            lastRasterVertices = [];
            throw;
        }
    }

    private void Upload()
    {
        var start = Stopwatch.GetTimestamp();
        // Update the union of new and old occupied pixels. Old material is cleared, while untouched
        // transparent capacity does not consume compositor upload bandwidth on every dynamic frame.
        var dirtyWidth = Math.Max(lastRasterWidth, uploadedWidth); var dirtyHeight = Math.Max(lastRasterHeight, uploadedHeight);
        bitmap!.WritePixels(new Int32Rect(0, 0, dirtyWidth, dirtyHeight), pixels, bitmap.PixelWidth * 4, 0);
        uploadedWidth = lastRasterWidth; uploadedHeight = lastRasterHeight;
        LastUploadMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void RasterBand(RasterWork work, int band, int workers)
    {
        var value = work.Frame; var width = work.Width; var height = work.Height; var stride = work.Stride;
        var bandTop = band * height / workers; var bandBottom = (band + 1) * height / workers - 1;
        for (var triangle = 0; triangle < value.TriangleIndices.Count; triangle += 3)
        {
            var ia = value.TriangleIndices[triangle]; var ib = value.TriangleIndices[triangle + 1]; var ic = value.TriangleIndices[triangle + 2];
            var a = RasterPoint(ia); var b = RasterPoint(ib); var c = RasterPoint(ic);
            var ua = value.TextureCoordinates[ia]; var ub = value.TextureCoordinates[ib]; var uc = value.TextureCoordinates[ic];
            var e1 = b - a; var e2 = c - a; var determinant = Vector.CrossProduct(e1, e2);
            if (determinant < -1e-12) throw new ArgumentException("配饰纹理网格翻折。", nameof(value));
            if (determinant <= 1e-12) continue;
            var top = Math.Max(bandTop, (int)Math.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 0.5));
            var bottom = Math.Min(bandBottom, (int)Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - 0.5));
            if (top > bottom) continue;
            var bx = e2.Y / determinant; var by = -e2.X / determinant; var cx = -e1.Y / determinant; var cy = e1.X / determinant;
            var uStep = (bx * (ub.X - ua.X) + cx * (uc.X - ua.X)) * sourceWidth;
            var vStep = (bx * (ub.Y - ua.Y) + cx * (uc.Y - ua.Y)) * sourceHeight;
            var low = a; var middle = b; var high = c;
            if (low.Y > middle.Y) (low, middle) = (middle, low);
            if (middle.Y > high.Y) (middle, high) = (high, middle);
            if (low.Y > middle.Y) (low, middle) = (middle, low);
            var longSlope = (high.X - low.X) / (high.Y - low.Y);
            var upperSlope = middle.Y > low.Y ? (middle.X - low.X) / (middle.Y - low.Y) : 0;
            var lowerSlope = high.Y > middle.Y ? (high.X - middle.X) / (high.Y - middle.Y) : 0;
            for (var y = top; y <= bottom; y++)
            {
                var scan = y + 0.5;
                var edgeA = low.X + (scan - low.Y) * longSlope;
                var edgeB = scan < middle.Y ? low.X + (scan - low.Y) * upperSlope : middle.X + (scan - middle.Y) * lowerSlope;
                var left = Math.Max(0, (int)Math.Ceiling(Math.Min(edgeA, edgeB) - 0.5 - 1e-8));
                var right = Math.Min(width - 1, (int)Math.Floor(Math.Max(edgeA, edgeB) - 0.5 + 1e-8));
                var wb = (left + 0.5 - a.X) * bx + (y + 0.5 - a.Y) * by;
                var wc = (left + 0.5 - a.X) * cx + (y + 0.5 - a.Y) * cy;
                var u = (ua.X + wb * (ub.X - ua.X) + wc * (uc.X - ua.X)) * sourceWidth - 0.5;
                var v = (ua.Y + wb * (ub.Y - ua.Y) + wc * (uc.Y - ua.Y)) * sourceHeight - 0.5;
                var index = y * stride + left;
                for (var x = left; x <= right; x++, index++, u += uStep, v += vStep) pixels[index] = Sample(u, v);
            }
        }
        Point RasterPoint(int index) => new((value.Vertices[index].X - drawingBounds.Left) * work.ScaleX,
            (value.Vertices[index].Y - drawingBounds.Top) * work.ScaleY);
    }

    private static int Capacity(int required)
    { var capacity = 32; while (capacity < required) capacity *= 2; return capacity; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Sample(double u, double v)
    {
        u = Math.Clamp(u, 0, sourceWidth - 1d); v = Math.Clamp(v, 0, sourceHeight - 1d);
        var x = (int)u; var y = (int)v;
        var fx = (int)((u - x) * 256); var fy = (int)((v - y) * 256);
        var index = y * sourceWidth + x;
        var right = x + 1 < sourceWidth ? 1 : 0; var bottom = y + 1 < sourceHeight ? sourceWidth : 0;
        var p0 = (uint)sourcePixels[index]; var p1 = (uint)sourcePixels[index + right];
        var p2 = (uint)sourcePixels[index + bottom]; var p3 = (uint)sourcePixels[index + bottom + right];
        return (int)PackedLerp(PackedLerp(p0, p1, (uint)fx), PackedLerp(p2, p3, (uint)fx), (uint)fy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackedLerp(uint a, uint b, uint fraction)
    {
        const uint mask = 0x00ff00ff; var inverse = 256 - fraction;
        var blueRed = (((a & mask) * inverse + (b & mask) * fraction + 0x00800080) >> 8) & mask;
        var greenAlpha = ((((a >> 8) & mask) * inverse + ((b >> 8) & mask) * fraction + 0x00800080) >> 8) & mask;
        return blueRed | (greenAlpha << 8);
    }

    private static bool IsIsolatedFault(Exception error) => error is ArgumentException or InvalidOperationException or
        ArithmeticException or System.Runtime.InteropServices.ExternalException;

    private static void ValidateFrame(AccessoryMeshFrame value)
    {
        if (value.Vertices.Count is < 3 or > 4096 || value.TextureCoordinates.Count != value.Vertices.Count ||
            value.TriangleIndices.Count is < 3 or > 24576 || value.TriangleIndices.Count % 3 != 0 ||
            value.Bounds.IsEmpty || !double.IsFinite(value.Bounds.X) || !double.IsFinite(value.Bounds.Y) ||
            !double.IsFinite(value.Bounds.Width) || !double.IsFinite(value.Bounds.Height) || value.Bounds.Width <= 0 || value.Bounds.Height <= 0 ||
            value.Vertices.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)) ||
            value.TextureCoordinates.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X is < 0 or > 1 || p.Y is < 0 or > 1) ||
            value.TriangleIndices.Any(i => i < 0 || i >= value.Vertices.Count))
            throw new ArgumentException("配饰纹理网格无效。", nameof(value));
    }
}
