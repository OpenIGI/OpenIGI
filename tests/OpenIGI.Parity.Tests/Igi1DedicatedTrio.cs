using System.Collections.Concurrent;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.Multiplayer;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Dedicated;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;
using OpenIGI.Net;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// A dedicated server and two players on it, run headless in one process as the release runs them: the server's game is
/// simulation only and plays no part (its mission's player is nobody's), the console's commands change its maps and modes,
/// and each player is a whole game of its own on a line of its own, with its own latency.
/// </summary>
internal sealed class Igi1DedicatedTrio : IDisposable
{
    private readonly LoopbackNetwork _network = new();
    private readonly RecordingGraphicsDevice _serverDevice = new() { IsRecording = false };
    private readonly HostedSession _session;
    private readonly DedicatedServer _dedicated;
    private readonly Igi1CoopPair.Watch _serverWatch;
    private readonly Player[] _players;
    private long _steps;

    /// <summary>Starts the server on a mission under a mode, and both players joining it.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="mission">The mission.</param>
    /// <param name="pings">Each player's round trip to the server, in seconds.</param>
    public Igi1DedicatedTrio(string mode, int mission, params double[] pings)
    {
        Core.Configuration.CVarRegistry.Default.ResetAll();
        string map = mission.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string[] args = ["-mission", map, "-lockstep", "-frames", "200000", "-skipcutscene"];
        _session = new HostedSession(
            _network.Open(26001),
            new HostedSessionOptions("trio", 8, string.Empty, mode, map, "test", IncludesHost: false),
            "igi1",
            30,
            Log);
        ServerGame = Make(args, new HeadlessGameWindow(), _serverDevice, simulationOnly: true);
        _serverWatch = new Igi1CoopPair.Watch(_session);
        ServerGame.TickObserver = _serverWatch;
        _dedicated = new DedicatedServer(_session, new ServerConfig(), new BanList(null), ServerGame.GoToMission, Log, missions: () => ServerGame.MissionList)
        {
            Arenas = ServerGame.HasArena,
        };
        ServerGame.Start();

        string[] names = ["Anya", "Boris"];
        _players = [.. names.Select((name, i) => new Player(this, name, args, pings.Length > i ? pings[i] : 0))];
    }

    /// <summary>Gets the server's game.</summary>
    public GameHost ServerGame { get; }

    /// <summary>Gets the server's session.</summary>
    public HostedSession Session => _session;

    /// <summary>Gets the server's level, once it is up.</summary>
    public Igi1Gameplay? Server => _serverWatch.Game;

    /// <summary>Gets the players.</summary>
    public IReadOnlyList<Player> Players => _players;

    /// <summary>Gets what the server logged, in order.</summary>
    public ConcurrentQueue<string> ServerLog { get; } = new();

    /// <summary>Gets the server's logger, which keeps every line in <see cref="ServerLog"/>.</summary>
    private IOpenIgiLogger Log => _log ??= new KeepingLogger(ServerLog);

    private KeepingLogger? _log;

    /// <summary>Runs a command on the server's console, as its operator or an admin's <c>rcon</c> does.</summary>
    /// <param name="command">The command, as <c>map 2 tdm</c>.</param>
    /// <returns>Its answer.</returns>
    public IReadOnlyList<string> Console(string command) => _dedicated.Console.Execute(command);

    /// <summary>Runs the server and both players a frame each.</summary>
    /// <returns><see langword="false"/> once any has stopped.</returns>
    public bool Step()
    {
        _steps++;
        bool running = ServerGame.Step();
        _dedicated.Tick(_steps / 30.0);
        foreach (Player player in _players)
        {
            running &= player.Game.Step();
        }

        return running;
    }

    /// <summary>Runs all three until a condition holds.</summary>
    /// <param name="until">The condition.</param>
    /// <param name="frames">The most frames to run.</param>
    /// <returns>The frames it took, or -1 when it never held.</returns>
    public int RunUntil(Func<bool> until, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            if (until())
            {
                return frame;
            }

            if (!Step())
            {
                return -1;
            }
        }

