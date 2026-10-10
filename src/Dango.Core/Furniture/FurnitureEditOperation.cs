namespace Dango.Core.Furniture;

public enum FurnitureEditKind { Insert, Move, Delete }

/// <summary>Immutable request; retries keep every field, not just the operation ID.</summary>
public sealed record FurnitureEditOperation(
    Guid OperationId,
    long ExpectedVersion,
    FurnitureEditKind Kind,
    Guid? TargetInstanceId,
    FurniturePlatformInstance? Candidate)
{
    public FurnitureLayout BuildCandidate(FurnitureLayout baseline, FurnitureLayoutContract? contract = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        baseline.Validate(contract);
        if (OperationId == Guid.Empty || ExpectedVersion < 0 || baseline.LayoutVersion != ExpectedVersion ||
            baseline.LastOperationId == OperationId)
            throw new InvalidOperationException("布局版本已变化或请求标识不可用。");
        var valid = Kind switch
        {
            FurnitureEditKind.Insert => baseline.Platform is null && TargetInstanceId is null && Candidate is not null,
            FurnitureEditKind.Move => baseline.Platform is not null && TargetInstanceId == baseline.Platform.InstanceId &&
                Candidate?.InstanceId == TargetInstanceId,
            FurnitureEditKind.Delete => baseline.Platform is not null && TargetInstanceId == baseline.Platform.InstanceId && Candidate is null,
            _ => false,
        };
        if (!valid) throw new InvalidOperationException("家具请求与当前实例不一致。");
        var layout = new FurnitureLayout(checked(ExpectedVersion + 1), OperationId, Candidate);
        layout.Validate(contract);
        return layout;
    }
}
