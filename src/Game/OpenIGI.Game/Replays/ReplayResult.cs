using OpenIGI.Game.Scoring;

namespace OpenIGI.Game.Replays;

/// <summary>How a recorded run ended.</summary>
public enum ReplayOutcome
{
    /// <summary>The player left it before it ended: quit, restarted, or a save loaded over it.</summary>
    Abandoned = 0,

    /// <summary>The mission was completed.</summary>
    Completed = 1,

    /// <summary>The mission was failed.</summary>
    Failed = 2,
}

/// <summary>
/// How a recorded run ended, as the machine that played it saw it: a claim, which a replay checker plays the inputs again to
/// see for itself, and never takes on trust.
/// </summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="EndTick">The level's tick it ended on.</param>
/// <param name="Score">The mission's score as it ended (<see cref="MissionScore"/>), or <see langword="null"/> where none was kept.</param>
/// <param name="Unranked">
/// Why the run could not be ranked, as the recording saw it -- a cheat set, a save loaded, a setting outside the rules -- or
/// empty when nothing it saw stops it.
/// </param>
public sealed record ReplayResult(ReplayOutcome Outcome, int EndTick, MissionScore? Score, string Unranked)
{
    /// <summary>Writes the result.</summary>
    /// <param name="writer">Where it goes.</param>
    public void Write(BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write((byte)Outcome);
        writer.Write(EndTick);
        writer.Write(Score.HasValue);
        if (Score is { } score)
        {
            writer.Write(score.Ticks);
            writer.Write(score.TimesSpotted);
            writer.Write(score.Health);
            writer.Write(score.Shots);
            writer.Write(score.Hits);
            writer.Write(score.Difficulty);
            writer.Write(score.MissionId);
            writer.Write(score.GameLoads);
            writer.Write(score.Kills);
            writer.Write(score.TimesAlarmTriggered);
        }

        ReplayBinary.WriteText(writer, Unranked, ReplayHeader.MaxText);
    }

    /// <summary>Reads a result.</summary>
    /// <param name="reader">Where it comes from.</param>
    /// <returns>The result.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a result.</exception>
    public static ReplayResult Read(BinaryReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        byte outcome = reader.ReadByte();
        if (!Enum.IsDefined((ReplayOutcome)outcome))
        {
            throw new InvalidDataException($"A run ended {outcome}, which is no ending.");
        }

        int end = reader.ReadInt32();
        MissionScore? score = null;
        if (reader.ReadBoolean())
        {
            score = new MissionScore(reader.ReadUInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadUInt32(), reader.ReadUInt32())
            {
                Difficulty = reader.ReadInt32(),
                MissionId = reader.ReadInt32(),
                GameLoads = reader.ReadInt32(),
                Kills = reader.ReadInt32(),
                TimesAlarmTriggered = reader.ReadInt32(),
            };
        }

        return new ReplayResult((ReplayOutcome)outcome, end, score, ReplayBinary.ReadText(reader, ReplayHeader.MaxText));
    }
}
