using Dango.Core.Animation;
using Dango.Desktop.Physics.Tuning;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation;

public sealed class DangoPhysicsPresentationAdapter
{
    private readonly DangoSelfPhysicsParameters parameters;
    private PresentationPoint leftEye = new(-PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
    private PresentationPoint rightEye = new(PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
    private PresentationPoint hat = new(0, 0.82);
    private double hatAngle;
    private int stepCount;

    public DangoPhysicsPresentationAdapter(DangoSelfPhysicsParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        this.parameters = parameters;
    }

    public PhysicsPresentationFrame Present(DangoBodyState state, TimeSpan fixedStep, DangoFacingDirection facing)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.PointCount != parameters.NodeCount || state.Contour.Count < parameters.NodeCount * 2)
            throw new ArgumentException("物理状态轮廓数量与配置不一致。", nameof(state));
        if (fixedStep <= TimeSpan.Zero || fixedStep > TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        stepCount++;
        // Normalize only the configured natural size. The host restores that size
        // through its bounds; material strain must never be fitted into a box.
        var presentationScale = DangoSelfPhysicsParameters.CreateDefault().RadialRestLength / parameters.RadialRestLength;
        var contour = Enumerable.Range(0, parameters.NodeCount).Select(index => new PresentationPoint(
            state.Contour[index * 2] * presentationScale,
            state.Contour[index * 2 + 1] * presentationScale)).ToArray();
        var facingValue = facing switch { DangoFacingDirection.Left => -1, DangoFacingDirection.Right => 1, _ => 0 };
        var eyeCenter = 0.06 * facingValue;
        var leftTarget = new PresentationPoint(eyeCenter - PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        var rightTarget = new PresentationPoint(eyeCenter + PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        var highest = contour.MaxBy(point => point.Y);
        var hatTarget = new PresentationPoint(highest.X * 0.08, Math.Min(1.05, highest.Y));
        var eyeBlend = 1 - Math.Exp(-8 * fixedStep.TotalSeconds);
        var hatBlend = 1 - Math.Exp(-6 * fixedStep.TotalSeconds);
        leftEye = Blend(leftEye, leftTarget, eyeBlend); rightEye = Blend(rightEye, rightTarget, eyeBlend);
        hat = Blend(hat, hatTarget, hatBlend);
        var leftTop = contour.OrderByDescending(point => point.Y).Take(3).MinBy(point => point.X);
        var rightTop = contour.OrderByDescending(point => point.Y).Take(3).MaxBy(point => point.X);
        var targetHatAngle = Math.Atan2(rightTop.Y - leftTop.Y, rightTop.X - leftTop.X);
        hatAngle += (targetHatAngle - hatAngle) * hatBlend;
        var maximumRadius = contour.Max(point => Math.Sqrt(point.X * point.X + point.Y * point.Y));
        var metrics = new SoftBodyMetrics(
            new SoftBodyConfiguration(parameters.NodeCount, parameters.SubStepCount),
            state.SignedArea * presentationScale * presentationScale,
            new PresentationPoint(state.CentroidX * presentationScale, state.CentroidY * presentationScale),
            maximumRadius, state.MaximumSpeed, state.MaximumSpeed * state.MaximumSpeed * parameters.NodeCount,
            state.SignedArea > 0, state.MaximumSpeed < 0.03);
        return AuthoredPoseSampler.CreateSoftBodyActive(
            contour, new PresentationAttachment(Blend(leftEye, rightEye, 0.5), 0),
            new PresentationAttachment(leftEye, 0), new PresentationAttachment(rightEye, 0),
            new PresentationAttachment(hat, hatAngle), stepCount, metrics);
    }

    public void Reset()
    {
        leftEye = new(-PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        rightEye = new(PhysicsPresentationFrame.DefaultEyeHalfDistance, -0.05);
        hat = new(0, 0.82); hatAngle = 0; stepCount = 0;
    }

    private static PresentationPoint Blend(PresentationPoint from, PresentationPoint to, double amount) =>
        new(from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount);
}
