using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A versus match's players take I.G.I. 2's multiplayer body and its damage for each gun, not the campaign hero's.</summary>
public sealed class VersusBodyTests
{
    // I.G.I.'s zones: a shin, a thigh, the trunk's side, its front, the head.
    private const int Shin = 1;
    private const int Thigh = 2;
    private const int Trunk = 12;
    private const int Chest = 13;
    private const int Head = 15;

    [Fact]
    public void The_campaign_hero_takes_eleven_of_its_own_rifle_rounds_and_its_head_is_no_weaker()
    {
        Assert.Equal(11, RoundsToKill(new HumanHealth(HumanHitZoneTable.Player, 3f), Chest, 0.3f));
        Assert.Equal(11, RoundsToKill(new HumanHealth(HumanHitZoneTable.Player, 3f), Head, 0.3f));
    }

    [Theory]
    [InlineData("WEAPON_ID_AK47", 3, 4, 4, 5)]
    [InlineData("WEAPON_ID_M16A2", 4, 4, 5, 5)]
    [InlineData("WEAPON_ID_MINIMI", 4, 4, 5, 5)]
    [InlineData("WEAPON_ID_MP5SD", 5, 5, 6, 7)]
    [InlineData("WEAPON_ID_UZI", 5, 6, 7, 8)]
    [InlineData("WEAPON_ID_COLT", 2, 2, 3, 3)]
    [InlineData("WEAPON_ID_DRAGUNOV", 2, 2, 3, 3)]
    public void A_versus_player_falls_to_igi2s_rounds_three_or_more_to_the_body_and_one_to_the_head(string gun, int chest, int trunk, int thigh, int shin)
    {
        float damage = Igi1VersusDamage.Of(gun, 0f);
        Assert.Equal((chest, trunk, thigh, shin, 1), (RoundsToKill(Versus(), Chest, damage), RoundsToKill(Versus(), Trunk, damage), RoundsToKill(Versus(), Thigh, damage), RoundsToKill(Versus(), Shin, damage), RoundsToKill(Versus(), Head, damage)));
    }

    [Fact]
    public void Two_shotgun_pellets_to_the_chest_kill_so_a_blast_up_close_is_one()
    {
        Assert.Equal(2, RoundsToKill(Versus(), Chest, Igi1VersusDamage.Of("WEAPON_ID_SPAS12", 0f)));
    }

    [Fact]
    public void A_gun_igi2_has_no_figure_for_keeps_its_own()
    {
        Assert.Equal(0.25f, Igi1VersusDamage.Of("WEAPON_ID_RPG18", 0.25f));
        Assert.Equal(0.1f, Igi1VersusDamage.Of(null, 0.1f));
    }

    [Fact]
    public void Taking_the_versus_body_keeps_the_share_of_health_left_and_drops_the_difficulty_scale()
    {
        HumanHealth player = new(HumanHitZoneTable.Player, 3f) { DamageScale = HumanHealth.DifficultyDamageScale(2) };
        player.Apply(Chest, 1.5f);
        player.UseBody(HumanHitZoneTable.Versus, HumanHitZoneTable.VersusMaximumHealth, 1f);
        Assert.Equal((1f, 1f), (player.MaximumHealth, player.DamageScale));
        Assert.Equal(1f - (1.5f * 1.25f / 3f), player.RemainingFraction, 4);
    }

    private static HumanHealth Versus()
    {
        HumanHealth health = new(HumanHitZoneTable.Player, 3f) { DamageScale = HumanHealth.DifficultyDamageScale(0) };
        health.UseBody(HumanHitZoneTable.Versus, HumanHitZoneTable.VersusMaximumHealth, 1f);
        return health;
    }

    private static int RoundsToKill(HumanHealth health, int zone, float damage)
    {
        int rounds = 0;
        while (!health.IsDead && rounds < 100)
        {
            health.Apply(zone, damage);
            rounds++;
        }

        return rounds;
    }
}
