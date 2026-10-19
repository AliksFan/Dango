namespace Dango.PhysicsLab;

internal readonly record struct LabBeltPresentationDecision(bool Primed, int ApplyBudget, bool Flush);

/// <summary>Whole, ordered physical ticks only; never interpolation or replacement.</summary>
internal static class LabBeltPresentationPacing
{
    internal static LabBeltPresentationDecision Decide(bool primed, int queuedSnapshots,
        bool producerRunning, bool terminalPending)
    {
        if (queuedSnapshots < 0) throw new ArgumentOutOfRangeException(nameof(queuedSnapshots));
        // A stopped producer cannot refill the reserve. Drain its tail and make
        // any failure visible after all preceding successful appearance ticks.
        if (!producerRunning || terminalPending)
            return new(primed, queuedSnapshots + (terminalPending ? 1 : 0), true);
        if (!primed && queuedSnapshots < 2) return new(false, 0, false);
        return new(true, queuedSnapshots >= 4 ? 2 : Math.Min(1, queuedSnapshots), false);
    }
}
