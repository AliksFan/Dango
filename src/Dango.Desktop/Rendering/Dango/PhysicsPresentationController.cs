using Dango.Desktop.Physics.Presentation;

namespace Dango.Desktop.Rendering.Visuals;

public interface IPhysicsPresentationSink
{
    void ApplyFrame(PhysicsPresentationFrame frame);
}

public sealed class PhysicsPresentationController : IDisposable
{
    private readonly IPhysicsPresentationWorld world;
    private readonly IPhysicsPresentationSink sink;

    public PhysicsPresentationController(IPhysicsPresentationWorld world, IPhysicsPresentationSink sink)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sink);
        this.world = world;
        this.sink = sink;
    }

    public PhysicsPresentationFrame Tick(PhysicsPresentationInput input, TimeSpan fixedStep)
    {
        var frame = PresentationSquashTransformer.Apply(world.Step(input, fixedStep), input.SquashStrength);
        frame = PresentationGroundFrictionTransformer.Apply(frame, input.GroundFrictionCue);
        sink.ApplyFrame(frame);
        return frame;
    }

    public void Dispose() => world.Dispose();
}

public static class PresentationGroundFrictionTransformer
{
    public static PhysicsPresentationFrame Apply(PhysicsPresentationFrame frame, double cue)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (cue == 0 || !frame.IsVisible || frame.Contour.Count == 0)
        {
            return frame;
        }

        var amount = Math.Clamp(cue, -1, 1);
        var bottom = frame.Contour.Min(point => point.Y);
        var height = Math.Max(0.0001, frame.Contour.Max(point => point.Y) - bottom);
        PresentationPoint Transform(PresentationPoint point)
        {
            var heightRatio = Math.Clamp((point.Y - bottom) / height, 0, 1);
            return new PresentationPoint(
                Math.Clamp(point.X + (amount * heightRatio * 0.055), -1, 1),
                point.Y);
        }

        PresentationAttachment TransformAttachment(PresentationAttachment attachment) =>
            new(Transform(attachment.Position), attachment.RotationRadians);

        return new PhysicsPresentationFrame(
            frame.IsVisible,
            frame.Contour.Select(Transform),
            TransformAttachment(frame.Eyes),
            TransformAttachment(frame.Hat),
            frame.FallbackReason,
            frame.Diagnostics,
            TransformAttachment(frame.LeftEye),
            TransformAttachment(frame.RightEye));
    }
}

public static class PresentationSquashTransformer
{
    public static PhysicsPresentationFrame Apply(PhysicsPresentationFrame frame, double strength)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (strength <= 0 || !frame.IsVisible || frame.Contour.Count == 0)
        {
            return frame;
        }

        var amount = Math.Clamp(strength, 0, 1);
        var bottom = frame.Contour.Min(point => point.Y);
        PresentationPoint Transform(PresentationPoint point) => new(
            Math.Clamp(point.X * (1 + (0.05 * amount)), -1, 1),
            Math.Clamp(bottom + ((point.Y - bottom) * (1 - (0.08 * amount))), -1, 1));
        PresentationAttachment TransformAttachment(PresentationAttachment attachment) =>
            new(Transform(attachment.Position), attachment.RotationRadians);

        return new PhysicsPresentationFrame(
            frame.IsVisible,
            frame.Contour.Select(Transform),
            TransformAttachment(frame.Eyes),
            TransformAttachment(frame.Hat),
            frame.FallbackReason,
            frame.Diagnostics,
            TransformAttachment(frame.LeftEye),
            TransformAttachment(frame.RightEye));
    }
}
