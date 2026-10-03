using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Host;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Platform.Desktop.Logging;

namespace OpenIGI.Desktop;

/// <summary>The desktop game's logging: one router writing to the session's log file and to standard output.</summary>
/// <remarks>
/// <para>
/// Built before anything else in the process, so even a failure to find the game's data is in the log. The
/// in-game console joins later through <see cref="Host.DevTools.HostConsole.AttachLog"/> and is shown everything
/// logged before it existed.
/// </para>
/// <para>
/// Standard output carries <see cref="LogWire"/> lines when the launcher asked for them through
/// <see cref="LogWire.StreamVariable"/>, and readable text otherwise — for a terminal, a capture script, a
/// soak reading its memory reports back.
/// </para>
/// <para>
/// How much is logged follows <c>con_log_verbosity</c>, live, unless <c>OPENIGI_LOG</c> set it for the run.
/// Memory reports are always on: they are only ever written because somebody asked for one.
/// </para>
/// </remarks>
internal sealed class DesktopLogging : IDisposable
{
    private const string LogCategory = "Log";

    private readonly IDisposable _crashLogging;
    private bool _disposed;

    private DesktopLogging(LogRouter router, string? logPath)
    {
        Logger = router;
        LogPath = logPath;
        _crashLogging = CrashLogging.Install(router);
        if (!router.Policy.IsPinned)
        {
            LoggingCVars.Verbosity.Changed += OnVerbosityChanged;
        }
    }

    /// <summary>Gets the process's logger.</summary>
    public LogRouter Logger { get; }

    /// <summary>Gets the file this session logs to, or <see langword="null"/> when none could be opened.</summary>
    public string? LogPath { get; }

    /// <summary>Builds the router and its sinks.</summary>
    /// <returns>The logging, which the caller disposes on the way out.</returns>
    public static DesktopLogging Start()
    {
        LogLevelPolicy policy = LogLevelPolicy.FromEnvironment(LogLevelPolicy.LevelFor(LoggingCVars.Verbosity.Value));
        if (!policy.Overrides.ContainsKey(GameHost.MemoryLogCategory))
        {
            policy.SetOverride(GameHost.MemoryLogCategory, LogLevel.Information);
        }

        LogRouter router = new(policy);
        router.AddSink(new ConsoleSink(LogWire.IsRequested ? ConsoleSinkLayout.Wire : ConsoleSinkLayout.Text));

        string? path = null;
        try
        {
            FileSink file = FileSink.Open(RuntimePaths.GetLogPath());
            router.AddSink(file);
            path = file.Path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The game is still worth running without a file; standard output and the console carry the log.
            router.Warning(LogCategory, $"No log file this session: {ex.Message}");
        }

        return new DesktopLogging(router, path);
    }

    /// <summary>Flushes and closes every sink.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LoggingCVars.Verbosity.Changed -= OnVerbosityChanged;
        _crashLogging.Dispose();
        Logger.Dispose();
    }

    private void OnVerbosityChanged(object? sender, CVarChangedEventArgs e) =>
        Logger.Policy.Default = LogLevelPolicy.LevelFor(LoggingCVars.Verbosity.Value);
}
