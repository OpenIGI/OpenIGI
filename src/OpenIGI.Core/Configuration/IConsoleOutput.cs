namespace OpenIGI.Core.Configuration;

/// <summary>
/// Where console output goes.
/// </summary>
/// <remarks>
/// The console system never writes to a screen or a stream itself. In the game the sink is the
/// on-screen console's scrollback; in tests it is a list; in headless tools it is standard output.
/// Implementations must not throw.
/// </remarks>
public interface IConsoleOutput
{
    /// <summary>Records one line.</summary>
    /// <param name="line">The line to record.</param>
    void Write(ConsoleLine line);
}