        return until() ? frames : -1;
    }

    /// <summary>Gets whether both players are in the server's level and their own.</summary>
    public bool AllIn =>
        Server is { } server && server.World.NetworkPlayers.Count == _players.Length && _players.All(p => p.Joined && p.Level?.World.Player is not null);

    /// <summary>Gets a player's rig on the server.</summary>
    /// <param name="player">The player.</param>
    /// <returns>Its rig as the server runs it.</returns>
    public Igi1NetworkPlayer OnServer(Player player) =>
        Server!.World.NetworkPlayers.Single(p => p.Name == player.Name);

    /// <inheritdoc/>
    public void Dispose()
    {
        ServerGame.Stop();
        foreach (Player player in _players)
        {
            player.Dispose();
        }

        _dedicated.Dispose();
        _session.Dispose();
        _serverDevice.Dispose();
    }

    private static GameHost Make(string[] args, HeadlessGameWindow window, RecordingGraphicsDevice device, bool simulationOnly = false)
    {
        string root = GameDataSource.Igi1.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(GameTitle.Igi1);
        VirtualFileSystem files = new(NullLogger.Instance);
        files.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(files);
        HostOptions options = HostOptions.Parse([.. args, "-nosave"]);
        if (simulationOnly)
        {
            options = options with { PlayCutscenes = false, SimulationOnly = true };
        }

        return new GameHost(new HeadlessPlatformHost(), window, device, NullAudioDeviceFactory.Instance,
            InlineWorkScheduler.Instance, AlwaysReadyLoadGate.Instance, TitleCatalog.Catalog, runtime.Title, files, options, root, NullLogger.Instance);
    }

    /// <summary>One player: its own game, on its own line to the server.</summary>
    internal sealed class Player : IDisposable
    {
        private readonly RecordingGraphicsDevice _device = new() { IsRecording = false };
        private readonly OpenIGI.Net.Identity.EcdsaPlayerIdentity _identity = OpenIGI.Net.Identity.EcdsaPlayerIdentity.CreateEphemeral();
        private readonly Igi1CoopPair.FollowingWatch _watch;

        public Player(Igi1DedicatedTrio trio, string name, string[] args, double ping)
        {
            Name = name;
            Link = new SimulatedLinkTransport(trio._network.Open(), () => trio._steps / 30.0);
            Link.Send.Latency = ping / 2;
            Link.Receive.Latency = ping / 2;
            Client = new SessionClient(Link, trio._session.LocalAddress, new JoinRequest(name, "test", string.Empty), "igi1", NullLogger.Instance, _identity)
            {
                Mode = trio._session.Mode,
            };
            Game = Make(args, Window, _device);
            _watch = new Igi1CoopPair.FollowingWatch(Client);
            Game.TickObserver = _watch;
            Game.Start();
            Game.ScriptedViewInput = Input;
        }

        public string Name { get; }

        public GameHost Game { get; }

        public HeadlessGameWindow Window { get; } = new();

        public InputState Input { get; } = new();

        public SessionClient Client { get; }

        public SimulatedLinkTransport Link { get; }

        public Igi1Gameplay? Level => _watch.Game;

        public bool Joined => Client.LocalEntity is not null && Level is not null;

        public Game.Igi1.Player.PlayerCamera Own => Level!.World.Player!;

        public void Dispose()
        {
            Game.Stop();
            Client.Dispose();
            _device.Dispose();
            _identity.Dispose();
        }
    }

    // Keeps every line the server logs, as the release's log file would have it.
    private sealed class KeepingLogger(ConcurrentQueue<string> lines) : IOpenIgiLogger
    {
        public bool IsEnabled(LogLevel level, string category) => level >= LogLevel.Information;

        public void Log(in LogRecord record) => lines.Enqueue($"{record.Level} {record.Category}: {record.Message}");
    }
}
