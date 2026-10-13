using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Accessories;
using Dango.Desktop.AppShell.ControlCenter;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Physics.Tuning;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Physics.Configuration;

namespace Dango.MaterialSample;

// Review-only: draws the experiment's bodies with the product presentation chain — presentation
// adapter, attachment motion (eyes, hat), legacy hat visual and deformable image accessories —
// without the product physics. The body is drawn 1:1 with the native contact outline:
//   product box: x = 80 + 72 p.x, y = 66 - 58 p.y (160 x 120 DIPs per body)
//   experiment:  screen = core + k * (box - (80, 66)), k = Scale * bodySize / 80
// so p.x = x / bodySize and p.y = y * 80 / 60 / bodySize give 0.9 and 58/60 of the world scale,
// exactly the contact outline, while eyes, hats and accessories keep their authored proportions.
internal sealed class FormalDangoLayer : Canvas
{
    private static readonly Brush[] SlotColors = [Brush(255, 232, 194), Brush(201, 226, 255), Brush(221, 204, 248)];
    private const double HatSize = 1.35; // StackedDangoSceneRuntime.DisplayHatSize with attachments.
    private readonly List<DangoPhysicsPresentationAdapter> presenters = [];
    private readonly List<DangoFacingDirection> facing = [];
    private readonly List<BodyVisual> visuals = [];
    private readonly Dictionary<string, AccessoryMeshVisual> meshes = [];
    private AttachmentSceneController? attachments;
    private AccessorySceneController? accessories;
    private double[] sizes = [], lastNorm = [];
    private IReadOnlyList<PhysicsPresentationFrame> lastFrames = [];

    private sealed class BodyVisual
    {
        public readonly Canvas Behind = new() { Width = 160, Height = 120, IsHitTestVisible = false };
        public readonly DeformableDangoVisual Body = new() { ShowHat = false, IsHitTestVisible = false };
        public readonly Canvas Front = new() { Width = 160, Height = 120, IsHitTestVisible = false };
        public readonly DangoHatVisual Hat = new() { IsHitTestVisible = false };
    }

    internal FormalDangoLayer() { IsHitTestVisible = false; ClipToBounds = true; }

    // Loaded read-only from the product's local settings; nothing is written back.
    internal DangoAppearanceConfiguration Appearance { get; private set; } = DangoAppearanceConfiguration.Default;
    internal AccessoryConfiguration SavedAccessories { get; private set; } = AccessoryConfiguration.Empty;
    internal bool SavedAccessoriesEnabled { get; private set; }
    internal string HatPresetId { get; set; } = "sprout";
    internal bool ImageAccessories { get; set; }
    internal string? Error { get; private set; }
    internal string SettingsSource { get; private set; } = "";

    // Product contour validation result per body (None = drawn from the live contour).
    internal string ContourFailures => string.Join(",", visuals.Select(v => v.Body.LastContourFailure));

    internal string Debug(IReadOnlyList<DangoBodyState> states, double scale) => string.Join(" | ", visuals.Select((v, i) =>
    {
        var k = scale * lastNorm[i] / 80; var b = v.Body.BodyPath.Data?.Bounds ?? Rect.Empty;
        var o = ContactOutline.Build(states[i], sizes[i]);
        var pc = lastFrames[i].Contour; var raw = states[i].Contour;
        var pw = pc.Max(q => q.X) - pc.Min(q => q.X); var rw = Enumerable.Range(0, 16).Max(n => raw[2 * n]) - Enumerable.Range(0, 16).Min(n => raw[2 * n]);
        return $"{i + 1}: pres/raw {pw / rw:F3} size {sizes[i]:F2} fill w {b.Width * k:F1} h {b.Height * k:F1} outline w {(o.Max(p => p.X) - o.Min(p => p.X)) * scale:F1} h {(o.Max(p => p.Y) - o.Min(p => p.Y)) * scale:F1}";
    }));

