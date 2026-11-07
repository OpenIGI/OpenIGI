using OpenIGI.Core.Diagnostics;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;

namespace OpenIGI.Host.Dedicated;

/// <summary>
/// A dedicated server's running: its session, the rotation that moves it from map to map, the bans, the message of the day
/// told to each player who joins, the master server's listing, and the console's commands -- all run on the loop's thread
/// between the host's steps.
/// </summary>
public sealed class DedicatedServer : IServerControl, IDisposable
{
    private const string LogCategory = "Server";
    private readonly HostedSession _session;
    private readonly ServerConfig _config;
    private readonly Func<int, bool> _goToMission;
    private readonly IOpenIgiLogger _logger;
    private readonly MasterHeartbeat? _heartbeat;
    private readonly Func<HeartbeatBody>? _beat;
    private readonly HashSet<string> _greeted = new(StringComparer.Ordinal);
    private readonly NextMapFlow _nextMap;
    private readonly Func<IReadOnlyList<(int Id, string Name)>> _missionList;
    private int _rotationAt;

    /// <summary>Initialises a new instance of the <see cref="DedicatedServer"/> class.</summary>
    /// <param name="session">The session.</param>
    /// <param name="config">The server's settings.</param>
    /// <param name="bans">The bans.</param>
    /// <param name="goToMission">Asks the host to go on to a mission; false for one it does not have.</param>
    /// <param name="logger">Where it tells what it does.</param>
    /// <param name="heartbeat">The master server's listing, or <see langword="null"/> for an unlisted server.</param>
    /// <param name="beat">What each beat says, with <paramref name="heartbeat"/>.</param>
    /// <param name="missions">The missions there are, in campaign order with their names: what a server without a rotation offers.</param>
    public DedicatedServer(
        HostedSession session,
        ServerConfig config,
        BanList bans,
        Func<int, bool> goToMission,
        IOpenIgiLogger logger,
        MasterHeartbeat? heartbeat = null,
        Func<HeartbeatBody>? beat = null,
        Func<IReadOnlyList<(int Id, string Name)>>? missions = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Bans = bans ?? throw new ArgumentNullException(nameof(bans));
        _goToMission = goToMission ?? throw new ArgumentNullException(nameof(goToMission));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _heartbeat = heartbeat;
        _beat = beat;
        Console = new ServerConsole(this);
        session.Banned = bans.Check;
        session.Rcon = Remote;
        _missionList = missions ?? (static () => []);
        session.RotatesMaps = config.Rotation.Count > 1 || missions is not null;
        _nextMap = new NextMapFlow(session, Offer, GoOn);
        session.ChangeMap = ChangeMap;
        session.IdleKickSeconds = config.IdleKickSeconds;
    }

    /// <summary>Gets the console its commands run through.</summary>
    public ServerConsole Console { get; }

    /// <summary>Gets a value indicating whether the operator asked it to stop.</summary>
    public bool QuitAsked { get; private set; }

    /// <inheritdoc/>
    public string Name => _config.Name;

    /// <inheritdoc/>
    public string Map => _session.Map;

    /// <inheritdoc/>
    public string? SaveDemo(string path) => _session.SaveDemo(path);

    /// <inheritdoc/>
    public string Mode => _session.Mode;

    /// <inheritdoc/>
    public int MaxPlayers => _config.MaxPlayers;

    /// <inheritdoc/>
    public IReadOnlyList<ServerPlayer> Players => _session.ServerPlayers;

    /// <inheritdoc/>
    public BanList Bans { get; }

    /// <inheritdoc/>
    public IReadOnlyList<RotationEntry> Rotation => _config.Rotation;

    /// <summary>Runs between the host's steps: the rotation, the greetings, the listing.</summary>
    /// <param name="now">The clock, in seconds.</param>
    public void Tick(double now)
    {
        // A decided match: the next maps offered to vote for while its card is up, then on to the one picked.
        _nextMap.Tick();

        Greet();
        if (_heartbeat is not null && _beat is not null)
        {
            _heartbeat.Tick(now, _beat);
        }
    }

    /// <inheritdoc/>
    public bool Remove(int slot, string reason) => _session.Remove(slot, reason);

    /// <inheritdoc/>
    public void Say(string text) => _session.Announce(text);

    /// <inheritdoc/>
    public string? ChangeMap(int mission, string? mode)
    {
        if (mode is not null && !_session.UseMode(mode))
        {
            return $"'{mode}' is not a mode: {string.Join(", ", Game.Multiplayer.Modes.GameModes.All)}.";
        }

        if (!_goToMission(mission))
        {
            return $"This game has no mission {mission}.";
        }

        _logger.Information(LogCategory, $"Going on to mission {mission} ({_session.Mode}).");
        return null;
    }

    // The missions there are, read when wanted: the host has them only once it has started.
    private IReadOnlyList<(int Id, string Name)> Missions => _missionList();

    /// <summary>Gets which missions' maps can be played as arenas (laid out for multiplayer); every one when unset.</summary>
    public Func<int, bool>? Arenas { get; init; }

    // The missions a mode can be played on: every one for co-op, the laid-out ones for a versus mode.
    private IReadOnlyList<(int Id, string Name)> MissionsFor(string mode) =>
        GameModes.IsVersus(mode) && Arenas is { } arenas ? [.. Missions.Where(m => arenas(m.Id))] : Missions;

