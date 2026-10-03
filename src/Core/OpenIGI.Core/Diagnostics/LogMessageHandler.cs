using System.Globalization;
using System.Runtime.CompilerServices;

namespace OpenIGI.Core.Diagnostics;

/// <summary>Names a <see cref="LogLevel"/> as a type, so one handler can serve every level.</summary>
public interface ILogLevelTag
{
    /// <summary>Gets the level this tag stands for.</summary>
    static abstract LogLevel Level { get; }
}

/// <summary>The level tags <see cref="OpenIgiLoggerExtensions"/> uses.</summary>
public static class LogLevelTags
{
    /// <summary>Stands for <see cref="LogLevel.Trace"/>.</summary>
    public readonly struct TraceTag : ILogLevelTag
    {
        /// <inheritdoc/>
        public static LogLevel Level => LogLevel.Trace;
    }

    /// <summary>Stands for <see cref="LogLevel.Debug"/>.</summary>
    public readonly struct DebugTag : ILogLevelTag
    {
        /// <inheritdoc/>
        public static LogLevel Level => LogLevel.Debug;
    }

    /// <summary>Stands for <see cref="LogLevel.Information"/>.</summary>
    public readonly struct InformationTag : ILogLevelTag
    {
        /// <inheritdoc/>
        public static LogLevel Level => LogLevel.Information;
    }

    /// <summary>Stands for <see cref="LogLevel.Warning"/>.</summary>
    public readonly struct WarningTag : ILogLevelTag
    {
        /// <inheritdoc/>
        public static LogLevel Level => LogLevel.Warning;
    }

    /// <summary>Stands for <see cref="LogLevel.Error"/>.</summary>
    public readonly struct ErrorTag : ILogLevelTag
    {
        /// <inheritdoc/>
        public static LogLevel Level => LogLevel.Error;
    }

    /// <summary>Stands for <see cref="LogLevel.Fatal"/>.</summary>
    public readonly struct FatalTag : ILogLevelTag
    {
        /// <inheritdoc/>
        public static LogLevel Level => LogLevel.Fatal;
    }
}

/// <summary>Builds an interpolated log message only when its level is enabled.</summary>
/// <typeparam name="TLevel">The level the message is logged at.</typeparam>
/// <remarks>
/// <para>
/// The compiler binds a <c>$"..."</c> argument to this rather than to <see cref="string"/>, and asks the
/// logger before formatting a single hole. A disabled message costs one lookup and no allocation, so a call
/// site never needs an <see cref="IOpenIgiLogger.IsEnabled"/> guard of its own just to stay cheap.
/// </para>
/// <para>
/// Formats with the invariant culture: a log is read by people and tools on other machines, and a decimal
/// comma would make it ambiguous.
/// </para>
/// </remarks>
[InterpolatedStringHandler]
public ref struct LogMessageHandler<TLevel>
    where TLevel : ILogLevelTag
{
    private DefaultInterpolatedStringHandler _builder;

    /// <summary>Initialises a new instance of the <see cref="LogMessageHandler{TLevel}"/> struct.</summary>
    /// <param name="literalLength">The number of literal characters, from the compiler.</param>
    /// <param name="formattedCount">The number of holes, from the compiler.</param>
    /// <param name="logger">The logger the message is for.</param>
    /// <param name="category">The message's category.</param>
    /// <param name="isEnabled">Whether the compiler should format the holes at all.</param>
    public LogMessageHandler(int literalLength, int formattedCount, IOpenIgiLogger logger, string category, out bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(logger);

        IsEnabled = isEnabled = logger.IsEnabled(TLevel.Level, category);
        _builder = isEnabled
            ? new DefaultInterpolatedStringHandler(literalLength, formattedCount, CultureInfo.InvariantCulture)
            : default;
    }

    /// <summary>Gets a value indicating whether the message will be logged.</summary>
    public bool IsEnabled { get; }

    /// <summary>Appends literal text.</summary>
    /// <param name="value">The text.</param>
    public void AppendLiteral(string value) => _builder.AppendLiteral(value);

    /// <summary>Appends a value.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    public void AppendFormatted<T>(T value) => _builder.AppendFormatted(value);

    /// <summary>Appends a value with a format string.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted<T>(T value, string? format) => _builder.AppendFormatted(value, format);

    /// <summary>Appends a value with an alignment.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="alignment">The minimum width; negative to left-align.</param>
    public void AppendFormatted<T>(T value, int alignment) => _builder.AppendFormatted(value, alignment);

    /// <summary>Appends a value with an alignment and a format string.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="alignment">The minimum width; negative to left-align.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted<T>(T value, int alignment, string? format) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Appends characters.</summary>
    /// <param name="value">The characters.</param>
    public void AppendFormatted(scoped ReadOnlySpan<char> value) => _builder.AppendFormatted(value);

    /// <summary>Appends characters with an alignment and a format string.</summary>
    /// <param name="value">The characters.</param>
    /// <param name="alignment">The minimum width; negative to left-align.</param>
    /// <param name="format">Ignored; present so the compiler can bind any hole.</param>
    public void AppendFormatted(scoped ReadOnlySpan<char> value, int alignment = 0, string? format = null) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Appends a string.</summary>
    /// <param name="value">The string.</param>
    public void AppendFormatted(string? value) => _builder.AppendFormatted(value);

    /// <summary>Appends a string with an alignment and a format string.</summary>
    /// <param name="value">The string.</param>
    /// <param name="alignment">The minimum width; negative to left-align.</param>
    /// <param name="format">Ignored; present so the compiler can bind any hole.</param>
    public void AppendFormatted(string? value, int alignment = 0, string? format = null) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Appends an object with an alignment and a format string.</summary>
    /// <param name="value">The object.</param>
    /// <param name="alignment">The minimum width; negative to left-align.</param>
    /// <param name="format">The format string.</param>
    public void AppendFormatted(object? value, int alignment = 0, string? format = null) =>
        _builder.AppendFormatted(value, alignment, format);

    /// <summary>Returns the built message and releases the buffer.</summary>
    /// <returns>The message, or an empty string when the level was disabled.</returns>
    internal string ToStringAndClear() => IsEnabled ? _builder.ToStringAndClear() : string.Empty;
}
