using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>Where a match stands.</summary>
public enum MatchPhase : byte
{
    /// <summary>Players gather and play, scoring nothing, until the match begins.</summary>
    Warmup,

    /// <summary>The match is being played.</summary>
    Live,

    /// <summary>A round was decided; the next begins after a pause.</summary>
    RoundOver,

    /// <summary>The match was decided; the scores are shown before the next map.</summary>
    Intermission,
}

/// <summary>A match's progress: its phase, when the phase ends, the round, and who won what was last decided.</summary>
/// <remarks>
/// A mode moves it on; the session sends it to every client whenever it changes (<see cref="Version"/>), so the clients' clocks
/// and banners follow the server's without a message every tick.
/// </remarks>
/// <param name="tickRate">The world's ticks per second.</param>
public sealed class MatchFlow(int tickRate)
{
    /// <summary>The tick a phase ends on when it has no clock.</summary>
    public const long Untimed = long.MaxValue;

    /// <summary>Gets the phase.</summary>
    public MatchPhase Phase { get; private set; } = MatchPhase.Live;

    /// <summary>Gets the tick the phase ends on, or <see cref="Untimed"/>.</summary>
    public long EndsAt { get; private set; } = Untimed;

    /// <summary>Gets the round being played, from 1.</summary>
    public int Round { get; private set; } = 1;

    /// <summary>Gets who won what was last decided: a team, a player's slot in a free-for-all, or -1 for nobody (a draw).</summary>
    public int Winner { get; private set; } = -1;

    /// <summary>Gets a number raised by every change, which says when the clients need telling.</summary>
    public int Version { get; private set; }

    /// <summary>Moves to a phase.</summary>
    /// <param name="phase">The phase.</param>
    /// <param name="tick">The tick it begins on.</param>
    /// <param name="seconds">How long it lasts, or zero or less for no clock.</param>
    /// <param name="winner">Who won, for a decided phase; -1 otherwise.</param>
    public void Enter(MatchPhase phase, long tick, double seconds, int winner = -1)
    {
        if (phase == MatchPhase.Live && Phase == MatchPhase.RoundOver)
        {
            Round++;
        }
        else if (phase is MatchPhase.Warmup || (phase == MatchPhase.Live && Phase == MatchPhase.Intermission))
        {
            Round = 1;
        }

        Phase = phase;
        EndsAt = seconds > 0 ? tick + (long)Math.Ceiling(seconds * tickRate) : Untimed;
        Winner = phase is MatchPhase.RoundOver or MatchPhase.Intermission ? winner : -1;
        Version++;
    }

    /// <summary>Brings the phase's end nearer, to a few seconds from now, when it is further off: a vote everyone has answered.</summary>
    /// <param name="tick">The tick now.</param>
    /// <param name="seconds">How long the phase may still last.</param>
    public void Hasten(long tick, double seconds)
    {
        long by = tick + (long)Math.Ceiling(seconds * tickRate);
        if (EndsAt != Untimed && EndsAt > by)
        {
            EndsAt = by;
            Version++;
        }
    }

    /// <summary>Gets whether the phase's clock has run out.</summary>
    /// <param name="tick">The tick now.</param>
    /// <returns><see langword="true"/> once the phase's end has come.</returns>
    public bool IsDue(long tick) => EndsAt != Untimed && tick >= EndsAt;

    /// <summary>Gets the phase's seconds left, or <see langword="null"/> when it has no clock.</summary>
    /// <param name="tick">The tick now.</param>
    /// <returns>The seconds, never below zero.</returns>
    public double? SecondsLeft(long tick) => EndsAt == Untimed ? null : Math.Max(0, (EndsAt - tick) / (double)tickRate);

    /// <summary>Writes where the match stands, for the clients.</summary>
    /// <param name="writer">Where it goes.</param>
    /// <param name="mode">The mode's id.</param>
    /// <param name="tick">The tick now.</param>
    /// <param name="winnerName">The winner's name as players read it (a team's, a player's), or empty.</param>
    /// <param name="report">What the mode tells of itself, or <see langword="null"/> for nothing.</param>
    public void Write(BitWriter writer, string mode, long tick, string winnerName = "", ModeReport? report = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteString(mode, 16);
        writer.WriteBits((uint)Phase, 8);
        writer.WriteRanged(Math.Clamp(Round, 0, 255), 0, 255);
        writer.WriteRanged(Math.Clamp(Winner, -1, 254), -1, 254);
        double? left = SecondsLeft(tick);
        writer.WriteBool(left is not null);
        if (left is { } seconds)
        {
            writer.WriteRanged((int)Math.Min(Math.Ceiling(seconds * 10), 65535), 0, 65535);
        }

        writer.WriteString(winnerName ?? string.Empty, 32);
        (report ?? ModeReport.None).Write(writer);
    }

