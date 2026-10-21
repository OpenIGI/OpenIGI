using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>Running tense in four directions, running in panic, and rolling aside.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>A soldier's speed a tick strafing tense at a speed scale of one, <c>267.60532</c>.</summary>
    public const float TenseStrafeSpeed = 267.60532f;

    /// <summary>A roll's speed a tick, <c>382.29333</c>, whatever the speed scale.</summary>
    public const float RollSpeed = 382.29333f;

    /// <summary>A quarter turn, the float <c>1.5707964</c>: left of the heading.</summary>
    private const float QuarterTurn = 1.5707964f;

    /// <summary>A half turn, the float <c>3.1415927</c>: behind the heading.</summary>
    private const float HalfTurn = 3.1415927f;

    /// <summary>
    /// Running tense, <c>HumanSoldier_InitStateTenseRunForward</c> (<c>igi2 0x4E51B0</c>) and its left, right and backward kin
    /// (<c>0x4E54A0</c>, <c>0x4E5790</c>, <c>0x4E5A80</c>): upright and running, the state's own row at once, moving by the step.
    /// </summary>
    /// <param name="run">The direction's tick.</param>
    private void EnterTenseRun(Action run)
    {
        Pose = Igi2HumanPose.Standing;
        IsRunning = true;
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => run();
        IsDisableDeltaTranslation = false;
    }

    /// <summary>
    /// Running tense forward's tick, <c>HumanSoldier_RunStateTenseRunForward</c> (<c>igi2 0x4E51F0</c>): at the run's speed, until a
    /// strafe or backing off takes over, the forward channel drops, or the tension goes.
    /// </summary>
    private void RunTenseForward()
    {
        MoveOnGround(((Fpu)MovementSpeedScale * RunSpeed).F, 0f);
        Igi2SoldierSignal s = Signal;
        if (TenseCommon(s) is int common)
        {
            Go(common);
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
        else if ((s & Igi2SoldierSignal.Forward) == 0 || (s & Igi2SoldierSignal.Tense) == 0)
        {
            Go(Igi2SoldierState.Stand);
        }
    }

    /// <summary>
    /// Strafing left's tick, <c>HumanSoldier_RunStateTenseRunLeft</c> (<c>igi2 0x4E54E0</c>): at the strafe's speed a quarter turn to
    /// the left, rolling left if still tense and asked to.
    /// </summary>
    private void RunTenseLeft()
    {
        MoveOnGround(((Fpu)MovementSpeedScale * TenseStrafeSpeed).F, QuarterTurn);
        Igi2SoldierSignal s = Signal;
        if (TenseCommon(s) is int common)
        {
            Go(common);
        }
        else if ((s & Igi2SoldierSignal.Backward) != 0)
        {
            Go(Igi2SoldierState.TenseRunBackward);
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            Go(Igi2SoldierState.TenseRunForward);
        }
        else if ((s & Igi2SoldierSignal.StrafeRight) != 0)
        {
            Go(Igi2SoldierState.TenseRunRight);
        }
        else if ((s & Igi2SoldierSignal.StrafeLeft) == 0 || (s & Igi2SoldierSignal.Tense) == 0)
        {
            Go(Igi2SoldierState.Stand);
        }
        else if ((s & Igi2SoldierSignal.RollLeft) != 0)
        {
            Go(Igi2SoldierState.StandRollLeft);
        }
    }

    /// <summary>
    /// Strafing right's tick, <c>HumanSoldier_RunStateTenseRunRight</c> (<c>igi2 0x4E57D0</c>): at the strafe's speed a quarter turn
    /// to the right, rolling right if still tense and asked to.
    /// </summary>
    private void RunTenseRight()
    {
        MoveOnGround(((Fpu)MovementSpeedScale * TenseStrafeSpeed).F, -QuarterTurn);
        Igi2SoldierSignal s = Signal;
        if (TenseCommon(s) is int common)
        {
            Go(common);
        }
        else if ((s & Igi2SoldierSignal.StrafeLeft) != 0)
        {
            Go(Igi2SoldierState.TenseRunLeft);
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            Go(Igi2SoldierState.TenseRunForward);
        }
        else if ((s & Igi2SoldierSignal.Backward) != 0)
        {
            Go(Igi2SoldierState.TenseRunBackward);
        }
        else if ((s & Igi2SoldierSignal.StrafeRight) == 0 || (s & Igi2SoldierSignal.Tense) == 0)
        {
            Go(Igi2SoldierState.Stand);
        }
        else if ((s & Igi2SoldierSignal.RollRight) != 0)
        {
            Go(Igi2SoldierState.StandRollRight);
        }
    }

    /// <summary>
    /// Backing off's tick, <c>HumanSoldier_RunStateTenseRunBackward</c> (<c>igi2 0x4E5AC0</c>): at the run's speed straight behind, until
    /// another direction takes over or the tension goes.
    /// </summary>
    private void RunTenseBackward()
    {
        MoveOnGround(((Fpu)MovementSpeedScale * RunSpeed).F, HalfTurn);
        Igi2SoldierSignal s = Signal;
        if (TenseCommon(s) is int common)
        {
            Go(common);
        }
        else if ((s & Igi2SoldierSignal.StrafeLeft) != 0)
        {
            Go(Igi2SoldierState.TenseRunLeft);
        }
        else if ((s & Igi2SoldierSignal.Forward) != 0)
        {
            Go(Igi2SoldierState.TenseRunForward);
        }
        else if ((s & Igi2SoldierSignal.StrafeRight) != 0)
        {
            Go(Igi2SoldierState.TenseRunRight);
        }
        else if ((s & Igi2SoldierSignal.Backward) == 0 || (s & Igi2SoldierSignal.Tense) == 0)
        {
            Go(Igi2SoldierState.Stand);
        }
    }

    /// <summary>
    /// The tests a tense or panicked run makes first, in their order: off the ground, hit, dead, kneel, lie down.
    /// </summary>
    /// <param name="s">The signal.</param>
    /// <returns>The state to go to, or <see langword="null"/>.</returns>
    private static int? TenseCommon(Igi2SoldierSignal s)
    {
        if ((s & Igi2SoldierSignal.Grounded) == 0)
        {
            return Igi2SoldierState.Fall;
        }

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

        return (s & Igi2SoldierSignal.LieDown) != 0 ? Igi2SoldierState.StandToLieDown : null;
    }

    /// <summary>
    /// Running in panic, <c>HumanSoldier_InitStatePanicForward</c> (<c>igi2 0x4E4F70</c>): upright and running, its row at once.
    /// </summary>
    private void EnterPanicForward() => EnterTenseRun(RunPanicForward);

    /// <summary>
    /// Running in panic's tick, <c>HumanSoldier_RunStatePanicForward</c> (<c>igi2 0x4E4FB0</c>): one step at the full run's speed,
    /// whatever the scale, then a walk or a run unless a fall, hit, death, kneel, lie or throw comes first.
    /// </summary>
    private void RunPanicForward()
    {
        MoveOnGround(RunSpeed, 0f);
        Igi2SoldierSignal s = Signal;
        if (TenseCommon(s) is int common)
        {
            Go(common);
        }
        else if ((s & Igi2SoldierSignal.ThrowGrenade) != 0)
        {
            Go(Igi2SoldierState.StandThrowGrenade);
        }
        else
        {
            Go((s & Igi2SoldierSignal.Walk) != 0 ? Igi2SoldierState.WalkForward : Igi2SoldierState.RunForward);
        }
    }

    /// <summary>
    /// A roll from standing, <c>HumanSoldier_InitStateStandRollLeft</c> (<c>igi2 0x4E61D0</c>) and <c>RollRight</c> (<c>0x4E6320</c>):
    /// crouched, the upper body cleared, kept off and cleared again, the state's row at once, moving by the step.
    /// </summary>
    /// <param name="gamma">Which way it rolls: a quarter turn left or right of the heading.</param>
    private void EnterStandRoll(float gamma)
    {
        Pose = Igi2HumanPose.Crouching;
        Animation.ClearUpperBody(0f);
        UpperBodyFlags |= 1;
        Animation.ClearUpperBody(0f);
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => RunRoll(gamma);
        IsDisableDeltaTranslation = false;
    }

    /// <summary>
    /// A roll from a kneel, <c>HumanSoldier_InitStateKneelRollLeft</c> (<c>igi2 0x4E7260</c>) and <c>RollRight</c> (<c>0x4E73A0</c>):
    /// crouched, the upper body kept off and cleared, the state's row at once, moving by the step.
    /// </summary>
    /// <param name="gamma">Which way it rolls.</param>
    private void EnterKneelRoll(float gamma)
    {
        UpperBodyFlags |= 1;
        Pose = Igi2HumanPose.Crouching;
        Animation.ClearUpperBody(0f);
        PlayCurrentState(0f);
        Locomotion.Current = (_, _) => RunRoll(gamma);
        IsDisableDeltaTranslation = false;
    }

    /// <summary>
    /// A roll's tick, <c>HumanSoldier_RunStateStandRollLeft</c> (<c>igi2 0x4E6230</c>) and its three kin: at the roll's speed aside,
    /// until it falls, dies kneeling, or its clip ends in a kneel with the upper body free again.
    /// </summary>
    /// <param name="gamma">Which way it rolls.</param>
    private void RunRoll(float gamma)
    {
        MoveOnGround(RollSpeed, gamma);
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
            ReleaseUpperBody();
            Go(Igi2SoldierState.Kneel);
        }
    }
}
