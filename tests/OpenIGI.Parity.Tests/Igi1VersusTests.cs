using OpenIGI.Core.Numerics;
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
    public void A_new_round_puts_the_wounded_back_whole_and_a_first_spawn_carries_only_its_sides_knife_and_pistol()
    {
        using Igi1CoopPair pair = Joined(GameModes.SearchAndDestroy);
        Landed(pair);
        Igi1NetworkPlayer partner = Assert.Single(pair.Host!.World.NetworkPlayers);

        // A first spawn on the map: I.G.I. 2's kit, not the campaign level's, each side its own pistol.
        static string[] Kit(Game.Igi1.Player.PlayerCamera rig) => [.. rig.Weapons!.Carried.Select(c => c.Weapon.ScriptId).Order(StringComparer.Ordinal)];
        static string Pistol(int team) => team == TeamRoster.Bad ? "WEAPON_ID_COLT" : "WEAPON_ID_GLOCK";
        int hostTeam = pair.Session.Session!.Match.Players[ServerSession.HostSlot].Team;
        int joinerTeam = pair.Session.Session.Match.Players.Values.Single(p => p.Name == "Anya").Team;
        string[] hostKit = [.. new[] { "WEAPON_ID_KNIFE", Pistol(hostTeam) }.Order(StringComparer.Ordinal)];
        string[] joinerKit = [.. new[] { "WEAPON_ID_KNIFE", Pistol(joinerTeam) }.Order(StringComparer.Ordinal)];
        Assert.True(Kit(pair.Host.World.Player!).Except(["WEAPON_ID_C4BOMB"]).SequenceEqual(hostKit), $"the host carries {string.Join(", ", Kit(pair.Host.World.Player!))}");
        Assert.True(Kit(partner.Player).Except(["WEAPON_ID_C4BOMB"]).SequenceEqual(joinerKit), $"the joiner carries {string.Join(", ", Kit(partner.Player))}");
        Assert.True(pair.RunUntil(() => Kit(pair.Joiner!.World.Player!).Except(["WEAPON_ID_C4BOMB"]).SequenceEqual(joinerKit), 30 * 3) >= 0, $"the joiner's own game carries {string.Join(", ", Kit(pair.Joiner!.World.Player!))}");

        // Both wounded and living; the next round (the match begun again in place) puts them back whole, on both games.
        pair.Host.World.PlayerHealth.Apply(13, 0.5f);
        pair.Host.Harm.Players.First(p => ReferenceEquals(p.Rig, partner.Player)).Health.Apply(13, 0.5f);
        Assert.True(pair.RunUntil(() => pair.Joiner!.World.PlayerHealth.RemainingFraction < 0.9f, 30 * 3) >= 0, "the joiner never saw its wound");
        pair.Session.PlayAgain();
        Assert.True(pair.RunUntil(() => pair.Host.World.PlayerHealth.RemainingFraction >= 1f && partner.Health.RemainingFraction >= 1f, 30 * 3) >= 0, "the server kept the wounds");
        Assert.True(pair.RunUntil(() => pair.Joiner!.World.PlayerHealth.RemainingFraction >= 1f, 30 * 3) >= 0, "the joiner's own game kept its wound");

        // The sides are I.G.I. 2's.
        Assert.Equal(("IGI", "Conspiracy"), (MatchState.TeamName(TeamRoster.Good), MatchState.TeamName(TeamRoster.Bad)));
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
    public void An_attacker_with_the_bomb_in_hand_holding_use_at_a_site_plants_it_and_its_blast_wins_the_round()
    {
        using Igi1CoopPair pair = Joined(GameModes.SearchAndDestroy);
        ServerSession session = pair.Session.Session!;
        SearchAndDestroyMode mode = Assert.IsType<SearchAndDestroyMode>(session.Mode);
        Landed(pair);

        // The round's freeze time first: everyone held where they start, nothing of the round run yet.
        pair.RunUntil(() => false, FreezeTicks);

        // The carrier's charge rides on its back on the other player's window: the host's own drawing of the joiner, or the
        // joiner's of the host, from the snapshot's mark.
        Assert.True(mode.Carrier >= 0, "nobody was handed the bomb");
        Igi1Gameplay watcher = mode.Carrier == ServerSession.HostSlot ? pair.Joiner! : pair.Host!;
        Assert.True(pair.RunUntil(() => watcher.World.PartnerBodies?.Invoke().Any(static b => b.CarriesBomb) == true, 30 * 2) >= 0,
            "the carrier was never drawn with the bomb");

        // Whoever attacks: a site under its feet, and the use key held on its own game.
        bool hostAttacks = session.Match.Players[ServerSession.HostSlot].Team == SearchAndDestroyMode.Attackers;
        Core.Numerics.Vector3d feet = hostAttacks ? pair.Host!.World.Player!.Body.Position : Assert.Single(pair.Host!.World.NetworkPlayers).Player.Body.Position;
        session.Layout = new MpLayout { Points = [new MpPoint("A", MpPointKind.BombSite, [feet.X, feet.Y, feet.Z], 5 * 4096)] };
        OpenIGI.Game.Igi1.Player.PlayerCamera planter = hostAttacks ? pair.Host!.World.Player! : Assert.Single(pair.Host!.World.NetworkPlayers).Player;
        OpenIGI.Game.Igi1.Player.PlayerCamera own = hostAttacks ? pair.Host!.World.Player! : pair.Joiner!.World.Player!;
        int bomb = OpenIGI.Game.Igi1.Multiplayer.Igi1BombWeapon.Id;

        // The bomb is a weapon the carrier carries, on its own game and the server's alike -- but not yet in hand, and with
        // it out of hand the use key at a site plants nothing.
        Assert.True(pair.RunUntil(() => own.Weapons?.HasWeapon(bomb) == true && planter.Weapons?.HasWeapon(bomb) == true, 30 * 2) >= 0,
            "the carrier was never given the bomb to carry");
        Assert.False(OpenIGI.Game.Igi1.Multiplayer.Igi1BombWeapon.Holds(planter));
        Igi1CoopPair.Hold(hostAttacks ? pair.HostInput : pair.JoinerInput, OpenIGI.Input.KeyCode.E, true);
        pair.RunUntil(() => false, (int)(30 * new BombSettings().PlantSeconds) + 15);
        Assert.Null(mode.PlantedAt);
        Igi1CoopPair.Hold(hostAttacks ? pair.HostInput : pair.JoinerInput, OpenIGI.Input.KeyCode.E, false);

        // Picked off the weapon wheel on the carrier's own game, it comes up in hand there and -- the pick carried in its
        // commands -- on the server; then the use key held plants it.
        own.ChooseFromWheel(bomb.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(pair.RunUntil(() => OpenIGI.Game.Igi1.Multiplayer.Igi1BombWeapon.Holds(own) && OpenIGI.Game.Igi1.Multiplayer.Igi1BombWeapon.Holds(planter), 30 * 3) >= 0,
            "the bomb never came up in hand");
        Igi1CoopPair.Hold(hostAttacks ? pair.HostInput : pair.JoinerInput, OpenIGI.Input.KeyCode.E, true);

        // The planter kneels over it -- the bomb's plant clip, on the server's body of it -- and stands once it is down.
        bool knelt = false;
        Assert.True(pair.RunUntil(() =>
        {
            knelt |= planter.ModeAnimation == OpenIGI.Game.Igi1.Multiplayer.Igi1BombAnimations.Plant && planter.BodyClip is not null;
            return mode.PlantedAt is not null;
        }, 30 * 6) >= 0, "the bomb was never planted");
        Assert.True(knelt, "the planter never played the plant");
        pair.RunUntil(() => false, 2);
        Assert.Equal(-1, planter.ModeAnimation);
        Assert.True(pair.RunUntil(() => watcher.World.PartnerBodies?.Invoke().Any(static b => b.CarriesBomb) == false, 30 * 2) >= 0,
            "the planter still carried the bomb once it was down");
        Assert.True(pair.RunUntil(() => own.Weapons?.HasWeapon(bomb) == false && planter.Weapons?.HasWeapon(bomb) == false, 30 * 2) >= 0,
            "the bomb stayed in the planter's weapons once it was down");
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

    // A buying mode's round opens on its freeze time, the players held where they start: in ticks, and a few over.
    private static readonly int FreezeTicks = (int)(30 * new RoundSettings().FreezeSeconds) + 15;

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
        pair.RunUntil(() => false, 90 + FreezeTicks);
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

    [RequiresGameDataFact]
    public void A_joiner_dropping_its_gun_throws_it_as_igi2_does_and_both_games_fly_it_to_the_same_rest()
    {
        using Igi1CoopPair pair = Joined(GameModes.TeamDeathmatch);
        Landed(pair);
        Igi1NetworkPlayer joiner = Assert.Single(pair.Host!.World.NetworkPlayers);
        OpenIGI.Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;
        Assert.True(pair.RunUntil(() => own.Weapons?.Carried.Any(c => OpenIGI.Game.Igi1.Player.PlayerCamera.IsDroppable(c.Weapon)) == true, 30 * 3) >= 0,
            "the joiner was never given a gun");
        own.ChooseFromWheel(own.Weapons!.Carried.First(c => OpenIGI.Game.Igi1.Player.PlayerCamera.IsDroppable(c.Weapon)).Weapon.WeaponId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(pair.RunUntil(() => own.Weapons?.InHand is { } held && OpenIGI.Game.Igi1.Player.PlayerCamera.IsDroppable(held.Weapon), 30 * 3) >= 0,
            $"the joiner never had a gun in hand: {own.Weapons?.InHand?.Weapon.ScriptId} {own.Weapons?.InHand?.Weapon.Category}, carries {string.Join(",", own.Weapons?.Carried.Select(c => c.Weapon.ScriptId + ":" + c.Weapon.Category) ?? [])}");
        int gun = own.Weapons!.InHand!.Value.Weapon.WeaponId;
        GunPickupRegistry served = pair.Host.World.GunPickups!;
        GunPickupRegistry shown = pair.Joiner.World.GunPickups!;
        int before = served.Items.Count;

        // The drop key, pressed and let go: the gun leaves the joiner's hand on both games, and the server throws it.
        Igi1CoopPair.Hold(pair.JoinerInput, OpenIGI.Input.KeyCode.X, true);
        pair.RunUntil(() => false, 3);
        Igi1CoopPair.Hold(pair.JoinerInput, OpenIGI.Input.KeyCode.X, false);
        Assert.True(pair.RunUntil(() => served.Items.Count > before, 30) >= 0, "the server never threw the gun");
        GunPickup thrown = served.Items[^1];
        Assert.Equal(gun, thrown.Weapon.WeaponId);
        Assert.NotNull(thrown.Launch);
        Vector3d speed = thrown.Launch!.Value.Velocity;
        Assert.Equal(614.4, Math.Sqrt((speed.X * speed.X) + (speed.Y * speed.Y)), 1);
        Assert.True(speed.Z > 270, $"it was thrown {speed.Z:0} up a tick, not I.G.I. 2's 273");
        Assert.True(pair.RunUntil(() => !joiner.Player.Weapons!.HasWeapon(gun) && !own.Weapons.HasWeapon(gun), 30) >= 0,
            "the gun stayed in the joiner's weapons");

        // The joiner's game flies the throw itself -- in the air there, not laid at once -- and both come to rest together.
        Assert.True(pair.RunUntil(() => shown.Items.Count > before, 30) >= 0, "the joiner's game never saw the throw");
        GunPickup flown = shown.Items[before];
        Assert.True(flown.IsMoving, "the joiner's game laid the gun down instead of flying it");
        Assert.True(pair.RunUntil(() => !thrown.IsMoving && !flown.IsMoving, 30 * 6) >= 0, "the gun never came to rest");
        pair.RunUntil(() => false, 30);
        Assert.True(Vector3d.Distance(thrown.Position, flown.Position) < 64, $"the games laid it {Vector3d.Distance(thrown.Position, flown.Position):0} units apart");

        // Standing by it, the one who threw it does not take it straight back.
        Assert.False(thrown.IsCollected);
        Assert.False(joiner.Player.Weapons!.HasWeapon(gun));

        // What the game says of a pickup still shows in the arena, on both games, with the campaign's own messages kept off.
        foreach (Igi1Gameplay game in new[] { pair.Host, pair.Joiner })
        {
            Assert.True(game.World.Objectives!.Silenced, "the arena showed the campaign's messages");
            Assert.True(game.World.Objectives.ShowTransientMessage("PICKED UP A GUN"));
            Assert.Contains(game.World.Objectives.DisplayedMessages, m => m.Text == "PICKED UP A GUN");
        }
    }

    [RequiresGameDataFact]
    public void A_carrier_dropping_the_bomb_throws_it_as_a_gun_is_thrown_and_only_takes_it_back_once_away()
    {
        using Igi1CoopPair pair = Joined(GameModes.SearchAndDestroy);
        SearchAndDestroyMode mode = Assert.IsType<SearchAndDestroyMode>(pair.Session.Session!.Mode);
        Landed(pair);
        pair.RunUntil(() => false, FreezeTicks);
        Assert.True(mode.Carrier >= 0, "nobody was handed the bomb");
        bool hostCarries = mode.Carrier == ServerSession.HostSlot;
        OpenIGI.Game.Igi1.Player.PlayerCamera carrier = hostCarries ? pair.Host!.World.Player! : Assert.Single(pair.Host!.World.NetworkPlayers).Player;
        OpenIGI.Game.Igi1.Player.PlayerCamera own = hostCarries ? pair.Host!.World.Player! : pair.Joiner!.World.Player!;
        Input.InputState keys = hostCarries ? pair.HostInput : pair.JoinerInput;
        int bomb = Igi1BombWeapon.Id;

        // Out of hand, the drop key drops whatever gun is held instead: the bomb stays.
        Assert.True(pair.RunUntil(() => own.Weapons?.HasWeapon(bomb) == true, 30 * 2) >= 0, "the carrier was never given the bomb");
        own.ChooseFromWheel(bomb.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(pair.RunUntil(() => Igi1BombWeapon.Holds(own) && Igi1BombWeapon.Holds(carrier), 30 * 3) >= 0, "the bomb never came up in hand");

        Igi1CoopPair.Hold(keys, OpenIGI.Input.KeyCode.X, true);
        pair.RunUntil(() => false, 3);
        Igi1CoopPair.Hold(keys, OpenIGI.Input.KeyCode.X, false);
        Assert.True(pair.RunUntil(() => mode.DroppedAt is not null, 30) >= 0, "the bomb was never dropped");
        Assert.Equal(-1, mode.Carrier);
        Vector3d stood = carrier.Body.Position;
        Assert.True(mode.BombFlies, "the bomb was laid down, not thrown");

        // Thrown as a dropped gun is: both games fly it, and it comes to rest well ahead of the carrier.
        GunPickup Bomb(Igi1Gameplay game) => Assert.Single(game.World.GunPickups!.Items, p => p.Weapon.WeaponId == bomb && !p.IsCollected);
        Igi1Gameplay other = hostCarries ? pair.Joiner! : pair.Host!;
        Assert.True(pair.RunUntil(() => other.World.GunPickups!.Items.Any(p => p.Weapon.WeaponId == bomb && !p.IsCollected), 30) >= 0,
            "the other game never saw the bomb thrown");
        Assert.True(Bomb(pair.Host!).IsMoving || Bomb(pair.Joiner!).IsMoving, "the bomb never flew");
        Assert.True(pair.RunUntil(() => !Bomb(pair.Host!).IsMoving && !Bomb(pair.Joiner!).IsMoving, 30 * 6) >= 0, "the bomb never came to rest");
        pair.RunUntil(() => false, 30);
        Assert.True(Vector3d.Distance(Bomb(pair.Host!).Position, Bomb(pair.Joiner!).Position) < 64, "the games laid the bomb apart");
        Assert.True(Vector3d.Distance(mode.DroppedAt!.Value, Bomb(pair.Host!).Position) < 1, "the mode lost track of the thrown bomb");
        Assert.True(Vector3d.Distance(mode.DroppedAt!.Value, stood) > 0.5 * 4096, "the bomb landed at the carrier's feet");
        Assert.True(pair.RunUntil(() => !own.Weapons!.HasWeapon(bomb) && !carrier.Weapons!.HasWeapon(bomb), 30 * 2) >= 0,
            "the bomb stayed in the carrier's weapons");

        // Standing over it, the one who dropped it does not take it straight back up.
        pair.RunUntil(() => false, 30);
        Assert.Equal(-1, mode.Carrier);
    }

    [RequiresGameDataFact]
    public void A_match_switched_to_search_and_destroy_in_place_is_search_and_destroy_on_the_joiner_with_its_bomb_sites()
    {
        using Igi1CoopPair pair = Joined(GameModes.TeamDeathmatch);
        Landed(pair);
        Assert.Equal(GameModes.TeamDeathmatch, pair.Client.Mode);

        // The same map under another mode, as an admin's "map 1 sd" or the vote asks: it starts over where it stands.
        Assert.True(pair.Session.UseMode(GameModes.SearchAndDestroy));
        pair.Session.PlayAgain();
        Assert.True(pair.RunUntil(() => pair.Session.Mode == GameModes.SearchAndDestroy, 30 * 3) >= 0, "the server never played search and destroy");

        // The joiner plays it too -- the mode its bomb sites are drawn for -- and has the sites to draw.
        Assert.True(pair.RunUntil(() => pair.Client.Mode == GameModes.SearchAndDestroy, 30 * 5) >= 0, $"the joiner still plays {pair.Client.Mode}");
        Assert.Contains(pair.Client.Layout!.Points, p => p.Kind == MpPointKind.BombSite);
    }

    [RequiresGameDataFact]
    public void In_the_buy_time_the_host_and_the_joiner_each_buy_a_gun_and_carry_it()
    {
        using Igi1CoopPair pair = Joined(GameModes.SearchAndDestroy);
        static bool Carries(Game.Igi1.Player.PlayerCamera rig, string weapon) =>
            rig.Weapons?.Carried.Any(c => c.Weapon.ScriptId == weapon) == true;

        pair.Session.Say("/buy uzi");
        pair.Client.Say("/buy uzi");
        Igi1NetworkPlayer joiner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Assert.True(pair.RunUntil(() => Carries(joiner.Player, "WEAPON_ID_UZI"), 300) >= 0, "the joiner's Uzi never came");
        Assert.True(Carries(pair.Host.World.Player!, "WEAPON_ID_UZI"), "the host's Uzi never came");
        Assert.True(pair.RunUntil(() => Carries(pair.Joiner!.World.Player!, "WEAPON_ID_UZI"), 300) >= 0, "the joiner's own game never got its Uzi");

        Match match = pair.Session.Session!.Match;
        Assert.All(match.Players.Values, p => Assert.Equal(match.Rules.MoneyStart - 900, p.Money));
    }

    [RequiresGameDataFact]
    public void A_player_killed_after_buying_starts_the_next_round_with_the_spawn_kit_and_the_survivor_keeps_its_gun()
    {
        using Igi1CoopPair pair = Joined(GameModes.SearchAndDestroy);
        static bool Carries(Game.Igi1.Player.PlayerCamera rig, string weapon) =>
            rig.Weapons?.Carried.Any(c => c.Weapon.ScriptId == weapon) == true;
        static string[] Kit(Game.Igi1.Player.PlayerCamera rig) =>
            [.. (rig.Weapons?.Carried ?? []).Select(c => c.Weapon.ScriptId).Order(StringComparer.Ordinal)];

        pair.Session.Say("/buy uzi");
        pair.Client.Say("/buy uzi");
        Igi1NetworkPlayer joiner = Assert.Single(pair.Host!.World.NetworkPlayers);
        Assert.True(pair.RunUntil(() => Carries(joiner.Player, "WEAPON_ID_UZI") && Carries(pair.Joiner!.World.Player!, "WEAPON_ID_UZI"), 300) >= 0, "the Uzis never came");
        Assert.True(Carries(pair.Host.World.Player!, "WEAPON_ID_UZI"), "the host's Uzi never came");

        // The joiner falls, which ends the round; the next one stands everyone again.
        Match match = pair.Session.Session!.Match;
        ((IModeHost)pair.Session.Session).World.KillPlayer(0);
        Assert.True(pair.RunUntil(() => match.Players[0] is { IsAlive: false }, 120) >= 0, "the joiner never fell");
        Assert.True(pair.RunUntil(() => match.Players[0].IsAlive && !pair.Joiner!.World.IsPlayerDead, 3000) >= 0, "the next round never stood the joiner up");
        pair.RunUntil(() => false, 60);

        Assert.False(Carries(joiner.Player, "WEAPON_ID_UZI"), "the dead joiner kept its Uzi on the server");
        Assert.False(Carries(pair.Joiner!.World.Player!, "WEAPON_ID_UZI"), "the dead joiner kept its Uzi in its own game");
        Assert.NotEmpty(Kit(joiner.Player));
        Assert.Equal(Kit(joiner.Player), Kit(pair.Joiner.World.Player!));
        Assert.True(Carries(pair.Host.World.Player!, "WEAPON_ID_UZI"), "the surviving host lost its Uzi");
    }

    [RequiresGameDataFact]
    public void A_joiner_on_a_slow_line_hits_a_host_running_across_where_its_own_game_shows_the_host()
    {
        using Igi1CoopPair pair = Joined(GameModes.TeamDeathmatch);
        pair.Ping(0.2);
        pair.RunUntil(() => false, 60);
        Game.Igi1.Player.PlayerCamera host = pair.Host!.World.Player!;
        Igi1NetworkPlayer joiner = Assert.Single(pair.Host.World.NetworkPlayers);
        Game.Igi1.Player.PlayerCamera own = pair.Joiner!.World.Player!;

        // The joiner stands; the host runs three metres either way across its front, five metres off, where no wall is between.
        (Vector3d Feet, Vector3d Ahead)? lane = new[] { joiner.Player.Body.Position, host.Body.Position, pair.Host.PlayerSetup!.LevelSpawn()!.Value.Position }
            .Select(f => pair.ClearAhead(f, 3.5) is { } way ? (f, way) : ((Vector3d, Vector3d)?)null)
            .FirstOrDefault(l => l is not null);
        Assert.True(lane is not null, "no clear lane to shoot along in the arena");
        (Vector3d feet, Vector3d ahead) = lane.Value;
        Vector3d side = new(ahead.Y, -ahead.X, 0);
        Vector3d middle = feet + (ahead * 5 * 4096);
        int tick = 0;
        void Run() => host.Body.Position = middle + (side * 3 * 4096 * Math.Sin(tick++ * 2 * Math.PI / 120));
        own.Body.Position = joiner.Player.Body.Position = feet;
        pair.RunUntil(() => { Run(); return false; }, 40);

        // A gun in hand, picked with the number keys.
        foreach (OpenIGI.Input.KeyCode category in new[] { OpenIGI.Input.KeyCode.D2, OpenIGI.Input.KeyCode.D3, OpenIGI.Input.KeyCode.D4, OpenIGI.Input.KeyCode.D5 })
        {
            if (joiner.Player.Weapons?.InHand?.Weapon.Category is Game.Weapons.WeaponCategory.Pistol or Game.Weapons.WeaponCategory.Gun)
            {
                break;
            }

            Igi1CoopPair.Hold(pair.JoinerInput, category, true);
            pair.RunUntil(() => { Run(); return false; }, 3);
            Igi1CoopPair.Hold(pair.JoinerInput, category, false);
            pair.RunUntil(() => { Run(); return false; }, 45);
        }

        Assert.True(joiner.Player.Weapons?.InHand?.Weapon.Category is Game.Weapons.WeaponCategory.Pistol or Game.Weapons.WeaponCategory.Gun, "the joiner holds no gun");

        // Aimed at the chest where the joiner's own game draws the host, clicked; until the host falls or ten rounds are out.
        int before = joiner.Player.Weapons!.ShotSequence;
        float damage = pair.Host.World.PlayerHealth.Damage;
        int landed = 0;
        int frame = 0;
        pair.RunUntil(
            () =>
            {
                Run();
                if (pair.Joiner.World.ShownPartners.FirstOrDefault() is { } shown)
                {
                    Vector3d toward = shown.Player.Body.Position + new Vector3d(0, 0, 1.3 * 4096) - own.Body.Eye;
                    own.Body.Yaw = (float)Math.Atan2(toward.X, toward.Y);
                    own.Body.Pitch = (float)Math.Atan2(toward.Z, Math.Sqrt((toward.X * toward.X) + (toward.Y * toward.Y)));
                }

                pair.JoinerInput.Mouse.SetButton(OpenIGI.Input.MouseButton.Left, frame++ % 6 < 2);
                if (pair.Host.World.PlayerHealth.Damage != damage)
                {
                    landed++;
                    damage = pair.Host.World.PlayerHealth.Damage;
                }

                return pair.Host.World.IsPlayerDead || joiner.Player.Weapons.ShotSequence - before >= 10;
            },
            900);
        pair.JoinerInput.Mouse.SetButton(OpenIGI.Input.MouseButton.Left, false);

        int rounds = joiner.Player.Weapons.ShotSequence - before;
        Assert.True(rounds > 0, "the joiner never fired");
        Assert.True(rounds - landed <= 1, $"the joiner's rounds passed by a host it drew running: {landed} of {rounds} landed");
    }

    [RequiresGameDataFact]
    public void A_hosted_match_is_saved_as_a_demo_that_plays_back_with_both_players_in_it()
    {
        using Igi1CoopPair pair = Joined(GameModes.TeamDeathmatch);
        pair.RunUntil(() => false, 150);
        string path = Path.Combine(Path.GetTempPath(), $"openigi-host-{Guid.NewGuid():N}.oidemo");
        try
        {
            Assert.Null(pair.Session.SaveDemo(path));
            DemoHeader header;
            IReadOnlyList<DemoMessage> messages;
            using (FileStream file = File.OpenRead(path))
            {
                (header, messages) = DemoFile.Read(file);
            }

            Assert.Equal(("igi1", GameModes.TeamDeathmatch), (header.Title, header.Mode));
            Assert.True(messages.Count > 30, $"only {messages.Count} messages were recorded");

            // Played back to an ordinary client: welcomed as nobody, and sent the host and the joiner as they played.
            double now = 0;
            using OpenIGI.Host.Multiplayer.DemoPlayback playback = new(header, messages, () => now);
            ClientSession viewer = new(new OpenIGI.Net.NetClient(playback, MultiplayerProtocol.IdFor("igi1"), MultiplayerProtocol.Version), 64);
            viewer.Connect(playback.Server, new JoinRequest("viewer", "test", string.Empty), now);
            for (; now < 4; now += 1 / 30.0)
            {
                viewer.Receive(now);
                viewer.Flush(now);
            }

            Assert.Equal(OpenIGI.Host.Multiplayer.DemoPlayback.GhostEntity, viewer.EntityId);
            int joiner = Assert.Single(pair.Host!.World.NetworkPlayers).Entity;
            Assert.True(viewer.Latest is { } seen && seen.TryGet(ServerSession.HostEntity, out _) && seen.TryGet(joiner, out _), "the demo did not carry both players");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Igi1CoopPair Joined(string mode, bool hostSkipsIntro = true)
    {
        Igi1CoopPair pair = new(mode, mission: 1, hostSkipsIntro: hostSkipsIntro);
        Assert.True(pair.RunUntil(() => pair.Joined && pair.Host!.World.NetworkPlayers.Count == 1, 3000) >= 0, "the joiner was never welcomed");
        return pair;
    }
}
