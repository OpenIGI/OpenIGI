namespace OpenIGI.Game.Multiplayer;

/// <summary>The server's queue of one client's commands, run each exactly once and in the order the client made them.</summary>
/// <remarks>
/// <para>
/// A client makes one command a tick and predicts its own player on it. The server must run that same sequence, no command
/// twice, none left out and none made up, or its copy of the player goes somewhere the client never went and the client is
/// corrected: the "step back". So nothing here is keyed to the server's own tick. Commands arrive several to a packet,
/// repeated, early and out of order; the queue keeps each once and hands them out in order.
/// </para>
/// <para>
/// The arrival of packets jitters, so a queue that ran a command the moment one arrived would starve every other tick. It
/// waits, as Source's does, until <see cref="TargetDepth"/> are queued before it starts (and again after it ran dry), and
/// from then gives one a tick. A client whose clock runs a little fast fills it; past <see cref="MaxDepth"/> it gives two a
/// tick until it is back down. Starved, it gives none: the player waits for its client rather than move on controls nobody
/// pressed, and the ticks it waited are counted in <see cref="Missed"/>.
/// </para>
/// <para>
/// Catching up is paid for. Each tick of the server's earns the client one command's run, kept up to <see cref="MaxCredit"/>,
/// and each command run spends one: a client that fell behind (its packets held up, then arriving together) catches up
/// on what it saved, but one sending commands faster than time passes -- a speed hack -- runs no faster than the server's
/// clock, and what it sends beyond that waits until the queue is full and is then refused. This is Source's
/// <c>sv_maxusrcmdprocessticks</c>.
/// </para>
/// <para>
/// A backlog the credit cannot pay off is dropped, oldest first, down to <see cref="TargetDepth"/>: a client that stalled for
/// a second or two (its window held, its line stopped) and then sent everything it made meanwhile would otherwise have its
/// every later command run that much late for the rest of the session, two catch-up runs a tick being all the credit
/// ever allows. What is dropped is stale already; the client's prediction is corrected once to where its player stands.
/// </para>
/// </remarks>
public sealed class UserCommandBuffer
{
    private readonly SortedDictionary<long, (float[] Channels, long ViewTick, float ViewFraction)> _pending = [];
    private readonly float[] _last;
    private bool _primed;
    private double _credit;

