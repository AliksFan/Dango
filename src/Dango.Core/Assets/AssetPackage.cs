using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Core.Scene;

namespace Dango.Core.Assets;

public sealed record AssetCanvas(
    double Width,
    double Height,
    double PixelDensity,
    LogicalPoint Origin)
{
    public LogicalRect Bounds => new(Origin.X, Origin.Y, Width, Height);
}

public sealed record AssetLayer(string Id, int Order, string Source, string SlotId);

public sealed record AssetSlot(string Id, LogicalPoint Position);

public sealed record AssetAnchor(string Id, LogicalPoint Position);

public sealed record AssetFrame(long TimeMilliseconds, IReadOnlyList<string> LayerIds);

public enum TransformProperty
{
    TranslationX,
    TranslationY,
    RotationDegrees,
    ScaleX,
    ScaleY,
    Opacity,
}

public sealed record TransformKeyframe(long TimeMilliseconds, double Value);

public sealed record TransformTrack(
    string TargetLayerId,
    TransformProperty Property,
    IReadOnlyList<TransformKeyframe> Keyframes);

public sealed record AssetClip(
    string Id,
    long DurationMilliseconds,
    bool Loop,
    IReadOnlyList<AssetFrame> Frames,
    IReadOnlyList<TransformTrack> TransformTracks);

public sealed record AssetEvent(
    string ClipId,
    long TimeMilliseconds,
    string Name,
    string? AnchorId);

public sealed class AssetPackage
{
    internal AssetPackage(
        int assetVersion,
        AssetCanvas canvas,
        IReadOnlyList<AssetLayer> layers,
        IReadOnlyDictionary<string, AssetSlot> slots,
        IReadOnlyDictionary<string, AssetClip> clips,
        IReadOnlyDictionary<DangoActionState, string> actions,
        IReadOnlyDictionary<string, AssetAnchor> anchors,
        IReadOnlyList<HitShape> hitShapes,
        IReadOnlyList<AssetEvent> events,
        IReadOnlyDictionary<string, string> fallbacks)
    {
        AssetVersion = assetVersion;
        Canvas = canvas;
        Layers = layers;
        Slots = slots;
        Clips = clips;
        Actions = actions;
        Anchors = anchors;
        HitShapes = hitShapes;
        Events = events;
        Fallbacks = fallbacks;
    }

    public int AssetVersion { get; }

    public AssetCanvas Canvas { get; }

    public IReadOnlyList<AssetLayer> Layers { get; }

    public IReadOnlyDictionary<string, AssetSlot> Slots { get; }

    public IReadOnlyDictionary<string, AssetClip> Clips { get; }

    public IReadOnlyDictionary<DangoActionState, string> Actions { get; }

    public IReadOnlyDictionary<string, AssetAnchor> Anchors { get; }

    public IReadOnlyList<HitShape> HitShapes { get; }

    public IReadOnlyList<AssetEvent> Events { get; }

    public IReadOnlyDictionary<string, string> Fallbacks { get; }

    public AssetClip ClipFor(DangoActionState actionState)
    {
        if (!Actions.TryGetValue(actionState, out var clipId) || !Clips.TryGetValue(clipId, out var clip))
        {
            throw new KeyNotFoundException($"动作 {actionState} 没有可用 clip。");
        }

        return clip;
    }
}

public sealed class AssetValidationException : FormatException
{
    public AssetValidationException(IEnumerable<string> errors)
        : base($"资产包校验失败：{string.Join("；", errors)}")
    {
    }

    public AssetValidationException(string error, Exception innerException)
        : base($"资产包校验失败：{error}", innerException)
    {
    }
}
