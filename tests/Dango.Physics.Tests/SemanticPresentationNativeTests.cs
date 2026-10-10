using System.Text.Json;
using System.Security.Cryptography;
using Dango.Physics.Presentation;

namespace Dango.Physics.Tests;

[TestClass]
public sealed class SemanticPresentationNativeTests
{
    public TestContext TestContext { get; set; } = null!;
    private static readonly TimeSpan FixedStep = SemanticPresentationSampler.FixedStep;

    [TestMethod]
    [DataRow(30, "stack")]
    [DataRow(60, "stack")]
    [DataRow(144, "stack")]
    [DataRow(30, "push")]
    [DataRow(60, "push")]
    [DataRow(144, "push")]
    [DataRow(30, "withdraw")]
    [DataRow(60, "withdraw")]
    [DataRow(144, "withdraw")]
    public void TwentyHertzAuthorityMustMeetFrozenDrawnContactGate(int renderRate, string scenario)
    {
        var evidenceName = Environment.GetEnvironmentVariable("DANGO_SEMANTIC_EVIDENCE_NAME") ?? "semantic-prescribed";
        if (evidenceName is not ("semantic-prescribed" or "semantic-prescribed-final" or "semantic-prescribed-final2"))
            throw new ArgumentException("The semantic probe writes only its named isolated evidence directories.");
        var moduleSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PhysicsTestLibraries.Unified)));
        var trace = BuildTrace(scenario);
        var published = trace.Where((_, index) => index % 3 == 0 || index == trace.Count - 1).Select(Snapshot).ToArray();
        var sampler = new SemanticPresentationSampler();
        sampler.BindEpoch("semantic-fixture");
        sampler.Push(published[0]);
        var cursor = 1;
        var latestTick = published[0].Tick;
        var diagnostics = new List<object>();
        var commonErrors = new List<double>();
        var impulseErrors = new List<double>();
        SemanticPresentationGateResult? firstFailedGate = null;
        object? firstFailure = null;
        string? runtimeFailure = null;
        var supportedSteps = 0;
        var renderedSteps = 0;
        DangoEntityId? survivorLocalId = null;
        double? withdrawalHeight = null;
        SemanticPresentedBody? lastSurvivor = null;
        var withdrawalObserved = false;
        var finalTick = published[^1].Tick;
        using var replica = new SemanticPresentationReplica(PhysicsTestLibraries.Unified);
        replica.BindEpoch("semantic-fixture");
        try
        {
            var nextFixedTick = 1;
            for (var render = 1; render <= finalTick * renderRate / 60 + 1; render++)
            {
                var requiredTick = Math.Min(finalTick, render * 60L / renderRate);
                while (nextFixedTick <= requiredTick)
                {
                    while (latestTick < nextFixedTick)
                    {
                        var next = published[cursor++];
                        sampler.Push(next);
                        latestTick = next.Tick;
                    }
                    var sample = sampler.Sample(nextFixedTick);
                    var oracle = trace[nextFixedTick - 1];
                    var reconstruction = sample.Bodies.Select(body =>
                    {
                        var truth = oracle.Bodies.SingleOrDefault(candidate => candidate.EntityId == body.EntityId);
                        return truth is null ? 0 : Math.Max(Math.Abs(body.CoreX - truth.CoreX), Math.Abs(body.CoreY - truth.CoreY));
                    }).DefaultIfEmpty().Max();
                    var transition = IsTransitionWindow(trace, nextFixedTick - 1);
                    (transition ? impulseErrors : commonErrors).Add(reconstruction);
                    SemanticPresentedFrame drawn;
                    try { drawn = replica.Advance(sample); }
                    catch (Exception error) when (error is DangoPhysicsException or InvalidOperationException or ArgumentException)
                    {
                        runtimeFailure = error.Message;
                        if (replica.LastFailedPresentedFrame is { } failed) firstFailedGate = SemanticPresentationGate.Evaluate(failed);
                        firstFailure = new { sample, oracle, prescribedInput = replica.LastPrescribedInput, nativeTrace = replica.LastNativeTrace,
                            localFrame = replica.LastLocalFrame, failedDrawn = replica.LastFailedPresentedFrame, firstFailedGate, runtimeFailure };
                        break;
                    }
                    renderedSteps++;
                    if (drawn.Bodies.Any(body => body.Authority.SupportEntityId is not null)) supportedSteps++;
                    var gate = SemanticPresentationGate.Evaluate(drawn);
                    if (scenario == "withdraw")
                    {
                        var survivor = drawn.Bodies.Single(body => body.Authority.EntityId.Value == 2);
                        survivorLocalId ??= survivor.LocalBody.EntityId;
                        Assert.AreEqual(survivorLocalId.Value, survivor.LocalBody.EntityId, "Removing the supporting body cannot recreate its survivor.");
                        lastSurvivor = survivor;
                        if (drawn.Bodies.Count == 1)
                        {
                            withdrawalObserved = true;
                            withdrawalHeight ??= survivor.Authority.CoreY;
                            Assert.IsNull(survivor.Authority.SupportEntityId);
                            Assert.AreEqual(0, drawn.Pairs.Count);
                            Assert.AreEqual(0, replica.LastPrescribedInput!.StartPairs.Count);
                            Assert.AreEqual(0, replica.LastPrescribedInput.EndPairs.Count);
                            Assert.IsNull(replica.LastPrescribedInput.Bodies[0].StartSupportEntityId);
                            Assert.IsNull(replica.LastPrescribedInput.Bodies[0].EndSupportEntityId);
                        }
                    }
                    diagnostics.Add(new
                    {
                        tick = nextFixedTick, sample = sample.Bodies, oracle = oracle.Bodies,
                        localCenters = drawn.Bodies.Select(body => new { body.Authority.EntityId, body.LocalBody.CoreX, body.LocalBody.CoreY }),
                        drawn.StepStartTick, drawn.LocalContourTick, prescribedInput = replica.LastPrescribedInput,
                        nativeTrace = replica.LastNativeTrace, gate, reconstruction, transition,
                    });
                    if (!gate.Passed)
                    {
                        firstFailedGate = gate;
                        firstFailure = new { sample, oracle, prescribedInput = replica.LastPrescribedInput, nativeTrace = replica.LastNativeTrace, drawn, gate };
                        break;
                    }
                    nextFixedTick++;
                }
                if (firstFailure is not null) break;
            }
            Assert.IsNull(runtimeFailure, "A local native fault or unsafe shape blocks presentation admission.");
            Assert.IsNull(firstFailedGate, "The frozen actual-drawn gate failed; evidence is retained without seam or center correction.");
            Assert.AreEqual(finalTick, renderedSteps, "Every fixed step must be rendered without an unreported skipped gate.");
            Assert.AreEqual(600L, finalTick, "The final source Tick must be sampled as well as the ordinary 20 Hz snapshots.");
            if (scenario == "withdraw")
            {
                Assert.IsTrue(withdrawalObserved && lastSurvivor is not null && withdrawalHeight is not null);
                Assert.IsTrue(lastSurvivor!.Authority.CoreY < withdrawalHeight - .2, "The real surviving core must descend after support withdrawal.");
                Assert.IsTrue(lastSurvivor.Authority.FloorGrounded && lastSurvivor.LocalBody.GroundContactCount > 0,
                    "The remaining body must actually reach the floor before this full trajectory passes.");
            }
            if (scenario != "push") Assert.IsTrue(supportedSteps > 30, "A stack test must contain actual authoritative support.");
            var p99 = Percentile99(commonErrors);
            Assert.IsTrue(p99 <= .02 && commonErrors.DefaultIfEmpty().Max() <= .06,
                $"Frozen ordinary reconstruction budget failed: p99={p99:R}, max={commonErrors.DefaultIfEmpty().Max():R}.");
        }
        finally
        {
            var directory = Path.Combine(PhysicsTestLibraries.Root, "artifacts", "p1-followup", evidenceName);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{scenario}-{renderRate}hz.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                scenario, renderRate, stateRate = 20, fixedRate = 60, moduleSha256,
                pipeline = "T-1/T shared Hermite endpoints and derivative -> prescribed native core and local nodes -> contour and authority center at T",
                requiredFixedSteps = finalTick, completedFullTrajectory = renderedSteps == finalTick,
                observedReconstructionPrefixWithinBudget = commonErrors.Count > 0 && Percentile99(commonErrors) <= .02 && commonErrors.Max() <= .06,
                displayGatePassed = firstFailure is null && renderedSteps == finalTick,
                reconstructionGatePassed = renderedSteps == finalTick && commonErrors.Count > 0 && Percentile99(commonErrors) <= .02 && commonErrors.Max() <= .06,
                overallGatePassed = firstFailure is null && renderedSteps == finalTick && commonErrors.Count > 0 &&
                    Percentile99(commonErrors) <= .02 && commonErrors.Max() <= .06 && (scenario == "push" || supportedSteps > 30),
                renderedSteps, supportedSteps, withdrawalObserved, survivorLocalId, withdrawalHeight,
                withdrawalFinalHeight = lastSurvivor?.Authority.CoreY, runtimeFailure, firstFailedGate,
                normalReconstructionP99 = Percentile99(commonErrors),
                normalReconstructionMaximum = commonErrors.DefaultIfEmpty().Max(),
                impulseWindowReconstructionMaximum = impulseErrors.DefaultIfEmpty().Max(),
                firstFailure, diagnostics, authoritativeSemanticTrace = trace,
                scope = "Isolated native presentation experiment; no network, WPF, manual or multiplayer acceptance.",
            }, new JsonSerializerOptions { WriteIndented = true }));
            TestContext.WriteLine(path);
        }
    }

    internal static IReadOnlyList<DangoAuthorityFrame> BuildTrace(string scenario)
    {
        using var authority = DangoPhysicsWorld.Create(PhysicsTestLibraries.Unified, capacity: 2);
        authority.EnableAuthority();
        var lower = authority.Add(0, 0);
        var upper = authority.Add(scenario == "push" ? 2.8 : 0, scenario == "push" ? 0 : 1.65);
        var trace = new List<DangoAuthorityFrame>();
        for (var tick = 0; tick < 600; tick++)
        {
            if (scenario == "withdraw" && tick == 180) authority.Remove(lower);
            var controls = scenario == "withdraw" && tick >= 180
                ? new[] { new DangoPhysicsInput(upper) }
                : new[] { new DangoPhysicsInput(lower, Horizontal: scenario == "push" && tick >= 90 ? 1 : 0), new DangoPhysicsInput(upper) };
            Assert.IsTrue(authority.Step(FixedStep, controls).Succeeded, "The source authority fixture itself must stay healthy.");
            var frame = authority.ReadAuthorityFrame();
            Assert.IsTrue(frame.Succeeded);
            trace.Add(frame);
        }
        return trace.AsReadOnly();
    }

    internal static SemanticPresentationSnapshot Snapshot(DangoAuthorityFrame frame) => new("semantic-fixture", checked((long)frame.Tick),
        frame.Bodies.Select(body => new SemanticBodyState(body.EntityId, body.CoreX, body.CoreY, body.CoreVelocityX, body.CoreVelocityY,
            body.FloorGrounded, body.SupportEntityId)),
        frame.Pairs.Select(pair => new SemanticPairState(pair.EntityA, pair.EntityB, pair.NormalX, pair.NormalY, pair.Gap, pair.MinimumProjectedWidth)),
        sceneRevision: frame.SceneRevision);

    private static bool IsTransitionWindow(IReadOnlyList<DangoAuthorityFrame> trace, int index)
    {
        for (var step = Math.Max(1, index - 3); step <= Math.Min(trace.Count - 1, index + 3); step++)
        {
            var before = trace[step - 1]; var after = trace[step];
            if (before.Bodies.Count != after.Bodies.Count || after.Bodies.Any(body => body.JumpResult is
                DangoAuthorityJumpResult.AppliedFloor or DangoAuthorityJumpResult.AppliedSupport)) return true;
            foreach (var body in after.Bodies)
            {
                var previous = before.Bodies.SingleOrDefault(candidate => candidate.EntityId == body.EntityId);
                if (previous is null || previous.FloorGrounded != body.FloorGrounded || previous.SupportEntityId != body.SupportEntityId) return true;
            }
            if (before.Pairs.Any(pair => pair.Touching) != after.Pairs.Any(pair => pair.Touching)) return true;
        }
        return false;
    }

    private static double Percentile99(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        var ordered = values.Order().ToArray();
        return ordered[(int)Math.Ceiling(ordered.Length * .99) - 1];
    }
}
