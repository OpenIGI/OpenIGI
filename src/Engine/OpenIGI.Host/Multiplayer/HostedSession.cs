using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;
using OpenIGI.Net.Rooms;
using OpenIGI.Net.Browsing;

namespace OpenIGI.Host.Multiplayer;

/// <summary>How a <see cref="HostedSession"/> runs: what it shows the server list and who may join.</summary>
/// <param name="Name">The name it shows in the server list.</param>
/// <param name="MaxPlayers">How many clients may be connected at once.</param>
/// <param name="Password">The password joining takes, or empty for none.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Map">The map.</param>
/// <param name="Version">The build it runs.</param>
/// <param name="IncludesHost">Whether the host's own player plays too: a listen server rather than a dedicated one.</param>
/// <param name="HostName">The name the host's own player plays under, when it plays.</param>
/// <param name="HostSkin">The host's own player's look, a <see cref="PlayerSkins"/> id.</param>
/// <param name="Content">The host's <see cref="JoinRequest.Content"/> fingerprint, or empty to take any.</param>
public sealed record HostedSessionOptions(
    string Name, int MaxPlayers, string Password, string Mode, string Map, string Version, bool IncludesHost, string HostName = "Jones", string Content = "", string HostSkin = "");

/// <summary>A server's session run inside the host: a dedicated server's, or a listen server's beside the host's own player.</summary>
/// <remarks>
/// The level is the host's. Once its title's player is ticking, the session is made over it; from then on it reads before
/// each tick and sends after it. A client trying to join while the level still loads keeps retrying its handshake until the
/// session starts reading.
/// </remarks>
public sealed partial class HostedSession : IHostTickObserver, ISessionScoreboard, ISessionTalk, ISessionLook, ISessionHold, ISessionMode, IDemoSource, IDisposable
{
    private const string LogCategory = "Session";

    private readonly NetServer _net;
    private readonly RoomTransport? _room;
    private bool _announced;
    private readonly HostedSessionOptions _options;
    private readonly string _titleId;
    private readonly int _tickRate;
    private readonly IOpenIgiLogger _logger;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<int> _known = [];
    private IReplicatedWorld? _world;
    // The level the session was last bound to.
    private object? _level;
    private ServerSession? _session;
    private readonly INetTransport _transport;

    // The readout's last write, and the host's ticks since: a host that ticks slowly reads its players' packets late.
    private double _diagnosedAt;
    private int _ticksSinceDiagnosed;

    /// <summary>Initializes a new instance of the <see cref="HostedSession"/> class.</summary>
    /// <param name="transport">What it listens on; the session disposes it.</param>
    /// <param name="options">How it runs.</param>
    /// <param name="titleId">The game's id, which keys the protocol.</param>
    /// <param name="tickRate">The level's ticks per second.</param>
    /// <param name="logger">Where joins and departures are told.</param>
    public HostedSession(INetTransport transport, HostedSessionOptions options, string titleId, int tickRate, IOpenIgiLogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _titleId = titleId ?? throw new ArgumentNullException(nameof(titleId));
        _tickRate = tickRate;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _room = transport as RoomTransport ?? (transport as MultiTransport)?.Part<RoomTransport>();
        _transport = transport;
        _mode = options.Mode;
        _net = new NetServer(WithRecorder(transport), new NetServerOptions
        {
            ProtocolId = MultiplayerProtocol.IdFor(titleId),
            ProtocolVersion = MultiplayerProtocol.Version,

            // The recorder's line is one more than the players'.
            MaxClients = options.MaxPlayers + (Records ? 1 : 0),
            Admit = Admit,
            Info = _ => Info().Encode(),
        });
    }

    /// <summary>Gets the room's code, when the session is hosted through one and the room is open.</summary>
    public string? RoomCode => _room?.Code;

    /// <summary>Gets the address the session listens on.</summary>
    public NetAddress LocalAddress => _net.LocalAddress;

    /// <summary>Gets the session once the level is up, or <see langword="null"/>.</summary>
    public ServerSession? Session => _session;

    /// <inheritdoc/>
    public Scoreboard? CurrentScoreboard => _session?.CurrentScoreboard();

    /// <inheritdoc/>
    public int? LocalEntity => _options.IncludesHost ? ServerSession.HostEntity : null;

