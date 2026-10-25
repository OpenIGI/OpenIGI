using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The soldier combat cycle, transcribed from <c>off_53EC10</c>'s per-tick functions: a fighting soldier
/// walks <c>0 → 11 → 10 → 6</c> as it raises and fires, and each state holds until its clip finishes — which
/// is what stops the port's old per-tick inference from looping the firing animation.
/// </summary>
public sealed class HumanSoldierBodyMachineTests
{
    private static HumanSoldierBodyMachine.Signal Grounded(
        bool firePose = false,
        bool fireHeld = false,
        bool throwGrenade = false,
        bool closeCombat = false,
        bool kneel = false,
        bool lieDown = false,
        bool rollLeft = false,
        bool rollRight = false,
        bool reloadPressed = false,
        bool animDone = false,
        bool movingForward = false) =>
        HumanSoldierBodyMachine.Signal.OnGround
        | Bit(firePose, HumanSoldierBodyMachine.Signal.FirePose)
        | Bit(fireHeld, HumanSoldierBodyMachine.Signal.Fire)
        | Bit(throwGrenade, HumanSoldierBodyMachine.Signal.ThrowGrenade)
        | Bit(closeCombat, HumanSoldierBodyMachine.Signal.CloseCombat)
        | Bit(kneel, HumanSoldierBodyMachine.Signal.Kneel)
        | Bit(lieDown, HumanSoldierBodyMachine.Signal.LieDown)
        | Bit(rollLeft, HumanSoldierBodyMachine.Signal.RollLeft)
        | Bit(rollRight, HumanSoldierBodyMachine.Signal.RollRight)
        | Bit(reloadPressed, HumanSoldierBodyMachine.Signal.Reload)
        | Bit(animDone, HumanSoldierBodyMachine.Signal.AnimationDone)
        | Bit(movingForward, HumanSoldierBodyMachine.Signal.Forward | HumanSoldierBodyMachine.Signal.TenseRun);

    private static HumanSoldierBodyMachine.Signal Bit(bool set, HumanSoldierBodyMachine.Signal bit) =>
        set ? bit : HumanSoldierBodyMachine.Signal.None;

    /// <summary>Raising the weapon runs stand → rack → ready, and the rack holds until its clip finishes.</summary>
    [Fact]
    public void RaisingTheWeaponRunsStandToRackToReady()
    {
        HumanSoldierBodyMachine machine = new();
        Assert.Equal(HumanSoldierStates.StandState, machine.State);

        Assert.Equal(HumanSoldierStates.RackState, machine.Tick(Grounded(firePose: true)));

        // The rack HOLDS while its clip is still playing.
        Assert.Equal(HumanSoldierStates.RackState, machine.Tick(Grounded(firePose: true)));
        Assert.Equal(HumanSoldierStates.RackState, machine.Tick(Grounded(firePose: true)));

        // Only when the clip finishes does it advance to ready.
        Assert.Equal(HumanSoldierStates.ReadyState, machine.Tick(Grounded(firePose: true, animDone: true)));
    }

    /// <summary>From ready, holding the trigger fires; FIRE holds down while the trigger stays down.</summary>
    [Fact]
    public void FiringHoldsWhileTheTriggerIsDown()
    {
        HumanSoldierBodyMachine machine = new();
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
        HumanSoldierBodyMachine machine = new();
        machine.Tick(Grounded(firePose: true));                       // -> rack
        machine.Tick(Grounded(firePose: true, animDone: true));       // -> ready
        machine.Tick(Grounded(firePose: true, fireHeld: true));       // -> fire

        // Trigger released, still posed, clip done: back to ready (another burst can follow).
        Assert.Equal(HumanSoldierStates.ReadyState, machine.Tick(Grounded(firePose: true, animDone: true)));

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
        HumanSoldierBodyMachine airborne = new();
        airborne.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.AirborneState,
            airborne.Tick(HumanSoldierBodyMachine.Signal.FirePose | HumanSoldierBodyMachine.Signal.Fire));

