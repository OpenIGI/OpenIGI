namespace OpenIGI.Core.Diagnostics;

/// <summary>
/// Sink for engine diagnostic messages.
/// </summary>
/// <remarks>
/// The engine never writes to the console directly; every subsystem takes an <see cref="IIgiLogger"/>
/// so that hosts (desktop, mobile, tests, tools) decide where output goes. Implementations must be safe
/// to call from the single game thread and must not throw.
/// </remarks>
public interface IIgiLogger
{
    /// <summary>Determines whether messages of the given severity would be recorded.</summary>
    /// <param name="level">The severity to test.</param>
    /// <returns><see langword="true"/> when a message at <paramref name="level"/> would be recorded.</returns>
    /// <remarks>Callers use this to skip building expensive messages.</remarks>
    bool IsEnabled(LogLevel level);

    /// <summary>Records one message.</summary>
    /// <param name="level">The severity of the message.</param>
    /// <param name="category">The subsystem that produced the message, for example <c>"Vfs"</c>.</param>
    /// <param name="message">The message text.</param>
    /// <param name="exception">An optional exception associated with the message.</param>
    void Log(LogLevel level, string category, string message, Exception? exception = null);
}
