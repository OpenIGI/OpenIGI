using OpenIGI.Core.Diagnostics;
using OpenIGI.Desktop;
using OpenIGI.Engine.Vfs;
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

IIgiLogger logger = new ConsoleLogger();

string? gamePath = options.GamePath ?? @"S:\I'm Going In\pc";
if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
{
    Console.Error.WriteLine(
        gamePath is null
            ? $"No original installation configured. Pass -game <dir> or set {DesktopOptions.GamePathVariable}."
            : $"'{gamePath}' does not exist. Pass -game <dir> or set {DesktopOptions.GamePathVariable}.");
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

using (window)
{
    DisplayReport.Report(window, options, logger);

    GL gl = GL.GetApi(window.GLContext);

    using GLGraphicsDevice device = new(
        gl,
        new DisplayMode(window.Width, window.Height, 32, window.RefreshRate),
        window.SwapBuffers,
        logger);

    using DesktopGame game = new(platform, window, device, fileSystem, options, gamePath, logger);
    return game.Run();
}
