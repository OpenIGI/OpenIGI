using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>One kill as the feed lists it: who killed whom, each by name and team.</summary>
/// <param name="Killer">The killer's name, or empty for a death of the player's own or the world's (a fall, a blast).</param>
/// <param name="KillerTeam">The killer's team, or -1.</param>
/// <param name="Victim">The victim's name.</param>
/// <param name="VictimTeam">The victim's team, or -1.</param>
public readonly record struct KillNote(string Killer, int KillerTeam, string Victim, int VictimTeam)
{
    /// <summary>Writes the note, for the clients.</summary>
    /// <param name="writer">Where it goes.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteString(Killer ?? string.Empty, 32);
        writer.WriteRanged(Math.Clamp(KillerTeam, -1, 14), -1, 14);
        writer.WriteString(Victim ?? string.Empty, 32);
        writer.WriteRanged(Math.Clamp(VictimTeam, -1, 14), -1, 14);
    }

    /// <summary>Reads a note a server sent.</summary>
    /// <param name="reader">What arrived.</param>
    /// <returns>The note.</returns>
    public static KillNote Read(ref BitReader reader)
    {
        string killer = reader.ReadString(32);
        int killerTeam = reader.ReadRanged(-1, 14);
        string victim = reader.ReadString(32);
        return new KillNote(killer, killerTeam, victim, reader.ReadRanged(-1, 14));
    }
}

/// <summary>The kills of late, newest last: a few, each for a few seconds.</summary>
public sealed class KillFeed
{
    /// <summary>The most notes listed at once.</summary>
    public const int Kept = 5;

    /// <summary>How long a note is listed, in seconds.</summary>
    public const double ShownSeconds = 6;

    private readonly List<(KillNote Note, double At)> _notes = [];

    /// <summary>Lists a kill.</summary>
    /// <param name="note">The kill.</param>
    /// <param name="now">The time, in seconds.</param>
    public void Add(KillNote note, double now)
    {
        _notes.Add((note, now));
        if (_notes.Count > Kept)
        {
            _notes.RemoveAt(0);
        }
    }

    /// <summary>Gets the kills still listed.</summary>
    /// <param name="now">The time, in seconds, on the clock the kills were added by.</param>
    /// <returns>Them, oldest first.</returns>
    public IReadOnlyList<KillNote> Shown(double now)
    {
        _notes.RemoveAll(n => now - n.At >= ShownSeconds);
        return [.. _notes.Select(n => n.Note)];
    }
}
