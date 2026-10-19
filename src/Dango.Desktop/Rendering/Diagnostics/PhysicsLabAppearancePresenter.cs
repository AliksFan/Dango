using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace Dango.Desktop.Rendering.Diagnostics;

public readonly record struct PhysicsLabBoundaryPoint(double X, double Y);

public sealed record PhysicsLabGaitContext(double DisplayScale, double Left, double Top,
    double ViewportWidth, double ViewportHeight);

public sealed record PhysicsLabGaitState(bool Enabled, long Tick, double Phase, double RequestedStrain,
    double DisplayedStrain, double MaximumDisplacementDip, string? Error, string? SuppressionReason = null,
    double? HeightRatio = null, double? WidthRatio = null, double? AreaRatio = null);

public sealed record PhysicsLabAccessoryInspection(AccessoryLayer Layer, AccessoryMeshFrame Frame,
    Point RawBodyAnchor, double Width, double Height, double MaximumConnectionTravelDip, bool AllowFolding);

public sealed record PhysicsLabAppearanceInspection(long Tick, PhysicsPresentationFrame? Body,
    AttachmentShapeGeometry? Shape, IReadOnlyList<PhysicsLabAccessoryInspection> Accessories, string? RuntimeError);

/// <summary>An externally owned successful physics tick. World Y points upwards.</summary>
public sealed record PhysicsLabAppearanceFrame(long Tick, double Seconds,
    IReadOnlyList<PhysicsLabBoundaryPoint> Boundary, double CoreX, double CoreY,
    double VelocityX, double VelocityY, bool Grounded, double Horizontal,
    bool Down, bool Stretch, bool Jump, bool Drag, PhysicsLabGaitContext? GaitContext = null);

/// <summary>
/// Read-only local appearance snapshot and display-only attachment consumer for physics laboratories.
/// Owns no body, world, host, timer, persistence session or cloud connection. Its owner supplies one
/// successful physics tick at a time; repeated paints cannot advance eye or accessory motion.
/// </summary>
public sealed class PhysicsLabAppearancePresenter : IDisposable
{
    public const double WorldToCanvas = 72;
    private readonly Canvas root = Layer(), gaitLayer = Layer();
    // This lab's accepted force-driven outline exceeds the original host's
    // 8%/10% presentation gate. Product callers retain the original defaults.
    private readonly GaitController gait = new(maximumCompression: .15, maximumWidthDeviation: .15);
    private bool gaitEnabled;
    private readonly Canvas behind = Layer(), front = Layer();
    private readonly Path body = new() { IsHitTestVisible = false };
    private readonly Path leftEye = new() { IsHitTestVisible = false };
    private readonly Path rightEye = new() { IsHitTestVisible = false };
    private readonly DangoAppearanceConfiguration appearance;
    private readonly List<EquippedEntry> equipped = [];
    private readonly string loadStatus;
    private readonly AttachmentCommandRouter router = new(enableHatMotion: true);
    private AttachmentTarget target;
    private AttachmentToken quietToken;
    private AttachmentMotionController motion = null!;
    private QuietIdleController quiet = null!;
    private long sequence, lastTick = -1;
    private DangoFacingDirection facing;
    private bool paused, disposed;
    private string? runtimeError;
    private PhysicsPresentationFrame? currentBody;
    private AttachmentShapeGeometry? currentShape;
    private readonly List<AccessoryMeshFrame> accessoryFrames = [];

    private sealed record EquippedEntry(AccessoryItem Item, BitmapSource Texture, AccessoryMeshVisual Visual,
        AccessoryMotionSolver Motion);

    /// <param name="settingsPath">The existing settings.json location; only adjacent saved appearance/accessory files are read.</param>
    public PhysicsLabAppearancePresenter(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(settingsPath))!;
        var notices = new List<string>();
        var appearancePath = System.IO.Path.Combine(directory, "appearance.json");
        var loadedAppearance = new AppearanceSettingsStore().LoadOrDefault(appearancePath);
        appearance = loadedAppearance.Settings.Configuration;
        notices.Add(loadedAppearance.Warning ?? (File.Exists(appearancePath) ? "已读取保存外观" : "未找到保存外观，使用默认外观"));
        body.Fill = Brush(appearance.BodyColor);
        leftEye.Fill = rightEye.Fill = Brush(appearance.EyeColor);
        root.Children.Add(gaitLayer);
        gaitLayer.Children.Add(behind); gaitLayer.Children.Add(body); gaitLayer.Children.Add(leftEye);
        gaitLayer.Children.Add(rightEye); gaitLayer.Children.Add(front);

