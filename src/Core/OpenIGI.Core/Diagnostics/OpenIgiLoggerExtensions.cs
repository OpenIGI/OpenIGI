using System.Runtime.CompilerServices;

namespace OpenIGI.Core.Diagnostics;

/// <summary>The methods code logs through: one per level, over <see cref="IOpenIgiLogger.Log"/>.</summary>
/// <remarks>
/// Each level has two forms. A plain <see cref="string"/> is checked against
/// <see cref="IOpenIgiLogger.IsEnabled"/> before it is logged; an interpolated <c>$"..."</c> binds to
/// <see cref="LogMessageHandler{TLevel}"/> instead and is not even formatted when the level is off.
/// </remarks>
public static class OpenIgiLoggerExtensions
{
    /// <summary>Determines whether a message at a level would be recorded, for a category or, without one, by default.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="level">The severity to test.</param>
    /// <param name="category">The category asking.</param>
    /// <returns>Whether such a message would be recorded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static bool IsEnabled(this IOpenIgiLogger logger, LogLevel level, string category = "")
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.IsEnabled(level, category);
    }

    /// <summary>Records a message at the given level.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="level">The severity.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static void Log(this IOpenIgiLogger logger, LogLevel level, string category, string message, Exception? exception = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (logger.IsEnabled(level, category))
        {
            logger.Log(LogRecord.Now(level, category, message, exception));
        }
    }

    /// <summary>Records a <see cref="LogLevel.Trace"/> message.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The text.</param>
    public static void Trace(this IOpenIgiLogger logger, string category, string message) =>
        logger.Log(LogLevel.Trace, category, message);

    /// <summary>Records a <see cref="LogLevel.Trace"/> message, formatted only when that level is enabled.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The interpolated text.</param>
    public static void Trace(
        this IOpenIgiLogger logger,
        string category,
        [InterpolatedStringHandlerArgument(nameof(logger), nameof(category))] ref LogMessageHandler<LogLevelTags.TraceTag> message) =>
        Write(logger, category, ref message, null);

    /// <summary>Records a <see cref="LogLevel.Debug"/> message.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The text.</param>
    public static void Debug(this IOpenIgiLogger logger, string category, string message) =>
        logger.Log(LogLevel.Debug, category, message);

    /// <summary>Records a <see cref="LogLevel.Debug"/> message, formatted only when that level is enabled.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The interpolated text.</param>
    public static void Debug(
        this IOpenIgiLogger logger,
        string category,
        [InterpolatedStringHandlerArgument(nameof(logger), nameof(category))] ref LogMessageHandler<LogLevelTags.DebugTag> message) =>
        Write(logger, category, ref message, null);

    /// <summary>Records a <see cref="LogLevel.Information"/> message.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The text.</param>
    public static void Information(this IOpenIgiLogger logger, string category, string message) =>
        logger.Log(LogLevel.Information, category, message);

    /// <summary>Records a <see cref="LogLevel.Information"/> message, formatted only when that level is enabled.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The interpolated text.</param>
    public static void Information(
        this IOpenIgiLogger logger,
        string category,
        [InterpolatedStringHandlerArgument(nameof(logger), nameof(category))] ref LogMessageHandler<LogLevelTags.InformationTag> message) =>
        Write(logger, category, ref message, null);

    /// <summary>Records a <see cref="LogLevel.Warning"/> message.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    public static void Warning(this IOpenIgiLogger logger, string category, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Warning, category, message, exception);

    /// <summary>Records a <see cref="LogLevel.Warning"/> message, formatted only when that level is enabled.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The interpolated text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    public static void Warning(
        this IOpenIgiLogger logger,
        string category,
        [InterpolatedStringHandlerArgument(nameof(logger), nameof(category))] ref LogMessageHandler<LogLevelTags.WarningTag> message,
        Exception? exception = null) =>
        Write(logger, category, ref message, exception);

    /// <summary>Records a <see cref="LogLevel.Error"/> message.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    public static void Error(this IOpenIgiLogger logger, string category, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Error, category, message, exception);

    /// <summary>Records a <see cref="LogLevel.Error"/> message, formatted only when that level is enabled.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The interpolated text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    public static void Error(
        this IOpenIgiLogger logger,
        string category,
        [InterpolatedStringHandlerArgument(nameof(logger), nameof(category))] ref LogMessageHandler<LogLevelTags.ErrorTag> message,
        Exception? exception = null) =>
        Write(logger, category, ref message, exception);

    /// <summary>Records a <see cref="LogLevel.Fatal"/> message; a <see cref="LogRouter"/> flushes every sink before returning.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    public static void Fatal(this IOpenIgiLogger logger, string category, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Fatal, category, message, exception);

    /// <summary>Records a <see cref="LogLevel.Fatal"/> message, formatted only when that level is enabled.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="category">The subsystem that produced the message.</param>
    /// <param name="message">The interpolated text.</param>
    /// <param name="exception">The exception it reports, if any.</param>
    public static void Fatal(
        this IOpenIgiLogger logger,
        string category,
        [InterpolatedStringHandlerArgument(nameof(logger), nameof(category))] ref LogMessageHandler<LogLevelTags.FatalTag> message,
        Exception? exception = null) =>
        Write(logger, category, ref message, exception);

    private static void Write<TLevel>(IOpenIgiLogger logger, string category, ref LogMessageHandler<TLevel> message, Exception? exception)
        where TLevel : ILogLevelTag
    {
        if (message.IsEnabled)
        {
            logger.Log(LogRecord.Now(TLevel.Level, category, message.ToStringAndClear(), exception));
        }
    }
}
