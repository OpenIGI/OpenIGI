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
using OpenIGI.Rendering.Streamline;
using OpenIGI.Rendering.Vulkan;
using OpenIGI.Rendering.Vulkan.Rhi;
using OpenIGI.Rendering.WebGpu;
using OpenIGI.Rendering;

// Before anything touches the console: a windowed build started from a terminal borrows it, so a capture
// or soak run from a shell still prints.
DesktopConsole.AttachToParent();

const string StartupLogCategory = "Startup";

using DesktopLogging logging = DesktopLogging.Start();
IOpenIgiLogger logger = logging.Logger;
return Run(args, logger);

// Reported where somebody will see it: the log and standard output always, and a dialog when the game was
// double-clicked and has nowhere else to say why it stopped.
static int Fail(IOpenIgiLogger logger, string message, int exitCode = 2, Exception? exception = null)
{
    logger.Error(StartupLogCategory, message, exception);
    DesktopConsole.ShowFailure("OpenIGI", message);
    return exitCode;
}

static int Run(string[] args, IOpenIgiLogger logger)
{
HostOptions options;
try
{
    // -vulkan is still swallowed rather than rejected. It names the default backend, so it does nothing,
    // but a shortcut or a script that has carried it since there was a choice of renderers should keep
    // working. The backend is chosen by OPENIGI_RENDERER.
    options = HostOptions.Parse(
        args.Where(a => !a.Equals("-vulkan", StringComparison.OrdinalIgnoreCase)).ToArray());
}
catch (ArgumentException ex)
{
    return Fail(logger, $"{ex.Message}\n\n{HostOptions.Usage}");
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
WindowSettings settings = new(
    Title: "OpenIGI",
    Width: options.Width,
    Height: options.Height,
    Mode: options.Mode,
    CursorVisible: false,
    VSync: options.VSync);

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

using (window)
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

    // The API the renderer runs over: d3d12 for Direct3D 12 on Windows, vulkan, or webgpu -- the browser's
    // API through wgpu-native, which is how the web build's renderer is compared against the desktop's on
    // one machine. OPENIGI_RENDERER for this run, then r_renderer as the command line or the settings page
    // left it; neither, and it is RendererSwitchOrder.Default. Read here rather than by the console, which
    // loads the settings only once there is a device to show it on -- and a capture reads no saved settings.
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
    if (window.VkSurface is null && OperatingSystem.IsWindows())
    {
        // The window was made without Vulkan because there is no loader; Direct3D 12 is all there is.
        logger.Warning("Renderer", "No Vulkan loader on this machine; drawing over Direct3D 12.");
        backend = RendererBackend.D3D12;
    }

    HostOptions runOptions = options;

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
        int code = RunOnce(ref backend, runOptions, out RendererSwitch? switched);
        if (switched is null || window.IsClosing)
        {
            return code;
        }

        // The API the settings page chose, or the next in F10's order.
        backend = switched.Target is { } target ? RendererBackends.Resolve(target) : RendererSwitchOrder.Next(backend);
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

        using VulkanRhi? vulkan = backend == RendererBackend.Vulkan
            ? VulkanRhi.CreateForSurface(
                new VulkanSurfaceProvider(window.VkSurface!),
                width,
                height,
                vsync: true,
                logger,
                vulkanValidation,
                streamline)
            : null;

        using WebGpuRhiDevice? webGpu = backend == RendererBackend.WebGpu
            ? WebGpuWindow.Create(window, width, height, vsync: true)
            : null;

        IRhiDevice rhiDevice = d3d12?.Device ?? vulkan?.Device ?? webGpu!;

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
            TitleCatalog.Catalog, titleRuntime.Title,
            fileSystem, runOptions with { Renderer = backend }, gamePath, logger);
        int code = game.Run();
        switched = game.RendererSwitchRequest;
        return code;
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
}
