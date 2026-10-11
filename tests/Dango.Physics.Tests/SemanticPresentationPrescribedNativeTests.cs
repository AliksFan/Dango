using System.Security.Cryptography;
using System.Text.Json;
using Dango.Physics.Presentation;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class SemanticPresentationPrescribedNativeTests
{
    [TestMethod]
    public void ForeignEpochWrongPhaseAndOldRevisionCannotResetTheLiveReplica()
    {
        var source = FlatTrace(3);
        using var replica = NewReplica();
        var first = replica.Advance(Sample(source[0]));
        var nativeTrace = replica.LastNativeTrace;
        var next = Sample(source[1]);
        Assert.ThrowsExactly<ArgumentException>(() => replica.Advance(next with { Epoch = "foreign" }));
        Assert.ThrowsExactly<ArgumentException>(() => replica.Advance(next with { Tick = next.Tick + .1 }));
        Assert.ThrowsExactly<ArgumentException>(() => replica.Advance(next with { SceneRevision = next.SceneRevision - 1 }));
        Assert.AreSame(nativeTrace, replica.LastNativeTrace);
        Assert.AreSame(first, replica.LastPresentedFrame);
        var second = replica.Advance(next);
        Assert.AreEqual(2ul, replica.LastNativeTrace!.StepOrdinal);
        CollectionAssert.AreEqual(first.Bodies.Select(body => body.LocalBody.EntityId).ToArray(), second.Bodies.Select(body => body.LocalBody.EntityId).ToArray());
        replica.BindEpoch("next");
        Assert.ThrowsExactly<ArgumentException>(() => replica.Advance(Sample(source[2])));
        Assert.IsNull(replica.LastPresentedFrame, "An explicitly new binding cannot resurrect an incoming old epoch.");
    }

    [TestMethod]
    public void FarApartBodiesCannotAcceptAForgedTouchingPair()
    {
        using var replica = NewReplica();
        var falsePair = new SemanticPresentationSample("semantic-fixture", 1,
            [Body(1, -5, 2), Body(2, 5, 2)], [new(new(1), new(2), 1, 0, 0, 1.2)], 2);
        Exception? failure = null;
        try
        {
            failure = ExpectFailure(() => replica.Advance(falsePair));
            Assert.IsNull(replica.LastPresentedFrame);
            Assert.ThrowsExactly<InvalidOperationException>(() => replica.Advance(falsePair with { Tick = 2 }));
        }
        finally { Save("false-far-pair", replica, failure); }
    }

    [TestMethod]
    public void CoherentZeroVelocityDeepOverlapCannotReturnASuccessfulFallback()
    {
        using var replica = NewReplica();
        var impossible = new SemanticPresentationSample("semantic-fixture", 1,
            [Body(1, 0, 2), Body(2, .001, 2)], [new(new(1), new(2), 1, 0, -.8, 1.2)], 2);
        Exception? failure = null;
        try
        {
            failure = ExpectFailure(() => replica.Advance(impossible));
            Assert.IsNotNull(replica.LastNativeTrace, "A native precheck rejection alone cannot satisfy this shape-solve counterexample.");
            Assert.IsTrue(replica.LastNativeTrace.SubstepCount > 0 && replica.LastNativeTrace.ErrorCode != 0,
                "The actual zero-velocity prescribed path must enter native solving before failing.");
            Assert.IsTrue(replica.LastPrescribedInput!.Bodies.All(body => body.Start.CoreVelocityX == 0 && body.Start.CoreVelocityY == 0 &&
                body.End.CoreVelocityX == 0 && body.End.CoreVelocityY == 0 && body.Start.CoreX == body.End.CoreX && body.Start.CoreY == body.End.CoreY));
            Assert.IsNull(replica.LastPresentedFrame);
            Assert.ThrowsExactly<InvalidOperationException>(() => replica.Advance(impossible with { Tick = 2 }));
        }
        finally { Save("deep-overlap-zero-velocity", replica, failure); }
    }

    [TestMethod]
    public void ImpossibleSampleAfterLegalPrefixLatchesAndPreservesTheLastLegalOutput()
    {
        // A healthy stack is the R1 positive gate. Fault preservation must use
        // a genuinely impossible input, rather than require the repaired bug.
        using var replica = NewReplica();
        SemanticPresentedFrame? lastLegal = null;
        Exception? failure = null;
        int? failureTick = null;
        try
        {
            for (var tick = 1; tick <= 96; tick++)
            {
                // At most .05 p per fixed step with zero endpoint velocities:
                // Hermite derivative stays below 4.51 p/s, within original 12.
                // A sufficiently low prescribed core cannot keep the entire
                // bounded radial body above the ground. No instant teleport.
                var sample = new SemanticPresentationSample("semantic-fixture", tick,
                    [Body(1, 0, 2 - Math.Max(0, tick - 3) * .05)], [], 1);
                try { lastLegal = replica.Advance(sample); }
                catch (InvalidOperationException error)
                {
                    failure = error; failureTick = tick;
                    Assert.IsNotNull(lastLegal);
                    Assert.IsTrue(replica.LastNativeTrace is { SubstepCount: > 0, ErrorCode: not 0 },
                        "Fault preservation must follow actual native solving, not a syntax precheck.");
                    Assert.AreSame(lastLegal, replica.LastPresentedFrame);
                    Assert.ThrowsExactly<InvalidOperationException>(() => replica.Advance(sample with { Tick = tick + 1 }));
                    Assert.AreSame(lastLegal, replica.LastPresentedFrame);
                    break;
                }
            }
            Assert.IsNotNull(failure, "An impossible below-floor core must explicitly fail without fallback.");
        }
        finally { Save("below-floor-after-legal-prefix", replica, failure, new { failureTick, lastLegalTick = lastLegal?.Tick }); }
    }

    [TestMethod]
    public void RealSideContactAndLaterSeparationDoNotGrantSupportOrStick()
    {
        var source = FlatTrace(600, moving: true);
        using var replica = NewReplica();
        var sideContacts = 0;
        var separated = false;
        Exception? failure = null;
        try
        {
            foreach (var frame in source)
            {
                var drawn = replica.Advance(Sample(frame));
                foreach (var pair in frame.Pairs.Where(pair => Math.Abs(pair.NormalX) > .7))
                {
                    sideContacts++;
                    Assert.IsFalse(pair.SupportsA || pair.SupportsB);
                    Assert.IsTrue(drawn.Bodies.All(body => body.Authority.SupportEntityId is null));
                }
                if (frame.Tick > 300 && frame.Pairs.Count == 0)
                {
                    var contact = SemanticDrawnGeometry.Contact(drawn.Bodies[0].DrawnContour, drawn.Bodies[1].DrawnContour);
                    if (contact.Gap > .1) separated = true;
                }
            }
            Assert.IsTrue(sideContacts > 0, "The fixture must execute a real horizontal contact.");
            Assert.IsTrue(separated, "The contacted bodies must later separate visibly without attraction.");
            Assert.AreEqual(600ul, replica.LastNativeTrace!.StepOrdinal);
            Assert.AreEqual(0, replica.LastPresentedFrame!.Pairs.Count);
        }
        catch (Exception error) { failure = error; throw; }
        finally { Save("side-contact-and-separation", replica, failure, new { sideContacts, separated, requiredSteps = 600 }); }
    }

    private static SemanticPresentationReplica NewReplica()
    {
        var replica = new SemanticPresentationReplica(PhysicsTestLibraries.Unified);
        replica.BindEpoch("semantic-fixture");
        return replica;
    }

    private static SemanticBodyState Body(int id, double x, double y) => new(new(id), x, y, 0, 0, false, null);

    private static Exception ExpectFailure(Action action)
    {
        try { action(); }
        catch (InvalidOperationException failure) { return failure; }
        Assert.Fail("The native admission must explicitly reject this impossible or contradictory candidate without fallback.");
        throw new InvalidOperationException("Unreachable failed assertion.");
    }

    private static SemanticPresentationSample Sample(DangoAuthorityFrame frame)
    {
        var snapshot = SemanticPresentationNativeTests.Snapshot(frame);
        return new(snapshot.Epoch, snapshot.Tick, snapshot.Bodies, snapshot.Pairs, snapshot.SceneRevision);
    }

    private static IReadOnlyList<DangoAuthorityFrame> FlatTrace(int steps, bool moving = false)
    {
        using var authority = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, capacity: 2);
        authority.EnableAuthority();
        var first = authority.Add(-1.4, 0);
        var second = authority.Add(1.4, 0);
        var frames = new List<DangoAuthorityFrame>();
        for (var tick = 0; tick < steps; tick++)
        {
            var horizontal = !moving || tick < 60 || tick >= 480 ? 0 : tick < 240 ? 1 : -1;
            Assert.IsTrue(authority.Step(SemanticPresentationSampler.FixedStep,
                [new(first, Horizontal: horizontal), new(second)]).Succeeded);
            frames.Add(authority.ReadAuthorityFrame());
        }
        return frames.AsReadOnly();
    }

    private static void Save(string name, SemanticPresentationReplica replica, Exception? failure, object? observations = null)
    {
        var evidenceName = Environment.GetEnvironmentVariable("DANGO_SEMANTIC_EVIDENCE_NAME") ?? "semantic-prescribed";
        if (evidenceName is not ("semantic-prescribed" or "semantic-prescribed-final" or "semantic-prescribed-final2" or "semantic-r1"))
            throw new ArgumentException("Counterexamples write only their named isolated evidence directories.");
        var directory = Path.Combine(PhysicsTestLibraries.Root, "artifacts", evidenceName == "semantic-r1" ? "p1-r1" : "p1-followup", $"{evidenceName}-counterexamples");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{name}.json"), JsonSerializer.Serialize(new
        {
            name, moduleSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PhysicsTestLibraries.Unified))),
            failure = failure?.Message, failureType = failure?.GetType().Name,
            nativeErrorCode = (failure as DangoPhysicsException)?.NativeErrorCode,
            prescribedInput = replica.LastPrescribedInput, nativeTrace = replica.LastNativeTrace,
            lastLegal = replica.LastPresentedFrame, failedDrawn = replica.LastFailedPresentedFrame, observations,
            scope = "Isolated admission counterexample or side-contact fixture; no UI, network, or manual acceptance.",
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
