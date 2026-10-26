using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Minefields: the wait between blasts both games share, and Project I.G.I.'s as its levels set them.</summary>
[Collection(RetailMissionWorld.Name)]
public sealed class MineFieldTests
{
    [Fact]
    public void AMineFieldGoesOffOnlyOnceItHasWaited()
    {
        MineFieldPause pause = new();
        Assert.True(pause.Due(static () => true));

        pause.Ticks = 2;
        int asked = 0;
        Assert.False(pause.Due(() => ++asked > 0));
        Assert.False(pause.Due(() => ++asked > 0));
        Assert.Equal(0, asked);
        Assert.True(pause.Due(() => ++asked > 0));
        Assert.Equal(1, asked);
    }

    [Fact]
    public void ExplodingOnceWaitsForEver()
    {
        // "-1: explode once" is a wait of -30 ticks, which counts down past nought and never reaches it.
        MineFieldPause pause = new() { Ticks = (int)(-1f * 30f) };
        for (int tick = 0; tick < 1000; tick++)
        {
            Assert.False(pause.Due(static () => true));
        }

        Assert.False(new MineFieldPause { Ticks = -1 }.Due(static () => true));
    }

    [RequiresGameDataFact]
    public void MissionSevensMineFieldBlastsAtThePlayerEveryFiveSeconds()
    {
        Igi1MineField field = Assert.Single(Igi1MineFields.Build(RetailMissionData.Get(7).Root));

        Assert.Equal(0, field.TargetId);
        Assert.Equal(3f * 4096f, field.RadiusUnits);
        Assert.Equal(1.5f * 4096f, field.FalloffUnits);
        Assert.Equal(4.5f, field.Damage);
        Assert.Equal(5f, field.WaitSeconds);
        Assert.Equal(3, field.Fragments);
        Assert.Equal(3, field.Fireballs);
        Assert.True(field.SnapsToGround);
        Assert.Contains("AreaActivate_151.nActive", field.Condition, StringComparison.Ordinal);
    }
}
