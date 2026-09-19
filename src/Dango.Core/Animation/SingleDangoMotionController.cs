using Dango.Core.Coordinates;
using Dango.Core.Scene;

namespace Dango.Core.Animation;

/// <summary>
/// Owns the authoritative single-Dango action state and position.
/// Presentation physics may observe snapshots, but cannot advance this state machine.
/// </summary>
public sealed class SingleDangoMotionController
{
    private static readonly IReadOnlyDictionary<DangoActionState, string> ClipIds =
        new Dictionary<DangoActionState, string>
        {
            [DangoActionState.Idle] = "idle",
            [DangoActionState.Wander] = "wander",
            [DangoActionState.ApproachIsland] = "approach-island",
            [DangoActionState.ClimbUp] = "climb-up",
            [DangoActionState.OnIsland] = "on-island",
            [DangoActionState.ClimbDown] = "climb-down",
            [DangoActionState.Dragged] = "dragged",
            [DangoActionState.Jumping] = "falling",
            [DangoActionState.Falling] = "falling",
            [DangoActionState.Landing] = "landing",
            [DangoActionState.Reacting] = "reacting",
        };

    private readonly ActionStateMachine stateMachine;
    private LogicalRect workArea;
    private LogicalVector dragOffset;
    private LogicalPoint jumpOrigin;
    private double jumpGroundContactY;
    private double jumpHorizontalVelocityDipsPerSecond;
    private double dragFallVelocityDipsPerSecond;
    private long jumpStartedAtMilliseconds;
    private long landingConfirmedAtMilliseconds = long.MinValue;
    private bool jumpWasHeld;
    private bool jumpInProgress;
    private bool repeatJumpArmed;

    private const double MaximumHorizontalSpeed = 55;
    private const double HorizontalAcceleration = 110;
    private const double BaseGroundFrictionDeceleration = 320;
    private const double JumpHeight = 48;
    private const long JumpDurationMilliseconds = 640;
    private const long JumpApexMilliseconds = JumpDurationMilliseconds / 2;
    private const long RepeatJumpCooldownMilliseconds = 450;
    private const double DragFallGravityDipsPerSecondSquared = 900;
    private const double MaximumDragFallSpeedDipsPerSecond = 520;

    public SingleDangoMotionController(SceneSnapshot initialSnapshot, LogicalRect workArea)
    {
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        if (!workArea.Contains(initialSnapshot.Dango.VisualBounds))
        {
            throw new ArgumentException("团子初始位置必须位于工作区内。", nameof(initialSnapshot));
        }

        Snapshot = initialSnapshot;
        this.workArea = workArea;
        stateMachine = new ActionStateMachine(initialSnapshot.Dango.State);
    }

    public SceneSnapshot Snapshot { get; private set; }

    public bool IsDragging { get; private set; }

    public bool IsExited => stateMachine.IsExited;

    public double HorizontalVelocityDipsPerSecond { get; private set; }

    public double SquashAmount { get; private set; }

    public int TakeoffCount { get; private set; }

    public bool IsDirectionalJumpInProgress => jumpInProgress;

    public GroundSurfaceMotionProfile GroundSurfaceProfile { get; private set; } =
        GroundSurfaceMotionProfile.DefaultFloor;

    public double GroundFrictionCue { get; private set; }

    public DangoFacingDirection FacingDirection { get; private set; }

