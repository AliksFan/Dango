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
    private sealed class TextureSnapshot(int[] pixels)
    {
        public readonly int[] Pixels = pixels;
    }
    // Frozen WPF sources are immutable. Weak identity keys share their exact converted pixels
    // across worn copies without keeping unused pictures alive or sharing mutable snapshots.
    private static readonly ConditionalWeakTable<BitmapSource, TextureSnapshot> FrozenTextures = new();
    private sealed class FoldCoverage
    {
        public readonly bool[] SharedEdges, OwnedEdges;
        public readonly int[] VertexOwners;

        public FoldCoverage(Point[] vertices, int[] indices)
        {
            SharedEdges = new bool[indices.Length]; OwnedEdges = new bool[indices.Length];
            VertexOwners = Enumerable.Repeat(int.MaxValue, vertices.Length).ToArray();
            var owners = new Dictionary<(int, int), (int Owner, int Count)>();
            for (var triangle = 0; triangle < indices.Length; triangle += 3)
            {
                var a = vertices[indices[triangle]]; var b = vertices[indices[triangle + 1]]; var c = vertices[indices[triangle + 2]];
                if (Math.Abs(Vector.CrossProduct(b - a, c - a)) <= 1e-12) continue;
                for (var edge = 0; edge < 3; edge++)
                {
                    var start = indices[triangle + edge]; var end = indices[triangle + (edge + 1) % 3];
                    VertexOwners[start] = Math.Min(VertexOwners[start], triangle);
                    var key = (Math.Min(start, end), Math.Max(start, end));
                    owners[key] = owners.TryGetValue(key, out var previous) ? (previous.Owner, previous.Count + 1) : (triangle, 1);
                }
            }
            for (var triangle = 0; triangle < indices.Length; triangle += 3)
                for (var edge = 0; edge < 3; edge++)
                {
                    var start = indices[triangle + edge]; var end = indices[triangle + (edge + 1) % 3];
                    var key = (Math.Min(start, end), Math.Max(start, end));
                    if (!owners.TryGetValue(key, out var owner)) continue;
                    SharedEdges[triangle + edge] = owner.Count > 1;
                    OwnedEdges[triangle + edge] = owner.Owner == triangle;
                }
        }
    }
    private sealed class RasterWork(AccessoryMeshVisual owner, AccessoryMeshFrame frame,
        int width, int height, int stride, Point[] rasterVertices, Point[] uvs, int[] indices)
    {
        public readonly AccessoryMeshVisual Owner = owner;
        public readonly AccessoryMeshFrame Frame = frame;
        public readonly int Width = width, Height = height, Stride = stride;
        public readonly Point[] RasterVertices = rasterVertices, Uvs = uvs;
        public readonly int[] Indices = indices;
        public readonly int[] Texture = owner.sourcePixels, Destination = owner.pixels;
        public readonly int TextureWidth = owner.sourceWidth, TextureHeight = owner.sourceHeight;
        public readonly FoldCoverage? Coverage = frame.AllowFolding ? new(rasterVertices, indices) : null;
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
    private bool lastAllowFolding;
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
        var width = texture.PixelWidth; var height = texture.PixelHeight;
        AccessoryImageAsset.ValidateDimensions(width, height);
        var snapshot = texture.IsFrozen ? FrozenTextures.GetValue(texture, ReadTexturePixels) : ReadTexturePixels(texture);
        source = texture; sourceWidth = width; sourceHeight = height; sourcePixels = snapshot.Pixels;
        lastRasterVertices = [];
        if (frame is not null) Rasterize(frame);
        InvalidateVisual();
    }

    private static TextureSnapshot ReadTexturePixels(BitmapSource texture)
    {
        var converted = texture.Format == PixelFormats.Pbgra32 ? texture : new FormatConvertedBitmap(texture, PixelFormats.Pbgra32, null, 0);
        var pixels = new int[checked(texture.PixelWidth * texture.PixelHeight)];
        converted.CopyPixels(pixels, texture.PixelWidth * 4, 0);
        return new(pixels);
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
            lastAllowFolding == value.AllowFolding &&
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
        // Preserve the exact original subtraction then scaling; deriving these from the
        // translation-cache vertices would round differently. Work owns every snapshot.
        var rasterVertices = new Point[value.Vertices.Count];
        for (var index = 0; index < rasterVertices.Length; index++)
            rasterVertices[index] = new((value.Vertices[index].X - drawingBounds.Left) * scaleX,
                (value.Vertices[index].Y - drawingBounds.Top) * scaleY);
        var uvs = value.TextureCoordinates.ToArray(); var indices = value.TriangleIndices.ToArray();
        lastRasterVertices = value.Vertices.Select(p => new Point(p.X - value.Bounds.Left, p.Y - value.Bounds.Top)).ToArray();
        lastScaleX = scaleX; lastScaleY = scaleY; lastRasterWidth = width; lastRasterHeight = height;
        lastAllowFolding = value.AllowFolding;
        lastUvs = value.TextureCoordinates; lastIndices = value.TriangleIndices;
        return new(this, value, width, height, bitmap.PixelWidth, rasterVertices, uvs, indices);
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

    // This bounded pixel kernel must start with optimized code, rather than repeat a profiled
    // Tier0 entry and depend on loop OSR before its existing sampling inline hints take effect.
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private void RasterBand(RasterWork work, int band, int workers)
    {
        var value = work.Frame; var width = work.Width; var height = work.Height; var stride = work.Stride;
        // Batch preparation owns these arrays until all workers return. Capture them once so
        // the inlined pixel loop does not reload texture dimensions and array fields per tap.
        var texture = work.Texture; var textureWidth = work.TextureWidth; var textureHeight = work.TextureHeight;
        var destination = work.Destination; var vertices = work.RasterVertices; var uvs = work.Uvs; var indices = work.Indices;
        var bandTop = band * height / workers; var bandBottom = (band + 1) * height / workers - 1;
        for (var triangle = 0; triangle < indices.Length; triangle += 3)
        {
            var ia = indices[triangle]; var ib = indices[triangle + 1]; var ic = indices[triangle + 2];
            var a = vertices[ia]; var b = vertices[ib]; var c = vertices[ic];
            var ua = uvs[ia]; var ub = uvs[ib]; var uc = uvs[ic];
            var e1 = b - a; var e2 = c - a; var determinant = Vector.CrossProduct(e1, e2);
            if (determinant < -1e-12 && !value.AllowFolding) throw new ArgumentException("配饰纹理网格翻折。", nameof(value));
            if (Math.Abs(determinant) <= 1e-12) continue;
            var top = Math.Max(bandTop, (int)Math.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 0.5));
            var bottom = Math.Min(bandBottom, (int)Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - 0.5));
            if (top > bottom) continue;
            var bx = e2.Y / determinant; var by = -e2.X / determinant; var cx = -e1.Y / determinant; var cy = e1.X / determinant;
            var uStep = (bx * (ub.X - ua.X) + cx * (uc.X - ua.X)) * textureWidth;
            var vStep = (bx * (ub.Y - ua.Y) + cx * (uc.Y - ua.Y)) * textureHeight;
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
                var u = (ua.X + wb * (ub.X - ua.X) + wc * (uc.X - ua.X)) * textureWidth - 0.5;
                var v = (ua.Y + wb * (ub.Y - ua.Y) + wc * (uc.Y - ua.Y)) * textureHeight - 0.5;
                var index = y * stride + left;
                if (!value.AllowFolding)
                {
                    for (var x = left; x <= right; x++, index++, u += uStep, v += vStep)
                        destination[index] = SamplePixels(texture, textureWidth, textureHeight, u, v);
                }
                else
                {
                    // Each material face is drawn once at shared edges. Actual overlaps are
                    // composited in stable mesh order; transparent folds reveal the earlier face.
                    for (var x = left; x <= right; x++, index++, u += uStep, v += vStep)
                        if (CoversFoldPixel(work.Coverage!, triangle, ia, ib, ic, a, b, c, determinant, new(x + .5, scan)))
                            destination[index] = CompositeOver(SamplePixels(texture, textureWidth, textureHeight, u, v), destination[index]);
                }
            }
        }
    }

    private static bool CoversFoldPixel(FoldCoverage coverage, int triangle, int ia, int ib, int ic,
        Point a, Point b, Point c, double determinant, Point point)
    {
        // A folded pair can lie on the same side of its shared edge. World winding
        // alone cannot assign that edge exactly once; use original material identity.
        if ((point - a).LengthSquared <= 1e-16) return coverage.VertexOwners[ia] == triangle;
        if ((point - b).LengthSquared <= 1e-16) return coverage.VertexOwners[ib] == triangle;
        if ((point - c).LengthSquared <= 1e-16) return coverage.VertexOwners[ic] == triangle;
        var orientation = determinant > 0 ? 1 : -1;
        return OwnsEdge(a, b, point, orientation, coverage.SharedEdges[triangle], coverage.OwnedEdges[triangle]) &&
            OwnsEdge(b, c, point, orientation, coverage.SharedEdges[triangle + 1], coverage.OwnedEdges[triangle + 1]) &&
            OwnsEdge(c, a, point, orientation, coverage.SharedEdges[triangle + 2], coverage.OwnedEdges[triangle + 2]);
    }

    private static bool OwnsEdge(Point a, Point b, Point point, int orientation, bool shared, bool owned)
    {
        var edge = b - a;
        var cross = Vector.CrossProduct(edge, point - a) * orientation;
        if (cross > 1e-8) return true;
        if (cross < -1e-8) return false;
        if (shared) return owned;
        edge *= orientation;
        return edge.Y < 0 || edge.Y == 0 && edge.X > 0;
    }

    private static int CompositeOver(int foreground, int background)
    {
        var source = (uint)foreground; var destination = (uint)background;
        var inverseAlpha = 255 - (source >> 24);
        if (inverseAlpha == 255) return background;
        if (inverseAlpha == 0) return foreground;
        uint output = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var channel = ((source >> shift) & 255) + (((destination >> shift) & 255) * inverseAlpha + 127) / 255;
            output |= channel << shift;
        }
        return (int)output;
    }

    private static int Capacity(int required)
    { var capacity = 32; while (capacity < required) capacity *= 2; return capacity; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Sample(double u, double v) => SamplePixels(sourcePixels, sourceWidth, sourceHeight, u, v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SamplePixels(int[] sourcePixels, int sourceWidth, int sourceHeight, double u, double v)
    {
        u = Math.Clamp(u, 0, sourceWidth - 1d); v = Math.Clamp(v, 0, sourceHeight - 1d);
        var x = (int)u; var y = (int)v;
        var index = y * sourceWidth + x;
        var right = x + 1 < sourceWidth ? 1 : 0; var bottom = y + 1 < sourceHeight ? sourceWidth : 0;
        var p0 = (uint)sourcePixels[index]; var p1 = (uint)sourcePixels[index + right];
        var p2 = (uint)sourcePixels[index + bottom]; var p3 = (uint)sourcePixels[index + bottom + right];
        // Every rounded interpolation of equal channels is the original pixel, including alpha.
        if (p0 == p1 && p0 == p2 && p0 == p3) return (int)p0;
        var fx = (int)((u - x) * 256); var fy = (int)((v - y) * 256);
        return (int)PackedLerp(PackedLerp(p0, p1, (uint)fx), PackedLerp(p2, p3, (uint)fx), (uint)fy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackedLerp(uint a, uint b, uint fraction)
    {
        const uint mask = 0x00ff00ff;
        var ar = a & mask; var br = b & mask;
        var ag = (a >> 8) & mask; var bg = (b >> 8) & mask;
        // A*(256-f)+B*f is (A<<8)+(B-A)*f in the uint ring, even when B<A.
        // Keep both horizontal rounding steps and the later vertical rounding unchanged.
        var blueRed = unchecked((ar << 8) + (br - ar) * fraction + 0x00800080);
        var greenAlpha = unchecked((ag << 8) + (bg - ag) * fraction + 0x00800080);
        return ((blueRed >> 8) & mask) | (((greenAlpha >> 8) & mask) << 8);
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
