using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The soldier combat cycle, transcribed from <c>off_53EC10</c>'s per-tick functions: a fighting soldier
/// walks <c>0 → 11 → 10 → 6</c> as it raises and fires, and each state holds until its clip finishes — which
/// is what stops the port's old per-tick inference from looping the firing animation.
/// </summary>
public sealed class AiCombatStateMachineTests
{
    private static AiCombatStateMachine.Signals Grounded(
        bool firePose = false,
        bool fireHeld = false,
        bool throwGrenade = false,
        bool closeCombat = false,
        bool kneel = false,
        bool lieDown = false,
        bool rollLeft = false,
        bool rollRight = false,
        bool reloadPressed = false,
        bool animDone = false) =>
        new(
            OnGround: true,
            Flinch: false,
            Dead: false,
            FirePose: firePose,
            FireHeld: fireHeld,
            ThrowGrenade: throwGrenade,
            CloseCombat: closeCombat,
            Kneel: kneel,
            LieDown: lieDown,
            RollLeft: rollLeft,
            RollRight: rollRight,
            ReloadPressed: reloadPressed,
            AnimationDone: animDone);

    /// <summary>Raising the weapon runs stand → rack → ready, and the rack holds until its clip finishes.</summary>
    [Fact]
    public void RaisingTheWeaponRunsStandToRackToReady()
    {
        AiCombatStateMachine machine = new();
        Assert.Equal(HumanSoldierStates.StandState, machine.State);

        Assert.Equal(AiCombatStateMachine.RackState, machine.Tick(Grounded(firePose: true)));

        // The rack HOLDS while its clip is still playing.
        Assert.Equal(AiCombatStateMachine.RackState, machine.Tick(Grounded(firePose: true)));
        Assert.Equal(AiCombatStateMachine.RackState, machine.Tick(Grounded(firePose: true)));

        // Only when the clip finishes does it advance to ready.
        Assert.Equal(AiCombatStateMachine.ReadyState, machine.Tick(Grounded(firePose: true, animDone: true)));
    }

    /// <summary>From ready, holding the trigger fires; FIRE holds down while the trigger stays down.</summary>
    [Fact]
    public void FiringHoldsWhileTheTriggerIsDown()
    {
        AiCombatStateMachine machine = new();
        machine.Tick(Grounded(firePose: true));                       // -> rack
        machine.Tick(Grounded(firePose: true, animDone: true));       // -> ready

        Assert.Equal(HumanSoldierStates.FiringState, machine.Tick(Grounded(firePose: true, fireHeld: true)));

        // FIRE stays put while the trigger is held, even across clip completions -- no per-tick flip.
        Assert.Equal(HumanSoldierStates.FiringState, machine.Tick(Grounded(firePose: true, fireHeld: true, animDone: true)));
        Assert.Equal(HumanSoldierStates.FiringState, machine.Tick(Grounded(firePose: true, fireHeld: true)));
    }

