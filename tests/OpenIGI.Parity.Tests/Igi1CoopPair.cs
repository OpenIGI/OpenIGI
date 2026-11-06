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
        : this(null, null, mission, frames, hostArgs)
    {
    }

    /// <summary>
    /// Starts both games on a mission, each heard and listening through the audio it is given; given audio, the games talk
    /// through the session themselves -- what is said and spoken is theirs to take, not the test's.
    /// </summary>
    /// <param name="hostAudio">The host's audio.</param>
    /// <param name="joinerAudio">The joiner's audio.</param>
    /// <param name="mission">The mission both play.</param>
    /// <param name="frames">How many frames each may run.</param>
    /// <param name="hostArgs">More of the host's command line.</param>
    public Igi1CoopPair(IAudioDeviceFactory? hostAudio, IAudioDeviceFactory? joinerAudio, int mission = 1, int frames = 20000, params string[] hostArgs)
        : this(hostAudio, joinerAudio, mission, frames, hostArgs, joinerArgs: [], joinLater: false)
    {
    }

    /// <summary>
    /// Starts the host on a mission, and the joiner either alongside or not until <see cref="StartJoiner"/>: a late joiner,
    /// coming to a level the host has played into. The host skips its intro; the joiner plays with no extras but its own.
    /// </summary>
    /// <param name="hostArgs">More of the host's command line.</param>
    /// <param name="joinerArgs">The joiner's extras, in place of the host's.</param>
    /// <param name="joinLater">Whether the joiner waits for <see cref="StartJoiner"/>.</param>
    /// <param name="mission">The mission both play.</param>
    /// <param name="hostSkipsIntro">Whether the host skips its intro (the joiner alongside skips its own with it).</param>
    public Igi1CoopPair(string[] hostArgs, string[] joinerArgs, bool joinLater, int mission = 1, bool hostSkipsIntro = true)
        : this(null, null, mission, 20000, hostArgs, joinerArgs, joinLater, hostSkipsIntro)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Igi1CoopPair"/> class playing a mode other than co-op.</summary>
    /// <param name="mode">The mode's id, as <c>tdm</c>.</param>
    /// <param name="mission">The mission both play.</param>
    /// <param name="joinerMode">The mode the joiner believes it is joining, as a stale listing would say; the host's by default.</param>
    /// <param name="hostSkipsIntro">Whether the host skips the level's intro, as a capture does; a player's game plays it.</param>
    public Igi1CoopPair(string mode, int mission, string? joinerMode = null, bool hostSkipsIntro = true)
        : this(null, null, mission, 20000, [], [], joinLater: false, hostSkipsIntro, mode, joinerMode)
    {
    }

    private Igi1CoopPair(
        IAudioDeviceFactory? hostAudio, IAudioDeviceFactory? joinerAudio, int mission, int frames, string[] hostArgs, string[] joinerArgs, bool joinLater, bool hostSkipsIntro = true, string mode = "coop", string? joinerMode = null)
    {
        _mode = mode;
        _joinerMode = joinerMode ?? mode;
        bool talks = hostAudio is not null;
        Core.Configuration.CVarRegistry.Default.ResetAll();
        string[] args = ["-mission", mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "-lockstep", "-frames", frames.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        string[] skip = hostSkipsIntro ? ["-skipcutscene"] : [];
        _session = new HostedSession(
            _network.Open(26001),
            new HostedSessionOptions("coop", 4, string.Empty, mode, mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "test", IncludesHost: true),
            "igi1",
            30,
            NullLogger.Instance);
        _client = NewClient();

        HostGame = Make([.. args, .. skip, .. hostArgs], HostWindow, _hostDevice, hostAudio ?? NullAudioDeviceFactory.Instance);
        JoinerGame = Make([.. args, .. (joinLater ? joinerArgs : skip)], JoinerWindow, _joinerDevice, joinerAudio ?? NullAudioDeviceFactory.Instance);
        _hostWatch = new Watch(_session) { Talks = talks };
        _joinerWatch = new FollowingWatch(_client) { Talks = talks };
        HostGame.TickObserver = _hostWatch;
        JoinerGame.TickObserver = _joinerWatch;
        HostGame.Start();
        HostGame.ScriptedViewInput = HostInput;
        JoinerGame.ScriptedViewInput = JoinerInput;
        if (!joinLater)
        {
            StartJoiner();
        }
    }

    private bool _joinerStarted;

    /// <summary>Starts the joiner's game, which joins the host as soon as its level is up.</summary>
    public void StartJoiner()
    {
        if (!_joinerStarted)
        {
            _joinerStarted = true;
            JoinerGame.Start();
        }
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
    public bool Step()
    {
        _steps++;
        return HostGame.Step() & (!_joinerStarted || JoinerGame.Step());
    }

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
        _joinerIdentity.Dispose();
    }

    private SessionClient NewClient()
    {
        JoinerLink = new SimulatedLinkTransport(_network.Open(), () => _steps / 30.0);
        return new(JoinerLink, _session.LocalAddress, new JoinRequest("Anya", "test", string.Empty), "igi1", NullLogger.Instance, _joinerIdentity,
            reopen: () => JoinerLink = new SimulatedLinkTransport(_network.Open(), () => _steps / 30.0))
        {
            Mode = _joinerMode,
        };
    }

    // The mode both play, and the one the joiner is told before it is welcomed.
    private readonly string _mode;
    private readonly string _joinerMode;

    // The joiner's identity, kept across its reconnections as its game keeps its key file.
    private readonly OpenIGI.Net.Identity.EcdsaPlayerIdentity _joinerIdentity = OpenIGI.Net.Identity.EcdsaPlayerIdentity.CreateEphemeral();

    // Frames run, the clock the joiner's line keeps.
    private long _steps;

    /// <summary>Gets the joiner's line to the host, perfect until a test makes it otherwise.</summary>
    public SimulatedLinkTransport JoinerLink { get; private set; } = null!;

    /// <summary>
    /// Finds a way ahead from a spot along which a round flies five metres clear: nothing solid crosses it, the ground is under
    /// its end, and nothing stands between an eye there and a target anywhere up to <paramref name="sweepMetres"/> to either
    /// side of that end -- or a round aimed at a target running across stops on a wall beside the shooter instead.
    /// </summary>
    /// <param name="feet">The shooter's feet.</param>
    /// <param name="sweepMetres">How far either side the target runs; nought for a standing one.</param>
    /// <returns>The way, flat; <see langword="null"/> when there is none.</returns>
    public Core.Numerics.Vector3d? ClearAhead(Core.Numerics.Vector3d feet, double sweepMetres)
    {
        if (Host?.World.Solids?.Caster() is not { } cast)
        {
            return null;
        }

        Core.Numerics.Vector3d from = feet + new Core.Numerics.Vector3d(0, 0, 1.6 * 4096);
        bool Clear(Core.Numerics.Vector3d along)
        {
            if (cast(from, from + (along * 7 * 4096), out _)
                || cast(from + (along * 5 * 4096), feet + (along * 5 * 4096) + new Core.Numerics.Vector3d(0, 0, 256), out _))
            {
                return false;
            }

            Core.Numerics.Vector3d across = new(along.Y, -along.X, 0);
            for (double metres = -sweepMetres; sweepMetres > 0 && metres <= sweepMetres; metres += sweepMetres / 4)
            {
                if (cast(from, feet + (along * 5 * 4096) + (across * metres * 4096) + new Core.Numerics.Vector3d(0, 0, 1.3 * 4096), out _))
                {
                    return false;
                }
            }

            return true;
        }

        for (int i = 0; i < 16; i++)
        {
            Core.Numerics.Vector3d along = new(Math.Sin(i * Math.PI / 8), Math.Cos(i * Math.PI / 8), 0);
            if (Clear(along))
            {
                return along;
            }
        }

        return null;
    }

    /// <summary>Puts a round trip between the joiner and the host, half each way.</summary>
    /// <param name="seconds">The round trip, in seconds.</param>
    public void Ping(double seconds)
    {
        JoinerLink.Send.Latency = seconds / 2;
        JoinerLink.Receive.Latency = seconds / 2;
    }

    private static GameHost Make(string[] args, HeadlessGameWindow window, RecordingGraphicsDevice device, IAudioDeviceFactory audio)
    {
        string root = GameDataSource.Igi1.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(GameTitle.Igi1);
        VirtualFileSystem files = new(NullLogger.Instance);
        files.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(files);
        return new GameHost(new HeadlessPlatformHost(), window, device, audio,
            InlineWorkScheduler.Instance, AlwaysReadyLoadGate.Instance, TitleCatalog.Catalog, runtime.Title, files, HostOptions.Parse([.. args, "-nosave"]), root, NullLogger.Instance);
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

    // The session's talk passes through too, as the game finds it on the session itself.
    private class Watch(IHostTickObserver inner) : IHostTickObserver, ISessionTalk, ISessionHold, ISessionMode
    {
        public IHostTickObserver Inner { get; set; } = inner;

        public string Mode => (Inner as ISessionMode)?.Mode ?? string.Empty;

        public Game.Multiplayer.Modes.MpLayout? Layout
        {
            get => (Inner as ISessionMode)?.Layout;
            set
            {
                if (Inner is ISessionMode mode)
                {
                    mode.Layout = value;
                }
            }
        }

        public bool HoldsLevel => Inner is ISessionHold { HoldsLevel: true };

        public void AnnounceGoing(string map) => (Inner as ISessionHold)?.AnnounceGoing(map);

        public void Pump(object? title)
        {
            if (title is not null)
            {
                Game = title as Igi1Gameplay ?? Game;
            }

            (Inner as ISessionHold)?.Pump(title);
        }

        public bool Talks { get; init; }

        private ISessionTalk? Talk => Talks ? Inner as ISessionTalk : null;

        public void Say(string text) => Talk?.Say(text);

        public void Mark(Int3 position) => Talk?.Mark(position);

        public IReadOnlyList<ChatLine> TakeChat() => Talk?.TakeChat() ?? [];

        public IReadOnlyList<WorldMark> TakeMarks() => Talk?.TakeMarks() ?? [];

        public void Speak(ReadOnlySpan<byte> packet) => Talk?.Speak(packet);

        public IReadOnlyList<VoicePacket> TakeVoices() => Talk?.TakeVoices() ?? [];

        public Int3? WhereIs(int entity) => Talk?.WhereIs(entity);

        public Igi1Gameplay? Game { get; private set; }

        public void BeforeTitleTick(object title)
        {
            Game = title as Igi1Gameplay ?? Game;
            Inner.BeforeTitleTick(title);
        }

        public void AfterTitleTick(object title) => Inner.AfterTitleTick(title);
    }
}
