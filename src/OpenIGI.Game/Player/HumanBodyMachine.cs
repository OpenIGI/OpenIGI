namespace OpenIGI.Game.Player;

/// <summary>
/// A human class's body state machine — the machine every <c>Human</c> class builds at <c>Human+748</c>.
/// </summary>
/// <remarks>
/// <para>
/// The engine's machine (<c>0x4EC190</c>, ticked by <c>0x4EC1E0</c>) is the same for every class: a
/// flag word the class's signal handler packs, the current and previous state, and one entry function
/// per state that sets the state up and installs its per-tick. A transition makes the previous state the
/// current one, enters the new state and runs its entry at once, so at most one transition is taken per
/// tick. What differs between classes is the state set, their entries and per-ticks, and the table of
/// clips at <c>Human+824</c> — which is what a derived class supplies.
/// </para>
/// </remarks>
public abstract class HumanBodyMachine
{
    /// <summary>Initializes a machine standing in <paramref name="standState"/>.</summary>
    /// <param name="table">The class's <c>{animation, mode}</c> table.</param>
    /// <param name="standState">The state the machine starts in and returns to on <see cref="Reset"/>.</param>
    protected HumanBodyMachine(HumanBodyStateTable table, int standState)
    {
        ArgumentNullException.ThrowIfNull(table);
        StateTable = table;
        InitialState = standState;
        State = standState;
        PreviousState = standState;
    }

    /// <summary>Gets the class's <c>{animation, mode}</c> table.</summary>
    public HumanBodyStateTable StateTable { get; }

    /// <summary>Gets the state the machine starts in and stands in — every class's state 0.</summary>
    public int InitialState { get; }

    /// <summary>Gets the state the machine is in — <c>Human+764</c>.</summary>
    public int State { get; private set; }

    /// <summary>Gets the state it was in before <see cref="State"/>.</summary>
    public int PreviousState { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="State"/> changed on the last tick.</summary>
    public bool ChangedThisTick { get; private set; }

    /// <summary>Gets whether the machine is in a state that forwards the trigger to the weapon.</summary>
    /// <remarks>Only these states' per-ticks copy frame slot 6 into <c>Human+1233</c>.</remarks>
    public abstract bool IsFiring { get; }

    /// <summary>Gets whether the machine is in forward locomotion, walking or running.</summary>
    public abstract bool IsMoving { get; }

    /// <summary>Gets the clip the current state plays, or <c>-1</c> when it names none.</summary>
    /// <remarks>A class whose states name their own clip at entry overrides this.</remarks>
    public virtual int CurrentAnimation => StateTable.AnimationOf(State);

    /// <summary>Puts the machine back to standing.</summary>
    public void Reset() => Advance(InitialState);

    /// <summary>Runs one tick, taking at most one transition.</summary>
    /// <param name="input">What the body reads this tick.</param>
    /// <returns>The state after the tick.</returns>
    public int Tick(in HumanBodyInput input) => Advance(NextState(input));

    /// <summary>Chooses the state the current state's per-tick leaves for.</summary>
    /// <param name="input">What the body reads this tick.</param>
    /// <returns>The next state, or <see cref="State"/> to stay.</returns>
    protected abstract int NextState(in HumanBodyInput input);

    /// <summary>
    /// The test every ordinary per-tick makes first: off the ground, then a flinch, then a kill.
    /// </summary>
    /// <param name="input">What the body reads this tick.</param>
    /// <param name="airborne">The class's airborne state.</param>
    /// <param name="flinch">The class's flinch state.</param>
    /// <param name="death">The class's death state.</param>
    /// <returns>The state the prefix leaves for, or <see langword="null"/> to run the rest of the per-tick.</returns>
    protected static int? Interrupt(in HumanBodyInput input, int airborne, int flinch, int death) =>
        !input.OnGround ? airborne
        : input.Flinch ? flinch
        : input.Dead ? death
        : null;

    /// <summary>Takes a transition the way <c>0x4EC1E0</c> does, or stays.</summary>
    /// <param name="next">The state to be in after this tick.</param>
    /// <returns>The state after the tick.</returns>
    protected int Advance(int next)
    {
        ChangedThisTick = next != State;
        if (ChangedThisTick)
        {
            PreviousState = State;
            State = next;
            OnEntered(next);
        }

        return State;
    }

    /// <summary>Runs a state's entry side effects.</summary>
    /// <param name="state">The state just entered.</param>
    protected virtual void OnEntered(int state)
    {
    }
}
