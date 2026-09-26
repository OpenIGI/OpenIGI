namespace OpenIGI.Game.Player;

/// <summary>
/// A soldier's body machine — <c>off_53EC10</c>'s thirty-eight states, packed by <c>0x45CCF0</c> — and in
/// particular its <b>combat cycle</b>: the states a fighting soldier moves through and <b>when</b> it
/// moves, read out of the per-tick functions.
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
public sealed class HumanSoldierBodyMachine : HumanBodyMachine
{
    // The frame slots 0x45CCF0 packs into the bits this machine reads.
    private const int RollLeftSlot = 2;
    private const int RollRightSlot = 3;
    private const int FireSlot = 6;
    private const int ReloadSlot = 8;
    private const int KneelSlot = 21;
    private const int LieDownSlot = 22;
    private const int FirePoseSlot = 27;
    private const int ThrowGrenadeSlot = 29;
    private const int CloseCombatSlot = 30;

    /// <summary>Initializes a soldier's machine, standing.</summary>
    public HumanSoldierBodyMachine()
        : base(HumanSoldierStates.Table, HumanSoldierStates.StandState)
    {
    }

    /// <summary>The flag-word inputs one tick of the machine reads — the bits <c>sub_45CCF0</c> packs.</summary>
    /// <param name="OnGround">Bit <c>0x4</c>: the body has ground under it. Losing it drops to falling.</param>
    /// <param name="Flinch">Bit <c>0x10000</c>: the recoil threshold was passed this tick.</param>
    /// <param name="Dead">Bit <c>0x20000</c>: the damage that killed it landed.</param>
    /// <param name="FirePose">Bit <c>0x2000000</c> (slot 27): the combat arm wants the firing pose.</param>
    /// <param name="FireHeld">Bit <c>0x200</c> (slot 6): the trigger is down this tick.</param>
    /// <param name="ThrowGrenade">Logical channel 8, slot 29 for the captured patrol soldier.</param>
    /// <param name="CloseCombat">Logical channel 9, slot 30 for the captured patrol soldier.</param>
    /// <param name="Kneel">Logical channel 1, slot 21, packed in signal mask <c>0x80010</c>.</param>
    /// <param name="LieDown">Logical channel 2, slot 22, packed as signal <c>0x100000</c>.</param>
    /// <param name="RollLeft">Raw input slot 2, packed as signal <c>0x20</c>.</param>
    /// <param name="RollRight">Raw input slot 3, packed as signal <c>0x40</c>.</param>
    /// <param name="ReloadPressed">Raw input slot 8, packed as signal <c>0x800</c>.</param>
    /// <param name="AnimationDone">Bit <c>0x1</c>: the current state's clip has finished a cycle.</param>
    /// <param name="MovingForward">Signals <c>0x1000000|0x80</c>: tense forward locomotion.</param>
    public readonly record struct Signals(
        bool OnGround,
        bool Flinch,
        bool Dead,
        bool FirePose,
        bool FireHeld,
        bool ThrowGrenade,
        bool CloseCombat,
        bool Kneel,
        bool LieDown,
        bool RollLeft,
        bool RollRight,
        bool ReloadPressed,
        bool AnimationDone,
        bool MovingForward = false)
    {
        /// <summary>Packs the flag word from the frame the way <c>0x45CCF0</c> does.</summary>
        /// <param name="input">What the body reads this tick.</param>
        /// <returns>The decoded flag word.</returns>
        public static Signals Pack(in HumanBodyInput input) => new(
            input.OnGround,
            input.Flinch,
            input.Dead,
            FirePose: input.IsSet(FirePoseSlot),
            FireHeld: input.IsSet(FireSlot),
            ThrowGrenade: input.IsSet(ThrowGrenadeSlot),
            CloseCombat: input.IsSet(CloseCombatSlot),
            Kneel: input.IsSet(KneelSlot),
            LieDown: input.IsSet(LieDownSlot),
            RollLeft: input.IsSet(RollLeftSlot),
            RollRight: input.IsSet(RollRightSlot),
            ReloadPressed: input.IsSet(ReloadSlot),
            AnimationDone: input.AnimationDone,
            MovingForward: input.MovingForward);
    }

    /// <inheritdoc/>
    /// <remarks>The four states whose per-ticks forward frame slot 6 to the weapon.</remarks>
    public override bool IsFiring => State is HumanSoldierStates.FiringState
        or HumanSoldierStates.RunFiringState
        or HumanSoldierStates.KneelFiringState
        or HumanSoldierStates.ProneFiringState;

    /// <inheritdoc/>
    public override bool IsMoving => State is HumanSoldierStates.RunState or HumanSoldierStates.RunFiringState;

    /// <summary>Runs one tick on an already-packed flag word.</summary>
    /// <param name="signals">The decoded flag word.</param>
    /// <returns>The state after the tick.</returns>
    public int Tick(in Signals signals) => Advance(NextState(signals));

