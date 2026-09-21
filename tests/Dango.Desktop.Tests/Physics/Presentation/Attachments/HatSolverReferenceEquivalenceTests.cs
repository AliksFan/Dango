using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Desktop.Tests.Physics.Presentation.Attachments.Reference;

namespace Dango.Desktop.Tests.Physics.Presentation.Attachments;

[TestClass]
public sealed class HatSolverReferenceEquivalenceTests
{
    private const double Tolerance = 1e-10;

    [TestMethod]
    public void TrackCreateGuideAndFitMatchFrozen75de095AcrossCrownFixtures()
    {
        var fixtures = new[]
        {
            HatTrackGeometryTests.FlatTop(),
            AttachmentShapeGeometryTests.Oval(),
            AttachmentShapeGeometryTests.Oval(shear: 0.16),
            AttachmentShapeGeometryTests.Oval(width: 0.1), // strict rejects while the visual guide remains usable.
            AttachmentShapeGeometryTests.Oval(width: 0.015),
            AttachmentShapeGeometryTests.Oval(height: 0.6, shear: -0.08)
        };
        foreach (var points in fixtures)
        {
            var shape = AttachmentShapeGeometry.Create(points);
            CompareTrack(HatTrackSolver.Create(shape), FrozenHatTrackSolver.Create(shape));
            CompareTrack(HatTrackSolver.CreateGeometryGuide(shape), FrozenHatTrackSolver.CreateGeometryGuide(shape));
        }
    }

