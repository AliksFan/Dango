namespace Dango.Desktop.Physics.Presentation.Attachments;

public readonly record struct SpringState(double Position, double Velocity);

/// <summary>Pure attachment motion; never steps or writes a physical body.</summary>
public static class DampedSpring
{
    public static SpringState Advance(SpringState state, double target, double frequency, double damping, double seconds)
    {
        Validate(state, target, frequency, damping, seconds);
        return Solve(state, target, frequency, damping, seconds);
    }

    public static SpringState AdvanceBounded(SpringState state, double target, double frequency, double damping, double seconds, double minimum, double maximum)
    {
        Validate(state, target, frequency, damping, seconds);
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
        if (minimum == maximum) return new(minimum, 0);
        target = Math.Clamp(target, minimum, maximum);
        state = Project(state, minimum, maximum);
        var count = Math.Max(1, (int)Math.Ceiling(seconds * 240));
        for (var i = 0; i < count; i++) state = BoundedSlice(state, target, frequency, damping, seconds / count, minimum, maximum);
        return state;
    }

    public static SpringState Impulse(SpringState state, double delta, double maximumSpeed)
    {
        if (!double.IsFinite(state.Position) || !double.IsFinite(state.Velocity) || !double.IsFinite(delta) ||
            !double.IsFinite(maximumSpeed) || maximumSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(delta));
        // Saturate the increment before addition so finite extremes cannot overflow.
        var velocity = Math.Clamp(state.Velocity, -maximumSpeed, maximumSpeed);
        var available = delta >= 0 ? maximumSpeed - velocity : -maximumSpeed - velocity;
        return new(state.Position, delta >= 0 ? velocity + Math.Min(delta, available) : velocity + Math.Max(delta, available));
    }

    private static void Validate(SpringState state, double target, double frequency, double damping, double seconds)
    {
        if (!double.IsFinite(state.Position) || !double.IsFinite(state.Velocity) || !double.IsFinite(target)) throw new ArgumentOutOfRangeException(nameof(state));
        if (!double.IsFinite(frequency) || frequency is <= 0 or > 10) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (!double.IsFinite(damping) || damping is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(damping));
        if (!double.IsFinite(seconds) || seconds is < 0 or > 0.1) throw new ArgumentOutOfRangeException(nameof(seconds));
    }

    private static SpringState Solve(SpringState state, double target, double frequency, double damping, double seconds)
    {
        if (seconds == 0) return state;
        var omega = 2 * Math.PI * frequency;
        var decayRate = damping * omega;
        var phase = omega * Math.Sqrt((1 - damping) * (1 + damping)) * seconds;
        // sinc avoids loss of precision as damping approaches the critical case.
        var sinc = Math.Abs(phase) < 1e-4 ? 1 - phase * phase / 6 + Math.Pow(phase, 4) / 120 : Math.Sin(phase) / phase;
        var sinOverFrequency = seconds * sinc;
        var cos = Math.Cos(phase);
        var decay = Math.Exp(-decayRate * seconds);
        var offset = state.Position - target;
        var position = target + decay * (offset * cos + (state.Velocity + decayRate * offset) * sinOverFrequency);
        var velocity = decay * (state.Velocity * cos - (decayRate * state.Velocity + omega * omega * offset) * sinOverFrequency);
        if (!double.IsFinite(position) || !double.IsFinite(velocity)) throw new ArithmeticException("Attachment spring exceeded finite numeric range.");
        return new(position, velocity);
    }

    private static SpringState Project(SpringState state, double minimum, double maximum)
    {
        var position = Math.Clamp(state.Position, minimum, maximum);
        var velocity = (position <= minimum && state.Velocity < 0) || (position >= maximum && state.Velocity > 0) ? 0 : state.Velocity;
        return new(position, velocity);
    }

    private static SpringState BoundedSlice(SpringState state, double target, double frequency, double damping, double seconds, double minimum, double maximum)
    {
        var end = Solve(state, target, frequency, damping, seconds);
        // At <= 1/240 s and <= 10 Hz there is at most one interior turning point.
        // Check it as well as the endpoint: an excursion can leave and re-enter a bound.
        var turn = seconds;
        if (state.Velocity != 0 && Math.Sign(state.Velocity) != Math.Sign(end.Velocity))
        {
            double lo = 0, hi = seconds;
            for (var i = 0; i < 48; i++)
            {
                var mid = (lo + hi) / 2;
                if (Math.Sign(Solve(state, target, frequency, damping, mid).Velocity) == Math.Sign(state.Velocity)) lo = mid;
                else hi = mid;
            }
            turn = (lo + hi) / 2;
        }
        foreach (var checkpoint in new[] { turn, seconds })
        {
            var probe = checkpoint == seconds ? end : Solve(state, target, frequency, damping, checkpoint);
            if (probe.Position >= minimum && probe.Position <= maximum) continue;
            var bound = probe.Position < minimum ? minimum : maximum;
            double lo = 0, hi = checkpoint;
            for (var i = 0; i < 48; i++)
            {
                var mid = (lo + hi) / 2;
                var position = Solve(state, target, frequency, damping, mid).Position;
                if (bound == maximum ? position < bound : position > bound) lo = mid;
                else hi = mid;
            }
            // Continue the remaining time from the first impact with outward speed removed.
            // A rest state with an in-range target cannot cross another bound within this slice.
            return Project(Solve(new(bound, 0), target, frequency, damping, seconds - hi), minimum, maximum);
        }
        return Project(end, minimum, maximum);
    }
}
