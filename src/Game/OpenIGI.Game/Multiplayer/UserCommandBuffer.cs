namespace OpenIGI.Game.Multiplayer;

/// <summary>The server's buffer of one client's commands, by the tick each is for.</summary>
/// <remarks>
/// <para>
/// Commands arrive several to a packet, repeated, early and out of order; the buffer keeps one per tick and gives the
/// server, each tick, the command for exactly that tick. A command for a tick already run is late and is dropped. When
/// the command for a tick has not arrived, the client's last one is run again, its held channels held, as Source does
/// when a client's command is missing, and the miss is counted so the client can be told to send further ahead.
/// </para>
/// <para>
/// The client stamps its commands ahead of the server by its half round trip and a small buffer (see
/// <see cref="Net.TickClockSync.CommandTick"/>), so in a steady session each arrives a tick or two before it is run.
/// </para>
/// </remarks>
public sealed class UserCommandBuffer
{
    private readonly SortedDictionary<long, float[]> _pending = [];
    private readonly float[] _last;
    private long _lastTaken = long.MinValue;

    /// <summary>Initializes a new instance of the <see cref="UserCommandBuffer"/> class.</summary>
    /// <param name="channelCount">The title's channel frame length.</param>
    /// <param name="maxAhead">How many ticks ahead of the server a command may be and still be kept.</param>
    public UserCommandBuffer(int channelCount, int maxAhead = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channelCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAhead, 1);
        _last = new float[channelCount];
        MaxAhead = maxAhead;
    }

    /// <summary>Gets how many ticks ahead of the server a command may be and still be kept.</summary>
    public int MaxAhead { get; }

    /// <summary>Gets the ticks whose command had not arrived when it was needed.</summary>
    public long Missed { get; private set; }

    /// <summary>Gets the commands that arrived for a tick already run.</summary>
    public long Late { get; private set; }

    /// <summary>Gets the newest tick a command has been run for, or <see cref="long.MinValue"/> before the first.</summary>
    public long LastTakenTick => _lastTaken;

    /// <summary>Gets how many ticks of commands wait beyond the last one run.</summary>
    public int Buffered => _pending.Count;

    /// <summary>Takes a command that arrived.</summary>
    /// <param name="command">The command.</param>
    /// <param name="serverTick">The tick the server is about to run.</param>
    public void Push(in UserCommand command, long serverTick)
    {
        if (command.Channels is null || command.Channels.Length != _last.Length)
        {
            return;
        }

        if (command.Tick <= _lastTaken || command.Tick < serverTick)
        {
            Late++;
            return;
        }

        if (command.Tick - serverTick > MaxAhead)
        {
            return;
        }

        _pending.TryAdd(command.Tick, command.Channels);
        NewestTick = command.Tick;
    }

    /// <summary>Gets the tick of the command that arrived last (a packet carries its newest last), or <see cref="long.MinValue"/> before the first.</summary>
    public long NewestTick { get; private set; } = long.MinValue;

    /// <summary>Gives the command for a tick, or repeats the last one when it has not arrived.</summary>
    /// <param name="tick">The tick being run.</param>
    /// <param name="frame">Where the channels go.</param>
    /// <returns><see langword="true"/> when the client's own command for the tick was there.</returns>
    public bool Take(long tick, Span<float> frame)
    {
        while (_pending.Count > 0)
        {
            long first = FirstKey();
            if (first >= tick)
            {
                break;
            }

            _pending.Remove(first);
        }

        _pending.Remove(tick, out float[]? channels);
        if (channels is not null)
        {
            channels.CopyTo(_last, 0);
        }
        else
        {
            Missed++;
        }

        _last.CopyTo(frame);
        _lastTaken = tick;
        return channels is not null;
    }

    private long FirstKey()
    {
        foreach (long key in _pending.Keys)
        {
            return key;
        }

        return long.MaxValue;
    }
}