    /// <inheritdoc/>
    public string Title => _room?.Code is { } code ? $"Room {code}  ·  {MapName(_options.Map)}" : $"{_options.Name}  ·  {MapName(_options.Map)}";

    /// <inheritdoc/>
    public string Place => MapName(_session?.Map ?? _options.Map);

    /// <inheritdoc/>
    public bool Relayed => _room?.Relayed == true;

    /// <summary>Ends the host's own player in a versus game, as the <c>kill</c> command does.</summary>
    /// <returns><see langword="true"/> when there was one to end.</returns>
    public bool KillOwn()
    {
        if (!_options.IncludesHost || _world is not { } world || !Game.Multiplayer.Modes.ArenaLevel.IsVersus(Mode))
        {
            return false;
        }

        world.KillPlayer(ServerSession.HostSlot);
        return true;
    }

    /// <inheritdoc/>
    public float? OwnRevive =>
        _options.IncludesHost && Game.Multiplayer.Modes.PlayerSides.Revivable(Mode) ? _world?.ReviveProgressOf(ServerSession.HostEntity) : null;

    /// <summary>A map as a player reads it: a mission by its number, anything else as it is.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The name.</returns>
    internal static string MapName(string map) =>
        int.TryParse(map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mission)
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Mission {mission}")
            : map;

    /// <inheritdoc/>
    public string OwnSkin => _options.HostSkin;

    // The longest a level is held for players still loading it: past this, those there begin without the rest.
    private const double LongestWaitSeconds = 30.0;

    // When the level now loading was announced, or null with none.
    private double? _goingSince;

    /// <inheritdoc/>
    public void AnnounceGoing(string map)
    {
        if (_session is { } session)
        {
            session.AnnounceGoing(map);
            _goingSince = _clock.Elapsed.TotalSeconds;
            _logger.Information(LogCategory, $"Going on to {MapName(map)}; its players load it alongside.");
        }
    }

    /// <inheritdoc/>
    public bool HoldsLevel =>
        _session is { EveryoneReady: false } && _goingSince is { } since && _clock.Elapsed.TotalSeconds - since < LongestWaitSeconds;

    /// <inheritdoc/>
    public void Pump(object? title)
    {
        if (_session is { } session && _world is not null)
        {
            session.Receive(_clock.Elapsed.TotalSeconds, _world.CurrentTick);
            ReportJoinsAndDepartures();
            session.Flush(_clock.Elapsed.TotalSeconds);
            PumpRecorder();
        }
    }

    /// <inheritdoc/>
    public void BeforeTitleTick(object title)
    {
        if (_session is null)
        {
            if (title is not IReplicatedTitle { Level: not null } replicated)
            {
                return;
            }

            _world = replicated.CreateReplicatedWorld(_options.IncludesHost);
            _level = replicated.Level;
            _session = new ServerSession(
                _net, _world, _tickRate, _options.Map, hostName: _options.IncludesHost ? _options.HostName : null, hostSkin: _options.HostSkin,
                protocolId: MultiplayerProtocol.IdFor(_titleId));
            _session.Title = _titleId;
            _session.Banned = Banned;
            _session.Rcon = Rcon;
            _session.ChangeMap = ChangeMap;
            _session.IdleKickSeconds = IdleKickSeconds;
            _session.Mode = NewMode(_world);
            if (Layout is { } layout)
            {
                _world.UseLayout(layout);
                _session.Layout = layout;
            }
            _logger.Information(LogCategory, $"Playing {_session.Mode.Id} (asked for {_mode}).");
            StartRecorder();
            _logger.Information(
                LogCategory,
                _room is null ? $"The level is up; players may join on port {_net.LocalAddress.Port}." : "The level is up; players may join through the room.");
        }

        else if (title is IReplicatedTitle { Level: { } level } next && !ReferenceEquals(level, _level))
        {
            // Another level: the next mission, or this one again. The room goes on, and every player with it -- once that
            // level is up, never to the gap between two, which would send them back to the mission just left.
            _world = next.CreateReplicatedWorld(_options.IncludesHost);
            _level = next.Level;
            string map = next.Map.Length > 0 ? next.Map : _session.Map;
            _session.Travel(_world, map);

            // The new level's match, under the mode now asked for, on its own layout.
            _session.RestartMode(NewMode(_world));
            _session.Layout = Layout;
            if (Layout is { } layout)
            {
                _world.UseLayout(layout);
            }

            // Begun: whoever loaded it is held no longer, and whoever didn't follows as it can.
            _goingSince = null;
            _room?.ReportState(_known.Count + (_options.IncludesHost ? 1 : 0), map);
            _logger.Information(LogCategory, $"The room goes on to {MapName(map)}; its players follow.");
        }

        if (_room is { Code: { } code } && !_announced)
        {
            _announced = true;
            _logger.Information(LogCategory, $"Room code {code}: friends join with it, on the desktop or in the browser.");
        }

        _session.Receive(_clock.Elapsed.TotalSeconds, _world!.CurrentTick);
        ReportJoinsAndDepartures();
    }

