using Dango.Core.Coordinates;

namespace Dango.Desktop.AppShell.Rooms;

// Presentation samples use cumulative successful simulation time, never render-count time.
// Shapes/gait remain the latest physical frame; this translates the complete entity only.
internal sealed class RoomPositionInterpolator
{
    private LogicalPoint previous;
    private LogicalPoint current;
    private long previousTicks;
    private long currentTicks;
    private bool hasSample;

    internal void Clear() => hasSample = false;

    internal void Reset(LogicalPoint point, long simulationTicks)
    {
        Validate(point);
        previous = current = point;
        previousTicks = currentTicks = simulationTicks;
        hasSample = true;
    }

    internal void Add(LogicalPoint point, long simulationTicks)
    {
        Validate(point);
        if (!hasSample || simulationTicks <= currentTicks)
        {
            Reset(point, simulationTicks);
            return;
        }
        previous = current;
        previousTicks = currentTicks;
        current = point;
        currentTicks = simulationTicks;
    }

    internal LogicalPoint At(long presentationTicks)
    {
        if (!hasSample) throw new InvalidOperationException("No healthy presentation sample.");
        var alpha = currentTicks == previousTicks ? 1 :
            Math.Clamp((double)(presentationTicks - previousTicks) / (currentTicks - previousTicks), 0, 1);
        return new(previous.X + (current.X - previous.X) * alpha,
            previous.Y + (current.Y - previous.Y) * alpha);
    }

    private static void Validate(LogicalPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentOutOfRangeException(nameof(point));
    }
}
