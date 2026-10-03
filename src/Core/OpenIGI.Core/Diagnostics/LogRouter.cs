namespace OpenIGI.Core.Diagnostics;

/// <summary>The logger a process runs on: filters each record by <see cref="Policy"/> and hands it to every sink.</summary>
/// <remarks>
/// <para>
/// One router per process, built first, before anything that logs. Sinks can be added at any point after:
/// the in-game console exists only once the graphics device does, and the startup that came before it is
/// exactly what somebody opening the console wants to read. So the router keeps the most recent records in
/// a replay buffer and plays them to a sink when it is added.
/// </para>
/// <para>
/// Safe from any thread. Records are handed to sinks one at a time under a lock, which keeps their order
/// identical in every sink and makes adding a sink atomic with the replay; the sinks themselves only
/// format or queue, so the lock is held for microseconds. A disabled call never takes it.
/// </para>
/// <para>
/// A <see cref="LogLevel.Fatal"/> record flushes every sink before the call returns, since it is normally
/// the last thing a process says.
/// </para>
/// </remarks>
public sealed class LogRouter : IOpenIgiLogger, IDisposable
{
    private readonly object _gate = new();
    private readonly LogRecord[] _replay;
    private volatile ILogSink[] _sinks = [];
    private volatile LogLevel _floor = LogLevel.Trace;
    private int _replayStart;
    private int _replayCount;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="LogRouter"/> class.</summary>
    /// <param name="policy">Which levels are logged per category; Information everywhere when omitted.</param>
    /// <param name="replayCapacity">How many recent records a newly added sink is shown; 0 for none.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="replayCapacity"/> is negative.</exception>
    public LogRouter(LogLevelPolicy? policy = null, int replayCapacity = 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(replayCapacity);
        Policy = policy ?? new LogLevelPolicy();
        _replay = new LogRecord[replayCapacity];
    }

    /// <summary>Gets the policy deciding which levels are logged per category.</summary>
    public LogLevelPolicy Policy { get; }

    /// <summary>Gets the sinks, in the order they were added.</summary>
    public IReadOnlyList<ILogSink> Sinks => _sinks;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level, string category) =>
        level < LogLevel.None && level >= _floor && level >= Policy.LevelFor(category);

    /// <inheritdoc/>
    public void Log(in LogRecord record)
    {
        if (!IsEnabled(record.Level, record.Category))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Remember(record);
            foreach (ILogSink sink in _sinks)
            {
                Emit(sink, record);
            }
        }

        if (record.Level >= LogLevel.Fatal)
        {
            Flush();
        }
    }

    /// <summary>Adds a sink, first playing it the records it missed.</summary>
    /// <param name="sink">The sink.</param>
    /// <param name="replay">Whether the sink is shown the replay buffer.</param>
    /// <returns>This router, so a set-up can be written as one expression.</returns>
    public LogRouter AddSink(ILogSink sink, bool replay = true)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (replay)
            {
                for (int i = 0; i < _replayCount; i++)
                {
                    Emit(sink, _replay[(_replayStart + i) % _replay.Length]);
                }
            }

            _sinks = [.. _sinks, sink];
            UpdateFloor();
        }

        return this;
    }

    /// <summary>Removes a sink without disposing it.</summary>
    /// <param name="sink">The sink.</param>
    /// <returns>Whether it had been added.</returns>
    public bool RemoveSink(ILogSink sink)
    {
        lock (_gate)
        {
            int index = Array.IndexOf(_sinks, sink);
            if (index < 0)
            {
                return false;
            }

            _sinks = [.. _sinks[..index], .. _sinks[(index + 1)..]];
            UpdateFloor();
            return true;
        }
    }

    /// <summary>Flushes every sink.</summary>
    public void Flush()
    {
        foreach (ILogSink sink in _sinks)
        {
            try
            {
                sink.Flush();
            }
#pragma warning disable CA1031 // A logger must never throw into the code that called it.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }

    /// <summary>Flushes and disposes every sink.</summary>
    public void Dispose()
    {
        ILogSink[] sinks;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sinks = _sinks;
            _sinks = [];
        }

        foreach (ILogSink sink in sinks)
        {
            sink.Dispose();
        }
    }

    private static void Emit(ILogSink sink, in LogRecord record)
    {
        if (record.Level < sink.MinimumLevel)
        {
            return;
        }

        try
        {
            sink.Emit(record);
        }
#pragma warning disable CA1031 // A logger must never throw into the code that called it.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void Remember(in LogRecord record)
    {
        if (_replay.Length == 0)
        {
            return;
        }

        if (_replayCount < _replay.Length)
        {
            _replay[(_replayStart + _replayCount++) % _replay.Length] = record;
        }
        else
        {
            _replay[_replayStart] = record;
            _replayStart = (_replayStart + 1) % _replay.Length;
        }
    }

    // With no sinks at all every record is still kept for replay, so nothing logged before the first sink
    // arrives is lost; the floor then is the lowest level anything could want.
    private void UpdateFloor() =>
        _floor = _sinks.Length == 0 ? LogLevel.Trace : _sinks.Min(static s => s.MinimumLevel);
}
