using System.Diagnostics;
using System.Globalization;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Titles;
using OpenIGI.Net;
using OpenIGI.Net.Browsing;
using OpenIGI.Net.Udp;

namespace OpenIGI.Launcher;

/// <content>
/// The servers: the shared server browser for the game chosen (the master's list, this network's, the saved and recent ones the
/// game keeps too), joining one by starting the game at it, and a dedicated server of this build hosted from here.
/// </content>
internal sealed partial class LauncherModel
{
    private const string ServerLogCategory = "Servers";

    private readonly Stopwatch _serverClock = Stopwatch.StartNew();
    private ServerBrowser? _browser;
    private string? _browsing;
    private Process? _hosted;
    private DedicatedServerLaunch? _hostedLaunch;

    /// <summary>Gets every server heard of for the chosen game, unordered.</summary>
    public IEnumerable<BrowserServer> Servers => _browser?.Servers ?? [];

    /// <summary>Gets why the master's list could not be had, or <see langword="null"/>.</summary>
    public string? ServerListError => _browser?.MasterError;

    /// <summary>Gets a value indicating whether the master's list is being fetched.</summary>
    public bool ListingServers => _browser?.IsFetching == true;

    /// <summary>Gets a count that moves whenever the server list changes, for a page to rebuild on.</summary>
    public int ServersRevision { get; private set; }

    /// <summary>Gets a value indicating whether the installed build ships a dedicated server to host from here.</summary>
    public bool CanHostServer => _installed is { } build && Current.Retail is not null && DedicatedServerLaunch.FindServer(build.Folder) is not null;

    /// <summary>Gets what the server hosted from here is doing, or <see langword="null"/> while none runs.</summary>
    public string? HostedServerStatus => _hosted is null || _hostedLaunch is not { } launch
        ? null
        : _hosted.HasExited
            ? $"The server stopped (exit code {_hosted.ExitCode}). Its log is in {DedicatedServerLaunch.FolderFor(launch.Title)}."
            : string.Create(CultureInfo.InvariantCulture, $"{launch.Name} plays {launch.Mode.ToUpperInvariant()} on mission {launch.Mission}, UDP port {launch.Port}{(launch.Public ? ", listed" : string.Empty)}.");

    /// <summary>Asks again: the master's list, this network, and the saved and recent servers.</summary>
    public void RefreshServers() => Browser()?.Refresh(_serverClock.Elapsed.TotalSeconds);

    /// <summary>Starts the game at a server: by address where it has one, else through its room.</summary>
    /// <param name="server">The server.</param>
    /// <param name="password">The password it asks, or empty.</param>
    public void JoinServer(BrowserServer server, string password = "")
    {
        ArgumentNullException.ThrowIfNull(server);
        List<string> join = server.Address is { IsIPv4: true } address
            ? ["-connect", address.ToString()]
            : server.Room is { } room ? ["-join", room] : [];
        if (join.Count == 0)
        {
            Report($"{server.Info.Name} can't be reached from here.");
            return;
        }

        if (password.Length > 0)
        {
            join.AddRange(["-password", password]);
        }

        _browser?.Played(server);
        _logger.Information(ServerLogCategory, $"Joining {server.Info.Name} ({string.Join(' ', join.Take(2))}).");
        Play(Current, join);
    }

    /// <summary>Saves a server, or takes it off the saved ones.</summary>
    /// <param name="server">The server.</param>
    /// <param name="favourite">Whether it is saved.</param>
    public void SetFavouriteServer(BrowserServer server, bool favourite)
    {
        _browser?.SetFavourite(server, favourite);
        ServersRevision++;
    }

    /// <summary>Starts a dedicated server of the installed build on this machine, reading the chosen game's own files.</summary>
    /// <param name="form">What it plays and how it is listed.</param>
    public void HostServer(DedicatedServerLaunch form)
    {
        ArgumentNullException.ThrowIfNull(form);
        StopHostedServer();
        if (_installed is not { } build || Current.Retail is not { } title || DedicatedServerLaunch.FindServer(build.Folder) is not { } executable)
        {
            Report("This build has no dedicated server to host.");
            return;
        }

        DedicatedServerLaunch launch = form with
        {
            Title = title.Id,
            GamePath = Services.GameStarter.Locate(title, Settings.GameFolders.GetValueOrDefault(title.Id)),
        };
        try
        {
            _hosted = launch.Start(executable, DedicatedServerLaunch.FolderFor(title.Id));
            _hostedLaunch = launch;
            _logger.Information(ServerLogCategory, $"Started a dedicated server for {title.DisplayName}: {launch.Name}, mission {launch.Mission}, {launch.Mode}, port {launch.Port}.");
            if (NetAddress.TryParseIp("127.0.0.1", launch.Port, out NetAddress here))
            {
                Browser()?.Watch(here, launch.Name, _serverClock.Elapsed.TotalSeconds);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Report($"The server didn't start: {ex.Message}", ex);
        }

        ServersRevision++;
    }

    /// <summary>Stops the server hosted from here.</summary>
    public void StopHostedServer()
    {
        if (_hosted is { } server)
        {
            DedicatedServerLaunch.Stop(server);
            server.Dispose();
            _hosted = null;
            _hostedLaunch = null;
            ServersRevision++;
        }
    }

    // Each frame: what the master and the servers answered, and a server hosted from here that stopped by itself.
    private void TickServers()
    {
        if (_browser is { } browser && browser.Update(_serverClock.Elapsed.TotalSeconds))
        {
            ServersRevision++;
        }

        if (_hosted is { HasExited: true } && _hostedLaunch is not null && _hostedExitTold != _hosted.Id)
        {
            _hostedExitTold = _hosted.Id;
            _logger.Warning(ServerLogCategory, HostedServerStatus ?? "The server stopped.");
            ServersRevision++;
        }
    }

    private int _hostedExitTold;

    // The browser of the chosen game: made again when another game is chosen, as each lists its own servers.
    private ServerBrowser? Browser()
    {
        if (Current.Retail is not { } title)
        {
            return null;
        }

        if (_browser is null || _browsing != title.Id)
        {
            _browser?.Dispose();
            _browsing = title.Id;
            NetQueryClient? query = null;
            try
            {
                query = new NetQueryClient(UdpTransport.Open(0), ServerInfo.ProtocolIdFor(title.Id));
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                _logger.Warning(ServerLogCategory, $"No UDP for the server list ({ex.Message}); the master's list alone.");
            }

            ServerBook book = new(SettingsPaths.GetFilePath("servers.json"));
            _browser = new ServerBrowser(new ServerBrowserOptions(Net.Rooms.RoomTransport.DefaultMaster, title.Id, Decode), query, book);
        }

        return _browser;
    }

    // A server's own answer; this build cannot tell another's protocol, so the version is shown and the game judges it.
    private static BrowserInfo? Decode(byte[] info) =>
        ServerInfo.Decode(info) is { } said
            ? new BrowserInfo(said.TitleId, said.Name, said.Map, said.Mode, said.Players, said.MaxPlayers, said.HasPassword, said.Version, Compatible: true)
            : null;

    private void DisposeServers()
    {
        StopHostedServer();
        _browser?.Dispose();
    }
}
