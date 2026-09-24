using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Platform;
using OpenIGI.Platform.Web;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Rendering.Presets;
using OpenIGI.Rendering.WebGpu;

namespace OpenIGI.Web;

/// <summary>The browser's composition root: what the page starts, and steps once a frame.</summary>
/// <remarks>
/// <para>
/// The desktop's root runs the game to the end in one call. A page cannot: the browser has to get its
/// thread back between frames to deliver input, present, and read files. So the page calls
/// <see cref="Boot"/> once, when the adapter is acquired and the picked files are in place, and then
/// <see cref="Step"/> from each animation frame until it says the game has finished.
/// </para>
/// <para>
/// The installation is read from <see cref="GameRoot"/>, where the page writes the picked files into the
/// runtime's in-memory file system -- so the host reads them by path, exactly as it reads a disk.
/// </para>
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class Program
{
    /// <summary>Where the page puts the installation.</summary>
    public const string GameRoot = "/game";

    private static GameHost? _game;

    /// <summary>Nothing: the page boots the game once its files are in place.</summary>
    public static void Main()
    {
    }

    /// <summary>Builds the game over the page's canvas and starts it.</summary>
    /// <param name="args">Command-line options, as the desktop takes them, from the page's address.</param>
    [JSExport]
    public static void Boot(string[] args)
    {
        if (_game is not null)
        {
            throw new InvalidOperationException("The game has already booted.");
        }

        ConsoleLogger logger = new();
        // A first visit starts on High rather than the desktop's Enhanced; a saved choice still wins.
        HostOptions options = HostOptions.Parse(args ?? []) with { FirstRunPreset = GraphicsPreset.High };
        IGameTitleRuntime runtime = TitleCatalog.Default;

        VirtualFileSystem fileSystem = new(logger);
        fileSystem.RegisterDevice(new LocalFileDevice(GameRoot));
        FileSystemConfig
            .LoadOrDefault(Path.Combine(GameRoot, FileSystemConfig.DefaultFileName), runtime.Layout)
            .ApplyTo(fileSystem);

        WebPlatformHost platform = new(new BrowserPage());
        IGameWindow window = platform.CreateWindow(new WindowSettings(
            Title: "OpenIGI",
            Width: 0,
            Height: 0,
            Mode: WindowMode.Windowed,
            CursorVisible: false,
            VSync: true));

        WebGpuRhiDevice rhi = WebGpuRhiDevice.CreateForWindow(
            new WebGpuWindowHandles(WebGpuWindowSystem.Canvas, 0, 0),
            window.Width,
            window.Height);

        GraphicsDevice device = new(rhi, new DisplayMode(window.Width, window.Height, 32, window.RefreshRate));
        logger.Information("Renderer", $"Drawing through the RHI over WebGPU on {rhi.AdapterName}.");

        _game = new GameHost(
            platform,
            window,
            device,
            NullAudioDeviceFactory.Instance,
            InlineWorkScheduler.Instance,
            new WebLoadGate(),
            TitleCatalog.Rooted(runtime, GameRoot),
            runtime.Title,
            fileSystem,
            options,
            GameRoot,
            logger);

        _game.Start();
    }

    /// <summary>Runs one frame.</summary>
    /// <returns><see langword="true"/> while the game goes on; <see langword="false"/> once it has stopped.</returns>
    [JSExport]
    public static bool Step()
    {
        if (_game is null)
        {
            return false;
        }

        if (_game.Step())
        {
            return true;
        }

        _game.Stop();
        _game.Dispose();
        _game = null;
        return false;
    }
}
