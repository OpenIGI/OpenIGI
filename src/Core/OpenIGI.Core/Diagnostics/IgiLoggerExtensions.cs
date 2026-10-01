namespace OpenIGI.Core.Diagnostics;

/// <summary>Convenience wrappers over <see cref="IIgiLogger.Log"/>.</summary>
public static class IgiLoggerExtensions
{
    /// <summary>Records a <see cref="LogLevel.Trace"/> message.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The message text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static void Trace(this IIgiLogger logger, string category, string message) =>
        Write(logger, LogLevel.Trace, category, message, null);

    /// <summary>Records a <see cref="LogLevel.Debug"/> message.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The message text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static void Debug(this IIgiLogger logger, string category, string message) =>
        Write(logger, LogLevel.Debug, category, message, null);

    /// <summary>Records a <see cref="LogLevel.Information"/> message.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The message text.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static void Information(this IIgiLogger logger, string category, string message) =>
        Write(logger, LogLevel.Information, category, message, null);

    /// <summary>Records a <see cref="LogLevel.Warning"/> message.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The message text.</param>
    /// <param name="exception">An optional exception associated with the message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static void Warning(this IIgiLogger logger, string category, string message, Exception? exception = null) =>
        Write(logger, LogLevel.Warning, category, message, exception);

    /// <summary>Records a <see cref="LogLevel.Error"/> message.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The message text.</param>
    /// <param name="exception">An optional exception associated with the message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static void Error(this IIgiLogger logger, string category, string message, Exception? exception = null) =>
        Write(logger, LogLevel.Error, category, message, exception);

    private static void Write(IIgiLogger logger, LogLevel level, string category, string message, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (logger.IsEnabled(level))
        {
            logger.Log(level, category, message, exception);
        }
    }
}
