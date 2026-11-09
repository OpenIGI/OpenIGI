using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Game.Multiplayer;

/// <content>
/// A player choosing its side (<c>/team igi</c>, <c>/team conspiracy</c>, <c>/team auto</c>): held to the teams being no more
/// than one apart. A mode that puts the fallen back straight away moves the player now, at a spawn of its new side; a round
/// mode moves a living player at the next round, as the tactical shooters do, so nobody turns on its side in the middle of one.
/// </content>
public sealed partial class ServerSession
{
    /// <summary>What a player asks for to take a side: its name, or its number on the team menu.</summary>
    /// <param name="asked">What was asked: <c>igi</c> or <c>1</c>, <c>conspiracy</c> or <c>2</c>, <c>auto</c> or <c>3</c>.</param>
    /// <returns>The side, <c>-1</c> for the fairer one, or <see langword="null"/> for none.</returns>
    public static int? TeamAsked(string asked) => (asked ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "igi" or "1" or "good" => TeamRoster.Good,
        "conspiracy" or "cons" or "2" or "bad" => TeamRoster.Bad,
        "auto" or "3" or "" => -1,
        _ => null,
    };

    /// <summary>Puts a player on the side it asked for, if the teams allow it.</summary>
    /// <param name="slot">The player's slot.</param>
    /// <param name="asked">What it asked for (see <see cref="TeamAsked"/>).</param>
    /// <returns>What it is told.</returns>
    public string ChooseTeam(int slot, string asked)
    {
        if (!Mode.IsTeamMode)
        {
            return "This mode has no teams.";
        }

        if (!Match.Players.TryGetValue(slot, out MatchPlayer? player))
        {
            return "You are not in the match.";
        }

        if (TeamAsked(asked) is not { } wanted)
        {
            return "Team: igi, conspiracy or auto.";
        }

        // The others' sides as they will stand, a pending move counted where it goes. A player kept for its return (a
        // negative slot) is not playing and is not counted, as the scoreboard and the team menu leave it out: counted, a
        // side two of whose players dropped stayed "full" with one player on it.
        int Count(int team) => Match.Players.Values.Count(p => p.Slot >= 0 && p.Slot != slot && (p.NextTeam ?? p.Team) == team);
        int now = player.NextTeam ?? player.Team;
        if (wanted < 0)
        {
            int good = Count(TeamRoster.Good);
            int bad = Count(TeamRoster.Bad);
            wanted = good == bad ? now : good < bad ? TeamRoster.Good : TeamRoster.Bad;
        }

        string name = MatchState.TeamName(wanted);
        if (wanted == now)
        {
            return player.NextTeam is null ? $"You are on {name}." : $"You join {name} next round.";
        }

        // Joining it must not leave it more than one ahead of the other side.
        if (Count(wanted) > Count(now))
        {
            return $"{name} has too many players.";
        }

        // A round mode's living player in the round under way moves at the next; anyone else moves now.
        bool roundUnderWay = Mode is RoundMode && Mode.Flow.Phase == MatchPhase.Live && Mode.Report(_tick).FreezeLeft <= 0;
        if (roundUnderWay && player.IsAlive)
        {
            player.NextTeam = wanted == player.Team ? null : wanted;
            return player.NextTeam is null ? $"You stay on {name}." : $"You join {name} next round.";
        }

        player.NextTeam = null;
        player.Team = wanted;
        _world.SetTeam(slot, wanted);
        if (player.IsAlive)
        {
            ((IModeHost)this).Respawn(slot);
        }

        return $"You are on {name}.";
    }
}
