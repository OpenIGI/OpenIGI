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
        bool firePose = false, bool fireHeld = false, bool animDone = false) =>
        new(OnGround: true, Flinch: false, Dead: false, FirePose: firePose, FireHeld: fireHeld, AnimationDone: animDone);

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
            airborne.Tick(new AiCombatStateMachine.Signals(false, false, false, true, true, false)));

        AiCombatStateMachine flinched = new();
        flinched.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.FlinchState,
            flinched.Tick(new AiCombatStateMachine.Signals(true, true, false, true, true, false)));

        AiCombatStateMachine killed = new();
        killed.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.DeathState,
            killed.Tick(new AiCombatStateMachine.Signals(true, false, true, true, true, false)));
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
