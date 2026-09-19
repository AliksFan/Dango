using Dango.Core.Animation;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Physics.Presentation.Attachments;
using Dango.Physics;

namespace Dango.Desktop.Rendering.Visuals;

internal sealed class QuietIdleSceneController
{
    private readonly Dictionary<DangoEntityId, QuietIdleController> entries = [];
    internal Func<int, int, int> Next { get; set; } = Random.Shared.Next;
    internal string? Error { get; private set; }
    internal bool HasActive => entries.Values.Any(value => value.Active != QuietIdleAction.None);
    internal QuietIdleAction ActiveFor(DangoEntityId id) => entries.TryGetValue(id, out var value) ? value.Active : QuietIdleAction.None;

    internal void BeforeStep(DesktopPhysicsSceneFrame frame, double seconds, AttachmentSceneController attachments,
        Func<DangoEntityId, bool> eligible)
    {
        foreach (var removed in entries.Keys.Except(frame.Physics.Bodies.Select(body => body.EntityId)).ToArray()) entries.Remove(removed);
        foreach (var body in frame.Physics.Bodies)
        {
            if (!entries.TryGetValue(body.EntityId, out var controller))
                entries.Add(body.EntityId, controller = new((min, max) => Next(min, max)));
            var succeeded = controller.BeforeStep(new(body.IsGrounded, body.CoreX, body.CoreY), seconds,
                Error is null && attachments.IsHealthy(body.EntityId) && eligible(body.EntityId),
                action => attachments.SubmitQuietIdle(body.EntityId, action == QuietIdleAction.Blink
                    ? Blink.Default(EyeSelection.Both)
                    : new SetGaze(EyeSelection.Both, action == QuietIdleAction.LookLeft ? -.4 : .4, 0)).Accepted,
                action => Release(body.EntityId, action, attachments));
            if (!succeeded) LatchError();
        }
    }

    internal void AfterStep(double seconds, AttachmentSceneController attachments)
    {
        foreach (var (id, controller) in entries)
        {
            if (attachments.IsHealthy(id)) controller.AfterStep(seconds);
            else controller.Cancel(_ => true);
        }
    }

    internal bool Cancel(DangoEntityId id, AttachmentSceneController? attachments)
    {
        if (entries.TryGetValue(id, out var controller) && !controller.Cancel(action => Release(id, action, attachments))) LatchError();
        return Error is null;
    }

    internal void CancelAll(AttachmentSceneController? attachments)
    {
        foreach (var id in entries.Keys) Cancel(id, attachments);
        entries.Clear();
    }

    internal void Reset() { entries.Clear(); Error = null; }

    private static bool Release(DangoEntityId id, QuietIdleAction action, AttachmentSceneController? attachments) =>
        attachments?.IsHealthy(id) != true || attachments.SubmitQuietIdle(id, new Release(action == QuietIdleAction.Blink
            ? AttachmentChannel.LeftBlink | AttachmentChannel.RightBlink
            : AttachmentChannel.LeftGaze | AttachmentChannel.RightGaze)).Accepted;

    private void LatchError() => Error = "待机眼睛动作取消失败；请使用恢复团子清除旧状态。";
}