    // What a decided match offers: the next few of the rotation and this map again; without a rotation, the maps around this one.
    private IReadOnlyList<MapChoice> Offer()
    {
        int mission = int.TryParse(_session.Map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : 0;
        if (_config.Rotation.Count <= 1)
        {
            return NextMapFlow.Around(MissionsFor(_session.Mode), mission, _session.Mode, Random.Shared);
        }

        string NameOf(int number) => Missions.FirstOrDefault(m => m.Id == number).Name ?? string.Empty;
        List<MapChoice> choices = [.. Enumerable.Range(1, Math.Min(NextMapVote.MostChoices - 1, _config.Rotation.Count - 1))
            .Select(step => _config.Rotation[(_rotationAt + step) % _config.Rotation.Count])
            .Where(r => !GameModes.IsVersus(r.Mode.Length > 0 ? r.Mode : _session.Mode) || Arenas?.Invoke(r.Mission) != false)
            .Select(r => new MapChoice(r.Mission, r.Mode.Length > 0 ? r.Mode : _session.Mode, NameOf(r.Mission)))];
        choices.Insert(0, new MapChoice(mission, _session.Mode, NameOf(mission), Again: true));
        return choices;
    }

    // On to the map picked -- staying at this place in the rotation for this one again -- or, with no pick, the rotation's next
    // (this map again without a rotation).
    private void GoOn(MapChoice? picked)
    {
        if (picked is null && _config.Rotation.Count > 1)
        {
            _ = NextMap();
            return;
        }

        int mission = picked?.Mission ?? (int.TryParse(_session.Map, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int id) ? id : 0);
        string mode = picked?.Mode ?? _session.Mode;
        if (picked is { Again: false } && _config.Rotation.ToList().FindIndex(r => r.Mission == picked.Mission && (r.Mode.Length == 0 || r.Mode == picked.Mode)) is >= 0 and var at)
        {
            _rotationAt = at;
        }

        if (mission.ToString(System.Globalization.CultureInfo.InvariantCulture) == _session.Map && (mode.Length == 0 || _session.UseMode(mode)))
        {
            _logger.Information(LogCategory, $"Playing mission {mission} again ({_session.Mode}).");
            _session.PlayAgain();
        }
        else if (ChangeMap(mission, mode.Length > 0 ? mode : null) is { } problem)
        {
            _logger.Warning(LogCategory, $"The picked mission {mission} cannot be played: {problem}");
        }
    }

    /// <inheritdoc/>
    public RotationEntry? NextMap()
    {
        if (_config.Rotation.Count == 0)
        {
            return null;
        }

        _rotationAt = (_rotationAt + 1) % _config.Rotation.Count;
        RotationEntry next = _config.Rotation[_rotationAt];
        if (ChangeMap(next.Mission, next.Mode.Length > 0 ? next.Mode : null) is { } problem)
        {
            _logger.Warning(LogCategory, $"The rotation's mission {next.Mission} cannot be played: {problem}");
        }

        return next;
    }

    /// <inheritdoc/>
    public bool UseMode(string mode) => _session.UseMode(mode);

    /// <inheritdoc/>
    public void EndMatch() => _session.EndMatch();

    /// <inheritdoc/>
    public void Quit() => QuitAsked = true;

    /// <summary>Takes the server off the master's list, as it stops.</summary>
    public void Dispose()
    {
        _heartbeat?.Shutdown();
        _heartbeat?.Dispose();
    }

    // The remote console: the configured password, compared in constant time; a client that keeps getting it wrong is put out.
    private IReadOnlyList<string> Remote(int slot, string password, string command)
    {
        if (_config.RconPassword.Length == 0)
        {
            return ["This server has no remote console (rcon_password is not set)."];
        }

        byte[] given = System.Text.Encoding.UTF8.GetBytes(password ?? string.Empty);
        byte[] wanted = System.Text.Encoding.UTF8.GetBytes(_config.RconPassword);
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(given, wanted))
        {
            int misses = _rconMisses[slot] = _rconMisses.GetValueOrDefault(slot) + 1;
            _logger.Warning(LogCategory, $"A wrong remote console password from slot {slot} ({misses} of {MostRconMisses}).");
            if (misses >= MostRconMisses)
            {
                _session.Remove(slot, "too many wrong remote console passwords");
            }

            return ["Wrong remote console password."];
        }

        _rconMisses.Remove(slot);
        _logger.Information(LogCategory, $"Remote console, slot {slot}: {command}");
        return Console.Execute(command);
    }

    // The wrong passwords each slot has given, and how many put it out.
    private readonly Dictionary<int, int> _rconMisses = [];
    private const int MostRconMisses = 5;

    // Each player who joined since, told the message of the day once a visit.
    private void Greet()
    {
        IReadOnlyList<ServerPlayer> players = _session.ServerPlayers;
        foreach (ServerPlayer player in players)
        {
            string key = player.PlayerId.Length > 0 ? player.PlayerId : player.Name;
            if (_greeted.Add(key) && _config.Motd.Length > 0)
            {
                _session.Announce($"Welcome, {player.Name}. {_config.Motd}");
            }
        }

        _greeted.RemoveWhere(key => !players.Any(p => (p.PlayerId.Length > 0 ? p.PlayerId : p.Name) == key));
    }
}