    public void SetGroundSurfaceProfile(GroundSurfaceMotionProfile profile)
    {
        GroundSurfaceProfile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public void StartAction(DangoActionState action, long nowMilliseconds)
    {
        stateMachine.TransitionTo(action);
        ApplyState(action, nowMilliseconds);
    }

    public void BeginDrag(LogicalPoint pointer, long nowMilliseconds)
    {
        ResetDirectionalControl();
        StartAction(DangoActionState.Dragged, nowMilliseconds);
        var bounds = Snapshot.Dango.VisualBounds;
        dragOffset = new LogicalVector(pointer.X - bounds.X, pointer.Y - bounds.Y);
        IsDragging = true;
    }

    public void MoveDrag(LogicalPoint pointer)
    {
        if (!IsDragging)
        {
            throw new InvalidOperationException("团子未处于拖动状态。");
        }

        var current = Snapshot.Dango.VisualBounds;
        var moved = new LogicalRect(
            pointer.X - dragOffset.X,
            pointer.Y - dragOffset.Y,
            current.Width,
            current.Height).ClampInside(workArea);
        ApplyBounds(moved);
    }

    public void ReleaseDrag(long nowMilliseconds)
    {
        if (!IsDragging)
        {
            throw new InvalidOperationException("团子未处于拖动状态。");
        }

        IsDragging = false;
        dragFallVelocityDipsPerSecond = 0;
        StartAction(DangoActionState.Falling, nowMilliseconds);
    }

    public void CancelDrag(long nowMilliseconds)
    {
        if (IsDragging)
        {
            ReleaseDrag(nowMilliseconds);
        }
    }

    public void MoveTo(LogicalPoint topLeft)
    {
        if (IsExited)
        {
            throw new InvalidActionTransitionException("团子退出后不能再移动。");
        }

        var current = Snapshot.Dango.VisualBounds;
        ApplyBounds(new LogicalRect(topLeft.X, topLeft.Y, current.Width, current.Height).ClampInside(workArea));
    }

    public void Rehost(SceneSnapshot snapshot, LogicalRect targetWorkArea)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.Dango.Id, Snapshot.Dango.Id, StringComparison.Ordinal) ||
            !string.Equals(snapshot.Island.Id, Snapshot.Island.Id, StringComparison.Ordinal))
        {
            throw new ArgumentException("宿主迁移必须保持同一场景实体。", nameof(snapshot));
        }

        if (!targetWorkArea.Contains(snapshot.Dango.VisualBounds) ||
            !targetWorkArea.Contains(snapshot.Island.VisualBounds))
        {
            throw new ArgumentException("迁移后的场景必须位于目标工作区内。", nameof(snapshot));
        }

        ResetDirectionalControl();
        IsDragging = false;
        workArea = targetWorkArea;
        Snapshot = snapshot;
    }

    public void ConfirmLanding(LogicalPoint groundContact, long nowMilliseconds)
    {
        var current = Snapshot.Dango.VisualBounds;
        var landed = new LogicalRect(
            groundContact.X - (current.Width / 2),
            groundContact.Y - current.Height,
            current.Width,
            current.Height).ClampInside(workArea);
        StartAction(DangoActionState.Landing, nowMilliseconds);
        Snapshot = Snapshot with
        {
            Dango = Snapshot.Dango with
            {
                VisualBounds = landed,
                GroundContactPoint = new LogicalPoint(landed.Center.X, landed.Bottom),
            },
        };
        landingConfirmedAtMilliseconds = nowMilliseconds;
        jumpInProgress = false;
    }

    public void AdvanceDirectionalControl(
        DirectionalControlIntent intent,
        TimeSpan fixedStep,
        long nowMilliseconds)
    {
        if (fixedStep < TimeSpan.Zero || fixedStep > TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(fixedStep));
        }

        if (IsExited || Snapshot.Dango.State == DangoActionState.Hidden || IsDragging)
        {
            ResetDirectionalControl();
            return;
        }

        var jumpPressed = intent.Jump && !jumpWasHeld;
        jumpWasHeld = intent.Jump;
        if (!intent.Jump)
        {
            repeatJumpArmed = false;
        }
        var state = Snapshot.Dango.State;
        var isAirborne = jumpInProgress &&
            state is DangoActionState.Jumping or DangoActionState.Falling;
        var requestedFacing = intent.Left == intent.Right
            ? DangoFacingDirection.Center
            : intent.Left ? DangoFacingDirection.Left : DangoFacingDirection.Right;
        if (!isAirborne &&
            requestedFacing != DangoFacingDirection.Center &&
            state is DangoActionState.Idle or DangoActionState.Wander or DangoActionState.Landing)
        {
            FacingDirection = requestedFacing;
        }
        var repeatReady = repeatJumpArmed && state == DangoActionState.Landing &&
            landingConfirmedAtMilliseconds != long.MinValue &&
            nowMilliseconds - landingConfirmedAtMilliseconds >= RepeatJumpCooldownMilliseconds;

        if (!isAirborne && (jumpPressed || (intent.Jump && repeatReady)) &&
            state is DangoActionState.Idle or DangoActionState.Wander or DangoActionState.Landing)
        {
            StartJump(nowMilliseconds, requestedFacing);
            isAirborne = true;
        }

        if (isAirborne)
        {
            AdvanceJump(nowMilliseconds);
            HorizontalVelocityDipsPerSecond = 0;
            GroundFrictionCue = 0;
            SquashAmount = Approach(SquashAmount, 0, fixedStep.TotalMilliseconds / 160d);
            return;
        }

        if (state == DangoActionState.Falling)
        {
            AdvanceDragFall(fixedStep, nowMilliseconds);
            HorizontalVelocityDipsPerSecond = 0;
            GroundFrictionCue = 0;
            SquashAmount = Approach(SquashAmount, 0, fixedStep.TotalMilliseconds / 160d);
            return;
        }

        if (state is not (DangoActionState.Idle or DangoActionState.Wander or DangoActionState.Landing))
        {
            HorizontalVelocityDipsPerSecond = 0;
            GroundFrictionCue = 0;
            SquashAmount = Approach(SquashAmount, 0, fixedStep.TotalMilliseconds / 160d);
            return;
        }

        var waitingForHeldRepeat = state == DangoActionState.Landing && intent.Jump && repeatJumpArmed;
        var targetVelocity = waitingForHeldRepeat || intent.Left == intent.Right
            ? 0
            : intent.Left ? -MaximumHorizontalSpeed : MaximumHorizontalSpeed;
        var rate = targetVelocity == 0
            ? BaseGroundFrictionDeceleration * GroundSurfaceProfile.FrictionCoefficient
            : HorizontalAcceleration;
        var previousVelocity = HorizontalVelocityDipsPerSecond;
        HorizontalVelocityDipsPerSecond = Approach(
            HorizontalVelocityDipsPerSecond,
            targetVelocity,
            rate * fixedStep.TotalSeconds);
        var velocityDelta = HorizontalVelocityDipsPerSecond - previousVelocity;
        var maximumVelocityDelta = rate * fixedStep.TotalSeconds;
        var frictionCueTarget = maximumVelocityDelta <= 0
            ? 0
            : Math.Clamp(
                -velocityDelta / maximumVelocityDelta,
                -1,
                1) * Math.Min(1, GroundSurfaceProfile.FrictionCoefficient);
        var frictionCueDuration = Math.Abs(frictionCueTarget) > 0.0001 ? 70d : 140d;
        GroundFrictionCue = Approach(
            GroundFrictionCue,
            frictionCueTarget,
            fixedStep.TotalMilliseconds / frictionCueDuration);

        if (Math.Abs(HorizontalVelocityDipsPerSecond) > 0.0001)
        {
            if (Snapshot.Dango.State is DangoActionState.Idle or DangoActionState.Landing)
            {
                StartAction(DangoActionState.Wander, nowMilliseconds);
            }

            var before = Snapshot.Dango.VisualBounds;
            MoveTo(new LogicalPoint(
                before.X + (HorizontalVelocityDipsPerSecond * fixedStep.TotalSeconds),
                before.Y));
            var after = Snapshot.Dango.VisualBounds;
            if ((after.X <= workArea.X && HorizontalVelocityDipsPerSecond < 0) ||
                (after.Right >= workArea.Right && HorizontalVelocityDipsPerSecond > 0))
            {
                HorizontalVelocityDipsPerSecond = 0;
            }
        }
        else if (Snapshot.Dango.State == DangoActionState.Wander)
        {
            StartAction(DangoActionState.Idle, nowMilliseconds);
        }
        else if (Snapshot.Dango.State == DangoActionState.Landing &&
            nowMilliseconds - landingConfirmedAtMilliseconds >= 750)
        {
            StartAction(DangoActionState.Idle, nowMilliseconds);
        }

        var squashTarget = intent.Squash && Snapshot.Dango.State is DangoActionState.Idle or DangoActionState.Wander
            ? 1
            : 0;
        var squashDuration = squashTarget > SquashAmount ? 120d : 160d;
        SquashAmount = Approach(SquashAmount, squashTarget, fixedStep.TotalMilliseconds / squashDuration);
    }

    public void ObservePresentationFrame()
    {
        // Deliberately empty: presentation frames cannot author Core events.
    }

    public void Hide(long nowMilliseconds)
    {
        ResetDirectionalControl();
        IsDragging = false;
        stateMachine.Hide();
        ApplyState(DangoActionState.Hidden, nowMilliseconds);
    }

    public void Exit(long nowMilliseconds)
    {
        ResetDirectionalControl();
        IsDragging = false;
        stateMachine.Exit();
        ApplyState(DangoActionState.Hidden, nowMilliseconds);
    }

    private void ApplyState(DangoActionState state, long nowMilliseconds)
    {
        Snapshot = Snapshot with
        {
            Dango = Snapshot.Dango with
            {
                State = state,
                StateStartedAtMilliseconds = nowMilliseconds,
                ClipId = state == DangoActionState.Hidden ? Snapshot.Dango.ClipId : ClipIds[state],
            },
        };
    }

    private void ApplyBounds(LogicalRect bounds)
    {
        Snapshot = Snapshot with
        {
            Dango = Snapshot.Dango with
            {
                VisualBounds = bounds,
                GroundContactPoint = new LogicalPoint(bounds.Center.X, bounds.Bottom),
            },
        };
    }

    private void StartJump(long nowMilliseconds, DangoFacingDirection direction)
    {
        HorizontalVelocityDipsPerSecond = 0;
        GroundFrictionCue = 0;
        SquashAmount = 0;
        jumpHorizontalVelocityDipsPerSecond = direction switch
        {
            DangoFacingDirection.Left => -MaximumHorizontalSpeed,
            DangoFacingDirection.Right => MaximumHorizontalSpeed,
            _ => 0,
        };
        jumpOrigin = new LogicalPoint(Snapshot.Dango.VisualBounds.X, Snapshot.Dango.VisualBounds.Y);
        jumpGroundContactY = Snapshot.Dango.GroundContactPoint.Y;
        jumpStartedAtMilliseconds = nowMilliseconds;
        jumpInProgress = true;
        repeatJumpArmed = true;
        landingConfirmedAtMilliseconds = long.MinValue;
        StartAction(DangoActionState.Jumping, nowMilliseconds);
        TakeoffCount++;
    }

    private void AdvanceJump(long nowMilliseconds)
    {
        var elapsed = Math.Max(0, nowMilliseconds - jumpStartedAtMilliseconds);
        if (elapsed >= JumpDurationMilliseconds)
        {
            MoveTo(new LogicalPoint(
                jumpOrigin.X + (jumpHorizontalVelocityDipsPerSecond * JumpDurationMilliseconds / 1000d),
                jumpOrigin.Y));
            if (Snapshot.Dango.State == DangoActionState.Jumping)
            {
                StartAction(DangoActionState.Falling, nowMilliseconds);
            }

            ConfirmLanding(new LogicalPoint(Snapshot.Dango.VisualBounds.Center.X, jumpGroundContactY), nowMilliseconds);
            return;
        }

        if (elapsed >= JumpApexMilliseconds && Snapshot.Dango.State == DangoActionState.Jumping)
        {
            StartAction(DangoActionState.Falling, nowMilliseconds);
        }

        var progress = elapsed / (double)JumpDurationMilliseconds;
        MoveTo(new LogicalPoint(
            jumpOrigin.X + (jumpHorizontalVelocityDipsPerSecond * elapsed / 1000d),
            jumpOrigin.Y - (Math.Sin(progress * Math.PI) * JumpHeight)));
    }

    private void AdvanceDragFall(TimeSpan fixedStep, long nowMilliseconds)
    {
        var current = Snapshot.Dango.VisualBounds;
        var groundTop = Math.Max(workArea.Y, workArea.Bottom - current.Height);
        dragFallVelocityDipsPerSecond = Math.Min(
            MaximumDragFallSpeedDipsPerSecond,
            dragFallVelocityDipsPerSecond + (DragFallGravityDipsPerSecondSquared * fixedStep.TotalSeconds));
        var nextTop = Math.Min(
            groundTop,
            current.Y + (dragFallVelocityDipsPerSecond * fixedStep.TotalSeconds));
        MoveTo(new LogicalPoint(current.X, nextTop));
        if (nextTop >= groundTop - 0.001)
        {
            ConfirmLanding(
                new LogicalPoint(Snapshot.Dango.VisualBounds.Center.X, workArea.Bottom),
                nowMilliseconds);
            dragFallVelocityDipsPerSecond = 0;
        }
    }

    private void ResetDirectionalControl()
    {
        HorizontalVelocityDipsPerSecond = 0;
        GroundFrictionCue = 0;
        SquashAmount = 0;
        jumpWasHeld = false;
        repeatJumpArmed = false;
        jumpInProgress = false;
        jumpHorizontalVelocityDipsPerSecond = 0;
        dragFallVelocityDipsPerSecond = 0;
        landingConfirmedAtMilliseconds = long.MinValue;
    }

    private static double Approach(double current, double target, double maximumDelta)
    {
        if (current < target)
        {
            return Math.Min(target, current + maximumDelta);
        }

        return Math.Max(target, current - maximumDelta);
    }
}