    /// <inheritdoc/>
    public void AfterTitleTick(object title)
    {
        _session?.Send(_clock.Elapsed.TotalSeconds, _world!.CurrentTick - 1);
        PumpRecorder();
        Diagnose();
    }

    // Every few seconds, each player's connection as the host sees it.
    private void Diagnose()
    {
        _ticksSinceDiagnosed++;
        double now = _clock.Elapsed.TotalSeconds;
        double seconds = now - _diagnosedAt;
        if (_session is null || seconds < NetDiagnostics.IntervalSeconds)
        {
            return;
        }

        _logger.Information(NetDiagnostics.LogCategory, string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"host: {_ticksSinceDiagnosed / seconds:F1} ticks/s, {_known.Count} client(s), relayed {_room?.Relayed == true}"));
        foreach (string line in _session.Diagnose(seconds, address => NetDiagnostics.Route(_transport, address)))
        {
            _logger.Information(NetDiagnostics.LogCategory, line);
        }

        _diagnosedAt = now;
        _ticksSinceDiagnosed = 0;
    }

    /// <summary>Gets the server list's line for this server now.</summary>
    /// <returns>The line.</returns>
    public ServerInfo Info() =>
        new(_titleId, _options.Name, _session?.Map ?? _options.Map, _mode, _known.Count + (_options.IncludesHost ? 1 : 0),
            _options.MaxPlayers, _options.Password.Length > 0, _options.Version)
        {
            Protocol = MultiplayerProtocol.Version,
        };

    /// <inheritdoc/>
    public string Mode => _mode;

    /// <inheritdoc/>
    public IReadOnlyList<Game.Multiplayer.Modes.KillNote> KillFeed => _session?.Feed.Shown(_clock.Elapsed.TotalSeconds) ?? [];

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.MatchBanner? Banner =>
        _session is { } session && _world is { } world ? session.CurrentMatch(world.CurrentTick).Banner(0) : null;

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.SpectateTarget? Spectate(int pick)
    {
        if (_world is not { } world || CurrentScoreboard is not { } board || LocalEntity is not { } own || !Game.Multiplayer.Modes.ArenaLevel.IsVersus(Mode))
        {
            return null;
        }

        double now = _clock.Elapsed.TotalSeconds;
        return Game.Multiplayer.Modes.Spectating.Pick(
            board,
            own,
            Game.Multiplayer.Modes.GameModes.HasTeams(Mode),
            entity => board.Lines.Where(l => l.Entity == entity).Select(static l => (int?)l.Slot).FirstOrDefault() is { } slot ? world.PositionOf(slot) : null,
            pick,
            _combat.KilledBy is { } killer ? (killer.Entity, now - killer.At) : null,
            world.PositionOf(ServerSession.HostSlot));
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> NetGraph =>
        _session is { } session
            ?
            [
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"HOSTING {session.Clients.Count()} {(session.Clients.Count() == 1 ? "PLAYER" : "PLAYERS")}   {_tickRate} TICKS/S"),
                .. session.Clients.Select(slot => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture, $"{session.NameOf(slot) ?? "?",-16}  {session.PingOf(slot)} ms")),
            ]
            : [];

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.CombatHud? Combat(Core.Numerics.Vector3d eye, Core.Numerics.Matrix3f view)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_session is not { } session || _world is not { } world)
        {
            return null;
        }

        _combat.Take(session.TakeHostCombat(), ServerSession.HostEntity, now);
        return _combat.Shown(now, eye, view, session.CurrentMatch(world.CurrentTick));
    }

    private readonly Game.Multiplayer.Modes.CombatFeedback _combat = new();

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.MatchHud? MatchHud(Core.Numerics.Vector3d? ownPosition) =>
        _session is { } session && _world is { } world
            && Game.Multiplayer.Modes.MatchHud.Of(session.CurrentMatch(world.CurrentTick), 0, CurrentScoreboard, LocalEntity, ownPosition, Layout) is { } hud
            ? hud with { MapVote = hud.Phase == Game.Multiplayer.Modes.MatchPhase.Intermission ? session.MapVote : null }
            : null;

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.ModeReport? ModeReport => _session is { } session && _world is { } world ? session.Mode.Report(world.CurrentTick) : null;

    /// <inheritdoc/>
    public Game.Multiplayer.Modes.MpLayout? Layout { get; set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Ended once: by leaving the mission, and again by the program that made it as it closes.
        if (!_disposed)
        {
            _disposed = true;
            _net.Dispose();
            _recorderLine?.Dispose();
        }
    }

    private bool _disposed;

    /// <summary>Gets the names of the players who joined, the host's own not among them.</summary>
    public IReadOnlyList<string> Players => _session is { } session ? [.. session.Clients.Select(session.NameOf).OfType<string>()] : [];

    /// <summary>Gets whether every player is down and the squad can stand again where it last stood together.</summary>
    public bool CanRetryFromCheckpoint => _world?.CanRetryFromCheckpoint == true;

    /// <summary>Stands every player up again where the squad last stood together.</summary>
    public void RetryFromCheckpoint() => _world?.RetryFromCheckpoint();

    /// <summary>Puts a player out of the room by its name.</summary>
    /// <param name="name">The player's name.</param>
    public void Kick(string name)
    {
        if (_session is { } session && session.Clients.FirstOrDefault(slot => session.NameOf(slot) == name) is int slot && session.NameOf(slot) == name)
        {
            _logger.Information(LogCategory, $"{name} was removed from the room.");
            session.Kick(slot);
        }
    }

    /// <inheritdoc/>
    public void Say(string text)
    {
        // The host's own /vote, /yes and /no are its say in the room, not lines to it.
        if (_session is { } session && !session.TryHostCommand(text))
        {
            session.Say(text);
        }
    }

    /// <inheritdoc/>
    public void Mark(Int3 position) => _session?.Mark(position);

    /// <inheritdoc/>
    public IReadOnlyList<ChatLine> TakeChat() => _session?.TakeChat() ?? [];

    /// <inheritdoc/>
    public IReadOnlyList<WorldMark> TakeMarks() => _session?.TakeMarks() ?? [];

    /// <inheritdoc/>
    public void Speak(ReadOnlySpan<byte> packet) => _session?.Speak(packet);

    /// <inheritdoc/>
    public IReadOnlyList<VoicePacket> TakeVoices() => _session?.TakeVoices() ?? [];

    /// <inheritdoc/>
    public Int3? WhereIs(int entity) => _session?.LastSnapshot is { } last && last.TryGet(entity, out ReplicatedHuman human) ? human.Position : null;

    private void ReportJoinsAndDepartures()
    {
        HashSet<int> now = [.. _session!.Clients];
        foreach (int slot in now.Except(_known))
        {
            _logger.Information(LogCategory, $"{_session.NameOf(slot)} joined from {_net.GetConnection(slot)?.RemoteAddress} (slot {slot}).");
        }

        foreach (int slot in _known.Except(now))
        {
            _logger.Information(LogCategory, $"The player in slot {slot} left.");
        }

        if (!_known.SetEquals(now))
        {
            _room?.ReportState(now.Count + (_options.IncludesHost ? 1 : 0), map: null);
        }

        _known.Clear();
        _known.UnionWith(now);
    }

    private NetAdmission Admit(NetConnectRequest request)
    {
        if (JoinRequest.Decode(request.Payload) is not { } join)
        {
            return NetAdmission.Reject("this client speaks another version of the protocol");
        }

        if (_options.Content.Length > 0 && join.Content.Length > 0 && !string.Equals(join.Content, _options.Content, StringComparison.Ordinal))
        {
            return NetAdmission.Reject("your game plays with different mods from the host's");
        }

        if (_options.Password.Length > 0 && !string.Equals(join.Password, _options.Password, StringComparison.Ordinal))
        {
            return NetAdmission.Reject("wrong password");
        }

        return string.IsNullOrWhiteSpace(join.PlayerName) ? NetAdmission.Reject("a player needs a name") : NetAdmission.Accept();
    }
}
