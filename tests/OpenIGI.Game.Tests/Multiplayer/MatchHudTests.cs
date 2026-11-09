using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

public sealed class MatchHudTests
{
    private const double Metre = WorldUnits.PerMetre;

    // Jones (entity 10, attacker, alive), Anya (11, defender, alive), Kim (12, defender, down).
    private static Scoreboard Board() => new(
        3,
        2,
        1,
        [
            new ScoreboardLine(0, "Jones", TeamRoster.Good, 0, 4, 1, true, Entity: 10),
            new ScoreboardLine(1, "Anya", TeamRoster.Bad, 0, 2, 3, true, Entity: 11),
            new ScoreboardLine(2, "Kim", TeamRoster.Bad, 0, 1, 2, false, Entity: 12),
        ]);

    private static MatchState Live(ModeReport report) => new(GameModes.SearchAndDestroy, MatchPhase.Live, 3, -1, 90, string.Empty, report);

    [Fact]
    public void A_report_reads_back_as_it_was_written()
    {
        ModeReport report = new()
        {
            Target = 6,
            Attackers = TeamRoster.Good,
            Reason = RoundEnd.Defused,
            Planted = "B",
            BombAt = new Vector3d(123456789, -987654321, 174414816),
            FuseLeft = 31.5,
            FuseSeconds = 40,
            Working = [new ModeWork(1, 0.5f, true)],
            Carrier = 3,
            DroppedAt = new Vector3d(-5, 6, 7),
            Risks = [new ModeRisk(3, 0.42f), new ModeRisk(7, 2.5f)],
        };
        BitWriter writer = new(256);
        report.Write(writer);
        BitReader reader = new(writer.AsSpan());
        ModeReport read = ModeReport.Read(ref reader);

        Assert.Equal(report.Key, read.Key);
        Assert.Equal(report.BombAt, read.BombAt);
        Assert.Equal((3, report.DroppedAt), (read.Carrier, read.DroppedAt));
        Assert.Equal(31.5, read.FuseLeft, 1);
        Assert.Equal(0.5f, read.Working[0].Progress, 2);
        Assert.Equal([3, 7], read.Risks.Select(r => r.Slot));
        Assert.Equal(0.42f, read.Risks[0].Share, 2);
        Assert.True(read.Risks[1].Share >= 1f, "a lethal risk reads back as lethal");
    }

    [Fact]
    public void The_own_team_is_on_the_left_with_its_role_and_each_side_shows_who_stands()
    {
        MatchHud hud = MatchHud.Of(Live(new ModeReport { Target = 6, Attackers = TeamRoster.Good }), 0, Board(), localEntity: 11)!;

        Assert.Equal("DEFEND", hud.Role);
        Assert.Equal(TeamRoster.Bad, hud.Left!.Team);
        Assert.True(hud.Left.IsOwn);
        Assert.Equal("CONSPIRACY", hud.Left.Name);
        Assert.Equal(1, hud.Left.Score);
        Assert.Equal([true, false], hud.Left.Alive);
        Assert.Equal([true], hud.Right!.Alive);
        Assert.Equal(3, hud.Round);
        Assert.Equal("1:30", hud.Clock);
    }

    [Fact]
    public void An_attacker_standing_in_a_site_is_told_to_hold_use_or_fire_with_the_bomb_in_hand_and_one_planting_sees_its_progress()
    {
        MpLayout layout = new() { Points = [new MpPoint("A", MpPointKind.BombSite, [0, 0, 0], 5 * Metre)] };
        ModeReport report = new() { Attackers = TeamRoster.Good, Carrier = 0 };

        MatchHud inSite = MatchHud.Of(Live(report), 0, Board(), 10, new Vector3d(2 * Metre, 0, 0), layout)!;
        Assert.Equal("OR FIRE TO PLANT THE BOMB AT A", inSite.Hint?.Action);
        Assert.True(inSite.Hint?.Keyed);

        // Carried but not in hand: told to take it up, with no key -- and holding use there starts no bar.
        MatchHud unarmed = MatchHud.Of(
            Live(report), 0, Board(), 10, new Vector3d(2 * Metre, 0, 0), layout, new LocalUse(true, 1.0), bombInHand: false)!;
        Assert.Equal("SWITCH TO THE BOMB", unarmed.Hint?.Verb);
        Assert.False(unarmed.Hint?.Keyed);
        Assert.Null(unarmed.Progress);

        MatchHud outside = MatchHud.Of(Live(report), 0, Board(), 10, new Vector3d(20 * Metre, 0, 0), layout)!;
        Assert.Null(outside.Hint);

        MatchHud planting = MatchHud.Of(Live(report with { Working = [new ModeWork(0, 0.4f, false)] }), 0, Board(), 10, new Vector3d(2 * Metre, 0, 0), layout)!;
        Assert.Equal(0.4f, planting.Progress);
        Assert.Equal("PLANTING THE BOMB", planting.ProgressLabel);
        Assert.Null(planting.Hint);
    }

