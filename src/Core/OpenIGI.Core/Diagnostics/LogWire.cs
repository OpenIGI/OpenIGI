using System.Globalization;
using System.Text;

namespace OpenIGI.Core.Diagnostics;

/// <summary>Carries log records between processes as lines of text: the game writes them, the launcher reads them.</summary>
/// <remarks>
/// <para>
/// One record per line, so a pipe that delivers lines delivers records. A line starts with
/// <see cref="Prefix"/>, which nothing else a process prints begins with, then tab-separated fields: the
/// time in Unix milliseconds, the UTC offset in minutes, the level, the thread, the category, the message
/// and the exception's text. Backslash, tab, carriage return and line feed inside a field are escaped.
/// </para>
/// <para>
/// A reader keeps any other line as plain text, so a build from before this format, or a native library
/// printing on its own, still shows up rather than vanishing.
/// </para>
/// </remarks>
public static class LogWire
{
    /// <summary>The variable that asks a game process to write its standard output in this format.</summary>
    /// <remarks>
    /// A variable rather than a switch: a launcher may start a build older than the format, and an unknown
    /// switch stops the game where an unknown variable is ignored.
    /// </remarks>
    public const string StreamVariable = "OPENIGI_LOG_STREAM";

    /// <summary>The value of <see cref="StreamVariable"/> that selects this format.</summary>
    public const string StreamValue = "wire";

    /// <summary>What every encoded line starts with.</summary>
    public const string Prefix = "\u001eOIGI1\t";

    private const int FieldCount = 7;

    /// <summary>Gets a value indicating whether this process was asked, through <see cref="StreamVariable"/>, to write this format.</summary>
    public static bool IsRequested =>
        string.Equals(Environment.GetEnvironmentVariable(StreamVariable), StreamValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>Writes one record as one line.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="record">The record.</param>
    public static void Write(TextWriter writer, in LogRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);

        StringBuilder line = new(Prefix.Length + record.Message.Length + 64);
        line.Append(Prefix)
            .Append(record.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(((int)record.Timestamp.Offset.TotalMinutes).ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(((int)record.Level).ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(record.ThreadId.ToString(CultureInfo.InvariantCulture)).Append('\t');
        Escape(line, record.Category).Append('\t');
        Escape(line, record.Message).Append('\t');
        if (record.Exception is not null)
        {
            Escape(line, record.Exception.ToString());
        }

        writer.WriteLine(line.ToString());
    }

    /// <summary>Reads a line written by <see cref="Write"/>.</summary>
    /// <param name="line">The line.</param>
    /// <param name="record">
    /// The record. An exception cannot cross a process, so its text is appended to the message on the lines
    /// after it.
    /// </param>
    /// <returns>Whether the line was in this format.</returns>
    public static bool TryDecode(string? line, out LogRecord record)
    {
        record = default;
        if (line is null || !line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] fields = line[Prefix.Length..].Split('\t');
        if (fields.Length != FieldCount
            || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixMs)
            || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetMinutes)
            || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level)
            || !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int thread)
            || level is < (int)LogLevel.Trace or >= (int)LogLevel.None)
        {
            return false;
        }

        string message = Unescape(fields[5]);
        string exception = Unescape(fields[6]);
        if (exception.Length > 0)
        {
            message = string.Concat(message, Environment.NewLine, exception);
        }

        DateTimeOffset timestamp;
        try
        {
            timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToOffset(TimeSpan.FromMinutes(offsetMinutes));
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        record = new LogRecord(timestamp, (LogLevel)level, Unescape(fields[4]), message, null, thread);
        return true;
    }

    private static StringBuilder Escape(StringBuilder into, string value)
    {
        foreach (char c in value)
        {
            _ = c switch
            {
                '\\' => into.Append(@"\\"),
                '\t' => into.Append(@"\t"),
                '\r' => into.Append(@"\r"),
                '\n' => into.Append(@"\n"),
                _ => into.Append(c),
            };
        }

        return into;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        StringBuilder text = new(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\' || i + 1 == value.Length)
            {
                text.Append(c);
                continue;
            }

            char next = value[++i];
            text.Append(next switch
            {
                't' => '\t',
                'r' => '\r',
                'n' => '\n',
                _ => next,
            });
        }

        return text.ToString();
    }
}
