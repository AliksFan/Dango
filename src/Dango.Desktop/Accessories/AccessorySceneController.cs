using System.Windows.Media.Imaging;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;
using Dango.Physics;
using Dango.Desktop.Accessories.Editing;
using Point = System.Windows.Point;

namespace Dango.Desktop.Accessories;

public sealed record AccessoryVisualFrame(AccessoryItem Item, BitmapSource Texture, AccessoryMeshFrame Mesh)
{
    public int PieceIndex { get; init; }
}

/// <summary>Single runtime-owned presentation state; hosts consume it without advancing simulation.</summary>
internal sealed class AccessorySceneController(Func<string, BitmapSource?> images,
    Func<AccessoryItem, AccessoryEditorProject?>? projects = null)
{
    private readonly Dictionary<string, AccessoryMotionSolver> solvers = [];
    private readonly Dictionary<string, AccessoryEditorProject?> projectsById = new(StringComparer.Ordinal);
    private AccessoryConfiguration configuration = AccessoryConfiguration.Empty;
    private DangoEntityId? entity;
    private PhysicsPresentationFrame? sourceBody;
    private GaitPresentationFrame? sourceGait;
    private LogicalRect sourceBounds;
    private long stepIndex;
    private bool paused, unavailable;
    public IReadOnlyList<AccessoryVisualFrame> Frames { get; private set; } = [];
    public string? Error { get; private set; }
    public bool Enabled { get; private set; }

    public void SetConfiguration(AccessoryConfiguration value, bool enabled, StackedPhysicsPresentationFrame? frame)
    {
        configuration = value; Enabled = enabled;
        foreach (var id in solvers.Keys.Where(id => !value.Items.Any(item => item.Id == id && item.IsEquipped)).ToArray()) solvers.Remove(id);
        var retainedProjects = value.Items.Where(item => item.IsEquipped && item.DeformationMode == AccessoryDeformationMode.Skeleton)
            .Select(item => item.ProjectId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in projectsById.Keys.Where(id => !retainedProjects.Contains(id) || projectsById[id] is null).ToArray()) projectsById.Remove(id);
        Refresh(frame);
    }

    public void Pause()
    {
        paused = true;
        foreach (var solver in solvers.Values) solver.Paused = true;
    }

    public void Resume()
    {
        paused = false;
        foreach (var solver in solvers.Values) solver.Paused = unavailable;
    }

    public void Reset()
    {
        solvers.Clear(); projectsById.Clear(); entity = null; sourceBody = null; Frames = []; Error = null;
    }

    public bool Matches(StackedPhysicsPresentationFrame frame, int index) => Enabled && index == 0 &&
        !frame.ConstraintsReleased && frame.Bodies.Count > 0 && frame.Bodies[0].IsVisible &&
        frame.EntityIds.Count > 0 && frame.EntityIds[0] == entity &&
        ReferenceEquals(frame.Bodies[0].Contour, sourceBody?.Contour) &&
        Equals(frame.Gait, sourceGait) && DangoHostFrameBinding.Bounds(frame, 0) == sourceBounds;

    public void Advance(StackedPhysicsPresentationFrame frame, double seconds) => Build(frame, seconds, ++stepIndex);
    public void Refresh(StackedPhysicsPresentationFrame? frame) => Build(frame, 0, stepIndex);

    private void Build(StackedPhysicsPresentationFrame? frame, double seconds, long step)
    {
        if (!Enabled || frame is null || frame.Bodies.Count == 0 || frame.EntityIds.Count == 0 ||
            !frame.Bodies[0].IsVisible || frame.ConstraintsReleased)
        {
            unavailable = true;
            foreach (var solver in solvers.Values) solver.Paused = true;
            Frames = []; return;
        }
        if (unavailable)
        {
            unavailable = false;
            foreach (var solver in solvers.Values) solver.Paused = paused;
        }
        if (entity != frame.EntityIds[0]) { Reset(); entity = frame.EntityIds[0]; }
        try
        {
            var body = frame.Bodies[0];
            var bounds = DangoHostFrameBinding.Bounds(frame, 0);
            var gait = frame.Gait is { } candidate && candidate.Matches(body, entity.Value) ? candidate : null;
            var outputs = new List<AccessoryVisualFrame>();
            foreach (var item in configuration.Items.Where(item => item.IsEquipped).OrderBy(item => item.Layer).ThenBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                var project = item.DeformationMode == AccessoryDeformationMode.Skeleton ? Project(item) : null;
                if (item.DeformationMode == AccessoryDeformationMode.Skeleton && project is null)
                    throw new InvalidOperationException("分组骨架工程不可用，无法独立呈现绘制片。");
                var texture = item.DeformationMode == AccessoryDeformationMode.Skeleton ? null : images(item.AssetId) ?? BuiltinAccessoryImages.Get(item.AssetId);
                if (texture is null && project is null) continue; // The session retains unavailable assets without flattening a skeleton.
                if (!solvers.TryGetValue(item.Id, out var solver)) solvers[item.Id] = solver = new() { Paused = paused };
                var local = AccessoryBodyMapper.Map(body, gait, item.BodyAnchorX, item.BodyAnchorY);
                var anchor = new Point(local.X + bounds.X, local.Y + bounds.Y);
                var longest = 64 * item.SizePercent / 100;
                var ratio = item.PixelWidth / (double)item.PixelHeight;
                var width = ratio >= 1 ? longest : longest * ratio;
                var height = ratio >= 1 ? longest / ratio : longest;
                var meshes = seconds == 0 ? solver.RefreshPieces(anchor, width, height, item, project) : solver.UpdatePieces(step, seconds, anchor, width, height, item, project);
                if (project is not null)
                {
                    for (var piece = 0; piece < project.RenderPieces.Count; piece++)
                        outputs.Add(new(item, project.Assets[project.RenderPieces[piece].AssetId].Bitmap, meshes[piece]) { PieceIndex = piece });
                }
                else outputs.Add(new(item, texture!, meshes[0]));
            }
            Frames = outputs.AsReadOnly(); Error = null;
            sourceBody = body; sourceGait = frame.Gait; sourceBounds = bounds;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            Frames = [];
            unavailable = true;
            foreach (var solver in solvers.Values) solver.Paused = true;
            Error = $"配饰预览暂不可用（{error.GetType().Name}）；可重试或取消。";
        }
    }

    private AccessoryEditorProject? Project(AccessoryItem item)
    {
        if (item.ProjectId is not { } id) return null;
        if (projectsById.TryGetValue(id, out var cached)) return cached;
        var project = projects?.Invoke(item);
        // A project is immutable and content-addressed. Never decode/read it on every owner frame.
        // Cache an unavailable result until an explicit configuration refresh retries the lookup.
        projectsById.Add(id, project?.ProjectId == id ? project : null);
        return projectsById[id];
    }
}
