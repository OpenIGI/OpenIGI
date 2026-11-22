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
/// Every command that has arrived is run on the server's next tick, as Source runs a player's
/// (<c>CBasePlayer::PhysicsSimulate</c>: all the commands waiting, in the frame they are found waiting). A tick on which
/// none arrived runs none -- the player waits for its client
/// rather than move on controls nobody pressed, and the tick is counted in <see cref="Missed"/> -- and the next runs the two
/// that came. So what a client does reaches the server's level half a round trip after it did it, and no later.
/// </para>
/// <para>
/// It once waited for two before it started and from then gave exactly one a tick, draining only past six: whatever came in a
/// clump -- any hitch of the line or the client's frame -- stayed queued for the rest of the session, and every command after
/// ran that much late: four deep on a 90 ms line, a hit shown four tenths of a second after the shot.
/// </para>
/// <para>
/// A line that brings its packets unevenly leaves ticks with no command and ticks with two, and the player's server copy
/// moves in fits for everyone watching. The queue sees that for itself -- a dry tick here and there among ticks that ran --
/// and holds its newest command back a tick while it lasts (<see cref="Held"/>, two for a worse line), letting it go again
/// once the line has been steady for a while. What it holds is what the line needs now, never what a hitch once left.
/// </para>
/// <para>
/// Catching up is paid for. Each tick of the server's earns the client one command's run, kept up to <see cref="MaxCredit"/>,
/// and each command run spends one: a client that fell behind (its packets held up, then arriving together) catches up
/// on what it saved, but one sending commands faster than time passes -- a speed hack -- runs no faster than the server's
/// clock, and what it sends beyond that waits until the queue is full and is then refused. This is Source's
/// <c>sv_maxusrcmdprocessticks</c>.
/// </para>
/// <para>
/// A backlog the credit cannot pay off is dropped, oldest first, down to what the queue holds back, as Source drops the
/// commands a client sends past what its time allows: a client that ran its own clock ahead -- a frame that hitched while the
/// level loaded, the ticks it owed run all at once -- or that stalled and then sent everything it made meanwhile would
/// otherwise have every later command run that much late for the rest of the session, the credit earned a tick at a time
/// being spent each tick on the command that came with it. Half a second of ticks it could not pay for, however many wait,
/// is enough. What is dropped is stale already; the client's prediction is corrected once to where its player stands.
/// </para>
/// </remarks>
public sealed class UserCommandBuffer
{
    private readonly SortedDictionary<long, (float[] Channels, long ViewTick, float ViewFraction)> _pending = [];
    private readonly float[] _last;
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

    /// <summary>Gets or sets the least of the newest commands held back a tick whatever the line does; none, as Source holds none.</summary>
    public int TargetDepth { get; set; }

    /// <summary>Gets or sets the most the queue holds back by itself for an uneven line.</summary>
    public int MaxHeld { get; set; } = 2;

    /// <summary>Gets or sets the share of ticks left dry by an uneven line past which one more command is held back.</summary>
    public double UnevenShare { get; set; } = 0.1;

    /// <summary>Gets or sets how many ticks in a row must run before a command held back for an uneven line is let go: ten seconds.</summary>
    public int SteadyTicks { get; set; } = 300;

    /// <summary>Gets how many of the newest commands are held back a tick now: <see cref="TargetDepth"/>, or what the line's unevenness asks.</summary>
    public int Held => Math.Max(Math.Max(0, TargetDepth), _heldForLine);

    // What the line's unevenness has the queue hold back, the share of recent ticks it left dry (stalls apart), the dry
    // ticks in a row now, and the ticks in a row that ran.
    private int _heldForLine;
    private double _unevenShare;
    private int _dryRun;
    private int _steadyRun;

    // Whether the queue is giving commands out: from when as many wait as it holds back and one more, until it runs dry.
    private bool _running;

    /// <summary>Gets or sets past how many waiting commands the client is behind: a backlog its credit cannot pay off is dropped.</summary>
    public int MaxDepth { get; set; } = 6;

