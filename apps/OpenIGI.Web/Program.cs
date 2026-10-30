using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using OpenIGI.Audio.Web;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Net.Browser;
using OpenIGI.Platform;
using OpenIGI.Platform.Web;
using OpenIGI.Rendering;
using OpenIGI.Rendering.Pipeline;
using OpenIGI.Core.Configuration.Presets;
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
    private static IDisposable? _session;

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

        // The browser's developer console, plus the in-game console once the game attaches it.
        LogRouter logger = new LogRouter().AddSink(new ConsoleSink());
        // The multiplayer options first, so the host's parser sees only its own. A page has no UDP socket: it plays
        // with others through rooms alone (-host, -join <code>), over the page's own WebRTC.
        SessionArguments session = SessionArguments.Parse(args ?? [], out string[] rest);
        if (session.Connect is not null || session.Listen)
        {
            logger.Warning("Session", "The browser can't open a UDP port: host or join a room instead (-host, -join <code>).");
        }

        // The browser build is the original's picture: the preset is held on Vanilla whatever was saved.
        HostOptions options = HostOptions.Parse(rest) with
        {
            LockedPreset = GraphicsPreset.Vanilla,
        };
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

        GraphicsDevice device = new(rhi, new DisplayMode(window.Width, window.Height, 32, window.RefreshRate), logger: logger);
        logger.Information("Renderer", $"Drawing through the RHI over WebGPU on {rhi.AdapterName}.");

        // The composition root names the backends: the RHI over WebGPU above, and the Web Audio API here.
        // GameHost itself sees only the abstractions, exactly as it does on the desktop.
        _game = new GameHost(
            platform,
            window,
            device,
            new WebAudioDeviceFactory(new WebAudioPage()),
            InlineWorkScheduler.Instance,
            new WebLoadGate(),
            TitleCatalog.Rooted(runtime, GameRoot),
            runtime.Title,
            fileSystem,
            options,
            GameRoot,
            logger);

        _session = RoomSessions.Start(session, runtime.Title.Id, options.StartMission, Version(), new BrowserRtcPeerFactory(), logger);
        _game.TickObserver = _session as IHostTickObserver;
        _game.RoomPeers = new BrowserRtcPeerFactory();
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
        _session?.Dispose();
        _session = null;
        return false;
    }

    private static string Version() =>
        typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "dev";
}
