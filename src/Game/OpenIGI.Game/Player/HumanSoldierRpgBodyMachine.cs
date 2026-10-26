using static OpenIGI.Game.Player.HumanSoldierRpgStates;

namespace OpenIGI.Game.Player;

/// <summary>
/// An RPG soldier's body machine — <c>off_53E6A8</c>'s fifteen states, packed by <c>0x456CF0</c>.
/// </summary>
/// <remarks>
/// <para>
/// Most per-ticks first leave for <see cref="AirborneState"/> off the ground, then for
/// <see cref="FlinchState"/>, then for <see cref="DeathState"/> — the prefix <see cref="HumanBodyMachine.Interrupt(in HumanBodyInput, int, int, int)"/>
/// models. The shoulder raise and lower (<c>0x4560F0</c>, <c>0x4561A0</c>) test no flinch, and the flinch
/// reuses the lower's per-tick.
/// </para>
/// <para>
/// A held trigger from standing or moving is a hip shot (4 → 3 → 0). The aim bit — frame slot 21, where
/// the RPG channel row puts the kneel the combat arm holds — raises the launcher to the shoulder
/// (5 → 7), and from there the trigger fires aimed (10 → 8 → 7) and a reload request reloads (9 → 7).
/// A reload asked for away from the shoulder raises it first. The per-ticks also accept bit
/// <c>0x10</c> as the aim, which <c>0x456CF0</c> never sets and nothing in the port raises.
/// </para>
/// </remarks>
public sealed class HumanSoldierRpgBodyMachine : HumanBodyMachine
{
    // The frame slots 0x456CF0 packs.
    private const int FireSlot = 6;
    private const int ReloadSlot = 8;
    private const int AimSlot = 21;
    private const int ScriptedAnimationSlot = 22;


    /// <summary>Initializes an RPG soldier's machine, standing.</summary>
    public HumanSoldierRpgBodyMachine()
        : base(
            HumanSoldierRpgStates.Table,
            HumanSoldierRpgStates.StandState,
            HumanHitZoneTable.RpgSoldier,
            HumanSoldierRpgStates.FlinchState,
            HumanSoldierRpgStates.ScriptedAnimationState)
    {
    }

    /// <inheritdoc/>
    public override bool IsFiring => State is HipFiringState or AimedFiringState;

    /// <inheritdoc/>
    public override bool IsMoving => State is WalkState or RunState;


    /// <inheritdoc/>
    protected override int NextState(in HumanBodyInput input)
    {
        NoteScriptedAnimationRequest(input, ScriptedAnimationSlot);
        Signals s = Signals.Pack(input);

        return State switch
        {
            // 0x4566A0: re-enters itself on every tick off the ground, restarting the fall; lands to standing,
            // or dies if the kill landed in the air. It tests no flinch.
            AirborneState => !input.OnGround ? Restart(AirborneState) : input.Dead ? DeathState : StandState,

            // 0x4560F0: the shoulder raise tests the ground and the kill, not the flinch.
            ShoulderRaiseState => !input.OnGround ? AirborneState
                : input.Dead ? DeathState
                : s.AnimationDone ? AimState
                : ShoulderRaiseState,

            // 0x4561A0, which the flinch also runs. No per-tick ever enters the shoulder lower itself.
            ShoulderLowerState or FlinchState => !input.OnGround ? AirborneState
                : input.Dead ? DeathState
                : s.AnimationDone ? StandState
                : State,

            // 0x456920 slides the corpse and then hands over to the shared end-of-death tick 0x45C740.
            DeathState => DeathState,

            _ => Interrupt(input, AirborneState, FlinchState, DeathState) ?? Act(s),
        };
    }


    private int Act(in Signals s) => State switch
    {
        // 0x455A40.
        StandState => s.FireHeld ? HipRaiseState
            : s.Reload ? ShoulderRaiseState
            : s.ScriptedAnimationRequested ? ScriptedAnimationState
            : s.MovingForward ? (s.Running ? RunState : WalkState)
            : s.Aim ? ShoulderRaiseState
            : StandState,

        // 0x455BE0.
        WalkState => s.FireHeld ? HipRaiseState
            : s.Reload ? ShoulderRaiseState
            : s.Running ? RunState
            : !s.MovingForward ? StandState
            : s.Aim ? ShoulderRaiseState
            : WalkState,

        // 0x455D80.
        RunState => s.FireHeld ? HipRaiseState
            : s.Reload ? ShoulderRaiseState
            : !s.Running ? WalkState
            : !s.MovingForward ? StandState
            : s.Aim ? ShoulderRaiseState
            : RunState,

        // 0x455EE0: fires while slot 6 is held; ends on its clip with the trigger released.
        HipFiringState => s.AnimationDone && !s.FireHeld ? StandState : HipFiringState,

        // 0x456010.
        HipRaiseState => s.AnimationDone ? HipFiringState : HipRaiseState,

        // 0x456250: the shoulder is held only while the aim bit is.
        AimState => s.FireHeld ? AimToFireState
            : s.Reload ? ReloadState
            : !s.Aim ? StandState
            : AimState,

        // 0x456380.
        AimedFiringState => s.AnimationDone && !s.FireHeld ? AimState : AimedFiringState,

        // 0x4564B0: the reload completes on its clip (0x4F2350).
        ReloadState => s.AnimationDone ? AimState : ReloadState,

        // 0x4565A0.
        AimToFireState => s.AnimationDone ? AimedFiringState : AimToFireState,

        // 0x456A10: any movement, fire or aim bit in 0x81FE1 — or a request of -1 — ends it.
        ScriptedAnimationState => s.EndsScriptedAnimation || ScriptedAnimationCancelled
            ? StandState
            : ScriptedAnimationState,

        _ => State,
    };

    /// <summary>The flag word <c>0x456CF0</c> packs, decoded.</summary>
    private readonly record struct Signals(
        bool AnimationDone,
        bool MovingForward,
        bool Running,
        bool FireHeld,
        bool Reload,
        bool Aim,
        bool ScriptedAnimationRequested,
        bool EndsScriptedAnimation)
    {
        public static Signals Pack(in HumanBodyInput input)
        {
            bool aim = input.IsSet(AimSlot);
            return new Signals(
                input.AnimationDone,
                input.MovingForward,
                input.Running,
                FireHeld: input.IsSet(FireSlot),
                Reload: input.IsSet(ReloadSlot),
                Aim: aim,
                ScriptedAnimationRequested: input.IsSet(ScriptedAnimationSlot),
                EndsScriptedAnimation: input.AsksForAction || aim || input.AnimationDone);
        }
    }

    /// <inheritdoc/>
    protected override bool IsAirborne(int state) => state == HumanSoldierRpgStates.AirborneState;

    /// <inheritdoc/>
    /// <remarks>Only the run's entry carries the stride, and only from the walk (<c>0x455D10</c>).</remarks>
    protected override bool KeepsStride(int left, int entered) =>
        left == HumanSoldierRpgStates.WalkState && entered == HumanSoldierRpgStates.RunState;
}
