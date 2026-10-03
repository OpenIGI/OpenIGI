using System.Globalization;

namespace OpenIGI.Core.Diagnostics;

/// <summary>The one place a <see cref="LogRecord"/> becomes a line of text.</summary>
/// <remarks>
/// Every text sink formats through here, so the log file, a terminal and a tool's error stream all read
/// the same way: <c>time LVL Category: message</c>, then the exception, if any, on the lines after.
/// </remarks>
public static class LogFormatter
{
    /// <summary>The three-letter tag for a level, as written in every text layout.</summary>
    /// <param name="level">The level.</param>
    /// <returns>The tag, such as <c>WRN</c>.</returns>
    public static string Tag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Fatal => "FTL",
        _ => "---",
    };

    /// <summary>Writes a record for a log file: the full date and offset, so lines from different sessions and machines compare.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="record">The record.</param>
    public static void WriteFileLine(TextWriter writer, in LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        writer.Write(' ');
        WriteBody(writer, record);
    }

    /// <summary>Writes a record for a person watching it live: the time of day only.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="record">The record.</param>
    public static void WriteConsoleLine(TextWriter writer, in LogRecord record)
    {
        WriteTimeOfDay(writer, record);
        WriteBody(writer, record);
    }

    /// <summary>Writes the part of a line after the time: <c>LVL Category: message</c> and the exception.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="record">The record.</param>
    public static void WriteBody(TextWriter writer, in LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(Tag(record.Level));
        WriteText(writer, record);
    }

    /// <summary>Writes the part of a line after the level tag: <c> Category: message</c>, then the exception.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="record">The record.</param>
    public static void WriteText(TextWriter writer, in LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(' ');
        if (record.Category.Length > 0)
        {
            writer.Write(record.Category);
            writer.Write(": ");
        }

        writer.WriteLine(record.Message);
        if (record.Exception is not null)
        {
            writer.WriteLine(record.Exception.ToString());
        }
    }

    /// <summary>Writes a record the way a command-line tool reports: the message alone, or prefixed <c>warning: </c> or <c>error: </c>.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="record">The record.</param>
    public static void WritePlainLine(TextWriter writer, in LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(record.Level switch
        {
            >= LogLevel.Error => "error: ",
            LogLevel.Warning => "warning: ",
            _ => string.Empty,
        });
        writer.WriteLine(record.Message);
        if (record.Exception is not null && record.Level >= LogLevel.Error)
        {
            writer.WriteLine(record.Exception.ToString());
        }
    }

    /// <summary>Writes the time of day a live view shows, followed by a space.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="record">The record.</param>
    public static void WriteTimeOfDay(TextWriter writer, in LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(record.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
        writer.Write(' ');
    }

    /// <summary>The message followed by the exception's own message, on one line, for a view that shows one line per record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The text.</returns>
    public static string SingleLine(in LogRecord record) =>
        record.Exception is null
            ? record.Message
            : string.Create(CultureInfo.InvariantCulture, $"{record.Message} ({record.Exception.GetType().Name}: {record.Exception.Message})");
}
