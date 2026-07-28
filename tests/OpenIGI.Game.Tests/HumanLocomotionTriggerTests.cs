using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the locomotion machine's two double-tap triggers against the table at <c>0x538710</c>.
/// </summary>
public sealed class HumanLocomotionTriggerTests
{
    [Fact]
    public void BothTriggersAreFourStepDoubleTaps()
    {
        IReadOnlyList<TaskStateTrigger> triggers = HumanLocomotionTriggers.Create();

        Assert.Equal(HumanLocomotionTriggers.Count, triggers.Count);
        Assert.All(triggers, t => Assert.Equal(4, t.Length));

        // set, clear, set, clear — the shape of a double tap.
        foreach (TaskStateTrigger trigger in triggers)
        {
            Assert.True(trigger.Steps[0].RequireSet);
            Assert.False(trigger.Steps[1].RequireSet);
            Assert.True(trigger.Steps[2].RequireSet);
            Assert.False(trigger.Steps[3].RequireSet);
        }
    }

    [Fact]
    public void OnlyTheFirstStepHasNoTolerance()
    {
        // Nothing has started at step 0, and a mismatch there costs nothing — so it carries zero.
        TaskStateTrigger trigger = HumanLocomotionTriggers.DoubleTap(HumanLocomotionTriggers.FirstMask);

        Assert.Equal(0, trigger.ToleranceOf(0));
        Assert.Equal(HumanLocomotionTriggers.WindowTicks, trigger.ToleranceOf(1));
        Assert.Equal(HumanLocomotionTriggers.WindowTicks, trigger.ToleranceOf(2));
        Assert.Equal(HumanLocomotionTriggers.WindowTicks, trigger.ToleranceOf(3));
    }

    [Fact]
    public void TheWindowIsSixTicksWhichIsAFifthOfASecond()
    {
        Assert.Equal(6, HumanLocomotionTriggers.WindowTicks);
        Assert.Equal(0.2, HumanLocomotionTriggers.WindowTicks / 30.0, 6);
    }

    [Fact]
    public void EachTriggerWatchesItsOwnBit()
    {
        IReadOnlyList<TaskStateTrigger> triggers = HumanLocomotionTriggers.Create();

        Assert.All(triggers[0].Steps, s => Assert.Equal(HumanLocomotionTriggers.FirstMask, s.Mask));
        Assert.All(triggers[1].Steps, s => Assert.Equal(HumanLocomotionTriggers.SecondMask, s.Mask));
        Assert.NotEqual(HumanLocomotionTriggers.FirstMask, HumanLocomotionTriggers.SecondMask);
    }

    [Fact]
    public void APromptDoubleTapFires()
    {
        TaskStateMachine machine = Machine();
        object owner = new();

        Tap(machine, owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);

        Tap(machine, owner);
        Assert.Equal(0, machine.FiredTrigger);
    }

    [Fact]
    public void ASingleTapDoesNotFire()
    {
        TaskStateMachine machine = Machine();
        object owner = new();

        Tap(machine, owner);
        for (int i = 0; i < 20; i++)
        {
            machine.Tick(owner);
            Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);
        }
    }

    [Fact]
    public void HoldingTheButtonTooLongResetsTheGesture()
    {
        // Step 1 wants the bit clear within six ticks. Hold past that and the trigger goes back to
        // the start, so the release-press-release that follows is only ever half a gesture.
        TaskStateMachine machine = Machine();
        object owner = new();

        machine.Flags = HumanLocomotionTriggers.FirstMask;
        for (int i = 0; i < 10; i++)
        {
            machine.Tick(owner);
        }

        Tap(machine, owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);
    }

    [Fact]
    public void ASecondTapArrivingTooLateDoesNotFire()
    {
        TaskStateMachine machine = Machine();
        object owner = new();

        Tap(machine, owner);

        // Idle past the window with the bit clear: step 2 wants it set within six ticks.
        machine.Flags = 0;
        for (int i = 0; i < 8; i++)
        {
            machine.Tick(owner);
        }

        Tap(machine, owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);
    }

    [Fact]
    public void TheTwoTriggersAreIndependent()
    {
        TaskStateMachine machine = new(
            null, [null], 0, HumanLocomotionTriggers.Create());
        object owner = new();

        // Double-tapping the second bit fires trigger 1, not trigger 0.
        Tap(machine, owner, HumanLocomotionTriggers.SecondMask);
        Tap(machine, owner, HumanLocomotionTriggers.SecondMask);

        Assert.Equal(1, machine.FiredTrigger);
    }

    private static TaskStateMachine Machine() =>
        new(null, [null], 0, [HumanLocomotionTriggers.DoubleTap(HumanLocomotionTriggers.FirstMask)]);

    private static void Tap(TaskStateMachine machine, object owner, uint? mask = null)
    {
        machine.Flags = mask ?? HumanLocomotionTriggers.FirstMask;
        machine.Tick(owner);
        machine.Flags = 0;
        machine.Tick(owner);
    }
}
