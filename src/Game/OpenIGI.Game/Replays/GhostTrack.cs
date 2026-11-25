using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Replays;

/// <summary>What happened along a run that a race against it marks: an objective, a kill, the alarm, the end.</summary>
public enum GhostEventKind : byte
{
    /// <summary>An objective was done; its detail is the objective's slot.</summary>
    Objective = 1,

    /// <summary>The runner killed someone; its detail is the one killed's team.</summary>
    Kill = 2,

    /// <summary>The alarm went up (detail 1) or down (detail 0).</summary>
    Alarm = 3,

    /// <summary>The mission ended; its detail is 1 completed, 0 failed.</summary>
    End = 4,

    /// <summary>A cutscene began (detail 1) or ended (detail 0): the ticks between are not played, and no race counts them.</summary>
    Cutscene = 5,
}

/// <summary>Something that happened along a run, on the tick it happened.</summary>
/// <param name="Tick">The level's tick.</param>
/// <param name="Kind">What it was.</param>
/// <param name="Detail">What of it: see <see cref="GhostEventKind"/>.</param>
public readonly record struct GhostEvent(int Tick, GhostEventKind Kind, int Detail);

/// <summary>
/// A run's runner as a ghost shows it: the body tick by tick, as a co-op partner's copy is drawn from it
/// (<see cref="ReplicatedHuman"/>), and what happened along the way. Drawn only, never part of the level a ghost is raced in.
/// </summary>
/// <remarks>
/// Written beside the inputs as the run is played, and, for a ranked run, by the server that checks it: a race against a
/// board's run shows the server's, which the runner cannot have made up.
/// </remarks>
public sealed class GhostTrack
{
    /// <summary>The most ticks a track holds: two hours, as a replay's inputs do.</summary>
    public const int MaxTicks = InputTape.MaxTicks;

    /// <summary>The most events a track holds.</summary>
    public const int MaxEvents = 65536;

    private readonly List<ReplicatedHuman> _frames = [];
    private readonly List<GhostEvent> _events = [];

    /// <summary>Gets how many ticks the track holds.</summary>
    public int Length => _frames.Count;

    /// <summary>Gets what happened along the run, in the order it happened.</summary>
    public IReadOnlyList<GhostEvent> Events => _events;

    /// <summary>Gets a value indicating whether the track holds as many ticks as one may.</summary>
    public bool IsFull => _frames.Count >= MaxTicks;

    /// <summary>Sets the runner's body on a tick; ticks skipped before it hold the body as it last was.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="frame">The body.</param>
    public void Set(int tick, in ReplicatedHuman frame)
    {
        if (tick < 0 || tick >= MaxTicks)
        {
            return;
        }

        ReplicatedHuman held = _frames.Count > 0 ? _frames[^1] : frame;
        while (_frames.Count < tick)
        {
            _frames.Add(held);
        }

        if (tick < _frames.Count)
        {
            _frames[tick] = frame;
        }
        else
        {
            _frames.Add(frame);
        }
    }

    /// <summary>The runner's body on a tick, or <see langword="null"/> past the track.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The body.</returns>
    public ReplicatedHuman? At(int tick) => tick >= 0 && tick < _frames.Count ? _frames[tick] : null;

    /// <summary>Notes something that happened, on its tick.</summary>
    /// <param name="tick">The tick.</param>
    /// <param name="kind">What it was.</param>
    /// <param name="detail">What of it.</param>
    public void Event(int tick, GhostEventKind kind, int detail = 0)
    {
        if (_events.Count < MaxEvents && tick >= 0)
        {
            _events.Add(new GhostEvent(tick, kind, detail));
        }
    }

    /// <summary>
    /// Gets how many of the ticks before one were played rather than watched: the tick less those a cutscene held. A race and the
    /// boards count a run's time so, a scene watched to its end costing no more than one skipped.
    /// </summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The ticks played before it.</returns>
    public int PlayTicksAt(int tick)
    {
        int watched = 0;
        foreach ((int start, int end) in Cutscenes())
        {
            watched += Math.Max(0, Math.Min(end, tick) - start);
        }

        return tick - watched;
    }

    /// <summary>Gets the tick on which as many ticks had been played as given: the inverse of <see cref="PlayTicksAt"/>.</summary>
    /// <param name="played">The ticks played.</param>
    /// <returns>The tick; one a cutscene never let go of is where it began.</returns>
    public int TickAtPlay(int played)
    {
        int tick = Math.Max(0, played);
        foreach ((int start, int end) in Cutscenes())
        {
            if (start > tick)
            {
                break;
            }

            if (end == int.MaxValue)
            {
                return start;
            }

            tick += end - start;
        }

        return tick;
    }

    // The spans of ticks cutscenes held, each from the tick it began to the tick it ended; one not ended runs on.
    private List<(int Start, int End)> Cutscenes()
    {
        List<(int Start, int End)> spans = [];
        int? began = null;
        foreach (GhostEvent scene in _events)
        {
            if (scene.Kind != GhostEventKind.Cutscene)
            {
                continue;
            }

            if (scene.Detail != 0)
            {
                began ??= scene.Tick;
            }
            else if (began is { } start)
            {
                spans.Add((start, Math.Max(start, scene.Tick)));
                began = null;
            }
        }

        if (began is { } open)
        {
            spans.Add((open, int.MaxValue));
        }

        return spans;
    }

    /// <summary>The ticks the run's objectives were done on, in the order they were done: a race's splits.</summary>
    /// <returns>Each objective's slot and the tick it was done on, its first time only.</returns>
    public IReadOnlyList<(int Slot, int Tick)> Splits()
    {
        List<(int Slot, int Tick)> splits = [];
        foreach (GhostEvent done in _events)
        {
            if (done.Kind == GhostEventKind.Objective && !splits.Exists(s => s.Slot == done.Detail))
            {
                splits.Add((done.Detail, done.Tick));
            }
        }

        return splits;
    }
}
