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
         new("random", "随机", "覆盖中间3/4高度，沿用 .04π 参考弧长间距并增加施力点；沿两眼实际连线向内，每对共享有界平滑随机张力；本次seed可重放",
             1.44, DangoMuscleShakeProfile.EyeAlignedRandom)]);

    internal static IReadOnlyList<ActionLabCandidate> For(DangoActionKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return kind == DangoActionKind.Shake ? Shake : Current;
    }
}
