using System.Collections;
using System.Reflection;
using System.Text.Json;
using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.DesktopHost.Interaction;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.Desktop.Tests.Rendering;

[TestClass]
public sealed class LightFeedbackRuntimeTests
{
    // Exact TimeSpan duration keeps boundary assertions independent of 1/60 tick rounding.
    private static readonly TimeSpan Dt = TimeSpan.FromMilliseconds(20);
    private static readonly PhysicsPresentationInput Primary = new("light-fixture", new LogicalRect(400, 500, 160, 120),
        DangoActionState.Idle, 1000, 17, "fixture-ground", new(0, -1), 0, 1, false, true, []);

    internal static StackedDangoSceneRuntime Create()
    {
        var runtime = StackedDangoSceneRuntime.CreateProduct();
        Assert.IsTrue(runtime.TryInitialize());
        runtime.Start();
        runtime.Tick(Primary, Dt);
        return runtime;
    }

    internal static void Steps(StackedDangoSceneRuntime runtime, int count)
    {
        for (var i = 0; i < count; i++) runtime.Tick(Primary, Dt);
    }

    [TestMethod]
    public void ClickUsesBothRealEyesAndSharedCooldownWithoutChangingPhysicsOrHat()
    {
        using var runtime = Create();
        using var reference = Create();
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        Assert.IsTrue(runtime.TryReact(target, petting: false));
        Assert.IsFalse(runtime.TryReact(target, petting: true));
        for (var i = 0; i < 15; i++)
        {
            Steps(runtime, 1); Steps(reference, 1);
            var face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
            Assert.AreEqual(face.Left.Openness, face.Right.Openness, 1e-10);
            if (i == 1) Assert.IsLessThan(0.1, face.Left.Openness);
            if (i >= 6) Assert.AreEqual(1, face.Left.Openness, 1e-8);
            Assert.AreEqual(JsonSerializer.Serialize(reference.LastPhysicsFrame), JsonSerializer.Serialize(runtime.LastPhysicsFrame));
            AssertPresentationEquivalent(JsonSerializer.SerializeToElement(reference.LastFrame!.Bodies[0].Attachments!.Hat),
                JsonSerializer.SerializeToElement(runtime.LastFrame.Bodies[0].Attachments!.Hat));
            if (i < 14) Assert.IsFalse(runtime.TryReact(target, petting: true));
        }
        Assert.IsTrue(runtime.TryReact(target, petting: true));
    }

