using Dango.Desktop.Rendering.Actions;

namespace Dango.ActionLab;

internal sealed record ActionLabCandidate(string Id, string Label, string Hint,
    double ShakeStrength = DangoActionController.ShakeStrength);

internal static class ActionLabCandidates
{
    private static readonly IReadOnlyList<ActionLabCandidate> Current = Array.AsReadOnly<ActionLabCandidate>(
        [new("current", "当前", "播放现有版本")]);
    private static readonly IReadOnlyList<ActionLabCandidate> Shake = Array.AsReadOnly<ActionLabCandidate>(
        [new("current", "当前", "播放当前抖身"), new("strong", "增强", "播放更明显的抖身", 1.08),
         new("max", "更强", "播放更强的抖身", 1.44)]);

    internal static IReadOnlyList<ActionLabCandidate> For(DangoActionKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return kind == DangoActionKind.Shake ? Shake : Current;
    }
}
