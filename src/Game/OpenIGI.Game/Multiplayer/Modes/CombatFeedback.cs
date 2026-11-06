using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>The hit marker: the local player's rounds struck someone a moment ago.</summary>
/// <param name="Strength">How much of it is left, from 1 as it lands to 0 as it fades.</param>
/// <param name="Killed">Whether the hit killed.</param>
/// <param name="Head">Whether it was to the head.</param>
public readonly record struct HitMarker(float Strength, bool Killed, bool Head);

/// <summary>Where a shot that struck the local player came from, as a mark round the middle of the screen.</summary>
/// <param name="Angle">Its bearing from the view's forward, in radians, clockwise: 0 straight ahead, π behind.</param>
/// <param name="Strength">How much of it is left, from 1 to 0, already weighted by how hard the shot struck.</param>
public readonly record struct DamageMark(float Angle, float Strength);

/// <summary>The local player's round in numbers, shown as the round ends.</summary>
/// <param name="Dealt">The damage it dealt to other players, in whole healths.</param>
/// <param name="Taken">The damage it took from them.</param>
/// <param name="Hits">The hits it scored, on players and the level's soldiers.</param>
public readonly record struct RoundReport(float Dealt, float Taken, int Hits);

/// <summary>What the HUD shows of the local player's fight this moment.</summary>
/// <param name="Marker">The hit marker, or <see langword="null"/> for none.</param>
/// <param name="Marks">Where the shots that struck it came from.</param>
/// <param name="Report">The round's damage report, once the round is over; <see langword="null"/> while it is played.</param>
public sealed record CombatHud(HitMarker? Marker, IReadOnlyList<DamageMark> Marks, RoundReport? Report = null);

/// <summary>
/// The local player's combat feedback, built from the session's <see cref="CombatNote"/>s: a hit marker as its rounds strike
/// (red for a kill, larger for the head), and a mark round the screen's middle for each shot that strikes it, pointing where
/// the shot came from as the player turns, fading as it goes. It also adds up the round's damage dealt and taken.
/// </summary>
/// <remarks>Shared by the listen host and every client, and by both games: the notes carry everything it needs.</remarks>
public sealed class CombatFeedback
{
    /// <summary>How long a hit marker shows, in seconds; a kill's twice as long.</summary>
    public const double MarkerSeconds = 0.25;

    /// <summary>How long a damage mark shows, in seconds.</summary>
    public const double MarkSeconds = 1.6;

    private readonly List<(Vector3d From, float Weight, double At)> _struck = [];
    private (double At, bool Killed, bool Head)? _hit;
    private int _round = -1;

    /// <summary>Gets the damage the local player dealt to other players since <see cref="ResetTally"/>, in whole healths.</summary>
    public float Dealt { get; private set; }

    /// <summary>Gets the damage it took from other players since <see cref="ResetTally"/>, in whole healths.</summary>
    public float Taken { get; private set; }

    /// <summary>Gets the hits it scored since <see cref="ResetTally"/>.</summary>
    public int Hits { get; private set; }

    /// <summary>Takes in the notes the session told of.</summary>
    /// <param name="notes">The notes.</param>
    /// <param name="own">The local player's entity.</param>
    /// <param name="now">The clock, in seconds.</param>
    public void Take(IReadOnlyList<CombatNote> notes, int own, double now)
    {
        ArgumentNullException.ThrowIfNull(notes);
        foreach (CombatNote note in notes)
        {
            if (note.Attacker == own && note.Victim != own)
            {
                _hit = (now, note.Killed || (_hit is { } last && now - last.At < MarkerSeconds && last.Killed), note.Head);
                Hits++;
                Dealt += note.Victim >= 0 ? note.Damage : 0f;
            }

            if (note.Victim == own && note.Attacker != own)
            {
                _struck.Add((note.From, Math.Clamp(0.45f + (note.Damage * 2f), 0.45f, 1f), now));
                Taken += note.Damage;
            }
        }

        _struck.RemoveAll(s => now - s.At > MarkSeconds);
    }

    /// <summary>Starts the round's tally of damage dealt and taken again.</summary>
    public void ResetTally()
    {
        Dealt = 0;
        Taken = 0;
        Hits = 0;
    }

    /// <summary>Gets what the HUD shows now, for a view at a place looking a way.</summary>
    /// <param name="now">The clock, in seconds.</param>
    /// <param name="eye">Where the view is, in world units.</param>
    /// <param name="view">Which way it looks: rows right, up and forward.</param>
    /// <param name="match">Where the match stands, or <see langword="null"/>: a new round starts the tally again, and a round
    /// over shows it.</param>
    /// <returns>The HUD, or <see langword="null"/> while there is nothing to show.</returns>
    public CombatHud? Shown(double now, Vector3d eye, Matrix3f view, MatchState? match = null)
    {
        if (match is not null && match.Round != _round)
        {
            _round = match.Round;
            ResetTally();
        }

        RoundReport? report = match?.Phase is MatchPhase.RoundOver or MatchPhase.Intermission && (Hits > 0 || Taken > 0)
            ? new RoundReport(Dealt, Taken, Hits)
            : null;

        HitMarker? marker = null;
        if (_hit is { } hit)
        {
            double life = hit.Killed ? MarkerSeconds * 2 : MarkerSeconds;
            double age = now - hit.At;
            marker = age < life ? new HitMarker((float)(1 - (age / life)), hit.Killed, hit.Head) : null;
        }

        List<DamageMark> marks = [];
        foreach ((Vector3d from, float weight, double at) in _struck)
        {
            double age = now - at;
            if (age >= MarkSeconds)
            {
                continue;
            }

            marks.Add(new DamageMark(BearingOf(from, eye, view), weight * (float)(1 - (age / MarkSeconds))));
        }

        return marker is null && marks.Count == 0 && report is null ? null : new CombatHud(marker, marks, report);
    }

    /// <summary>Gets the bearing of a place from a view, flat on the ground: 0 ahead, clockwise, π behind.</summary>
    /// <param name="from">The place.</param>
    /// <param name="eye">The view's place.</param>
    /// <param name="view">The view: rows right, up and forward.</param>
    /// <returns>The bearing, in radians, in <c>(-π, π]</c>.</returns>
    public static float BearingOf(Vector3d from, Vector3d eye, Matrix3f view)
    {
        double dx = from.X - eye.X;
        double dy = from.Y - eye.Y;
        Vector3f right = view.Row1;
        Vector3f forward = view.Row3;

        // Flattened to the ground: a shot from above or below still points round the screen, not off it.
        double side = (dx * right.X) + (dy * right.Y);
        double ahead = (dx * forward.X) + (dy * forward.Y);
        return side == 0 && ahead == 0 ? 0f : (float)Math.Atan2(side, ahead);
    }
}
