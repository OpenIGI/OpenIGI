using OpenIGI.Core.Diagnostics;

namespace OpenIGI.TestSupport;

/// <summary>A logger that writes every line to a file, for a test of two games whose logs are wanted after it.</summary>
/// <param name="path">The file, written afresh.</param>
public sealed class FileLogger(string path) : IOpenIgiLogger, IDisposable
{
    private readonly StreamWriter _writer = new(path, append: false) { AutoFlush = true };
    private readonly object _gate = new();

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel level, string category) => level >= LogLevel.Debug && level < LogLevel.None;

    /// <inheritdoc/>
    public void Log(in LogRecord record)
    {
        if (!IsEnabled(record.Level, record.Category))
        {
            return;
        }

        using StringWriter line = new(System.Globalization.CultureInfo.InvariantCulture);
        LogFormatter.WriteConsoleLine(line, record);
        lock (_gate)
        {
            _writer.WriteLine(line.ToString().TrimEnd());
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _writer.Dispose();
}
