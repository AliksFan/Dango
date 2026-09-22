using Dango.Core.Coordinates;
using Dango.Physics;

namespace Dango.Desktop.Physics.Presentation;

/// <summary>Read-only local drawing transform; it never participates in physics or hit testing.</summary>
public sealed record GaitPresentationFrame(DangoEntityId EntityId, int Generation, long StepIndex,
    double CenterX, double BottomY, double ScaleX, double ScaleY)
{
    public bool IsValid => Generation >= 0 && StepIndex >= 0 && double.IsFinite(CenterX) &&
        double.IsFinite(BottomY) && double.IsFinite(ScaleX) && double.IsFinite(ScaleY) &&
        ScaleX is >= .98 and <= 1.02 && ScaleY is >= .98 and <= 1.01;
    public bool IsIdentity => Math.Abs(ScaleX - 1) < 1e-12 && Math.Abs(ScaleY - 1) < 1e-12;
    public bool Matches(PhysicsPresentationFrame frame, DangoEntityId entity) => IsValid && EntityId == entity &&
        frame.Attachments is { } attachments && attachments.Generation == Generation && attachments.StepIndex == StepIndex;
    public LogicalPoint Map(LogicalPoint point) => new(CenterX + ScaleX * (point.X - CenterX),
        BottomY + ScaleY * (point.Y - BottomY));
}