    /// <inheritdoc/>
    protected override int NextState(in HumanBodyInput input) => NextState(Signals.Pack(input));

    private int NextState(in Signals s)
    {
        // The evasive/prone branch has state-specific hit/death outcomes. These are the exact exits
        // from 0x45B900, 0x458BF0, 0x45BA80, 0x45BF90 and 0x45BB40.
        if (!s.OnGround)
        {
            return HumanSoldierStates.AirborneState;
        }

        if (State is HumanSoldierStates.RollLeftState
            or HumanSoldierStates.RollRightState
            or HumanSoldierStates.CrouchDownState
            or HumanSoldierStates.KneelRollLeftState
            or HumanSoldierStates.KneelRollRightState)
        {
            if (s.Dead)
            {
                return 33;
            }

            return s.AnimationDone ? HumanSoldierStates.KneelingState : State;
        }

        if (State == HumanSoldierStates.KneelingState)
        {
            if (s.Flinch)
            {
                return HumanSoldierStates.FlinchState;
            }

            if (s.Dead)
            {
                return 33;
            }

            if (s.LieDown)
            {
                return HumanSoldierStates.KneelToProneState;
            }

            if (!s.Kneel)
            {
                // 0x458BF0 falls straight to state 0 when the kneel signal is released.
                return HumanSoldierStates.StandState;
            }

            if (s.FireHeld)
            {
                return HumanSoldierStates.KneelReadyState;
            }

            if (s.ReloadPressed)
            {
                return HumanSoldierStates.KneelReloadState;
            }

            if (s.RollLeft)
            {
                return HumanSoldierStates.KneelRollLeftState;
            }

            return s.RollRight ? HumanSoldierStates.KneelRollRightState : HumanSoldierStates.KneelingState;
        }

        if (State == HumanSoldierStates.KneelReadyState)
        {
            if (s.Flinch)
            {
                return HumanSoldierStates.FlinchState;
            }

            if (s.Dead)
            {
                return 33;
            }

            if (!s.FirePose)
            {
                return HumanSoldierStates.KneelingState;
            }

            return s.AnimationDone && s.FireHeld
                ? HumanSoldierStates.KneelFiringState
                : HumanSoldierStates.KneelReadyState;
        }

        if (State == HumanSoldierStates.KneelFiringState)
        {
            if (s.Flinch)
            {
                return HumanSoldierStates.FlinchState;
            }

            if (s.Dead)
            {
                return 33;
            }

            return s.AnimationDone && !s.FireHeld
                ? (s.FirePose ? HumanSoldierStates.KneelReadyState : HumanSoldierStates.KneelingState)
                : HumanSoldierStates.KneelFiringState;
        }

        if (State == HumanSoldierStates.KneelReloadState)
        {
            if (s.Flinch)
            {
                return HumanSoldierStates.FlinchState;
            }

            if (s.Dead)
            {
                return 33;
            }

            return s.AnimationDone ? HumanSoldierStates.KneelingState : State;
        }

        if (State == HumanSoldierStates.KneelToProneState)
        {
            if (s.Dead)
            {
                return 33;
            }

            return s.AnimationDone ? HumanSoldierStates.ProneState : State;
        }

        if (State == HumanSoldierStates.LieDownState)
        {
            if (s.Dead)
            {
                return 34;
            }

            return s.AnimationDone ? HumanSoldierStates.ProneState : State;
        }

        if (State == HumanSoldierStates.ProneState)
        {
            if (s.Flinch)
            {
                return 31;
            }

            if (s.Dead)
            {
                return 34;
            }

            return s.LieDown
                ? (s.FireHeld
                    ? HumanSoldierStates.ProneFiringState
                    : s.ReloadPressed
                        ? HumanSoldierStates.ProneReloadState
                        : HumanSoldierStates.ProneState)
                : HumanSoldierStates.GetUpState;
        }

        if (State == HumanSoldierStates.ProneFiringState)
        {
            if (s.Flinch)
            {
                return 31;
            }

            if (s.Dead)
            {
                return 34;
            }

            return s.AnimationDone && !s.FireHeld
                ? HumanSoldierStates.ProneState
                : HumanSoldierStates.ProneFiringState;
        }

        if (State == HumanSoldierStates.ProneReloadState)
        {
            if (s.Flinch)
            {
                return 31;
            }

            if (s.Dead)
            {
                return 34;
            }

            return s.AnimationDone ? HumanSoldierStates.ProneState : HumanSoldierStates.ProneReloadState;
        }

        if (State == HumanSoldierStates.GetUpState)
        {
            if (s.Dead)
            {
                return 34;
            }

            return s.AnimationDone ? HumanSoldierStates.StandState : State;
        }

        if (s.Flinch)
        {
            return HumanSoldierStates.FlinchState;
        }

        if (s.Dead)
        {
            return HumanSoldierStates.DeathState;
        }

        // 0x457FC0: tense forward run. A held trigger changes 17 -> 18 before the ordinary
        // movement exits; raw rolls are considered while the forward-run signals remain set.
        if (State == HumanSoldierStates.RunState)
        {
            if (s.Kneel)
            {
                return HumanSoldierStates.CrouchDownState;
            }

            if (s.LieDown)
            {
                return HumanSoldierStates.LieDownState;
            }

            if (s.FireHeld)
            {
                return HumanSoldierStates.RunFiringState;
            }

            if (s.MovingForward)
            {
                return s.RollLeft
                    ? HumanSoldierStates.RollLeftState
                    : s.RollRight
                        ? HumanSoldierStates.RollRightState
                        : HumanSoldierStates.RunState;
            }

            return HumanSoldierStates.StandState;
        }

        // 0x45AB00: state 18 forwards slot 6 to the weapon. Releasing the trigger while the tense
        // forward signals remain set returns to 17. This is a running-fire state, not a diagonal run.
        if (State == HumanSoldierStates.RunFiringState)
        {
            if (s.MovingForward)
            {
                if (s.RollLeft)
                {
                    return HumanSoldierStates.RollLeftState;
                }

                if (s.RollRight)
                {
                    return HumanSoldierStates.RollRightState;
                }

                return s.FireHeld ? HumanSoldierStates.RunFiringState : HumanSoldierStates.RunState;
            }

            return HumanSoldierStates.StandState;
        }

        if (State == HumanSoldierStates.DisplayReloadState)
        {
            return s.AnimationDone
                ? (s.FirePose ? HumanSoldierStates.ReadyState : HumanSoldierStates.StandState)
                : HumanSoldierStates.DisplayReloadState;
        }

        return State switch
        {
            // 0x457A00: standing raises the weapon into the ready sequence when the fire pose is asked for.
            // 0x457A00 precedence for the signals this runtime currently produces: kneel precedes lie
            // down, fire pose precedes grenade/reload/melee, and raw roll slots are considered last.
            HumanSoldierStates.StandState => s.Kneel
                ? HumanSoldierStates.CrouchDownState
                : s.LieDown
                    ? HumanSoldierStates.LieDownState
                    : s.FirePose
                        ? HumanSoldierStates.RackState
                        : s.ThrowGrenade
                            ? HumanSoldierStates.GrenadeThrowState
                            : s.ReloadPressed
                                ? HumanSoldierStates.DisplayReloadState
                                : s.CloseCombat
                                    ? HumanSoldierStates.CloseCombatState
                                    : s.RollLeft
                                    ? HumanSoldierStates.RollLeftState
                                    : s.RollRight
                                        ? HumanSoldierStates.RollRightState
                                            : s.MovingForward
                                                ? HumanSoldierStates.RunState
                                                : HumanSoldierStates.StandState,

            // mission1-parity.log: channel 8/slot 29 enters state 8/animation 58. The child stops
            // writing on the next AI tick, but the body owns the state until its one-shot clip ends.
            HumanSoldierStates.GrenadeThrowState => s.AnimationDone
                ? HumanSoldierStates.StandState
                : HumanSoldierStates.GrenadeThrowState,

            // The same measured lifecycle for channel 9/slot 30: state 9, animation 70.
            HumanSoldierStates.CloseCombatState => s.AnimationDone
                ? HumanSoldierStates.StandState
                : HumanSoldierStates.CloseCombatState,

            // 0x458810: the rack holds until its clip finishes, then goes to ready.
            HumanSoldierStates.RackState => s.AnimationDone ? HumanSoldierStates.ReadyState : HumanSoldierStates.RackState,

            // 0x45B6E0: from ready, fire when the trigger is down under the fire pose; drop out otherwise.
            HumanSoldierStates.ReadyState => s.FirePose
                ? (s.FireHeld ? HumanSoldierStates.FiringState : HumanSoldierStates.ReadyState)
                : HumanSoldierStates.ReloadState,

            // 0x45B060: FIRE holds while the trigger stays down; when the clip finishes AND the trigger is
            // released it goes back to ready (still posed) or drops to recover. This hold is the loop fix.
            HumanSoldierStates.FiringState => s.AnimationDone && !s.FireHeld
                ? (s.FirePose ? HumanSoldierStates.ReadyState : HumanSoldierStates.ReloadState)
                : HumanSoldierStates.FiringState,

            // 0x45B820: recover holds until its clip finishes, then stands.
            HumanSoldierStates.ReloadState => s.AnimationDone ? HumanSoldierStates.StandState : HumanSoldierStates.ReloadState,

            // Any other state is outside the combat cycle this machine models; hold it.
            _ => State,
        };
    }
}
