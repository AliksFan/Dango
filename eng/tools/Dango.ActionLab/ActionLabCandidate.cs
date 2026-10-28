using Dango.Desktop.Rendering.Actions;
using Dango.Physics;

namespace Dango.ActionLab;

internal sealed record ActionLabCandidate(string Id, string Label, string Hint,
    double ShakeStrength = DangoActionController.ShakeStrength,
    DangoMuscleShakeProfile ShakeProfile = DangoMuscleShakeProfile.Original);

internal static class ActionLabCandidates
{
    private static readonly IReadOnlyList<ActionLabCandidate> Current = Array.AsReadOnly<ActionLabCandidate>(
        [new("current", "当前", "播放现有版本")]);
    private static readonly IReadOnlyList<ActionLabCandidate> Shake = Array.AsReadOnly<ActionLabCandidate>(
        [new("current", "原更强", "保留原更强抖身作为对照", 1.44),
         new("soft", "肩部柔和", "保持更强幅度，柔和上半两侧轮廓", 1.44, DangoMuscleShakeProfile.SoftShoulders),
         new("random", "随机", "覆盖中间3/4高度，采用 .08π 参考弧长间距；沿两眼实际连线，相邻施力行反向，大小平滑随机；本次seed可重放",
             1.44, DangoMuscleShakeProfile.EyeAlignedRandom),
         new("random-wide", "间距 +1/3", "与随机候选相同，仅施力行间距增加1/3；本次seed可重放",
             1.44, DangoMuscleShakeProfile.EyeAlignedRandomWide)]);

    internal static IReadOnlyList<ActionLabCandidate> For(DangoActionKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return kind == DangoActionKind.Shake ? Shake : Current;
    }
}
