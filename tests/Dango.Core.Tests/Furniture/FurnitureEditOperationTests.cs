using Dango.Core.Furniture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Furniture;

[TestClass]
public sealed class FurnitureEditOperationTests
{
    private static readonly Guid Instance = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid Previous = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid Operation = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private static readonly Guid Other = Guid.Parse("40000000-0000-4000-8000-000000000004");
    private static FurniturePlatformInstance Platform => new(Instance, LocalFurnitureSceneGeometry.DefaultAnchor);
    private static FurnitureLayout Baseline => new(1, Previous, Platform);

    [TestMethod]
    public void ValidInsertMoveAndDeleteBuildExactlyOneNewVersionWithoutMutatingBaseline()
    {
        var insert = new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Insert, null, Platform);
        var inserted = insert.BuildCandidate(FurnitureLayout.Empty);
        Assert.AreEqual(new FurnitureLayout(1, Operation, Platform), inserted);
        Assert.AreEqual(FurnitureLayout.Empty, new FurnitureLayout(0, null, null));

        var moved = Platform with { Anchor = new(1, 2.5) };
        var move = new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Move, Instance, moved);
        Assert.AreEqual(new FurnitureLayout(2, Operation, moved), move.BuildCandidate(Baseline));
        Assert.AreEqual(Platform, Baseline.Platform);

        var delete = new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Delete, Instance, null);
        Assert.AreEqual(new FurnitureLayout(2, Operation, null), delete.BuildCandidate(Baseline));
        Assert.AreEqual(Platform, Baseline.Platform);
    }

    [TestMethod]
    [DynamicData(nameof(InvalidRequests))]
    public void WrongKindTargetOrContentsCannotProduceAValidCandidate(FurnitureEditOperation operation, FurnitureLayout baseline)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => operation.BuildCandidate(baseline));
    }

    public static IEnumerable<object[]> InvalidRequests()
    {
        yield return [new FurnitureEditOperation(Guid.Empty, 1, FurnitureEditKind.Delete, Instance, null), Baseline];
        yield return [new FurnitureEditOperation(Operation, -1, FurnitureEditKind.Delete, Instance, null), Baseline];
        yield return [new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Delete, Instance, null), Baseline];
        yield return [new FurnitureEditOperation(Operation, 2, FurnitureEditKind.Delete, Instance, null), Baseline];
        yield return [new FurnitureEditOperation(Operation, 1, (FurnitureEditKind)100, Instance, null), Baseline];
        yield return [new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Insert, Instance, Platform), FurnitureLayout.Empty];
        yield return [new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Insert, Guid.Empty, Platform), FurnitureLayout.Empty];
        yield return [new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Insert, null, null), FurnitureLayout.Empty];
        yield return [new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Insert, null, Platform), Baseline];
        yield return [new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Move, Instance, Platform), FurnitureLayout.Empty];
        yield return [new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Delete, Instance, null), FurnitureLayout.Empty];
        foreach (var target in new Guid?[] { null, Guid.Empty, Other })
        {
            yield return [new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Move, target, Platform), Baseline];
            yield return [new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Delete, target, null), Baseline];
        }
        yield return [new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Move, Instance, null), Baseline];
        yield return [new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Move, Instance, Platform with { InstanceId = Other }), Baseline];
        yield return [new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Delete, Instance, Platform), Baseline];
    }

    [TestMethod]
    public void RestartedLastOperationAndStaleOldRequestCannotBeResubmittedAsNew()
    {
        var original = new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Insert, null, Platform);
        var persisted = FurnitureLayoutSerializer.Deserialize(FurnitureLayoutSerializer.Serialize(original.BuildCandidate(FurnitureLayout.Empty)));
        Assert.ThrowsExactly<InvalidOperationException>(() => original.BuildCandidate(persisted));
        // Altering the expected version cannot turn the known old ID into a new operation.
        Assert.ThrowsExactly<InvalidOperationException>(() => (original with { ExpectedVersion = 1, Kind = FurnitureEditKind.Move, TargetInstanceId = Instance })
            .BuildCandidate(persisted));
        var later = new FurnitureLayout(9, Other, Platform);
        Assert.ThrowsExactly<InvalidOperationException>(() => original.BuildCandidate(later));
    }

    [TestMethod]
    public void CompleteRequestEqualityDistinguishesChangesUnderTheSameOperationId()
    {
        var original = new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Move, Instance, Platform);
        Assert.AreEqual(original, original with { Candidate = new(Instance, LocalFurnitureSceneGeometry.DefaultAnchor) });
        foreach (var changed in new[]
        {
            original with { ExpectedVersion = 0 }, original with { Kind = FurnitureEditKind.Delete, Candidate = null },
            original with { TargetInstanceId = Other }, original with { Candidate = Platform with { Anchor = new(1, 2) } },
        })
        {
            Assert.AreEqual(original.OperationId, changed.OperationId);
            Assert.AreNotEqual(original, changed);
        }
    }

    [TestMethod]
    public void VersionOverflowNeverWrapsToZeroOrProducesACandidate()
    {
        var baseline = new FurnitureLayout(long.MaxValue, Previous, Platform);
        var operation = new FurnitureEditOperation(Operation, long.MaxValue, FurnitureEditKind.Delete, Instance, null);
        Assert.ThrowsExactly<OverflowException>(() => operation.BuildCandidate(baseline));
        Assert.AreEqual(long.MaxValue, baseline.LayoutVersion);
        Assert.AreEqual(Platform, baseline.Platform);
    }

    [TestMethod]
    public void EmptyCandidateGuidAndNonFiniteAnchorAreRejectedEvenOnInsert()
    {
        foreach (var candidate in new[]
        {
            Platform with { InstanceId = Guid.Empty },
            Platform with { Anchor = new(double.NaN, 2) },
            Platform with { Anchor = new(0, double.PositiveInfinity) },
        })
            Assert.ThrowsExactly<InvalidDataException>(() =>
                new FurnitureEditOperation(Operation, 0, FurnitureEditKind.Insert, null, candidate).BuildCandidate(FurnitureLayout.Empty));
    }

    [TestMethod]
    public void InvalidBaselineCannotBeLaunderedByDeletingItsInvalidFurniture()
    {
        var invalid = new FurnitureLayout(1, Previous, Platform with { InstanceId = Guid.Empty });
        var delete = new FurnitureEditOperation(Operation, 1, FurnitureEditKind.Delete, Guid.Empty, null);
        Assert.ThrowsExactly<InvalidDataException>(() => delete.BuildCandidate(invalid));
        Assert.ThrowsExactly<ArgumentNullException>(() => delete.BuildCandidate(null!));
    }
}
