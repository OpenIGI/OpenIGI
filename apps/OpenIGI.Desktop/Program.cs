using OpenIGI.Core.Diagnostics;
using OpenIGI.Desktop;
using OpenIGI.Desktop.Infrastructure;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Profiles;
using OpenIGI.Platform;
using OpenIGI.Platform.Desktop;
using OpenIGI.Rendering;
using OpenIGI.Rendering.OpenGL;
using Silk.NET.OpenGL;

DesktopOptions options;
try
{
    options = DesktopOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(DesktopOptions.Usage);
    return 2;
}

OpenIGI.Desktop.Infrastructure.PersistentLogger persistentLogger;
try
{
    persistentLogger = new(RuntimePaths.GetLogPath());
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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
string[] searched = [AppContext.BaseDirectory, Environment.CurrentDirectory];

// -game, then IGI_GAME_PATH, then whatever is lying around the executable.
string? gamePath = options.GamePath ?? GameProfileResolver.DiscoverDataRoot(searched);

if (string.IsNullOrWhiteSpace(gamePath))
{
    Console.Error.WriteLine("No Project I.G.I. installation found.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Put OpenIGI in the folder that holds IGI.exe, or point it at one:");
    Console.Error.WriteLine("  -game <dir>");
    Console.Error.WriteLine($"  {DesktopOptions.GamePathVariable}=<dir>");
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
GameProfile profile = new(GameTitle.Igi1, gamePath);

if (!profile.IsInstalled)
{
    Console.Error.WriteLine(
        Directory.Exists(gamePath)
            ? $"'{gamePath}' is not a Project I.G.I. installation -- no {GameTitle.Igi1.SignatureFile} in it."
            : $"'{gamePath}' does not exist.");
    return 2;
}

VirtualFileSystem fileSystem = new(logger);
fileSystem.RegisterDevice(new LocalFileDevice(gamePath));

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

// macOS reports a zero drawable for a hidden OpenGL window. Show it before the graphics device reads
// the drawable dimensions. Other platforms keep the window hidden until DesktopGame.Run so boot does
// not present a blank focused surface.
if (OperatingSystem.IsMacOS())
{
    window.Show();
}

using (window)
{
    DisplayReport.Report(window, options, logger);

    GL gl = GL.GetApi(window.GLContext);

    int width = window.Width > 0 ? window.Width : options.Width > 0 ? options.Width : 640;
    int height = window.Height > 0 ? window.Height : options.Height > 0 ? options.Height : 480;
    using GLGraphicsDevice device = new(
        gl,
        new DisplayMode(width, height, 32, window.RefreshRate),
        window.SwapBuffers,
        logger);

    using DesktopGame game = new(platform, window, device, fileSystem, options, gamePath, logger);
    return game.Run();
}
}
