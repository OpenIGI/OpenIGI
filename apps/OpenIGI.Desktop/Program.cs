using OpenIGI.Audio.OpenAL;
using OpenIGI.Composition;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Desktop;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net.Identity;
using OpenIGI.Net.Udp;
using OpenIGI.Net.WebRtc;
using OpenIGI.Host;
using OpenIGI.Host.Titles;
using OpenIGI.Modding;
using OpenIGI.Modding.Loader;
using OpenIGI.Modding.Presentation;
using OpenIGI.Platform.Desktop;
using OpenIGI.Platform;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Rhi;
using OpenIGI.Rendering.D3D12;
using OpenIGI.Rendering.D3D12.Rhi;
using OpenIGI.Rendering.OpenGL;
using OpenIGI.Rendering.Streamline;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Rendering;

// Before anything touches the console: a windowed build started from a terminal borrows it, so a capture
// or soak run from a shell still prints.
DesktopConsole.AttachToParent();

const string StartupLogCategory = "Startup";

// What a run returns when no device would start over the window's API, so the caller can try OpenGL.
const int NoRendererStarted = -1;

using DesktopLogging logging = DesktopLogging.Start();
IOpenIgiLogger logger = logging.Logger;
return Run(args, logger);

// The session the command line asks for: a server to join, or this game hosting one; nothing otherwise.
static IDisposable? StartSession(SessionArguments session, GameTitle title, HostOptions options, IOpenIgiLogger logger)
{
    string version = System.Reflection.Assembly.GetEntryAssembly()
        ?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";
    if (session.Join is not null || session.Host)
    {
        if (!DataChannelPeerFactory.IsAvailable())
        {
            logger.Warning("Session", "Rooms need libdatachannel beside the game (tools/native/build-datachannel.ps1); playing alone.");
            return null;
        }

        return RoomSessions.Start(session, title.Id, options.StartMission, version, new DataChannelPeerFactory(logger), logger);
    }

    JoinRequest join = new(session.PlayerName, version, session.Password);
    if (session.Demo is { } demo)
    {
        try
        {
            using FileStream file = File.OpenRead(demo);
            (DemoHeader header, IReadOnlyList<DemoMessage> messages) = DemoFile.Read(file);
            DemoPlayback playback = new(header, messages);
            logger.Information("Session", $"Playing {demo}: {header.Player}'s {header.Mode} on map {header.Map}, {playback.Length:0} s.");
            return new SessionClient(playback, playback.Server, join, title.Id, logger) { Mode = header.Mode };
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.Warning("Session", $"Cannot play {demo}: {e.Message}.");
            return null;
        }
    }

    if (session.Connect is { } target)
    {
        if (UdpTransport.Parse(target, SessionArguments.DefaultPort) is not { } server)
        {
            logger.Warning("Session", $"'{target}' is not a server address; playing alone.");
            return null;
        }

        return new SessionClient(UdpTransport.Open(0), server, join, title.Id, logger, reopen: () => UdpTransport.Open(0))
        {
            Mode = session.Mode,
        };
    }

    if (session.Listen)
    {
        string map = options.StartMission.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new HostedSession(
            UdpTransport.Open(session.Port),
            new HostedSessionOptions(session.ServerName, session.MaxPlayers, session.Password, session.Mode, map, version, IncludesHost: true, session.PlayerName),
            title.Id,
            OpenIGI.Engine.Time.GameClock.DefaultTickRate,
            logger);
    }

    return null;
}

// Reported where somebody will see it: the log and standard output always, and a dialog when the game was
// double-clicked and has nowhere else to say why it stopped.
static int Fail(IOpenIgiLogger logger, string message, int exitCode = 2, Exception? exception = null)
{
    logger.Error(StartupLogCategory, message, exception);
    DesktopConsole.ShowFailure("OpenIGI", message);
    return exitCode;
}

// What a window is made with for a renderer: a context of its own for OpenGL, and nothing for the rest, which each
// make a swapchain on it. OPENIGI_GL_VALIDATION asks for a debug context, which reports what the driver finds wrong.
static WindowGraphics GraphicsFor(RendererBackend backend) =>
    backend != RendererBackend.OpenGL ? WindowGraphics.Native
    : GlRhiDevice.ValidationFromEnvironment ? WindowGraphics.OpenGLDebug
    : WindowGraphics.OpenGL;

