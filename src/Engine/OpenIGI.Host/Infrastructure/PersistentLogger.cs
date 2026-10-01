using System.Globalization;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Infrastructure;

/// <summary>Mirrors desktop diagnostics to the console and a per-session file.</summary>
internal sealed class PersistentLogger : IIgiLogger, IDisposable
{
    internal const string LogFileName = "runtime.log";

    /// <summary>Overrides <c>con_log_verbosity</c> from the environment: <c>off</c>, <c>minimal</c>, <c>verbose</c>.</summary>
    /// <remarks>
    /// The console variable cannot answer for the start-up that happens before there IS a console: the
    /// graphics device is created first, and a crash inside the driver during that takes the process with
    /// it. An environment variable is readable from the first line of the process and cannot be overridden
    /// by a saved settings file, which makes it the only way to turn the log up for the part of the run
    /// that is hardest to diagnose and least reproducible.
    /// </remarks>
    internal const string VerbosityEnvironmentVariable = "OPENIGI_LOG_VERBOSITY";

    private static readonly LogLevel? EnvironmentLevel = ReadEnvironmentLevel();

    private readonly ConsoleLogger _console;
    private readonly StreamWriter _file;
    private readonly object _gate = new();
    private bool _disposed;

    public PersistentLogger(string path, LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        _console = new ConsoleLogger(minimumLevel);

        // Runtime diagnostics must not synchronously flush the disk from the render thread, so the file is
        // normally buffered and flushed when disposed. That is exactly wrong for a crash: an access
        // violation inside a driver ends the process without unwinding, Dispose never runs, and the whole
        // log is lost -- which is the case a log is most needed for. Asking for a verbosity through the
        // environment is asking to diagnose something, so that mode writes through instead and accepts
        // the cost.
        _file = new StreamWriter(path, append: false) { AutoFlush = EnvironmentLevel is not null };
        _file.WriteLine($"# OpenIGI runtime log started {DateTimeOffset.Now:O}");
    }

    public bool IsEnabled(LogLevel level) =>
        level >= MinimumLevel && level != LogLevel.None;

    private static LogLevel MinimumLevel => EnvironmentLevel ?? Level(RenderCVars.LogVerbosity.Value);

    private static LogLevel Level(OpenIGI.Rendering.LogVerbosity verbosity) => verbosity switch
    {
        OpenIGI.Rendering.LogVerbosity.Off => LogLevel.None,
        OpenIGI.Rendering.LogVerbosity.Minimal => LogLevel.Warning,
        _ => LogLevel.Information,
    };

    /// <summary>Reads the environment override once, or null when it is unset or unrecognised.</summary>
    private static LogLevel? ReadEnvironmentLevel()
    {
        string? value = Environment.GetEnvironmentVariable(VerbosityEnvironmentVariable);
        return Enum.TryParse(value, ignoreCase: true, out OpenIGI.Rendering.LogVerbosity verbosity)
            ? Level(verbosity)
            : null;
    }

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
