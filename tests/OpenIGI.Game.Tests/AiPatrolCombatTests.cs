using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The move-and-fight arm's two decisions — <c>sub_48BFD0</c> and <c>sub_48BE30</c>.
/// </summary>
public sealed class AiPatrolCombatTests
{
    [Fact]
    public void StandingOffItsOwnNodeRepositions()
    {
        AiPatrolCombat patrol = Patrol();

        Assert.Equal(
            AiPatrolAction.RePosition,
            patrol.Choose(Sense(standingOnOwnNode: false)));
    }

    [Fact]
    public void AlreadyWalkingSomewhereDoesNotReposition()
    {
        AiPatrolCombat patrol = Patrol();

        // Beyond the tracking distance with a free node ahead sets the destination.
        patrol.Choose(Sense(distance: 100_000.0, freeToward: 7));
        Assert.Equal(7, patrol.DestinationNodeId);

        // Now off its node, but with a destination, gate (a) does not fire.
        Assert.NotEqual(
            AiPatrolAction.RePosition,
            patrol.Choose(Sense(standingOnOwnNode: false)));
    }

    [Fact]
    public void InsideOneAndAHalfMetresGoesToMelee()
    {
        AiPatrolCombat patrol = Patrol();

        Assert.Equal(
            AiPatrolAction.CloseCombat,
            patrol.Choose(Sense(distance: AiPatrolCombat.CloseCombatRangeInUnits - 1.0)));

        Assert.Equal(
            AiPatrolAction.Fire,
            patrol.Choose(Sense(distance: AiPatrolCombat.CloseCombatRangeInUnits)));
    }

    [Fact]
    public void BeyondTheTrackingDistanceItAdvances()
    {
        AiPatrolCombat patrol = Patrol();

        Assert.Equal(
            AiPatrolAction.Follow,
            patrol.Choose(Sense(distance: 100_000.0, freeToward: 12)));
        Assert.Equal(12, patrol.DestinationNodeId);
    }

    [Fact]
    public void ItWillNotAdvanceOntoTheNodeItIsAlreadyOn()
    {
        AiPatrolCombat patrol = Patrol();

        Assert.Equal(
            AiPatrolAction.Fire,
            patrol.Choose(Sense(distance: 100_000.0, currentNode: 4, freeToward: 4)));
    }

    /// <summary>A patrol is crowded out at three attackers where a guard's figure is four.</summary>
    [Fact]
    public void ThreeAimersCrowdItOutOfFiring()
    {
        AiPatrolCombat patrol = Patrol();

        Assert.Equal(AiPatrolAction.Fire, patrol.Choose(Sense(aimers: 2)));
        Assert.Equal(AiPatrolAction.LookForTarget, patrol.Choose(Sense(aimers: 3)));
        Assert.True(AiPatrolCombat.CrowdedAimerCount < AiCombatTarget.MaximumAimers);
    }

    [Fact]
    public void AnEmptyClipReloads()
    {
        AiPatrolCombat patrol = Patrol();

        Assert.Equal(AiPatrolAction.Reload, patrol.Choose(Sense(ammo: 0)));
    }

    [Fact]
    public void NoDetectionSlotEndsTheAction()
    {
        AiPatrolCombat patrol = Patrol();

        Assert.Equal(AiPatrolAction.End, patrol.Choose(Sense(hasSlot: false)));
    }

    /// <summary>Ticks 0–15 of every second are spent watching; 16–29 try to move.</summary>
    [Fact]
    public void TheSearchCycleWatchesThenMoves()
    {
        AiPatrolCombat patrol = Patrol();
        List<AiPatrolAction> actions = [];

        for (int i = 0; i < AiPatrolCombat.SearchCycleTicks; i++)
        {
            // No free node anywhere, so the move half has nothing to take.
            actions.Add(patrol.Choose(Sense(visible: false)));
        }

        // 1..15 watch, 16..29 still watch because nothing was found, and 30 wraps to watching.
        Assert.All(actions, a => Assert.Equal(AiPatrolAction.LookForTarget, a));
        Assert.Equal(AiPatrolCombat.SearchCycleTicks, patrol.NotVisibleTicks);
    }

    /// <summary>The headline: somewhere new to look resets the targeting timeout.</summary>
    [Fact]
    public void FindingSomewhereNewToLookResetsTheTimeout()
    {
        AiPatrolCombat patrol = Patrol();

        for (int i = 0; i < AiPatrolCombat.SearchWatchTicks + 1; i++)
        {
            patrol.Choose(Sense(visible: false));
        }

        Assert.Equal(AiPatrolCombat.SearchWatchTicks + 1, patrol.NotVisibleTicks);

        // Now the move half of the cycle finds a node.
        Assert.Equal(
            AiPatrolAction.Follow,
            patrol.Choose(Sense(visible: false, currentNode: 1, freeAway: 9)));

        Assert.Equal(9, patrol.DestinationNodeId);
        Assert.Equal(0, patrol.NotVisibleTicks);
    }

    /// <summary>It only gives up once the search is exhausted.</summary>
    [Fact]
    public void ItGivesUpOnlyAfterTheTimeoutWithNowhereLeftToLook()
    {
        // One second of timeout.
        AiPatrolCombat patrol = new(Parameters(targetingTimeoutSeconds: 1f), () => 0.0);

        AiPatrolAction last = AiPatrolAction.LookForTarget;
        for (int i = 0; i < 40 && last != AiPatrolAction.End; i++)
        {
            last = patrol.Choose(Sense(visible: false));
        }

        Assert.Equal(AiPatrolAction.End, last);
    }

