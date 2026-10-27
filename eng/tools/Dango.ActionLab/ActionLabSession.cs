using Dango.Core.Animation;
using Dango.Core.Coordinates;
using Dango.Desktop.Physics.Presentation;
using Dango.Desktop.Rendering.Actions;
using Dango.Desktop.Rendering.Visuals;

namespace Dango.ActionLab;

/// <summary>One in-memory body. UI and verification use this exact step boundary.</summary>
internal sealed class ActionLabSession : IDisposable
{
    internal const double Left = 28, Right = 492, Floor = 300;
    internal static readonly TimeSpan FixedStep = TimeSpan.FromSeconds(1d / 60);
    private static readonly LogicalRect Reference = new(180, 180, 160, 120);
    private readonly PhysicsPresentationInput input = new("action-lab", Reference, DangoActionState.Idle,
        0, 1, "floor", new(0, -1), 0, 1, false, true, []);
    internal StackedDangoSceneRuntime Runtime { get; }
    internal DangoActionController Controller { get; } = new();
    internal string Phase { get; private set; } = "等待接地";
    internal string? Error { get; private set; }
    internal DangoActionKind? LastAction { get; private set; }
    internal string? LastCandidateId { get; private set; }
    internal long Steps { get; private set; }
    internal bool Paused { get; private set; }
    private bool disposed;

    internal ActionLabSession(string module)
    {
        Runtime = StackedDangoSceneRuntime.CreateActionPreview(module);
        try
        {
            if (!Runtime.TryInitialize()) throw new InvalidOperationException(Runtime.LastError);
            Runtime.SetViewport(new(0, 0, 520, 340));
            Runtime.Start();
            // Establish host coordinates before setting the physical side walls.
            Runtime.TickActionPreview(input, FixedStep, default);
            Runtime.SetHorizontalBounds(Left, Right);
            for (var i = 0; i < 120; i++) Step();
            if (Error is not null) throw new InvalidOperationException(Error);
            Phase = "等待动作";
        }
        catch { Runtime.Dispose(); throw; }
    }

    internal ActionObservation Observe()
    {
        var frame = Runtime.LastBeltRoomFrame ?? throw new InvalidOperationException("缺少物理快照。");
        var s = frame.Stats;
        var top = Enumerable.Range(0, 256).MaxBy(i => frame.Curve[2 * i + 1]);
        // BeltRoomSceneAdapter uses 72 DIP/m and the fixed reference floor.
        return new(s[0], s[1], frame.Curve[2 * top], frame.Curve[2 * top + 1], s[14] != 0,
            frame.Scaffold[2], frame.Scaffold[3], (Left - Reference.Center.X) / 72,
            (Right - Reference.Center.X) / 72);
    }

    internal bool Trigger(DangoActionKind kind, int direction, string candidateId = "current")
    {
        if (Paused || Error is not null) return false;
        if (kind == DangoActionKind.Shake && !Runtime.SupportsMuscleActuation)
        { Phase = "当前物理模块不支持内部肌肉抖身"; return false; }
        if (!Enum.IsDefined(kind)) { Phase = "不支持此动作类型"; return false; }
        var candidate = ActionLabCandidates.For(kind).FirstOrDefault(c => c.Id == candidateId);
        if (candidate is null) { Phase = "不支持此动作候选"; return false; }
        var started = Controller.TryStart(kind, direction, Observe(), candidate.ShakeStrength);
        if (!started) Phase = Controller.RejectionReason ?? "请等待当前动作结束";
        else { LastAction = kind; LastCandidateId = candidate.Id; Phase = "准备动作"; }
        return started;
    }

    internal void Step()
    {
        if (disposed || Paused || Error is not null) return;
        try
        {
            var output = Controller.Advance(FixedStep.TotalSeconds, Observe());
            Submit(output);
            Runtime.TickActionPreview(input, FixedStep, output.Motor);
            Steps++;
            if (Controller.Active) Phase = output.Phase;
            else if (output.Completed) Phase = "动作结束 · 可再次选择";
            var failure = Runtime.LastError ?? Runtime.AttachmentError ?? Controller.Error;
            if (failure is not null) Fail(failure);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or ArithmeticException)
        { Fail(error.Message); }
    }

    private void Submit(ActionOutput output)
    {
        foreach (var intent in output.Attachments)
        {
            var result = Runtime.SubmitAttachment(intent);
            if (!result.Accepted) throw new InvalidOperationException($"眼部指令被拒绝：{result.Reason}");
        }
    }

    internal void Cancel()
    {
        if (Error is not null || Paused) return;
        Submit(Controller.Cancel());
        Runtime.CancelInput();
        Phase = "已停止 · 身体自然收稳";
    }

    internal void SetPaused(bool paused)
    {
        if (Paused == paused || Error is not null) return;
        if (paused) { Cancel(); Runtime.Stop(); }
        else Runtime.Start();
        Paused = paused;
        Phase = paused ? "已暂停 · 恢复后重新选择动作" : "等待动作";
    }

    private void Fail(string message)
    {
        Error = message;
        Controller.Cancel();
        Runtime.Stop();
        Phase = "已停止：" + message;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Runtime.Dispose();
    }
}
