using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>The soldier's locomotion states.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>A soldier's walking speed a tick at a speed scale of one, <c>204.8</c>.</summary>
    public const float WalkSpeed = 204.8f;

    /// <summary>A soldier's running speed a tick at a speed scale of one, <c>458.75201</c>.</summary>
    public const float RunSpeed = 458.75201f;

    /// <summary>How much of a falling soldier's speed is kept a tick, <c>0.99000001</c>.</summary>
    public const float FallDamping = 0.99000001f;

    /// <summary>What gravity takes off a falling soldier's height speed a tick, <c>84.741692</c>.</summary>
    public const float FallGravity = 84.741692f;

    /// <summary>The weapon clip a walk's name is tested on, <c>13</c>.</summary>
    private const int WalkWeaponAnim = 13;

    /// <summary>The weapon clip a run's name is tested on, <c>14</c>.</summary>
    private const int RunWeaponAnim = 14;

    private TaskStateMachine CreateLocomotion()
    {
        var states = new TaskStateFunction?[Igi2SoldierState.Count];
        states[Igi2SoldierState.Stand] = (_, _) => EnterStand();
        states[Igi2SoldierState.StandTurn] = (_, _) => EnterStandTurn();
        states[Igi2SoldierState.RunForward] = (_, _) => EnterRunForward();
        states[Igi2SoldierState.WalkForward] = (_, _) => EnterWalkForward();
        states[Igi2SoldierState.Fall] = (_, _) => EnterFall();
        states[Igi2SoldierState.PanicForward] = (_, _) => EnterPanicForward();
        states[Igi2SoldierState.TenseRunForward] = (_, _) => EnterTenseRun(RunTenseForward);
        states[Igi2SoldierState.TenseRunLeft] = (_, _) => EnterTenseRun(RunTenseLeft);
        states[Igi2SoldierState.TenseRunRight] = (_, _) => EnterTenseRun(RunTenseRight);
        states[Igi2SoldierState.TenseRunBackward] = (_, _) => EnterTenseRun(RunTenseBackward);
        states[Igi2SoldierState.StandRollLeft] = (_, _) => EnterStandRoll(QuarterTurn);
        states[Igi2SoldierState.StandRollRight] = (_, _) => EnterStandRoll(-QuarterTurn);
        states[Igi2SoldierState.KneelRollLeft] = (_, _) => EnterKneelRoll(QuarterTurn);
        states[Igi2SoldierState.KneelRollRight] = (_, _) => EnterKneelRoll(-QuarterTurn);
        states[Igi2SoldierState.StandToLieDown] = (_, _) => EnterPostureChange(Igi2HumanPose.LyingFlat, RunStandToLieDown);
        states[Igi2SoldierState.LieDownToStand] = (_, _) => EnterPostureChange(Igi2HumanPose.LyingFlat, RunLieDownToStand);
        states[Igi2SoldierState.KneelToLieDown] = (_, _) => EnterPostureChange(Igi2HumanPose.Crouching, RunKneelToLieDown);
        states[Igi2SoldierState.LieDown] = (_, _) => EnterLieDown();
        states[Igi2SoldierState.LieDownFire] = (_, _) => EnterProneAction(RunLieDownFire);
        states[Igi2SoldierState.LieDownReload] = (_, _) => EnterProneAction(RunLieDownReload);
        states[Igi2SoldierState.LieDownHit] = (_, _) => EnterLieDownHit();
        states[Igi2SoldierState.Dead] = (_, _) => EnterDead();
        states[Igi2SoldierState.StandThrowGrenade] = (_, _) => EnterStandThrowGrenade();
        states[Igi2SoldierState.StandCloseCombat] = (_, _) => EnterStandCloseCombat();
        states[Igi2SoldierState.StandPressSwitch] = (_, _) => EnterStandPressSwitch();
        states[Igi2SoldierState.Door] = (_, _) => EnterDoor();
        states[Igi2SoldierState.StationaryGun] = (_, _) => EnterStationaryGun();
        states[Igi2SoldierState.SilentKill] = (_, _) => EnterSilentKill();
        states[Igi2SoldierState.StandAction] = (_, _) => EnterStandAction();
        states[Igi2SoldierState.StandToKneel] = (_, _) => EnterStandToKneel();
        states[Igi2SoldierState.KneelToStand] = (_, _) => EnterKneelToStand();
        states[Igi2SoldierState.Kneel] = (_, _) => EnterKneel();
        states[Igi2SoldierState.KneelTurn] = (_, _) => EnterKneelTurn();
        states[Igi2SoldierState.Hit] = (_, _) => EnterHit();
        for (int slot = Igi2SoldierState.FirstLieDownDie; slot <= Igi2SoldierState.LastLieDownDie; slot++)
        {
            states[slot] = (_, _) => EnterLieDownDie();
        }

        for (int slot = Igi2SoldierState.FirstKneelDie; slot <= Igi2SoldierState.LastKneelDie; slot++)
        {
            states[slot] = (_, _) => EnterKneelDie();
        }

        for (int slot = Igi2SoldierState.FirstDie; slot <= Igi2SoldierState.LastDie; slot++)
        {
            states[slot] = (_, _) => EnterDie();
        }
        return new TaskStateMachine((_, _) => PackSignal(), states, Igi2SoldierState.Stand, exits: new TaskStateFunction?[Igi2SoldierState.Count]);
    }

    /// <summary>Leaves the current state for another and runs its entry.</summary>
    private void Go(int state) => Locomotion.Enter(state, this);

    /// <summary>The animation row a tense stand plays, <c>103</c>.</summary>
    public const int TenseStandRow = 103;

    /// <summary>The animation row a walk plays, <c>7</c>: not its own slot's.</summary>
    public const int WalkRow = 7;

    /// <summary>The row a forward run plays, <c>5</c> (<c>humansoldier.c HumanSoldier_InitStateRunForward</c>).</summary>
    public const int RunForwardRow = 5;

    /// <summary>The row a forward run plays when the weapon in hand has its own running gait, <c>4</c>.</summary>
    public const int WeaponRunForwardRow = 4;

    /// <summary>The animation row a kneel plays, <c>28</c>: not its own slot's.</summary>
    public const int KneelRow = 28;

    /// <summary>Plays the clip the current state's row names, as the entries that pass their own slot do.</summary>
    private void PlayCurrentState(float blendSeconds) => PlayRow(Locomotion.CurrentState, blendSeconds);

    /// <summary>Plays the clip an animation row names (<c>HumanAnim_SetAnimationAndTimeFromState</c>, the row in <c>ebx</c>).</summary>
    private void PlayRow(int row, float blendSeconds) => Animation.PlayState(row, 0, blendSeconds);

    /// <summary>Lets the upper body play again if a gait had kept it off.</summary>
    private void ReleaseUpperBody()
    {
        if ((UpperBodyFlags & 1) != 0)
        {
            UpperBodyFlags &= ~1;
            Animation.ClearUpperBody(0f);
        }
    }

    /// <summary>
    /// Standing, <c>HumanSoldier_InitStateStand</c> (<c>igi2 0x4E42E0</c>): upright, not running, the upper body free, and still.
    /// </summary>
    /// <remarks>A tense soldier plays row 103 at once and stands tense; otherwise row 0, blended in over half a second once its mind has started.</remarks>
    private void EnterStand()
    {
        Pose = Igi2HumanPose.Standing;
        IsRunning = false;
        ReleaseUpperBody();
        if ((Signal & Igi2SoldierSignal.Tense) != 0)
        {
            PlayRow(TenseStandRow, 0f);
            Locomotion.Current = (_, _) => RunStandTense();
        }
        else
        {
            PlayRow(Igi2SoldierState.Stand, Mind is { HasStarted: true } ? 0.5f : 0f);
            Locomotion.Current = (_, _) => RunStand();
        }

        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>Standing's tick, <c>HumanSoldier_RunStateStand</c> (<c>igi2 0x4E4390</c>): the first signal that asks for a move takes it.</summary>
    private void RunStand()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if (Common(s, Igi2SoldierSignal.Kept8 | Igi2SoldierSignal.Kneel) is int common)
        {
            Go(common);
        }
        else if ((s & Igi2SoldierSignal.Turn) != 0)
        {
            Go(Igi2SoldierState.StandTurn);
        }
        else if ((s & Igi2SoldierSignal.PressSwitch) != 0)
        {
            Go(Igi2SoldierState.StandPressSwitch);
        }
        else if ((s & Igi2SoldierSignal.StrafeLeft) != 0)
        {
            Go(Igi2SoldierState.TenseRunLeft);
        }
        else if ((s & Igi2SoldierSignal.StrafeRight) != 0)
        {
            Go(Igi2SoldierState.TenseRunRight);
        }
        else if ((s & Igi2SoldierSignal.Backward) != 0)
        {
            Go(Igi2SoldierState.TenseRunBackward);
        }
        else if ((s & Igi2SoldierSignal.Tense) != 0 && (s & Igi2SoldierSignal.Forward) != 0)
        {
            Go(Igi2SoldierState.TenseRunForward);
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            Go((s & Igi2SoldierSignal.Walk) != 0 ? Igi2SoldierState.WalkForward : Igi2SoldierState.RunForward);
        }
        else if ((s & Igi2SoldierSignal.RollLeft) != 0)
        {
            Go(Igi2SoldierState.StandRollLeft);
        }
        else if ((s & Igi2SoldierSignal.RollRight) != 0)
        {
            Go(Igi2SoldierState.StandRollRight);
        }
    }

    /// <summary>
    /// Standing tense, <c>HumanSoldier_RunStateStandTense</c> (<c>igi2 0x4E4BF0</c>): moves only tense, and stands again when the
    /// tension goes.
    /// </summary>
    private void RunStandTense()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if (Common(s, Igi2SoldierSignal.Kept8 | Igi2SoldierSignal.Kneel) is int common)
        {
            Go(common);
        }
        else if ((s & Igi2SoldierSignal.PressSwitch) != 0)
        {
            Go(Igi2SoldierState.StandPressSwitch);
        }
        else if ((s & Igi2SoldierSignal.Tense) == 0)
        {
            Go(Igi2SoldierState.Stand);
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            Go(Igi2SoldierState.TenseRunForward);
        }
        else if ((s & Igi2SoldierSignal.StrafeLeft) != 0)
        {
            Go(Igi2SoldierState.TenseRunLeft);
        }
        else if ((s & Igi2SoldierSignal.StrafeRight) != 0)
        {
            Go(Igi2SoldierState.TenseRunRight);
        }
        else if ((s & Igi2SoldierSignal.Backward) != 0)
        {
            Go(Igi2SoldierState.TenseRunBackward);
        }
    }

    /// <summary>
    /// The tests every standing state makes first, in their order: hit, dead, kneel, lie down, grenade, stand action, close
    /// combat.
    /// </summary>
    /// <param name="s">The signal.</param>
    /// <param name="kneel">The bits that send the soldier to a kneel.</param>
    /// <returns>The state to go to, or <see langword="null"/>.</returns>
    private static int? Common(Igi2SoldierSignal s, Igi2SoldierSignal kneel)
    {
        if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            return Igi2SoldierState.Hit;
        }

        if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            return Igi2SoldierState.Die;
        }

        if ((s & kneel) != 0)
        {
            return Igi2SoldierState.StandToKneel;
        }

        if ((s & Igi2SoldierSignal.LieDown) != 0)
        {
            return Igi2SoldierState.StandToLieDown;
        }

        if ((s & Igi2SoldierSignal.ThrowGrenade) != 0)
        {
            return Igi2SoldierState.StandThrowGrenade;
        }

        if ((s & Igi2SoldierSignal.StandAction) != 0)
        {
            return Igi2SoldierState.StandAction;
        }

        return (s & Igi2SoldierSignal.CloseCombat) != 0 ? Igi2SoldierState.StandCloseCombat : null;
    }

    /// <summary>Turning on the spot, <c>HumanSoldier_InitStateStandTurn</c> (<c>igi2 0x4E4810</c>): upright, still, its row blended in over half a second.</summary>
    private void EnterStandTurn()
    {
        Pose = Igi2HumanPose.Standing;
        IsRunning = false;
        PlayCurrentState(0.5f);
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
        Locomotion.Current = (_, _) => RunStandTurn();
    }

    /// <summary>Turning's tick, <c>HumanSoldier_RunStateStandTurn</c> (<c>igi2 0x4E4860</c>): it turns while the turn channel holds.</summary>
    private void RunStandTurn()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if (Common(s, Igi2SoldierSignal.Kept8 | Igi2SoldierSignal.Kneel) is int common)
        {
            Go(common);
        }
        else if ((s & Igi2SoldierSignal.Tense) != 0)
        {
            Go(Igi2SoldierState.Stand);
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            Go((s & Igi2SoldierSignal.Walk) != 0 ? Igi2SoldierState.WalkForward : Igi2SoldierState.RunForward);
        }
        else if ((s & Igi2SoldierSignal.StrafeLeft) != 0)
        {
            Go(Igi2SoldierState.TenseRunLeft);
        }
        else if ((s & Igi2SoldierSignal.StrafeRight) != 0)
        {
            Go(Igi2SoldierState.TenseRunRight);
        }
        else if ((s & Igi2SoldierSignal.Backward) != 0)
        {
            Go(Igi2SoldierState.TenseRunBackward);
        }
        else if ((s & Igi2SoldierSignal.Turn) == 0)
        {
            Go(Igi2SoldierState.Stand);
        }
    }

    /// <summary>
    /// Walking forward, <c>HumanSoldier_InitStateWalkForward</c> (<c>igi2 0x4E5F90</c>): row 7 plays at once, and if the weapon's
    /// walk is one of its own (the clip named for the weapon) the upper body is kept off.
    /// </summary>
    private void EnterWalkForward()
    {
        bool own = IsWeaponsOwnGait(WalkWeaponAnim);
        PlayRow(WalkRow, 0f);
        if (own)
        {
            UpperBodyFlags |= 1;
            Animation.ClearUpperBody(0f);
        }
        else
        {
            ReleaseUpperBody();
        }

        Pose = Igi2HumanPose.Standing;
        IsRunning = false;
        IsDisableDeltaTranslation = false;
        Locomotion.Current = (_, _) => RunWalkForward();
    }

    /// <summary>Walking's tick, <c>HumanSoldier_RunStateWalkForward</c> (<c>igi2 0x4E60C0</c>): a walk's step, then on until the signal changes.</summary>
    private void RunWalkForward()
    {
        MoveOnGround(((Fpu)MovementSpeedScale * WalkSpeed).F, 0f);
        Igi2SoldierSignal s = Signal;
        int next;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            next = Igi2SoldierState.Fall;
        }
        else if (Gait(s) is int gait)
        {
            next = gait;
        }
        else if ((s & Igi2SoldierSignal.Tense) != 0)
        {
            next = Igi2SoldierState.TenseRunForward;
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            if ((s & Igi2SoldierSignal.Walk) != 0)
            {
                return;
            }

            next = Igi2SoldierState.RunForward;
        }
        else
        {
            next = Igi2SoldierState.Stand;
        }

        ReleaseUpperBody();
        Go(next);
    }

    /// <summary>
    /// Running forward, <c>HumanSoldier_InitStateRunForward</c> (<c>igi2 0x4E5D40</c>): upright and running, its row at once, the upper
    /// body kept off if the weapon's run is its own.
    /// </summary>
    private void EnterRunForward()
    {
        Pose = Igi2HumanPose.Standing;
        IsRunning = true;
        ReleaseUpperBody();
        int row = RunForwardRow;
        if (IsWeaponsOwnGait(RunWeaponAnim))
        {
            UpperBodyFlags |= 1;
            row = WeaponRunForwardRow;
            Animation.ClearUpperBody(0f);
        }

        PlayRow(row, 0f);
        IsDisableDeltaTranslation = false;
        Locomotion.Current = (_, _) => RunRunForward();
    }

    /// <summary>Running's tick, <c>HumanSoldier_RunStateRunForward</c> (<c>igi2 0x4E5E80</c>).</summary>
    private void RunRunForward()
    {
        MoveOnGround(((Fpu)MovementSpeedScale * RunSpeed).F, 0f);
        Igi2SoldierSignal s = Signal;
        int next;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            next = Igi2SoldierState.Fall;
        }
        else if (GaitPosture(s) is int posture)
        {
            next = posture;
        }
        else if ((s & Igi2SoldierSignal.Tense) != 0)
        {
            next = Igi2SoldierState.TenseRunForward;
        }
        else if ((s & Igi2SoldierSignal.Walk) != 0)
        {
            next = Igi2SoldierState.WalkForward;
        }
        else if (Strafe(s) is int strafe)
        {
            next = strafe;
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            return;
        }
        else
        {
            next = Igi2SoldierState.Stand;
        }

        ReleaseUpperBody();
        Go(next);
    }

    /// <summary>A gait's first tests: hit, dead, kneel, lie down, grenade, then a strafe or backing off.</summary>
    private static int? Gait(Igi2SoldierSignal s) => GaitPosture(s) ?? Strafe(s);

    /// <summary>A gait's posture tests: hit, dead, kneel, lie down, grenade.</summary>
    private static int? GaitPosture(Igi2SoldierSignal s)
    {
        if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            return Igi2SoldierState.Hit;
        }

        if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            return Igi2SoldierState.Die;
        }

        if ((s & Igi2SoldierSignal.Kneel) != 0)
        {
            return Igi2SoldierState.StandToKneel;
        }

        if ((s & Igi2SoldierSignal.LieDown) != 0)
        {
            return Igi2SoldierState.StandToLieDown;
        }

        return (s & Igi2SoldierSignal.ThrowGrenade) != 0 ? Igi2SoldierState.StandThrowGrenade : null;
    }

    /// <summary>A strafe or backing off.</summary>
    private static int? Strafe(Igi2SoldierSignal s)
    {
        if ((s & Igi2SoldierSignal.StrafeLeft) != 0)
        {
            return Igi2SoldierState.TenseRunLeft;
        }

        if ((s & Igi2SoldierSignal.StrafeRight) != 0)
        {
            return Igi2SoldierState.TenseRunRight;
        }

        return (s & Igi2SoldierSignal.Backward) != 0 ? Igi2SoldierState.TenseRunBackward : null;
    }

    /// <summary>
    /// Going down to a kneel, <c>HumanSoldier_InitStateStandToKneel</c> (<c>igi2 0x4E68C0</c>): its row at once, still; kneeling once
    /// the clip has played (<c>HumanSoldier_RunStateStandToKneel</c>, <c>0x4E6910</c>).
    /// </summary>
    private void EnterStandToKneel()
    {
        Pose = Igi2HumanPose.Standing;
        IsRunning = false;
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => RunPosture(Igi2SoldierState.Kneel);
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// Getting up from a kneel, <c>HumanSoldier_InitStateKneelToStand</c> (<c>igi2 0x4E69E0</c>): standing once the clip has played
    /// (<c>HumanSoldier_RunStateKneelToStand</c>, <c>0x4E6A30</c>).
    /// </summary>
    private void EnterKneelToStand()
    {
        Pose = Igi2HumanPose.Standing;
        IsRunning = false;
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => RunPosture(Igi2SoldierState.Stand);
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>A change of posture's tick: off the ground it falls, dead it dies kneeling, and when its clip ends it is done.</summary>
    private void RunPosture(int done)
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.KneelDie);
        }
        else if ((s & Igi2SoldierSignal.MainEnded) != 0)
        {
            Go(done);
        }
    }

    /// <summary>Kneeling, <c>HumanSoldier_InitStateKneel</c> (<c>igi2 0x4E6D80</c>): crouched, row 28 at once, still.</summary>
    private void EnterKneel()
    {
        Pose = Igi2HumanPose.Crouching;
        PlayRow(KneelRow, 0f);
        Locomotion.Current = (_, _) => RunKneel();
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// Kneeling's tick, <c>HumanSoldier_RunStateKneel</c> (<c>igi2 0x4E6DE0</c>): it kneels while the kneel channel or a low roof
    /// holds it down, turning or rolling as asked, and gets up as soon as neither does or it is told to move.
    /// </summary>
    private void RunKneel()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.Hit);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.KneelDie);
        }
        else if ((s & Igi2SoldierSignal.LieDown) != 0)
        {
            Go(Igi2SoldierState.KneelToLieDown);
        }
        else if ((s & (Igi2SoldierSignal.Kept8 | Igi2SoldierSignal.Kneel)) == 0)
        {
            Go(Igi2SoldierState.KneelToStand);
        }
        else if ((s & Igi2SoldierSignal.Turn) != 0)
        {
            Go(Igi2SoldierState.KneelTurn);
        }
        else if ((s & Moves) != 0)
        {
            Go(Igi2SoldierState.KneelToStand);
        }
        else if ((s & Igi2SoldierSignal.RollLeft) != 0)
        {
            Go(Igi2SoldierState.KneelRollLeft);
        }
        else if ((s & Igi2SoldierSignal.RollRight) != 0)
        {
            Go(Igi2SoldierState.KneelRollRight);
        }
    }

    /// <summary>The four ways to move, <c>0x1E0</c>.</summary>
    private const Igi2SoldierSignal Moves =
        Igi2SoldierSignal.StrafeLeft | Igi2SoldierSignal.StrafeRight | Igi2SoldierSignal.Forward | Igi2SoldierSignal.Backward;

    /// <summary>Turning while kneeling, <c>HumanSoldier_InitStateKneelTurn</c> (<c>igi2 0x4E7030</c>): crouched, its row at once, still.</summary>
    private void EnterKneelTurn()
    {
        Pose = Igi2HumanPose.Crouching;
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => RunKneelTurn();
        IsDisableDeltaTranslation = true;
        DeltaTranslation = default;
    }

    /// <summary>
    /// Turning while kneeling's tick, <c>HumanSoldier_RunStateKneelTurn</c> (<c>igi2 0x4E7090</c>): it kneels again when the turn ends,
    /// and gets up to move.
    /// </summary>
    private void RunKneelTurn()
    {
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            Go(Igi2SoldierState.Fall);
        }
        else if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.Hit);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.KneelDie);
        }
        else if ((s & Igi2SoldierSignal.LieDown) != 0)
        {
            Go(Igi2SoldierState.KneelToLieDown);
        }
        else if ((s & (Igi2SoldierSignal.Kept8 | Igi2SoldierSignal.Kneel)) == 0)
        {
            Go(Igi2SoldierState.KneelToStand);
        }
        else if ((s & Igi2SoldierSignal.Turn) == 0)
        {
            Go(Igi2SoldierState.Kneel);
        }
        else if ((s & Moves) != 0)
        {
            Go(Igi2SoldierState.KneelToStand);
        }
    }

    /// <summary>
    /// Falling, <c>HumanSoldier_InitStateFall</c> (<c>igi2 0x4E7B30</c>): upright, not running, its row at once, carrying on at the
    /// speed it had.
    /// </summary>
    private void EnterFall()
    {
        Pose = Igi2HumanPose.Standing;
        IsRunning = false;
        PlayCurrentState(0f);
        Speed = TotalSpeed;
        IsDisableDeltaTranslation = false;
        Locomotion.Current = (_, _) => RunFall();
    }

    /// <summary>
    /// Falling's tick, <c>HumanSoldier_RunStateFall</c> (<c>igi2 0x4E7B90</c>): air slows it and gravity pulls it down until it
    /// lands, sounding the ground it lands on, and stands.
    /// </summary>
    private void RunFall()
    {
        Vector3f v = Speed;
        Speed = new Vector3f(
            ((Fpu)v.X * FallDamping).F,
            ((Fpu)v.Y * FallDamping).F,
            (((Fpu)v.Z - FallGravity) * FallDamping).F);
        Igi2SoldierSignal s = Signal;
        if ((s & Igi2SoldierSignal.Hit) != 0)
        {
            Go(Igi2SoldierState.Hit);
        }
        else if ((s & Igi2SoldierSignal.Dead) != 0)
        {
            Go(Igi2SoldierState.Die);
        }
        else if ((s & Igi2SoldierSignal.Grounded) != 0)
        {
            if (GroundIntersection.EnterMaterial is int material && Surroundings.LandingSound(material) is { } sound)
            {
                Surroundings.PlaySound(sound);
            }

            Speed = default;
            Go(Igi2SoldierState.Stand);
        }
    }

    /// <summary>
    /// The soldier's own step along the ground, <c>Human_MoveOnGround</c> (<c>igi2 0x503570</c>): a speed forward, turned by a
    /// heading off the body's and then by the body, as the animation's step.
    /// </summary>
    /// <param name="speed">The step forward.</param>
    /// <param name="gamma">The heading off the body's the step is taken at.</param>
    private void MoveOnGround(float speed, float gamma)
    {
        float[] r = Igi2Pose.Rotation(0f, 0f, gamma);
        Fpu a = (((Fpu)r[2] + r[0]) * 0f) + ((Fpu)r[1] * speed);
        Fpu b = (((Fpu)r[5] + r[3]) * 0f) + ((Fpu)r[4] * speed);
        Fpu c = (((Fpu)r[8] + r[6]) * 0f) + ((Fpu)r[7] * speed);
        Matrix3f o = Orientation;
        float x = ((c * o.M13) + (b * o.M12) + (a * o.M11)).F;
        float y = ((c * o.M23) + (b * o.M22) + (a * o.M21)).F;
        float z = ((c * o.M33) + (b * o.M32) + (a * o.M31)).F;
        DeltaTranslation = new Vector3f(x, y, z);
    }

    /// <summary>
    /// Whether the weapon's clip for a gait is its own, named for the weapon's family or animation (<c>rifl</c>, <c>pistol</c>,
    /// <c>knife</c>), as the walk and run entries test it.
    /// </summary>
    private bool IsWeaponsOwnGait(int anim)
    {
        if (HeldType is not { } held || Weapon.BodyClips(Animation.Rig) is not { } clips || (uint)anim >= (uint)clips.Count
            || clips[anim] is not { } clip)
        {
            return false;
        }

        string? family = (Igi2WeaponAnimFamily)held.WeaponType switch
        {
            Igi2WeaponAnimFamily.Rifle or Igi2WeaponAnimFamily.Shotgun => "rifl",
            Igi2WeaponAnimFamily.Pistol => "pistol",
            Igi2WeaponAnimFamily.Knife => "knife",
            _ => null,
        };
        return family is not null
            && (clip.Name.Contains(family, StringComparison.Ordinal) || clip.Name.Contains(held.WeaponAnimationName, StringComparison.Ordinal));
    }
}