    internal void LoadProductSettings()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dango");
        try
        {
            var settings = Path.Combine(root, "settings.json");
            if (File.Exists(settings) && JsonDocument.Parse(File.ReadAllText(settings)).RootElement.TryGetProperty("hatPresetId", out var hat) &&
                DangoHatPresets.IsKnown(hat.GetString())) HatPresetId = hat.GetString()!;
            Appearance = new AppearanceSettingsSession(Path.Combine(root, "appearance.json")).SavedAppearance;
            var session = new AccessorySettingsSession(root, HatPresetId);
            images = session.TryGetImage;
            SavedAccessories = session.SavedConfiguration;
            SavedAccessoriesEnabled = !session.IsCompatibilityMode;
            ImageAccessories = SavedAccessoriesEnabled;
            SettingsSource = $"本机外观（只读）：帽型 {HatPresetId}，图片配饰{(SavedAccessoriesEnabled ? "已保存" : "未保存（兼容旧帽子）")}";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            SettingsSource = $"本机外观读取失败，使用默认：{error.Message}";
        }
    }

    private Func<string, System.Windows.Media.Imaging.BitmapSource?> images = _ => null;

    // The neutral pose that anchors eyes and hat, from the experiment's own default body
    // (the product calibrates the same way from its physics: 240 frames from Add(0, 0)).
    internal void Reset(string library, IReadOnlyList<DangoEntityId> ids, double[] bodySizes)
    {
        Children.Clear(); visuals.Clear(); meshes.Clear(); presenters.Clear(); facing.Clear();
        attachments?.Dispose(); attachments = null; accessories = null; Error = null;
        sizes = bodySizes;
        try
        {
            var shape = new LandingBuildControls(100, 50, 50).Configure(LandingBuildTable.SizeDampingPreview);
            PhysicsPresentationFrame baseline;
            using (var calibration = DangoPhysicsWorld.Create(library, capacity: 1, configuration: DangoPhysicsDefaults.Current))
            {
                var id = calibration.Add(0, 0, shape);
                var adapter = new DangoPhysicsPresentationAdapter(DangoPhysicsDefaults.Current);
                DangoBodyState? last = null;
                for (var f = 0; f < 240; f++)
                {
                    var frame = calibration.Step(MaterialSession.FixedStep, [new DangoPhysicsInput(id)]);
                    if (!frame.Succeeded) throw new InvalidOperationException($"眼帽标定失败：原生错误 {frame.ErrorCode}");
                    last = frame.Bodies[0];
                }
                baseline = adapter.Present(ToPresentation(last!, 1), MaterialSession.FixedStep, DangoFacingDirection.Center);
            }
            attachments = new(baseline);
            attachments.Synchronize(ids);
            attachments.SetEyeLayout(ids[0], Appearance.EyeLayout);
            accessories = new(images);
            ApplyAccessoryMode(null);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or DangoPhysicsException)
        {
            Error = error.Message;
        }
        foreach (var _ in ids)
        {
            presenters.Add(new(DangoPhysicsDefaults.Current));
            facing.Add(DangoFacingDirection.Center);
            var visual = new BodyVisual();
            visuals.Add(visual);
            Children.Add(visual.Behind); Children.Add(visual.Body); Children.Add(visual.Front);
        }
        // Every hat above every body, as the product scene does.
        foreach (var visual in visuals) Children.Add(visual.Hat);
    }

    internal void ApplyAccessoryMode(StackedPhysicsPresentationFrame? frame)
    {
        if (accessories is null) return;
        var configuration = SavedAccessoriesEnabled ? SavedAccessories : AccessoryConfiguration.FromLegacyHat(HatPresetId);
        accessories.SetConfiguration(configuration, ImageAccessories, frame);
    }

    private const double Vertical = 80.0 / 60;

    // Presentation units: body size, enlarged only when a wide or tall shape would otherwise
    // exceed the product presenter's .98 box limit and be shrunk. The drawing scale compensates.
    private static double Normalisation(DangoBodyState s, double size)
    {
        double x = 0, y = 0;
        for (var k = 0; k < s.Contour.Count; k += 2) { x = Math.Max(x, Math.Abs(s.Contour[k])); y = Math.Max(y, Math.Abs(s.Contour[k + 1])); }
        return Math.Max(size, Math.Max(x, y * Vertical) / .97);
    }

    private static DangoBodyState ToPresentation(DangoBodyState s, double size)
    {
        const double vertical = Vertical;
        var contour = new float[s.Contour.Count];
        for (var k = 0; k < contour.Length; k++) contour[k] = (float)(s.Contour[k] * (k % 2 == 0 ? 1 : vertical) / size);
        return s with
        {
            Contour = contour, SignedArea = (float)(s.SignedArea * vertical / (size * size)),
            CentroidX = (float)(s.CentroidX / size), CentroidY = (float)(s.CentroidY * vertical / size),
        };
    }

    // Experiment: mirror the face tilt about the ground normal (vertical). The product solves the
    // face at angle a = -tilt; here the eyes are re-rotated about their midpoint to angle -a.
    internal bool MirrorEyeTilt { get; set; } = true;

    private static PhysicsPresentationFrame MirrorFace(PhysicsPresentationFrame frame)
    {
        if (frame.Attachments is not { Face: var face } attachments) return frame;
        var angle = face.Left.Angle;
        if (angle == 0) return frame;
        var mid = new AttachmentPoint((face.Left.Center.X + face.Right.Center.X) / 2, (face.Left.Center.Y + face.Right.Center.Y) / 2);
        double c = Math.Cos(-2 * angle), s = Math.Sin(-2 * angle);
        EyePresentationFrame Mirror(EyePresentationFrame eye)
        {
            double x = eye.Center.X - mid.X, y = eye.Center.Y - mid.Y;
            return eye with { Center = new(mid.X + x * c - y * s, mid.Y + x * s + y * c), Angle = -eye.Angle };
        }
        return frame with { Attachments = attachments with { Face = face with { Left = Mirror(face.Left), Right = Mirror(face.Right) } } };
    }

    internal void Apply(DangoPhysicsFrame physics, IReadOnlyList<DangoEntityId> ids, int selected, double horizontal,
        Func<double, double, Point> toScreen, double scale)
    {
        if (attachments is null || accessories is null || visuals.Count != ids.Count) return;
        var seconds = MaterialSession.FixedStep.TotalSeconds;
        if (horizontal != 0) facing[selected] = horizontal < 0 ? DangoFacingDirection.Left : DangoFacingDirection.Right;
        var states = ids.Select(id => physics.Bodies.Single(b => b.EntityId == id)).ToArray();
        var box = new LogicalRect(0, 0, 160, 120);
        lastNorm = [.. states.Select((s, i) => Normalisation(s, sizes[i]))];
        var bodies = states.Select((s, i) => new DesktopPhysicsBody(s.EntityId, box,
            presenters[i].Present(ToPresentation(s, lastNorm[i]), MaterialSession.FixedStep, facing[i]))).ToArray();
        var selectedFacing = horizontal < 0 ? DangoFacingDirection.Left : horizontal > 0 ? DangoFacingDirection.Right : DangoFacingDirection.Center;
        IReadOnlyList<PhysicsPresentationFrame> frames;
        try { frames = attachments.Advance(new DesktopPhysicsSceneFrame(physics, bodies), seconds, ids[selected], selectedFacing); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { Error = error.Message; return; }
        lastFrames = frames;
        var stacked = new StackedPhysicsPresentationFrame(frames, [.. ids.Select(_ => box)], [.. ids.Select(_ => default(LogicalVector))],
            0, [], false, 0) { EntityIds = ids, ColorSlots = [.. ids.Select((_, i) => i)] };
        accessories.Advance(stacked, seconds);
        Error = attachments.LastError ?? accessories.Error;
        var hat = DangoHatPresets.GetAppearance(HatPresetId);
        for (var i = 0; i < visuals.Count; i++)
        {
            var v = visuals[i];
            var k = scale * lastNorm[i] / 80;
            var core = toScreen(states[i].CoreX, states[i].CoreY);
            var transform = new MatrixTransform(k, 0, 0, k, core.X - k * 80, core.Y - k * 66);
            transform.Freeze();
            foreach (var element in new UIElement[] { v.Behind, v.Body, v.Front, v.Hat }) element.RenderTransform = transform;
            // Body 1 carries the saved appearance and accessories, as the product's customized entity.
            var customized = i == 0;
            v.Body.BodyFill = customized ? Brush(Appearance.BodyColor.R, Appearance.BodyColor.G, Appearance.BodyColor.B) : SlotColors[i];
            v.Body.EyeFill = customized ? Brush(Appearance.EyeColor.R, Appearance.EyeColor.G, Appearance.EyeColor.B) : Brush(0x2B, 0x20, 0x1E);
            v.Body.Appearance = hat; v.Body.HatSize = HatSize;
            v.Body.ApplyFrame(MirrorEyeTilt ? MirrorFace(frames[i]) : frames[i]);
            var imageHat = customized && accessories.Enabled;
            v.Hat.Visibility = imageHat ? Visibility.Collapsed : Visibility.Visible;
            if (!imageHat) { v.Hat.Appearance = hat; v.Hat.HatSize = HatSize; v.Hat.ApplyFrame(frames[i]); }
        }
        // Product layering (AccessoryHostBinding): with image accessories each body is
        // behind < body < hat < front, per index; without them every hat is above every body.
        for (var i = 0; i < visuals.Count; i++)
        {
            var on = accessories.Enabled;
            SetZIndex(visuals[i].Behind, on ? i * 4 : 0); SetZIndex(visuals[i].Body, on ? i * 4 + 1 : 0);
            SetZIndex(visuals[i].Hat, on ? i * 4 + 2 : 1); SetZIndex(visuals[i].Front, on ? i * 4 + 3 : 0);
        }
        ApplyMeshes(visuals[0], scale * lastNorm[0] / 80);
    }

    private void ApplyMeshes(BodyVisual body, double k)
    {
        var outputs = accessories!.Enabled ? accessories.Frames : [];
        var ids = outputs.Select(o => o.Item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in meshes.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            if (meshes[id].Parent is Canvas owner) owner.Children.Remove(meshes[id]);
            meshes.Remove(id);
        }
        var requests = new List<(AccessoryMeshVisual, AccessoryMeshFrame)>();
        foreach (var output in outputs)
        {
            if (!meshes.TryGetValue(output.Item.Id, out var mesh)) meshes[output.Item.Id] = mesh = new() { Width = 160, Height = 120, IsHitTestVisible = false };
            var target = output.Item.Layer == AccessoryLayer.Behind ? body.Behind : body.Front;
            if (!ReferenceEquals(mesh.Parent, target)) { (mesh.Parent as Canvas)?.Children.Remove(mesh); target.Children.Add(mesh); }
            SetZIndex(mesh, output.Item.Order);
            mesh.SetTexture(output.Texture);
            requests.Add((mesh, output.Mesh));
        }
        // Rasterise at the on-screen scale so the enlarged review stays sharp.
        var dpi = VisualTreeHelper.GetDpi(this);
        foreach (var (_, error) in AccessoryMeshVisual.ApplyBatch(requests, new System.Windows.DpiScale(dpi.DpiScaleX * k, dpi.DpiScaleY * k)).Errors)
            Error = $"配饰画面暂不可用（{error.GetType().Name}）";
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
