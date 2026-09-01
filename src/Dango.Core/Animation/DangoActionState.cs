namespace Dango.Core.Animation;

public enum DangoActionState
{
    Hidden,
    Idle,
    Wander,
    ApproachIsland,
    ClimbUp,
    OnIsland,
    ClimbDown,
    Dragged,
    Falling,
    Landing,
    Reacting,
}

public sealed class InvalidActionTransitionException : InvalidOperationException
{
    public InvalidActionTransitionException(DangoActionState from, DangoActionState to)
        : base($"非法团子状态转换：{from} -> {to}。")
    {
    }

    public InvalidActionTransitionException(string message)
        : base(message)
    {
    }
}

public sealed class ActionStateMachine
{
    private static readonly IReadOnlyDictionary<DangoActionState, IReadOnlySet<DangoActionState>> AllowedTransitions =
        new Dictionary<DangoActionState, IReadOnlySet<DangoActionState>>
        {
            [DangoActionState.Hidden] = new HashSet<DangoActionState> { DangoActionState.Idle },
            [DangoActionState.Idle] = new HashSet<DangoActionState>
            {
                DangoActionState.Wander,
                DangoActionState.ApproachIsland,
                DangoActionState.Dragged,
                DangoActionState.Reacting,
            },
            [DangoActionState.Wander] = new HashSet<DangoActionState>
            {
                DangoActionState.Idle,
                DangoActionState.ApproachIsland,
                DangoActionState.Dragged,
                DangoActionState.Reacting,
            },
            [DangoActionState.ApproachIsland] = new HashSet<DangoActionState>
            {
                DangoActionState.Idle,
                DangoActionState.ClimbUp,
            },
            [DangoActionState.ClimbUp] = new HashSet<DangoActionState> { DangoActionState.OnIsland },
            [DangoActionState.OnIsland] = new HashSet<DangoActionState>
            {
                DangoActionState.ClimbDown,
                DangoActionState.Dragged,
                DangoActionState.Reacting,
            },
            [DangoActionState.ClimbDown] = new HashSet<DangoActionState> { DangoActionState.Falling },
            [DangoActionState.Dragged] = new HashSet<DangoActionState> { DangoActionState.Falling },
            [DangoActionState.Falling] = new HashSet<DangoActionState> { DangoActionState.Landing },
            [DangoActionState.Landing] = new HashSet<DangoActionState> { DangoActionState.Idle },
            [DangoActionState.Reacting] = new HashSet<DangoActionState>
            {
                DangoActionState.Idle,
                DangoActionState.OnIsland,
            },
        };

    public ActionStateMachine(DangoActionState initialState)
    {
        State = initialState;
    }

    public DangoActionState State { get; private set; }

    public bool IsExited { get; private set; }

    public void TransitionTo(DangoActionState nextState)
    {
        if (IsExited)
        {
            throw new InvalidActionTransitionException("团子退出后不能再转换状态。");
        }

        if (nextState == DangoActionState.Hidden)
        {
            Hide();
            return;
        }

        if (!AllowedTransitions[State].Contains(nextState))
        {
            throw new InvalidActionTransitionException(State, nextState);
        }

        State = nextState;
    }

    public void Hide()
    {
        if (IsExited)
        {
            throw new InvalidActionTransitionException("团子退出后不能再次隐藏。");
        }

        State = DangoActionState.Hidden;
    }

    public void Exit()
    {
        State = DangoActionState.Hidden;
        IsExited = true;
    }
}
