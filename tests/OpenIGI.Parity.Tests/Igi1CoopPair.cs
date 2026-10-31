using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Input;
using OpenIGI.Net;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering.Headless;
using OpenIGI.TestSupport;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// Two real games playing one mission together, run headless in one process: a host whose level is the session's, and
/// a joiner with its own copy of the level that the host's snapshots keep in step -- the pair the rooms connect, without
/// the rooms. Each is stepped a frame in turn, and each player is moved by the keys its own window holds.
/// </summary>
internal sealed class Igi1CoopPair : IDisposable
{
    private readonly RecordingGraphicsDevice _hostDevice = new() { IsRecording = false };
    private readonly RecordingGraphicsDevice _joinerDevice = new() { IsRecording = false };
    private readonly HostedSession _session;
    private readonly LoopbackNetwork _network = new();
    private SessionClient _client;
    private readonly Watch _hostWatch;
    private readonly FollowingWatch _joinerWatch;

    /// <summary>Starts both games on a mission; the joiner connects once the host's level is up.</summary>
    /// <param name="mission">The mission both play.</param>
    /// <param name="frames">How many frames each may run.</param>
    /// <param name="hostArgs">More of the host's command line, such as a <c>-runat</c>.</param>
    public Igi1CoopPair(int mission = 1, int frames = 20000, params string[] hostArgs)
    {
        Core.Configuration.CVarRegistry.Default.ResetAll();
        string[] args = ["-mission", mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep", "-frames", frames.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        _session = new HostedSession(
            _network.Open(26001),
            new HostedSessionOptions("coop", 4, string.Empty, "coop", mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "test", IncludesHost: true),
            "igi1",
            30,
            NullLogger.Instance);
        _client = NewClient();

        HostGame = Make([.. args, .. hostArgs], HostWindow, _hostDevice);
        JoinerGame = Make(args, JoinerWindow, _joinerDevice);
        _hostWatch = new Watch(_session);
        _joinerWatch = new FollowingWatch(_client);
        HostGame.TickObserver = _hostWatch;
        JoinerGame.TickObserver = _joinerWatch;
        HostGame.Start();
        JoinerGame.Start();
        HostGame.ScriptedViewInput = HostInput;
        JoinerGame.ScriptedViewInput = JoinerInput;
    }

    /// <summary>Gets the hosting game.</summary>
    public GameHost HostGame { get; }

    /// <summary>Gets the joining game.</summary>
    public GameHost JoinerGame { get; }

    /// <summary>Gets the host's window.</summary>
    public HeadlessGameWindow HostWindow { get; } = new();

    /// <summary>Gets the joiner's window.</summary>
    public HeadlessGameWindow JoinerWindow { get; } = new();

    /// <summary>Gets the input the host's player is moved by, as a lockstep run reads it.</summary>
    public InputState HostInput { get; } = new();

    /// <summary>Gets the input the joiner's player is moved by.</summary>
    public InputState JoinerInput { get; } = new();

    /// <summary>Gets the host's session.</summary>
    public HostedSession Session => _session;

    /// <summary>Gets the joiner's side of the session.</summary>
    public SessionClient Client => _client;

    /// <summary>Gets the host's level, once it is up.</summary>
    public Igi1Gameplay? Host => _hostWatch.Game;

    /// <summary>Gets the joiner's level, once it is up.</summary>
    public Igi1Gameplay? Joiner => _joinerWatch.Game;

    /// <summary>Gets whether the joiner has been welcomed into the session.</summary>
    public bool Joined => _client.LocalEntity is not null && Joiner is not null;

    /// <summary>Holds or lets go of a key for a player.</summary>
    /// <param name="input">The player's input, <see cref="HostInput"/> or <see cref="JoinerInput"/>.</param>
    /// <param name="key">The key.</param>
    /// <param name="down">Whether it is held.</param>
    public static void Hold(InputState input, KeyCode key, bool down) => input.Keyboard.SetKey(key, down);

    /// <summary>Runs both games a frame each.</summary>
    /// <returns><see langword="false"/> once either has stopped.</returns>
    public bool Step() => HostGame.Step() & JoinerGame.Step();

    /// <summary>Runs both games until a condition holds.</summary>
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

    /// <summary>Drops the joiner's connection, as a line going down does, and connects again under the same name.</summary>
    /// <param name="frames">How many frames both run between dropping and coming back.</param>
    public void Reconnect(int frames)
    {
        _client.Dispose();
        _joinerWatch.Inner = new Watch(new NoSession());
        RunUntil(() => false, frames);
        _client = NewClient();
        _joinerWatch.Inner = _client;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        HostGame.Stop();
        JoinerGame.Stop();
        _client.Dispose();
        _session.Dispose();
        _hostDevice.Dispose();
        _joinerDevice.Dispose();
    }

    private SessionClient NewClient() =>
        new(_network.Open(), _session.LocalAddress, new JoinRequest("Anya", "test", string.Empty), "igi1", NullLogger.Instance);

    private static GameHost Make(string[] args, HeadlessGameWindow window, RecordingGraphicsDevice device)
    {
        string root = GameDataSource.Igi1.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(GameTitle.Igi1);
        VirtualFileSystem files = new(NullLogger.Instance);
        files.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(files);
        return new GameHost(new HeadlessPlatformHost(), window, device, NullAudioDeviceFactory.Instance,
            InlineWorkScheduler.Instance, AlwaysReadyLoadGate.Instance, TitleCatalog.Catalog, runtime.Title, files, HostOptions.Parse(args), root, NullLogger.Instance);
    }

    // Passes the ticks on to a session and keeps the level it runs on.
    // A client's watch follows its session where the server goes.
    private sealed class FollowingWatch(SessionClient inner) : Watch(inner), ISessionFollower
    {
        public string? TakeTravel() => (Inner as SessionClient)?.TakeTravel();

        public string? Ended => (Inner as SessionClient)?.Ended;

        public string? RoomCode => (Inner as SessionClient)?.RoomCode;
    }

    // Between a dropped connection and the next: no session at all.
    private sealed class NoSession : IHostTickObserver
    {
        public void BeforeTitleTick(object title)
        {
        }

        public void AfterTitleTick(object title)
        {
        }
    }

    private class Watch(IHostTickObserver inner) : IHostTickObserver
    {
        public IHostTickObserver Inner { get; set; } = inner;

        public Igi1Gameplay? Game { get; private set; }

        public void BeforeTitleTick(object title)
        {
            Game = title as Igi1Gameplay ?? Game;
            Inner.BeforeTitleTick(title);
        }

        public void AfterTitleTick(object title) => Inner.AfterTitleTick(title);
    }
}