static int Run(string[] args, IOpenIgiLogger logger)
{
HostOptions options;
SessionArguments session;
try
{
    // The multiplayer options first, so the host's parser sees only its own.
    session = SessionArguments.Parse(args, out args);

    // -vulkan is still swallowed rather than rejected. It names the default backend, so it does nothing,
    // but a shortcut or a script that has carried it since there was a choice of renderers should keep
    // working. The backend is chosen by OPENIGI_RENDERER.
    options = HostOptions.Parse(
        args.Where(a => !a.Equals("-vulkan", StringComparison.OrdinalIgnoreCase)).ToArray());
}
catch (ArgumentException ex)
{
    return Fail(logger, $"{ex.Message}\n\n{HostOptions.Usage}\n\n{SessionArguments.Usage}");
}

// Where the executable is, then where it was run from. Dropping the port into the game's own folder
// is then the whole of the installation procedure, which is how somebody expects to install an add-on
// for a game of this age -- and it is the only route that works with no environment to set up.
// The executable's folder is asked for by process path rather than AppContext.BaseDirectory, because
// a single-file build runs from a temporary folder it unpacked itself into: BaseDirectory would name
// that, the installation procedure would stop working, and the error below would list a path nobody
// recognises. BaseDirectory is still searched after it, for the app's own shipped data.
string[] searched = new[] { SettingsPaths.ExecutableDirectory, AppContext.BaseDirectory, Environment.CurrentDirectory }
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

// The mods, applied to the games they name before either is resolved, so whichever is chosen is the modded one: the
// ones the launcher's profile (OPENIGI_MOD_PROFILE) switches on for each game, in its order; without one, every directory
// holding a mod.json under mods/ beside the executable, or under OPENIGI_MODS -- which, naming a folder, wins over the
// profile, so a mod's author can try a build from the launcher. OPENIGI_MODS=off plays the games as they ship.
string? modsSetting = Environment.GetEnvironmentVariable("OPENIGI_MODS");
if (!string.Equals(modsSetting, "off", StringComparison.OrdinalIgnoreCase))
{
    // A mod reading another game's data is played only where that game is installed; -game is the active title's.
    ModEnvironment machine = new(id =>
        (options.GamePath is not null && string.Equals(id, options.Title, StringComparison.OrdinalIgnoreCase))
        || TitleCatalog.IsInstalled(id));
    Func<GameTitle, ModSet> modsFor;
    if (Environment.GetEnvironmentVariable(ModProfile.EnvironmentVariable) is { Length: > 0 } profilePath
        && string.IsNullOrWhiteSpace(modsSetting))
    {
        IReadOnlyDictionary<string, ModSet> sets;
        try
        {
            sets = ModLoader.Load(ModProfile.Read(profilePath), machine, logger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            logger.Warning("Mods", $"The mod profile '{profilePath}' could not be read, so no mods are played: {ex.Message}");
            sets = new Dictionary<string, ModSet>();
        }

        modsFor = title => sets.GetValueOrDefault(title.Id) ?? ModSet.None;
    }
    else
    {
        ModSet mods = ModLoader.Load(
            string.IsNullOrWhiteSpace(modsSetting) ? Path.Combine(SettingsPaths.ExecutableDirectory, "mods") : modsSetting,
            machine,
            logger);
        modsFor = _ => mods;
    }

    foreach (TitlePackage package in TitleCatalog.Titles.Packages.ToArray())
    {
        ModSet mods = modsFor(package.Title);
        TitleCatalog.Titles.Replace(package with { Runtime = mods.Extend(package.Runtime) });

        // And what the mods draw, added to the game's own presenters.
        PresentationMods.Apply(mods, package.Title, package.Presentation.Presenters);
    }
}

// The title this build runs, chosen here because the composition root is the only place that may name
// one. Everything below takes the runtime and never asks which game it is.
//
// -title wins, then OPENIGI_TITLE, then a probe of what is actually installed. An id nobody recognises
// stops rather than falling back: a run asked for one game and quietly given another is worse than one
// that refuses.
IGameTitleRuntime? titleRuntime;
string? probed = null;

if (options.Title is { } requestedTitle)
{
    if (!TitleCatalog.TryResolve(requestedTitle, out titleRuntime))
    {
        return Fail(logger, $"'{requestedTitle}' is not a game this build runs.\n  -title {TitleCatalog.KnownTitleIds}");
    }
}
else
{
    titleRuntime = TitleCatalog.Probe(searched, out probed) ?? TitleCatalog.Default;
}

GameTitle title = titleRuntime.Title;

// -game, then the title's OWN path variable, then whatever the probe found or is lying around the
// executable. Per title, because a machine may have both installed and one variable cannot say where
// two of them are.
string? gamePath = options.GamePath
    ?? Environment.GetEnvironmentVariable(TitleCatalog.GamePathVariableFor(title))
    ?? probed
    ?? GameProfileResolver.DiscoverDataRoot(searched, title);

if (string.IsNullOrWhiteSpace(gamePath))
{
    return Fail(
        logger,
        string.Join(
            '\n',
            [
                $"No {title.DisplayName} installation found.",
                string.Empty,
                $"Put OpenIGI in the folder that holds {title.SignatureFile}, or point it at one:",
                "  -game <dir>",
                $"  {TitleCatalog.GamePathVariableFor(title)}=<dir>",
                string.Empty,
                $"To run a different game: -title {TitleCatalog.KnownTitleIds}",
                string.Empty,
                "Looked in:",
                .. searched.Select(static candidate => $"  {candidate}"),
            ]));
}

// Reported apart from the search, because a path somebody typed being wrong is a different mistake
// from there being nothing to find, and telling them the file that is missing is what makes a
// mistyped path or a half-copied installation obvious rather than mysterious.
GameProfile profile = new(title, gamePath);

if (!profile.IsInstalled)
{
    return Fail(
        logger,
        Directory.Exists(gamePath)
            ? $"'{gamePath}' is not a {title.DisplayName} installation -- no {title.SignatureFile} in it."
            : $"'{gamePath}' does not exist.");
}

VirtualFileSystem fileSystem = new(logger);
fileSystem.RegisterDevice(new LocalFileDevice(gamePath));

// The title's own alias bindings, overridable by a filesys.cfg beside the data. The original stores
// that file name in a global nothing reads and hard-codes its aliases in the modules that own them;
// this is the OpenIGI extension the config type was written for, and binding it here is what makes a
// data-file mod able to redirect an alias without touching the base install.
FileSystemConfig
    .LoadOrDefault(Path.Combine(gamePath, FileSystemConfig.DefaultFileName), titleRuntime.Layout)
    .ApplyTo(fileSystem);

DesktopPlatformHost platform = new();

// The API the renderer runs over: d3d12 for Direct3D 12 on Windows, vulkan, webgpu -- the browser's API through
// wgpu-native, which is how the web build's renderer is compared against the desktop's on one machine -- or opengl,
// for a machine with none of the others. OPENIGI_RENDERER for this run, then r_renderer as the command line or the
// settings page left it; neither, and it is RendererSwitchOrder.Default. Read before the window is made, because an
// OpenGL renderer needs a window made with a context; and here rather than by the console, which loads the settings
// only once there is a device to show it on -- and a capture reads no saved settings.
string? requested = Environment.GetEnvironmentVariable("OPENIGI_RENDERER");
string requestedBy = "OPENIGI_RENDERER";
if (string.IsNullOrEmpty(requested))
{
    requested = SettingsFile.LastAssignment(options.StartupCommands, DisplayCVars.Renderer.Name)
        ?? (options.Lockstep ? null : SettingsFile.ReadAssignment(SettingsPaths.GetSettingsPath(), DisplayCVars.Renderer.Name));
    requestedBy = DisplayCVars.Renderer.Name;
}

if (!RendererSwitchOrder.IsKnown(requested))
{
    logger.Warning(
        "Renderer",
        $"{requestedBy}={requested} names no renderer; drawing over {RendererSwitchOrder.Describe(RendererSwitchOrder.Default)}.");
}

RendererBackend backend = RendererSwitchOrder.Parse(requested);
WindowSettings settings = new(
    Title: "OpenIGI",
    Width: options.Width,
    Height: options.Height,
    Mode: options.Mode,
    CursorVisible: false,
    VSync: options.VSync,
    Graphics: GraphicsFor(backend));

SilkGameWindow window;
try
{
    window = (SilkGameWindow)platform.CreateWindow(settings);
}
catch (PlatformException ex)
{
    return Fail(logger, ex.Message, 3, ex);
}

// macOS reports a zero drawable for a window that has never been shown, and the swapchain is sized from
// it. Show it before the graphics device reads those dimensions. Other platforms keep the window hidden
// until DesktopGame.Run so boot does not present a blank focused surface.
if (OperatingSystem.IsMacOS())
{
    window.Show();
}

try
{
    DisplayReport.Report(window, options, logger);

    int width = window.Width > 0 ? window.Width : options.Width > 0 ? options.Width : 640;
    int height = window.Height > 0 ? window.Height : options.Height > 0 ? options.Height : 480;
    DisplayMode displayMode = new(width, height, 32, window.RefreshRate);

    // The device presents through its own swapchain, so it takes the window's surface rather than a
    // context and a present callback.
    bool vulkanValidation = string.Equals(
        Environment.GetEnvironmentVariable("OPENIGI_VK_VALIDATION"),
        "1",
        StringComparison.Ordinal);

    if (backend != RendererBackend.OpenGL && window.VkSurface is null && OperatingSystem.IsWindows())
    {
        // The window was made without Vulkan because there is no loader; Direct3D 12 is all there is.
        logger.Warning("Renderer", "No Vulkan loader on this machine; drawing over Direct3D 12.");
        backend = RendererBackend.D3D12;
    }

    // A demo brings up the map it was recorded on, played its way.
    if (session.Demo is { } demoPath && File.Exists(demoPath))
    {
        try
        {
            using FileStream file = File.OpenRead(demoPath);
            (DemoHeader header, _) = DemoFile.Read(file);
            session = session with { Mode = header.Mode };
            if (int.TryParse(header.Map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int demoMission))
            {
                options = options with { StartMission = demoMission };
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            logger.Warning("Session", $"Cannot read {demoPath}: {e.Message}.");
        }
    }

    // A server joined is asked first what it plays, so its level is brought up once, its way: the right map, an arena's or a
    // mission's. One that does not answer is joined on the command line's word, and followed to its map once welcomed.
    if (session.Connect is { } connect && UdpTransport.Parse(connect, SessionArguments.DefaultPort) is { } asked)
    {
        if (ServerProbe.Ask(UdpTransport.Open(0), asked, title.Id) is { } info)
        {
            logger.Information("Session", $"{connect} plays {info.Mode} on map {info.Map}.");
            session = session with { Mode = info.Mode };
            if (ServerProbe.MissionOf(info) is { } mission)
            {
                options = options with { StartMission = mission };
            }
        }
        else
        {
            logger.Warning("Session", $"{connect} did not answer before its level was loaded; joining on the command line's word.");
        }
    }

    HostOptions runOptions = options;

    // The game a run opens on, and where it is installed. The shell changes games without the process restarting, and
    // the file system it re-points is this one, so a renderer switch has to build the game that was running again --
    // not the one the process started on, which would read the other game's installation through its own readers.
    GameTitle runTitle = titleRuntime.Title;
    string runGamePath = gamePath;

    // Who this player is to every server: a key kept beside the settings, made on first play.
    using EcdsaPlayerIdentity identity = EcdsaPlayerIdentity.LoadOrCreate(SettingsPaths.GetFilePath("identity.key"));
    PlayerIdentities.Default = identity;

    // A session joined (-connect) or hosted (-listen) runs either side of the title's ticks, and outlives a renderer switch.
    using IDisposable? multiplayer = StartSession(session, title, options, logger);

    // NVIDIA's optional runtime (DLSS, ray reconstruction, frame generation, Reflex). Found once; each
    // Vulkan or Direct3D 12 device started over it opens a session of its own, and a WebGPU one never looks.
    StreamlineOptions? streamline = StreamlineLocator.Locate(options);
    logger.Information(
        "Streamline",
        streamline is null
            ? "No runtime found (install the NVIDIA DLSS pack from the launcher, or run tools/fetch-streamline.ps1); "
                + "the engine's own reconstruction is used."
            : $"Runtime folder '{streamline.EffectiveDirectory}'.");

    // The render loop runs on this thread, and the menu builds every backdrop on the pool beside it --
    // a texture pack's worth of pictures decoded and mipped on every core. A profile of the menu had the
    // pool at sixteen cores' worth while the frame waited its turn for one, so the picture stalled for as
    // long as the build ran. Above normal, the frame is scheduled first and the build takes what is left.
    Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

    // F10 (r_switch_renderer) ends the game to change API rather than to quit. Everything the game
    // holds on the GPU belongs to the device that made it, so the game is built again, over the other
    // API and on the same window, and put back where it was.
    while (true)
    {
        // A window is made with an OpenGL context or without one, and only the OpenGL renderer draws into one with
        // a context; changing between them is a new window, made before anything has been drawn into the old.
        if ((backend == RendererBackend.OpenGL) != (window.Graphics != WindowGraphics.Native))
        {
            window.Dispose();
            try
            {
                window = (SilkGameWindow)platform.CreateWindow(settings with { Graphics = GraphicsFor(backend) });
            }
            catch (PlatformException ex)
            {
                return Fail(logger, ex.Message, 3, ex);
            }
        }

        int code = RunOnce(ref backend, runOptions, out RendererSwitch? switched);

        // Nothing would start over the window's API. OpenGL is the last thing there is to try: over a new window.
        if (code == NoRendererStarted)
        {
            if (backend == RendererBackend.OpenGL)
            {
                return Fail(
                    logger,
                    "No graphics API would start: the game needs a driver offering Direct3D 12, Vulkan 1.2 or OpenGL 4.3. "
                        + "Updating the graphics driver may add one.",
                    3);
            }

            logger.Warning("Renderer", "Neither Direct3D 12 nor Vulkan would start here; drawing over OpenGL.");
            backend = RendererBackend.OpenGL;
            continue;
        }

        if (switched is null || window.IsClosing)
        {
            return code;
        }

        // The API the settings page chose, or the next in F10's order.
        backend = switched.Target is { } target ? RendererBackends.Resolve(target) : RendererSwitchOrder.Next(backend);
        runTitle = switched.Title ?? runTitle;
        runGamePath = switched.DataRoot ?? runGamePath;
        runOptions = runOptions with
        {
            StartMission = switched.MissionId,
            PlayCutscenes = false,
            Resume = switched,
            ShellTab = switched.ShellTab ?? runOptions.ShellTab,

            // What the first run was scripted to do has been done: a -runat that switched renderer would
            // switch again, and -entrycam would press Play over a mission already being played.
            DeferredCommands = [],
            EntryCamMission = 0,

            // And where it opened the camera: the resumed pose decides where the view is, and a flight
            // left in place would fly its path again from the start.
            CameraPosition = null,
            CameraEnd = null,
            CameraYawEnd = null,
            CameraPitchEnd = null,
            FlightFrames = null,
            CameraShake = 0f,
        };

        logger.Information("Renderer", $"Switching to {RendererSwitchOrder.Describe(backend)}.");
    }

    int RunOnce(ref RendererBackend backend, HostOptions runOptions, out RendererSwitch? switched)
    {
        switched = null;

        // OpenGL where asked for, or where nothing else started: over a window made with its context.
        using GlRhiDevice? openGl = backend == RendererBackend.OpenGL ? TryCreateOpenGL() : null;
        if (backend == RendererBackend.OpenGL && openGl is null)
        {
            return NoRendererStarted;
        }

        // WebGPU where asked for: a window it cannot present to, or a wgpu-native that will not start, says why and draws
        // over the native API instead of ending the game.
        using WebGpuRhiDevice? webGpu = backend == RendererBackend.WebGpu ? TryCreateWebGpu() : null;
        if (backend == RendererBackend.WebGpu && webGpu is null)
        {
            backend = RendererBackend.Vulkan;
        }

        if (backend == RendererBackend.Vulkan && window.VkSurface is null)
        {
            backend = RendererBackend.D3D12;
        }

        // Direct3D 12 first, where asked for: a machine short of its baseline -- no Agility runtime beside
        // the executable, an adapter below shader model 6.6 -- says why and draws over Vulkan instead.
        using D3D12Rhi? d3d12 = backend == RendererBackend.D3D12 ? TryCreateD3D12() : null;
        if (backend == RendererBackend.D3D12 && d3d12 is null)
        {
            backend = RendererBackend.Vulkan;
        }

        using VulkanRhi? vulkan = backend == RendererBackend.Vulkan ? TryCreateVulkan() : null;
        if (backend == RendererBackend.Vulkan && vulkan is null)
        {
            return NoRendererStarted;
        }

        IRhiDevice rhiDevice = openGl ?? d3d12?.Device ?? vulkan?.Device ?? webGpu!;

        using IGraphicsDevice device = new GraphicsDevice(
            rhiDevice,
            displayMode,
            runOptions.ScreenshotPath is not null,
            logger);

        logger.Information(
            "Renderer",
            $"Drawing over {RendererSwitchOrder.Describe(backend)} on {rhiDevice.AdapterName}.");

        // The composition root is the only place that names a concrete backend: the graphics device
        // above, and the audio factory here. GameHost itself sees only the abstractions -- and which API
        // was built, after any fallback, so it can tell when r_renderer asks for a different one.
        using GameHost game = new(
            platform, window, device, OpenAlAudioDeviceFactory.Instance, ThreadPoolWorkScheduler.Instance, AlwaysReadyLoadGate.Instance,
            TitleCatalog.Catalog, runTitle,
            fileSystem, runOptions with { Renderer = backend }, runGamePath, logger);
        game.TickObserver = multiplayer as IHostTickObserver;

        // The editor ships with the desktop build only: the browser and server builds have no factory and so no editor.
        // Its folder picker is the system's, owned by the game's window so it stays in front.
        nint owner = window.NativeHandles is { System: DesktopWindowSystem.Win32 } owning ? owning.Window : 0;
        game.EditorFactory = new OpenIGI.Editor.EditorFactory { PickFolder = (title, initial) => FolderPicker.PickAsync(title, initial, owner) };
        game.RoomPeers = DataChannelPeerFactory.IsAvailable() ? new DataChannelPeerFactory(logger) : null;
        game.OpenUdp = port => UdpTransport.Open(port);
        int code = game.Run();
        switched = game.RendererSwitchRequest;
        return code;
    }

    WebGpuRhiDevice? TryCreateWebGpu()
    {
        try
        {
            return WebGpuWindow.Create(window, width, height, vsync: true);
        }
        catch (Exception ex) when (ex is PlatformException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            logger.Warning("Renderer", $"WebGPU is not available here ({ex.Message}); drawing over the native renderer.");
            return null;
        }
    }

    VulkanRhi? TryCreateVulkan()
    {
        if (window.VkSurface is not { } surface)
        {
            logger.Warning("Renderer", "This window has no Vulkan surface; Vulkan cannot draw into it.");
            return null;
        }

        // For trying the fallback on a machine that has Vulkan: as if it would not start.
        if (Environment.GetEnvironmentVariable("OPENIGI_VULKAN_FORCE_FAIL") is "1")
        {
            logger.Warning("Renderer", "OPENIGI_VULKAN_FORCE_FAIL=1: Vulkan is treated as unavailable.");
            return null;
        }

        try
        {
            return VulkanRhi.CreateForSurface(
                new VulkanSurfaceProvider(surface),
                width,
                height,
                vsync: true,
                logger,
                vulkanValidation,
                streamline);
        }
        catch (GraphicsDeviceException ex)
        {
            logger.Warning("Renderer", $"Vulkan is not available here ({ex.Message}).");
            return null;
        }
    }

    GlRhiDevice? TryCreateOpenGL()
    {
        try
        {
            return GlRhiDevice.Create(new SilkGlSurface(window), width, height, vsync: true, logger, GlRhiDevice.ValidationFromEnvironment);
        }
        catch (OpenGLUnavailableException ex)
        {
            logger.Error("Renderer", ex.Message);
            return null;
        }
    }

    D3D12Rhi? TryCreateD3D12()
    {
        if (window.NativeHandles is not { System: DesktopWindowSystem.Win32 } handles)
        {
            logger.Warning("Renderer", "Direct3D 12 needs a Win32 window; drawing over Vulkan.");
            return null;
        }

        try
        {
            return D3D12Rhi.CreateForWindow(handles.Window, width, height, vsync: true, logger, D3D12Rhi.ValidationFromEnvironment, streamline);
        }
        catch (D3D12UnavailableException ex)
        {
            logger.Warning("Renderer", $"Direct3D 12 is not available here ({ex.Message}); drawing over Vulkan.");
            return null;
        }
    }
}
finally
{
    window.Dispose();
}
}
