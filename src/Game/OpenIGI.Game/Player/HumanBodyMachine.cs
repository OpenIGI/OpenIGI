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
/// <para>
/// Two entries are the same shape in every class and live here. The flinch state plays the struck zone's
/// limb clip — the row <c>Human+744</c> names in the class's hit-zone table, <c>+4</c> into it — and the
/// scripted-animation state plays the clip the AI wrote to its class's request slot, consuming the
/// request as it enters so only a fresh <c>-1</c> can end it.
/// </para>
/// </remarks>
public abstract class HumanBodyMachine
{
    private readonly IReadOnlyList<HumanHitZone> _hitZones;
    private readonly int _flinchState;
    private readonly int _scriptedAnimationState;
    private int _lastHitZone = -1;
    private int _flinchAnimation = -1;
    private int _scriptedAnimationRequest;
    private int _scriptedAnimation = -1;
    private bool _restart;

    /// <summary>Initializes a machine standing in <paramref name="initialState"/>.</summary>
    /// <param name="table">The class's <c>{animation, mode}</c> table.</param>
    /// <param name="initialState">The state the machine starts in and returns to on <see cref="Reset"/>.</param>
    /// <param name="hitZones">The class's hit-zone table, whose rows name the flinch clips.</param>
    /// <param name="flinchState">The state a flinch enters.</param>
    /// <param name="scriptedAnimationState">The state that plays the clip the AI names.</param>
    protected HumanBodyMachine(
        HumanBodyStateTable table,
        int initialState,
        IReadOnlyList<HumanHitZone> hitZones,
        int flinchState,
        int scriptedAnimationState)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(hitZones);
        StateTable = table;
        InitialState = initialState;
        State = initialState;
        PreviousState = initialState;
        _hitZones = hitZones;
        _flinchState = flinchState;
        _scriptedAnimationState = scriptedAnimationState;
    }

    /// <summary>Gets the <c>{animation, mode}</c> table in force — <c>Human+824</c>.</summary>
    public HumanBodyStateTable StateTable { get; private set; }

    /// <summary>Gets the state the machine starts in and stands in — every class's state 0.</summary>
    public int InitialState { get; }

    /// <summary>Gets the state the machine is in — <c>Human+764</c>.</summary>
    public int State { get; private set; }

    /// <summary>Gets the crouch byte, <c>Human+829</c>, as the state entries leave it.</summary>
    /// <remarks>
    /// It lowers the view (0x465820 puts it 0.35 m over the origin rather than 0.7 m), so a round leaves lower, and
    /// halves the raise of the point a node search takes the human at (0x451380).
    /// </remarks>
    public bool CrouchByte { get; protected set; }

    /// <summary>Gets the state it was in before <see cref="State"/>.</summary>
    public int PreviousState { get; private set; }

    /// <summary>Gets whether a fall ended on the ground this tick: the body left its airborne state for its standing one.</summary>
    /// <remarks>Every class's fall plays the ground's land sound then (<c>0x45C210</c>); a flinch or a death out of the fall does not.</remarks>
    public bool LandedThisTick => ChangedThisTick && State == InitialState && IsAirborne(PreviousState);

    /// <summary>Gets whether the gait entered this tick starts at the stride the last one had reached.</summary>
    /// <remarks>Each class's own gait entries decide which changes of pace carry the walk cycle's phase over (<c>0x4D6520</c>).</remarks>
    public bool KeptStrideThisTick => ChangedThisTick && KeepsStride(PreviousState, State);

    /// <summary>Whether entering a state from another carries the stride's phase over.</summary>
    /// <param name="left">The state left.</param>
    /// <param name="entered">The state entered.</param>
    /// <returns><see langword="true"/> when the new clip starts at the old one's phase.</returns>
    protected virtual bool KeepsStride(int left, int entered) => false;

    /// <summary>Whether a state is the class's fall.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> for the airborne state.</returns>
    protected virtual bool IsAirborne(int state) => false;

    /// <summary>Gets whether the body turns to look along its view — the human's look byte, which <c>0x489F70</c> reads.</summary>
    /// <remarks>
    /// On from the human's creation. The flinch, death and scripted-clip entries turn it off (<c>0x48A330</c>), and only
    /// a flinch or a clip that plays out to its standing state turns it back on (<c>0x48A320</c>): a flinch that ends
    /// in a fall or a death leaves it off.
    /// </remarks>
    public bool LooksAlongView { get; protected set; } = true;

    /// <summary>Gets a value indicating whether <see cref="State"/> changed on the last tick.</summary>
    public bool ChangedThisTick { get; private set; }

    /// <summary>Gets whether the machine is in a state that forwards the trigger to the weapon.</summary>
    /// <remarks>Only these states' per-ticks copy frame slot 6 into <c>Human+1233</c>.</remarks>
    public abstract bool IsFiring { get; }

    /// <summary>Gets whether the machine is in forward locomotion, walking or running.</summary>
    public abstract bool IsMoving { get; }

    /// <summary>
    /// Gets the clip a death in the current posture plays when it is not the general death's to choose, or
    /// <see langword="null"/> when the general death chooses.
    /// </summary>
    public virtual int? PostureDeathAnimation => null;

    /// <summary>Gets whether the body is flinching from a hit, whatever the AI is doing.</summary>
    public virtual bool IsFlinching => State == _flinchState;

    /// <summary>Gets the clip the current state plays, or <c>-1</c> when it names none.</summary>
    public int CurrentAnimation =>
        State == _flinchState ? _flinchAnimation
        : State == _scriptedAnimationState ? _scriptedAnimation
        : StateTable.AnimationOf(State);

    /// <summary>Gets whether the stored scripted-animation request is the <c>-1</c> that ends the state.</summary>
    protected bool ScriptedAnimationCancelled => _scriptedAnimationRequest == -1;

    /// <summary>Puts the machine back to standing.</summary>
    public void Reset() => Advance(InitialState);

    /// <summary>Runs one tick, taking at most one transition.</summary>
    /// <param name="input">What the body reads this tick.</param>
    /// <returns>The state after the tick.</returns>
    public int Tick(in HumanBodyInput input)
    {
        if (input.Flinch && input.HitZone >= 0)
        {
            // The hit handler writes the struck zone to Human+744 before it raises the flinch bit.
            _lastHitZone = input.HitZone;
        }

        return Advance(NextState(input));
    }

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

    /// <summary>Stores the scripted-animation request the class's signal handler reads from its slot.</summary>
    /// <param name="input">What the body reads this tick.</param>
    /// <param name="slot">The frame slot the class keeps the request in.</param>
    /// <remarks>The handler stores the slot's value on every tick the slot is written.</remarks>
    protected void NoteScriptedAnimationRequest(in HumanBodyInput input, int slot)
    {
        if (input.IsSet(slot))
        {
            _scriptedAnimationRequest = (int)input.Frame[slot];
        }
    }

    /// <summary>Installs a different <c>{animation, mode}</c> table, as a class's update may.</summary>
    /// <param name="table">The table to read clips from.</param>
    protected void UseStateTable(HumanBodyStateTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        StateTable = table;
    }

    /// <summary>Asks for the current state to be entered again, restarting its clip.</summary>
    /// <param name="state">The state a per-tick transitions to itself.</param>
    /// <returns><paramref name="state"/>, for the per-tick to return.</returns>
    /// <remarks>
    /// A per-tick that names its own state still runs the transition — previous state, entry, the clip from
    /// the top — which is how a fresh prone flinch restarts the flinch it lands in.
    /// </remarks>
    protected int Restart(int state)
    {
        _restart = true;
        return state;
    }

    /// <summary>Takes a transition the way <c>0x4EC1E0</c> does, or stays.</summary>
    /// <param name="next">The state to be in after this tick.</param>
    /// <returns>The state after the tick.</returns>
    protected int Advance(int next)
    {
        ChangedThisTick = next != State || _restart;
        _restart = false;
        if (ChangedThisTick)
        {
            PreviousState = State;
            State = next;
            Enter(next);
        }

        return State;
    }

    /// <summary>Runs a class's own entry side effects, after the shared ones.</summary>
    /// <param name="state">The state just entered.</param>
    protected virtual void OnEntered(int state)
    {
    }

    private void Enter(int state)
    {
        if (state == _flinchState)
        {
            _flinchAnimation = _lastHitZone >= 0 && _lastHitZone < _hitZones.Count
                ? _hitZones[_lastHitZone].LimbAnimation
                : -1;
        }
        else if (state == _scriptedAnimationState)
        {
            _scriptedAnimation = _scriptedAnimationRequest;
            _scriptedAnimationRequest = 0;
        }

        OnEntered(state);
    }
}
