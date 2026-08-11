using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;

namespace OpenIGI.Desktop.Infrastructure;

/// <summary>Mirrors desktop diagnostics to the console and a per-session file.</summary>
internal sealed class PersistentLogger : IIgiLogger, IDisposable
{
    private readonly ConsoleLogger _console;
    private readonly StreamWriter _file;
    private readonly object _gate = new();
    private bool _disposed;

    public PersistentLogger(string path, LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        _console = new ConsoleLogger(minimumLevel);
        // Runtime diagnostics must not synchronously flush the disk from the render thread. The file is
        // flushed when disposed, while normal logging remains visible immediately on the console.
        _file = new StreamWriter(path, append: false) { AutoFlush = false };
        _file.WriteLine($"# OpenIGI runtime log started {DateTimeOffset.Now:O}");
    }

    public bool IsEnabled(LogLevel level) =>
        level >= MinimumLevel && level != LogLevel.None;

    private static LogLevel MinimumLevel => RenderCVars.LogVerbosity.Value switch
    {
        OpenIGI.Rendering.LogVerbosity.Off => LogLevel.None,
        OpenIGI.Rendering.LogVerbosity.Minimal => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    public void Log(LogLevel level, string category, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        _console.Log(level, category, message, exception);
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:O} [{level}] {category}: {message}");
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                try { _file.WriteLine(line); }
                catch (IOException) { }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _file.Dispose();
        }
    }
}
