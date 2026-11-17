using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Dedicated;
using OpenIGI.Releases.Accounts;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// A dedicated server's result of a decided match, as the master is sent it: every player under the key they played
/// under, their side, their kills and deaths, and the winners marked; nothing for a match still being played.
/// </summary>
public sealed class Igi1MatchReportTests
{
    /// <summary>Two players, one kill, the match ended: the report names both by key and marks the leading team the winner.</summary>
    [RequiresGameDataFact]
    public void A_decided_match_is_reported_with_every_player_under_their_key()
    {
        using Igi1DedicatedTrio trio = new(GameModes.TeamDeathmatch, 1, 0.05, 0.05);
        Assert.True(trio.RunUntil(() => trio.AllIn, 3000) >= 0, "the players never both came in");
        ServerSession server = trio.Session.Session!;
        MatchPlayer[] players = [.. server.Match.Players.Values.Where(static p => p.Slot >= 0).OrderBy(static p => p.Slot)];
        Assert.Equal(2, players.Length);
        Assert.NotEqual(players[0].Team, players[1].Team);

        // Not decided yet: nothing to report beyond the counts so far, and the reporter waits for the decision.
        Assert.False(trio.Session.IsMatchDecided);

        // The kill, and the point the mode gives the killer's team for it.
        server.Match.Kill(players[1].Slot, players[0].Slot, 1.0);
        server.Match.AddTeamScore(players[0].Team, 1);
        trio.Session.EndMatch();
        trio.RunUntil(() => trio.Session.IsMatchDecided, 60);
        Assert.True(trio.Session.IsMatchDecided);

        MatchReport report = MatchReporter.Report(server, trio.Session.Map, 125)!;
        Assert.Equal("igi1", report.Title);
        Assert.Equal(GameModes.TeamDeathmatch, report.Mode);
        Assert.Equal(125, report.Seconds);
        Assert.Equal(players[0].Team, report.Winner);

        MatchReportLine killer = report.Players.Single(line => line.Name == players[0].Name);
        MatchReportLine victim = report.Players.Single(line => line.Name == players[1].Name);
        Assert.Equal((1, 0, true), (killer.Kills, killer.Deaths, killer.Won));
        Assert.Equal((0, 1, false), (victim.Kills, victim.Deaths, victim.Won));
        Assert.Matches("^[0-9a-f]{16}$", killer.Player!);
        Assert.NotEqual(killer.Player, victim.Player);
    }
}
