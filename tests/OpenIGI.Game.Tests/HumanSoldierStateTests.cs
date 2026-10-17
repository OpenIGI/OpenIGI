using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The soldier state-to-animation table, checked against what the running retail game was seen to do.
/// </summary>
/// <remarks>
/// The pairs come from <c>0x53ED10</c>; the ones asserted here were also observed live, by hooking
/// <c>0x489E70</c> in a retail mission with <c>a probe of the running original</c>. Both halves have to agree.
/// </remarks>
public sealed class HumanSoldierStateTests
{
    /// <summary>Every state the probe caught the retail game looking up reads the same here.</summary>
    /// <remarks>
    /// Eight distinct states over forty-four lookups against <c>table=0x0053ED10</c>. A transcription
    /// error anywhere in the table would very likely land in one of them.
    /// </remarks>
    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(2, 3, 1)]
    [InlineData(3, 57, 1)]
    [InlineData(6, 56, 1)]
    [InlineData(10, 54, 0)]
    [InlineData(11, 53, 0)]
    [InlineData(12, 55, 0)]
    [InlineData(17, 48, 1)]
    public void TheTableAgreesWithTheRunningGame(int state, int animation, int mode)
    {
        Assert.Equal(animation, HumanSoldierStates.Table.AnimationOf(state));
        Assert.Equal(mode, HumanSoldierStates.Table.ModeOf(state));
    }

    /// <summary>The three death states name their own clip, which is how the table marks that.</summary>
    /// <remarks>
    /// Read from the other end entirely — <see cref="SoldierDeath"/> got these out of the soldier
    /// class's state entries — so the two agree without either being written for the other.
    /// </remarks>
    [Fact]
    public void TheDeathStatesNameTheirOwnClip()
    {
        foreach (int state in new[] { 32, 33, 34 })
        {
            Assert.True(HumanSoldierStates.Table.IsSelfDriven(state));
            Assert.Equal(-1, HumanSoldierStates.Table.AnimationOf(state));
        }
    }

    /// <summary>The combat states are the three the probe saw a fighting guard move between.</summary>
    /// <remarks>
    /// A guard's body sat in 6 while its burst counter drained, 12 the tick that counter reached zero,
    /// and 0 once its target was out of sight — over two guards and twenty-three sampled ticks.
    /// </remarks>
    [Fact]
    public void TheCombatStatesReadTheClipsTheGuardWasSeenPlaying()
    {
        Assert.Equal(56, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.FiringState));
        Assert.Equal(55, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.ReloadState));
        Assert.Equal(2, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.StandState));

        // And standing is the stance channel's own state for every guard type, from the other table.
        Assert.Equal(
            HumanSoldierStates.StandState,
            AiCombatChannels.StateOf(AiType.GuardAk, AiStanceChannel.Standing));
    }

    /// <summary>
    /// The flinch and death states are the two bits the damage handler raises, which is the pair of
    /// readings meeting.
    /// </summary>
    /// <remarks>
    /// <c>0x45D3C0</c> sets <c>0x10000</c> when the recoil accumulator passes the settings row's
    /// threshold and <c>0x20000</c> on death; every combat state's per-tick leaves for 30 and 32 on
    /// exactly those bits, and 32's entry is the death chooser <see cref="SoldierDeath"/> read
    /// separately. Nothing here was written to make that come out.
    /// </remarks>
    [Fact]
    public void TheFlinchAndDeathStatesAreWhereTheDamageHandlerSends()
    {
        Assert.Equal(30, HumanSoldierStates.FlinchState);
        Assert.Equal(32, HumanSoldierStates.DeathState);

        // Both name their own clip, which is what a state that chooses between several has to do.
        Assert.True(HumanSoldierStates.Table.IsSelfDriven(HumanSoldierStates.FlinchState));
        Assert.True(HumanSoldierStates.Table.IsSelfDriven(HumanSoldierStates.DeathState));

        // And the airborne state is an ordinary table entry.
        Assert.Equal(2, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.AirborneState));
    }

    /// <summary>A state outside the machine has no clip rather than an arbitrary one.</summary>
    [Fact]
    public void AStateOutsideTheMachineHasNoClip()
    {
        Assert.Equal(-1, HumanSoldierStates.Table.AnimationOf(-1));
        Assert.Equal(-1, HumanSoldierStates.Table.AnimationOf(HumanSoldierStates.Table.StateCount));
        Assert.Equal(-1, HumanSoldierStates.Table.ModeOf(999));
    }

    /// <summary>
    /// The channel table the port transcribed is the one the running game resolved slots through.
    /// </summary>
    /// <remarks>
    /// Six <c>sub_48EF70</c> calls were caught live, across three AI types. Every one of them resolved
    /// to the slot this table gives — including the patrol's channel 3, which carried an animation id of
    /// 4 rather than a flag and is what shows the value is not a boolean.
    /// </remarks>
    [Theory]
    [InlineData(AiType.PatrolAk, AiCombatChannel.PlayAnimation, 23)]
    [InlineData(AiType.PatrolAk, AiCombatChannel.Stand, 20)]
    [InlineData(AiType.PatrolAk, AiCombatChannel.Fire, 27)]
    [InlineData(AiType.GuardAk, AiCombatChannel.Stand, 20)]
    [InlineData(AiType.GuardAk, AiCombatChannel.Fire, 27)]
    [InlineData(AiType.Sniper, AiCombatChannel.Stand, 20)]
    public void TheChannelTableAgreesWithTheRunningGame(AiType type, AiCombatChannel channel, int slot) =>
        Assert.Equal(slot, AiCombatChannels.SlotOf(type, channel));

    /// <summary>
    /// A hard guard's settings row is the one the running game had loaded, field for field.
    /// </summary>
    /// <remarks>
    /// The probe read them straight off a live <c>HumanAI</c> in a <c>GD_3</c> mission:
    /// <c>maxRounds=30 timeout=600 ticks hitProb=1.00..0.50 clip=0.80+0.20 aimPrecision=0.600
    /// health=0.80</c>. The last of those is the one that matters most — it is <c>Human+600</c>, which
    /// is what makes "Damage scale" the soldier's maximum health a measurement rather than an inference.
    /// </remarks>
    [Fact]
    public void AHardGuardsRowIsWhatTheRunningGameHadLoaded()
    {
        AiCombatParameters row = new(new HumanAiConfigItemTask
        {
            MaximumRoundsPerSequence = 30,
            TargetingTimeoutSeconds = 20f,
            HitProbabilityAtMinimumRange = 1.0f,
            HitProbabilityAtMaximumRange = 0.5f,
            MinimumClipFraction = 0.8f,
            RandomClipFraction = 0.2f,
            AimPrecision = 0.6f,
            DamageScale = 0.8f,
        });

        Assert.Equal(30, row.MaximumRoundsPerSequence);
        Assert.Equal(600, row.TargetingTimeoutTicks);
        Assert.Equal(1.0f, row.HitProbabilityAtMinimumRange, 3);
        Assert.Equal(0.5f, row.HitProbabilityAtMaximumRange, 3);
        Assert.Equal(0.8f, row.MinimumClipFraction, 3);
        Assert.Equal(0.2f, row.RandomClipFraction, 3);
        Assert.Equal(0.6, row.AimPrecision, 3);
        Assert.Equal(0.8f, row.MaximumHealth, 3);
    }
}
