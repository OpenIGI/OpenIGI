using System.Globalization;

namespace OpenIGI.Core.Diagnostics;

/// <summary>
/// An <see cref="IIgiLogger"/> that writes one line per message to the standard output and error streams.
/// </summary>
/// <remarks>Intended for the command-line tools and for desktop development builds.</remarks>
public sealed class ConsoleLogger : IIgiLogger
{
    /// <summary>Initializes a new instance of the <see cref="ConsoleLogger"/> class.</summary>
    /// <param name="minimumLevel">The lowest severity that is written; anything below it is discarded.</param>
    public ConsoleLogger(LogLevel minimumLevel = LogLevel.Information) => MinimumLevel = minimumLevel;

    /// <summary>Gets the lowest severity this logger writes.</summary>
    public LogLevel MinimumLevel { get; }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level) => level >= MinimumLevel && level != LogLevel.None;

    /// <inheritdoc/>
    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"[{Abbreviate(level)}] {category}: {message}");

        TextWriter writer = level >= LogLevel.Error ? Console.Error : Console.Out;
        writer.WriteLine(line);
        if (exception is not null)
        {
            writer.WriteLine(exception);
        }
    }

    private static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "trc",
        LogLevel.Debug => "dbg",
        LogLevel.Information => "inf",
        LogLevel.Warning => "wrn",
        LogLevel.Error => "err",
        LogLevel.Fatal => "ftl",
        _ => "___",
    };
}
