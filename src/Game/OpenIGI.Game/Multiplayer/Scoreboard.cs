using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>One player's line on the scoreboard.</summary>
/// <param name="Slot">The player's slot.</param>
/// <param name="Name">The player's name.</param>
/// <param name="Team">The player's team.</param>
/// <param name="Money">The player's money.</param>
/// <param name="Kills">The enemies the player has killed.</param>
/// <param name="Deaths">The times the player has died.</param>
/// <param name="IsAlive">Whether the player is alive.</param>
/// <param name="Entity">The player's entity in the snapshots, or -1.</param>
/// <param name="Ping">The player's round trip to the server, in milliseconds; nought for the server's own.</param>
public readonly record struct ScoreboardLine(
    int Slot, string Name, int Team, int Money, int Kills, int Deaths, bool IsAlive, int Entity = -1, int Ping = 0);

/// <summary>The scoreboard a server sends its clients: every player's standing, the team scores and the round.</summary>
/// <param name="Round">The round being played.</param>
/// <param name="GoodScore">The first team's score.</param>
/// <param name="BadScore">The second team's score.</param>
/// <param name="Lines">The players.</param>
public sealed record Scoreboard(int Round, int GoodScore, int BadScore, ScoreboardLine[] Lines)
{
    /// <summary>Gets the scoreboard of a match now.</summary>
    /// <param name="match">The match.</param>
    /// <returns>The scoreboard.</returns>
    /// <param name="entityOf">Each slot's entity in the snapshots, or -1; none named when omitted.</param>
    /// <param name="pingOf">Each slot's round trip in milliseconds; nought when omitted.</param>
    public static Scoreboard Of(Match match, Func<int, int>? entityOf = null, Func<int, int>? pingOf = null)
    {
        ArgumentNullException.ThrowIfNull(match);
        return new Scoreboard(
            match.Round,
            match.ScoreOf(TeamRoster.Good),
            match.ScoreOf(TeamRoster.Bad),
            [.. match.Players.Values.OrderBy(static p => p.Slot)
                .Select(p => new ScoreboardLine(
                    p.Slot, p.Name, p.Team, p.Money, p.Kills, p.Deaths, p.IsAlive, entityOf?.Invoke(p.Slot) ?? -1, pingOf?.Invoke(p.Slot) ?? 0))]);
    }

    /// <summary>Writes it after a message's kind.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteVarUInt((uint)Round);
        writer.WriteVarInt(GoodScore);
        writer.WriteVarInt(BadScore);
        writer.WriteVarUInt((uint)Lines.Length);
        foreach (ScoreboardLine line in Lines)
        {
            writer.WriteVarUInt((uint)line.Slot);
            writer.WriteString(line.Name, JoinRequest.MaxNameBytes);
            writer.WriteVarInt(line.Team);
            writer.WriteVarInt(line.Money);
            writer.WriteVarUInt((uint)line.Kills);
            writer.WriteVarUInt((uint)line.Deaths);
            writer.WriteBool(line.IsAlive);
            writer.WriteVarInt(line.Entity);
            writer.WriteVarUInt((uint)Math.Clamp(line.Ping, 0, 9999));
        }
    }

    /// <summary>Reads what <see cref="Write"/> wrote.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>The scoreboard, or <see langword="null"/> when malformed.</returns>
    public static Scoreboard? Read(ref BitReader reader)
    {
        int round = (int)reader.ReadVarUInt();
        int good = reader.ReadVarInt();
        int bad = reader.ReadVarInt();
        uint count = reader.ReadVarUInt();
        if (reader.IsOverflowed || count > 256)
        {
            return null;
        }

        ScoreboardLine[] lines = new ScoreboardLine[count];
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = new ScoreboardLine(
                (int)reader.ReadVarUInt(), reader.ReadString(JoinRequest.MaxNameBytes), reader.ReadVarInt(), reader.ReadVarInt(),
                (int)reader.ReadVarUInt(), (int)reader.ReadVarUInt(), reader.ReadBool(), reader.ReadVarInt(), (int)reader.ReadVarUInt());
        }

        return reader.IsOverflowed ? null : new Scoreboard(round, good, bad, lines);
    }
}