    [TestMethod]
    public void PetAutomaticallyReleasesOnlyEyePoseAndSharesOneSecondGate()
    {
        using var runtime = Create();
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        Assert.IsTrue(runtime.SubmitAttachment(new SetGaze(EyeSelection.Left, -1, 0)).Accepted);
        Assert.IsTrue(runtime.TryReact(target, petting: true));
        Steps(runtime, 10);
        var face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
        Assert.IsTrue(face.Left.Openness is >= 0.30 and <= 0.40);
        Assert.IsTrue(face.Right.Openness is >= 0.30 and <= 0.40);
        Steps(runtime, 20);
        Assert.IsLessThan(0.40, runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness);
        Steps(runtime, 10);
        face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
        Assert.IsGreaterThanOrEqualTo(0.98, face.Left.Openness);
        Assert.IsLessThanOrEqualTo(0.02, face.Left.Strength);
        Assert.IsLessThan(runtime.LastFrame.Bodies[0].Attachments!.Face.Right.Center.X - 0.26, face.Left.Center.X);
        Assert.IsFalse(runtime.TryReact(target, petting: false));
        Steps(runtime, 10);
        Assert.IsTrue(runtime.TryReact(target, petting: false));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void StopCancelsBeforePausingAndCannotReplayAcceptedOrActiveFeedback(bool advance)
    {
        using var runtime = Create();
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        Assert.IsTrue(runtime.TryReact(target, petting: true));
        if (advance) Steps(runtime, 10);
        var before = runtime.LastFrame;
        runtime.Stop();
        Assert.AreSame(before, runtime.LastFrame);
        Assert.IsFalse(runtime.TryReact(target, petting: false));
        Assert.IsFalse(runtime.HasLightFeedback);
        runtime.Start();
        Steps(runtime, 12);
        var face = runtime.LastFrame!.Bodies[0].Attachments!.Face;
        Assert.IsGreaterThanOrEqualTo(0.98, face.Left.Openness);
        Assert.IsLessThanOrEqualTo(0.02, face.Left.Strength);
        Assert.IsFalse(runtime.TryReact(target, petting: true), "Old input generation must stay invalid after resume.");
        Assert.IsTrue(runtime.TryReact(runtime.GetLightInteractionTarget(target.Entity)!.Value, petting: false));
    }

    [TestMethod]
    public void TargetsAreScopedAndResetAndDirectionalInputCancelWithoutRetargeting()
    {
        using var runtime = Create();
        using var other = Create();
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        Assert.IsFalse(other.TryReact(target, petting: true));
        Assert.IsTrue(runtime.TryReact(target, petting: true));
        runtime.SetDirectionalControl(new(true, false, false, false));
        Assert.IsFalse(runtime.HasLightFeedback);
        Assert.IsFalse(runtime.TryReact(target, petting: true));
        runtime.SetDirectionalControl(DirectionalControlIntent.None);
        runtime.RegenerateDango();
        Steps(runtime, 1);
        Assert.IsFalse(runtime.TryReact(target, petting: false));
        Assert.IsTrue(runtime.TryReact(runtime.GetLightInteractionTarget(target.Entity)!.Value, petting: false));
    }

    [TestMethod]
    public void AFeedbackTargetsTheRequestedEntityAndNeverChangesSelection()
    {
        using var runtime = Create();
        runtime.AddDango(); Steps(runtime, 1);
        var selected = runtime.SelectedEntity;
        var second = runtime.EntityIds[1];
        Assert.IsTrue(runtime.TryReact(runtime.GetLightInteractionTarget(second)!.Value, petting: true));
        Steps(runtime, 12);
        Assert.AreEqual(selected, runtime.SelectedEntity);
        Assert.AreEqual(1, runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness, 1e-10);
        Assert.IsLessThan(0.4, runtime.LastFrame.Bodies[1].Attachments!.Face.Left.Openness);
    }

    [TestMethod]
    public void FaultCancellationDoesNotSubmitThroughTheHealthGuardAndResetIsClean()
    {
        using var runtime = Create();
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        Assert.IsTrue(runtime.TryReact(target, petting: true)); Steps(runtime, 6);
        typeof(StackedDangoSceneRuntime).GetProperty(nameof(StackedDangoSceneRuntime.LastError))!
            .SetValue(runtime, "fictional physical fault");
        var before = runtime.LastFrame;
        runtime.CancelInput(); runtime.CancelInput();
        Steps(runtime, 20);
        Assert.AreSame(before, runtime.LastFrame);
        Assert.IsFalse(runtime.HasLightFeedback);
        Assert.IsFalse(runtime.TryReact(target, petting: false));
        runtime.RegenerateDango(); Steps(runtime, 1);
        Assert.AreEqual(1, runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness, 1e-10);
    }

    [TestMethod]
    public void ReviewRuntimeNeverEnablesProductGestures()
    {
        using var runtime = StackedDangoSceneRuntime.CreateEyeHatReview();
        runtime.Start(); Steps(runtime, 1);
        Assert.IsFalse(runtime.LightInteractionsEnabled);
        Assert.IsNull(runtime.GetLightInteractionTarget(runtime.EntityIds[0]));
        Assert.IsTrue(runtime.SubmitAttachment(Blink.Default(EyeSelection.Both)).Accepted);
    }

    [TestMethod]
    [DataRow(10)]
    [DataRow(20)]
    [DataRow(40)]
    public void PetDeadlineUsesSuccessfulPhysicsTimeAndNoUiQueries(int milliseconds)
    {
        using var runtime = Create();
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        Assert.IsTrue(runtime.TryReact(target, true));
        var initial = runtime.LastFrame;
        for (var i = 0; i < 120; i++) _ = runtime.CreateDiagnosticSummary();
        Assert.AreSame(initial, runtime.LastFrame);
        for (var time = 0; time < 600; time += milliseconds)
            runtime.Tick(Primary, TimeSpan.FromMilliseconds(milliseconds));
        Assert.IsLessThan(0.4, runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness);
        for (var time = 0; time < 200; time += milliseconds)
            runtime.Tick(Primary, TimeSpan.FromMilliseconds(milliseconds));
        Assert.IsGreaterThanOrEqualTo(0.98, runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness);
        Assert.IsFalse(runtime.TryReact(target, false));
        for (var time = 0; time < 200; time += milliseconds)
            runtime.Tick(Primary, TimeSpan.FromMilliseconds(milliseconds));
        Assert.IsTrue(runtime.TryReact(target, false));
    }

    [TestMethod]
    public void FailedCancelLatchesUntilExplicitResetInsteadOfResumingOldFeedback()
    {
        using var runtime = Create();
        Assert.IsTrue(runtime.TryReact(runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value, true));
        // Fill the existing ledger with valid fictional commands without a consumer step.
        for (var i = 0; i < 63; i++) Assert.IsTrue(runtime.SubmitAttachment(new SetGaze(EyeSelection.Left, 0, 0)).Accepted);
        runtime.Stop();
        Assert.IsNotNull(runtime.LightInteractionError);
        Assert.ThrowsExactly<InvalidOperationException>(() => runtime.Start());
        runtime.RegenerateDango();
        runtime.Start(); Steps(runtime, 1);
        Assert.IsNull(runtime.LightInteractionError);
        Assert.AreEqual(1, runtime.LastFrame!.Bodies[0].Attachments!.Face.Left.Openness, 1e-8);
    }

    [TestMethod]
    public void AttachmentFaultRejectsFeedbackButPreservesBodyDrag()
    {
        using var runtime = Create();
        var target = runtime.GetLightInteractionTarget(runtime.EntityIds[0])!.Value;
        var controller = typeof(StackedDangoSceneRuntime).GetField("attachments", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(runtime)!;
        var entries = (IDictionary)controller.GetType().GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        var entry = entries[target.Entity]!;
        entry.GetType().GetField("Error")!.SetValue(entry, "fictional attachment failure");
        Assert.IsFalse(runtime.TryReact(target, false));
        Assert.IsFalse(runtime.TryReact(target, true));
        var point = DangoHostFrameBinding.Bounds(runtime.LastFrame!, 0).Center;
        var session = new LightInteractionSession(runtime);
        Assert.IsTrue(session.TryBegin(target.Entity, point, 0, false, () => true, () => { }, () => true));
        session.Move(new(point.X + 3, point.Y), 1, false);
        Assert.IsTrue(runtime.IsDragging);
        session.Cancel();
        Assert.IsFalse(runtime.IsDragging);
        runtime.RegenerateDango(); Steps(runtime, 1);
        Assert.IsTrue(runtime.TryReact(runtime.GetLightInteractionTarget(target.Entity)!.Value, false));
    }

    private static void AssertPresentationEquivalent(JsonElement expected, JsonElement actual)
    {
        Assert.AreEqual(expected.ValueKind, actual.ValueKind);
        if (expected.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in expected.EnumerateObject())
                AssertPresentationEquivalent(property.Value, actual.GetProperty(property.Name));
        }
        else if (expected.ValueKind == JsonValueKind.Number)
        {
            // Existing blink phase boundaries split the shared analytical spring
            // integration; only floating point roundoff is permitted, not a hat action.
            Assert.AreEqual(expected.GetDouble(), actual.GetDouble(), 1e-7);
        }
        else Assert.AreEqual(expected.GetRawText(), actual.GetRawText());
    }
}
