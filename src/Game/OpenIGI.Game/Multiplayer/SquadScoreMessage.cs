using OpenIGI.Game.Scoring;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// A co-op mission's score as the server sends it, <see cref="SessionMessage.MissionScore"/>: what its ranks ask, the seconds left
/// before the squad goes on, the squad's own counts, then each player's entity, name and counts. Each client builds its own card from it, in
/// its own language.
/// </summary>
public static class SquadScoreMessage
{
    /// <summary>The most players a message carries.</summary>
    public const int MaxMembers = 64;

    /// <summary>Writes a squad's score.</summary>
    /// <param name="writer">The message, its kind already written.</param>
    /// <param name="squad">The score.</param>
    public static void Write(BitWriter writer, SquadScore squad)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(squad);
        for (int rank = 0; rank < MissionRankRequirements.RankCount; rank++)
        {
            for (int item = 0; item < MissionRankItem.Count; item++)
            {
                writer.WriteSingle(squad.Requirements[rank, item]);
            }
        }

        writer.WriteVarUInt((uint)Math.Clamp(squad.SecondsLeft, 0, 255));
        WriteCounts(writer, squad.Squad, squad.Requirements);
        SquadScoreMember[] members = [.. squad.Members.Take(MaxMembers)];
        writer.WriteVarUInt((uint)members.Length);
        foreach (SquadScoreMember member in members)
        {
            writer.WriteVarInt(member.Entity);
            writer.WriteString(member.Name, JoinRequest.MaxNameBytes);
            WriteCounts(writer, member.Score, squad.Requirements);
        }
    }

    // A score's counts, in a kept record's order.
    private static void WriteCounts(BitWriter writer, in MissionScore score, MissionRankRequirements requirements)
    {
        foreach (int count in MissionScoreRecord.Encode(score, requirements).Counts)
        {
            writer.WriteVarInt(count);
        }
    }

    // A score's counts, or null where they are not a score's.
    private static MissionScore? ReadCounts(ref BitReader reader)
    {
        int[] counts = new int[MissionScoreRecord.CountCount];
        for (int c = 0; c < counts.Length; c++)
        {
            counts[c] = reader.ReadVarInt();
        }

        return !reader.IsOverflowed && MissionScoreRecord.TryReadScore(counts, out MissionScore score) ? score : null;
    }

    /// <summary>Reads a squad's score.</summary>
    /// <param name="reader">The message, past its kind.</param>
    /// <returns>The score, or <see langword="null"/> where the message does not hold one.</returns>
    public static SquadScore? Read(ref BitReader reader)
    {
        float[] figures = new float[MissionScoreRecord.FigureCount];
        for (int i = 0; i < figures.Length; i++)
        {
            figures[i] = reader.ReadSingle();
        }

        int secondsLeft = (int)Math.Min(reader.ReadVarUInt(), 255u);
        MissionScore? squad = ReadCounts(ref reader);
        uint count = reader.ReadVarUInt();
        if (squad is not { } together || count > MaxMembers || !MissionScoreRecord.TryReadRequirements(figures, out MissionRankRequirements? requirements))
        {
            return null;
        }

        List<SquadScoreMember> members = [];
        for (int i = 0; i < count; i++)
        {
            int entity = reader.ReadVarInt();
            string name = reader.ReadString(JoinRequest.MaxNameBytes);
            if (ReadCounts(ref reader) is not { } score)
            {
                return null;
            }

            members.Add(new SquadScoreMember(entity, name, score));
        }

        return reader.IsOverflowed ? null : new SquadScore(requirements, together, members, secondsLeft);
    }
}
