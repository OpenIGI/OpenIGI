using OpenIGI.Engine.Tasks;
using Xunit;

namespace OpenIGI.Engine.Tests;

/// <summary>
/// Pins the engine's function-pointer state machine and, in particular, the trigger recogniser —
/// the timed condition sequence over the flag word that <c>0x4EC1E0</c> advances every tick.
/// </summary>
public sealed class TaskStateMachineTests
{
    private const uint Bit0 = 1;
    private const uint Bit1 = 2;

    [Fact]
    public void ItStartsInTheRegisteredStateWithNoHistory()
    {
        TaskStateMachine machine = Build(states: 49, initial: 0);

        Assert.Equal(49, machine.StateCount);
        Assert.Equal(0, machine.CurrentState);
        Assert.Equal(TaskStateMachine.NoState, machine.PreviousState);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);
    }

    [Fact]
    public void ATickRunsThePrologueThenTheStateAndInThatOrder()
    {
        List<string> order = [];
        TaskStateFunction prologue = (_, _) => order.Add("prologue");
        TaskStateFunction state = (_, _) => order.Add("state");

        TaskStateMachine machine = new(prologue, [state], 0);
        machine.Tick(new object());

        Assert.Equal(["prologue", "state"], order);
    }

    [Fact]
    public void AStateChangeTakesEffectOnTheNextTick()
    {
        List<int> ran = [];
        TaskStateFunction zero = (m, _) =>
        {
            ran.Add(0);
            m.TransitionTo(1);
        };
        TaskStateFunction one = (_, _) => ran.Add(1);

        TaskStateMachine machine = new(null, [zero, one], 0);
        object owner = new();

        machine.Tick(owner);
        machine.Tick(owner);

        // State 0 runs to completion on the tick it transitions; state 1 only runs on the next one.
        Assert.Equal([0, 1], ran);
        Assert.Equal(0, machine.PreviousState);
        Assert.Equal(1, machine.CurrentState);
    }

    [Fact]
    public void ASingleStepTriggerFiresOnTheTickItsConditionHolds()
    {
        TaskStateTrigger trigger = new([new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: Bit0)]);
        TaskStateMachine machine = new(null, [null], 0, [trigger]);
        object owner = new();

        machine.Tick(owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);

        machine.Flags = Bit0;
        machine.Tick(owner);
        Assert.Equal(0, machine.FiredTrigger);

        // It reset on firing, so holding the condition fires it again rather than latching.
        machine.Tick(owner);
        Assert.Equal(0, machine.FiredTrigger);
    }

    [Fact]
    public void AMultiStepTriggerNeedsItsStepsInOrder()
    {
        // "bit 0 set, then bit 0 clear" — the shape of a tap.
        TaskStateTrigger trigger = new(
        [
            new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: Bit0),
            new TaskStateStep(RequireSet: false, Tolerance: 4, Mask: Bit0),
        ]);
        TaskStateMachine machine = new(null, [null], 0, [trigger]);
        object owner = new();

        machine.Flags = Bit0;
        machine.Tick(owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);

        machine.Flags = 0;
        machine.Tick(owner);
        Assert.Equal(0, machine.FiredTrigger);
    }

    [Fact]
    public void ToleranceCountsDownOnlyOnceAStepHasBeenReached()
    {
        TaskStateTrigger trigger = new(
        [
            new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: Bit0),
            new TaskStateStep(RequireSet: true, Tolerance: 2, Mask: Bit1),
        ]);
        TaskStateMachine machine = new(null, [null], 0, [trigger]);
        object owner = new();

        // A mismatch at step 0 costs nothing at all: the trigger simply has not started, and no
        // tolerance is spent. Ten idle ticks leave it exactly where it was.
        for (int i = 0; i < 10; i++)
        {
            machine.Tick(owner);
        }

        machine.Flags = Bit0;
        machine.Tick(owner);          // step 0 matches, tolerance 2 loaded from step 1

        machine.Flags = 0;
        machine.Tick(owner);          // miss: tolerance 2 -> 1, still alive
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);

        machine.Flags = Bit1;
        machine.Tick(owner);          // recovered in time
        Assert.Equal(0, machine.FiredTrigger);
    }

    [Fact]
    public void ARunOutOfToleranceResetsTheTriggerToItsFirstStep()
    {
        TaskStateTrigger trigger = new(
        [
            new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: Bit0),
            new TaskStateStep(RequireSet: true, Tolerance: 2, Mask: Bit1),
        ]);
        TaskStateMachine machine = new(null, [null], 0, [trigger]);
        object owner = new();

        machine.Flags = Bit0;
        machine.Tick(owner);

        machine.Flags = 0;
        machine.Tick(owner);          // 2 -> 1
        machine.Tick(owner);          // 1 -> 0, reset

        // Back at step 0, so bit 1 alone can no longer complete it.
        machine.Flags = Bit1;
        machine.Tick(owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);
    }

    [Fact]
    public void TheTriggersSeeTheFlagsAsTheyWereBeforeThePrologue()
    {
        // The driver reads the flag word into a local before calling the prologue, so a prologue that
        // sets a flag does not let a trigger see it until the following tick.
        TaskStateTrigger trigger = new([new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: Bit0)]);
        TaskStateMachine machine = new((m, _) => m.Flags = Bit0, [null], 0, [trigger]);
        object owner = new();

        machine.Tick(owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);

        machine.Tick(owner);
        Assert.Equal(0, machine.FiredTrigger);
    }

    [Fact]
    public void FiredTriggerIsClearedAtTheHeadOfEveryTick()
    {
        TaskStateTrigger trigger = new([new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: Bit0)]);
        TaskStateMachine machine = new(null, [null], 0, [trigger]);
        object owner = new();

        machine.Flags = Bit0;
        machine.Tick(owner);
        Assert.Equal(0, machine.FiredTrigger);

        machine.Flags = 0;
        machine.Tick(owner);
        Assert.Equal(TaskStateMachine.NoState, machine.FiredTrigger);
    }

    [Fact]
    public void AZeroMaskStepDistinguishesTheTwoPolarities()
    {
        // "require set" with an empty mask is vacuously true; "require clear" with an empty mask is
        // also true. The polarity only matters once the mask has bits, but both arms must be reachable.
        TaskStateTrigger set = new([new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: 0)]);
        TaskStateTrigger clear = new([new TaskStateStep(RequireSet: false, Tolerance: 0, Mask: 0)]);

        Assert.True(set.Matches(0, 0xFFFFFFFF));
        Assert.True(clear.Matches(0, 0xFFFFFFFF));

        TaskStateTrigger partial = new([new TaskStateStep(RequireSet: true, Tolerance: 0, Mask: Bit0 | Bit1)]);
        Assert.False(partial.Matches(0, Bit0));
        Assert.True(partial.Matches(0, Bit0 | Bit1));
    }

    private static TaskStateMachine Build(int states, int initial) =>
        new(null, new TaskStateFunction?[states], initial);
}
