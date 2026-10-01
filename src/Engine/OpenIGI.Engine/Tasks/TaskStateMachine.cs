namespace OpenIGI.Engine.Tasks;

/// <summary>
/// A state function: one tick of a <see cref="TaskStateMachine"/> state.
/// </summary>
/// <param name="machine">The machine running the state; a state may re-point it at another.</param>
/// <param name="owner">The task the machine belongs to.</param>
public delegate void TaskStateFunction(TaskStateMachine machine, object owner);

/// <summary>
/// The engine's function-pointer state machine — a prologue, a table of state functions, a flag word,
/// and a set of timed triggers over that flag word.
/// </summary>
/// <remarks>
/// <para>
/// This is a general mechanism (<c>0x4EC190</c> registers, <c>0x4EC1E0</c> ticks), not a player-specific
/// one, though the player is where it is best evidenced: <c>HumanPlayer</c>'s constructor builds two of
/// them, a 49-state locomotion machine at <c>Human+748</c> and a 38-state machine of its own at
/// <c>+3320</c>.
/// </para>
/// <para>
/// The order within a tick matters and is reproduced exactly: prologue, then the triggers, then the
/// current state function. A state function that changes the state therefore takes effect on the
/// <em>next</em> tick, while a trigger fired this tick is visible to the state function running after
/// it.
/// </para>
/// </remarks>
public sealed class TaskStateMachine
{
    /// <summary>The value <see cref="PreviousState"/> and <see cref="FiredTrigger"/> hold for "none".</summary>
    public const int NoState = -1;

    private readonly TaskStateFunction?[] _states;
    private readonly TaskStateTrigger[] _triggers;
    private readonly int[] _triggerStep;
    private readonly byte[] _triggerTolerance;
    private readonly TaskStateFunction? _prologue;

    /// <summary>Initializes a new instance of the <see cref="TaskStateMachine"/> class.</summary>
    /// <param name="prologue">Run first every tick, before the triggers. May be <see langword="null"/>.</param>
    /// <param name="states">The state functions, indexed by state.</param>
    /// <param name="initialState">The state to start in.</param>
    /// <param name="triggers">The triggers, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="states"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="initialState"/> is not a state.</exception>
    public TaskStateMachine(
        TaskStateFunction? prologue,
        IReadOnlyList<TaskStateFunction?> states,
        int initialState,
        IReadOnlyList<TaskStateTrigger>? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (states.Count == 0)
        {
            throw new ArgumentException("A state machine needs at least one state.", nameof(states));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(initialState);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(initialState, states.Count);

        _prologue = prologue;
        _states = [.. states];
        _triggers = triggers is null ? [] : [.. triggers];
        _triggerStep = new int[_triggers.Length];
        _triggerTolerance = new byte[_triggers.Length];

        CurrentState = initialState;
        Current = _states[initialState];
        PreviousState = NoState;
        FiredTrigger = NoState;
    }

    /// <summary>Gets the number of states.</summary>
    public int StateCount => _states.Length;

    /// <summary>Gets or sets the machine's flag word — what the triggers test.</summary>
    public uint Flags { get; set; }

    /// <summary>Gets the state the machine was in before <see cref="CurrentState"/>.</summary>
    /// <remarks><see cref="NoState"/> until the state first changes, as the original's <c>-1</c>.</remarks>
    public int PreviousState { get; private set; }

    /// <summary>Gets the current state.</summary>
    public int CurrentState { get; private set; }

    /// <summary>Gets the function the current state runs.</summary>
    public TaskStateFunction? Current { get; set; }

    /// <summary>Gets the trigger that fired during the last <see cref="Tick"/>.</summary>
    /// <remarks><see cref="NoState"/> when none did. Reset at the head of every tick.</remarks>
    public int FiredTrigger { get; private set; }

    /// <summary>Moves to another state, recording the one being left.</summary>
    /// <param name="state">The state to enter.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a state.</exception>
    /// <remarks>
    /// This is what the original's state functions do by hand — write the previous and current state
    /// fields and load the table entry into the running-function slot.
    /// </remarks>
    public void TransitionTo(int state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state, _states.Length);

        PreviousState = CurrentState;
        CurrentState = state;
        Current = _states[state];
    }

    /// <summary>Gets a state's function without entering it.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The table entry.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="state"/> is not a state.</exception>
    public TaskStateFunction? StateFunction(int state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state, _states.Length);
        return _states[state];
    }

    /// <summary>Runs one tick: the prologue, then the triggers, then the current state.</summary>
    /// <param name="owner">The task the machine belongs to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public void Tick(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // The driver samples the trigger count and the flag word *before* the prologue runs, so a
        // prologue that changes the flags does not affect this tick's triggers.
        uint flags = Flags;

        _prologue?.Invoke(this, owner);
        FiredTrigger = NoState;

        for (int i = 0; i < _triggers.Length; i++)
        {
            AdvanceTrigger(i, flags);
        }

        Current?.Invoke(this, owner);
    }

    private void AdvanceTrigger(int index, uint flags)
    {
        TaskStateTrigger trigger = _triggers[index];
        int step = _triggerStep[index];

        if (!trigger.Matches(step, flags))
        {
            // A mismatch on the first step costs nothing: the trigger simply has not started.
            if (step == 0)
            {
                return;
            }

            byte remaining = --_triggerTolerance[index];
            if (remaining != 0)
            {
                return;
            }

            ResetTrigger(index);
            return;
        }

        int next = step + 1;
        _triggerStep[index] = next;
        if (next == trigger.Length)
        {
            FiredTrigger = index;
            ResetTrigger(index);
            return;
        }

        _triggerTolerance[index] = trigger.ToleranceOf(next);
    }

    private void ResetTrigger(int index)
    {
        _triggerStep[index] = 0;
        _triggerTolerance[index] = 0;
    }
}
