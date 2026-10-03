namespace OpenIGI.Core.Diagnostics;

/// <summary>One logged message.</summary>
/// <param name="Timestamp">When the message was logged, in local time.</param>
/// <param name="Level">The severity.</param>
/// <param name="Category">The subsystem that produced it, dotted from general to specific, for example <c>"AI.Gunner"</c>.</param>
/// <param name="Message">The text.</param>
/// <param name="Exception">The exception it reports, if any.</param>
/// <param name="ThreadId">The managed id of the thread that logged it.</param>
public readonly record struct LogRecord(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    Exception? Exception = null,
    int ThreadId = 0)
{
    /// <summary>Creates a record stamped with the current time and thread.</summary>
    /// <param name="level">The severity.</param>
    /// <param name="category">The subsystem that produced it.</param>
    /// <param name="message">The text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    /// <returns>The record.</returns>
    public static LogRecord Now(LogLevel level, string category, string message, Exception? exception = null) =>
        new(DateTimeOffset.Now, level, category, message, exception, Environment.CurrentManagedThreadId);
}
