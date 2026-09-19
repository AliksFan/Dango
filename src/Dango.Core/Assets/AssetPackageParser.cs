using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;

namespace Dango.Core.Assets;

public static class AssetPackageParser
{
    public const int RuntimeContractVersion = 1;

    private static readonly string[] RequiredAnchorIds =
    [
        "ground-contact",
        "click",
        "island-grab",
        "island-stand",
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AssetPackage Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        AssetDocument document;
        try
        {
            document = JsonSerializer.Deserialize<AssetDocument>(json, SerializerOptions) ??
                throw new JsonException("资产文档为空。");
        }
        catch (JsonException exception)
        {
            throw new AssetValidationException($"JSON 结构无效：{exception.Message}", exception);
        }

        var errors = Validate(document);
        if (errors.Count > 0)
        {
            throw new AssetValidationException(errors);
        }

        return CreatePackage(document);
    }

    private static List<string> Validate(AssetDocument document)
    {
        var errors = new List<string>();
        if (document.AssetVersion != RuntimeContractVersion)
        {
            errors.Add($"不支持 assetVersion {document.AssetVersion}，当前仅支持 {RuntimeContractVersion}");
        }

        if (document.Compatibility is null ||
            document.Compatibility.MinimumRuntimeVersion > RuntimeContractVersion ||
            document.Compatibility.MaximumRuntimeVersion < RuntimeContractVersion ||
            document.Compatibility.MinimumRuntimeVersion > document.Compatibility.MaximumRuntimeVersion)
        {
            errors.Add("compatibility 未包含当前运行时契约版本");
        }

        var canvasIsValid = ValidateCanvas(document.Canvas, errors);

        var slotIds = UniqueIds(document.Slots.Select(slot => slot.Id), "slots", errors);
        var layerIds = UniqueIds(document.Layers.Select(layer => layer.Id), "layers", errors);
        foreach (var layer in document.Layers)
        {
            RequireText(layer.Source, $"layers[{layer.Id}].source", errors);
            if (!IsSafeAssetPath(layer.Source))
            {
                errors.Add($"layers[{layer.Id}].source 必须是资产包内的相对路径");
            }

            if (!slotIds.Contains(layer.Slot))
            {
                errors.Add($"layers[{layer.Id}] 引用了未知挂点 {layer.Slot}");
            }
        }

        var clipsById = UniqueById(document.Clips, clip => clip.Id, "clips", errors);
        foreach (var clip in document.Clips)
        {
            ValidateClip(clip, layerIds, errors);
        }

        foreach (var action in Enum.GetValues<DangoActionState>().Where(state => state != DangoActionState.Hidden))
        {
            if (!document.Actions.TryGetValue(action, out var clipId))
            {
                errors.Add($"actions 缺少 {action} clip");
            }
            else if (!clipsById.ContainsKey(clipId))
            {
                errors.Add($"actions[{action}] 引用了缺失 clip {clipId}");
            }
        }

        var anchorIds = UniqueIds(document.Anchors.Select(anchor => anchor.Id), "anchors", errors);
        foreach (var requiredAnchorId in RequiredAnchorIds)
        {
            if (!anchorIds.Contains(requiredAnchorId))
            {
                errors.Add($"anchors 缺少 {requiredAnchorId}");
            }
        }

        if (canvasIsValid)
        {
            var canvasBounds = ToLogicalRect(document.Canvas!);
            foreach (var slot in document.Slots)
            {
                if (!canvasBounds.Contains(new LogicalPoint(slot.X, slot.Y)))
                {
                    errors.Add($"slots[{slot.Id}] 超出 canvas");
                }
            }

            foreach (var anchor in document.Anchors)
            {
                if (!canvasBounds.Contains(new LogicalPoint(anchor.X, anchor.Y)))
                {
                    errors.Add($"anchors[{anchor.Id}] 超出 canvas");
                }
            }

            ValidateHitShapes(document.HitShapes, canvasBounds, errors);
        }

        ValidateEvents(document.Events, clipsById, anchorIds, errors);
        ValidateFallbacks(document.Fallbacks, clipsById.Keys, errors);
        return errors;
    }

    private static bool ValidateCanvas(CanvasDocument? canvas, ICollection<string> errors)
    {
        if (canvas is null)
        {
            errors.Add("缺少 canvas");
            return false;
        }

        var isValid = true;
        if (!PositiveFinite(canvas.Width) || !PositiveFinite(canvas.Height) || !PositiveFinite(canvas.PixelDensity))
        {
            errors.Add("canvas 尺寸和 pixelDensity 必须是有限正数");
            isValid = false;
        }

        if (!double.IsFinite(canvas.Origin.X) || !double.IsFinite(canvas.Origin.Y))
        {
            errors.Add("canvas.origin 必须是有限坐标");
            isValid = false;
        }

        return isValid;
    }

