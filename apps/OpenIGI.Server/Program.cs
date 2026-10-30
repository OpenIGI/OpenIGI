using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Net.Udp;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.Server;

// The dedicated server: the game's own host, run with no window, no picture and no sound, its level ticking in real time,
// and the multiplayer session beside it. Everything the level does is the same code a client runs.

const string LogCategory = "Server";

using LogRouter logger = new();
logger.AddSink(new ConsoleSink(minimumLevel: LogLevel.Information));

ServerOptions serverOptions;
HostOptions hostOptions;
try
{
    serverOptions = ServerOptions.Parse(args, out string[] hostArgs);
    hostOptions = HostOptions.Parse(hostArgs);
}
catch (ArgumentException ex)
{
    logger.Error(LogCategory, $"{ex.Message}\n\n{ServerOptions.Usage}\n\n{HostOptions.Usage}");
    return 2;
}

if (hostOptions.StartMission <= 0)
{
    logger.Error(LogCategory, "A dedicated server needs a map: -mission <id>.");
    return 2;
}

IGameTitleRuntime? titleRuntime = TitleCatalog.Default;
if (hostOptions.Title is { } requested && !TitleCatalog.TryResolve(requested, out titleRuntime))
{
    logger.Error(LogCategory, $"'{requested}' is not a game this build runs.\n  -title {TitleCatalog.KnownTitleIds}");
    return 2;
}

GameTitle title = titleRuntime!.Title;
string? gamePath = hostOptions.GamePath ?? Environment.GetEnvironmentVariable(TitleCatalog.GamePathVariableFor(title));
if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
{
    logger.Error(LogCategory, $"No {title.DisplayName} installation: pass -game <dir> or set {TitleCatalog.GamePathVariableFor(title)}.");
    return 2;
}

VirtualFileSystem fileSystem = new(logger);
fileSystem.RegisterDevice(new LocalFileDevice(gamePath));
FileSystemConfig.LoadOrDefault(Path.Combine(gamePath, FileSystemConfig.DefaultFileName), titleRuntime.Layout).ApplyTo(fileSystem);

// The cutscene is the clients' to watch; the server goes straight to play.
HostOptions runOptions = hostOptions with { PlayCutscenes = false };
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
string map = hostOptions.StartMission.ToString(CultureInfo.InvariantCulture);
using DedicatedServer server = new(UdpTransport.Open(serverOptions.Port), serverOptions, title.Id, map, version, logger);
logger.Information(LogCategory, $"{title.DisplayName} server '{serverOptions.Name}' on UDP {server.LocalAddress.Port}, map {map}, up to {serverOptions.MaxPlayers} players.");

// Ctrl+C in the terminal, or a service manager's stop, ends the run between steps.
bool stopping = false;
void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    stopping = true;
}

using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);

Stopwatch clock = Stopwatch.StartNew();
host.Start();
while (!stopping && host.Step())
{
    server.Pump(clock.Elapsed.TotalSeconds);

    // A host step ends as soon as its frame is done; with nothing to draw, the next tick is most of a period away.
    Thread.Sleep(1);
}

host.Stop();
logger.Information(LogCategory, "Server stopped.");
return 0;
