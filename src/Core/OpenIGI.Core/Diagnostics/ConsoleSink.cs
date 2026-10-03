namespace OpenIGI.Core.Diagnostics;

/// <summary>How a <see cref="ConsoleSink"/> writes.</summary>
public enum ConsoleSinkLayout
{
    /// <summary>For a person: <c>time LVL Category: message</c>; errors on the error stream.</summary>
    Text,

    /// <summary>For another process: every record as one <see cref="LogWire"/> line on standard output.</summary>
    Wire,

    /// <summary>
    /// For a command-line tool: the bare message on standard output, and <c>warning: </c> or <c>error: </c>
    /// before it on the error stream.
    /// </summary>
    Plain,
}

/// <summary>Writes records to the process's standard output and error streams.</summary>
/// <remarks>
/// <para>
/// This, the command-line tools' result stream and the console attachment on Windows are the only code
/// allowed to touch <c>System.Console</c>; everything else logs. In the <see cref="ConsoleSinkLayout.Text"/>
/// layout, warnings and worse go to the error stream so a tool's real output on standard output stays clean,
/// and the level tag is coloured when the stream is a terminal.
/// </para>
/// <para>
/// In a browser the streams are the developer tools' console, which is where the web build's log belongs.
/// </para>
/// </remarks>
public sealed class ConsoleSink : ILogSink
{
    private readonly ConsoleSinkLayout _layout;
    private readonly TextWriter _out;
    private readonly TextWriter _error;
    private readonly bool _colour;

    /// <summary>Initialises a new instance of the <see cref="ConsoleSink"/> class over the process's own streams.</summary>
    /// <param name="layout">How records are written.</param>
    /// <param name="minimumLevel">The lowest level written.</param>
#pragma warning disable RS0030 // The console sink is where the process's standard streams are written.
    public ConsoleSink(ConsoleSinkLayout layout = ConsoleSinkLayout.Text, LogLevel minimumLevel = LogLevel.Trace)
        : this(Console.Out, Console.Error, layout, minimumLevel, colour: CanColour())
#pragma warning restore RS0030
    {
    }

    /// <summary>Initialises a new instance of the <see cref="ConsoleSink"/> class over the given writers.</summary>
    /// <param name="output">Where ordinary records go.</param>
    /// <param name="error">Where warnings and worse go in the text layout.</param>
    /// <param name="layout">How records are written.</param>
    /// <param name="minimumLevel">The lowest level written.</param>
    /// <param name="colour">Whether the level tag is coloured through the console colour.</param>
    public ConsoleSink(TextWriter output, TextWriter error, ConsoleSinkLayout layout = ConsoleSinkLayout.Text, LogLevel minimumLevel = LogLevel.Trace, bool colour = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        _out = output;
        _error = error;
        _layout = layout;
        _colour = colour && layout == ConsoleSinkLayout.Text;
        MinimumLevel = minimumLevel;
    }

    /// <inheritdoc/>
    public LogLevel MinimumLevel { get; }

    /// <inheritdoc/>
    public void Emit(in LogRecord record)
    {
        if (_layout == ConsoleSinkLayout.Wire)
        {
            LogWire.Write(_out, record);
            return;
        }

        TextWriter writer = record.Level >= LogLevel.Warning ? _error : _out;
        if (_layout == ConsoleSinkLayout.Plain)
        {
            LogFormatter.WritePlainLine(writer, record);
            return;
        }

        if (!_colour)
        {
            LogFormatter.WriteConsoleLine(writer, record);
            return;
        }

        WriteColoured(writer, record);
    }

    /// <inheritdoc/>
    public void Flush()
    {
        _out.Flush();
        _error.Flush();
    }

    /// <inheritdoc/>
    public void Dispose() => Flush();

#pragma warning disable RS0030 // The console sink is where the process's standard streams are written.
    private static bool CanColour() =>
        !OperatingSystem.IsBrowser() && !Console.IsOutputRedirected && !Console.IsErrorRedirected;

    private static void WriteColoured(TextWriter writer, in LogRecord record)
    {
        LogFormatter.WriteTimeOfDay(writer, record);
        writer.Flush();

        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = record.Level switch
        {
            LogLevel.Trace or LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Information => ConsoleColor.Gray,
            LogLevel.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.Red,
        };
        writer.Write(LogFormatter.Tag(record.Level));
        writer.Flush();
        Console.ForegroundColor = previous;

        LogFormatter.WriteText(writer, record);
    }
#pragma warning restore RS0030
}
