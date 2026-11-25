using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Replays;

/// <summary>
/// How a race against a ghost stands: where along the ghost's route the player is now, so how much sooner or later than the
/// ghost they got there; and the splits, the objectives both did, each by how much sooner or later.
/// </summary>
/// <remarks>
/// The player is found on the ghost's route as the point of it nearest where they stand, sought a little behind the last match
/// and some seconds ahead of it, so a route that crosses itself is followed rather than jumped along; where the ghost stood still,
/// so that many of its moments are as near, the one nearest now is taken -- two runs standing together read level, and one left
/// standing where the ghost has gone on falls behind as it waits. Off the route -- further
/// from it than <see cref="OffRouteUnits"/> -- there is no gap to tell; the whole route is sought again now and then, so a player
/// who takes another way and comes back onto it is found again.
/// </remarks>
/// <param name="ghost">The ghost's track.</param>
public sealed class RaceClock(GhostTrack ghost)
{
    /// <summary>How far from the ghost's route the player may stand and still be on it: fifteen metres.</summary>
    public const double OffRouteUnits = 15.0 * 4096.0;

    // How far behind the last match and ahead of it the route is sought, in the ghost's ticks; and how often, off the route, the
    // whole of it is.
    private const int SoughtBehind = 15;
    private const int SoughtAhead = 150;
    private const int WholeRouteEvery = 30;

    // How much further than the nearest a point of the route may be and still count as as near: a quarter of a metre.
    private const double AsNearUnits = 1024.0;

    private readonly GhostTrack _ghost = ghost ?? throw new ArgumentNullException(nameof(ghost));
    private int _matched;
    private int _offSince = -1;

    /// <summary>Gets how much later than the ghost the player reached where they stand, in seconds, negative sooner; null off its route.</summary>
    public double? DeltaSeconds { get; private set; }

    /// <summary>Takes where the player stands after a tick.</summary>
    /// <param name="played">The ticks the player has played, those a cutscene held left out (<see cref="GhostTrack.PlayTicksAt"/>).</param>
    /// <param name="runner">The player's body, as a ghost of them would be recorded.</param>
    /// <remarks>Both runs are timed in the ticks they played: a scene one watched longer than the other puts neither behind.</remarks>
    public void Ran(int played, in ReplicatedHuman runner)
    {
        int tick = _ghost.TickAtPlay(played);
        if (_ghost.Length == 0)
        {
            DeltaSeconds = null;
            return;
        }

        (int found, double distance) = Nearest(runner.Position, Math.Max(0, _matched - SoughtBehind), Math.Min(_ghost.Length - 1, _matched + SoughtAhead), tick);
        if (distance > OffRouteUnits && (_offSince < 0 || tick - _offSince >= WholeRouteEvery))
        {
            _offSince = tick;
            (found, distance) = Nearest(runner.Position, 0, _ghost.Length - 1, tick);
        }

        if (distance > OffRouteUnits)
        {
            DeltaSeconds = null;
            return;
        }

        _offSince = -1;
        _matched = found;
        DeltaSeconds = (played - _ghost.PlayTicksAt(found)) / (double)Scoring.MissionScore.TicksPerSecond;
    }

    /// <summary>The splits of a race: each objective both runs did, in the order the player did them, and the finish where both completed.</summary>
    /// <param name="player">The player's own run as its ghost is recorded, with what happened along it.</param>
    /// <param name="ghost">The ghost's track.</param>
    /// <returns>Each split's objective slot (or -1 for the finish), and the ticks the player and the ghost had played by it.</returns>
    public static IReadOnlyList<(int Slot, int Tick, int GhostTick)> Splits(GhostTrack player, GhostTrack ghost)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(ghost);
        IReadOnlyList<(int Slot, int Tick)> theirs = ghost.Splits();
        List<(int Slot, int Tick, int GhostTick)> splits = [];
        foreach ((int slot, int tick) in player.Splits())
        {
            int at = theirs.ToList().FindIndex(s => s.Slot == slot);
            if (at >= 0)
            {
                splits.Add((slot, player.PlayTicksAt(tick), ghost.PlayTicksAt(theirs[at].Tick)));
            }
        }

        if (Finish(player) is { } ours && Finish(ghost) is { } ghosts)
        {
            splits.Add((-1, player.PlayTicksAt(ours), ghost.PlayTicksAt(ghosts)));
        }

        return splits;
    }

    // The tick a run completed its mission on, or null.
    private static int? Finish(GhostTrack track)
    {
        foreach (GhostEvent happened in track.Events)
        {
            if (happened is { Kind: GhostEventKind.End, Detail: 1 })
            {
                return happened.Tick;
            }
        }

        return null;
    }

    // The point of the route nearest a place, within a span of its ticks -- of those as near, the one nearest now: across the
    // ground, and up and down twice as much, so a floor above or below is never taken for this one.
    private (int Tick, double Distance) Nearest(Int3 at, int from, int to, int now)
    {
        double least = double.MaxValue;
        for (int tick = from; tick <= to; tick++)
        {
            least = Math.Min(least, Distance(at, tick));
        }

        int best = -1;
        for (int tick = from; tick <= to; tick++)
        {
            if (Distance(at, tick) <= least + AsNearUnits && (best < 0 || Math.Abs(tick - now) < Math.Abs(best - now)))
            {
                best = tick;
            }
        }

        return (best, least);
    }

    private double Distance(Int3 at, int tick)
    {
        Int3 there = _ghost.At(tick)!.Value.Position;
        double dx = there.X - at.X;
        double dy = there.Y - at.Y;
        double dz = (there.Z - at.Z) * 2.0;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
