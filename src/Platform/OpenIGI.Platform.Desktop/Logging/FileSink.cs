using System.Collections.Concurrent;
using System.Globalization;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Platform.Desktop.Logging;

/// <summary>Writes records to a log file from a background thread, keeping the previous sessions' files beside it.</summary>
/// <remarks>
/// <para>
/// Desktop only: a browser has no file to write and no thread to write it from. The thread that logs only
/// queues the record. The writer thread formats it, and flushes to disk every time
/// the queue runs dry, so the file is never more than a moment behind the game. That matters most when the
/// process dies without unwinding — an access violation inside a graphics driver — which is exactly the
/// run a log is wanted for. <see cref="Flush"/> waits for the queue to drain.
/// </para>
/// <para>
/// Opening a log rotates the earlier ones: <c>runtime.log</c> becomes <c>runtime.1.log</c>, and so on, up
/// to the number kept. When another process is still writing the file — a second copy run for a capture
/// or a comparison — rotation is impossible, so this session writes <c>runtime.&lt;pid&gt;.log</c> beside it
/// instead.
/// </para>
/// </remarks>
public sealed class FileSink : ILogSink
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    private readonly BlockingCollection<LogRecord> _queue = new(new ConcurrentQueue<LogRecord>());
    private readonly StreamWriter _writer;
    private readonly Thread _thread;
    private long _pending;
    private bool _disposed;

    private FileSink(string path, StreamWriter writer, LogLevel minimumLevel)
    {
        Path = path;
        MinimumLevel = minimumLevel;
        _writer = writer;
        _thread = new Thread(Run) { IsBackground = true, Name = "Log file writer", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Gets the file this session writes, which is not the requested path when another process held it.</summary>
    public string Path { get; }

    /// <inheritdoc/>
    public LogLevel MinimumLevel { get; }

    /// <summary>Opens a session's log, rotating the earlier ones.</summary>
    /// <param name="path">The log's path, for example <c>…/runtime.log</c>.</param>
    /// <param name="keep">How many earlier sessions' files are kept.</param>
    /// <param name="minimumLevel">The lowest level written.</param>
    /// <returns>The sink.</returns>
    /// <exception cref="IOException">Neither the file nor the per-process fallback could be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder cannot be written.</exception>
    public static FileSink Open(string path, int keep = 4, LogLevel minimumLevel = LogLevel.Trace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(keep);

        string full = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full) ?? ".");

        StreamWriter writer;
        try
        {
            ThrowIfHeld(full);
            Rotate(full, keep);
            writer = Create(full);
        }
        catch (IOException)
        {
            full = Numbered(full, Environment.ProcessId);
            writer = Create(full);
        }

        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"# Log opened {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} by process {Environment.ProcessId}"));
        writer.Flush();
        return new FileSink(full, writer, minimumLevel);
    }

    /// <inheritdoc/>
    public void Emit(in LogRecord record)
    {
        if (_queue.IsAddingCompleted)
        {
            return;
        }

        Interlocked.Increment(ref _pending);
        try
        {
            _queue.Add(record);
        }
        catch (InvalidOperationException)
        {
            // Completed between the check and the add, by Dispose on another thread.
            Interlocked.Decrement(ref _pending);
        }
    }

    /// <inheritdoc/>
    public void Flush() => SpinWait.SpinUntil(() => Interlocked.Read(ref _pending) == 0, FlushTimeout);

    /// <summary>Writes what is queued, then closes the file.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        _thread.Join(FlushTimeout);
        _writer.Dispose();
        _queue.Dispose();
    }

    // Whether another session is writing the log is asked of the file itself, not left to the rotation to find
    // out. Moving a file another process has open fails on Windows, but succeeds on Linux and macOS, where the
    // rotation would carry the other session's log away while it went on writing to it. An exclusive open
    // fails on all three while a session holds the file (on Unix, .NET takes an advisory lock for it).
    private static void ThrowIfHeld(string path)
    {
        if (File.Exists(path))
        {
            using FileStream probe = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    private static StreamWriter Create(string path) =>
        new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = false };

    private static string Numbered(string path, int number) =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(path) ?? ".",
            string.Create(CultureInfo.InvariantCulture, $"{System.IO.Path.GetFileNameWithoutExtension(path)}.{number}{System.IO.Path.GetExtension(path)}"));

    private static void Rotate(string path, int keep)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (keep == 0)
        {
            File.Delete(path);
            return;
        }

        File.Delete(Numbered(path, keep));
        for (int i = keep - 1; i >= 1; i--)
        {
            string older = Numbered(path, i);
            if (File.Exists(older))
            {
                File.Move(older, Numbered(path, i + 1));
            }
        }

        File.Move(path, Numbered(path, 1));
    }

    private void Run()
    {
        foreach (LogRecord record in _queue.GetConsumingEnumerable())
        {
            try
            {
                LogFormatter.WriteFileLine(_writer, record);
                if (_queue.Count == 0)
                {
                    _writer.Flush();
                }
            }
            catch (IOException)
            {
                // A full disk loses log lines, not the game.
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }

        try
        {
            _writer.Flush();
        }
        catch (IOException)
        {
        }
    }
}
