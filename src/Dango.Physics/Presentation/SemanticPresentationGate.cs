namespace Dango.Physics.Presentation;

public sealed record SemanticPresentationGateResult(bool Passed, double MaximumCenterError,
    double MaximumPenetrationRatio, double MaximumSupportedGap, IReadOnlyList<string> Failures);

public static class SemanticPresentationGate
{
    public const double MaximumCenterError = 1e-5;
    public const double MaximumPenetrationRatio = .031;
    public const double MaximumSupportedGap = .008;

    public static SemanticPresentationGateResult Evaluate(SemanticPresentedFrame frame)
    {
        var failures = new List<string>();
        if (!double.IsFinite(frame.StepStartTick) || !double.IsFinite(frame.LocalContourTick) ||
            Math.Abs(frame.LocalContourTick - frame.Tick) > 1e-8 || Math.Abs(frame.LocalContourTick - frame.StepStartTick - 1) > 1e-8)
            failures.Add("local-contour-phase");
        var center = frame.Bodies.Select(body => Math.Max(Math.Abs(body.DrawnCenterX - body.Authority.CoreX),
            Math.Abs(body.DrawnCenterY - body.Authority.CoreY))).DefaultIfEmpty().Max();
        if (center > MaximumCenterError || !double.IsFinite(center)) failures.Add("authority-center");
        if (frame.Bodies.Any(body => !SemanticDrawnGeometry.IsSafeLocalContour(body.LocalBody))) failures.Add("local-contour-safety");
        var ratio = 0d;
        var supportGap = 0d;
        for (var a = 0; a < frame.Bodies.Count; a++)
            for (var b = a + 1; b < frame.Bodies.Count; b++)
            {
                var first = frame.Bodies[a]; var second = frame.Bodies[b];
                var contact = SemanticDrawnGeometry.Contact(first.DrawnContour, second.DrawnContour);
                ratio = Math.Max(ratio, contact.PenetrationRatio);
                if (first.Authority.SupportEntityId == second.Authority.EntityId || second.Authority.SupportEntityId == first.Authority.EntityId)
                    supportGap = Math.Max(supportGap, contact.Gap);
            }
        if (ratio > MaximumPenetrationRatio) failures.Add("display-penetration");
        if (supportGap > MaximumSupportedGap) failures.Add("supported-display-gap");
        return new(failures.Count == 0, center, ratio, supportGap, Array.AsReadOnly(failures.ToArray()));
    }
}
