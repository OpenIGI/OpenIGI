using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

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
}
