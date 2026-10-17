using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using Xunit;
using Signal = OpenIGI.Game.Player.HumanSoldierBodyMachine.Signal;
using OpenIGI.Game.Igi1.Ai;

namespace OpenIGI.Game.Tests;

/// <summary>The noises an enemy human makes, as <c>0x450110</c> admits them.</summary>
public sealed class AiHumanNoiseTests
{
    private const double Metre = 4096.0;

    [Theory]
    [InlineData(AiEvent.Door)]
    [InlineData(AiEvent.Fence)]
    [InlineData(AiEvent.Ladder)]
    public void ANoiseCarriesFiveMetresAndNoFurther(AiEvent type)
    {
        AiSoldier near = new(graph: null, default, facing: 0f, phase: 0, seed: 1);
        AiSoldier far = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(near.ReceiveHumanNoise(type, new Vector3d(4.9 * Metre, 0, 0), default, AiSoldier.HumanNoiseRadiusInUnits));
        Assert.False(far.ReceiveHumanNoise(type, new Vector3d(5.1 * Metre, 0, 0), default, AiSoldier.HumanNoiseRadiusInUnits));
        Assert.Equal(1, near.Events.PendingCount(type));
        Assert.Equal(0, far.Events.PendingCount(type));
    }

    [Fact]
    public void TheRangeTestPassesAtExactlyTheRadius()
    {
        // 0x450110 rejects only radius < distance.
        AiSoldier soldier = new(graph: null, default, facing: 0f, phase: 0, seed: 1);

        Assert.True(soldier.ReceiveHumanNoise(
            AiEvent.Door, new Vector3d(AiSoldier.HumanNoiseRadiusInUnits, 0, 0), default, AiSoldier.HumanNoiseRadiusInUnits));
    }

    [Fact]
    public void AFriendlyDoesNotHearItsOwnSide()
    {
        // Filter 0x46 carries OtherSide: a soldier on the player's team hears none of the player's noises.
        AiSoldier friendly = new(graph: null, default, facing: 0f, phase: 0, seed: 1) { PlayerIsEnemy = false };

        Assert.False(friendly.ReceiveHumanNoise(AiEvent.Door, default, default, AiSoldier.HumanNoiseRadiusInUnits));
    }

    [Fact]
    public void ASoldiersRoundGoingPastIsHeardByTheOtherSideOnly()
    {
        // 0x4606F0 asks only whose side fired: a friendly hears a guard's round go by, not its own side's.
        AiSoldier otherSide = new(graph: null, default, facing: 0f, phase: 0, seed: 1) { PlayerIsEnemy = false };
        AiSoldier sameSide = new(graph: null, default, facing: 0f, phase: 0, seed: 1) { PlayerIsEnemy = false };
        Vector3d start = new(-10 * Metre, 1 * Metre, otherSide.EyePosition().Z);
        Vector3d end = new(10 * Metre, 1 * Metre, otherSide.EyePosition().Z);

        Assert.True(otherSide.ReceiveGunshotMiss(start, end, start, fromOtherSide: true, source: 42));
        Assert.False(sameSide.ReceiveGunshotMiss(start, end, start, fromOtherSide: false, source: 42));
        Assert.Equal(42, otherSide.Events.HeadOf(AiEvent.GunshotMiss)!.Value.Source);
    }

    [Fact]
    public void TheUseObjectStateLeavesOnlyOnAFreshPress()
    {
        // 0x45C9B0: slot 24 set this frame and clear the last, or death. Seating the body consumes the slot,
        // so it sits until the slot is written again.
        HumanSoldierBodyMachine body = new();
        body.EnterUseObject();
        Assert.Equal(HumanSoldierStates.UseObjectState, body.Tick(Signal.OnGround));
        Assert.Equal(HumanSoldierStates.StandState, body.Tick(Signal.OnGround | Signal.UseObject));
    }
}