        var store = new AccessorySettingsStore();
        var loaded = store.Load(directory);
        EquippedCount = loaded.Configuration.Items.Count(item => item.IsEquipped);
        notices.Add(loaded.Warning ?? (loaded.State == AccessorySettingsLoadState.Missing
            ? "未找到配饰清单，未加载配饰" : $"已读取 {EquippedCount} 件已佩戴配饰"));
        var textures = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);
        var failedAssets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in loaded.Configuration.Items.Where(item => item.IsEquipped)
            .OrderBy(item => item.Layer).ThenBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            if (failedAssets.Contains(item.AssetId)) continue;
            try
            {
                if (!textures.TryGetValue(item.AssetId, out var texture))
                {
                    texture = AccessoryItem.IsBuiltInAsset(item.AssetId)
                        ? BuiltinAccessoryImages.Get(item.AssetId) ?? throw new InvalidDataException("内置配饰图片缺失。")
                        : store.LoadImage(directory, item.AssetId).Bitmap;
                    // Built-in images deliberately rasterize their logical 64-square artwork at 512px.
                    if (!AccessoryItem.IsBuiltInAsset(item.AssetId) &&
                        (texture.PixelWidth != item.PixelWidth || texture.PixelHeight != item.PixelHeight))
                        throw new InvalidDataException("已佩戴配饰图片尺寸与清单不一致。");
                    textures.Add(item.AssetId, texture);
                }
                var visual = new AccessoryMeshVisual();
                visual.SetTexture(texture);
                (item.Layer == AccessoryLayer.Behind ? behind : front).Children.Add(visual);
                equipped.Add(new(item, texture, visual, new()));
            }
            catch (Exception error) when (IsAppearanceFault(error))
            {
                failedAssets.Add(item.AssetId);
                // No filenames, image bytes or private library names enter diagnostics.
                notices.Add($"已佩戴配饰图片读取失败（{error.GetType().Name}：{SafeAssetReason(error)}）");
            }
        }
        loadStatus = string.Join("；", notices);
        target = router.Add(new DangoEntityId(1));
        InitializeMotion();
    }

    /// <summary>Canonical 160×120 canvas with the physical core at (80,66). Content may extend beyond it.</summary>
    public FrameworkElement View => root;
    public int EquippedCount { get; }
    public int RenderedAccessoryCount => equipped.Count(entry => entry.Visual.Frame is not null);
    public string Status => runtimeError is null ? loadStatus : loadStatus + "；" + runtimeError;
    public string EyeDiagnostic { get; private set; } = "眼睛等待物理帧";
    public string MeshDiagnostic { get; private set; } = "配饰等待物理帧";
    internal AttachmentPresentationFrame? CurrentAttachments => currentBody?.Attachments;
    internal IReadOnlyList<AccessoryMeshFrame> AccessoryFrames => accessoryFrames;

    public GaitPresentationFrame? GaitFrame { get; private set; }
    public PhysicsLabGaitState GaitState { get; private set; } = new(false, -1, 0, 0, 0, 0, null);
    public bool GaitEnabled
    {
        get => gaitEnabled;
        set
        {
            VerifyAccess();
            if (gaitEnabled == value) return;
            gaitEnabled = value;
            ResetGait();
        }
    }

    public void InvalidateGaitSampling()
    {
        VerifyAccess(); gait.InvalidateSampling();
    }

    /// <summary>Returns the current presentation snapshot without advancing any motion state.</summary>
    public PhysicsLabAppearanceInspection Inspect()
    {
        VerifyAccess();
        var bodyFrame = currentBody;
        var accessories = bodyFrame is null
            ? Array.Empty<PhysicsLabAccessoryInspection>()
            : equipped.Select(entry =>
            {
                var mesh = entry.Visual.Frame;
                if (mesh is null) return null;
                var anchor = AccessoryBodyMapper.Map(bodyFrame, null, entry.Item.BodyAnchorX, entry.Item.BodyAnchorY);
                var (width, height) = Size(entry.Item);
                var travel = Math.Sqrt(width * width + height * height) * entry.Item.MaxConnectionTravelPercent / 100;
                return new PhysicsLabAccessoryInspection(entry.Item.Layer, mesh, anchor, width, height, travel, entry.Item.AllowFolding);
            }).Where(item => item is not null).Select(item => item!).ToArray();
        return new(lastTick, bodyFrame, currentShape, Array.AsReadOnly(accessories), runtimeError);
    }

    public bool Paused
    {
        get => paused;
        set
        {
            VerifyAccess();
            if (paused == value) return;
            paused = value;
            ClearMotion();
            // Pausing clears residual inertia/blink envelopes without introducing a new physics tick.
            if (currentBody is not null && currentShape is not null) RefreshStill();
        }
    }

    public void Update(PhysicsLabAppearanceFrame frame)
    {
        VerifyAccess(); ArgumentNullException.ThrowIfNull(frame);
        if (paused || frame.Tick <= lastTick) return;
        if (frame.Tick < 0 || !double.IsFinite(frame.Seconds) || frame.Seconds is <= 0 or > .1 ||
            !double.IsFinite(frame.CoreX) || !double.IsFinite(frame.CoreY) ||
            !double.IsFinite(frame.VelocityX) || !double.IsFinite(frame.VelocityY) ||
            !double.IsFinite(frame.Horizontal) || Math.Abs(frame.Horizontal) > 1)
            throw new ArgumentOutOfRangeException(nameof(frame));
        ArgumentNullException.ThrowIfNull(frame.Boundary);
        var points = frame.Boundary.Select(point => new AttachmentPoint(
            (point.X - frame.CoreX) * WorldToCanvas / 160,
            (point.Y - frame.CoreY) * WorldToCanvas / 160)).ToArray();
        var area = points.Select((point, index) =>
        {
            var next = points[(index + 1) % points.Length];
            return point.X * next.Y - point.Y * next.X;
        }).Sum();
        if (area < 0) Array.Reverse(points);
        var shape = AttachmentShapeGeometry.FromBoundary(points);
        var presentation = new PhysicsPresentationFrame(true,
            points.Select(point => new PresentationPoint(point.X * 160 / WorldToCanvas, point.Y * 160 / WorldToCanvas)),
            new(new(0, 0), 0), new(new(0, 0), 0), PhysicsPresentationFallbackReason.None,
            new(PhysicsPresentationBackend.Box2D, 1, PhysicsPresentationDiagnosticCode.SoftBodyActive));
        if (frame.Horizontal < 0) facing = DangoFacingDirection.Left;
        else if (frame.Horizontal > 0) facing = DangoFacingDirection.Right;
        var eligible = frame.Horizontal == 0 && !frame.Down && !frame.Stretch && !frame.Jump && !frame.Drag &&
            Math.Sqrt(frame.VelocityX * frame.VelocityX + frame.VelocityY * frame.VelocityY) <= .15;
        if (!quiet.BeforeStep(new(frame.Grounded, frame.CoreX, frame.CoreY), frame.Seconds, eligible,
            action => Submit(action == QuietIdleAction.Blink ? Blink.Default(EyeSelection.Both) :
                new SetGaze(EyeSelection.Both, action == QuietIdleAction.LookLeft ? -.4 : .4, 0)), ReleaseQuiet))
            throw new InvalidOperationException("待机眼睛动作取消失败。");
        var attachments = motion.UpdateBoundary(presentation, shape, frame.Tick, frame.Seconds, facing,
            new(frame.CoreX, frame.CoreY));
        quiet.AfterStep(frame.Seconds);
        router.ConsumeResults(target);
        currentShape = shape;
        currentBody = presentation with { Attachments = attachments };
        lastTick = frame.Tick;
        runtimeError = null;
        DrawBody(shape);
        DrawEyes(attachments!.Face);
        UpdateAccessories(frame);
        UpdateGait(frame);
    }

    public void Reset()
    {
        VerifyAccess(); ClearMotion(); lastTick = -1; ResetGait();
        currentBody = null; currentShape = null; accessoryFrames.Clear();
        body.Data = leftEye.Data = rightEye.Data = Geometry.Empty;
        foreach (var entry in equipped) entry.Visual.ClearFrame();
        runtimeError = null; EyeDiagnostic = "眼睛等待物理帧"; MeshDiagnostic = "配饰等待物理帧";
    }

    public void Dispose()
    {
        if (disposed) return;
        root.VerifyAccess(); router.Dispose(); equipped.Clear(); accessoryFrames.Clear();
        root.Children.Clear(); disposed = true;
    }

    private void ClearMotion()
    {
        ResetGait();
        target = router.Reset(target); sequence = 0; facing = DangoFacingDirection.Center;
        InitializeMotion();
        foreach (var entry in equipped) entry.Motion.Reset();
    }

    private void InitializeMotion()
    {
        quietToken = router.RegisterAction(target);
        motion = new AttachmentMotionController(router, target);
        motion.SetEyeLayout(appearance.EyeLayout);
        quiet = new QuietIdleController(Random.Shared.Next);
    }

    private bool Submit(AttachmentIntent intent) => router.Submit(target, quietToken, ++sequence, intent).Accepted;
    private bool ReleaseQuiet(QuietIdleAction action) => Submit(new Release(action == QuietIdleAction.Blink
        ? AttachmentChannel.LeftBlink | AttachmentChannel.RightBlink
        : AttachmentChannel.LeftGaze | AttachmentChannel.RightGaze));

    private void DrawBody(AttachmentShapeGeometry shape)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(CanvasPoint(shape.Boundary[0]), true, true);
            context.PolyLineTo(shape.Boundary.Skip(1).Select(CanvasPoint).ToArray(), true, false);
        }
        geometry.Freeze(); body.Data = geometry;
    }

    private void DrawEyes(FaceLayout face)
    {
        leftEye.Data = AttachmentGeometryFactory.CreateEye(face.Left);
        rightEye.Data = AttachmentGeometryFactory.CreateEye(face.Right);
        EyeDiagnostic = $"正式眼睛 · {face.Safety} · tick {lastTick}";
    }

    private void UpdateAccessories(PhysicsLabAppearanceFrame frame)
    {
        accessoryFrames.Clear();
        var requests = new List<(AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)>();
        foreach (var entry in equipped)
        {
            try
            {
                var anchor = AccessoryBodyMapper.Map(currentBody!, null, entry.Item.BodyAnchorX, entry.Item.BodyAnchorY);
                // Keep core translation in the solver's observations, then remove it only for local rendering.
                // Otherwise a translating undeformed body would generate no attachment inertia.
                var sceneOffset = new Vector(frame.CoreX * WorldToCanvas, -frame.CoreY * WorldToCanvas);
                var sceneAnchor = anchor + sceneOffset;
                var (width, height) = Size(entry.Item);
                var mesh = entry.Motion.Update(frame.Tick, frame.Seconds, sceneAnchor, width, height, entry.Item)
                    .Translated(-sceneOffset);
                accessoryFrames.Add(mesh); requests.Add((entry.Visual, mesh));
            }
            catch (Exception error) when (IsAppearanceFault(error))
            {
                entry.Visual.ClearFrame(); runtimeError = $"配饰运动失败（{error.GetType().Name}）";
            }
        }
        foreach (var (_, error) in AccessoryMeshVisual.ApplyBatch(requests).Errors)
            runtimeError = $"配饰网格呈现失败（{error.GetType().Name}）";
        MeshDiagnostic = $"已佩戴 {EquippedCount} · 呈现 {RenderedAccessoryCount} · 顶点 {accessoryFrames.Sum(mesh => mesh.Vertices.Count)}";
    }

    private void RefreshStill()
    {
        var neutral = new EyeMotionSample(new(0, 0), 1, EyeShape.Round, 0);
        var face = FaceLayoutSolver.Solve(currentShape!, neutral, neutral, 0, appearance.EyeLayout, 7);
        currentBody = currentBody! with { Attachments = new(lastTick, target.Generation, new(0, 0, "Reset"), face) { SourceShape = currentShape } };
        DrawEyes(face); accessoryFrames.Clear();
        var requests = new List<(AccessoryMeshVisual Visual, AccessoryMeshFrame Frame)>();
        foreach (var entry in equipped)
        {
            var anchor = AccessoryBodyMapper.Map(currentBody, null, entry.Item.BodyAnchorX, entry.Item.BodyAnchorY);
            var (width, height) = Size(entry.Item);
            var mesh = entry.Motion.Refresh(anchor, width, height, entry.Item);
            accessoryFrames.Add(mesh); requests.Add((entry.Visual, mesh));
        }
        foreach (var (_, error) in AccessoryMeshVisual.ApplyBatch(requests).Errors)
            runtimeError = $"配饰网格呈现失败（{error.GetType().Name}）";
    }

    private void ResetGait()
    {
        gait.Reset();
        GaitFrame = null; gaitLayer.RenderTransform = Transform.Identity;
        GaitState = new(gaitEnabled, lastTick, 0, 0, 0, 0, null);
    }

    private void UpdateGait(PhysicsLabAppearanceFrame frame)
    {
        if (!gaitEnabled) return;
        GaitFrame = null; gaitLayer.RenderTransform = Transform.Identity;
        double requested = 0;
        try
        {
            var context = frame.GaitContext;
            if (context is null || !double.IsFinite(context.DisplayScale) || context.DisplayScale <= 0 ||
                !double.IsFinite(context.Left) || !double.IsFinite(context.Top) ||
                !double.IsFinite(context.ViewportWidth) || context.ViewportWidth <= 0 ||
                !double.IsFinite(context.ViewportHeight) || context.ViewportHeight <= 0)
                throw new ArgumentException("InvalidGaitContext");
            var shape = currentShape!;
            var width = shape.Width * 160; var height = shape.Height * 160;
            // Keep area in world square units; only ratios are used by the original controller.
            var area = Math.Abs(frame.Boundary.Select((p, i) =>
            {
                var q = frame.Boundary[(i + 1) % frame.Boundary.Count];
                return (p.X - frame.CoreX) * (q.Y - frame.CoreY) - (p.Y - frame.CoreY) * (q.X - frame.CoreX);
            }).Sum()) / 2;
            var unsafeInput = frame.Down || frame.Stretch || frame.Jump || frame.Drag;
            var reason = unsafeInput ? "InputOrVerticalRamp" : !frame.Grounded ? "Airborne" : null;
            var baseline = gait.NeutralDimensions;
            if (gait.OutsideNeutralShape(width, height, area)) reason ??= "LabShapeGate15Percent";
            requested = gait.Advance(new(frame.CoreX * WorldToCanvas,
                frame.Boundary.Min(point => point.Y) * WorldToCanvas, frame.Grounded,
                width, height, area, Math.Sign(frame.Horizontal), !unsafeInput, unsafeInput), frame.Seconds);
            if (unsafeInput || !frame.Grounded) gait.InvalidateSampling();
            // Read the controller's actual neutral baseline; diagnostic sampling
            // must not invent a different baseline or alter its safety thresholds.
            baseline = gait.NeutralDimensions;
            var bounds = new LogicalRect(context.Left, context.Top, 160 * context.DisplayScale, 120 * context.DisplayScale);
            var viewport = new LogicalRect(0, 0, context.ViewportWidth, context.ViewportHeight);
            var candidate = GaitGeometry.Create(currentBody!, target.Entity, bounds, viewport, requested, false, 1, clipToTemplate: false);
            if (candidate is not null && !MeshesSafe(candidate, context))
            {
                double lo = 0, hi = 1;
                for (var i = 0; i < 12; i++)
                {
                    var mid = (lo + hi) / 2;
                    var smaller = GaitGeometry.Create(currentBody!, target.Entity, bounds, viewport, requested * mid, false, 1, clipToTemplate: false);
                    if (smaller is not null && MeshesSafe(smaller, context)) lo = mid; else hi = mid;
                }
                candidate = lo < 1e-6 ? null : GaitGeometry.Create(currentBody!, target.Entity, bounds, viewport, requested * lo, false, 1, clipToTemplate: false);
            }
            if (candidate is not null && candidate.Matches(currentBody!, target.Entity) && MeshesSafe(candidate, context))
            {
                GaitFrame = candidate;
                gaitLayer.RenderTransform = DeformableDangoVisual.CreateGaitTransform(currentBody!, candidate);
            }
            var displayed = GaitFrame is null ? 0 : GaitFrame.ScaleY - 1;
            gait.AcceptDisplayed(displayed);
            if (requested != 0 && GaitFrame is null) reason ??= currentBody!.Attachments?.Hat?.Pose is null
                ? "OriginalGeometryRequiresHatPose" : "OriginalGeometryOrMeshSafety";
            if (baseline.Height == 0) reason ??= "AwaitingNeutralBaseline";
            GaitState = new(true, frame.Tick, gait.Phase, requested, displayed,
                GaitFrame is null ? 0 : MaximumDisplacement(GaitFrame, context.DisplayScale), gait.Error, reason,
                baseline.Height > 0 ? height / baseline.Height : null,
                baseline.Width > 0 ? width / baseline.Width : null, baseline.Area > 0 ? area / baseline.Area : null);
        }
        catch (Exception error) when (IsAppearanceFault(error))
        {
            gait.Fail(); GaitFrame = null; gaitLayer.RenderTransform = Transform.Identity;
            GaitState = new(true, frame.Tick, gait.Phase, requested, 0, 0, "InvalidGaitFrame");
        }
    }

    private bool MeshesSafe(GaitPresentationFrame transform, PhysicsLabGaitContext context)
    {
        foreach (var mesh in accessoryFrames)
            foreach (var point in mesh.Vertices)
            {
                var mapped = transform.Map(new(point.X, point.Y));
                var dx = mapped.X - point.X; var dy = mapped.Y - point.Y;
                var x = mapped.X * context.DisplayScale; var y = mapped.Y * context.DisplayScale;
                if (!double.IsFinite(x) || !double.IsFinite(y) ||
                    Math.Sqrt(dx * dx + dy * dy) * context.DisplayScale > 1.99 ||
                    x + context.Left < 0 || x + context.Left > context.ViewportWidth ||
                    y + context.Top < 0 || y + context.Top > context.ViewportHeight) return false;
                // Actual accessory meshes use the unclipped lab layers. Their visible
                // envelope is the surface viewport, not the legacy hat's 16 DIP overlay.
            }
        return true;
    }

    private double MaximumDisplacement(GaitPresentationFrame transform, double scale)
    {
        var points = currentShape!.Boundary.Select(GaitGeometry.Canvas)
            .Concat(new[] { currentBody!.Attachments!.Face.Left, currentBody.Attachments.Face.Right }
                .Where(eye => eye.Visible).SelectMany(EyeOutline.Create).Select(GaitGeometry.Canvas))
            .Concat(accessoryFrames.SelectMany(mesh => mesh.Vertices).Select(point => new LogicalPoint(point.X, point.Y)));
        return points.Max(point =>
        {
            var mapped = transform.Map(point);
            return Math.Sqrt(Math.Pow(mapped.X - point.X, 2) + Math.Pow(mapped.Y - point.Y, 2)) * scale;
        });
    }

    private static (double Width, double Height) Size(AccessoryItem item)
    {
        var longest = 64 * item.SizePercent / 100;
        var ratio = item.PixelWidth / (double)item.PixelHeight;
        return ratio >= 1 ? (longest, longest / ratio) : (longest * ratio, longest);
    }
    private static Point CanvasPoint(AttachmentPoint point) => new(80 + point.X * 160, 66 - point.Y * 160);
    private static Canvas Layer() => new() { Width = 160, Height = 120, IsHitTestVisible = false, ClipToBounds = false };
    private static SolidColorBrush Brush(OpaqueSrgbColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)); brush.Freeze(); return brush;
    }
    private static bool IsAppearanceFault(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or
        ArgumentException or InvalidOperationException or ArithmeticException or System.Runtime.InteropServices.ExternalException;
    private static string SafeAssetReason(Exception error) => error switch
    {
        FileNotFoundException or DirectoryNotFoundException => "文件缺失",
        UnauthorizedAccessException => "无读取权限",
        InvalidDataException => "图片校验、格式或尺寸无效",
        IOException => "文件读取失败",
        _ => "图片处理失败",
    };
    private void VerifyAccess() { root.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this); }
}
