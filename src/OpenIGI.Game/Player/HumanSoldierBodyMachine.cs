using static OpenIGI.Game.Player.HumanSoldierStates;

namespace OpenIGI.Game.Player;

/// <summary>
/// A soldier's body machine — <c>off_53EC10</c>'s thirty-eight states, packed by <c>0x45CCF0</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every state is transcribed from its entry and per-tick. The per-ticks share a prefix: off the ground
/// (<see cref="Signal.OnGround"/> clear) leaves for <see cref="AirborneState"/>, then a flinch pulse for
/// <see cref="FlinchState"/>, then a kill for the death state the posture calls for — 32 standing, 33
/// kneeling, 34 prone. The transitional states (rolls, kneel and prone changes) test the ground and the
/// kill but no flinch, and prone ones flinch into the prone flinch, 31.
/// </para>
/// <para>
/// <b>The combat cycle</b> is <c>0 → 11 → 10 → 6</c>: the fire pose (slot 27) racks the weapon, the rack
/// holds until its clip ends, the trigger (slot 6) fires from ready, and the fire holds until its clip ends
/// with the trigger released, returning to ready while the pose holds or recovering (12) to standing.
/// Only 6, 18, 22 and 27 forward the trigger to the weapon.
/// </para>
/// <para>
/// I.G.I. 2's symbols name what I.G.I. 1 strips: <c>HumanSoldier_StateSignalHandler</c> is
/// <c>0x45CCF0</c>, state 0 is <c>RunStateStand</c>, and 17 is the tense forward run. I.G.I. 2 split firing
/// into an upper-body layer; I.G.I. 1 folds it into this one machine, which is what this follows.
/// </para>
/// </remarks>
public sealed class HumanSoldierBodyMachine : HumanBodyMachine
{
    // The request slot 0x45CCF0 stores at +3340 when it packs 0x200000.
    private const int ScriptedAnimationSlot = 23;

    // State 31 counts its first twenty ticks before a fresh flinch may restart it (0x45C370).
    private const int ProneFlinchLockTicks = 20;

    // Every bit 0x45C7E0 ends a scripted clip on: 0xFD81FE1 -- the actions, movement, posture and combat
    // requests, and the clip's own end. Not the request bit itself and not the run byte.
    private const Signal EndsScriptedAnimation = (Signal)0xFD81FE1;

    private readonly bool _pistol;
    private int _proneFlinchTicks;

    /// <summary>Initializes a soldier's machine, standing.</summary>
    public HumanSoldierBodyMachine()
        : this(pistol: false)
    {
    }

    private HumanSoldierBodyMachine(bool pistol)
        : base(
            pistol ? PistolTable : Table,
            StandState,
            pistol ? HumanHitZoneTable.PistolSoldier : HumanHitZoneTable.Soldier,
            FlinchState,
            ScriptedAnimationState)
    {
        _pistol = pistol;
    }

    /// <summary>Builds the pistol soldier's machine — <c>off_53E8B8</c>, standing.</summary>
    /// <returns>The machine.</returns>
    /// <remarks>
    /// <c>HumanSoldierPistol</c> (<c>0x459880</c>) runs the soldier's 38 states and most of its per-ticks
    /// over its own clips. Its own per-ticks for the rolls, the kneel and prone changes and the kneel to prone
    /// (<c>0x458DD0</c>, <c>0x458950</c>, <c>0x458A30</c>, <c>0x458B10</c>, <c>0x459040</c>) test the flinch
    /// the soldier's do not, and its posture deaths play 29 and 30.
    /// </remarks>
    public static HumanSoldierBodyMachine ForPistol() => new(pistol: true);

    /// <summary>The flag word <c>0x45CCF0</c> packs — <c>machine+8</c>, bit for bit.</summary>
    [Flags]
    public enum Signal : uint
    {
        /// <summary>No bit.</summary>
        None = 0,

        /// <summary><c>0x1</c>: the current clip finished a cycle.</summary>
        AnimationDone = 0x1,

