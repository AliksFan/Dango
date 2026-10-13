namespace Dango.Core.Furniture;

/// <summary>One confirmed ordinary-room item edit; dynamic poses are not part of this request.</summary>
public sealed record RoomFurnitureEditOperation(
    Guid OperationId,
    long ExpectedVersion,
    FurnitureEditKind Kind,
    Guid? TargetInstanceId,
    FurnitureItemInstance? Candidate)
{
    public FurnitureLayout BuildCandidate(FurnitureLayout baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var contract = FurnitureLayoutContract.OrdinaryRoom;
        baseline.Validate(contract);
        if (OperationId == Guid.Empty || ExpectedVersion < 0 || baseline.LayoutVersion != ExpectedVersion ||
            baseline.LastOperationId == OperationId)
            throw new InvalidOperationException("布局版本已变化或请求标识不可用。");
        var target = TargetInstanceId is { } id ? baseline.Find(id) : null;
        var valid = Kind switch
        {
            FurnitureEditKind.Insert => TargetInstanceId is null && Candidate is not null && baseline.Find(Candidate.InstanceId) is null,
            FurnitureEditKind.Move => target is not null && Candidate is not null && Candidate.InstanceId == target.InstanceId && Candidate.Kind == target.Kind,
            FurnitureEditKind.Delete => target is not null && Candidate is null,
            _ => false,
        };
        if (!valid) throw new InvalidOperationException("家具请求与当前实例不一致。");
        Candidate?.Validate(contract);
        // A ball may have rolled away from its saved anchor: confirming that
        // anchor again still places the live ball there and clears its motion.
        if (Kind == FurnitureEditKind.Move && target!.Kind == FurnitureItemKind.StaticPlatform && Candidate == target) return baseline;
        var items = Kind switch
        {
            FurnitureEditKind.Insert => baseline.Items.Append(Candidate!),
            FurnitureEditKind.Move => baseline.Items.Select(item => item.InstanceId == TargetInstanceId ? Candidate! : item),
            _ => baseline.Items.Where(item => item.InstanceId != TargetInstanceId),
        };
        var result = FurnitureLayout.ForItems(checked(ExpectedVersion + 1), OperationId, items);
        result.Validate(contract);
        return result;
    }
}
