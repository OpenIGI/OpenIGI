using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Releases;
using OpenIGI.Releases.Accounts;

namespace OpenIGI.Host.Dedicated;

/// <summary>
/// Reports each decided versus match to the master, signed by the server's own key: who played, on which side, their
/// kills, deaths and objectives, and who won. The master counts it only when the key is one of its listed servers'; any
/// other server is told so once, and plays on as before.
/// </summary>
public sealed class MatchReporter
{
    private const string LogCategory = "Stats";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly HostedSession _session;
    private readonly StatsService _stats;
    private readonly IRequestSigner _key;
    private readonly IOpenIgiLogger _logger;
    private bool _decided;
    private double? _startedAt;
    private Task? _sending;
    private bool _refusedOnce;

    /// <summary>Initializes a new instance of the <see cref="MatchReporter"/> class.</summary>
    /// <param name="session">The session whose matches are reported.</param>
    /// <param name="master">The master server.</param>
    /// <param name="key">The server's own key.</param>
    /// <param name="logger">Where it tells what it does.</param>
    public MatchReporter(HostedSession session, Uri master, IRequestSigner key, IOpenIgiLogger logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _stats = new StatsService(Http, master);
    }

    /// <summary>Watches for a decided match, and reports it. Called between the host's steps.</summary>
    /// <param name="now">The clock, in seconds.</param>
    public void Tick(double now)
    {
        if (_sending is { IsCompleted: true } sent)
        {
            _sending = null;
            Collect(sent);
        }

        bool decided = _session.IsMatchDecided;
        if (!decided)
        {
            _startedAt ??= now;
        }
        else if (!_decided && _session.Session is { } server && _startedAt is { } started)
        {
            if (Report(server, _session.Map, (int)Math.Round(now - started)) is { } report && _sending is null)
            {
                _sending = _stats.ReportAsync(_key, report);
            }

            _startedAt = null;
        }

        _decided = decided;
    }

    /// <summary>A decided match's result, or <see langword="null"/> for one that isn't counted: co-op, or fewer than two players.</summary>
    /// <param name="server">The session's server.</param>
    /// <param name="map">The map played.</param>
    /// <param name="seconds">How long the match lasted.</param>
    /// <returns>The result.</returns>
    public static MatchReport? Report(ServerSession server, string map, int seconds)
    {
        ArgumentNullException.ThrowIfNull(server);
        string mode = server.Mode.Id;
        if (!MatchModes.IsVersus(mode))
        {
            return null;
        }

        Match match = server.Match;
        int winner = server.Mode.Flow.Winner;
        List<MatchReportLine> lines = [.. match.Players.Values
            .Where(static player => player.Slot >= 0)
            .Select(player => new MatchReportLine(
                server.IdOf(player.Slot),
                player.Name,
                match.IsFreeForAll ? player.Slot : player.Team,
                player.Kills,
                player.Deaths,
                player.Objectives,
                winner >= 0 && (match.IsFreeForAll ? player.Slot == winner : player.Team == winner)))];
        return lines.Count < 2 ? null : new MatchReport(server.Title, mode, map, Math.Max(0, seconds), winner, lines);
    }

    private void Collect(Task sent)
    {
        if (sent.IsCompletedSuccessfully)
        {
            _logger.Information(LogCategory, "The match's result was counted.");
            return;
        }

        Exception? error = sent.Exception?.GetBaseException();
        if (error is AccountRefusedException { Status: System.Net.HttpStatusCode.Forbidden })
        {
            if (!_refusedOnce)
            {
                _refusedOnce = true;
                _logger.Information(LogCategory, "This server's matches don't count toward the official stats (it isn't listed); that's fine for a community server.");
            }

            return;
        }

        _logger.Warning(LogCategory, $"The match's result could not be sent: {error?.Message}");
    }
}
