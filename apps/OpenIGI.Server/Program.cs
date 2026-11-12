using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Dedicated;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Net;
using OpenIGI.Net.Rooms;
using OpenIGI.Net.Udp;
using OpenIGI.Net.WebRtc;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.Server;

// The dedicated server: the game's own host, run with no window, no picture and no sound, its level ticking in real time,
// and the multiplayer session beside it. Everything the level does is the same code a client runs. Its settings come from
// server.cfg (see ServerConfig), the command line winning; desktops join it on its UDP port, browsers through its WebRTC room,
// and its operator types commands at its terminal.

const string LogCategory = "Server";

using LogRouter logger = new();
logger.AddSink(new ConsoleSink(minimumLevel: LogLevel.Information));

SessionArguments session;
HostOptions hostOptions;
string configPath = ServerConfig.DefaultFileName;
try
{
    string[] rest = ServerArguments.TakeConfig(args, ref configPath);
    session = SessionArguments.Parse(rest, out string[] hostArgs);
    hostOptions = HostOptions.Parse(hostArgs);
}
catch (ArgumentException ex)
{
    logger.Error(LogCategory, $"{ex.Message}\n\n  -config <file>          the server's settings (default server.cfg)\n\n{SessionArguments.Usage}\n\n{HostOptions.Usage}");
    return 2;
}

// No settings yet (a first run, a fresh container): the shipped example's, said so.
if (!File.Exists(configPath) && Path.Combine(AppContext.BaseDirectory, "server.cfg.example") is { } example && File.Exists(example))
{
    logger.Warning(LogCategory, $"No {configPath}: running on the example's settings. Copy {example} to {configPath} and edit it.");
    configPath = example;
}

List<string> problems = [];
ServerConfig config = ServerArguments.Merge(ServerConfig.Load(configPath, problems), session, args);
foreach (string problem in problems)
{
    logger.Warning(LogCategory, $"{configPath}: {problem}");
}

if (config.LogFile.Length > 0)
{
    logger.AddSink(FileSink.Open(config.LogFile, keep: 8, minimumLevel: LogLevel.Information));
}

// The first map: the command line's, else the rotation's first.
int mission = hostOptions.StartMission > 0 ? hostOptions.StartMission : config.Rotation.Count > 0 ? config.Rotation[0].Mission : 0;
string mode = ServerArguments.Given(args, "-mode") ? session.Mode : config.Rotation.Count > 0 && config.Rotation[0].Mode.Length > 0 ? config.Rotation[0].Mode : session.Mode;
if (mission <= 0)
{
    logger.Error(LogCategory, "A dedicated server needs a map: -mission <id>, or map lines in server.cfg.");
    return 2;
}

IGameTitleRuntime? titleRuntime = TitleCatalog.Default;
if (hostOptions.Title is { } requested && !TitleCatalog.TryResolve(requested, out titleRuntime))
{
    logger.Error(LogCategory, $"'{requested}' is not a game this build runs.\n  -title {TitleCatalog.KnownTitleIds}");
    return 2;
}

GameTitle title = titleRuntime.Title;
string? gamePath = hostOptions.GamePath ?? Environment.GetEnvironmentVariable(TitleCatalog.GamePathVariableFor(title)) ?? (GameInstalls.Find(title) is [var found, ..] ? found : null);
if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
{
    logger.Error(LogCategory, $"No {title.DisplayName} installation: pass -game <dir> or set {TitleCatalog.GamePathVariableFor(title)}.");
    return 2;
}

logger.Information(LogCategory, $"Game data from {gamePath}.");
VirtualFileSystem fileSystem = new(logger);
fileSystem.RegisterDevice(new LocalFileDevice(gamePath));
FileSystemConfig.LoadOrDefault(Path.Combine(gamePath, FileSystemConfig.DefaultFileName), titleRuntime.Layout).ApplyTo(fileSystem);

// The cutscene is the clients' to watch; the server goes straight to play.
HostOptions runOptions = hostOptions with { PlayCutscenes = false, StartMission = mission, SimulationOnly = true };
using RecordingGraphicsDevice device = new() { IsRecording = false };
using GameHost host = new(
    new HeadlessPlatformHost(),
    new HeadlessGameWindow(),
    device,
    NullAudioDeviceFactory.Instance,
    InlineWorkScheduler.Instance,
    AlwaysReadyLoadGate.Instance,
    TitleCatalog.Catalog,
    title,
    fileSystem,
    runOptions,
    gamePath,
    logger);