    [Fact]
    public void The_team_menu_lists_each_sides_players_and_shuts_the_side_that_would_be_two_ahead()
    {
        MatchHud hud = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good }), 0, Board(), 10)!;
        TeamPick pick = Assert.IsType<TeamPick>(hud.Teams);
        Assert.Equal(Board().Lines.Count(l => l.Team == TeamRoster.Good), pick.Igi.Count);
        Assert.Equal(Board().Lines.Count(l => l.Team == TeamRoster.Bad), pick.Conspiracy.Count);

        // Two against one: the one may not join the two, either of the two may go over, and a side is always open to its own.
        TeamPick uneven = new(TeamRoster.Bad, ["a", "b"], ["c"]);
        Assert.False(uneven.Open(TeamRoster.Good));
        Assert.True(uneven.Open(TeamRoster.Bad));
        Assert.True(new TeamPick(TeamRoster.Good, ["a", "b"], ["c"]).Open(TeamRoster.Bad));
        Assert.True(new TeamPick(-1, ["a"], ["c"]).Open(TeamRoster.Good));
        Assert.False(new TeamPick(-1, ["a", "b"], ["c"]).Open(TeamRoster.Good));
        Assert.Equal(["igi", "conspiracy", "auto"], TeamPick.Choices);
    }

    [Fact]
    public void Taking_the_bomb_up_from_the_ground_is_told_as_a_pickup_and_a_round_handing_it_out_is_not()
    {
        MatchHud handedOut = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, Carrier = 0 }), 0, Board(), 10)!;
        MatchHud lying = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, DroppedAt = new Vector3d(0, 0, 0) }), 0, Board(), 10)!;
        MatchHud flying = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, BombFlies = true }), 0, Board(), 10)!;
        BombPickupWatch watch = new();

        Assert.False(watch.Saw(null));
        Assert.False(watch.Saw(handedOut));
        Assert.False(watch.Saw(lying));
        Assert.True(watch.Saw(handedOut));
        Assert.False(watch.Saw(handedOut));
        Assert.False(watch.Saw(flying));
        Assert.True(watch.Saw(handedOut));
    }

    [Fact]
    public void Only_the_carrier_is_told_to_plant_and_an_attacker_is_told_who_has_the_bomb_or_where_it_lies()
    {
        MpLayout layout = new() { Points = [new MpPoint("A", MpPointKind.BombSite, [0, 0, 0], 5 * Metre)] };
        Vector3d inSite = new(2 * Metre, 0, 0);

        MatchHud carrying = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, Carrier = 0 }), 0, Board(), 10, inSite, layout)!;
        Assert.Equal("YOU CARRY THE BOMB", carrying.BombNote);
        Assert.NotNull(carrying.Hint);
        Assert.True(carrying.CarriesBomb);
        Assert.False(carrying.BombIsLoose);

        // Another attacker has it (slot 5, not on this board, is nobody named): no hint, and no name to give.
        MatchHud without = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, Carrier = 5 }), 0, Board(), 10, inSite, layout)!;
        Assert.Null(without.Hint);
        Assert.Null(without.BombNote);

        Vector3d dropped = new(30 * Metre, 0, 0);
        MatchHud down = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, DroppedAt = dropped }), 0, Board(), 10, inSite, layout)!;
        Assert.Equal(dropped, down.BombMarkAt);
        Assert.StartsWith("THE BOMB IS DOWN", down.BombNote, StringComparison.Ordinal);
        Assert.True(down.BombIsLoose);
        Assert.False(down.CarriesBomb);
        Assert.True(MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, BombFlies = true }), 0, Board(), 10, inSite, layout)!.BombIsLoose);

        // A defender is told nothing of the bomb's whereabouts.
        MatchHud defender = MatchHud.Of(Live(new ModeReport { Attackers = TeamRoster.Good, DroppedAt = dropped }), 0, Board(), 11, inSite, layout)!;
        Assert.Null(defender.BombMarkAt);
        Assert.Null(defender.BombNote);
    }

    [Fact]
    public void A_clients_own_plant_starts_as_its_key_goes_down_runs_on_between_reports_and_ends_as_it_comes_up()
    {
        MpLayout layout = new() { Points = [new MpPoint("A", MpPointKind.BombSite, [0, 0, 0], 5 * Metre)] };
        ModeReport report = new() { Attackers = TeamRoster.Good, Carrier = 0 };
        Vector3d inSite = new(2 * Metre, 0, 0);

        // Held for a second, and the server has not heard yet: the bar is already a third of the way.
        MatchHud early = MatchHud.Of(Live(report), 0, Board(), 10, inSite, layout, new LocalUse(true, 1))!;
        Assert.Equal(1 / 3f, early.Progress!.Value, 3);
        Assert.Equal("PLANTING THE BOMB", early.ProgressLabel);
        Assert.Null(early.Hint);

        // The server's word, half a second old: run on by that half second, not held at what it said.
        ModeReport working = report with { Working = [new ModeWork(0, 0.2f, false, 3)] };
        MatchHud between = MatchHud.Of(Live(working), 0.5, Board(), 10, inSite, layout, new LocalUse(true, 0.1))!;
        Assert.Equal(0.2f + (0.5f / 3f), between.Progress!.Value, 3);

        // Let go: gone at once, though the server still says it is planting.
        MatchHud released = MatchHud.Of(Live(working), 0.5, Board(), 10, inSite, layout, new LocalUse(false, 0))!;
        Assert.Null(released.Progress);

        // Held outside any site: nothing to start.
        Assert.Null(MatchHud.Of(Live(report), 0, Board(), 10, new Vector3d(20 * Metre, 0, 0), layout, new LocalUse(true, 1))!.Progress);
    }

    [Fact]
    public void A_planted_bomb_runs_its_fuse_down_and_a_teammate_defusing_is_told()
    {
        ModeReport report = new()
        {
            Attackers = TeamRoster.Good,
            Planted = "A",
            BombAt = new Vector3d(0, 0, 0),
            FuseLeft = 30,
            FuseSeconds = 40,
            Working = [new ModeWork(1, 0.2f, true)],
        };

        MatchHud hud = MatchHud.Of(Live(report), 10, Board(), localEntity: 12)!;

        Assert.Equal("0:20", hud.Fuse);
        Assert.Equal(0.5f, hud.FuseFraction, 2);
        Assert.Equal("ANYA IS DEFUSING", hud.TeamNote);
    }

    [Fact]
    public void A_decided_round_says_who_won_it_and_why_from_each_side()
    {
        MatchState over = new(GameModes.SearchAndDestroy, MatchPhase.RoundOver, 3, TeamRoster.Bad, 5, "Conspiracy", new ModeReport { Attackers = TeamRoster.Good, Reason = RoundEnd.Defused });

        ResultCard won = MatchHud.Of(over, 0, Board(), localEntity: 11)!.Result!;
        ResultCard lost = MatchHud.Of(over, 0, Board(), localEntity: 10)!.Result!;

        Assert.Equal(("ROUND WON", "BOMB DEFUSED", 1), (won.Title, won.Detail, won.Outcome));
        Assert.Equal(("ROUND LOST", -1), (lost.Title, lost.Outcome));
    }

    [Fact]
    public void A_free_for_all_shows_the_players_place_and_the_one_to_beat()
    {
        MatchState state = new(GameModes.FreeForAll, MatchPhase.Live, 1, -1, 300, string.Empty, new ModeReport { Target = 30 });

        MatchHud hud = MatchHud.Of(state, 0, Board(), localEntity: 11)!;

        Assert.False(hud.HasTeams);
        Assert.Equal((2, 2, "Jones", 4), (hud.OwnScore, hud.OwnRank, hud.LeaderName, hud.LeaderScore));
    }

    [Fact]
    public void Co_op_has_no_match_hud()
    {
        Assert.Null(MatchHud.Of(new MatchState(GameModes.Coop, MatchPhase.Live, 1, -1, null), 0, Board(), 10));
    }
}
