namespace Dango.Physics.Presentation;

public sealed record SemanticPresentationGateResult(bool Passed, double MaximumCenterError,
    double MaximumPenetrationRatio, double MaximumSupportedGap, IReadOnlyList<string> Failures)
{
    public double MaximumNativeCoreError { get; init; }
}

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
        var nativeCenter = frame.Bodies.Select(body => Math.Max(Math.Abs(body.LocalBody.CoreX - body.Authority.CoreX),
            Math.Abs(body.LocalBody.CoreY - body.Authority.CoreY))).DefaultIfEmpty().Max();
        var proof = frame.NativeCoreProof;
        if (proof is null || proof.ExpectedEpoch == 0 || proof.ActualEpoch != proof.ExpectedEpoch ||
            proof.ExpectedOrdinal == 0 || proof.ActualOrdinal != proof.ExpectedOrdinal || proof.ErrorCode != 0 ||
            proof.SubstepCount is < 1 or > 96 || proof.Bodies.Count != frame.Bodies.Count ||
            !proof.Bodies.Select(body => body.EntityId).ToHashSet().SetEquals(frame.Bodies.Select(body => body.Authority.EntityId)))
            failures.Add("native-core-proof");
        else
        {
            foreach (var body in proof.Bodies)
            {
                var authority = frame.Bodies.Single(value => value.Authority.EntityId == body.EntityId).Authority;
                nativeCenter = Math.Max(nativeCenter, Math.Max(Math.Abs(body.End.CoreX - authority.CoreX),
                    Math.Abs(body.End.CoreY - authority.CoreY)));
                if (body.Start.EntityId != body.End.EntityId || body.Samples.Count != proof.SubstepCount + 1 ||
                    !double.IsFinite(body.MaximumStageError) || body.MaximumStageError < 0)
                {
                    failures.Add("native-core-proof");
                    continue;
                }
                nativeCenter = Math.Max(nativeCenter, body.MaximumStageError);
                for (var index = 0; index < body.Samples.Count; index++)
                {
                    var sample = body.Samples[index];
                    if (!double.IsFinite(sample.Fraction) || sample.Fraction is < 0 or > 1 ||
                        Math.Abs(sample.Fraction - (double)index / proof.SubstepCount) > 1e-6 ||
                        !double.IsFinite(sample.ActualCoreX) || !double.IsFinite(sample.ActualCoreY) ||
                        !double.IsFinite(sample.NativeTargetCoreX) || !double.IsFinite(sample.NativeTargetCoreY))
                    {
                        failures.Add("native-core-proof");
                        continue;
                    }
                    var expected = SemanticPresentationStepTiming.PrescribedPosition(body.Start, body.End, sample.Fraction);
                    nativeCenter = Math.Max(nativeCenter, new[] { Math.Abs(sample.ActualCoreX - expected.X),
                        Math.Abs(sample.ActualCoreY - expected.Y), Math.Abs(sample.NativeTargetCoreX - expected.X),
                        Math.Abs(sample.NativeTargetCoreY - expected.Y) }.Max());
                }
            }
        }
        if (nativeCenter > MaximumCenterError || !double.IsFinite(nativeCenter)) failures.Add("native-core-path");
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
        return new(failures.Count == 0, center, ratio, supportGap, Array.AsReadOnly(failures.Distinct().ToArray()))
            { MaximumNativeCoreError = nativeCenter };
    }
}
