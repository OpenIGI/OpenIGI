using OpenIGI.Game.Multiplayer.Modes;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

public sealed class BombWaveTests
{
    [Fact]
    public void The_front_is_held_a_second_then_swells_at_its_speed()
    {
        Assert.Equal(0.0, BombWave.FrontMetres(0.0));
        Assert.Equal(0.0, BombWave.FrontMetres(BombWave.HoldSeconds));
        Assert.Equal(BombWave.FrontMetresPerSecond, BombWave.FrontMetres(BombWave.HoldSeconds + 1.0), 6);

        // It runs no further than its longest.
        Assert.Equal(BombWave.FrontMetres(BombWave.HoldSeconds + BombWave.MostSeconds), BombWave.FrontMetres(1000.0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(40.0)]
    [InlineData(76.2)]
    public void A_place_hears_the_front_a_beat_before_it_is_reached(double metres)
    {
        double arrives = BombWave.ArrivalSeconds(metres);
        Assert.Equal(BombWave.HoldSeconds + (metres / BombWave.FrontMetresPerSecond), arrives, 9);

        Assert.False(BombWave.IsHeard(arrives - BombWave.CueLeadSeconds - 0.01, metres));
        Assert.True(BombWave.IsHeard(arrives - BombWave.CueLeadSeconds, metres));
        Assert.False(BombWave.HasReached(arrives - 0.01, metres));
        Assert.True(BombWave.HasReached(arrives, metres));
    }

    [Fact]
    public void The_front_is_spent_past_the_farthest_place_or_at_its_longest()
    {
        Assert.False(BombWave.IsSpent(BombWave.ArrivalSeconds(50.0) - 0.01, 50.0));
        Assert.True(BombWave.IsSpent(BombWave.ArrivalSeconds(50.0) + 0.05, 50.0));
        Assert.True(BombWave.IsSpent(BombWave.HoldSeconds + BombWave.MostSeconds, double.MaxValue));
    }

    [Fact]
    public void A_harder_hit_shakes_and_throws_the_view_further()
    {
        Assert.True(BombWave.ShakeScale(0.9f) > BombWave.ShakeScale(0.1f));
        Assert.Equal(BombWave.ShakeScale(1f), BombWave.ShakeScale(3f));
        Assert.InRange(float.RadiansToDegrees(BombWave.KickRadians(0f)), 9.99f, 10.01f);
        Assert.InRange(float.RadiansToDegrees(BombWave.KickRadians(1f)), 19.99f, 20.01f);
        Assert.Equal(0f, BombWave.ShoveShare(0f));
        Assert.Equal(1f, BombWave.ShoveShare(2f));
    }

    [Fact]
    public void The_bell_gives_out_where_its_reach_says()
    {
        double reach = BombDamage.ReachMetres(40.0);
        Assert.True(BombDamage.Share(reach - 0.5, 40.0) > 0f);
        Assert.Equal(0f, BombDamage.Share(reach + 0.5, 40.0));
    }
}