string version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
string map = mission.ToString(CultureInfo.InvariantCulture);

// Desktops on the UDP port; browsers (and desktops behind anything) through the room, when this machine can make WebRTC.
INetTransport transport = UdpTransport.Open(config.Port);
RoomTransport? room = null;
if (config.Room && DataChannelPeerFactory.IsAvailable())
{
    RoomRequest request = new(
        title.Id,
        MultiplayerProtocol.IdFor(title.Id).ToString("x16", CultureInfo.InvariantCulture),
        config.Name,
        map,
        mode,
        version,
        config.MaxPlayers,
        config.Public,
        config.Password.Length > 0);
    // A server outlives any one room: when the master restarts, or is out of reach for longer than a room waits, it opens
    // another, so browsers can still join it weeks on.
    room = RoomTransport.Host(config.Master, request, new DataChannelPeerFactory(logger), logger, reopens: true);
    transport = new MultiTransport(transport, room);
}
else if (config.Room)
{
    logger.Warning(LogCategory, "No WebRTC library beside the server: browsers cannot join it, desktops can.");
}

// A recording of the session is kept only where the server's settings ask for one.
OpenIGI.Game.Multiplayer.NetCVars.HostRecord.Value = config.RecordDemos;
using HostedSession server = new(
    transport,
    new HostedSessionOptions(config.Name, config.MaxPlayers, config.Password, mode, map, version, IncludesHost: false, MaxPerAddress: config.MaxPerAddress),
    title.Id,
    GameClock.DefaultTickRate,
    logger);
host.TickObserver = server;

BanList bans = new(config.BansFile);
if (bans.Load() is { } banProblem)
{
    logger.Warning(LogCategory, banProblem);
}

MasterHeartbeat? heartbeat = config.Public ? new MasterHeartbeat(config.Master, logger) : null;
using DedicatedServer dedicated = new(
    server,
    config,
    bans,
    host.GoToMission,
    logger,
    heartbeat,
    // A closed room is never named: browsers sent to it would only be told there is no such room.
    () => MasterHeartbeat.BodyOf(server, config, title.Id, version, room is { Status: not RoomStatus.Closed } open ? open.Code : null),
    () => host.MissionList)
{
    Arenas = host.HasArena,
};
if (GameModes.IsVersus(mode) && !host.HasArena(mission))
{
    logger.Warning(LogCategory, $"Mission {map} has no multiplayer layout, so {mode} plays it from the level's own start; the laid-out maps are {string.Join(", ", host.ArenaMissionList.Select(m => m.Id))}.");
}
logger.Information(
    LogCategory,
    $"{title.DisplayName} server '{config.Name}' on UDP {config.Port}, mission {map} ({mode}), up to {config.MaxPlayers} players, {(config.Public ? "listed" : "unlisted")}.");
logger.Information(LogCategory, "Type help for the console's commands.");

// Ctrl+C in the terminal, or a service manager's stop, ends the run between steps.
bool stopping = false;
void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    stopping = true;
}

using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);

// The operator's commands, read off the loop's thread and run on it. A server with no terminal (a service) reads none.
ConcurrentQueue<string> commands = new();
OperatorInput.Start(commands, always: ServerArguments.Given(args, "-console"));

using TimerResolution timer = TimerResolution.Fine();
Stopwatch clock = Stopwatch.StartNew();
string? roomTold = null;
host.Start();
while (!stopping && !dedicated.QuitAsked && host.Step())
{
    while (commands.TryDequeue(out string? line))
    {
        foreach (string answer in dedicated.Console.Execute(line))
        {
            logger.Information(LogCategory, answer);
        }
    }

    dedicated.Tick(clock.Elapsed.TotalSeconds);
    if (room?.Code is { } code && code != roomTold)
    {
        roomTold = code;
        logger.Information(LogCategory, $"Room {code}: browsers join with it at {OpenIGI.Net.Rooms.RoomTransport.InviteLink(code)}.");
    }

    // A host step ends as soon as its frame is done; with nothing to draw, the next tick is most of a period away. With the
    // timer at a millisecond the sleep is one, not Windows' default 15.6 that made the ticks uneven.
    Thread.Sleep(1);
}

host.Stop();
logger.Information(LogCategory, "Server stopped.");
return 0;