    /// <summary>
    /// Releasing the trigger drops FIRE back to ready while still posed, and to recover once the pose is
    /// dropped — and recover holds until its clip finishes, then stands. The whole cycle, without a loop.
    /// </summary>
    [Fact]
    public void ReleasingTheTriggerRunsBackDownToStand()
    {
        AiCombatStateMachine machine = new();
        machine.Tick(Grounded(firePose: true));                       // -> rack
        machine.Tick(Grounded(firePose: true, animDone: true));       // -> ready
        machine.Tick(Grounded(firePose: true, fireHeld: true));       // -> fire

        // Trigger released, still posed, clip done: back to ready (another burst can follow).
        Assert.Equal(AiCombatStateMachine.ReadyState, machine.Tick(Grounded(firePose: true, animDone: true)));

        // Pose dropped: ready -> recover.
        Assert.Equal(HumanSoldierStates.ReloadState, machine.Tick(Grounded()));

        // Recover holds until its clip finishes, then stands.
        Assert.Equal(HumanSoldierStates.ReloadState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded(animDone: true)));
    }

    /// <summary>Losing the ground, a flinch or death pre-empts any combat state, in that order.</summary>
    [Fact]
    public void GroundFlinchAndDeathPreemptTheFiringPose()
    {
        AiCombatStateMachine airborne = new();
        airborne.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.AirborneState,
            airborne.Tick(new AiCombatStateMachine.Signals(false, false, false, true, true, false, false, false, false, false, false, false, false)));

        AiCombatStateMachine flinched = new();
        flinched.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.FlinchState,
            flinched.Tick(new AiCombatStateMachine.Signals(true, true, false, true, true, false, false, false, false, false, false, false, false)));

        AiCombatStateMachine killed = new();
        killed.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.DeathState,
            killed.Tick(new AiCombatStateMachine.Signals(true, false, true, true, true, false, false, false, false, false, false, false, false)));
    }

    [Fact]
    public void GrenadeChannelRunsMeasuredOneShotState()
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.GrenadeThrowState,
            machine.Tick(Grounded(throwGrenade: true)));
        Assert.Equal(HumanSoldierStates.GrenadeThrowState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(58, HumanSoldierStates.AnimationOf(HumanSoldierStates.GrenadeThrowState));
    }

    [Fact]
    public void CloseCombatChannelRunsMeasuredOneShotState()
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.CloseCombatState,
            machine.Tick(Grounded(closeCombat: true)));
        Assert.Equal(HumanSoldierStates.CloseCombatState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(70, HumanSoldierStates.AnimationOf(HumanSoldierStates.CloseCombatState));
    }

    [Theory]
    [InlineData(true, false, HumanSoldierStates.RollLeftState, 10)]
    [InlineData(false, true, HumanSoldierStates.RollRightState, 11)]
    public void RollRawSlotsFinishThroughKneeling(
        bool left,
        bool right,
        int rollState,
        int animation)
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(rollState, machine.Tick(Grounded(rollLeft: left, rollRight: right)));
        Assert.Equal(rollState, machine.Tick(Grounded(rollLeft: left, rollRight: right)));
        Assert.Equal(HumanSoldierStates.KneelingState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded()));
        Assert.Equal(animation, HumanSoldierStates.AnimationOf(rollState));
    }

    [Fact]
    public void LieDownHoldsProneUntilChannelIsReleased()
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(HumanSoldierStates.LieDownState, machine.Tick(Grounded(lieDown: true)));
        Assert.Equal(HumanSoldierStates.LieDownState, machine.Tick(Grounded(lieDown: true)));
        Assert.Equal(HumanSoldierStates.ProneState, machine.Tick(Grounded(lieDown: true, animDone: true)));
        Assert.Equal(HumanSoldierStates.ProneState, machine.Tick(Grounded(lieDown: true)));

        Assert.Equal(HumanSoldierStates.GetUpState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.GetUpState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(12, HumanSoldierStates.AnimationOf(HumanSoldierStates.LieDownState));
        Assert.Equal(16, HumanSoldierStates.AnimationOf(HumanSoldierStates.ProneState));
        Assert.Equal(13, HumanSoldierStates.AnimationOf(HumanSoldierStates.GetUpState));
    }

    [Fact]
    public void ProneTriggerUsesTheProneFiringState()
    {
        AiCombatStateMachine machine = new();
        machine.Tick(Grounded(lieDown: true));
        machine.Tick(Grounded(lieDown: true, animDone: true));

        Assert.Equal(
            HumanSoldierStates.ProneFiringState,
            machine.Tick(Grounded(lieDown: true, fireHeld: true)));
        Assert.Equal(
            HumanSoldierStates.ProneFiringState,
            machine.Tick(Grounded(lieDown: true, fireHeld: true, animDone: true)));
        Assert.Equal(
            HumanSoldierStates.ProneState,
            machine.Tick(Grounded(lieDown: true, animDone: true)));
    }

    [Fact]
    public void KneelRunsTransitionStableReadyAndFireStates()
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(HumanSoldierStates.CrouchDownState, machine.Tick(Grounded(kneel: true)));
        Assert.Equal(HumanSoldierStates.CrouchDownState, machine.Tick(Grounded(kneel: true)));
        Assert.Equal(HumanSoldierStates.KneelingState, machine.Tick(Grounded(kneel: true, animDone: true)));
        Assert.Equal(HumanSoldierStates.KneelingState, machine.Tick(Grounded(kneel: true)));

        Assert.Equal(
            HumanSoldierStates.KneelReadyState,
            machine.Tick(Grounded(kneel: true, firePose: true, fireHeld: true)));
        Assert.Equal(
            HumanSoldierStates.KneelFiringState,
            machine.Tick(Grounded(kneel: true, firePose: true, fireHeld: true, animDone: true)));
        Assert.Equal(
            HumanSoldierStates.KneelReadyState,
            machine.Tick(Grounded(kneel: true, firePose: true, animDone: true)));
        Assert.Equal(HumanSoldierStates.KneelingState, machine.Tick(Grounded(kneel: true)));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded()));
    }

    [Fact]
    public void KneelingLieDownUsesItsOwnTransition()
    {
        AiCombatStateMachine machine = new();
        machine.Tick(Grounded(kneel: true));
        machine.Tick(Grounded(kneel: true, animDone: true));

        Assert.Equal(
            HumanSoldierStates.KneelToProneState,
            machine.Tick(Grounded(kneel: true, lieDown: true)));
        Assert.Equal(
            HumanSoldierStates.ProneState,
            machine.Tick(Grounded(lieDown: true, animDone: true)));
    }

    [Fact]
    public void RawReloadSlotSelectsDisplayedReloadOnly()
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.DisplayReloadState,
            machine.Tick(Grounded(reloadPressed: true)));
        Assert.Equal(HumanSoldierStates.DisplayReloadState, machine.Tick(Grounded()));
        Assert.Equal(
            AiCombatStateMachine.ReadyState,
            machine.Tick(Grounded(firePose: true, animDone: true)));
    }

    [Fact]
    public void RawReloadSlotUsesProneReloadStateWhileLyingDown()
    {
        AiCombatStateMachine machine = new();
        machine.Tick(Grounded(lieDown: true));
        machine.Tick(Grounded(lieDown: true, animDone: true));

        Assert.Equal(
            HumanSoldierStates.ProneReloadState,
            machine.Tick(Grounded(lieDown: true, reloadPressed: true)));
        Assert.Equal(HumanSoldierStates.ProneReloadState, machine.Tick(Grounded(lieDown: true)));
        Assert.Equal(
            HumanSoldierStates.ProneState,
            machine.Tick(Grounded(lieDown: true, animDone: true)));
        Assert.Equal(21, HumanSoldierStates.AnimationOf(HumanSoldierStates.ProneReloadState));
    }

    [Fact]
    public void ForwardRunUsesItsTriggerForwardingState()
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.RunState,
            machine.Tick(Grounded() with { MovingForward = true }));
        Assert.Equal(
            HumanSoldierStates.RunFiringState,
            machine.Tick(Grounded(fireHeld: true) with { MovingForward = true }));
        Assert.Equal(
            HumanSoldierStates.RunFiringState,
            machine.Tick(Grounded(fireHeld: true) with { MovingForward = true }));
        Assert.Equal(
            HumanSoldierStates.RunState,
            machine.Tick(Grounded() with { MovingForward = true }));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded()));
    }

    [Fact]
    public void SoldierAnimationModesSeparateLoopsFromOneShots()
    {
        Assert.Equal(1, HumanSoldierStates.ModeOf(HumanSoldierStates.StandState));
        Assert.Equal(1, HumanSoldierStates.ModeOf(HumanSoldierStates.RunState));
        Assert.Equal(1, HumanSoldierStates.ModeOf(HumanSoldierStates.RunFiringState));
        Assert.Equal(1, HumanSoldierStates.ModeOf(HumanSoldierStates.FiringState));

        Assert.Equal(0, HumanSoldierStates.ModeOf(AiCombatStateMachine.RackState));
        Assert.Equal(0, HumanSoldierStates.ModeOf(HumanSoldierStates.ReloadState));
        Assert.Equal(0, HumanSoldierStates.ModeOf(HumanSoldierStates.RollLeftState));
        Assert.Equal(0, HumanSoldierStates.ModeOf(HumanSoldierStates.DisplayReloadState));
    }

    [Fact]
    public void StandSignalPrecedenceMatchesRetail()
    {
        AiCombatStateMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.LieDownState,
            machine.Tick(Grounded(
                firePose: true,
                throwGrenade: true,
                closeCombat: true,
                lieDown: true,
                rollLeft: true,
                rollRight: true)));

        machine.Reset();
        Assert.Equal(
            HumanSoldierStates.CrouchDownState,
            machine.Tick(Grounded(kneel: true, lieDown: true, firePose: true)));

        machine.Reset();
        Assert.Equal(
            AiCombatStateMachine.RackState,
            machine.Tick(Grounded(
                firePose: true,
                throwGrenade: true,
                closeCombat: true,
                rollLeft: true,
                rollRight: true)));
    }

    /// <summary>Each combat state maps to the animation the retail capture showed for it.</summary>
    [Fact]
    public void TheCombatStatesMapToTheirMeasuredAnimations()
    {
        Assert.Equal(2, HumanSoldierStates.AnimationOf(HumanSoldierStates.StandState));   // 0 -> stand
        Assert.Equal(53, HumanSoldierStates.AnimationOf(AiCombatStateMachine.RackState)); // 11 -> rack
        Assert.Equal(54, HumanSoldierStates.AnimationOf(AiCombatStateMachine.ReadyState)); // 10 -> ready
        Assert.Equal(56, HumanSoldierStates.AnimationOf(HumanSoldierStates.FiringState)); // 6 -> fire
        Assert.Equal(55, HumanSoldierStates.AnimationOf(HumanSoldierStates.ReloadState)); // 12 -> recover
    }
}
