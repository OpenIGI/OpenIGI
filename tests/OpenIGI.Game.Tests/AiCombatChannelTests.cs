using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The two channel tables an AI reaches its body through, and the shared record it fights over.
/// </summary>
public sealed class AiCombatChannelTests
{
    /// <summary>Every one of the twenty-nine types has a row in both tables.</summary>
    [Fact]
    public void EveryTypeHasBothRows()
    {
        foreach (AiType type in Enum.GetValues<AiType>())
        {
            for (int channel = 0; channel < AiCombatChannels.InputChannelCount; channel++)
            {
                int slot = AiCombatChannels.SlotOf(type, (AiCombatChannel)channel);
                Assert.True(slot == AiCombatChannels.None || slot is >= 20 and <= 30);
            }

            for (int channel = 0; channel < AiCombatChannels.StanceChannelCount; channel++)
            {
                int state = AiCombatChannels.StateOf(type, (AiStanceChannel)channel);
                Assert.True(state == AiCombatChannels.None || state is >= 0 and < 49);
            }
        }
    }

    /// <summary>
    /// The two tables agree on which capabilities a type has, which is what shows they describe the
    /// same thing from two directions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Channels 0 to 2 line up between the two — the callers use the same index in both — so a type
    /// that cannot be asked to kneel should have no kneeling state either. The <c>RPG</c> is the case
    /// that makes it visible: no lie-down in either table.
    /// </para>
    /// <para>
    /// <b>The civilian is the one exception, and it is the original's own inconsistency.</b> Its input
    /// row carries kneel and lie-down channels while its stance row has neither, so a civilian told to
    /// kneel would raise the slot and then wait for a state it can never enter. No shipped behaviour
    /// asks a civilian to do either — <c>AIAction_Combat</c> has no arm for one at all — so the
    /// contradiction never runs. Pinned rather than tidied.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInputAndStanceTablesAgreeOnWhichStancesATypeHasExceptForTheCivilian()
    {
        foreach (AiType type in Enum.GetValues<AiType>())
        {
            if (type == AiType.Civilian)
            {
                continue;
            }

            for (int channel = 0; channel <= 2; channel++)
            {
                bool canAsk = AiCombatChannels.SlotOf(type, (AiCombatChannel)channel) != AiCombatChannels.None;
                bool canWait = AiCombatChannels.StateOf(type, (AiStanceChannel)channel) != AiCombatChannels.None;
                Assert.True(canAsk == canWait, $"{type} disagrees on channel {channel}");
            }
        }

        Assert.Equal(AiCombatChannels.None, AiCombatChannels.SlotOf(AiType.Rpg, AiCombatChannel.LieDown));
        Assert.Equal(AiCombatChannels.None, AiCombatChannels.StateOf(AiType.Rpg, AiStanceChannel.Prone));

        // The civilian: asked for, never reachable.
        Assert.NotEqual(AiCombatChannels.None, AiCombatChannels.SlotOf(AiType.Civilian, AiCombatChannel.Kneel));
        Assert.Equal(AiCombatChannels.None, AiCombatChannels.StateOf(AiType.Civilian, AiStanceChannel.Kneeling));
    }

    /// <summary>A guard has no mounted stance, so the firing tick's emplacement branch is dead for one.</summary>
    [Fact]
    public void AGuardHasNoMountedStanceAndAPatrolDoes()
    {
        Assert.Equal(AiCombatChannels.None, AiCombatChannels.StateOf(AiType.GuardAk, AiStanceChannel.Mounted));
        Assert.Equal(36, AiCombatChannels.StateOf(AiType.PatrolAk, AiStanceChannel.Mounted));
        Assert.Equal(36, AiCombatChannels.StateOf(AiType.Gunner, AiStanceChannel.Mounted));
    }

    /// <summary>A guard's stances are the soldier state machine's 0, 19 and 26.</summary>
    /// <remarks>
    /// Which is a cross-check against <c>player.md</c> §16: state 0 is standing still, 19 is crouched
    /// still and 26 the last of the crouched movement set — read there off the state-to-animation table
    /// with no reference to the AI at all.
    /// </remarks>
    [Fact]
    public void AGuardsStancesAreTheSoldierMachinesOwnStates()
    {
        Assert.Equal(0, AiCombatChannels.StateOf(AiType.GuardAk, AiStanceChannel.Standing));
        Assert.Equal(19, AiCombatChannels.StateOf(AiType.GuardAk, AiStanceChannel.Kneeling));
        Assert.Equal(26, AiCombatChannels.StateOf(AiType.GuardAk, AiStanceChannel.Prone));
        Assert.Equal(35, AiCombatChannels.StateOf(AiType.GuardAk, AiStanceChannel.AnimationPlaying));

        Assert.True(AiCombatChannels.IsInStance(AiType.GuardAk, AiStanceChannel.Standing, 0));
        Assert.False(AiCombatChannels.IsInStance(AiType.GuardAk, AiStanceChannel.Standing, 19));
        Assert.False(AiCombatChannels.IsInStance(AiType.GuardAk, AiStanceChannel.Mounted, -1));
    }