    private static void ValidateClip(
        ClipDocument clip,
        IReadOnlySet<string> layerIds,
        ICollection<string> errors)
    {
        RequireText(clip.Id, "clips.id", errors);
        if (clip.DurationMilliseconds <= 0)
        {
            errors.Add($"clips[{clip.Id}].durationMs 必须大于 0");
        }

        if (clip.Frames.Count == 0)
        {
            errors.Add($"clips[{clip.Id}] 至少需要一帧");
        }

        foreach (var frame in clip.Frames)
        {
            if (frame.TimeMilliseconds < 0 || frame.TimeMilliseconds > clip.DurationMilliseconds)
            {
                errors.Add($"clips[{clip.Id}] frame 时间越界");
            }

            foreach (var layerId in frame.Layers)
            {
                if (!layerIds.Contains(layerId))
                {
                    errors.Add($"clips[{clip.Id}] frame 引用了未知 layer {layerId}");
                }
            }
        }

        foreach (var track in clip.TransformTracks)
        {
            if (!layerIds.Contains(track.TargetLayer))
            {
                errors.Add($"clips[{clip.Id}] transformTrack 引用了未知 layer {track.TargetLayer}");
            }

            if (track.Keyframes.Count == 0)
            {
                errors.Add($"clips[{clip.Id}] transformTrack 至少需要一个关键帧");
            }

            foreach (var keyframe in track.Keyframes)
            {
                if (keyframe.TimeMilliseconds < 0 || keyframe.TimeMilliseconds > clip.DurationMilliseconds ||
                    !double.IsFinite(keyframe.Value))
                {
                    errors.Add($"clips[{clip.Id}] transformTrack 关键帧无效");
                }
            }
        }
    }

    private static void ValidateHitShapes(
        IReadOnlyList<HitShapeDocument> hitShapes,
        LogicalRect canvasBounds,
        ICollection<string> errors)
    {
        UniqueIds(hitShapes.Select(shape => shape.Id), "hitShapes", errors);
        foreach (var purpose in Enum.GetValues<HitShapePurpose>())
        {
            if (!hitShapes.Any(shape => shape.Purpose == purpose))
            {
                errors.Add($"hitShapes 缺少 {purpose} 区域");
            }
        }

        foreach (var shape in hitShapes)
        {
            if (!PositiveFinite(shape.Bounds.Width) || !PositiveFinite(shape.Bounds.Height) ||
                !double.IsFinite(shape.Bounds.X) || !double.IsFinite(shape.Bounds.Y))
            {
                errors.Add($"hitShapes[{shape.Id}] bounds 无效");
                continue;
            }

            var bounds = new LogicalRect(
                shape.Bounds.X,
                shape.Bounds.Y,
                shape.Bounds.Width,
                shape.Bounds.Height);
            if (!canvasBounds.Contains(bounds))
            {
                errors.Add($"hitShapes[{shape.Id}] 超出 canvas");
            }
        }
    }

    private static void ValidateEvents(
        IReadOnlyList<EventDocument> events,
        IReadOnlyDictionary<string, ClipDocument> clipsById,
        IReadOnlySet<string> anchorIds,
        ICollection<string> errors)
    {
        foreach (var assetEvent in events)
        {
            if (!clipsById.TryGetValue(assetEvent.Clip, out var clip))
            {
                errors.Add($"events 引用了未知 clip {assetEvent.Clip}");
            }
            else if (assetEvent.TimeMilliseconds < 0 || assetEvent.TimeMilliseconds > clip.DurationMilliseconds)
            {
                errors.Add($"events[{assetEvent.Name}] 时间越界");
            }

            if (assetEvent.Anchor is not null && !anchorIds.Contains(assetEvent.Anchor))
            {
                errors.Add($"events[{assetEvent.Name}] 引用了未知 anchor {assetEvent.Anchor}");
            }
        }
    }

    private static void ValidateFallbacks(
        IReadOnlyList<FallbackDocument> fallbacks,
        IEnumerable<string> clipIds,
        ICollection<string> errors)
    {
        var knownClips = clipIds.ToHashSet(StringComparer.Ordinal);
        var fallbackMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fallback in fallbacks)
        {
            if (!knownClips.Contains(fallback.From) || !knownClips.Contains(fallback.To))
            {
                errors.Add($"fallback 引用了未知 clip {fallback.From} -> {fallback.To}");
            }

            if (!fallbackMap.TryAdd(fallback.From, fallback.To))
            {
                errors.Add($"fallback 重复定义 {fallback.From}");
            }
        }

