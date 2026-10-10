namespace Dango.Physics.Presentation;

/// <summary>Admission-only semantic input. No contour or local solver state enters this contract.</summary>
public sealed record SemanticBodyState(
    DangoEntityId EntityId, double CoreX, double CoreY,
    double CoreVelocityX, double CoreVelocityY,
    bool FloorGrounded, DangoEntityId? SupportEntityId);

public sealed record SemanticPairState(
    DangoEntityId First, DangoEntityId Second,
    double NormalX, double NormalY, double Gap, double ProjectedWidth);

public sealed record SemanticPresentationSnapshot
{
    public const int Version = 1;
    public SemanticPresentationSnapshot(string epoch, long tick,
        IEnumerable<SemanticBodyState> bodies, IEnumerable<SemanticPairState> pairs,
        int schemaVersion = Version, ulong sceneRevision = 1)
    {
        Epoch = epoch;
        Tick = tick;
        SchemaVersion = schemaVersion;
        SceneRevision = sceneRevision;
        Bodies = Array.AsReadOnly(bodies.ToArray());
        Pairs = Array.AsReadOnly(pairs.ToArray());
        Validate();
    }

    public string Epoch { get; }
    public long Tick { get; }
    public int SchemaVersion { get; }
    public ulong SceneRevision { get; }
    public IReadOnlyList<SemanticBodyState> Bodies { get; }
    public IReadOnlyList<SemanticPairState> Pairs { get; }

    public void Validate()
    {
        if (SchemaVersion != Version || string.IsNullOrWhiteSpace(Epoch) || Epoch.Length > 128 || Tick < 0 || SceneRevision == 0)
            throw new ArgumentException("Unsupported semantic frame boundary.");
        if (Bodies.Count is < 1 or > 2 || Pairs.Count > 1 ||
            Bodies.Select(body => body.EntityId).Distinct().Count() != Bodies.Count)
            throw new ArgumentException("Semantic frames require one or two unique bodies and at most one pair.");
        foreach (var body in Bodies)
        {
            if (body.EntityId.Value <= 0 || !Finite(body.CoreX, body.CoreY, body.CoreVelocityX, body.CoreVelocityY) ||
                Math.Abs(body.CoreVelocityX) > 12.00001 || Math.Abs(body.CoreVelocityY) > 12.00001 ||
                body.SupportEntityId is { } support && (support == body.EntityId || !Bodies.Any(b => b.EntityId == support)))
                throw new ArgumentException("Invalid semantic body.");
        }
        foreach (var pair in Pairs)
        {
            var normalLength = Math.Sqrt(pair.NormalX * pair.NormalX + pair.NormalY * pair.NormalY);
            if (pair.First == pair.Second || !Bodies.Any(b => b.EntityId == pair.First) || !Bodies.Any(b => b.EntityId == pair.Second) ||
                !Finite(pair.NormalX, pair.NormalY, pair.Gap, pair.ProjectedWidth) ||
                Math.Abs(normalLength * normalLength - 1) > .0002 || pair.ProjectedWidth <= 0 || pair.Gap > .008)
                throw new ArgumentException("Invalid semantic contact pair.");
            var first = Bodies.Single(body => body.EntityId == pair.First);
            var second = Bodies.Single(body => body.EntityId == pair.Second);
            if ((second.CoreX - first.CoreX) * pair.NormalX + (second.CoreY - first.CoreY) * pair.NormalY < 0)
                throw new ArgumentException("A common pair normal must point from its first body toward its second body.");
        }
        foreach (var body in Bodies.Where(body => body.SupportEntityId is not null))
        {
            var supportingBody = Bodies.Single(candidate => candidate.EntityId == body.SupportEntityId);
            if (supportingBody.SupportEntityId == body.EntityId)
                throw new ArgumentException("The two-body support graph cannot contain a cycle.");
            var pair = Pairs.SingleOrDefault(candidate => (candidate.First == body.EntityId && candidate.Second == body.SupportEntityId) ||
                (candidate.Second == body.EntityId && candidate.First == body.SupportEntityId));
            if (pair is null || pair.Gap > .008 || (pair.First == body.EntityId ? -pair.NormalY : pair.NormalY) < .7 ||
                body.CoreY <= supportingBody.CoreY + .4)
                throw new ArgumentException("A supporting relationship requires its upward common contact pair.");
        }
    }

