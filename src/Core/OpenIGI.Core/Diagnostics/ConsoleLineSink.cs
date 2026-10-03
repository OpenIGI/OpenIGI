using System.Collections.Concurrent;
using System.Diagnostics;
using OpenIGI.Core.Configuration;

namespace OpenIGI.Core.Diagnostics;

/// <summary>Turns records into <see cref="ConsoleLine"/>s for the in-game developer console.</summary>
/// <remarks>
/// <para>
/// Records arrive on any thread — a mission preview loads on the pool — but the console's scrollback belongs
/// to the game thread. So <see cref="Emit"/> only queues, and the game thread moves the queue into the
/// console once a frame with <see cref="Drain"/>.
/// </para>
/// <para>
/// The category rides beside the text rather than inside it: formatted in, it could not be filtered on, it
/// would match every search for the subsystem's name, and it would come along when a line was copied.
/// </para>
/// </remarks>
public sealed class ConsoleLineSink : ILogSink
{
    // Console lines are stamped in seconds since the process started, so a replayed startup line and a
    // command typed later read as one timeline. The start is read once; from then on the clock is the
    // monotonic stopwatch, which cannot run backwards when the machine's clock is corrected mid-session.
    private static readonly DateTimeOffset ProcessStartTime = ReadProcessStart();
    private static readonly long ClockStart = Stopwatch.GetTimestamp();
    private static readonly double ClockOffset = Math.Max(0.0, (DateTimeOffset.Now - ProcessStartTime).TotalSeconds);

    private readonly ConcurrentQueue<ConsoleLine> _pending = new();

    /// <summary>Initialises a new instance of the <see cref="ConsoleLineSink"/> class.</summary>
    /// <param name="minimumLevel">The lowest level shown; the console is for reading, so Information by default.</param>
    public ConsoleLineSink(LogLevel minimumLevel = LogLevel.Information) => MinimumLevel = minimumLevel;

    /// <inheritdoc/>
    public LogLevel MinimumLevel { get; }

    /// <summary>Gets the seconds since the process started, the clock console lines are stamped with.</summary>
    public static double Elapsed => ClockOffset + Stopwatch.GetElapsedTime(ClockStart).TotalSeconds;

    /// <summary>Maps a log level onto how the console presents it.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The severity.</returns>
    public static ConsoleSeverity SeverityOf(LogLevel level) => level switch
    {
        LogLevel.Warning => ConsoleSeverity.Warning,
        LogLevel.Error or LogLevel.Fatal => ConsoleSeverity.Error,
        _ => ConsoleSeverity.Output,
    };

    /// <inheritdoc/>
    public void Emit(in LogRecord record)
    {
        double seconds = Math.Max(0.0, (record.Timestamp - ProcessStartTime).TotalSeconds);
        ConsoleSeverity severity = SeverityOf(record.Level);

        // The scrollback draws one row per line, so a multi-line report becomes as many lines, each
        // carrying the record's category and severity so a filter keeps or hides it whole.
        string message = LogFormatter.SingleLine(record);
        foreach (Range range in message.AsSpan().Split('\n'))
        {
            string text = message[range].TrimEnd('\r');
            _pending.Enqueue(new ConsoleLine(text, severity, record.Category, seconds));
        }
    }

    /// <summary>Moves every queued line into the console. Call on the thread that owns it.</summary>
    /// <param name="write">Where each line goes, such as <see cref="ConsoleSystem.Write(ConsoleLine)"/>.</param>
    /// <returns>How many lines were moved.</returns>
    public int Drain(Action<ConsoleLine> write)
    {
        ArgumentNullException.ThrowIfNull(write);

        int count = 0;
        while (_pending.TryDequeue(out ConsoleLine line))
        {
            write(line);
            count++;
        }

        return count;
    }

    /// <inheritdoc/>
    public void Flush()
    {
        // Lines become visible when the game thread drains them; there is nothing to make durable.
    }

    /// <inheritdoc/>
    public void Dispose() => _pending.Clear();

    // A browser has no process to ask; there, the first line logged is as near the start as can be known.
    private static DateTimeOffset ReadProcessStart()
    {
        if (OperatingSystem.IsBrowser())
        {
            return DateTimeOffset.Now;
        }

        try
        {
            using Process self = Process.GetCurrentProcess();
            return self.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return DateTimeOffset.Now;
        }
    }
}
