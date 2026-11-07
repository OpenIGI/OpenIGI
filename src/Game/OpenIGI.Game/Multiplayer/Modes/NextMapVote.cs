using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>One map a decided match offers for the next: a mission, the mode played on it, and its name.</summary>
/// <param name="Mission">The mission's id.</param>
/// <param name="Mode">The mode's id (<see cref="GameModes"/>), or empty for the one played now.</param>
/// <param name="Name">The mission's name, as the vote shows it; empty when the server doesn't know it.</param>
/// <param name="Again">Whether it is the map and mode just played, offered to play again.</param>
public sealed record MapChoice(int Mission, string Mode, string Name = "", bool Again = false);

/// <summary>
/// The vote for the next map while a decided match's card is up, as one player sees it: the maps offered, how many picked each,
/// how many may pick, and which this player picked. The server tells each player its own whenever a pick changes, and once the
/// vote closes tells them none.
/// </summary>
/// <param name="Choices">The maps offered, in order: the first is picked by 1, the second by 2.</param>
/// <param name="Counts">How many picked each, by its index.</param>
/// <param name="Voters">How many players may pick.</param>
/// <param name="OwnPick">The index this player picked, or -1.</param>
public sealed record NextMapVote(IReadOnlyList<MapChoice> Choices, IReadOnlyList<int> Counts, int Voters, int OwnPick)
{
    /// <summary>The most maps a vote offers: one for each of the number keys a player reaches without looking.</summary>
    public const int MostChoices = 4;

    /// <summary>Gets how many have picked.</summary>
    public int Picked => Counts.Sum();

    /// <summary>Gets the index leading the vote, the first offered on a tie, or -1 while nobody has picked.</summary>
    public int Leading
    {
        get
        {
            int best = -1;
            for (int i = 0; i < Counts.Count; i++)
            {
                if (Counts[i] > 0 && (best < 0 || Counts[i] > Counts[best]))
                {
                    best = i;
                }
            }

            return best;
        }
    }

    /// <summary>Writes a vote, or that there is none.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="vote">The vote, or <see langword="null"/> once it is closed.</param>
    public static void Write(BitWriter writer, NextMapVote? vote)
    {
        ArgumentNullException.ThrowIfNull(writer);
        int count = vote is null ? 0 : Math.Min(vote.Choices.Count, MostChoices);
        writer.WriteRanged(count, 0, MostChoices);
        if (vote is null)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            MapChoice choice = vote.Choices[i];
            writer.WriteVarUInt((uint)Math.Max(0, choice.Mission));
            writer.WriteString(choice.Mode, 16);
            writer.WriteString(choice.Name, 64);
            writer.WriteBool(choice.Again);
            writer.WriteVarUInt((uint)Math.Max(0, i < vote.Counts.Count ? vote.Counts[i] : 0));
        }

        writer.WriteVarUInt((uint)Math.Max(0, vote.Voters));
        writer.WriteRanged(Math.Clamp(vote.OwnPick, -1, MostChoices - 1), -1, MostChoices - 1);
    }

    /// <summary>Reads what <see cref="Write"/> wrote.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The vote, or <see langword="null"/> when there is none.</returns>
    public static NextMapVote? Read(ref BitReader reader)
    {
        int count = reader.ReadRanged(0, MostChoices);
        if (count == 0)
        {
            return null;
        }

        var choices = new MapChoice[count];
        int[] counts = new int[count];
        for (int i = 0; i < count; i++)
        {
            int mission = (int)Math.Min(reader.ReadVarUInt(), int.MaxValue);
            string mode = reader.ReadString(16);
            string name = reader.ReadString(64);
            bool again = reader.ReadBool();
            counts[i] = (int)Math.Min(reader.ReadVarUInt(), 255);
            choices[i] = new MapChoice(mission, mode, name, again);
        }

        int voters = (int)Math.Min(reader.ReadVarUInt(), 255);
        int own = reader.ReadRanged(-1, MostChoices - 1);
        return new NextMapVote(choices, counts, voters, own);
    }
}