        /// <summary><c>0x4</c>: the body has ground under it.</summary>
        OnGround = 0x4,

        /// <summary><c>0x10</c>: part of the kneel mask <c>0x80010</c>; nothing in the handler sets it.</summary>
        KneelExternal = 0x10,

        /// <summary><c>0x20</c>: frame slot 2, the roll left.</summary>
        RollLeft = 0x20,

        /// <summary><c>0x40</c>: frame slot 3, the roll right.</summary>
        RollRight = 0x40,

        /// <summary><c>0x80</c>: frame slot 4, forward.</summary>
        Forward = 0x80,

        /// <summary><c>0x200</c>: frame slot 6, the trigger.</summary>
        Fire = 0x200,

        /// <summary><c>0x800</c>: frame slot 8, the displayed reload.</summary>
        Reload = 0x800,

        /// <summary><c>0x10000</c>: a hit passed the recoil threshold this tick — a one-tick pulse.</summary>
        Flinch = 0x10000,

        /// <summary><c>0x20000</c>: the killing damage landed.</summary>
        Dead = 0x20000,

        /// <summary><c>0x40000</c>: the run byte at <c>+3328</c>.</summary>
        Running = 0x40000,

        /// <summary><c>0x80000</c>: frame slot 21, kneel.</summary>
        Kneel = 0x80000,

        /// <summary><c>0x100000</c>: frame slot 22, lie down.</summary>
        LieDown = 0x100000,

        /// <summary><c>0x200000</c>: frame slot 23, play the clip it names.</summary>
        ScriptedAnimation = 0x200000,

        /// <summary><c>0x400000</c>: frame slot 24, use an object.</summary>
        UseObject = 0x400000,

        /// <summary><c>0x800000</c>: frame slot 25, walk.</summary>
        Walk = 0x800000,

        /// <summary><c>0x1000000</c>: frame slot 26, the tense run.</summary>
        TenseRun = 0x1000000,

        /// <summary><c>0x2000000</c>: frame slot 27, the fire pose.</summary>
        FirePose = 0x2000000,

        /// <summary><c>0x4000000</c>: frame slot 29, throw a grenade.</summary>
        ThrowGrenade = 0x4000000,

        /// <summary><c>0x8000000</c>: frame slot 30, close combat.</summary>
        CloseCombat = 0x8000000,
    }

    // The frame slot each packed bit comes from.
    private static readonly (int Slot, Signal Bit)[] PackedSlots =
    [
        (2, Signal.RollLeft), (3, Signal.RollRight), (4, Signal.Forward), (6, Signal.Fire), (8, Signal.Reload),
        (21, Signal.Kneel), (22, Signal.LieDown), (23, Signal.ScriptedAnimation), (24, Signal.UseObject),
        (25, Signal.Walk), (26, Signal.TenseRun), (27, Signal.FirePose), (29, Signal.ThrowGrenade),
        (30, Signal.CloseCombat),
    ];

    /// <inheritdoc/>
    /// <remarks>The four states whose per-ticks forward frame slot 6 to the weapon.</remarks>
    public override bool IsFiring => State is FiringState or RunFiringState or KneelFiringState or ProneFiringState;

    /// <inheritdoc/>
    public override bool IsMoving => State is WalkState or ForwardState or FastForwardState or RunState or RunFiringState;

    /// <inheritdoc/>
    public override bool IsFlinching => State is FlinchState or ProneFlinchState;

    /// <inheritdoc/>
    /// <remarks>
    /// Rolls, kneels and their changes die into 33, whose entry <c>0x45C5A0</c> plays 24; prone states and
    /// theirs into 34, whose <c>0x45C650</c> plays 25. Neither turns or draws.
    /// </remarks>
    public override int? PostureDeathAnimation => State switch
    {
        RollLeftState or RollRightState or CrouchDownState or StandUpState or KneelingState
            or KneelRollLeftState or KneelRollRightState or KneelFiringState or KneelReloadState
            or KneelReadyState or KneelToProneState => _pistol ? PistolKneelDeathAnimation : KneelDeathAnimation,
        LieDownState or GetUpState or ProneState or ProneFiringState or ProneReloadState
            or ProneFlinchState => _pistol ? PistolProneDeathAnimation : ProneDeathAnimation,
        _ => null,
    };

