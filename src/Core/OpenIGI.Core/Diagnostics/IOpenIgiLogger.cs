namespace OpenIGI.Core.Diagnostics;

/// <summary>
/// Where engine diagnostics are written.
/// </summary>
/// <remarks>
/// <para>
/// The engine never writes to the console directly; every subsystem takes an <see cref="IOpenIgiLogger"/>
/// so that the host decides where output goes. A running game uses a <see cref="LogRouter"/>, which hands
/// each record to its sinks: the log file, standard output, the in-game console, the launcher.
/// </para>
/// <para>
/// Call sites use <see cref="OpenIgiLoggerExtensions"/> rather than <see cref="Log"/>. Those methods ask
/// <see cref="IsEnabled"/> first and, for an interpolated message, do not build the text at all when the
/// answer is no.
/// </para>
/// <para>
/// Implementations must be safe to call from any thread and must not throw.
/// </para>
/// </remarks>
public interface IOpenIgiLogger
{
    /// <summary>Determines whether a message of the given severity and category would be recorded.</summary>
    /// <param name="level">The severity to test.</param>
    /// <param name="category">The subsystem asking, for example <c>"Vfs"</c> or <c>"AI.Gunner"</c>.</param>
    /// <returns><see langword="true"/> when such a message would be recorded.</returns>
    bool IsEnabled(LogLevel level, string category);

    /// <summary>Records one message.</summary>
    /// <param name="record">The message.</param>
    void Log(in LogRecord record);
}
