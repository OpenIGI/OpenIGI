namespace OpenIGI.Core.Diagnostics;

/// <summary>Keeps the most recent records in memory, for a view that shows a log and for tests that read one.</summary>
/// <remarks>
/// Safe from any thread. A reader polls <see cref="Version"/> and copies the records out with
/// <see cref="CopyTo"/> only when it has moved, so a view drawn every frame costs nothing while nothing is
/// logged.
/// </remarks>
public sealed class MemorySink : ILogSink, IOpenIgiLogger
{
    private readonly object _gate = new();
    private readonly LogRecord[] _records;
    private int _start;
    private int _count;
    private long _version;

    /// <summary>Initialises a new instance of the <see cref="MemorySink"/> class.</summary>
    /// <param name="capacity">How many records are kept before the oldest are dropped.</param>
    /// <param name="minimumLevel">The lowest level kept.</param>
    public MemorySink(int capacity = 4096, LogLevel minimumLevel = LogLevel.Trace)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _records = new LogRecord[capacity];
        MinimumLevel = minimumLevel;
    }

    /// <inheritdoc/>
    public LogLevel MinimumLevel { get; }

    /// <summary>Gets a number that changes whenever the records do.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Gets how many records are kept now.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>Copies the records out, oldest first.</summary>
    /// <returns>The records.</returns>
    public IReadOnlyList<LogRecord> Snapshot()
    {
        List<LogRecord> records = [];
        CopyTo(records);
        return records;
    }

    /// <summary>Replaces a list's contents with the records, oldest first.</summary>
    /// <param name="into">The list to fill.</param>
    /// <returns>The <see cref="Version"/> the copy reflects.</returns>
    public long CopyTo(List<LogRecord> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        lock (_gate)
        {
            into.Clear();
            for (int i = 0; i < _count; i++)
            {
                into.Add(_records[(_start + i) % _records.Length]);
            }

            return _version;
        }
    }

    /// <summary>Drops every record.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_records);
            _start = 0;
            _count = 0;
            _version++;
        }
    }

    /// <inheritdoc/>
    public void Emit(in LogRecord record)
    {
        lock (_gate)
        {
            if (_count < _records.Length)
            {
                _records[(_start + _count++) % _records.Length] = record;
            }
            else
            {
                _records[_start] = record;
                _start = (_start + 1) % _records.Length;
            }

            _version++;
        }
    }

    /// <inheritdoc/>
    bool IOpenIgiLogger.IsEnabled(LogLevel level, string category) => level >= MinimumLevel && level < LogLevel.None;

    /// <inheritdoc/>
    void IOpenIgiLogger.Log(in LogRecord record)
    {
        if (record.Level >= MinimumLevel)
        {
            Emit(record);
        }
    }

    /// <inheritdoc/>
    public void Flush()
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