    // The literals 0x45C5A0 and 0x45C650 play, and the pistol soldier's 0x459520 and 0x4595D0.
    private const int KneelDeathAnimation = 24;
    private const int ProneDeathAnimation = 25;
    private const int PistolKneelDeathAnimation = 29;
    private const int PistolProneDeathAnimation = 30;

    /// <summary>Packs the flag word from the frame the way <c>0x45CCF0</c> does.</summary>
    /// <param name="input">What the body reads this tick.</param>
    /// <returns>The flag word.</returns>
    /// <remarks>
    /// The port's AI moves its bodies itself rather than through slots 4 and 26, so a body it reports moving
    /// in combat is packed as the tense forward run, <see cref="Signal.Forward"/> with
    /// <see cref="Signal.TenseRun"/> — the state 17 retail's Follow runs in.
    /// </remarks>
    public static Signal Pack(in HumanBodyInput input)
    {
        Signal s = Signal.None;
        foreach ((int slot, Signal bit) in PackedSlots)
        {
            if (input.IsSet(slot))
            {
                s |= bit;
            }
        }

        if (input.AnimationDone)
        {
            s |= Signal.AnimationDone;
        }

        if (input.OnGround)
        {
            s |= Signal.OnGround;
        }

        if (input.Flinch)
        {
            s |= Signal.Flinch;
        }

        if (input.Dead)
        {
            s |= Signal.Dead;
        }

        if (input.Running)
        {
            s |= Signal.Running;
        }

        if (input.MovingForward)
        {
            s |= Signal.Forward | Signal.TenseRun;
        }

        return s;
    }

    /// <summary>Installs the clip table the soldier's update selects for its brain's type.</summary>
    /// <param name="table">
    /// <see cref="HumanSoldierStates.Table"/>, or one of its Priboi, civilian and Spetsnaz variants.
    /// </param>
    /// <remarks>
    /// <c>0x45CF90</c> writes <c>Human+824</c> from <c>HumanAI+15672</c> on every update: AI type 5 (Priboi)
    /// takes <c>0x53EF70</c>, 6 (civilian) <c>0x53F0A0</c>, 23..28 (the Spetsnaz) <c>0x53EE40</c>, and every
    /// other type the base table. The type does not change after the AI is configured, so neither does this.
    /// </remarks>
    public void UseClips(HumanBodyStateTable table) => UseStateTable(table);

    /// <summary>Runs one tick on an already-packed flag word.</summary>
    /// <param name="signals">The flag word.</param>
    /// <returns>The state after the tick.</returns>
    public int Tick(Signal signals) => Advance(NextState(signals));

    /// <inheritdoc/>
    protected override int NextState(in HumanBodyInput input)
    {
        NoteScriptedAnimationRequest(input, ScriptedAnimationSlot);
        return NextState(Pack(input));
    }

    private static bool Has(Signal s, Signal bit) => (s & bit) != 0;

    // X(a:b) in the per-ticks: back to the combat state while the fire pose holds, else the fallback.
    private static int WhilePosed(Signal s, int posed, int otherwise) => Has(s, Signal.FirePose) ? posed : otherwise;

    // The standing forward gaits: 0x40000 set is the run clip 57 (state 3), clear the forward clip 3 (state 2).
    private static int Gait(Signal s) => Has(s, Signal.Running) ? FastForwardState : ForwardState;

    // G, F, D in the per-ticks, with the death each posture falls into.
    private static int? Prefix(Signal s, int death, bool flinches = true) =>
        !Has(s, Signal.OnGround) ? AirborneState
        : flinches && Has(s, Signal.Flinch) ? FlinchState
        : Has(s, Signal.Dead) ? death
        : null;

