using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Tests.Physics.Presentation.Attachments.Reference;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class HatAllocationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void FrozenFullResultPathFailsTheScalarAllocationBudget()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var old = FrozenHatTrackSolver.CreateGeometryGuide(shape)!;
        var current = HatTrackSolver.CreateGeometryGuide(shape)!;
        double sink = 0;
        var oldBytes = Measure(() => sink += old.Fit(0)!.Center.Y, 512);
        var newBytes = Measure(() => { if (current.TryFitScalars(0, out var pose)) sink += pose.Center.Y; }, 512);
        Assert.IsTrue(sink > 0);
        Assert.IsTrue(oldBytes > 512 * 1024, "The frozen implementation must defeat the zero-per-call allocation gate.");
        Assert.AreEqual(0L, newBytes);
        TestContext.WriteLine($"Frozen full-result scalar use: {oldBytes} bytes; new scalar use: {newBytes} bytes/512 calls.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void LegalityAndScalarEvaluationDoNotAllocatePerCall(bool strict)
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var track = (strict ? HatTrackSolver.Create(shape) : HatTrackSolver.CreateGeometryGuide(shape))!;
        var positions = new[] { track.Minimum, 0, track.Maximum, double.NaN, Math.BitDecrement(track.Minimum), Math.BitIncrement(track.Maximum) };
        var cursor = 0;
        var accepted = 0;
        Action scalar = () => { if (track.TryFitScalars(positions[cursor++ % positions.Length], out _)) accepted++; };
        Action legality = () => { if (track.IsLegal(positions[cursor++ % positions.Length])) accepted++; };
        var scalarBytes = Measure(scalar, 512);
        var legalityBytes = Measure(legality, 512);
        Assert.IsTrue(accepted > 0);
        TestContext.WriteLine($"strict={strict}: scalar={scalarBytes}, legality={legalityBytes} bytes/512 calls.");
        Assert.AreEqual(0L, scalarBytes);
        Assert.AreEqual(0L, legalityBytes);
    }

    [TestMethod]
    public void ScalarAndLegalityPathsMatchCompleteResultsIncludingRejection()
    {
        foreach (var width in new[] { 0.1, 0.7 })
            foreach (var strict in new[] { false, true })
            {
                var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(width: width, shear: 0.12));
                var track = strict ? HatTrackSolver.Create(shape) : HatTrackSolver.CreateGeometryGuide(shape);
                if (track is null) continue;
                var positions = Enumerable.Range(0, 321).Select(i => track.Minimum + (track.Maximum - track.Minimum) * i / 320)
                    .Concat([double.NaN, double.PositiveInfinity, double.NegativeInfinity, Math.BitDecrement(track.Minimum), Math.BitIncrement(track.Maximum)]);
                foreach (var s in positions)
                {
                    var complete = track.Fit(s);
                    Assert.AreEqual(complete is not null, track.IsLegal(s));
                    Assert.AreEqual(complete is not null, track.TryFitScalars(s, out var scalar));
                    if (complete is null) continue;
                    Assert.AreEqual(complete.Center, scalar.Center);
                    Assert.AreEqual(complete.Angle, scalar.Angle);
                    Assert.AreEqual(complete.S, scalar.S);
                    Assert.AreEqual(complete.ContactWidth, scalar.ContactWidth);
                    Assert.AreEqual(complete.MaximumGap, scalar.MaximumGap);
                    Assert.AreEqual(complete.MaximumPenetration, scalar.MaximumPenetration);
                }
            }
    }

    [TestMethod]
    public void AvailableLiftDoesNotAllocatePerCall()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var placement = HatPlacementSolver.Create(shape, new(new(0, 0.8), 0))!;
        double sum = 0;
        var bytes = Measure(() => sum += placement.AvailableLift(0), 512);
        Assert.IsTrue(sum > 0);
        Assert.AreEqual(0L, bytes);
    }

    [TestMethod]
    public void CompleteCreateAndRemapStayWithinScratchReuseBudget()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval());
        var placement = HatPlacementSolver.Create(shape, new(new(0, 0.8), 0))!;
        Action create = () => GC.KeepAlive(HatPlacementSolver.Create(shape, new(new(0, 0.8), 0)));
        Action remap = () => GC.KeepAlive(placement.Remap(shape));
        var created = Measure(create, 8) / 8;
        var remapped = Measure(remap, 8) / 8;
        TestContext.WriteLine($"Create: {created} bytes/call; Remap: {remapped} bytes/call.");
        // Includes the new solver, crown/length arrays and legal-run collection.
        // Excludes shape construction, which is unchanged by this optimization.
        Assert.IsTrue(created <= 64 * 1024, $"Create allocated {created} bytes/call.");
        Assert.IsTrue(remapped <= 64 * 1024, $"Remap allocated {remapped} bytes/call.");
    }

    private static long Measure(Action action, int count)
    {
        for (var i = 0; i < 16; i++) action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++) action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
