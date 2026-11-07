using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A versus match's players take a normal soldier's body, not the campaign hero's.</summary>
public sealed class VersusBodyTests
{
    // I.G.I.'s rifle round (the AK-47's and M16's damage factor), and the zones it lands in.
    private const float Rifle = 0.3f;
    private const int Chest = 2;
    private const int Head = 15;

    [Fact]
    public void The_campaign_hero_takes_eleven_rifle_rounds_to_the_body_and_its_head_is_no_weaker()
    {
        HumanHealth hero = new(HumanHitZoneTable.Player, 3f);
        Assert.Equal(11, RoundsToKill(hero, Chest));
        Assert.Equal(11, RoundsToKill(new HumanHealth(HumanHitZoneTable.Player, 3f), Head));
    }

    [Fact]
    public void A_versus_player_falls_to_two_rifle_rounds_to_the_body_or_one_to_the_head()
    {
        HumanHealth player = Versus();
        Assert.Equal(2, RoundsToKill(player, Chest));
        Assert.Equal(1, RoundsToKill(Versus(), Head));
    }

    [Fact]
    public void Taking_the_versus_body_keeps_the_share_of_health_left_and_drops_the_difficulty_scale()
    {
        HumanHealth player = new(HumanHitZoneTable.Player, 3f) { DamageScale = HumanHealth.DifficultyDamageScale(2) };
        player.Apply(Chest, 1.5f);
        player.UseBody(HumanHitZoneTable.Soldier, HumanHitZoneTable.VersusMaximumHealth, 1f);
        Assert.Equal((0.5f, 1f), (player.MaximumHealth, player.DamageScale));
        Assert.Equal(1f - (1.5f * 1.25f / 3f), player.RemainingFraction, 4);
    }

    private static HumanHealth Versus()
    {
        HumanHealth health = new(HumanHitZoneTable.Player, 3f) { DamageScale = HumanHealth.DifficultyDamageScale(0) };
        health.UseBody(HumanHitZoneTable.Soldier, HumanHitZoneTable.VersusMaximumHealth, 1f);
        return health;
    }

    private static int RoundsToKill(HumanHealth health, int zone)
    {
        int rounds = 0;
        while (!health.IsDead && rounds < 100)
        {
            health.Apply(zone, Rifle);
            rounds++;
        }

        return rounds;
    }
}
