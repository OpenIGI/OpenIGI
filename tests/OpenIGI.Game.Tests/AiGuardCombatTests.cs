using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Weapons;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A guard engaging, aiming, firing a burst and reloading — <c>0x48D000</c> through <c>0x48DB10</c>.
/// </summary>
public sealed class AiGuardCombatTests
{
    private const double Metre = 4096.0;
    private const double ViewLength = 40.0 * Metre;

    /// <summary>A target the vision subsystem holds no slot for ends the action at once.</summary>
    /// <remarks>
    /// <c>ai+15844</c> is filled from the detection sweep, so an AI told to fight somebody it has never
    /// detected has nothing to fight and gives up rather than firing blind.
    /// </remarks>
    [Fact]
    public void ATargetWithNoDetectionSlotEndsTheAction()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        AiCombatCommand command = combat.Update(
            Sense() with { HasDetectionSlot = false }, standing: true, ViewLength);

        Assert.True(command.Ended);
        Assert.Equal(AiCombatPhase.Ended, combat.Phase);
    }

    /// <summary>Out of the cone and out of sight, the guard turns toward the target.</summary>
    [Fact]
    public void OutOfConeTheGuardTurnsToTheTarget()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        AiCombatCommand command = combat.Update(
            Sense() with { Visible = false, InCone = false }, standing: true, ViewLength);

        Assert.Equal(AiAimMode.None, command.Aim);
        Assert.NotNull(command.TurnTo);
        Assert.False(command.PullTrigger);
    }

    /// <summary>
    /// The miss counter is cleared every tick and written back only while the target is out of sight,
    /// so one glimpse resets the whole targeting timeout.
    /// </summary>
    [Fact]
    public void OneGlimpseResetsTheTargetingTimeout()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        for (int i = 0; i < 20; i++)
        {
            combat.Update(Sense() with { Visible = false }, standing: true, ViewLength);
        }

        Assert.Equal(20, combat.MissTicks);

        combat.Update(Sense(), standing: true, ViewLength);
        Assert.Equal(0, combat.MissTicks);
    }

    /// <summary>Losing the target for the whole timeout ends the action.</summary>
    [Fact]
    public void TheTargetingTimeoutEndsTheAction()
    {
        AiCombatParameters parameters = Parameters(targetingTimeoutSeconds: 1f);
        AiGuardCombat combat = new(parameters, () => 0.0);
        combat.Begin();

        Assert.Equal(30, parameters.TargetingTimeoutTicks);

        AiCombatCommand command = default;
        for (int i = 0; i < 31; i++)
        {
            command = combat.Update(Sense() with { Visible = false }, standing: true, ViewLength);
        }

        Assert.True(command.Ended);
    }

    /// <summary>An empty clip pushes the reload sub-action, which owns the tick until it is done.</summary>
    [Fact]
    public void AnEmptyClipReloads()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        AiCombatCommand command = combat.Update(Sense() with { AmmoInClip = 0 }, standing: true, ViewLength);
        Assert.Equal(AiAimMode.Reloading, command.Aim);
        Assert.Equal(AiCombatPhase.Reload, combat.Phase);

        // While the clip is empty the reload releases the trigger and asks for the reload -- the first
        // time straight at the weapon, because the latch starts clear.
        command = combat.Update(Sense() with { AmmoInClip = 0 }, standing: true, ViewLength);
        Assert.True(command.ReleaseTrigger);
        Assert.True(command.ReloadWeaponDirectly);
        Assert.False(command.PressReload);

        // Once the clip is full and the body is standing again, the sub-action is done.
        command = combat.Update(Sense(), standing: true, ViewLength);
        Assert.Equal(AiCombatPhase.Stand, combat.Phase);
        Assert.False(command.ReloadWeaponDirectly);
    }

    /// <summary>Consecutive reloads alternate between the input frame and the weapon itself.</summary>
    /// <remarks>
    /// <c>ai+15857</c> is toggled every time a reload finishes, and it selects which of the two routes
    /// the next one takes. Both reach the same weapon; only one plays it the way the player's would.
    /// </remarks>
    [Fact]
    public void ConsecutiveReloadsAlternateBetweenTheInputFrameAndTheWeapon()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        Assert.True(RunOneReload(combat).ReloadWeaponDirectly);
        Assert.True(RunOneReload(combat).PressReload);
        Assert.True(RunOneReload(combat).ReloadWeaponDirectly);
    }

    /// <summary>
    /// A burst is set up with a length from the settings row and a hit probability from the range, and
    /// the tick hands over to the firing sub-state.
    /// </summary>
    [Fact]
    public void SeeingTheTargetSetsUpABurst()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        AiCombatCommand command = combat.Update(Sense(), standing: true, ViewLength);

        Assert.Equal(AiAimMode.Aiming, command.Aim);
        Assert.NotNull(command.StartedBurst);
        Assert.Equal(AiCombatPhase.Fire, combat.Phase);

        // 0.8 minimum clip percentage of a 30-round clip is 24, under the row's cap of 30.
        Assert.Equal(24, command.StartedBurst!.Value.Rounds);
        Assert.Equal(24, combat.RoundsLeftInBurst);
    }

    /// <summary>The fifth attacker on one target aims in the crowded mode and does not open fire.</summary>
    [Fact]
    public void TheFifthAttackerCrowdsInsteadOfFiring()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        AiCombatCommand command = combat.Update(
            Sense() with { AimersOnTarget = AiCombatTarget.MaximumAimers }, standing: true, ViewLength);

        Assert.Equal(AiAimMode.Crowded, command.Aim);
        Assert.Null(command.StartedBurst);
        Assert.Equal(AiCombatPhase.Stand, combat.Phase);
    }

    /// <summary>The trigger stays down until the facing is inside two degrees of the aim point.</summary>
    [Fact]
    public void TheTriggerWaitsForTheAimToSettle()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();
        combat.Update(Sense(), standing: true, ViewLength);

        combat.ReportAimed(false);
        AiCombatCommand command = combat.Update(Sense(), standing: true, ViewLength);
        Assert.True(command.FirePose);
        Assert.False(command.PullTrigger);

        combat.ReportAimed(true);
        command = combat.Update(Sense(), standing: true, ViewLength);
        Assert.True(command.PullTrigger);
    }

    /// <summary>
    /// An automatic holds the trigger through the burst; a semi-automatic releases it for a tick every
    /// time a round leaves, which is what makes it semi-automatic.
    /// </summary>
    /// <remarks>
    /// The hand-off note that opened this session had this test the wrong way round, reading the
    /// original's <c>if (!v8) return 0</c> as "return when automatic". It returns when <b>single
    /// shot</b>, and that is the only reading under which a semi-automatic ever stops firing.
    /// </remarks>
    [Fact]
    public void ASingleShotWeaponReleasesTheTriggerBetweenRounds()
    {
        AiGuardCombat automatic = Guard();
        automatic.Begin();
        automatic.Update(Sense(), standing: true, ViewLength);
        automatic.ReportAimed(true);

        // A round left the clip since the last tick.
        AiCombatCommand command = automatic.Update(Sense() with { AmmoInClip = 29 }, standing: true, ViewLength);
        Assert.True(command.PullTrigger);

        AiGuardCombat semi = Guard(burstSize: 1);
        semi.Begin();
        semi.Update(Sense() with { BurstSize = 1 }, standing: true, ViewLength);
        semi.ReportAimed(true);

        command = semi.Update(Sense() with { BurstSize = 1, AmmoInClip = 29 }, standing: true, ViewLength);
        Assert.False(command.PullTrigger);
        Assert.True(command.FirePose);
    }

    /// <summary>A semi-automatic then waits out its per-shot delay before the next round.</summary>
    [Fact]
    public void ASingleShotWeaponWaitsOutItsShotDelay()
    {
        AiCombatParameters parameters = Parameters(minimumShotDelaySeconds: 0.1f);
        AiGuardCombat semi = new(parameters, () => 0.0);
        semi.Begin();
        semi.Update(Sense() with { BurstSize = 1 }, standing: true, ViewLength);
        semi.ReportAimed(true);

        // 0.1 s becomes three ticks.
        semi.Update(Sense() with { BurstSize = 1, AmmoInClip = 29 }, standing: true, ViewLength);
        Assert.Equal(3, semi.ShotDelayTicks);

        for (int i = 0; i < 3; i++)
        {
            AiCombatCommand waiting = semi.Update(
                Sense() with { BurstSize = 1, AmmoInClip = 29 }, standing: true, ViewLength);
            Assert.False(waiting.PullTrigger);
        }

        AiCombatCommand firing = semi.Update(
            Sense() with { BurstSize = 1, AmmoInClip = 29 }, standing: true, ViewLength);
        Assert.True(firing.PullTrigger);
    }

    /// <summary>Spending the burst hands the tick back to the stand state and counts the sequence.</summary>
    [Fact]
    public void SpendingTheBurstReturnsToTheStandState()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();
        combat.Update(Sense(), standing: true, ViewLength);
        combat.ReportAimed(true);

        int ammo = 30;
        for (int tick = 0; tick < 200 && combat.Phase == AiCombatPhase.Fire; tick++)
        {
            AiCombatCommand command = combat.Update(
                Sense() with { AmmoInClip = ammo }, standing: true, ViewLength);
            if (command.PullTrigger && ammo > 0)
            {
                ammo--;
            }
        }

        Assert.Equal(AiCombatPhase.Stand, combat.Phase);
        Assert.Equal(1, combat.BurstsFired);
        Assert.Equal(0, combat.RoundsLeftInBurst);

        // Twenty-five rounds for a burst of twenty-four, because the counter counts rounds it has
        // OBSERVED leave the clip and the observation lags the trigger by a tick -- so the trigger is
        // still down on the tick the count reaches zero. This test fires one round per pull; a real
        // weapon fires at its own cadence and that last tick usually produces nothing.
        Assert.Equal(5, ammo);
    }

    /// <summary>An already-started burst is not cancelled when its target disappears behind cover.</summary>
    /// <remarks>
    /// Mission 1 probe capture <c>mission1-parity.log</c> records the patrol guard at
    /// <c>0x1574CC44</c> losing LOS during an eleven-round burst and spending it to zero. The world/body
    /// nearest-contact cast, not this action, prevents those rounds from damaging the occluded player.
    /// </remarks>
    [Fact]
    public void LosingSightDoesNotCancelAnActiveBurst()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();
        combat.Update(Sense(), standing: true, ViewLength);
        combat.ReportAimed(true);

        int ammo = 30;
        AiCombatCommand command = combat.Update(
            Sense() with { Visible = false, AmmoInClip = ammo }, standing: true, ViewLength);

        Assert.Equal(AiCombatPhase.Fire, combat.Phase);
        Assert.True(command.FirePose);
        Assert.True(command.PullTrigger);

        // Every observed round drains the original burst even though LOS remains blocked.
        ammo--;
        command = combat.Update(
            Sense() with { Visible = false, AmmoInClip = ammo }, standing: true, ViewLength);

        Assert.Equal(AiCombatPhase.Fire, combat.Phase);
        Assert.Equal(23, combat.RoundsLeftInBurst);
        Assert.True(command.PullTrigger);
    }

    /// <summary>The aim point chases the target at the row's aim precision, not instantly.</summary>
    [Fact]
    public void TheAimPointChasesTheTargetAtTheRowsPrecision()
    {
        AiCombatParameters parameters = Parameters(aimPrecision: 0.5f);
        AiGuardCombat combat = new(parameters, () => 0.0);
        combat.Begin();
        combat.Update(Sense(), standing: true, ViewLength);

        Assert.Equal(0.0, combat.TrackedAim.Y, 3);

        // The target jumps 8 metres; the aim closes half the gap each tick.
        AiCombatSense moved = Sense() with { TargetPosition = new Vector3d(0, 8 * Metre, 0) };
        combat.Update(moved, standing: true, ViewLength);
        Assert.Equal(4 * Metre, combat.TrackedAim.Y, 1);

        combat.Update(moved, standing: true, ViewLength);
        Assert.Equal(6 * Metre, combat.TrackedAim.Y, 1);
    }

    /// <summary>A weapon that does not fire in bursts never reaches the trigger.</summary>
    [Fact]
    public void AKnifeNeverFires()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        AiCombatCommand command = combat.Update(
            Sense() with { Category = WeaponCategory.Knife }, standing: true, ViewLength);

        Assert.NotNull(command.StartedBurst);
        Assert.Equal(0, command.StartedBurst!.Value.Rounds);
        Assert.Equal(AiCombatPhase.Fire, combat.Phase);

        combat.ReportAimed(true);
        command = combat.Update(Sense() with { Category = WeaponCategory.Knife }, standing: true, ViewLength);
        Assert.False(command.PullTrigger);
    }

    /// <summary>A guard waits for its standing stance before doing anything at all.</summary>
    [Fact]
    public void AGuardWaitsForItsStance()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();

        AiCombatCommand command = combat.Update(Sense(), standing: false, ViewLength);

        Assert.Null(command.Aim);
        Assert.Null(command.TurnTo);
        Assert.Equal(AiCombatPhase.Stand, combat.Phase);
    }

    /// <summary>The first burst of an engagement is deliberately half as accurate.</summary>
    /// <remarks>
    /// <c>sub_48B9C0</c>'s second argument halves the hit probability and the caller clears the
    /// argument-block byte straight after, so it fires exactly once per engagement. The guard arm keeps
    /// the byte at <c>argBlock+40</c> and the patrol arm at <c>argBlock+32</c>, which is why two different
    /// offsets for it are both right.
    /// </remarks>
    [Fact]
    public void TheFirstBurstOfAnEngagementIsHalfAsAccurate()
    {
        AiGuardCombat combat = Guard();
        combat.Begin();
        Assert.True(combat.FirstFirePending);

        AiCombatCommand first = combat.Update(Sense(), standing: true, primaryViewLengthInUnits: 0.0);

        Assert.NotNull(first.StartedBurst);
        Assert.False(combat.FirstFirePending);

        // The row's minimum-range probability is 1.0, so a halved first burst reads 0.5.
        Assert.Equal(0.5f, first.StartedBurst!.Value.HitProbability, 5);
    }

    [Fact]
    public void AnRpgFiresOnItsFirstDecisionWithoutADraw()
    {
        // 0x48CB40 installs +15824 = -1: only a state of 0 (straight after a shot) draws. A draw below
        // 0.4 would change stance instead of firing.
        AiGuardCombat rpg = new(Parameters(), () => 0.1);

        AiCombatCommand command = rpg.UpdateRpg(Sense(), inStance: true, ViewLength);

        Assert.NotNull(command.StartedBurst);
        Assert.False(rpg.RpgKneeling);
        Assert.Null(command.Aim);
    }

    [Theory]
    [InlineData(0.39, true)]
    [InlineData(0.40000001, false)]
    public void AfterAShotAnRpgChangesStanceOnADrawBelowPointFour(double draw, bool changes)
    {
        AiGuardCombat rpg = new(Parameters(maximumRoundsPerSequence: 1), () => draw);
        rpg.UpdateRpg(Sense(), inStance: true, ViewLength);
        rpg.UpdateRpg(Sense() with { AmmoInClip = 29 }, inStance: true, ViewLength);
        rpg.UpdateRpg(Sense() with { AmmoInClip = 29 }, inStance: true, ViewLength);
        Assert.Equal(AiCombatPhase.Stand, rpg.Phase);

        AiCombatCommand decision = rpg.UpdateRpg(Sense() with { AmmoInClip = 29 }, inStance: true, ViewLength);

        Assert.Equal(changes, rpg.RpgKneeling);
        Assert.Equal(!changes, decision.StartedBurst is not null);
    }

    [Fact]
    public void AnEmptyRpgReloadsAtTheShoulderAndThenFiresWithoutADraw()
    {
        // Every draw is below 0.4: a stance draw would change stance instead of firing.
        AiGuardCombat rpg = new(Parameters(), () => 0.1);

        // From the stand tick, 0x48AE70 switches to the kneel tick and the reload starts at once.
        AiCombatCommand reload = rpg.UpdateRpg(Sense() with { AmmoInClip = 0 }, inStance: true, ViewLength);
        Assert.True(rpg.RpgKneeling);
        Assert.Equal(AiCombatPhase.Reload, rpg.Phase);
        Assert.True(reload.ReleaseTrigger);

        // 0x48DD10 is done only with a round loaded AND the body kneeling.
        rpg.UpdateRpg(Sense(), inStance: false, ViewLength);
        Assert.Equal(AiCombatPhase.Reload, rpg.Phase);
        rpg.UpdateRpg(Sense(), inStance: true, ViewLength);
        Assert.Equal(AiCombatPhase.Stand, rpg.Phase);

        Assert.NotNull(rpg.UpdateRpg(Sense(), inStance: true, ViewLength).StartedBurst);
        Assert.True(rpg.RpgKneeling);
    }

    [Fact]
    public void AnRpgIsNotHeldBackByTheCrowdOfAimers()
    {
        AiGuardCombat rpg = new(Parameters(), () => 0.99);

        AiCombatCommand command = rpg.UpdateRpg(
            Sense() with { AimersOnTarget = AiCombatTarget.MaximumAimers }, inStance: true, ViewLength);

        Assert.NotNull(command.StartedBurst);
    }

    [Fact]
    public void AMountedGunnerDropsItsBurstTheMomentItLosesSight()
    {
        AiGuardCombat gunner = Guard();
        Vector3d record = new(1, 2, 3);
        Assert.NotNull(gunner.UpdateMounted(Sense(), record, ViewLength).StartedBurst);
        Assert.Equal(AiCombatPhase.Fire, gunner.Phase);

        AiCombatCommand unseen = gunner.UpdateMounted(Sense() with { Visible = false }, record, ViewLength);

        Assert.Equal(AiCombatPhase.Stand, gunner.Phase);
        Assert.False(unseen.PullTrigger);
        Assert.Equal(AiAimMode.None, unseen.Aim);
        Assert.Equal(record, unseen.TurnTo);
    }

    [Fact]
    public void AMountedGunnerStartsItsNextBurstOnTheTickTheLastOneEnds()
    {
        AiGuardCombat gunner = new(Parameters(maximumRoundsPerSequence: 1), () => 0.0);
        gunner.UpdateMounted(Sense(), default, ViewLength);
        gunner.UpdateMounted(Sense() with { AmmoInClip = 29 }, default, ViewLength);

        AiCombatCommand next = gunner.UpdateMounted(Sense() with { AmmoInClip = 29 }, default, ViewLength);

        Assert.NotNull(next.StartedBurst);
        Assert.Equal(AiCombatPhase.Fire, gunner.Phase);
    }

    private static AiCombatCommand RunOneReload(AiGuardCombat combat)
    {
        combat.Update(Sense() with { AmmoInClip = 0 }, standing: true, ViewLength);
        AiCombatCommand command = combat.Update(Sense() with { AmmoInClip = 0 }, standing: true, ViewLength);
        combat.Update(Sense(), standing: true, ViewLength);

        // And clear the burst the stand tick then starts, so the next reload begins from the stand state.
        while (combat.Phase == AiCombatPhase.Fire)
        {
            combat.Update(Sense() with { AmmoInClip = 0 }, standing: true, ViewLength);
        }

        return command;
    }

    private static AiCombatSense Sense() => new(
        HasDetectionSlot: true,
        InCone: true,
        Visible: true,
        TargetPosition: default,
        DistanceToTargetInUnits: 0.0,
        LocomotionState: 0,
        AmmoInClip: 30,
        Category: WeaponCategory.Gun,
        ClipSize: 30,
        BurstSize: 30,
        AimersOnTarget: 0);

    private static AiGuardCombat Guard(int burstSize = 30)
    {
        _ = burstSize;
        return new AiGuardCombat(Parameters(), () => 0.0);
    }

    // A GUARD_AK row on GD_2, with the fields each test needs overridable.
    private static AiCombatParameters Parameters(
        float targetingTimeoutSeconds = 15f,
        float minimumShotDelaySeconds = 0f,
        float aimPrecision = 0.6f,
        int maximumRoundsPerSequence = 30) =>
        new(new HumanAiConfigItemTask
        {
            AiTypeName = "AITYPE_GUARD_AK",
            DifficultyName = "GD_2",
            TargetingTimeoutSeconds = targetingTimeoutSeconds,
            MinimumClipFraction = 0.8f,
            RandomClipFraction = 0.2f,
            MaximumRoundsPerSequence = maximumRoundsPerSequence,
            HitProbabilityAtMinimumRange = 1.0f,
            HitProbabilityAtMaximumRange = 0.3f,
            MinimumShotDelaySeconds = minimumShotDelaySeconds,
            RandomShotDelaySeconds = 0f,
            AimPrecision = aimPrecision,
        });
}
