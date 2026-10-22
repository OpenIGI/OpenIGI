using System.Globalization;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Platform.Desktop.Logging;
using OpenIGI.TestSupport;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// What every AI scenario capture shares: the hosts it boots headless over the installed game, the log they
/// write to, a scratch folder, and the small readers the trace lines are written with.
/// </summary>
/// <remarks>
/// One per scenario class instance, so one per run: the scratch folder and the log are closed with it.
/// </remarks>
internal sealed class AiScenarioSupport : IDisposable
{
    /// <summary>
    /// The collection every scenario class joins. A capture boots a whole game and the gun-shot tracer hook is
    /// one static, so no two of them may run at once -- as when they were one class.
    /// </summary>
    public const string CollectionName = "ai scenario captures";

    /// <summary>World units per metre.</summary>
    public const double Metre = 4096.0;

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-aiscn-{Guid.NewGuid():N}");

    private LogRouter? _log;

    /// <summary>Creates the scratch folder.</summary>
    public AiScenarioSupport() => Directory.CreateDirectory(_scratch);

    /// <summary>An integer environment variable, or <paramref name="fallback"/> when it is unset or unreadable.</summary>
    /// <param name="name">The variable.</param>
    /// <param name="fallback">What an unset variable stands for.</param>
    /// <returns>The value.</returns>
    public static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), CultureInfo.InvariantCulture, out int value) ? value : fallback;

    /// <summary>How far apart two points are across the ground, height ignored, in world units.</summary>
    /// <param name="p">One point.</param>
    /// <param name="q">The other.</param>
    /// <returns>The distance.</returns>
    public static double Horizontal(Vector3d p, Vector3d q) => Math.Sqrt(((p.X - q.X) * (p.X - q.X)) + ((p.Y - q.Y) * (p.Y - q.Y)));

    /// <summary>A point in metres, one decimal each, as the trace lines write it.</summary>
    /// <param name="p">The point, in world units.</param>
    /// <returns>The text.</returns>
    public static string Fmt(Vector3d p) =>
        string.Create(CultureInfo.InvariantCulture, $"({p.X / Metre:0.0},{p.Y / Metre:0.0},{p.Z / Metre:0.0})");

    /// <summary>
    /// The log a scenario's hosts write to: the file <c>OPENIGI_AI_SCENARIO_LOG</c> names, at
    /// <see cref="LogLevel.Information"/> and up, or nothing when it is unset.
    /// </summary>
    /// <returns>The logger, shared by every host this harness boots and closed when it is disposed.</returns>
    private IOpenIgiLogger ScenarioLog()
    {
        if (_log is not null)
        {
            return _log;
        }

        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_LOG") is not { Length: > 0 } logPath)
        {
            return NullLogger.Instance;
        }

        // OPENIGI_AI_SCENARIO_LOGSPEC takes the same spec as OPENIGI_LOG, e.g. "Weapon=debug" for every guard
        // round's outcome.
        LogLevelPolicy policy = new(LogLevel.Information);
        if (Environment.GetEnvironmentVariable("OPENIGI_AI_SCENARIO_LOGSPEC") is { Length: > 0 } spec)
        {
            policy.TryApply(spec);
        }

        _log = new LogRouter(policy).AddSink(FileSink.Open(logPath));
        return _log;
    }

    /// <summary>A host over the installed game, drawn into a headless window.</summary>
    /// <param name="device">The graphics device it records into.</param>
    /// <param name="args">The command line.</param>
    /// <returns>The host, not yet started.</returns>
    public GameHost Host(RecordingGraphicsDevice device, params string[] args) =>
        Host(device, new HeadlessGameWindow(), args);

    /// <summary>A host over the installed game, with no sound and the scenario log.</summary>
    /// <param name="device">The graphics device it records into.</param>
    /// <param name="window">The window it draws into.</param>
    /// <param name="args">The command line.</param>
    /// <returns>The host, not yet started.</returns>
    public GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args)
    {
        string root = GameData.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.Default;
        IOpenIgiLogger logger = ScenarioLog();

        VirtualFileSystem fileSystem = new(logger);
        fileSystem.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig
            .LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout)
            .ApplyTo(fileSystem);

        HostOptions options = HostOptions.Parse(args);
        return new GameHost(
            new HeadlessPlatformHost(),
            window,
            device,
            new SilentAudioFactory(),
            SynchronousParallelWorkScheduler.Instance,
            AlwaysReadyLoadGate.Instance,
            TitleCatalog.Catalog,
            runtime.Title,
            fileSystem,
            options,
            root,
            logger);
    }

    /// <summary>The played mission's registries, which the host keeps together.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The mission.</returns>
    public static MissionWorld MissionOf(GameHost host) => host.Mission();

    /// <inheritdoc/>
    public void Dispose()
    {
        _log?.Dispose();
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class SilentAudioFactory : IAudioDeviceFactory
    {
        public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => new RecordingAudioDevice();
    }
}
