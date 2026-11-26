using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
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
internal abstract class CoopPair<TLevel> : IDisposable
    where TLevel : class
{
    private readonly RecordingGraphicsDevice _hostDevice = new() { IsRecording = false };
    private readonly RecordingGraphicsDevice _joinerDevice = new() { IsRecording = false };
    private readonly HostedSession _session;
    private readonly LoopbackNetwork _network = new();
    private SessionClient _client;
    private readonly Watch _hostWatch;
    private readonly FollowingWatch _joinerWatch;

    /// <summary>Starts both games of a title on a mission; the joiner connects once the host's level is up, unless told to wait.</summary>
    /// <param name="data">Where the title's files are.</param>
    /// <param name="title">The title.</param>
    /// <param name="titleId">The title's id, as a session names it.</param>
    /// <param name="hostAudio">The host's audio, or <see langword="null"/>.</param>
    /// <param name="joinerAudio">The joiner's audio, or <see langword="null"/>.</param>
    /// <param name="mission">The mission both play.</param>
    /// <param name="frames">How many frames each may run.</param>
    /// <param name="hostArgs">More of the host's command line.</param>
    /// <param name="joinerArgs">The joiner's extras when it joins later.</param>
    /// <param name="joinLater">Whether the joiner waits for <see cref="StartJoiner"/>.</param>
    /// <param name="hostSkipsIntro">Whether the host skips its intro.</param>
    /// <param name="mode">The session's mode.</param>
    /// <param name="joinerMode">The mode the joiner believes it joins.</param>
    /// <param name="dedicated">Whether the server plays no part itself.</param>
    /// <param name="joinerLook">The look the joiner asks to play in, a <see cref="OpenIGI.Game.Multiplayer.PlayerSkins"/> id; empty for none.</param>
    /// <param name="hostLook">The host's own look; empty for none.</param>
    protected CoopPair(
        GameDataSource data, GameTitle title, string titleId,
        IAudioDeviceFactory? hostAudio, IAudioDeviceFactory? joinerAudio, int mission, int frames, string[] hostArgs, string[] joinerArgs, bool joinLater, bool hostSkipsIntro = true, string mode = "coop", string? joinerMode = null, bool dedicated = false, string joinerLook = "", string hostLook = "")
    {
        _joinerLook = joinerLook;
        _data = data;
        _title = title;
        _titleId = titleId;
        _joinerMode = joinerMode ?? mode;
        bool talks = hostAudio is not null;
        Core.Configuration.CVarRegistry.Default.ResetAll();
        string[] args = ["-mission", mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "-lockstep", "-frames", frames.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        string[] skip = hostSkipsIntro ? ["-skipcutscene"] : [];
        _session = new HostedSession(
            _network.Open(26001),
            new HostedSessionOptions("coop", 4, string.Empty, mode, mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "test", IncludesHost: !dedicated, HostSkin: hostLook),
            titleId,
            30,
            HostLogger) { TimeSource = FrameTime };
        _client = NewClient();

        HostGame = Make([.. args, .. skip, .. hostArgs], HostWindow, _hostDevice, hostAudio ?? NullAudioDeviceFactory.Instance, HostLogger);
        JoinerGame = Make([.. args, .. (joinLater ? joinerArgs : skip)], JoinerWindow, _joinerDevice, joinerAudio ?? NullAudioDeviceFactory.Instance, JoinerLogger);
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
    public TLevel? Host => _hostWatch.Game;

    /// <summary>Gets the joiner's level, once it is up.</summary>
    public TLevel? Joiner => _joinerWatch.Game;

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
        (_hostLogger as IDisposable)?.Dispose();
        (_joinerLogger as IDisposable)?.Dispose();
    }

    private SessionClient NewClient()
    {
        JoinerLink = new SimulatedLinkTransport(_network.Open(), () => _steps / 30.0);
        return new(JoinerLink, _session.LocalAddress, new JoinRequest("Anya", "test", string.Empty, Skin: _joinerLook), _titleId, JoinerLogger, _joinerIdentity,
            reopen: () => JoinerLink = new SimulatedLinkTransport(_network.Open(), () => _steps / 30.0))
        {
            Mode = _joinerMode,
            TimeSource = FrameTime,
        };
    }

    private readonly string _joinerMode;
    private readonly GameDataSource _data;
    private readonly GameTitle _title;
    private readonly string _titleId;

    // The joiner's identity, kept across its reconnections as its game keeps its key file.
    // The look the joiner asks to play in.
    private readonly string _joinerLook;

    private readonly OpenIGI.Net.Identity.EcdsaPlayerIdentity _joinerIdentity = OpenIGI.Net.Identity.EcdsaPlayerIdentity.CreateEphemeral();

    // Frames run, the clock the joiner's line keeps, and both sessions with it.
    private long _steps;

    private double FrameTime() => _steps / 30.0;

    /// <summary>Gets the joiner's line to the host, perfect until a test makes it otherwise.</summary>
    public SimulatedLinkTransport JoinerLink { get; private set; } = null!;

    /// <summary>Puts a round trip between the joiner and the host, half each way.</summary>
    /// <param name="seconds">The round trip, in seconds.</param>
    public void Ping(double seconds)
    {
        JoinerLink.Send.Latency = seconds / 2;
        JoinerLink.Receive.Latency = seconds / 2;
    }

    // Each game's log, written to a folder when OPENIGI_PAIR_LOG names one, to read what the two did after a test; else none.
    private static IOpenIgiLogger PairLogger(string role) =>
        Environment.GetEnvironmentVariable("OPENIGI_PAIR_LOG") is { Length: > 0 } folder
            ? new FileLogger(Path.Combine(Directory.CreateDirectory(folder).FullName, $"{role}.log"))
            : NullLogger.Instance;

    private IOpenIgiLogger HostLogger => _hostLogger ??= PairLogger("host");

    private IOpenIgiLogger JoinerLogger => _joinerLogger ??= PairLogger("joiner");

    private IOpenIgiLogger? _hostLogger;
    private IOpenIgiLogger? _joinerLogger;

    private GameHost Make(string[] args, HeadlessGameWindow window, RecordingGraphicsDevice device, IAudioDeviceFactory audio, IOpenIgiLogger logger)
    {
        string root = _data.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(_title);
        VirtualFileSystem files = new(NullLogger.Instance);
        files.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(files);
        return new GameHost(new HeadlessPlatformHost(), window, device, audio,
            InlineWorkScheduler.Instance, AlwaysReadyLoadGate.Instance, TitleCatalog.Catalog, runtime.Title, files, HostOptions.Parse([.. args, "-nosave"]), root, logger);
    }

    // Passes the ticks on to a session and keeps the level it runs on.
    // A client's watch follows its session where the server goes.
    internal sealed class FollowingWatch(SessionClient inner) : Watch(inner), ISessionFollower
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
    internal class Watch(IHostTickObserver inner) : IHostTickObserver, ISessionTalk, ISessionHold, ISessionMode, ISessionScoreboard
    {
        public IHostTickObserver Inner { get; set; } = inner;

        // The host's frame reaches the session's fight through this, as it does the session itself in the game.
        public Game.Multiplayer.Scoreboard? CurrentScoreboard => (Inner as ISessionScoreboard)?.CurrentScoreboard;

        public int? LocalEntity => (Inner as ISessionScoreboard)?.LocalEntity;

        public string Title => (Inner as ISessionScoreboard)?.Title ?? string.Empty;

        public void CreditHits(Game.Player.ShotTally? own) => (Inner as ISessionScoreboard)?.CreditHits(own);

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

        public bool Unattended => Inner is ISessionHold { Unattended: true };

        public void AnnounceGoing(string map) => (Inner as ISessionHold)?.AnnounceGoing(map);

        public void Pump(object? title)
        {
            if (title is not null)
            {
                Game = title as TLevel ?? Game;
            }

            (Inner as ISessionHold)?.Pump(title);
        }

        public bool Talks { get; init; }

        private ISessionTalk? Talk => Talks ? Inner as ISessionTalk : null;

        public void Say(string text, bool team = false) => Talk?.Say(text, team);

        public void Mark(Int3 position) => Talk?.Mark(position);

        public IReadOnlyList<ChatLine> TakeChat() => Talk?.TakeChat() ?? [];

        public IReadOnlyList<WorldMark> TakeMarks() => Talk?.TakeMarks() ?? [];

        public void Speak(ReadOnlySpan<byte> packet) => Talk?.Speak(packet);

        public IReadOnlyList<VoicePacket> TakeVoices() => Talk?.TakeVoices() ?? [];

        public Int3? WhereIs(int entity) => Talk?.WhereIs(entity);

        public TLevel? Game { get; private set; }

        public void BeforeTitleTick(object title)
        {
            Game = title as TLevel ?? Game;
            Inner.BeforeTitleTick(title);
        }

        public void AfterTitleTick(object title) => Inner.AfterTitleTick(title);
    }
}