    // The rolls and posture changes into and out of the kneel: no flinch on a soldier, the flinch (30) on the
    // pistol soldier.
    private int? KneelChangePrefix(Signal s) => Prefix(s, KneelDeathState, flinches: _pistol);

    // Lying down and getting up: no flinch on a soldier, the prone flinch (31) on the pistol soldier.
    private int? ProneChangePrefix(Signal s) => _pistol ? PronePrefix(s) : Prefix(s, ProneDeathState, flinches: false);

    // Whether slot 24 was held on the last tick -- the previous frame's +368.
    private bool _useObjectHeld;

    private int NextState(Signal s)
    {
        // The frame is copied to the previous one (+272) every tick; state 36 reads slot 24 in both.
        bool useObjectPressed = Has(s, Signal.UseObject) && !_useObjectHeld;
        _useObjectHeld = Has(s, Signal.UseObject);

        return State switch
        {
            // 0x457A00.
            StandState => Prefix(s, DeathState) ?? Stand(s),

            // 0x457D30: the walk keeps going only while slot 25 holds; letting it go takes the forward gait.
            WalkState => Prefix(s, DeathState) ?? UprightAction(s) ?? (Has(s, Signal.Walk)
                ? Roll(s) ?? (Has(s, Signal.Forward) ? WalkState : StandState)
                : Gait(s)),

            // 0x45AD10.
            ForwardState => Prefix(s, DeathState) ?? UprightAction(s)
                ?? (Has(s, Signal.TenseRun) ? RunState
                    : Has(s, Signal.Walk) ? WalkState
                    : Has(s, Signal.Running) ? FastForwardState
                    : Roll(s) ?? (Has(s, Signal.Forward) ? ForwardState : StandState)),

            // 0x4582C0.
            FastForwardState => Prefix(s, DeathState) ?? UprightAction(s)
                ?? (Has(s, Signal.TenseRun) ? RunState
                    : Has(s, Signal.Walk) ? WalkState
                    : !Has(s, Signal.Running) ? ForwardState
                    : Roll(s) ?? (Has(s, Signal.Forward) ? FastForwardState : StandState)),

            // 0x45B900: the rolls end kneeling, and test no flinch (the pistol's 0x458DD0 does).
            RollLeftState or RollRightState => KneelChangePrefix(s)
                ?? (Has(s, Signal.AnimationDone) ? KneelingState : State),

            // 0x45B060: the fire holds until its clip ends with the trigger released.
            FiringState => Prefix(s, DeathState)
                ?? (Has(s, Signal.AnimationDone) && !Has(s, Signal.Fire) ? WhilePosed(s, ReadyState, ReloadState) : FiringState),

            // 0x45B1C0, 0x45B2F0: the displayed reload and the grenade go back to ready while posed.
            DisplayReloadState or GrenadeThrowState => Prefix(s, DeathState)
                ?? (Has(s, Signal.AnimationDone) ? WhilePosed(s, ReadyState, StandState) : State),

            // 0x45B530.
            CloseCombatState => Prefix(s, DeathState) ?? (Has(s, Signal.AnimationDone) ? StandState : CloseCombatState),

            // 0x45B6E0: ready fires on the trigger while posed, and recovers once the pose drops.
            ReadyState => Prefix(s, DeathState)
                ?? (Has(s, Signal.FirePose) ? (Has(s, Signal.Fire) ? FiringState : ReadyState) : ReloadState),

            // 0x458810 and 0x45B820.
            RackState => Prefix(s, DeathState) ?? (Has(s, Signal.AnimationDone) ? ReadyState : RackState),
            ReloadState => Prefix(s, DeathState) ?? (Has(s, Signal.AnimationDone) ? StandState : ReloadState),

            // 0x45B900, 0x45B9C0: kneeling down and standing back up test no flinch.
            CrouchDownState => KneelChangePrefix(s)
                ?? (Has(s, Signal.AnimationDone) ? KneelingState : CrouchDownState),
            StandUpState => KneelChangePrefix(s)
                ?? (Has(s, Signal.AnimationDone) ? StandState : StandUpState),

            // 0x45BA80, 0x45BB40.
            LieDownState => ProneChangePrefix(s)
                ?? (Has(s, Signal.AnimationDone) ? ProneState : LieDownState),
            GetUpState => ProneChangePrefix(s)
                ?? (Has(s, Signal.AnimationDone) ? StandState : GetUpState),

            // 0x457FC0: the tense run fires on the move, and leaves the tense gait for walk or the forward gaits.
            RunState => Prefix(s, DeathState) ?? (Has(s, Signal.Kneel) ? CrouchDownState
                : Has(s, Signal.LieDown) ? LieDownState
                : Has(s, Signal.Fire) ? RunFiringState
                : Has(s, Signal.TenseRun) ? Roll(s) ?? (Has(s, Signal.Forward) ? RunState : StandState)
                : Has(s, Signal.Walk) ? WalkState
                : Gait(s)),

            // 0x45AB00: back to the tense run once the clip ends with the trigger released; no rolls.
            RunFiringState => Prefix(s, DeathState) ?? (Has(s, Signal.TenseRun)
                ? (Has(s, Signal.Forward)
                    ? (Has(s, Signal.AnimationDone) && !Has(s, Signal.Fire) ? RunState : RunFiringState)
                    : StandState)
                : Gait(s)),

            // 0x458BF0.
            KneelingState => Prefix(s, KneelDeathState) ?? (Has(s, Signal.LieDown) ? KneelToProneState
                : !IsKneelHeld(s) ? StandState
                : Has(s, Signal.Fire) ? KneelReadyState
                : Has(s, Signal.Reload) ? KneelReloadState
                : Has(s, Signal.RollLeft) ? KneelRollLeftState
                : Has(s, Signal.RollRight) ? KneelRollRightState
                : KneelingState),

            // 0x45B900 again for the kneeling rolls.
            KneelRollLeftState or KneelRollRightState => KneelChangePrefix(s)
                ?? (Has(s, Signal.AnimationDone) ? KneelingState : State),

            // 0x458EB0.
            KneelFiringState => Prefix(s, KneelDeathState)
                ?? (Has(s, Signal.AnimationDone) && !Has(s, Signal.Fire) ? WhilePosed(s, KneelReadyState, KneelingState) : KneelFiringState),

            // 0x45BCC0.
            KneelReloadState => Prefix(s, KneelDeathState) ?? (Has(s, Signal.AnimationDone) ? KneelingState : KneelReloadState),

            // 0x45BDC0: the kneeling ready fires on a finished clip with the trigger held.
            KneelReadyState => Prefix(s, KneelDeathState) ?? (Has(s, Signal.FirePose)
                ? (Has(s, Signal.AnimationDone) && Has(s, Signal.Fire) ? KneelFiringState : KneelReadyState)
                : KneelingState),

            // 0x45BED0.
            KneelToProneState => KneelChangePrefix(s)
                ?? (Has(s, Signal.AnimationDone) ? ProneState : KneelToProneState),

            // 0x45BF90, 0x45C0C0, 0x459180: prone flinches into the prone flinch and dies prone.
            ProneState => PronePrefix(s) ?? (Has(s, Signal.LieDown)
                ? (Has(s, Signal.Fire) ? ProneFiringState : Has(s, Signal.Reload) ? ProneReloadState : ProneState)
                : GetUpState),
            ProneFiringState => PronePrefix(s)
                ?? (Has(s, Signal.AnimationDone) && !Has(s, Signal.Fire) ? ProneState : ProneFiringState),
            ProneReloadState => PronePrefix(s) ?? (Has(s, Signal.AnimationDone) ? ProneState : ProneReloadState),

            // 0x45C210: a fall tests the flinch and the kill before it looks for the ground.
            AirborneState => Has(s, Signal.Flinch) ? FlinchState
                : Has(s, Signal.Dead) ? DeathState
                : Has(s, Signal.OnGround) ? StandState
                : AirborneState,

            // 0x4592F0: the flinch plays out and stands; a kneeling soldier stands to flinch.
            FlinchState => !Has(s, Signal.OnGround) ? AirborneState
                : Has(s, Signal.Dead) ? DeathState
                : Has(s, Signal.AnimationDone) ? StandState
                : FlinchState,

            // 0x45C370: after twenty ticks a fresh flinch restarts it.
            ProneFlinchState => !Has(s, Signal.OnGround) ? AirborneState
                : Has(s, Signal.Dead) ? ProneDeathState
                : ++_proneFlinchTicks > ProneFlinchLockTicks && Has(s, Signal.Flinch) ? Restart(ProneFlinchState)
                : Has(s, Signal.AnimationDone) ? ProneState
                : ProneFlinchState,

            // 0x45C7E0.
            ScriptedAnimationState => Prefix(s, DeathState)
                ?? ((s & EndsScriptedAnimation) != 0 || ScriptedAnimationCancelled ? StandState : ScriptedAnimationState),

            // 0x45C9B0, 0x45CA80: the use-object states, which only the object dispatch enters. The first
            // leaves on a fresh press of slot 24 -- held from before, it stays -- or on death.
            UseObjectState => useObjectPressed || Has(s, Signal.Dead) ? StandState : UseObjectState,
            UseObjectOnceState => Has(s, Signal.AnimationDone) ? StandState : UseObjectOnceState,

            // 0x45C700: the three deaths have no exits.
            _ => State,
        };
    }

