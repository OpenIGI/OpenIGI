using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>What killed a player, as the feed pictures it.</summary>
public enum KillCause
{
    /// <summary>A weapon's round: the feed shows the weapon.</summary>
    Weapon = 0,

    /// <summary>A blast: a grenade, a mine, a rocket, a launcher's round.</summary>
    Explosive = 1,

    /// <summary>A blow or a blade.</summary>
    Melee = 2,

    /// <summary>A fall.</summary>
    Fall = 3,

    /// <summary>The level: a fence, a barrel, a guard, nobody's blast.</summary>
    World = 4,

    /// <summary>The player's own doing.</summary>
    Suicide = 5,
}

/// <summary>What else the feed marks a kill with.</summary>
[Flags]
public enum KillMarks
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>The killing round struck the head.</summary>
    Headshot = 1,

    /// <summary>A blade in an unaware back.</summary>
    Backstab = 2,

    /// <summary>The killer and the victim played on the same side.</summary>
    TeamKill = 4,

    /// <summary>The victim is one of the level's soldiers, not a player: a co-op kill.</summary>
    Guard = 8,
}

/// <summary>One kill as the feed lists it: who killed whom, with what, and how.</summary>
/// <param name="Killer">The killer's name, or empty for a death of the player's own or the world's (a fall, a blast).</param>
/// <param name="KillerTeam">The killer's team, or -1.</param>
/// <param name="Victim">The victim's name.</param>
/// <param name="VictimTeam">The victim's team, or -1.</param>
/// <param name="KillerEntity">The killer's entity, or -1: what a player's own kills are told apart by.</param>
/// <param name="VictimEntity">The victim's entity, or -1 for a level's soldier.</param>
/// <param name="Weapon">The title's key for the weapon that killed (an I.G.I. script id), or empty.</param>
/// <param name="Cause">What killed.</param>
/// <param name="Marks">What else marks it.</param>
public readonly record struct KillNote(
    string Killer,
    int KillerTeam,
    string Victim,
    int VictimTeam,
    int KillerEntity = -1,
    int VictimEntity = -1,
    string Weapon = "",
    KillCause Cause = KillCause.Weapon,
    KillMarks Marks = KillMarks.None)
{
    /// <summary>The name a level's soldier is listed under.</summary>
    public const string GuardName = "Guard";

    /// <summary>The longest weapon key sent.</summary>
    public const int MaxWeaponBytes = 32;

    /// <summary>Gets a value indicating whether nobody is credited: the victim's own death or the world's.</summary>
    public bool HasKiller => Killer.Length > 0;

    /// <summary>Makes the note of a counted death.</summary>
    /// <param name="death">The death, as the world told it.</param>
    /// <param name="victim">The player killed, or <see langword="null"/> where the match does not know it.</param>
    /// <param name="killer">The player credited, or <see langword="null"/> for none.</param>
    /// <param name="teams">Whether the match is played in teams, so a kill of one's own side is a team kill.</param>
    /// <returns>The note.</returns>
    public static KillNote Of(WorldDeath death, MatchPlayer? victim, MatchPlayer? killer, bool teams)
    {
        bool own = killer is not null && victim is not null && killer.Slot == victim.Slot;
        bool credited = killer is not null && !own;
        KillCause cause = own ? KillCause.Suicide : death.Cause;
        KillMarks marks = KillMarks.None;
        if (credited && death.Headshot)
        {
            marks |= KillMarks.Headshot;
        }

        if (credited && death.Backstab)
        {
            marks |= KillMarks.Backstab;
        }

        if (credited && teams && victim is not null && killer!.Team == victim.Team)
        {
            marks |= KillMarks.TeamKill;
        }

        return new KillNote(
            credited ? killer!.Name : string.Empty,
            credited ? killer!.Team : -1,
            victim?.Name ?? string.Empty,
            victim?.Team ?? -1,
            credited ? death.Killer : -1,
            death.Victim,
            death.Weapon ?? string.Empty,
            cause,
            marks);
    }

    /// <summary>Makes the note of a player's kill of one of the level's soldiers.</summary>
    /// <param name="killer">The player.</param>
    /// <param name="entity">The player's entity.</param>
    /// <param name="weapon">The weapon's key, or empty.</param>
    /// <param name="head">Whether the round struck the head.</param>
    /// <returns>The note.</returns>
    public static KillNote OfGuard(MatchPlayer killer, int entity, string weapon, bool head)
    {
        ArgumentNullException.ThrowIfNull(killer);
        return new KillNote(
            killer.Name, killer.Team, GuardName, -1, entity, -1, weapon ?? string.Empty, KillCause.Weapon, KillMarks.Guard | (head ? KillMarks.Headshot : KillMarks.None));
    }

    /// <summary>Writes the note, for the clients.</summary>
    /// <param name="writer">Where it goes.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteString(Killer ?? string.Empty, 32);
        writer.WriteRanged(Math.Clamp(KillerTeam, -1, 14), -1, 14);
        writer.WriteString(Victim ?? string.Empty, 32);
        writer.WriteRanged(Math.Clamp(VictimTeam, -1, 14), -1, 14);
        writer.WriteRanged(Math.Clamp(KillerEntity, -1, 1022), -1, 1022);
        writer.WriteRanged(Math.Clamp(VictimEntity, -1, 1022), -1, 1022);
        writer.WriteString(Weapon ?? string.Empty, MaxWeaponBytes);
        writer.WriteBits((uint)Cause & 7u, 3);
        writer.WriteBits((uint)Marks & 15u, 4);
    }

    /// <summary>Reads a note a server sent.</summary>
    /// <param name="reader">What arrived.</param>
    /// <returns>The note.</returns>
    public static KillNote Read(ref BitReader reader)
    {
        string killer = reader.ReadString(32);
        int killerTeam = reader.ReadRanged(-1, 14);
        string victim = reader.ReadString(32);
        int victimTeam = reader.ReadRanged(-1, 14);
        int killerEntity = reader.ReadRanged(-1, 1022);
        int victimEntity = reader.ReadRanged(-1, 1022);
        string weapon = reader.ReadString(MaxWeaponBytes);
        uint cause = reader.ReadBits(3);
        KillMarks marks = (KillMarks)reader.ReadBits(4);
        return new KillNote(
            killer, killerTeam, victim, victimTeam, killerEntity, victimEntity, weapon,
            cause <= (uint)KillCause.Suicide ? (KillCause)cause : KillCause.World, marks);
    }
}

