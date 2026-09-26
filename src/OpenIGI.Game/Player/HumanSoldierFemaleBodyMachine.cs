using static OpenIGI.Game.Player.HumanSoldierFemaleStates;

namespace OpenIGI.Game.Player;

/// <summary>
/// A female soldier's body machine — <c>off_53E400</c>'s thirteen states, packed by <c>0x454BA0</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every per-tick first leaves for <see cref="AirborneState"/> off the ground, then for
/// <see cref="FlinchState"/> on a flinch, then for <see cref="DeathState"/> on a kill — the prefix
/// <see cref="HumanBodyMachine.Interrupt(in HumanBodyInput, int, int, int)"/> models. What follows is each per-tick's own list, in the order it tests.
/// </para>
/// <para>
/// The weapon has one path: drawing it (frame slot 23, <c>0x200000</c> — where the Anya/Ekk channel row
/// puts the firing pose) raises it (6) into the aim (5); the trigger fires (3); releasing it returns to
/// the aim while the weapon stays drawn, and lowers it (7) otherwise. The reload (4) completes on its
/// clip and returns to the aim or to standing.
/// </para>
/// </remarks>
public sealed class HumanSoldierFemaleBodyMachine : HumanBodyMachine
{
    // The frame slots 0x454BA0 packs, and the bits they become.
    private const int FireSlot = 6;
    private const int ReloadSlot = 8;
    private const int ScriptedAnimationSlot = 21;
    private const int UseObjectSlot = 22;
    private const int WeaponDrawnSlot = 23;


    /// <summary>Initializes a female soldier's machine, standing.</summary>
    public HumanSoldierFemaleBodyMachine()
        : base(
            HumanSoldierFemaleStates.Table,
            HumanSoldierFemaleStates.StandState,
            HumanHitZoneTable.FemaleSoldier,
            HumanSoldierFemaleStates.FlinchState,
            HumanSoldierFemaleStates.ScriptedAnimationState)
    {
    }

    /// <inheritdoc/>
    public override bool IsFiring => State == FiringState;

    /// <inheritdoc/>
    public override bool IsMoving => State is WalkState or RunState;


    /// <inheritdoc/>
    protected override int NextState(in HumanBodyInput input)
    {
        NoteScriptedAnimationRequest(input, ScriptedAnimationSlot);
        Signals s = Signals.Pack(input);

        return State switch
        {
            // 0x454510 (entered by 0x4544C0): the fall is the one state with no airborne test of its own.
            AirborneState => input.Flinch ? FlinchState : input.Dead ? DeathState : input.OnGround ? StandState : AirborneState,

            // 0x454630: a flinch can still be knocked off its feet or killed; it tests no flinch.
            FlinchState => !input.OnGround ? AirborneState
                : input.Dead ? DeathState
                : s.AnimationDone ? StandState
                : FlinchState,

            // 0x4547B0 slides the corpse and then hands over to the shared end-of-death tick 0x45C740.
            DeathState => DeathState,

            // 0x45CA80, shared with the soldier's state 37: only 0x454DA0's object dispatch enters it.
            UseObjectState => s.AnimationDone ? StandState : UseObjectState,

            _ => Interrupt(input, AirborneState, FlinchState, DeathState) ?? Act(s),
        };
    }


    private int Act(in Signals s) => State switch
    {
        // 0x453AE0.
        StandState => s.WeaponDrawn ? RaiseWeaponState
            : s.Reload ? ReloadState
            : s.ScriptedAnimationRequested ? ScriptedAnimationState
            : s.MovingForward ? (s.Running ? RunState : WalkState)
            : StandState,

        // 0x453CB0.
        WalkState => s.WeaponDrawn ? RaiseWeaponState
            : s.Reload ? ReloadState
            : s.Running ? RunState
            : !s.MovingForward ? StandState
            : WalkState,

        // 0x453E50.
        RunState => s.WeaponDrawn ? RaiseWeaponState
            : s.Reload ? ReloadState
            : s.Running && !s.MovingForward ? StandState
            : !s.Running ? WalkState
            : RunState,

        // 0x453FB0: fires while slot 6 is held; holds until its clip ends with the trigger released.
        FiringState => s.AnimationDone && !s.FireHeld
            ? (s.WeaponDrawn ? AimState : LowerWeaponState)
            : FiringState,

        // 0x454100: the reload completes on its clip (0x4F2350).
        ReloadState => s.AnimationDone ? (s.WeaponDrawn ? AimState : StandState) : ReloadState,

        // 0x454220.
        AimState => !s.WeaponDrawn ? LowerWeaponState : s.FireHeld ? FiringState : AimState,

        // 0x454330 and 0x454410.
        RaiseWeaponState => s.AnimationDone ? AimState : RaiseWeaponState,
        LowerWeaponState => s.AnimationDone ? StandState : LowerWeaponState,

        // 0x454880: any movement, fire or weapon bit in 0x301FE1 — or a request of -1 — ends it.
        ScriptedAnimationState => s.EndsScriptedAnimation || ScriptedAnimationCancelled
            ? StandState
            : ScriptedAnimationState,

        _ => State,
    };

    /// <summary>The flag word <c>0x454BA0</c> packs, decoded.</summary>
    private readonly record struct Signals(
        bool AnimationDone,
        bool MovingForward,
        bool Running,
        bool FireHeld,
        bool Reload,
        bool WeaponDrawn,
        bool ScriptedAnimationRequested,
        bool EndsScriptedAnimation)
    {
        public static Signals Pack(in HumanBodyInput input)
        {
            bool weaponDrawn = input.IsSet(WeaponDrawnSlot);
            return new Signals(
                input.AnimationDone,
                input.MovingForward,
                input.Running,
                FireHeld: input.IsSet(FireSlot),
                Reload: input.IsSet(ReloadSlot),
                WeaponDrawn: weaponDrawn,
                ScriptedAnimationRequested: input.IsSet(ScriptedAnimationSlot),
                EndsScriptedAnimation: input.AsksForAction
                    || weaponDrawn
                    || input.IsSet(UseObjectSlot)
                    || input.AnimationDone);
        }
    }
}
