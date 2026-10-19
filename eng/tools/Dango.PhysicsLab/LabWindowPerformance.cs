using System.Diagnostics;

namespace Dango.PhysicsLab;

/// <summary>
/// Opt-in, UI-thread laboratory timing collector. Samples stay in memory until the owner
/// exports a report. Nested categories overlap and must not be added together.
/// </summary>
internal sealed class LabWindowPerformance
{
    private readonly Dictionary<string, List<double>> samples = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, IReadOnlyList<double>> Samples => samples.ToDictionary(
        entry => entry.Key, entry => (IReadOnlyList<double>)entry.Value.AsReadOnly(), StringComparer.Ordinal);

    internal IDisposable Measure(string category) => new Scope(this, category);

    internal void Add(string category, double milliseconds)
    {
        if (!samples.TryGetValue(category, out var values)) samples.Add(category, values = []);
        values.Add(milliseconds);
    }

    internal IReadOnlyDictionary<string, CategoryReport> Report() => samples.ToDictionary(
        entry => entry.Key, entry => Summarize(entry.Value), StringComparer.Ordinal);

    private static CategoryReport Summarize(List<double> values)
    {
        var ordered = values.Order().ToArray();
        var total = values.Sum();
        double Percentile(double fraction) => ordered.Length == 0 ? 0 : ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * fraction) - 1, 0, ordered.Length - 1)];
        return new(values.Count, total, values.Count == 0 ? 0 : total / values.Count,
            Percentile(.50), Percentile(.95), Percentile(.99), ordered.Length == 0 ? 0 : ordered[^1]);
    }

    internal sealed record CategoryReport(int Count, double Total, double Mean, double P50, double P95, double P99, double Max);

    private sealed class Scope(LabWindowPerformance owner, string category) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.Add(category, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
