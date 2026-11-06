using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>I.G.I. levels played player against player: an arena of the level, two teams, kills and the dead put back.</summary>
public sealed class Igi1VersusTests
{
    [RequiresGameDataFact]
    public void A_team_deathmatch_makes_the_level_an_arena_on_both_games_and_puts_the_players_on_two_teams()
    {
        using Igi1CoopPair pair = Joined(GameModes.TeamDeathmatch);
        Assert.Equal(GameModes.TeamDeathmatch, pair.Session.Session!.Mode.Id);

        // Nobody of the level's own on either game.
        Assert.Empty(pair.Host!.World.Soldiers?.SoldierViews ?? []);
        Assert.Empty(pair.Joiner!.World.Soldiers?.SoldierViews ?? []);

        // One a side.
        Match match = pair.Session.Session.Match;
        int[] teams = [.. match.Players.Values.Select(p => p.Team).Order()];
        Assert.Equal([TeamRoster.Good, TeamRoster.Bad], teams);
    }

    [RequiresGameDataFact]
    public void A_player_killed_in_a_deathmatch_is_counted_and_put_back_by_itself_rather_than_held_down()
    {
        using Igi1CoopPair pair = Joined(GameModes.FreeForAll);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Igi1.World.IHarmedPlayer harmed = pair.Host.Harm.Players.First(p => ReferenceEquals(p.Rig, partner.Player));
        harmed.Health.Apply(0, 100000f);
        Assert.True(pair.RunUntil(() => partner.IsDead, 300) >= 0, "the partner never fell");

        MatchPlayer standing = pair.Session.Session!.Match.Players.Values.Single(p => p.Name == "Anya");
        Assert.True(pair.RunUntil(() => standing.Deaths == 1, 120) >= 0, "the death was never counted");
        Assert.True(pair.RunUntil(() => !partner.IsDead, 30 * 6) >= 0, "the partner was never put back");
        Assert.True(standing.IsAlive);
    }

    [RequiresGameDataFact]
    public void A_player_put_back_in_a_laid_out_map_starts_at_the_spawn_farthest_from_the_others()
    {
        using Igi1CoopPair pair = Joined(GameModes.FreeForAll);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Core.Numerics.Vector3d host = pair.Host.World.Player!.Body.Position;

        // Two spawns on the host's own ground: one beside it, one well away.
        double[] near = [host.X + 2048, host.Y, host.Z];
        double[] far = [host.X + 40960, host.Y, host.Z];
        MpLayout layout = new() { Title = "igi1", Map = "1", Spawns = [new MpSpawn(near), new MpSpawn(far)] };
        ((IModeHost)pair.Session.Session!).World.UseLayout(layout);

        Game.Igi1.World.IHarmedPlayer harmed = pair.Host.Harm.Players.First(p => ReferenceEquals(p.Rig, partner.Player));
        harmed.Health.Apply(0, 100000f);
        Assert.True(pair.RunUntil(() => partner.IsDead, 300) >= 0, "the partner never fell");
        Assert.True(pair.RunUntil(() => !partner.IsDead, 30 * 6) >= 0, "the partner was never put back");
        double fromFar = Math.Abs(partner.Player.Body.Position.X - far[0]) + Math.Abs(partner.Player.Body.Position.Y - far[1]);
        Assert.True(fromFar < 4096, $"put back {fromFar} units from the far spawn");
    }

    private static Igi1CoopPair Joined(string mode)
    {
        Igi1CoopPair pair = new(mode, mission: 1);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }
}