    [TestMethod]
    public void NarrowPointOneFixtureKeepsStrictAndVisualGuideDistinct()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(width: 0.1));
        Assert.IsNull(HatTrackSolver.Create(shape));
        Assert.IsNull(FrozenHatTrackSolver.Create(shape));
        Assert.IsNotNull(HatTrackSolver.CreateGeometryGuide(shape));
        Assert.IsNotNull(FrozenHatTrackSolver.CreateGeometryGuide(shape));
        var root = new PresentationAttachment(new(0, 0.8), 0);
        Assert.IsNotNull(HatPlacementSolver.Create(shape, root));
        Assert.IsNotNull(FrozenHatPlacementSolver.Create(shape, root));
    }

    [TestMethod]
    public void TrackIsEquivalentForContourRotationsEndpointsAndAdjacentDoubles()
    {
        var contour = AttachmentShapeGeometryTests.Oval(shear: 0.16);
        for (var rotation = 0; rotation < contour.Length; rotation++)
        {
            var shape = AttachmentShapeGeometry.Create(contour.Skip(rotation).Concat(contour.Take(rotation)).ToArray());
            var actual = HatTrackSolver.CreateGeometryGuide(shape)!;
            var expected = FrozenHatTrackSolver.CreateGeometryGuide(shape)!;
            AssertTrack(actual, expected);
            foreach (var s in ValuesAround(actual.Minimum, actual.Maximum, 0d)) AssertPose(actual.Fit(s), expected.Fit(s));
        }
    }

    [TestMethod]
    public void Canonical321PointEligibilityScanAndPlacement160StepLimitsMatchFrozenBaseline()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(shear: 0.16));
        foreach (var pair in new[]
        {
            (HatTrackSolver.Create(shape), FrozenHatTrackSolver.Create(shape)),
            (HatTrackSolver.CreateGeometryGuide(shape), FrozenHatTrackSolver.CreateGeometryGuide(shape))
        })
        {
            Assert.IsNotNull(pair.Item1); Assert.IsNotNull(pair.Item2);
            for (var i = 0; i <= 320; i++)
            {
                var s = pair.Item1.Minimum + (pair.Item1.Maximum - pair.Item1.Minimum) * i / 320d;
                AssertPose(pair.Item1.Fit(s), pair.Item2.Fit(s));
            }
        }
        var reference = new PresentationAttachment(new(0.04, 0.75), 0.2);
        var actualPlacement = HatPlacementSolver.Create(shape, reference)!;
        var expectedPlacement = FrozenHatPlacementSolver.Create(shape, reference)!;
        for (var i = 0; i <= 160; i++)
        {
            var s = actualPlacement.Minimum + (actualPlacement.Maximum - actualPlacement.Minimum) * i / 160d;
            AssertVisualPose(actualPlacement.Fit(s), expectedPlacement.Fit(s));
            Assert.AreEqual(expectedPlacement.AvailableLift(s), actualPlacement.AvailableLift(s), Tolerance);
        }
    }

    [TestMethod]
    public void PublicTrackResultsOwnTheirSamplesAndDoNotCrossTalkAcrossConcurrentFits()
    {
        var shape = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(shear: 0.16));
        var track = HatTrackSolver.CreateGeometryGuide(shape)!;
        var saved = track.Fit(0)!;
        var contact = saved.ContactSamples.ToArray(); var fitting = saved.FittingSamples.ToArray();
        var positions = Enumerable.Range(0, 33).Select(i => track.Minimum + (track.Maximum - track.Minimum) * i / 32d).ToArray();
        var results = Task.WhenAll(positions.Select(s => Task.Run(() => track.Fit(s)))).GetAwaiter().GetResult();
        Assert.AreEqual(positions.Length, results.Length);
        CollectionAssert.AreEqual(contact, saved.ContactSamples.ToArray());
        CollectionAssert.AreEqual(fitting, saved.FittingSamples.ToArray());
        var reference = FrozenHatTrackSolver.CreateGeometryGuide(shape)!;
        for (var i = 0; i < positions.Length; i++) AssertPose(results[i], reference.Fit(positions[i]));
    }

    [TestMethod]
    public void PlacementCreateFitLiftAndRemapMatchFrozen75de095()
    {
        var source = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(shear: 0.16));
        var deformed = AttachmentShapeGeometry.Create(AttachmentShapeGeometryTests.Oval(height: 0.6, shear: -0.08));
        var references = new[]
        {
            new PresentationAttachment(new(0, 0.8), 0),
            new PresentationAttachment(new(0.04, 0.75), 0.2),
            new PresentationAttachment(new(-0.03, 0.6), -0.25),
            new PresentationAttachment(new(0.3, 0.3), 0.7)
        };
        foreach (var root in references) ComparePlacement(HatPlacementSolver.Create(source, root), FrozenHatPlacementSolver.Create(source, root), deformed);
        var referenceRoot = references[1];
        var actual = HatPlacementSolver.Create(source, referenceRoot)!;
        var expected = FrozenHatPlacementSolver.Create(source, referenceRoot)!;
        ComparePlacementResult(actual.Remap(deformed)?.Remap(source), expected.Remap(deformed)?.Remap(source));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PresentationAttachment(new(0, 0.8), double.NaN));
    }

    private static void CompareTrack(HatTrackSolver? actual, FrozenHatTrackSolver? expected)
    {
        Assert.AreEqual(expected is null, actual is null);
        if (actual is null || expected is null) return;
        AssertTrack(actual, expected);
        foreach (var s in ValuesAround(actual.Minimum, actual.Maximum, 0d)) AssertPose(actual.Fit(s), expected.Fit(s));
    }

    private static void AssertTrack(HatTrackSolver actual, FrozenHatTrackSolver expected)
    {
        Assert.AreEqual(expected.Minimum, actual.Minimum); Assert.AreEqual(expected.Maximum, actual.Maximum);
        Assert.AreEqual(expected.Track.Count, actual.Track.Count);
        for (var i = 0; i < actual.Track.Count; i++) AssertPoint(actual.Track[i], expected.Track[i]);
    }

    private static void ComparePlacement(HatPlacementSolver? actual, FrozenHatPlacementSolver? expected, AttachmentShapeGeometry remap)
    {
        Assert.AreEqual(expected is null, actual is null);
        if (actual is null || expected is null) return;
        Assert.AreEqual(expected.Minimum, actual.Minimum); Assert.AreEqual(expected.Maximum, actual.Maximum);
        Assert.AreEqual(expected.Guide.Count, actual.Guide.Count);
        for (var i = 0; i < actual.Guide.Count; i++) AssertPoint(actual.Guide[i], expected.Guide[i]);
        foreach (var s in ValuesAround(actual.Minimum, actual.Maximum, 0d))
        {
            AssertVisualPose(actual.Fit(s), expected.Fit(s));
            Assert.AreEqual(expected.AvailableLift(s), actual.AvailableLift(s), Tolerance);
        }
        ComparePlacementResult(actual.Remap(remap), expected.Remap(remap));
    }

    private static void ComparePlacementResult(HatPlacementSolver? actual, FrozenHatPlacementSolver? expected)
    {
        Assert.AreEqual(expected is null, actual is null);
        if (actual is null || expected is null) return;
        Assert.AreEqual(expected.Minimum, actual.Minimum, Tolerance); Assert.AreEqual(expected.Maximum, actual.Maximum, Tolerance);
        foreach (var s in ValuesAround(actual.Minimum, actual.Maximum, 0d))
        {
            AssertVisualPose(actual.Fit(s), expected.Fit(s));
            Assert.AreEqual(expected.AvailableLift(s), actual.AvailableLift(s), Tolerance);
        }
    }

    private static IEnumerable<double> ValuesAround(double minimum, double maximum, double neutral)
    {
        yield return double.NaN; yield return double.PositiveInfinity; yield return double.NegativeInfinity; yield return -0d;
        foreach (var value in new[] { minimum, maximum, neutral })
        {
            yield return Math.BitDecrement(value); yield return value; yield return Math.BitIncrement(value);
        }
        yield return minimum - 0.01; yield return maximum + 0.01;
    }

    private static void AssertPose(HatContactPose? actual, FrozenHatContactPose? expected)
    {
        Assert.AreEqual(expected is null, actual is null);
        if (actual is null || expected is null) return;
        AssertPoint(actual.Center, expected.Center); Assert.AreEqual(expected.Angle, actual.Angle, Tolerance); Assert.AreEqual(expected.S, actual.S, Tolerance);
        Assert.AreEqual(expected.ContactWidth, actual.ContactWidth, 1e-12); Assert.AreEqual(expected.MaximumGap, actual.MaximumGap, Tolerance); Assert.AreEqual(expected.MaximumPenetration, actual.MaximumPenetration, Tolerance);
        Assert.AreEqual(expected.ContactSamples.Count, actual.ContactSamples.Count); Assert.AreEqual(expected.FittingSamples.Count, actual.FittingSamples.Count);
        // ContactSamples contains the ordered 65-point footprint plus any interior contour vertices.
        Assert.IsTrue(actual.ContactSamples.Count >= 65); Assert.AreEqual(9, actual.FittingSamples.Count);
        for (var i = 0; i < actual.ContactSamples.Count; i++) AssertPoint(actual.ContactSamples[i], expected.ContactSamples[i]);
        for (var i = 0; i < actual.FittingSamples.Count; i++) AssertPoint(actual.FittingSamples[i], expected.FittingSamples[i]);
    }

    private static void AssertVisualPose(HatVisualPose? actual, FrozenHatVisualPose? expected)
    {
        Assert.AreEqual(expected is null, actual is null);
        if (actual is null || expected is null) return;
        AssertPoint(actual.Center, expected.Center); Assert.AreEqual(expected.Angle, actual.Angle, Tolerance); Assert.AreEqual(expected.S, actual.S, Tolerance);
    }

    private static void AssertPoint(AttachmentPoint actual, AttachmentPoint expected)
    {
        Assert.AreEqual(expected.X, actual.X, Tolerance); Assert.AreEqual(expected.Y, actual.Y, Tolerance);
    }
}
