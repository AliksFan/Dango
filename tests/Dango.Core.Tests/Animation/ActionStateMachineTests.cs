using Dango.Core.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Core.Tests.Animation;

[TestClass]
public sealed class ActionStateMachineTests
{
    [TestMethod]
    public void RejectsAnIllegalStateTransition()
    {
        var machine = new ActionStateMachine(DangoActionState.Idle);

        Assert.ThrowsExactly<InvalidActionTransitionException>(
            () => machine.TransitionTo(DangoActionState.OnIsland));
    }

    [TestMethod]
    public void HideStopsActivityFromEveryState()
    {
        foreach (var state in Enum.GetValues<DangoActionState>())
        {
            var machine = new ActionStateMachine(state);

            machine.Hide();

            Assert.AreEqual(DangoActionState.Hidden, machine.State, $"初始状态：{state}");
            Assert.IsFalse(machine.IsExited, $"隐藏不应等价于退出：{state}");
        }
    }

    [TestMethod]
    public void ExitTerminatesActivityFromEveryState()
    {
        foreach (var state in Enum.GetValues<DangoActionState>())
        {
            var machine = new ActionStateMachine(state);

            machine.Exit();

            Assert.AreEqual(DangoActionState.Hidden, machine.State, $"初始状态：{state}");
            Assert.IsTrue(machine.IsExited, $"初始状态：{state}");
            Assert.ThrowsExactly<InvalidActionTransitionException>(
                () => machine.TransitionTo(DangoActionState.Idle));
        }
    }
}