    // 0x457A00's list after the prefix.
    private static int Stand(Signal s) =>
        IsKneelHeld(s) ? CrouchDownState
        : Has(s, Signal.LieDown) ? LieDownState
        : Has(s, Signal.FirePose) ? RackState
        : Has(s, Signal.ThrowGrenade) ? GrenadeThrowState
        : Has(s, Signal.Reload) ? DisplayReloadState
        : Has(s, Signal.ScriptedAnimation) ? ScriptedAnimationState
        : Has(s, Signal.CloseCombat) ? CloseCombatState
        : Has(s, Signal.TenseRun) && Has(s, Signal.Forward) ? RunState
        : Has(s, Signal.Walk) && Has(s, Signal.Forward) ? WalkState
        : Has(s, Signal.Forward) ? Gait(s)
        : Roll(s) ?? StandState;

    // The actions every upright gait checks before its movement (0x457D30, 0x45AD10, 0x4582C0).
    private static int? UprightAction(Signal s) =>
        Has(s, Signal.Kneel) ? CrouchDownState
        : Has(s, Signal.LieDown) ? LieDownState
        : Has(s, Signal.FirePose) ? RackState
        : Has(s, Signal.ThrowGrenade) ? GrenadeThrowState
        : Has(s, Signal.Reload) ? DisplayReloadState
        : null;

    private static int? Roll(Signal s) =>
        Has(s, Signal.RollLeft) ? RollLeftState
        : Has(s, Signal.RollRight) ? RollRightState
        : null;

    // The kneel mask 0x80010: slot 21, or the external bit nothing sets.
    private static bool IsKneelHeld(Signal s) => Has(s, Signal.Kneel) || Has(s, Signal.KneelExternal);

    // The prone per-ticks' prefix: the flinch goes to the prone flinch, 31, and the kill to 34.
    private static int? PronePrefix(Signal s) =>
        !Has(s, Signal.OnGround) ? AirborneState
        : Has(s, Signal.Flinch) ? ProneFlinchState
        : Has(s, Signal.Dead) ? ProneDeathState
        : null;

    /// <inheritdoc/>
    protected override void OnEntered(int state)
    {
        if (state == ProneFlinchState)
        {
            _proneFlinchTicks = 0;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"soldier body in state {State}";
}
