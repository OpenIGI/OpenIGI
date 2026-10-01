namespace OpenIGI.Core.Diagnostics;

/// <summary>An <see cref="IIgiLogger"/> that discards everything.</summary>
/// <remarks>Use this as the default when a host has not supplied a logger.</remarks>
public sealed class NullLogger : IIgiLogger
{
    private NullLogger()
    {
    }

    /// <summary>Gets the shared instance.</summary>
    public static NullLogger Instance { get; } = new NullLogger();

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level) => false;

    /// <inheritdoc/>
    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        // Intentionally empty.
    }
}