    /// <summary>Initializes a new instance of the <see cref="UserCommandBuffer"/> class.</summary>
    /// <param name="channelCount">The title's channel frame length.</param>
    /// <param name="maxQueued">How many commands may wait before more are refused, so a client cannot fill the server.</param>
    public UserCommandBuffer(int channelCount, int maxQueued = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxQueued, 1);
        _last = new float[channelCount];
        MaxQueued = maxQueued;
    }

    /// <summary>Gets how many commands may wait before more are refused.</summary>
    public int MaxQueued { get; }

    /// <summary>Gets or sets how many commands the queue waits for before it starts handing them out.</summary>
    public int TargetDepth { get; set; } = 2;

    /// <summary>Gets or sets past how many waiting commands the queue gives two a tick.</summary>
    public int MaxDepth { get; set; } = 6;

    /// <summary>Gets or sets the most commands' runs a client may save up to catch up with: a third of a second at the original's tick.</summary>
    public int MaxCredit { get; set; } = 10;

    /// <summary>Gets or sets how many ticks running a backlog over <see cref="MaxDepth"/> the credit cannot pay off waits before it is dropped: half a second.</summary>
    public int StaleBacklogTicks { get; set; } = 15;

    /// <summary>Gets the commands dropped as a backlog too stale to run.</summary>
    public long Dropped { get; private set; }

    // How many ticks in a row the queue has been over its depth with too little credit to give two.
    private int _backlogTicks;

    /// <summary>Gets the ticks the queue would have given two but the client had not earned them: a client sending faster than time.</summary>
    public long Throttled { get; private set; }

    /// <summary>Gets the ticks the queue had nothing to give.</summary>
    public long Missed { get; private set; }

    /// <summary>Gets the commands that came with values that are no numbers, which were cleared.</summary>
    public long Unfit { get; private set; }

    /// <summary>Gets the commands that arrived after one made later had already run.</summary>
    public long Late { get; private set; }

    /// <summary>Gets the stamp of the newest command run, or <see cref="long.MinValue"/> before the first.</summary>
    public long LastTakenTick { get; private set; } = long.MinValue;

    /// <summary>Gets the tick the client was drawing at when it made the command last run, or <see cref="long.MinValue"/>.</summary>
    /// <remarks>
    /// As far back from the server's present (<see cref="ServerTick"/>) as the client's line explains and no further
    /// (<see cref="MaxRewindTicks"/>, and the commands that waited here before it): a client claiming to have drawn the
    /// others further in the past than that is shooting at where they were, not where it saw them -- a backtrack -- and is held
    /// to what its line allows. Never ahead of the server's present. The command's own stamp is no measure: after the first,
    /// a client numbers its commands on one a tick, and its clock and the server's drift apart over a session.
    /// </remarks>
    public long LastViewTick { get; private set; } = long.MinValue;

    /// <summary>Gets how far past <see cref="LastViewTick"/> towards the next its client was drawing, 0 to 1.</summary>
    public float LastViewFraction { get; private set; }

    /// <summary>Gets or sets how far behind the server's present a client may have been drawing the others, in ticks; the session sets it from the client's line.</summary>
    public int MaxRewindTicks { get; set; } = int.MaxValue;

    /// <summary>Gets or sets the tick the server is running, which the session sets before each; <see cref="long.MinValue"/> while unknown.</summary>
    public long ServerTick { get; set; } = long.MinValue;

    // How far behind the server the client's views usually are, in ticks (an average of the believed ones; negative until
    // the first), and how much further one may reach and still be believed: about Source's 200 ms.
    private double _typicalViewAge;
    private const int ViewAgeToleranceTicks = 6;

    // The client's first views, a second's worth, are what its usual age is learned from before any is held.
    private int _viewsSeen;
    private const int LearnedViews = 30;

    /// <summary>Gets the commands whose view tick reached back further than the client's line explains, and were held to it.</summary>
    public long Backtracked { get; private set; }

    /// <summary>Gets the stamp of the command that arrived last (a packet carries its newest last).</summary>
    public long NewestTick { get; private set; } = long.MinValue;

    /// <summary>Gets how many commands wait to be run.</summary>
    public int Buffered => _pending.Count;

    /// <summary>Takes a command that arrived.</summary>
    /// <param name="command">The command.</param>
    public void Push(in UserCommand command)
    {
        if (command.Channels is null || command.Channels.Length != _last.Length)
        {
            return;
        }

        // A value that is no number would poison whatever the player's code does with it, on the server's level.
        float[] channels = command.Channels;
        if (Array.Exists(channels, static c => !float.IsFinite(c)))
        {
            channels = Array.ConvertAll(channels, static c => float.IsFinite(c) ? c : 0f);
            Unfit++;
        }

        // A repeat of one already run is what the packets' redundancy sends; one older than the newest run came too late.
        if (command.Tick <= LastTakenTick)
        {
            if (command.Tick < LastTakenTick - 8)
            {
                Late++;
            }

            return;
        }

        if (_pending.Count >= MaxQueued)
        {
            return;
        }

        _pending.TryAdd(command.Tick, (channels, command.ViewTick, float.IsFinite(command.ViewFraction) ? Math.Clamp(command.ViewFraction, 0f, 1f) : 0f));
        NewestTick = command.Tick;
    }

    /// <summary>Gets how many commands to run this tick, and counts a tick the queue is dry.</summary>
    /// <returns>Nought, one, or two while catching up.</returns>
    /// <remarks>Called once a server tick: that is what earns the client its runs.</remarks>
    public int Due()
    {
        _credit = Math.Min(_credit + 1, MaxCredit);
        if (!_primed)
        {
            if (_pending.Count < TargetDepth)
            {
                Missed += LastTakenTick == long.MinValue ? 0 : 1;
                return 0;
            }

            _primed = true;
        }

        if (_pending.Count == 0)
        {
            _primed = false;
            Missed++;
            return 0;
        }

        // A backlog the credit cannot pay off, held half a second: dropped to the depth the queue keeps, oldest first.
        if (_pending.Count > MaxDepth && _credit < 2)
        {
            if (++_backlogTicks >= StaleBacklogTicks)
            {
                DropBacklog();
            }
        }
        else
        {
            _backlogTicks = 0;
        }

        int wanted = _pending.Count > MaxDepth ? 2 : 1;
        int due = Math.Min(wanted, (int)Math.Floor(_credit));
        if (due < wanted)
        {
            Throttled++;
        }

        _credit -= due;
        return due;
    }

    // Drops the oldest waiting commands until the queue's own depth is left. Each counts as run, so its repeats in later
    // packets are not taken again, and what it held down is what is held down after it.
    private void DropBacklog()
    {
        _backlogTicks = 0;
        while (_pending.Count > TargetDepth)
        {
            KeyValuePair<long, (float[] Channels, long ViewTick, float ViewFraction)> oldest = _pending.First();
            _pending.Remove(oldest.Key);
            oldest.Value.Channels.CopyTo(_last, 0);
            LastTakenTick = oldest.Key;
            Dropped++;
        }
    }

    /// <summary>Takes the next command in the client's order.</summary>
    /// <param name="frame">Where the channels go.</param>
    /// <returns><see langword="false"/> when none waits.</returns>
    public bool TryTake(Span<float> frame)
    {
        if (_pending.Count == 0)
        {
            return false;
        }

        (long tick, (float[] channels, long viewTick, float viewFraction)) = _pending.First();
        _pending.Remove(tick);
        channels.CopyTo(_last, 0);
        _last.CopyTo(frame);
        LastTakenTick = tick;
        if (viewTick != long.MinValue && ServerTick != long.MinValue)
        {
            if (viewTick >= ServerTick)
            {
                viewTick = ServerTick;
                viewFraction = 0f;
            }

            if (MaxRewindTicks != int.MaxValue)
            {
                // As far back as the line explains, or as the client's own views usually are and a hitch beyond (Source's
                // 200 ms): a backtrack is one shot reaching further than the rest, which is what is held. The commands still
                // waiting here add to its age, a tick each.
                // The usual age is the average of the client's first views, believed (sv_maxunlag still bounds them), and
                // after them learns from every view, a held one only as far as it was held: it follows a client that
                // honestly draws later (a slow machine, a deep buffer), but a reach is never learned in one go.
                long age = ServerTick - viewTick;
                if (_viewsSeen < LearnedViews)
                {
                    // Learned no further than twice what the line explains: a client can't teach the server a reach.
                    double learned = Math.Min(age, (2.0 * MaxRewindTicks) + ViewAgeToleranceTicks);
                    _viewsSeen++;
                    _typicalViewAge = _viewsSeen == 1 ? learned : _typicalViewAge + ((learned - _typicalViewAge) / _viewsSeen);
                    LastViewTick = viewTick;
                    LastViewFraction = viewFraction;
                    return true;
                }

                double allowed = Math.Max(MaxRewindTicks, _typicalViewAge + ViewAgeToleranceTicks) + _pending.Count;
                if (age > allowed)
                {
                    Backtracked++;
                    age = (long)Math.Ceiling(allowed);
                    viewTick = ServerTick - age;
                    viewFraction = 0f;
                }

                _typicalViewAge += (age - _typicalViewAge) / 16;
            }
        }

        LastViewTick = viewTick;
        LastViewFraction = viewTick == long.MinValue ? 0f : viewFraction;
        return true;
    }

    /// <summary>Gives the next command if one waits, else repeats the last: for a player that must be run every tick.</summary>
    /// <param name="frame">Where the channels go.</param>
    /// <returns><see langword="true"/> when a command of the client's own was given.</returns>
    /// <remarks>A repeated command is input the client never gave, which its prediction then disagrees with; prefer <see cref="Due"/>.</remarks>
    public bool TakeOrRepeat(Span<float> frame)
    {
        if (TryTake(frame))
        {
            return true;
        }

        Missed++;
        _last.CopyTo(frame);
        return false;
    }
}