    [Fact]
    public void AGuardThatKeepsFindingNodesNeverTimesOut()
    {
        AiPatrolCombat patrol = new(Parameters(targetingTimeoutSeconds: 1f), () => 0.0);

        // Alternate the node it finds so the destination is always somewhere new.
        for (int i = 0; i < 200; i++)
        {
            AiPatrolAction action = patrol.Choose(
                Sense(visible: false, currentNode: 0, freeAway: 1 + (i % 5)));
            Assert.NotEqual(AiPatrolAction.End, action);
        }
    }

    [Fact]
    public void EvasionIsConsideredAtMostEveryTenTicks()
    {
        // p == 0 on the roll parameter always lies down, which makes the outcome deterministic and
        // leaves the cooldown as the only thing under test.
        AiPatrolCombat patrol = new(
            Parameters(evasiveActionProbability: 1f, rollOrLieDownProbability: 0f),
            AlwaysZero());

        Assert.Equal(AiEvasiveAction.LieDown, patrol.ChooseEvasion(0, 0.0, AiRollSpace.Left, false));

        for (int tick = 1; tick <= AiPatrolCombat.EvasionCooldownTicks; tick++)
        {
            Assert.Equal(
                AiEvasiveAction.None,
                patrol.ChooseEvasion(tick, 0.0, AiRollSpace.Left, false));
        }

        Assert.Equal(
            AiEvasiveAction.LieDown,
            patrol.ChooseEvasion(AiPatrolCombat.EvasionCooldownTicks + 1, 0.0, AiRollSpace.Left, false));
    }

    /// <summary>A zero roll probability means "always lie down", tested as an exact float compare.</summary>
    [Fact]
    public void AZeroRollProbabilityAlwaysLiesDown()
    {
        AiPatrolCombat patrol = new(
            Parameters(evasiveActionProbability: 1f, rollOrLieDownProbability: 0f),
            AlwaysZero());

        Assert.Equal(
            AiEvasiveAction.LieDown,
            patrol.ChooseEvasion(0, 0.0, AiRollSpace.Left | AiRollSpace.Right, false));
    }

    /// <summary>A soldier never rolls the same way twice running.</summary>
    [Fact]
    public void ItAlternatesTheRollDirection()
    {
        AiPatrolCombat patrol = new(
            Parameters(evasiveActionProbability: 1f, rollOrLieDownProbability: 1f, rollSideProbability: 1f),
            AlwaysZero());

        // p == 1 short-circuits to the right, but only while the last roll was not itself right.
        AiEvasiveAction first = patrol.ChooseEvasion(0, 0.0, AiRollSpace.Left | AiRollSpace.Right, false);
        AiEvasiveAction second = patrol.ChooseEvasion(
            AiPatrolCombat.EvasionCooldownTicks + 1, 0.0, AiRollSpace.Left | AiRollSpace.Right, false);

        Assert.Equal(AiEvasiveAction.RollRight, first);
        Assert.NotEqual(AiEvasiveAction.RollRight, second);
    }

    [Fact]
    public void ARollNeedsNodeOnThatSide()
    {
        AiPatrolCombat patrol = new(
            Parameters(evasiveActionProbability: 1f, rollOrLieDownProbability: 1f, rollSideProbability: 1f),
            AlwaysZero());

        // Right is the only side p == 1 takes, and there is no node there.
        Assert.Equal(
            AiEvasiveAction.None,
            patrol.ChooseEvasion(0, 0.0, AiRollSpace.None, false));
    }

    [Fact]
    public void AForbiddenNodeBlocksEvasionButStillSpendsTheCooldown()
    {
        AiPatrolCombat patrol = new(Parameters(evasiveActionProbability: 1f), AlwaysZero());

        Assert.Equal(
            AiEvasiveAction.None,
            patrol.ChooseEvasion(0, 0.0, AiRollSpace.Left, onForbiddenNode: true));

        // The cooldown was taken before the gate, so the next tick is still barred.
        Assert.Equal(
            AiEvasiveAction.None,
            patrol.ChooseEvasion(1, 0.0, AiRollSpace.Left, onForbiddenNode: false));
    }

    private static Func<double> AlwaysZero() => () => 0.0;

    private static AiPatrolCombat Patrol() => new(Parameters(), () => 0.0);

    private static AiPatrolSense Sense(
        bool hasSlot = true,
        bool visible = true,
        double distance = 20_000.0,
        int currentNode = 1,
        bool standingOnOwnNode = true,
        int ammo = 30,
        bool hasGrenade = false,
        int aimers = 0,
        int? freeToward = null,
        int? freeAway = null) =>
        new(hasSlot, visible, distance, currentNode, standingOnOwnNode, ammo, hasGrenade, aimers, freeToward, freeAway);

    // A PATROL_AK row on GD_2, with the fields each test needs overridable.
    private static AiCombatParameters Parameters(
        float targetingTimeoutSeconds = 15f,
        float evasiveActionProbability = 0f,
        float rollOrLieDownProbability = 0.5f,
        float rollSideProbability = 0.5f) =>
        new(new HumanAiConfigItemTask
        {
            AiTypeName = "AITYPE_PATROL_AK",
            DifficultyName = "GD_2",
            TargetingTimeoutSeconds = targetingTimeoutSeconds,
            TrackingDistanceMetres = 15.0f,
            LieDownMinimumDistanceMetres = 15.0f,
            EvasiveActionProbability = evasiveActionProbability,
            EvasiveStandRollOrLieDownProbability = rollOrLieDownProbability,
            EvasiveStandRollSideProbability = rollSideProbability,
            GrenadeThrowProbability = 0f,
            GrenadeThrowWhenReloadingProbability = 0f,
            MinimumClipFraction = 0.8f,
            RandomClipFraction = 0.2f,
            MaximumRoundsPerSequence = 30,
        });
}