        HumanSoldierBodyMachine flinched = new();
        flinched.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.FlinchState,
            flinched.Tick(Grounded(firePose: true, fireHeld: true) | HumanSoldierBodyMachine.Signal.Flinch));

        HumanSoldierBodyMachine killed = new();
        killed.Tick(Grounded(firePose: true, fireHeld: true));
        Assert.Equal(
            HumanSoldierStates.DeathState,
            killed.Tick(Grounded(firePose: true, fireHeld: true) | HumanSoldierBodyMachine.Signal.Dead));
    }

    [Fact]
    public void GrenadeChannelRunsMeasuredOneShotState()
    {
        HumanSoldierBodyMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.GrenadeThrowState,
            machine.Tick(Grounded(throwGrenade: true)));
        Assert.Equal(HumanSoldierStates.GrenadeThrowState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(58, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.GrenadeThrowState));
    }

    [Fact]
    public void CloseCombatChannelRunsMeasuredOneShotState()
    {
        HumanSoldierBodyMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.CloseCombatState,
            machine.Tick(Grounded(closeCombat: true)));
        Assert.Equal(HumanSoldierStates.CloseCombatState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(70, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.CloseCombatState));
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
        HumanSoldierBodyMachine machine = new();

        Assert.Equal(rollState, machine.Tick(Grounded(rollLeft: left, rollRight: right)));
        Assert.Equal(rollState, machine.Tick(Grounded(rollLeft: left, rollRight: right)));
        Assert.Equal(HumanSoldierStates.KneelingState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded()));
        Assert.Equal(animation, HumanSoldierStates.Table.AnimationOf(rollState));
    }

    [Fact]
    public void LieDownHoldsProneUntilChannelIsReleased()
    {
        HumanSoldierBodyMachine machine = new();

        Assert.Equal(HumanSoldierStates.LieDownState, machine.Tick(Grounded(lieDown: true)));
        Assert.Equal(HumanSoldierStates.LieDownState, machine.Tick(Grounded(lieDown: true)));
        Assert.Equal(HumanSoldierStates.ProneState, machine.Tick(Grounded(lieDown: true, animDone: true)));
        Assert.Equal(HumanSoldierStates.ProneState, machine.Tick(Grounded(lieDown: true)));

        Assert.Equal(HumanSoldierStates.GetUpState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.GetUpState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded(animDone: true)));
        Assert.Equal(12, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.LieDownState));
        Assert.Equal(16, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.ProneState));
        Assert.Equal(13, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.GetUpState));
    }

    [Fact]
    public void ProneTriggerUsesTheProneFiringState()
    {
        HumanSoldierBodyMachine machine = new();
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
        HumanSoldierBodyMachine machine = new();

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
        HumanSoldierBodyMachine machine = new();
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
        HumanSoldierBodyMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.DisplayReloadState,
            machine.Tick(Grounded(reloadPressed: true)));
        Assert.Equal(HumanSoldierStates.DisplayReloadState, machine.Tick(Grounded()));
        Assert.Equal(
            HumanSoldierStates.ReadyState,
            machine.Tick(Grounded(firePose: true, animDone: true)));
    }

    [Fact]
    public void RawReloadSlotUsesProneReloadStateWhileLyingDown()
    {
        HumanSoldierBodyMachine machine = new();
        machine.Tick(Grounded(lieDown: true));
        machine.Tick(Grounded(lieDown: true, animDone: true));

        Assert.Equal(
            HumanSoldierStates.ProneReloadState,
            machine.Tick(Grounded(lieDown: true, reloadPressed: true)));
        Assert.Equal(HumanSoldierStates.ProneReloadState, machine.Tick(Grounded(lieDown: true)));
        Assert.Equal(
            HumanSoldierStates.ProneState,
            machine.Tick(Grounded(lieDown: true, animDone: true)));
        Assert.Equal(21, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.ProneReloadState));
    }

    [Fact]
    public void ForwardRunUsesItsTriggerForwardingState()
    {
        HumanSoldierBodyMachine machine = new();

        Assert.Equal(
            HumanSoldierStates.RunState,
            machine.Tick(Grounded(movingForward: true)));
        Assert.Equal(
            HumanSoldierStates.RunFiringState,
            machine.Tick(Grounded(fireHeld: true, movingForward: true)));
        Assert.Equal(
            HumanSoldierStates.RunFiringState,
            machine.Tick(Grounded(fireHeld: true, movingForward: true)));

        // 0x45AB00 returns to the run only once the clip ends with the trigger released.
        Assert.Equal(
            HumanSoldierStates.RunFiringState,
            machine.Tick(Grounded(movingForward: true)));
        Assert.Equal(
            HumanSoldierStates.RunState,
            machine.Tick(Grounded(movingForward: true, animDone: true)));

        // 0x457FC0 leaves the tense run for the forward gait, whose own per-tick then stands.
        Assert.Equal(HumanSoldierStates.ForwardState, machine.Tick(Grounded()));
        Assert.Equal(HumanSoldierStates.StandState, machine.Tick(Grounded()));
    }

    [Fact]
    public void SoldierAnimationModesSeparateLoopsFromOneShots()
    {
        Assert.Equal(1, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.StandState));
        Assert.Equal(1, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.RunState));
        Assert.Equal(1, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.RunFiringState));
        Assert.Equal(1, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.FiringState));

        Assert.Equal(0, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.RackState));
        Assert.Equal(0, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.ReloadState));
        Assert.Equal(0, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.RollLeftState));
        Assert.Equal(0, HumanSoldierStates.Table.ModeOf(HumanSoldierStates.DisplayReloadState));
    }

    [Fact]
    public void StandSignalPrecedenceMatchesRetail()
    {
        HumanSoldierBodyMachine machine = new();

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
            HumanSoldierStates.RackState,
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
        Assert.Equal(2, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.StandState));   // 0 -> stand
        Assert.Equal(53, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.RackState)); // 11 -> rack
        Assert.Equal(54, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.ReadyState)); // 10 -> ready
        Assert.Equal(56, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.FiringState)); // 6 -> fire
        Assert.Equal(55, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.ReloadState)); // 12 -> recover
    }

    /// <summary>
    /// The look goes off for a flinch and comes back when it plays out to standing (<c>0x45C2E0</c>, <c>0x4592F0</c>),
    /// but a flinch that ends in a fall leaves it off.
    /// </summary>
    [Fact]
    public void AFlinchTurnsTheLookOffUntilItPlaysOut()
    {
        HumanSoldierBodyMachine body = new();
        Assert.True(body.LooksAlongView);

        body.Tick(Grounded() | HumanSoldierBodyMachine.Signal.Flinch);
        Assert.Equal(HumanSoldierStates.FlinchState, body.State);
        Assert.False(body.LooksAlongView);

        body.Tick(Grounded(animDone: true));
        Assert.Equal(HumanSoldierStates.StandState, body.State);
        Assert.True(body.LooksAlongView);

        body.Tick(Grounded() | HumanSoldierBodyMachine.Signal.Flinch);
        body.Tick(HumanSoldierBodyMachine.Signal.None);
        Assert.Equal(HumanSoldierStates.AirborneState, body.State);
        body.Tick(Grounded());
        Assert.Equal(HumanSoldierStates.StandState, body.State);
        Assert.False(body.LooksAlongView);
    }
}
