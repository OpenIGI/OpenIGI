using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
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

        // Laid out from where they stood: spawns for both teams, and the host's own start inside play.
        MpLayout layout = Assert.IsType<MpLayout>(pair.Session.Session.Layout);
        Assert.Contains(layout.Spawns, s => s.Team == TeamRoster.Good);
        Assert.Contains(layout.Spawns, s => s.Team == TeamRoster.Bad);
        Assert.True(layout.InBounds(pair.Host.World.Player!.Body.Position));

        // One a side.
        Match match = pair.Session.Session.Match;
        int[] teams = [.. match.Players.Values.Select(p => p.Team).Order()];
        Assert.Equal([TeamRoster.Good, TeamRoster.Bad], teams);

        // Both in the same arena: not one in the base and the other at the level's far-off start.
        double apart = Core.Numerics.Vector3d.Distance(pair.Host.World.Player!.Body.Position, Assert.Single(pair.Host.World.NetworkPlayers).Player.Body.Position);
        Assert.True(apart < 300 * 4096, $"the players start {apart / 4096:0} m apart");
        Assert.True(layout.InBounds(pair.Host.World.Player.Body.Position));

        // The campaign's objectives and messages are nobody's here: neither game shows them.
        pair.RunUntil(() => false, 60);
        Assert.Empty(pair.Host.World.Objectives!.Objectives);
        Assert.Empty(pair.Host.World.Objectives.DisplayedMessages);
        Assert.Empty(pair.Joiner.World.Objectives!.Objectives);

        // On opposite sides, neither is shown the other's name: not on the host, and not on the joiner once its board has come.
        Assert.Null(Assert.Single(pair.Host.World.PartnerBodies!()).Name);
        Assert.True(pair.RunUntil(() => pair.Joiner.World.PartnerBodies!() is [{ Name: null }] && pair.Client.CurrentScoreboard is not null, 300) >= 0,
            "the joiner was shown its enemy's name");
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
        Assert.True(pair.RunUntil(() => pair.Client.KillFeed.Any(k => k.Victim == "Anya"), 120) >= 0, "the joiner's feed never listed the death");
        Assert.True(pair.RunUntil(() => !partner.IsDead, 30 * 6) >= 0, "the partner was never put back");
        Assert.True(standing.IsAlive);
    }

    [RequiresGameDataFact]
    public void A_player_put_back_in_a_laid_out_map_starts_at_the_spawn_farthest_from_the_others()
    {
        using Igi1CoopPair pair = Joined(GameModes.FreeForAll);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        // The host's feet once it has come down from the level's start, so the spawns stand on its ground.
        Core.Numerics.Vector3d host = default;
        Assert.True(pair.RunUntil(() =>
        {
            Core.Numerics.Vector3d now = pair.Host.World.Player!.Body.Position;
            bool settled = Math.Abs(now.Z - host.Z) < 1;
            host = now;
            return settled;
        }, 600) >= 0, "the host never came to rest");

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
        Assert.True(fromFar < 4096, $"put back {fromFar} units from the far spawn, at {partner.Player.Body.Position}, host {pair.Host.World.Player!.Body.Position} was {host}, deaths {pair.Session.Session!.Match.Players.Values.Sum(p => p.Deaths)}");
    }

    [RequiresGameDataFact]
    public void A_joiner_put_down_in_a_laid_out_arena_lands_on_its_feet_and_lives()
    {
        using Igi1CoopPair pair = Joined(GameModes.TeamDeathmatch);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Core.Numerics.Vector3d first = partner.Player.Body.Position;
        double lowest = first.Z;
        pair.RunUntil(() =>
        {
            lowest = Math.Min(lowest, partner.Player.Body.Position.Z);
            return partner.IsDead;
        }, 30 * 10);
        Assert.False(partner.IsDead, $"put down at {first}, fell to {lowest} ({(first.Z - lowest) / 4096:0.0} m) and died");
    }

    [RequiresGameDataFact]
    public void A_spawn_with_no_ground_under_it_is_passed_over_for_one_that_has()
    {
        using Igi1CoopPair pair = Joined(GameModes.FreeForAll);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Core.Numerics.Vector3d host = Landed(pair);

        // Mission 1 has a soldier far below its ground, where a player put would fall forever (seen live, 2026-11-06); it is
        // the farthest spawn, and passed over for the one on the host's ground.
        double[] grounded = [host.X + 20480, host.Y, host.Z];
        double[] under = [24103034, -55681832, 161166576];
        MpLayout layout = new() { Title = "igi1", Map = "1", Spawns = [new MpSpawn(grounded), new MpSpawn(under)] };
        ((IModeHost)pair.Session.Session!).World.UseLayout(layout);

        Game.Igi1.World.IHarmedPlayer harmed = pair.Host.Harm.Players.First(p => ReferenceEquals(p.Rig, partner.Player));
        harmed.Health.Apply(0, 100000f);
        Assert.True(pair.RunUntil(() => partner.IsDead, 300) >= 0, "the partner never fell");
        Assert.True(pair.RunUntil(() => !partner.IsDead, 30 * 6) >= 0, "the partner was never put back");
        Assert.True(Math.Abs(partner.Player.Body.Position.X - grounded[0]) < 4096, $"put back at {partner.Player.Body.Position}");
        pair.RunUntil(() => partner.IsDead, 90);
        Assert.False(partner.IsDead, "the partner fell to its death again");
    }

    [RequiresGameDataFact]
    public void An_elimination_round_ends_when_a_side_falls_and_everyone_stands_again_for_the_next()
    {
        using Igi1CoopPair pair = Joined(GameModes.Elimination);
        IGameMode mode = pair.Session.Session!.Mode;
        Assert.Equal(GameModes.Elimination, mode.Id);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        int hostTeam = pair.Session.Session.Match.Players[ServerSession.HostSlot].Team;

        ((IModeHost)pair.Session.Session).World.KillPlayer(0);
        Assert.True(pair.RunUntil(() => mode.Flow.Phase == MatchPhase.RoundOver, 300) >= 0, "the round never ended");
        Assert.Equal(hostTeam, mode.Flow.Winner);
        Assert.True(partner.IsDead, "the fallen came back inside the round");

        Assert.True(pair.RunUntil(() => mode.Flow.Round == 2 && !partner.IsDead, 30 * 8) >= 0, "round 2 never began with everyone standing");
        Assert.Equal(1, pair.Session.Session.Match.ScoreOf(hostTeam));
    }

    [RequiresGameDataFact]
    public void A_new_round_puts_the_level_back_on_the_server_and_the_joiner_with_no_load()
    {
        using Igi1CoopPair pair = Joined(GameModes.Elimination);
        ServerSession session = pair.Session.Session!;
        IGameMode mode = session.Mode;
        Igi1Gameplay server = pair.Host!;
        Igi1Gameplay joiner = pair.Joiner!;
        Assert.True(pair.RunUntil(() => mode.Flow.Phase == MatchPhase.Live, 30 * 30) >= 0, "the first round never began");

        // The round's play: doors opened, the level's guns taken, a gun dropped, something broken.
        PlacedDoor[] doors = [.. server.World.Doors!.Doors.Take(3)];
        Assert.NotEmpty(doors);
        foreach (PlacedDoor door in doors)
        {
            door.State.CommandOpen();
        }

        GunPickup[] guns = [.. server.World.GunPickups!.Items.Where(static g => g.Task is not null && !g.IsCollected).Take(2)];
        foreach (GunPickup gun in guns)
        {
            server.World.GunPickups.Withdraw(gun);
        }

        int gunsAtStart = server.World.GunPickups.Items.Count;
        GunPickup dropped = server.World.GunPickups.Spawn(server.World.Player!.Body.Position, Core.Numerics.Matrix3f.Identity, guns.Length > 0 ? guns[0].Weapon : server.World.WeaponTypes!.Weapons[0]);
        DestructibleObject? broken = server.World.Destructibles!.Objects.FirstOrDefault(static o => !o.IsDestroyed);
        if (broken is not null)
        {
            server.World.Destructibles.TryDestroy(broken.Owner, broken.Placement, float.MaxValue);
        }

        Assert.True(pair.RunUntil(() => doors.All(static d => d.State.IsFullyOpen), 30 * 5) >= 0, "the doors never opened");
        Assert.True(pair.RunUntil(() => joiner.World.Doors!.Doors.Take(doors.Length).All(static d => d.State.IsFullyOpen), 30 * 5) >= 0,
            "the joiner never saw the doors open");

        // The round ends; the next begins with the level as the match found it, on both games.
        ((IModeHost)session).World.KillPlayer(0);
        Assert.True(pair.RunUntil(() => mode.Flow.Round == 2 && mode.Flow.Phase == MatchPhase.Live, 30 * 20) >= 0, "round 2 never began");
        pair.RunUntil(() => false, 3);

        foreach (Igi1Gameplay game in new[] { server, joiner })
        {
            string who = ReferenceEquals(game, server) ? "server" : "joiner";
            Assert.All(game.World.Doors!.Doors.Take(doors.Length), d => Assert.True(d.State.IsFullyClosed, $"a door stayed open on the {who}"));
            Assert.DoesNotContain(dropped, game.World.GunPickups!.Items);
            if (broken is not null)
            {
                Assert.False(game.World.Destructibles!.IsDestroyed(broken.Owner), $"{broken.TypeName} stayed broken on the {who}");
            }
        }

        Assert.Equal(gunsAtStart, server.World.GunPickups.Items.Count);
        Assert.All(guns, g => Assert.False(g.IsCollected, "a gun the round took stayed taken on the server"));
        Assert.All(
            joiner.World.GunPickups!.Items.Where(g => guns.Any(t => ReferenceEquals(t.Task, g.Task) || (t.Task is not null && g.Task?.Id == t.Task.Id))),
            g => Assert.False(g.IsCollected, "a gun the round took stayed taken on the joiner"));
    }

    [RequiresGameDataFact]
    public void An_attacker_holding_use_at_a_site_plants_the_bomb_and_its_blast_wins_the_round()
    {
        using Igi1CoopPair pair = Joined(GameModes.SearchAndDestroy);
        ServerSession session = pair.Session.Session!;
        SearchAndDestroyMode mode = Assert.IsType<SearchAndDestroyMode>(session.Mode);
        Landed(pair);

        // Whoever attacks: a site under its feet, and the use key held on its own game.
        bool hostAttacks = session.Match.Players[ServerSession.HostSlot].Team == SearchAndDestroyMode.Attackers;
        Core.Numerics.Vector3d feet = hostAttacks ? pair.Host!.World.Player!.Body.Position : Assert.Single(pair.Host!.World.NetworkPlayers).Player.Body.Position;
        session.Layout = new MpLayout { Points = [new MpPoint("A", MpPointKind.BombSite, [feet.X, feet.Y, feet.Z], 5 * 4096)] };
        Igi1CoopPair.Hold(hostAttacks ? pair.HostInput : pair.JoinerInput, OpenIGI.Input.KeyCode.E, true);

        Assert.True(pair.RunUntil(() => mode.PlantedAt is not null, 30 * 6) >= 0, "the bomb was never planted");
        Igi1CoopPair.Hold(hostAttacks ? pair.HostInput : pair.JoinerInput, OpenIGI.Input.KeyCode.E, false);
        Assert.True(pair.RunUntil(() => pair.Client.Banner?.Headline?.StartsWith("BOMB PLANTED AT A", StringComparison.Ordinal) == true, 30 * 3) >= 0,
            "the joiner was never told of the plant");

        Assert.True(pair.RunUntil(() => mode.Exploded, 30 * 45) >= 0, "the bomb never went off");
        Assert.True(pair.RunUntil(() => mode.Flow.Phase == MatchPhase.RoundOver, 30) >= 0, "the round never ended");
        Assert.Equal(SearchAndDestroyMode.Attackers, mode.Flow.Winner);
    }

    [RequiresGameDataFact]
    public void A_host_killed_in_an_arena_comes_back_rather_than_failing_the_mission()
    {
        using Igi1CoopPair pair = Joined(GameModes.FreeForAll);
        ((IModeHost)pair.Session.Session!).World.KillPlayer(ServerSession.HostSlot);
        Assert.True(pair.RunUntil(() => pair.Host!.World.IsPlayerDead, 300) >= 0, "the host never fell");
        Assert.True(pair.RunUntil(() => !pair.Host!.World.IsPlayerDead, 30 * 8) >= 0, "the host was never put back");
        Assert.False(pair.HostGame.ShowsMissionFailed);
    }

    [RequiresGameDataFact]
    public void A_player_put_back_in_a_deathmatch_has_its_ammunition_filled_again()
    {
        using Igi1CoopPair pair = Joined(GameModes.FreeForAll);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Game.Player.PlayerWeapons weapons = partner.Player.Weapons!;
        (int ammo, int full) = weapons.Ammunition.Contents.First(c => c.Value > 0);
        weapons.Ammunition.Take(ammo, full);
        Assert.Equal(0, weapons.Ammunition.Count(ammo));

        Game.Igi1.World.IHarmedPlayer harmed = pair.Host.Harm.Players.First(p => ReferenceEquals(p.Rig, partner.Player));
        harmed.Health.Apply(0, 100000f);
        Assert.True(pair.RunUntil(() => partner.IsDead, 300) >= 0, "the partner never fell");
        Assert.True(pair.RunUntil(() => !partner.IsDead, 30 * 6) >= 0, "the partner was never put back");
        Assert.Equal(full, weapons.Ammunition.Count(ammo));
    }

    [RequiresGameDataFact]
    public void A_joiner_that_brought_the_level_up_for_co_op_loads_it_again_as_the_servers_arena()
    {
        using Igi1CoopPair pair = new(GameModes.FreeForAll, mission: 1, joinerMode: GameModes.Coop);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        Assert.Equal(GameModes.FreeForAll, pair.Client.Mode);
        Assert.True(pair.RunUntil(() => pair.Joiner?.World.Soldiers?.SoldierViews is { } views && !views.Any(), 3000) >= 0,
            "the joiner still has the level's soldiers");

        // And, the level loaded again, it stands where the server has it and lives.
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Core.Numerics.Vector3d first = partner.Player.Body.Position;
        double lowest = first.Z;
        pair.RunUntil(() =>
        {
            lowest = Math.Min(lowest, partner.Player.Body.Position.Z);
            return partner.IsDead;
        }, 30 * 10);
        Assert.False(partner.IsDead, $"at {first}, fell to {lowest} ({(first.Z - lowest) / 4096:0.0} m) and died");
        double apart = Core.Numerics.Vector3d.Distance(pair.Joiner!.World.Player!.Body.Position, partner.Player.Body.Position);
        Assert.True(apart < 4096, $"the joiner's own body is {apart / 4096:0.0} m from the server's");
    }

    // The host's feet once it has come down from the level's start.
    private static Core.Numerics.Vector3d Landed(Igi1CoopPair pair)
    {
        Core.Numerics.Vector3d host = default;
        Assert.True(pair.RunUntil(() =>
        {
            Core.Numerics.Vector3d now = pair.Host!.World.Player!.Body.Position;
            bool settled = Math.Abs(now.Z - host.Z) < 1;
            host = now;
            return settled;
        }, 600) >= 0, "the host never came to rest");
        return host;
    }

    [RequiresGameDataFact]
    public void With_the_levels_intro_left_to_play_both_players_in_an_arena_still_get_their_controls()
    {
        // A player's game plays cutscenes; held in an arena, mission 1's intro must still count as over, or the level never
        // hands either player its controls (seen live, 2026-11-06).
        using Igi1CoopPair pair = Joined(GameModes.SearchAndDestroy, hostSkipsIntro: false);
        pair.RunUntil(() => false, 90);
        Core.Numerics.Vector3d hostFrom = pair.Host!.World.Player!.Body.Position;
        Core.Numerics.Vector3d joinerFrom = pair.Joiner!.World.Player!.Body.Position;
        Igi1CoopPair.Hold(pair.HostInput, OpenIGI.Input.KeyCode.W, true);
        Igi1CoopPair.Hold(pair.JoinerInput, OpenIGI.Input.KeyCode.W, true);
        pair.RunUntil(() => false, 90);
        Igi1CoopPair.Hold(pair.HostInput, OpenIGI.Input.KeyCode.W, false);
        Igi1CoopPair.Hold(pair.JoinerInput, OpenIGI.Input.KeyCode.W, false);

        double hostWalked = Core.Numerics.Vector3d.Distance(hostFrom, pair.Host.World.Player.Body.Position);
        double joinerWalked = Core.Numerics.Vector3d.Distance(joinerFrom, pair.Joiner.World.Player!.Body.Position);
        Assert.True(hostWalked > 4096, $"the host walked {hostWalked / 4096:0.00} m");
        Assert.True(joinerWalked > 4096, $"the joiner walked {joinerWalked / 4096:0.00} m");
    }

    private static Igi1CoopPair Joined(string mode, bool hostSkipsIntro = true)
    {
        Igi1CoopPair pair = new(mode, mission: 1, hostSkipsIntro: hostSkipsIntro);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }
}