    /// <summary>Reads where a server's match stands.</summary>
    /// <param name="reader">What arrived.</param>
    /// <returns>It, as the client keeps it.</returns>
    public static MatchState Read(ref BitReader reader)
    {
        string mode = reader.ReadString(16);
        MatchPhase phase = (MatchPhase)reader.ReadBits(8);
        int round = reader.ReadRanged(0, 255);
        int winner = reader.ReadRanged(-1, 254);
        double? left = reader.ReadBool() ? reader.ReadRanged(0, 65535) / 10.0 : null;
        string winnerName = reader.ReadString(32);
        ModeReport report = ModeReport.Read(ref reader);
        return new MatchState(mode, phase, round, winner, left, winnerName, report.Key == ModeReport.None.Key ? null : report);
    }
}

/// <summary>Where a server's match stood when it last said: as a client shows it.</summary>
/// <param name="Mode">The mode's id.</param>
/// <param name="Phase">The phase.</param>
/// <param name="Round">The round, from 1.</param>
/// <param name="Winner">Who won what was last decided, or -1.</param>
/// <param name="SecondsLeft">The phase's seconds left when it was said, or <see langword="null"/> for no clock.</param>
/// <param name="WinnerName">The winner's name as players read it, or empty.</param>
/// <param name="Report">What the mode tells of itself (a bomb planted, who is defusing), or <see langword="null"/> for nothing.</param>
public sealed record MatchState(string Mode, MatchPhase Phase, int Round, int Winner, double? SecondsLeft, string WinnerName = "", ModeReport? Report = null)
{
    /// <summary>Gets the mode's own line on the round in play: a bomb planted and its fuse, as of the seconds since it was said.</summary>
    /// <param name="secondsSince">The seconds since the state was said.</param>
    /// <returns>The line, or empty.</returns>
    public string Status(double secondsSince) =>
        Report is { Planted.Length: > 0 } bomb ? $"BOMB PLANTED AT {bomb.Planted}  ·  {Clock(Math.Max(0, bomb.FuseLeft - secondsSince))}" : string.Empty;

    /// <summary>Gets the name a team is shown by.</summary>
    /// <param name="team">The team.</param>
    /// <returns>Its name.</returns>
    public static string TeamName(int team) => team == TeamRoster.Good ? "Team A" : team == TeamRoster.Bad ? "Team B" : "Nobody";

    /// <summary>Gets what a player is shown of the match: its mode and clock, and once decided, who won.</summary>
    /// <param name="secondsSince">The seconds since the state was said, which its clock has run down by.</param>
    /// <returns>The banner, or <see langword="null"/> for a match with nothing to show (co-op).</returns>
    public MatchBanner? Banner(double secondsSince)
    {
        if (Mode == GameModes.Coop || string.IsNullOrEmpty(Mode))
        {
            return null;
        }

        string? clock = SecondsLeft is { } left ? Clock(Math.Max(0, left - secondsSince)) : null;
        string title = GameModes.Title(Mode);
        return Phase switch
        {
            MatchPhase.Warmup => new MatchBanner("WARMUP", clock, "The match begins when the clock runs out"),
            MatchPhase.Intermission or MatchPhase.RoundOver => new MatchBanner(
                title, clock, Winner < 0 || WinnerName.Length == 0 ? "DRAW" : $"{WinnerName.ToUpperInvariant()} WINS"),
            _ when Round > 0 && Mode is GameModes.Elimination or GameModes.SearchAndDestroy =>
                new MatchBanner($"{title}  ·  ROUND {Round}", clock, Status(secondsSince) is { Length: > 0 } status ? status : null),
            _ => new MatchBanner(title, clock, null),
        };
    }

    /// <summary>Gets seconds as a clock reads them, as <c>1:05</c>.</summary>
    /// <param name="seconds">The seconds.</param>
    /// <returns>The clock.</returns>
    public static string Clock(double seconds)
    {
        int whole = (int)Math.Ceiling(seconds);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{whole / 60}:{whole % 60:00}");
    }
}

/// <summary>What a player is shown of the match across the top of the screen.</summary>
/// <param name="Title">The mode, or the phase while warming up.</param>
/// <param name="Clock">The phase's time left, as <c>7:32</c>, or <see langword="null"/>.</param>
/// <param name="Headline">A decided match's result, or a line of explanation; <see langword="null"/> while playing.</param>
public sealed record MatchBanner(string Title, string? Clock, string? Headline);