        foreach (var start in fallbackMap.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (fallbackMap.TryGetValue(current, out var next))
            {
                if (!visited.Add(current))
                {
                    errors.Add($"fallback 存在循环，起点 {start}");
                    break;
                }

                current = next;
            }
        }
    }

    private static AssetPackage CreatePackage(AssetDocument document)
    {
        var canvasDocument = document.Canvas!;
        var canvas = new AssetCanvas(
            canvasDocument.Width,
            canvasDocument.Height,
            canvasDocument.PixelDensity,
            new LogicalPoint(canvasDocument.Origin.X, canvasDocument.Origin.Y));
        var layers = Array.AsReadOnly(document.Layers
            .OrderBy(layer => layer.Order)
            .ThenBy(layer => layer.Id, StringComparer.Ordinal)
            .Select(layer => new AssetLayer(layer.Id, layer.Order, layer.Source, layer.Slot))
            .ToArray());
        var slots = ReadOnlyDictionary(document.Slots.ToDictionary(
            slot => slot.Id,
            slot => new AssetSlot(slot.Id, new LogicalPoint(slot.X, slot.Y)),
            StringComparer.Ordinal));
        var clips = ReadOnlyDictionary(document.Clips.ToDictionary(
            clip => clip.Id,
            CreateClip,
            StringComparer.Ordinal));
        var actions = new ReadOnlyDictionary<DangoActionState, string>(
            new Dictionary<DangoActionState, string>(document.Actions));
        var anchors = ReadOnlyDictionary(document.Anchors.ToDictionary(
            anchor => anchor.Id,
            anchor => new AssetAnchor(anchor.Id, new LogicalPoint(anchor.X, anchor.Y)),
            StringComparer.Ordinal));
        var hitShapes = Array.AsReadOnly(document.HitShapes.Select(CreateHitShape).ToArray());
        var events = Array.AsReadOnly(document.Events.Select(assetEvent => new AssetEvent(
            assetEvent.Clip,
            assetEvent.TimeMilliseconds,
            assetEvent.Name,
            assetEvent.Anchor)).ToArray());
        var fallbacks = ReadOnlyDictionary(document.Fallbacks.ToDictionary(
            fallback => fallback.From,
            fallback => fallback.To,
            StringComparer.Ordinal));

        return new AssetPackage(
            document.AssetVersion,
            canvas,
            layers,
            slots,
            clips,
            actions,
            anchors,
            hitShapes,
            events,
            fallbacks);
    }

    private static AssetClip CreateClip(ClipDocument clip) =>
        new(
            clip.Id,
            clip.DurationMilliseconds,
            clip.Loop,
            Array.AsReadOnly(clip.Frames.Select(frame => new AssetFrame(
                frame.TimeMilliseconds,
                Array.AsReadOnly(frame.Layers.ToArray()))).ToArray()),
            Array.AsReadOnly(clip.TransformTracks.Select(track => new TransformTrack(
                track.TargetLayer,
                track.Property,
                Array.AsReadOnly(track.Keyframes.Select(keyframe => new TransformKeyframe(
                    keyframe.TimeMilliseconds,
                    keyframe.Value)).ToArray()))).ToArray()));

    private static HitShape CreateHitShape(HitShapeDocument shape)
    {
        var bounds = new LogicalRect(
            shape.Bounds.X,
            shape.Bounds.Y,
            shape.Bounds.Width,
            shape.Bounds.Height);
        return shape.Kind switch
        {
            HitShapeKind.Rectangle => new RectangleHitShape(shape.Id, shape.Purpose, bounds),
            HitShapeKind.Ellipse => new EllipseHitShape(shape.Id, shape.Purpose, bounds),
            _ => throw new InvalidOperationException($"未知 hit shape：{shape.Kind}"),
        };
    }

    private static HashSet<string> UniqueIds(
        IEnumerable<string> ids,
        string collectionName,
        ICollection<string> errors)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"{collectionName} 包含空 id");
            }
            else if (!result.Add(id))
            {
                errors.Add($"{collectionName} 包含重复 id {id}");
            }
        }

        return result;
    }

    private static Dictionary<string, T> UniqueById<T>(
        IEnumerable<T> items,
        Func<T, string> idSelector,
        string collectionName,
        ICollection<string> errors)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"{collectionName} 包含空 id");
            }
            else if (!result.TryAdd(id, item))
            {
                errors.Add($"{collectionName} 包含重复 id {id}");
            }
        }

        return result;
    }

    private static ReadOnlyDictionary<string, TValue> ReadOnlyDictionary<TValue>(
        Dictionary<string, TValue> dictionary) =>
        new(dictionary);

    private static LogicalRect ToLogicalRect(CanvasDocument canvas) =>
        new(canvas.Origin.X, canvas.Origin.Y, canvas.Width, canvas.Height);

    private static bool PositiveFinite(double value) => double.IsFinite(value) && value > 0;

    private static bool IsSafeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        return !path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static void RequireText(string value, string path, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{path} 不能为空");
        }
    }

    private sealed class AssetDocument
    {
        [JsonPropertyName("$schema")]
        public string? Schema { get; init; }

        public int AssetVersion { get; init; }

        public CompatibilityDocument? Compatibility { get; init; }

        public CanvasDocument? Canvas { get; init; }

        public IReadOnlyList<LayerDocument> Layers { get; init; } = [];

        public IReadOnlyList<SlotDocument> Slots { get; init; } = [];

        public IReadOnlyList<ClipDocument> Clips { get; init; } = [];

        public IReadOnlyDictionary<DangoActionState, string> Actions { get; init; } =
            new Dictionary<DangoActionState, string>();

        public IReadOnlyList<AnchorDocument> Anchors { get; init; } = [];

        public IReadOnlyList<HitShapeDocument> HitShapes { get; init; } = [];

        public IReadOnlyList<EventDocument> Events { get; init; } = [];

        public IReadOnlyList<FallbackDocument> Fallbacks { get; init; } = [];
    }

    private sealed class CompatibilityDocument
    {
        public int MinimumRuntimeVersion { get; init; }

        public int MaximumRuntimeVersion { get; init; }
    }

    private sealed class CanvasDocument
    {
        public double Width { get; init; }

        public double Height { get; init; }

        public double PixelDensity { get; init; }

        public PointDocument Origin { get; init; } = new();
    }

    private sealed class PointDocument
    {
        public double X { get; init; }

        public double Y { get; init; }
    }

    private sealed class RectDocument
    {
        public double X { get; init; }

        public double Y { get; init; }

        public double Width { get; init; }

        public double Height { get; init; }
    }

    private sealed class LayerDocument
    {
        public string Id { get; init; } = string.Empty;

        public int Order { get; init; }

        public string Source { get; init; } = string.Empty;

        public string Slot { get; init; } = string.Empty;
    }

    private sealed class SlotDocument
    {
        public string Id { get; init; } = string.Empty;

        public double X { get; init; }

        public double Y { get; init; }
    }

    private sealed class ClipDocument
    {
        public string Id { get; init; } = string.Empty;

        public long DurationMilliseconds { get; init; }

        public bool Loop { get; init; }

        public IReadOnlyList<FrameDocument> Frames { get; init; } = [];

        public IReadOnlyList<TransformTrackDocument> TransformTracks { get; init; } = [];
    }

    private sealed class FrameDocument
    {
        public long TimeMilliseconds { get; init; }

        public IReadOnlyList<string> Layers { get; init; } = [];
    }

    private sealed class TransformTrackDocument
    {
        public string TargetLayer { get; init; } = string.Empty;

        public TransformProperty Property { get; init; }

        public IReadOnlyList<TransformKeyframeDocument> Keyframes { get; init; } = [];
    }

    private sealed class TransformKeyframeDocument
    {
        public long TimeMilliseconds { get; init; }

        public double Value { get; init; }
    }

    private sealed class AnchorDocument
    {
        public string Id { get; init; } = string.Empty;

        public double X { get; init; }

        public double Y { get; init; }
    }

    private enum HitShapeKind
    {
        Rectangle,
        Ellipse,
    }

    private sealed class HitShapeDocument
    {
        public string Id { get; init; } = string.Empty;

        public HitShapePurpose Purpose { get; init; }

        public HitShapeKind Kind { get; init; }

        public RectDocument Bounds { get; init; } = new();
    }

    private sealed class EventDocument
    {
        public string Clip { get; init; } = string.Empty;

        public long TimeMilliseconds { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Anchor { get; init; }
    }

    private sealed class FallbackDocument
    {
        public string From { get; init; } = string.Empty;

        public string To { get; init; } = string.Empty;
    }
}
