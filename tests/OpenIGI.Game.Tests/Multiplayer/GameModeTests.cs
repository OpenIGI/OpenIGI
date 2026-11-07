using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The modes' rules on their own: who scores, who comes back, and when a match is decided.</summary>
public sealed class GameModeTests
{
    private const int TickRate = 30;

    [Fact]
    public void A_free_for_all_kill_scores_the_killer_and_the_first_to_the_limit_wins()
    {
        Host host = new(teams: [0, 0, 0]);
        DeathmatchMode mode = new(TickRate, teams: false, new DeathmatchSettings { ScoreLimit = 3, IntermissionSeconds = 5 });
        mode.Start(host, 0);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);

        long tick = 1;
        for (int kill = 0; kill < 3; kill++, tick += TickRate * 5)
        {
            mode.Tick(host, tick, [new WorldDeath(Victim: 11, Killer: 10)]);
        }

        // Same team in the world, but nobody's team mate here: three kills, no fines.
        Assert.Equal(3, host.Match.Players[0].Kills);
        Assert.Equal(MatchPhase.Intermission, mode.Flow.Phase);
        Assert.Equal(0, mode.Flow.Winner);
    }

    [Fact]
    public void The_dead_come_back_after_the_delay_and_not_before()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { RespawnSeconds = 3 });
        mode.Start(host, 0);
        host.Respawned.Clear();
        mode.Tick(host, TickRate, [new WorldDeath(Victim: 11, Killer: 10)]);
        mode.Tick(host, TickRate * 3, []);
        Assert.Empty(host.Respawned);
        mode.Tick(host, TickRate * 4, []);
        Assert.Equal([1], host.Respawned);
    }

    [Fact]
    public void A_deathmatch_loadout_is_free_handed_over_just_after_a_spawn_and_later_kept_for_the_next()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { RespawnSeconds = 3, LoadoutSeconds = 10 });
        mode.Start(host, 0);

        // Just spawned: the Uzi now, for nothing.
        Assert.Equal("Equipped Uzi.", mode.Buy(host, 0, "uzi", TickRate * 2));
        Assert.Equal([(0, "WEAPON_ID_UZI", 320)], host.Given);
        Assert.Equal(host.Match.Rules.MoneyStart, host.Match.Players[0].Money);

        // Long after: kept for the next spawn, not handed over in the fight; the next spawn brings it.
        Assert.EndsWith("from your next spawn.", mode.Buy(host, 1, "uzi", TickRate * 30), StringComparison.Ordinal);
        Assert.Single(host.Given);
        mode.Tick(host, TickRate * 31, [new WorldDeath(Victim: 11, Killer: 10)]);
        mode.Tick(host, TickRate * 35, []);
        Assert.Equal((1, "WEAPON_ID_UZI", 320), host.Given[^1]);
    }

    [Fact]
    public void A_team_scores_its_kills_of_the_other_and_is_fined_for_its_own()
    {
        Host host = new(teams: [0, 0, 1]);
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { ScoreLimit = 0 });
        mode.Start(host, 0);
        mode.Tick(host, 1, [new WorldDeath(Victim: 12, Killer: 10), new WorldDeath(Victim: 12, Killer: 11)]);
        mode.Tick(host, 2, [new WorldDeath(Victim: 11, Killer: 10)]);
        Assert.Equal(1, host.Match.ScoreOf(0));
        Assert.Equal(0, host.Match.ScoreOf(1));
    }

    [Fact]
    public void The_clock_decides_for_the_leader_and_a_level_score_is_a_draw()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { ScoreLimit = 0, TimeLimitMinutes = 1 });
        mode.Start(host, 0);
        mode.Tick(host, TickRate * 60, []);
        Assert.Equal(MatchPhase.Intermission, mode.Flow.Phase);
        Assert.Equal(-1, mode.Flow.Winner);
    }

    [Fact]
    public void Warmup_scores_nothing_and_the_match_begins_from_nothing_with_everyone_put_back()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: false, new DeathmatchSettings { WarmupSeconds = 10 });
        mode.Start(host, 0);
        Assert.Equal(MatchPhase.Warmup, mode.Flow.Phase);
        mode.Tick(host, 5, [new WorldDeath(Victim: 11, Killer: 10)]);
        host.Respawned.Clear();
        mode.Tick(host, TickRate * 10, []);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);
        Assert.Equal(0, host.Match.Players[0].Kills);
        Assert.Equal([0, 1], host.Respawned.Order());
    }

    [Fact]
    public void After_the_intermission_the_same_map_is_played_again_without_a_rotation()
    {
        Host host = new(teams: [0, 1]);
        DeathmatchMode mode = new(TickRate, teams: false, new DeathmatchSettings { ScoreLimit = 1, IntermissionSeconds = 5 });
        mode.Start(host, 0);
        mode.Tick(host, 1, [new WorldDeath(Victim: 11, Killer: 10)]);
        Assert.Equal(MatchPhase.Intermission, mode.Flow.Phase);
        mode.Tick(host, 1 + (TickRate * 5), []);
        Assert.Equal(1, mode.MatchesPlayed);
        Assert.False(mode.IsMapOver);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);
        Assert.Equal(0, host.Match.Players[0].Kills);
    }

    [Fact]
    public void Co_op_keeps_the_dead_down_until_the_world_says_they_were_revived()
    {
        Host host = new(teams: [0, 0]) { HoldsTheDead = true };
        IGameMode mode = GameModes.For(GameModes.FreeForAll, host.World, TickRate);
        Assert.IsType<CoopMode>(mode);
        mode.Start(host, 0);
        mode.Tick(host, 1, [new WorldDeath(Victim: 11, Killer: -1)]);
        mode.Tick(host, TickRate * 60, []);
        Assert.False(host.Match.Players[1].IsAlive);
        host.Revive(1);
        mode.Tick(host, (TickRate * 60) + 1, []);
        Assert.True(host.Match.Players[1].IsAlive);
        Assert.Empty(host.Respawned);
    }

    [Fact]
    public void A_newcomer_to_team_deathmatch_joins_the_smaller_team_and_free_for_all_has_none()
    {
        Host host = new(teams: [0, 0, 1]);
        Assert.Equal(1, new DeathmatchMode(TickRate, teams: true).TeamFor(host, 7));
        Assert.Equal(0, new DeathmatchMode(TickRate, teams: false).TeamFor(host, 7));
    }

    [Fact]
    public void A_player_out_of_bounds_dies_when_its_grace_runs_out_and_one_who_comes_back_is_spared()
    {
        Host host = new(teams: [0, 1])
        {
            Layout = new MpLayout { OutOfBoundsSeconds = 2, Bounds = [new MpBox([0, 0, 0], [100, 100, 100])], Spawns = [new MpSpawn([1, 1, 1])] },
        };
        DeathmatchMode mode = new(TickRate, teams: true, new DeathmatchSettings { ScoreLimit = 0 });
        mode.Start(host, 0);
        host.Positions[0] = new(500, 50, 50);
        host.Positions[1] = new(500, 50, 50);
        mode.Tick(host, 1, []);
        host.Positions[1] = new(50, 50, 50);
        mode.Tick(host, TickRate, []);
        mode.Tick(host, 1 + (TickRate * 2), []);
        Assert.Equal([0], host.Killed);
    }

    [Fact]
    public void The_match_state_reads_back_as_it_was_written()
    {
        MatchFlow flow = new(TickRate);
        flow.Enter(MatchPhase.RoundOver, 100, 7.5, winner: 1);
        OpenIGI.Net.BitWriter writer = new();
        flow.Write(writer, GameModes.TeamDeathmatch, 130);
        OpenIGI.Net.BitReader reader = new(writer.AsSpan());
        MatchState read = MatchFlow.Read(ref reader);
        Assert.Equal(new MatchState(GameModes.TeamDeathmatch, MatchPhase.RoundOver, 1, 1, 6.5), read);
    }

    [Fact]
    public void Names_are_shown_over_team_mates_only_and_revive_marks_only_in_co_op()
    {
        Assert.True(PlayerSides.ShowsName(GameModes.Coop, null, null));
        Assert.True(PlayerSides.ShowsName(GameModes.TeamDeathmatch, 1, 1));
        Assert.False(PlayerSides.ShowsName(GameModes.TeamDeathmatch, 1, 2));
        Assert.False(PlayerSides.ShowsName(GameModes.TeamDeathmatch, null, 1));
        Assert.False(PlayerSides.ShowsName(GameModes.FreeForAll, 1, 1));
        Assert.True(PlayerSides.Revivable(GameModes.Coop));
        Assert.False(PlayerSides.Revivable(GameModes.FreeForAll));
    }

    [Fact]
    public void The_kill_feed_keeps_the_last_few_for_a_few_seconds_and_reads_back_as_written()
    {
        KillFeed feed = new();
        for (int i = 0; i < KillFeed.Kept + 2; i++)
        {
            feed.Add(new KillNote($"k{i}", 1, "v", 2), i);
        }

        Assert.Equal(KillFeed.Kept, feed.Shown(KillFeed.Kept + 1).Count);
        Assert.Equal("k6", Assert.Single(feed.Shown(6 + KillFeed.ShownSeconds - 0.5)).Killer);

        OpenIGI.Net.BitWriter writer = new();
        KillNote note = new("Anya", TeamRoster.Good, "Joe", -1);
        note.Write(writer);
        OpenIGI.Net.BitReader reader = new(writer.AsSpan());
        Assert.Equal(note, KillNote.Read(ref reader));
    }

    [Fact]
    public void An_elimination_round_goes_to_the_team_left_standing_and_the_dead_wait_for_the_next()
    {
        Host host = new(teams: [0, 0, 1]);
        EliminationMode mode = new(TickRate, new RoundSettings { RoundsToWin = 2, RoundOverSeconds = 1 });
        mode.Start(host, 0);
        Assert.Equal([0, 1, 2], host.Respawned);
        host.Respawned.Clear();

        // One of team 0 falls: no respawn, the round plays on.
        mode.Tick(host, 10, [new WorldDeath(10, 12)]);
        mode.Tick(host, 10 + (TickRate * 10), []);
        Assert.Empty(host.Respawned);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);

        // Team 1's only player falls: round to team 0, everyone back for round 2 after the pause.
        mode.Tick(host, 400, [new WorldDeath(12, 11)]);
        Assert.Equal(MatchPhase.RoundOver, mode.Flow.Phase);
        Assert.Equal(0, mode.Flow.Winner);
        Assert.Equal(1, host.Match.ScoreOf(0));
        mode.Tick(host, 400 + TickRate, []);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);
        Assert.Equal(2, mode.Flow.Round);
        Assert.Equal([0, 1, 2], host.Respawned);

        // Round 2 to the clock, two of team 0 standing to one: theirs, and with it the match after the pause.
        mode.Tick(host, 400 + TickRate + (TickRate * 120), []);
        Assert.Equal(MatchPhase.RoundOver, mode.Flow.Phase);
        Assert.Equal(2, host.Match.ScoreOf(0));
        mode.Tick(host, 400 + (TickRate * 122) + 1, []);
        Assert.Equal(MatchPhase.Intermission, mode.Flow.Phase);
        Assert.Equal(0, mode.Flow.Winner);
    }

    [Fact]
    public void A_side_asked_for_in_the_middle_of_a_round_is_taken_when_the_next_begins()
    {
        Host host = new(teams: [0, 0, 1]);
        EliminationMode mode = new(TickRate, new RoundSettings { RoundsToWin = 3, RoundOverSeconds = 1 });
        mode.Start(host, 0);
        host.Match.Players[0].NextTeam = 1;

        // Team 1 falls: the round ends with the mover still on team 0, and the next puts it on team 1.
        mode.Tick(host, 400, [new WorldDeath(12, 10)]);
        Assert.Equal(0, host.Match.Players[0].Team);
        mode.Tick(host, 400 + TickRate, []);
        Assert.Equal(2, mode.Flow.Round);
        Assert.Equal(1, host.Match.Players[0].Team);
        Assert.Null(host.Match.Players[0].NextTeam);
    }

    [Fact]
    public void A_round_with_one_team_empty_is_won_by_nobody_and_runs_out_as_a_draw_into_the_next()
    {
        Host host = new(teams: [0]);
        SearchAndDestroyMode mode = new(TickRate, new RoundSettings { FreezeSeconds = 0, RoundSeconds = 2, RoundOverSeconds = 1 });
        mode.Start(host, 0);

        // Nobody to beat: the round stays live through its time...
        long tick = Run(mode, host, 1, TickRate);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);

        // ...and when it runs out is a draw scoring nothing, not a clock held at nought; the next round begins after it.
        tick = Run(mode, host, tick, TickRate + 1);
        Assert.Equal((MatchPhase.RoundOver, -1, RoundEnd.TimeRanOut), (mode.Flow.Phase, mode.Flow.Winner, mode.Report(tick).Reason));
        Assert.Equal((0, 0), (host.Match.ScoreOf(TeamRoster.Good), host.Match.ScoreOf(TeamRoster.Bad)));
        Run(mode, host, tick, TickRate + 1);
        Assert.Equal((MatchPhase.Live, 1), (mode.Flow.Phase, mode.RoundsPlayed));
    }

    [Fact]
    public void At_the_half_the_teams_change_sides_and_take_their_scores_with_them()
    {
        Host host = new(teams: [0, 1]) { Layout = new MpLayout { Points = [new MpPoint("A", MpPointKind.BombSite, [100 * Metre, 0, 0], 5 * Metre)] } };
        SearchAndDestroyMode mode = new(TickRate, new RoundSettings { FreezeSeconds = 0, RoundsToWin = 3, RoundSeconds = 1, RoundOverSeconds = 1 });
        mode.Start(host, 0);

        // Two rounds run out on the clock, both the defenders'; the third begins with the sides changed.
        long tick = Run(mode, host, 1, (TickRate * 4) + 4);
        Assert.Equal((2, MatchPhase.Live), (mode.RoundsPlayed, mode.Flow.Phase));
        Assert.Equal((TeamRoster.Bad, TeamRoster.Good), (host.Match.Players[0].Team, host.Match.Players[1].Team));
        Assert.Equal((2, 0), (host.Match.ScoreOf(TeamRoster.Good), host.Match.ScoreOf(TeamRoster.Bad)));
        Assert.True(tick > 0);
    }

    private const double Metre = OpenIGI.Core.Numerics.WorldUnits.PerMetre;

    // Attackers 0 (slot 0), defenders 1 (slot 1); site A at 100 m, B at 300 m.
    private static (Host Host, SearchAndDestroyMode Mode) BombRound(BombSettings? bomb = null, double freeze = 0)
    {
        Host host = new(teams: [0, 1])
        {
            Layout = new MpLayout
            {
                Points =
                [
                    new MpPoint("A", MpPointKind.BombSite, [100 * Metre, 0, 0], 5 * Metre),
                    new MpPoint("B", MpPointKind.BombSite, [300 * Metre, 0, 0], 5 * Metre),
                ],
            },
        };
        host.Positions[0] = new(0, 0, 0);
        host.Positions[1] = new(-50 * Metre, 0, 0);
        SearchAndDestroyMode mode = new(TickRate, new RoundSettings { FreezeSeconds = freeze, RoundOverSeconds = 1 }, bomb ?? new BombSettings { PlantSeconds = 1, DefuseSeconds = 2, FuseSeconds = 10 });
        mode.Start(host, 0);
        return (host, mode);
    }

    private static long Run(SearchAndDestroyMode mode, Host host, long from, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            mode.Tick(host, from + i, []);
        }

        return from + ticks;
    }

    [Fact]
    public void A_round_opens_on_its_freeze_time_everyone_held_and_buying_and_the_round_runs_from_its_end()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound(freeze: 10);

        // Held, the clock on the freeze, the buy time running past it; a plant held down does nothing yet.
        host.Using.Add(0);
        host.Positions[0] = new(100 * Metre, 0, 0);
        long tick = Run(mode, host, 1, TickRate * 5);
        Assert.True(host.Held);
        Assert.Equal(5, mode.Report(tick).FreezeLeft, 1);
        Assert.Equal(25, mode.Report(tick).BuyLeft, 1);
        Assert.StartsWith("Bought Uzi", mode.Buy(host, 1, "uzi", tick), StringComparison.Ordinal);
        Assert.Null(mode.PlantedAt);

        // Let go at its end; then the round is played, and the plant goes in.
        tick = Run(mode, host, tick, (TickRate * 5) + 1);
        Assert.False(host.Held);
        Assert.Equal(0, mode.Report(tick).FreezeLeft);
        tick = Run(mode, host, tick, TickRate + 1);
        Assert.Equal("A", mode.PlantedSite);
    }

    [Fact]
    public void A_bomb_planted_at_a_site_goes_off_on_its_fuse_and_wins_the_round_for_the_attackers()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound();

        // Holding use away from a site plants nothing; at A, a second of it does.
        host.Using.Add(0);
        long tick = Run(mode, host, 1, TickRate * 2);
        Assert.Null(mode.PlantedAt);
        host.Positions[0] = new(102 * Metre, 0, 0);
        tick = Run(mode, host, tick, TickRate + 1);
        Assert.Equal("A", mode.PlantedSite);
        Assert.StartsWith("BOMB PLANTED AT A", mode.Status(tick), StringComparison.Ordinal);

        // The defender stood 150 m off: the blast reaches the attacker beside it, not him; the round is the attackers'.
        host.Using.Clear();
        tick = Run(mode, host, tick, (TickRate * 10) + 1);
        Assert.Single(host.Blasts);
        Assert.Equal([0], host.Killed);
        Assert.Equal(MatchPhase.RoundOver, mode.Flow.Phase);
        Assert.Equal(SearchAndDestroyMode.Attackers, mode.Flow.Winner);
    }

    [Fact]
    public void A_defender_who_holds_use_beside_the_bomb_long_enough_defuses_it_and_one_who_lets_go_starts_again()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound();
        host.Positions[0] = new(300 * Metre, 0, 0);
        host.Using.Add(0);
        long tick = Run(mode, host, 1, TickRate + 1);
        Assert.Equal("B", mode.PlantedSite);
        host.Using.Clear();

        // A second and a half beside it, then let go: nothing; then two seconds unbroken.
        host.Positions[1] = new(301 * Metre, 0, 0);
        host.Using.Add(1);
        tick = Run(mode, host, tick, TickRate + (TickRate / 2));
        host.Using.Clear();
        tick = Run(mode, host, tick, 1);
        Assert.False(mode.Defused);
        host.Using.Add(1);
        tick = Run(mode, host, tick, (TickRate * 2) + 1);
        Assert.True(mode.Defused);
        Assert.Equal(SearchAndDestroyMode.Defenders, mode.Flow.Winner);
        Assert.Empty(host.Blasts);
        _ = tick;
    }

    [Fact]
    public void A_round_the_clock_ends_with_no_bomb_down_is_the_defenders()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound();
        Run(mode, host, 1, (TickRate * 120) + 1);
        Assert.Equal(MatchPhase.RoundOver, mode.Flow.Phase);
        Assert.Equal(SearchAndDestroyMode.Defenders, mode.Flow.Winner);
    }

    [Fact]
    public void Once_the_bomb_is_down_the_attackers_falling_does_not_end_the_round()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound();
        host.Positions[0] = new(100 * Metre, 0, 0);
        host.Using.Add(0);
        long tick = Run(mode, host, 1, TickRate + 1);
        host.Using.Clear();
        mode.Tick(host, tick, [new WorldDeath(10, 11)]);
        Assert.Equal(MatchPhase.Live, mode.Flow.Phase);
    }

    [Fact]
    public void The_banner_runs_its_clock_down_and_names_the_winner()
    {
        MatchState live = new(GameModes.FreeForAll, MatchPhase.Live, 1, -1, 90);
        Assert.Equal(new MatchBanner("FREE FOR ALL", "1:20", null), live.Banner(10));
        MatchState over = new(GameModes.TeamDeathmatch, MatchPhase.Intermission, 1, 0, 15, MatchState.TeamName(0));
        Assert.Equal("IGI WINS", over.Banner(0)!.Headline);
        Assert.Equal("DRAW", (over with { Winner = -1 }).Banner(0)!.Headline);
        Assert.Null(new MatchState(GameModes.Coop, MatchPhase.Live, 1, -1, null).Banner(0));
    }

    [Fact]
    public void In_the_buy_time_near_its_start_a_player_buys_what_it_can_afford_and_not_after()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound();
        Assert.True(mode.Report(1).BuyLeft > 0);

        // An Uzi from the start money; a rifle it cannot pay for; then walked off, and later, nothing.
        Assert.StartsWith("Bought Uzi", mode.Buy(host, 0, "uzi", 1), StringComparison.Ordinal);
        Assert.Equal([(0, "WEAPON_ID_UZI", 320)], host.Given);
        Assert.Equal(host.Match.Rules.MoneyStart - 900, host.Match.Players[0].Money);
        Assert.Contains("costs", mode.Buy(host, 0, "ak47", 1), StringComparison.Ordinal);
        host.Positions[0] = new(40 * Metre, 0, 0);
        Assert.Equal("Buy where your side starts.", mode.Buy(host, 0, "colt", 1));
        host.Positions[0] = new(0, 0, 0);
        Assert.Equal("The buy time is over.", mode.Buy(host, 0, "colt", (TickRate * 21) + 1));
        Assert.Single(host.Given);
        Assert.StartsWith("Buy: 1 colt", mode.Buy(host, 0, "nothing", 1), StringComparison.Ordinal);
    }

    [Fact]
    public void A_carried_guns_ammunition_is_bought_as_igi2_sells_it_filled_back_up_and_not_while_full()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound();
        int start = host.Match.Players[0].Money;

        // Not carried: nothing to fill.
        Assert.Equal("You carry no Uzi.", mode.Buy(host, 0, "ammo uzi", 1));

        // Carried and full: not sold.
        host.Room[(0, "WEAPON_ID_UZI")] = 0;
        Assert.Equal("Your Uzi ammo is full.", mode.Buy(host, 0, "ammo uzi", 1));

        // Some spent: sold at I.G.I. 2's clip price, the pouch filled back to what the gun comes with.
        host.Room[(0, "WEAPON_ID_UZI")] = 120;
        Assert.StartsWith("Bought Uzi ammo", mode.Buy(host, 0, "ammo uzi", 1), StringComparison.Ordinal);
        Assert.Equal([(0, "WEAPON_ID_UZI", 320)], host.Given);
        Assert.Equal(start - 60, host.Match.Players[0].Money);

        // The menu's row for it.
        ArmouryItem row = Armoury.AmmoRow(Armoury.Find(Armoury.Igi1, "uzi")!, full: true);
        Assert.Equal(("ammo uzi", 60, true, true), (row.Id, row.Price, row.IsAmmo, row.Full));
    }

    [Fact]
    public void Only_the_carrier_plants_and_a_carrier_who_falls_drops_the_bomb_for_another_attacker_to_take_up()
    {
        // Attackers in slots 0 and 1, a defender in 2; the first round's bomb goes to slot 0.
        Host host = new(teams: [0, 0, 1]) { Layout = new MpLayout { Points = [new MpPoint("A", MpPointKind.BombSite, [100 * Metre, 0, 0], 5 * Metre)] } };
        host.Positions[0] = new(10 * Metre, 0, 0);
        host.Positions[1] = new(101 * Metre, 0, 0);
        host.Positions[2] = new(-50 * Metre, 0, 0);
        SearchAndDestroyMode mode = new(TickRate, new RoundSettings { FreezeSeconds = 0, RoundOverSeconds = 1 }, new BombSettings { PlantSeconds = 1 });
        mode.Start(host, 0);
        Assert.Equal(0, mode.Carrier);

        // Slot 1 holds use at the site without the bomb: nothing.
        host.Using.Add(1);
        long tick = Run(mode, host, 1, TickRate * 2);
        Assert.Null(mode.PlantedAt);

        // The carrier falls 10 m out: the bomb lies there, not with anyone; the defender walking over it leaves it.
        mode.Tick(host, tick++, [new WorldDeath(10, 12)]);
        Assert.Equal(-1, mode.Carrier);
        Assert.Equal(new OpenIGI.Core.Numerics.Vector3d(10 * Metre, 0, 0), mode.DroppedAt);
        Assert.Equal(mode.DroppedAt, mode.Report(tick).DroppedAt);
        host.Positions[2] = new(10.5 * Metre, 0, 0);
        tick = Run(mode, host, tick, 2);
        Assert.Equal(-1, mode.Carrier);

        // Slot 1 takes it up, walks back to the site and plants.
        host.Positions[1] = new(11 * Metre, 0, 0);
        tick = Run(mode, host, tick, 1);
        Assert.Equal(1, mode.Carrier);
        Assert.Null(mode.DroppedAt);
        host.Positions[1] = new(101 * Metre, 0, 0);
        tick = Run(mode, host, tick, TickRate + 1);
        Assert.Equal("A", mode.PlantedSite);
        Assert.Equal(-1, mode.Carrier);
        _ = tick;
    }

    [Fact]
    public void Each_round_hands_the_bomb_to_the_next_attacker()
    {
        Host host = new(teams: [0, 0, 1]) { Layout = new MpLayout { Points = [new MpPoint("A", MpPointKind.BombSite, [100 * Metre, 0, 0], 5 * Metre)] } };
        host.Positions[0] = new(0, 0, 0);
        host.Positions[1] = new(0, 0, 0);
        host.Positions[2] = new(-50 * Metre, 0, 0);
        SearchAndDestroyMode mode = new(TickRate, new RoundSettings { FreezeSeconds = 0, RoundsToWin = 5, RoundSeconds = 1, RoundOverSeconds = 1, SwapSides = false });
        mode.Start(host, 0);
        Assert.Equal(0, mode.Carrier);

        // The round runs out and the next begins: the bomb goes on to slot 1, then back to slot 0.
        long tick = Run(mode, host, 1, (TickRate * 2) + 2);
        Assert.Equal((1, MatchPhase.Live), (mode.RoundsPlayed, mode.Flow.Phase));
        Assert.Equal(1, mode.Carrier);
        _ = Run(mode, host, tick, (TickRate * 2) + 2);
        Assert.Equal(0, mode.Carrier);
    }

    [Fact]
    public void A_round_pays_its_winners_and_losers_and_a_plant_pays_its_planter()
    {
        (Host host, SearchAndDestroyMode mode) = BombRound();
        int start = host.Match.Rules.MoneyStart;
        host.Positions[0] = new(102 * Metre, 0, 0);
        host.Using.Add(0);
        long tick = Run(mode, host, 1, TickRate + 1);
        Assert.Equal(start + host.Match.Rules.MoneyPlant, host.Match.Players[0].Money);

        // The defender stood clear; the blast wins the round for the attackers, and each side is paid for it.
        host.Using.Clear();
        _ = Run(mode, host, tick, (TickRate * 10) + 1);
        Assert.Equal(start + host.Match.Rules.MoneyPlant + host.Match.Rules.MoneyMissionWin, host.Match.Players[0].Money);
        Assert.Equal(start + host.Match.Rules.MoneyMissionLost, host.Match.Players[1].Money);
    }

    // A match of players in slots 0.., each entity 10 + slot, on the teams given.
    private sealed class Host : IModeHost
    {
        private readonly FakeWorld _world = new();

        public Host(int[] teams)
        {
            Match = new Match(new MultiplayerRules());
            for (int slot = 0; slot < teams.Length; slot++)
            {
                Match.Join(slot, $"p{slot}", teams[slot]).IsAlive = true;
            }

            PlayerSlots = [.. Enumerable.Range(0, teams.Length)];
        }

        public bool HoldsTheDead { get => _world.Holds; init => _world.Holds = value; }

        public MpLayout? Layout { get; init; }

        public Dictionary<int, OpenIGI.Core.Numerics.Vector3d> Positions => _world.Positions;

        public List<int> Killed => _world.Killed;

        public HashSet<int> Using => _world.Using;

        public List<OpenIGI.Core.Numerics.Vector3d> Blasts => _world.Blasts;

        public Match Match { get; }

        public string Title => "igi1";

        public List<(int Slot, string Weapon, int Rounds)> Given => _world.Given;

        public Dictionary<(int Slot, string Weapon), int> Room => _world.Room;

        public bool Held => _world.Held;

        public IReplicatedWorld World => _world;

        public int TickRate => GameModeTests.TickRate;

        public IReadOnlyList<int> PlayerSlots { get; }

        public IReadOnlyList<int> ClientSlots => PlayerSlots;

        public List<int> Respawned { get; } = [];

        public int SlotOf(int entity) => entity >= 10 ? entity - 10 : -1;

        public void Respawn(int slot)
        {
            Respawned.Add(slot);
            Match.Players[slot].IsAlive = true;
            Match.Players[slot].DiedAt = null;
        }

        public void Revive(int slot) => _world.Revived.Add(slot);
    }

    private sealed class FakeWorld : IReplicatedWorld
    {
        public bool Holds { get; set; }

        public List<int> Revived { get; } = [];

        public Dictionary<int, OpenIGI.Core.Numerics.Vector3d> Positions { get; } = [];

        public List<int> Killed { get; } = [];

        public HashSet<int> Using { get; } = [];

        public List<OpenIGI.Core.Numerics.Vector3d> Blasts { get; } = [];

        public List<(int Slot, string Weapon, int Rounds)> Given { get; } = [];

        public bool Held { get; private set; }

        public void HoldStill(bool held) => Held = held;

        public bool Give(int slot, string weapon, int rounds)
        {
            Given.Add((slot, weapon, rounds));
            return true;
        }

        public Dictionary<(int Slot, string Weapon), int> Room { get; } = [];

        public int? AmmoRoom(int slot, string weapon, int upTo) => Room.TryGetValue((slot, weapon), out int room) ? room : null;

        public bool IsUsing(int slot) => Using.Contains(slot);

        public void Blast(OpenIGI.Core.Numerics.Vector3d at, double radius) => Blasts.Add(at);

        public OpenIGI.Core.Numerics.Vector3d? PositionOf(int slot) => Positions.TryGetValue(slot, out OpenIGI.Core.Numerics.Vector3d at) ? at : null;

        public void KillPlayer(int slot) => Killed.Add(slot);

        public bool HoldsTheDead => Holds;

        public int ChannelCount => 1;

        public long CurrentTick => 0;

        public LagCompensation? LagCompensation { get; set; }

        public IReadOnlyList<IRoundObjective> RoundObjectives => [];

        public int CurrentObjective { get; set; }

        public IReadOnlyList<int> TakeRevivals()
        {
            int[] revived = [.. Revived];
            Revived.Clear();
            return revived;
        }

        public int? AddPlayer(int slot, string name) => slot;

        public void RemovePlayer(int slot)
        {
        }

        public UserCommandBuffer? CommandsFor(int slot) => null;

        public Snapshot Capture(long tick) => new(tick, []);

        public int TeamOf(int slot) => 0;

        public IReadOnlyList<WorldDeath> TakeDeaths() => [];

        public void Respawn(int slot)
        {
        }
    }
}