    /// <summary>The slots a soldier's channels drive are the frame's high, soldier-only ones.</summary>
    [Fact]
    public void AGuardsChannelsMapOntoTheHighInputSlots()
    {
        Assert.True(AiCombatChannels.TrySlotOf(AiType.GuardAk, AiCombatChannel.Stand, out PlayerInputSlot stand));
        Assert.Equal(20, (int)stand);

        Assert.Equal(27, AiCombatChannels.SlotOf(AiType.GuardAk, AiCombatChannel.Fire));
        Assert.Equal(29, AiCombatChannels.SlotOf(AiType.GuardAk, AiCombatChannel.ThrowGrenade));

        // Slot 26 is skipped by every ten-channel row, which is the one irregularity in the tables.
        for (int channel = 0; channel < AiCombatChannels.InputChannelCount; channel++)
        {
            Assert.NotEqual(26, AiCombatChannels.SlotOf(AiType.GuardAk, (AiCombatChannel)channel));
        }
    }

    /// <summary>The four-attacker rule, and what the fifth AI does instead.</summary>
    [Fact]
    public void TheFifthAttackerIsCrowdedOut()
    {
        AiCombatTarget target = new(new object());
        object[] attackers = [new(), new(), new(), new(), new()];

        foreach (object attacker in attackers)
        {
            target.Add(attacker);
            target.Aim(attacker, target.ModeToTake(attacker));
        }

        Assert.Equal(AiCombatTarget.MaximumAimers, target.Count(AiAimMode.Aiming));
        Assert.Equal(1, target.Count(AiAimMode.Crowded));
        Assert.Equal(AiAimMode.Crowded, target.ModeOf(attackers[4]));
    }

    /// <summary>An attacker joins on no aim mode at all, not on the ordinary one.</summary>
    [Fact]
    public void AnAttackerJoinsWithoutAiming()
    {
        AiCombatTarget target = new(new object());
        object attacker = new();

        target.Add(attacker);
        Assert.Equal(AiAimMode.None, target.ModeOf(attacker));
        Assert.Equal(0, target.Count(AiAimMode.Aiming));
    }

    /// <summary>Removing the last attacker frees the record; removing any other swaps it out.</summary>
    [Fact]
    public void RemovingTheLastAttackerFreesTheRecord()
    {
        AiCombatTarget target = new(new object());
        object first = new();
        object second = new();
        object third = new();

        target.Add(first);
        target.Add(second);
        target.Add(third);

        Assert.False(target.Remove(first));

        // The swap-with-last is visible: the third attacker now sits where the first did.
        Assert.Equal([third, second], target.Attackers);

        Assert.False(target.Remove(third));
        Assert.True(target.Remove(second));
        Assert.False(target.IsLive);
    }

    /// <summary>The shared position is whatever the last attacker to look wrote.</summary>
    [Fact]
    public void ThePositionIsShared()
    {
        AiCombatTarget target = new(new object());
        target.Refresh(new Vector3d(1, 2, 3));
        Assert.Equal(new Vector3d(1, 2, 3), target.Position);
    }

    /// <summary>A weapon with its own burst length overrides the clip fraction and the row's cap.</summary>
    /// <remarks>
    /// So a three-round-burst weapon fires three rounds however the settings row is tuned. The clamp is
    /// on the <i>other</i> branch only, which is what makes this an override rather than a maximum.
    /// </remarks>
    [Fact]
    public void AWeaponsOwnBurstLengthOverridesTheSettingsRow()
    {
        AiCombatParameters parameters = new(new HumanAiConfigItemTask
        {
            MinimumClipFraction = 0.8f,
            RandomClipFraction = 0f,
            MaximumRoundsPerSequence = 2,
            HitProbabilityAtMinimumRange = 1f,
            HitProbabilityAtMaximumRange = 0f,
        });

        AiCombatBurst burst = AiCombatBurst.Begin(
            parameters, WeaponCategory.Gun, clipSize: 30, burstSize: 3,
            distanceToTargetInUnits: 0, primaryViewLengthInUnits: 40 * 4096.0, random: 0);
        Assert.Equal(3, burst.Rounds);

        // Without one, the clip fraction applies and the row's cap bites.
        AiCombatBurst capped = AiCombatBurst.Begin(
            parameters, WeaponCategory.Gun, clipSize: 30, burstSize: 30,
            distanceToTargetInUnits: 0, primaryViewLengthInUnits: 40 * 4096.0, random: 0);
        Assert.Equal(2, capped.Rounds);
    }

    /// <summary>The hit probability interpolates over the primary view length and clamps beyond it.</summary>
    [Fact]
    public void TheHitProbabilityInterpolatesOverTheViewCone()
    {
        AiCombatParameters parameters = new(new HumanAiConfigItemTask
        {
            MinimumClipFraction = 0.5f,
            HitProbabilityAtMinimumRange = 1.0f,
            HitProbabilityAtMaximumRange = 0.3f,
            MaximumRoundsPerSequence = 30,
        });

        const double view = 40 * 4096.0;

        Assert.Equal(1.0f, Probability(parameters, 0.0, view), 4);
        Assert.Equal(0.65f, Probability(parameters, view / 2, view), 4);
        Assert.Equal(0.3f, Probability(parameters, view, view), 4);

        // Beyond the cone the distance is clamped, so it never extrapolates past the maximum figure.
        Assert.Equal(0.3f, Probability(parameters, view * 4, view), 4);
    }

    private static float Probability(AiCombatParameters parameters, double distance, double view) =>
        AiCombatBurst.Begin(
            parameters, WeaponCategory.Gun, clipSize: 30, burstSize: 30,
            distanceToTargetInUnits: distance, primaryViewLengthInUnits: view, random: 0).HitProbability;
}