    private static bool Finite(params double[] values) => values.All(double.IsFinite);
}

public sealed record SemanticPresentationSample(string Epoch, double Tick,
    IReadOnlyList<SemanticBodyState> Bodies, IReadOnlyList<SemanticPairState> Pairs, ulong SceneRevision = 1);

/// <summary>A bounded common-frame buffer; all bodies are sampled at the same delayed time.</summary>
public sealed class SemanticPresentationSampler
{
    public const int Capacity = 8;
    public static TimeSpan FixedStep { get; } = TimeSpan.FromSeconds(1d / 60);
    private readonly List<SemanticPresentationSnapshot> frames = [];
    private string? epoch;
    public int Count => frames.Count;

    public void BindEpoch(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("Invalid epoch.", nameof(value));
        if (epoch == value) return;
        frames.Clear();
        epoch = value;
    }

    public bool Push(SemanticPresentationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        if (epoch != snapshot.Epoch) return false;
        if (frames.Count > 0 && snapshot.Tick <= frames[^1].Tick) return false;
        if (frames.Count > 0)
        {
            var previous = frames[^1];
            if (snapshot.SceneRevision < previous.SceneRevision) return false;
            if (snapshot.SceneRevision == previous.SceneRevision &&
                !snapshot.Bodies.Select(body => body.EntityId).ToHashSet().SetEquals(previous.Bodies.Select(body => body.EntityId)))
                return false;
        }
        frames.Add(snapshot);
        if (frames.Count > Capacity) frames.RemoveAt(0);
        return true;
    }

    public SemanticPresentationSample Sample(double tick)
    {
        if (!double.IsFinite(tick) || frames.Count == 0 || tick < frames[0].Tick || tick > frames[^1].Tick)
            throw new ArgumentOutOfRangeException(nameof(tick), "Only buffered common time may be sampled; no hidden extrapolation.");
        var left = frames.Last(frame => frame.Tick <= tick);
        var right = frames.FirstOrDefault(frame => frame.Tick > tick);
        if (right is null) return new(left.Epoch, tick, left.Bodies, left.Pairs, left.SceneRevision);
        var fraction = (tick - left.Tick) / (right.Tick - left.Tick);
        var seconds = (right.Tick - left.Tick) * FixedStep.TotalSeconds;
        var sampled = left.Bodies.Select(body =>
        {
            var next = right.Bodies.SingleOrDefault(candidate => candidate.EntityId == body.EntityId);
            // Membership changes are atomic at their authoritative snapshot, never interpolated into another body.
            if (next is null) return body;
            return body with
            {
                CoreX = Hermite(body.CoreX, next.CoreX, body.CoreVelocityX, next.CoreVelocityX, fraction, seconds),
                CoreY = Hermite(body.CoreY, next.CoreY, body.CoreVelocityY, next.CoreVelocityY, fraction, seconds),
                CoreVelocityX = HermiteVelocity(body.CoreX, next.CoreX, body.CoreVelocityX, next.CoreVelocityX, fraction, seconds),
                CoreVelocityY = HermiteVelocity(body.CoreY, next.CoreY, body.CoreVelocityY, next.CoreVelocityY, fraction, seconds),
            };
        }).ToArray();
        return new(left.Epoch, tick, Array.AsReadOnly(sampled), left.Pairs, left.SceneRevision);
    }

    private static double Hermite(double first, double second, double firstVelocity, double secondVelocity,
        double fraction, double seconds)
    {
        var squared = fraction * fraction;
        var cubed = squared * fraction;
        return (2 * cubed - 3 * squared + 1) * first + (cubed - 2 * squared + fraction) * seconds * firstVelocity +
            (-2 * cubed + 3 * squared) * second + (cubed - squared) * seconds * secondVelocity;
    }

    private static double HermiteVelocity(double first, double second, double firstVelocity, double secondVelocity,
        double fraction, double seconds)
    {
        var squared = fraction * fraction;
        return (6 * squared - 6 * fraction) * first / seconds + (3 * squared - 4 * fraction + 1) * firstVelocity +
            (-6 * squared + 6 * fraction) * second / seconds + (3 * squared - 2 * fraction) * secondVelocity;
    }
}