    /// <summary>Gets or sets the most commands' runs a client may save up to catch up with: a second at the original's tick.</summary>
    /// <remarks>
    /// A third of a second once. A client that stalls for longer than that and then runs the ticks it owes -- a browser's page
    /// taking in a new part of a level, a collection, a held line let go -- sends a second's commands together; what the credit
    /// could not pay was dropped as run, and its player here was left on a ladder its client had already climbed off. Two
    /// over a second: a second's stall leaves its client's thirty and the one it sent as the line came back, and a credit of
    /// thirty left one of them a tick late for good.
    /// </remarks>
    public int MaxCredit { get; set; } = 32;

    /// <summary>Gets or sets the most commands the queue gives in one tick.</summary>
    public int MaxPerTick { get; set; } = 4;

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
        NewestTick = Math.Max(NewestTick, command.Tick);
    }

    /// <summary>Gets how many commands to run this tick, and counts a tick the queue is dry.</summary>
    /// <returns>As many as wait beyond <see cref="TargetDepth"/>, up to <see cref="MaxPerTick"/> and what the client has earned.</returns>
    /// <remarks>Called once a server tick: that is what earns the client its runs.</remarks>
    public int Due()
    {
        _credit = Math.Min(_credit + 1, MaxCredit);

        // Dry, or -- holding some back for an uneven line -- not yet as many waiting as it holds and one to run: the hold is
        // what the queue starts on again after it ran dry, and from then it gives one a tick down to nothing.
        if (_pending.Count == 0 || (!_running && _pending.Count <= Held))
        {
            _running = false;
            Missed += LastTakenTick == long.MinValue ? 0 : 1;
            _dryRun += LastTakenTick == long.MinValue ? 0 : 1;
            _steadyRun = 0;
            return 0;
        }

        _running = true;
        WeighTheLine();

        // A backlog the credit cannot pay off, held half a second: dropped to what the queue holds back, oldest first. Any
        // backlog: one hovering a few commands deep, never past MaxDepth for long, once sat there for the whole session.
        if (_pending.Count > Held + 1 && _credit < 2)
        {
            if (++_backlogTicks >= StaleBacklogTicks)
            {
                DropBacklog();
                if (_pending.Count == 0)
                {
                    _running = false;
                    return 0;
                }
            }
        }
        else
        {
            _backlogTicks = 0;
        }

        // One, and with it all that wait beyond what is held back, a few a tick: at one a tick and no more, a clump that came
        // together stayed queued for good, and at two a tick a second's backlog took a second to run while the commands still
        // arriving kept it there until the credit was gone.
        int wanted = Math.Clamp(_pending.Count - Held, 1, Math.Max(1, MaxPerTick));
        int due = Math.Min(wanted, (int)Math.Floor(_credit));
        if (due < wanted)
        {
            Throttled++;
        }

        _credit -= due;
        return due;
    }

    // A tick that has commands to run, after however many that had none: a dry tick or two among ticks that ran is the line
    // coming unevenly, and counts towards holding a command back; a longer gap is a stall -- the client's window held, its
    // line stopped -- which no buffer bridges, and counts for nothing. A long steady run lets a held command go.
    private void WeighTheLine()
    {
        const double Smoothing = 1.0 / 60;
        const int LongestUnevenGap = 3;
        if (_dryRun is > 0 and <= LongestUnevenGap)
        {
            for (int i = 0; i < _dryRun; i++)
            {
                _unevenShare += (1 - _unevenShare) * Smoothing;
            }
        }

        _dryRun = 0;
        _unevenShare -= _unevenShare * Smoothing;
        _steadyRun++;
        if (_unevenShare > UnevenShare && _heldForLine < MaxHeld)
        {
            _heldForLine++;
            _unevenShare = 0;
            _steadyRun = 0;
        }
        else if (_heldForLine > 0 && _steadyRun >= SteadyTicks)
        {
            _heldForLine--;
            _unevenShare = 0;
            _steadyRun = 0;
        }
    }

    // Drops the oldest waiting commands until what the queue holds back is left. Each counts as run, so its repeats in later
    // packets are not taken again, and what it held down is what is held down after it.
    private void DropBacklog()
    {
        _backlogTicks = 0;
        while (_pending.Count > Held)
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