/// <summary>A kill still listed, and how long it has been.</summary>
/// <param name="Note">The kill.</param>
/// <param name="Age">Seconds since it was listed.</param>
/// <param name="Life">Seconds it is listed for in all.</param>
public readonly record struct KillFeedEntry(KillNote Note, double Age, double Life);

/// <summary>The kills of late, newest last: a few, each for a few seconds; a player's own a while longer.</summary>
public sealed class KillFeed
{
    /// <summary>The most notes listed at once.</summary>
    public const int Kept = 5;

    /// <summary>How long a note is listed, in seconds.</summary>
    public const double ShownSeconds = 6;

    /// <summary>How long a note the player is in is listed, in seconds.</summary>
    public const double OwnShownSeconds = 9;

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
    public IReadOnlyList<KillNote> Shown(double now) => [.. Entries(now, own: -1).Select(static e => e.Note)];

    /// <summary>Gets the kills still listed, each with its age: one the player is in kept longer.</summary>
    /// <param name="now">The time, in seconds, on the clock the kills were added by.</param>
    /// <param name="own">The player's own entity, or -1.</param>
    /// <returns>Them, oldest first.</returns>
    public IReadOnlyList<KillFeedEntry> Entries(double now, int own)
    {
        // Kept as long as any player's view of it lasts; each view filters by its own.
        _notes.RemoveAll(n => now - n.At >= OwnShownSeconds);
        List<KillFeedEntry> shown = new(_notes.Count);
        foreach ((KillNote note, double at) in _notes)
        {
            double life = LifeOf(note, own);
            if (now - at < life)
            {
                shown.Add(new KillFeedEntry(note, Math.Max(0.0, now - at), life));
            }
        }

        return shown;
    }

    /// <summary>Gets how long a note is listed for a player.</summary>
    /// <param name="note">The note.</param>
    /// <param name="own">The player's own entity, or -1.</param>
    /// <returns>Seconds.</returns>
    public static double LifeOf(KillNote note, int own) =>
        own >= 0 && (note.KillerEntity == own || note.VictimEntity == own) ? OwnShownSeconds : ShownSeconds;
}
