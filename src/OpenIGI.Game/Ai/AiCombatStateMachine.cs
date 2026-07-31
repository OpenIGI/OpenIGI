using OpenIGI.Game.Player;

namespace OpenIGI.Game.Ai;

/// <summary>
/// The soldier locomotion machine's <b>combat cycle</b> — the states a stationary fighting soldier moves
/// through and, crucially, <b>when</b> it moves, read out of <c>off_53EC10</c>'s per-tick functions.
/// </summary>
/// <remarks>
/// <para>
/// The port used to infer the state each render tick (fire when a burst was live, recover when it was
/// spent), which flipped the clip every tick and made the firing animation stutter and loop. The original
/// runs a real machine (<c>sub_4EC190</c> over <c>off_53EC10</c>): each state <b>holds until its clip
/// finishes</b> — flag bit <c>0x1</c>, the animation-done signal the machine's flag word carries — and only
/// then takes the transition its per-tick names. Reproducing that hold is what stops the loop.
/// </para>
/// <para>
/// <b>The cycle, from the per-ticks</b> (0x457A00 state 0, 0x458810 state 11, 0x45B6E0 state 10, 0x45B060
/// state 6, 0x45B820 state 12): a guard raising its weapon goes <c>0 → 11 → 10 → 6</c>, holds 6 while it
/// keeps firing, and drops to 12 then 0 once it stops. Every state first tests the three that pre-empt
/// everything — off the ground (<see cref="HumanSoldierStates.AirborneState"/>), the flinch bit and the
/// death bit — so a hit or a fall interrupts the firing pose exactly as the original's does.
/// </para>
/// <para>
/// <b>Cross-checked against I.G.I. 2's symbols</b> (which name what I.G.I. 1 strips): the states read here
/// carry those names — <c>HumanSoldier_RunStateStand</c> (state 0), <c>RunStateStandTense</c> (the
/// combat-ready stance), <c>RunStateRunForward</c>/<c>TenseRunForward</c> (the run) — and
/// <c>HumanSoldier_StateSignalHandler</c> is <c>sub_45CCF0</c> by name. <b>But I.G.I. 2 split the machine in
/// two</b> (<c>HumanSoldier_FireUpperBodySignalHandler</c> + <c>MaintainUpperBodyState</c>): firing is an
/// upper-body layer over the locomotion, where I.G.I. 1 folds fire into the one machine as state 6. So the
/// sequel confirms the state <em>meanings</em> but not the structure; this follows I.G.I. 1.
/// </para>
/// <para>
/// The bits are the ones <c>sub_45CCF0</c> packs from the AI's input-frame writes: <see cref="Signals.FirePose"/>
/// is slot 27 (<c>0x2000000</c>), the fire-pose channel the combat arm raises while engaging;
/// <see cref="Signals.FireHeld"/> is slot 6 (<c>0x200</c>), the trigger itself; and
/// <see cref="Signals.AnimationDone"/> is the clip-finished bit <c>0x1</c>. See the original
/// §19.
/// </para>
/// </remarks>
public sealed class AiCombatStateMachine
{
    /// <summary>The flag-word inputs one tick of the machine reads — the bits <c>sub_45CCF0</c> packs.</summary>
    /// <param name="OnGround">Bit <c>0x4</c>: the body has ground under it. Losing it drops to falling.</param>
    /// <param name="Flinch">Bit <c>0x10000</c>: the recoil threshold was passed this tick.</param>
    /// <param name="Dead">Bit <c>0x20000</c>: the damage that killed it landed.</param>
    /// <param name="FirePose">Bit <c>0x2000000</c> (slot 27): the combat arm wants the firing pose.</param>
    /// <param name="FireHeld">Bit <c>0x200</c> (slot 6): the trigger is down this tick.</param>
    /// <param name="AnimationDone">Bit <c>0x1</c>: the current state's clip has finished a cycle.</param>
    public readonly record struct Signals(
        bool OnGround,
        bool Flinch,
        bool Dead,
        bool FirePose,
        bool FireHeld,
        bool AnimationDone);

    /// <summary>Gets the state the machine is in.</summary>
    public int State { get; private set; } = HumanSoldierStates.StandState;

    /// <summary>Gets the state it was in before <see cref="State"/>.</summary>
    public int PreviousState { get; private set; } = HumanSoldierStates.StandState;

    /// <summary>Gets a value indicating whether <see cref="State"/> changed on the last <see cref="Tick"/>.</summary>
    public bool ChangedThisTick { get; private set; }

    /// <summary>Puts the machine back to standing — what re-arming combat does.</summary>
    public void Reset()
    {
        PreviousState = State;
        State = HumanSoldierStates.StandState;
        ChangedThisTick = PreviousState != State;
    }

    /// <summary>Runs one tick, taking at most one transition.</summary>
    /// <param name="signals">The flag-word inputs this tick.</param>
    /// <returns>The state after the tick.</returns>
    public int Tick(in Signals signals)
    {
        int next = Next(signals);
        ChangedThisTick = next != State;
        if (ChangedThisTick)
        {
            PreviousState = State;
            State = next;
        }

        return State;
    }

    private int Next(in Signals s)
    {
        // The three pre-emptions every combat state tests first, in this order.
        if (!s.OnGround)
        {
            return HumanSoldierStates.AirborneState;
        }

        if (s.Flinch)
        {
            return HumanSoldierStates.FlinchState;
        }

        if (s.Dead)
        {
            return HumanSoldierStates.DeathState;
        }

        return State switch
        {
            // 0x457A00: standing raises the weapon into the ready sequence when the fire pose is asked for.
            HumanSoldierStates.StandState => s.FirePose ? RackState : HumanSoldierStates.StandState,

            // 0x458810: the rack holds until its clip finishes, then goes to ready.
            RackState => s.AnimationDone ? ReadyState : RackState,

            // 0x45B6E0: from ready, fire when the trigger is down under the fire pose; drop out otherwise.
            ReadyState => s.FirePose
                ? (s.FireHeld ? HumanSoldierStates.FiringState : ReadyState)
                : HumanSoldierStates.ReloadState,

            // 0x45B060: FIRE holds while the trigger stays down; when the clip finishes AND the trigger is
            // released it goes back to ready (still posed) or drops to recover. This hold is the loop fix.
            HumanSoldierStates.FiringState => s.AnimationDone && !s.FireHeld
                ? (s.FirePose ? ReadyState : HumanSoldierStates.ReloadState)
                : HumanSoldierStates.FiringState,

            // 0x45B820: recover holds until its clip finishes, then stands.
            HumanSoldierStates.ReloadState => s.AnimationDone ? HumanSoldierStates.StandState : HumanSoldierStates.ReloadState,

            // Any other state is outside the combat cycle this machine models; hold it.
            _ => State,
        };
    }

    /// <summary>The weapon-raise state, animation 53 — <c>off_53EC10</c> state 11.</summary>
    public const int RackState = 11;

    /// <summary>The ready state between bursts, animation 54 — <c>off_53EC10</c> state 10.</summary>
    public const int ReadyState = 10;
}
