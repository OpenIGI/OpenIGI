using OpenIGI.Core.Titles;
using OpenIGI.Game.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>Each game's co-op looks.</summary>
public sealed class PlayerSkinsTests
{
    [Fact]
    public void Each_game_offers_its_own_looks_and_keeps_its_own_choice()
    {
        Assert.Same(PlayerSkins.Igi1, PlayerSkins.For(GameTitle.Igi1.Id));
        Assert.Same(PlayerSkins.Igi2, PlayerSkins.For(GameTitle.Igi2.Id));
        Assert.NotSame(PlayerSkins.Igi1.Chosen, PlayerSkins.Igi2.Chosen);
    }

    [Fact]
    public void I_g_i_2_offers_the_multiplayer_bodies_by_body_and_face()
    {
        // NetManager_GetTeamModelName: "%s%d_1", body then face, for both sides' three bodies.
        foreach (string body in new[] { "jones", "igiagent", "csf", "resg", "les", "chn" })
        {
            for (int face = 1; face <= 3; face++)
            {
                Assert.Equal($"{body}{face}_1", PlayerSkins.Igi2.Of($"{body}{face}").Model);
            }
        }
    }

    [Fact]
    public void I_g_i_2_s_default_is_the_mission_s_own_jones_and_an_unknown_look_falls_back_to_it()
    {
        Assert.Equal(string.Empty, PlayerSkins.Igi2.Default.Model);
        Assert.Same(PlayerSkins.Igi2.Default, PlayerSkins.Igi2.Of("harrison"));
        Assert.Equal("015_01_1", PlayerSkins.Igi1.Of("anya").Model);
    }
}
