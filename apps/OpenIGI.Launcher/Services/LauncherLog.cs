using System.Diagnostics;
using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Launcher.Services;

/// <summary>The launcher's logging, and the log of the game it started, for the Logs page.</summary>
/// <remarks>
/// <para>
/// The launcher logs through a <see cref="LogRouter"/> to <c>launcher.log</c> and to <see cref="Launcher"/>,
/// which the Logs page shows. The game writes its own <c>runtime.log</c>; while the launcher is open it also
/// reads the game's standard output, which the game writes as <see cref="LogWire"/> lines when asked to, into
/// <see cref="Game"/>.
/// </para>
/// <para>
/// A build from before that format prints readable text, and a native library may print on its own; such a
/// line is kept as it is, at Information, or Warning from the error stream.
/// </para>
/// </remarks>
internal sealed class LauncherLog : IDisposable
{
    private const string GameCategory = "Game";

    private readonly IDisposable _crashLogging;

    private LauncherLog(LogRouter router, string? path)
    {
        Logger = router;
        FilePath = path;
        _crashLogging = CrashLogging.Install(router);
    }

    /// <summary>Gets the launcher's logger.</summary>
    public LogRouter Logger { get; }

    /// <summary>Gets what the launcher has logged this session.</summary>
    public MemorySink Launcher { get; } = new();

    /// <summary>Gets what the game it started last has logged.</summary>
    public MemorySink Game { get; } = new();

    /// <summary>Gets the launcher's log file, or <see langword="null"/> when none could be opened.</summary>
    public string? FilePath { get; }

    /// <summary>Gets the folder the log files are kept in.</summary>
    public static string Folder => LauncherPaths.LogDirectory;

    /// <summary>Starts the launcher's logging.</summary>
    /// <returns>The log, which the caller disposes on the way out.</returns>
    public static LauncherLog Start()
    {
        LogRouter router = new(LogLevelPolicy.FromEnvironment(LogLevel.Information));

        string? path = null;
        string? failure = null;
        try
        {
            FileSink file = FileSink.Open(Path.Combine(Folder, "launcher.log"));
            router.AddSink(file);
            path = file.Path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            failure = ex.Message;
        }

        LauncherLog log = new(router, path);
        router.AddSink(log.Launcher);
        if (failure is not null)
        {
            router.Warning("Log", $"No log file this session: {failure}");
        }

        router.Information("Launcher", $"OpenIGI Launcher {LauncherUpdater.OwnVersion} on {LauncherPaths.Runtime}.");
        return log;
    }

    /// <summary>Reads a started game's output into <see cref="Game"/>, replacing the previous game's.</summary>
    /// <param name="game">The game's process, started with its output redirected.</param>
    public void Follow(Process game)
    {
        ArgumentNullException.ThrowIfNull(game);

        Game.Clear();
        game.OutputDataReceived += (_, e) => Read(e.Data, LogLevel.Information);
        game.ErrorDataReceived += (_, e) => Read(e.Data, LogLevel.Warning);
        game.BeginOutputReadLine();
        game.BeginErrorReadLine();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _crashLogging.Dispose();
        Logger.Dispose();
    }

    private void Read(string? line, LogLevel plainLevel)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        Game.Emit(LogWire.TryDecode(line, out LogRecord record) ? record : LogRecord.Now(plainLevel, GameCategory, line));
    }
}
