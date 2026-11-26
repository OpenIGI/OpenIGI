using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Presentation.Hud.Remastered;

/// <summary>The kill feed as one player sees it: the kills listed, and who the player is among them.</summary>
/// <param name="Entries">The kills, oldest first, each with its age.</param>
/// <param name="OwnEntity">The player's own entity, or -1: the rows it is in are marked.</param>
/// <param name="OwnTeam">The player's team, or -1: the sides are coloured from it, its own as friends.</param>
/// <param name="Teams">Whether the match is played in sides; otherwise every other player is an enemy (co-op: none is).</param>
/// <param name="Coop">Whether everyone plays together against the level.</param>
/// <param name="Art">The title's weapon pictures, or <see langword="null"/> for names only.</param>
public sealed record KillFeedView(
    IReadOnlyList<KillFeedEntry> Entries, int OwnEntity, int OwnTeam, bool Teams, bool Coop, IKillFeedArt? Art)
{
    /// <summary>How long a player's own kill is announced in the middle of the screen, in seconds.</summary>
    public const double ConfirmSeconds = 2.2;

    /// <summary>The longest gap between a player's kills that still counts them together, in seconds.</summary>
    public const double StreakSeconds = 4.0;

    /// <summary>Gets the player's newest kill still being announced, and how many came together with it.</summary>
    /// <returns>The kill and its age and count, or <see langword="null"/> for none.</returns>
    public (KillFeedEntry Kill, int Streak)? Confirmed()
    {
        if (OwnEntity < 0)
        {
            return null;
        }

        KillFeedEntry? newest = null;
        int streak = 0;
        double? previous = null;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            KillFeedEntry entry = Entries[i];
            if (entry.Note.KillerEntity != OwnEntity || (entry.Note.Marks & KillMarks.TeamKill) != 0)
            {
                continue;
            }

            if (newest is null)
            {
                if (entry.Age >= ConfirmSeconds)
                {
                    return null;
                }

                newest = entry;
            }
            else if (entry.Age - previous!.Value > StreakSeconds)
            {
                break;
            }

            streak++;
            previous = entry.Age;
        }

        return newest is { } kill ? (kill, streak) : null;
    }

    /// <summary>Gets the newest kill of the player itself: who killed it and with what.</summary>
    /// <returns>The kill, or <see langword="null"/> for none listed.</returns>
    public KillNote? KilledBy()
    {
        for (int i = Entries.Count - 1; i >= 0 && OwnEntity >= 0; i--)
        {
            if (Entries[i].Note.VictimEntity == OwnEntity)
            {
                return Entries[i].Note;
            }
        }

        return null;
    }

    /// <summary>Gets the multikill a count of kills together is called, or empty for one.</summary>
    /// <param name="streak">The kills together.</param>
    /// <returns>The call.</returns>
    public static string StreakName(int streak) => streak switch
    {
        <= 1 => string.Empty,
        2 => "DOUBLE KILL",
        3 => "TRIPLE KILL",
        _ => "MULTI KILL",
    };
}
