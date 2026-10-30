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
/// </remarks>
public sealed class UserCommandBuffer
{
    private readonly SortedDictionary<long, (float[] Channels, long ViewTick)> _pending = [];
    private readonly float[] _last;
    private bool _primed;

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

    /// <summary>Gets the ticks the queue had nothing to give.</summary>
    public long Missed { get; private set; }

    /// <summary>Gets the commands that arrived after one made later had already run.</summary>
    public long Late { get; private set; }

    /// <summary>Gets the stamp of the newest command run, or <see cref="long.MinValue"/> before the first.</summary>
    public long LastTakenTick { get; private set; } = long.MinValue;

    /// <summary>Gets the tick the client was drawing at when it made the command last run, or <see cref="long.MinValue"/>.</summary>
    public long LastViewTick { get; private set; } = long.MinValue;

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

        _pending.TryAdd(command.Tick, (command.Channels, command.ViewTick));
        NewestTick = command.Tick;
    }

    /// <summary>Gets how many commands to run this tick, and counts a tick the queue is dry.</summary>
    /// <returns>Nought, one, or two while catching up.</returns>
    public int Due()
    {
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

        return _pending.Count > MaxDepth ? 2 : 1;
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

        (long tick, (float[] channels, long viewTick)) = _pending.First();
        _pending.Remove(tick);
        channels.CopyTo(_last, 0);
        _last.CopyTo(frame);
        LastTakenTick = tick;
        LastViewTick = viewTick;
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
