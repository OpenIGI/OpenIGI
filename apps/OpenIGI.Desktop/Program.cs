using OpenIGI.Audio.OpenAL;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Desktop;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host;
using OpenIGI.Platform.Desktop;
using OpenIGI.Platform;
using OpenIGI.Rendering.Vulkan;
using OpenIGI.Rendering;

HostOptions options;
try
{
    // -vulkan is still swallowed rather than rejected. It names the only backend there is, so it does
    // nothing, but a shortcut or a script that has carried it since there were two should keep working.
    options = HostOptions.Parse(
        args.Where(a => !a.Equals("-vulkan", StringComparison.OrdinalIgnoreCase)).ToArray());
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(HostOptions.Usage);
    return 2;
}

OpenIGI.Host.Infrastructure.PersistentLogger persistentLogger;
try
{
    persistentLogger = new(RuntimePaths.GetLogPath());
}
catch (IOException)
{
    // Another instance already owns the log — a second copy for a capture or a comparison is a normal
    // thing to run, and it should not die over a file name. It logs beside the first, named by process.
    try
    {
        string sidecar = Path.Combine(
            Path.GetDirectoryName(RuntimePaths.GetLogPath()) ?? ".",
            FormattableString.Invariant($"runtime.{Environment.ProcessId}.log"));
        persistentLogger = new(sidecar);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        Console.Error.WriteLine($"Could not initialize the runtime log: {ex.Message}");
        return 2;
    }
}
catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Could not initialize the runtime log: {ex.Message}");
    return 2;
}

using (persistentLogger)
{
IIgiLogger logger = persistentLogger;

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

// The title this build runs, chosen here because the composition root is the only place that may name
// one. Everything below takes the runtime and never asks which game it is.
IGameTitleRuntime titleRuntime = DesktopTitleCatalog.Default;
GameTitle title = titleRuntime.Title;

// -game, then IGI_GAME_PATH, then whatever is lying around the executable.
string? gamePath = options.GamePath ?? GameProfileResolver.DiscoverDataRoot(searched, title);

if (string.IsNullOrWhiteSpace(gamePath))
{
    Console.Error.WriteLine($"No {title.DisplayName} installation found.");
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Put OpenIGI in the folder that holds {title.SignatureFile}, or point it at one:");
    Console.Error.WriteLine("  -game <dir>");
    Console.Error.WriteLine($"  {HostOptions.GamePathVariable}=<dir>");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Looked in:");

    foreach (string candidate in searched)
    {
        Console.Error.WriteLine($"  {candidate}");
    }

    return 2;
}

// Reported apart from the search, because a path somebody typed being wrong is a different mistake
// from there being nothing to find, and telling them the file that is missing is what makes a
// mistyped path or a half-copied installation obvious rather than mysterious.
GameProfile profile = new(title, gamePath);

if (!profile.IsInstalled)
{
    Console.Error.WriteLine(
        Directory.Exists(gamePath)
            ? $"'{gamePath}' is not a {title.DisplayName} installation -- no {title.SignatureFile} in it."
            : $"'{gamePath}' does not exist.");
    return 2;
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
    logger.Error("Desktop", ex.Message, ex);
    return 3;
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
    // The deferred, HDR renderer -- the G-buffer, cascaded shadows and the GI passes -- is the modern
    // path; the direct renderer is the vanilla fallback. It is on by default and chosen per preset by the
    // r_hdr/r_deferred cvars (BeginSceneCapture declines it where a preset does not ask for it), so vanilla
    // still draws direct. OPENIGI_VK_DEFERRED=0 forces the direct path for debugging.
    bool vulkanDeferred = !string.Equals(
        Environment.GetEnvironmentVariable("OPENIGI_VK_DEFERRED"),
        "0",
        StringComparison.Ordinal);
    using IGraphicsDevice device = new VulkanGraphicsDevice(
        new VulkanSurfaceProvider(window.VkSurface!),
        displayMode,
        vulkanValidation,
        vulkanDeferred,
        options.ScreenshotPath is not null,
        logger);

    // The composition root is the only place that names a concrete backend: the graphics device
    // above, and the audio factory here. GameHost itself sees only the abstractions.
    using GameHost game = new(
        platform, window, device, OpenAlAudioDeviceFactory.Instance, titleRuntime,
        DesktopTitleCatalog.PresentationFor(titleRuntime.Title),
        DesktopTitleCatalog.WorldBuilderFor(titleRuntime.Title),
        log => DesktopTitleCatalog.MissionCatalogueFor(titleRuntime.Title, log),
        fileSystem, options, gamePath, logger);
    return game.Run();
}
}
