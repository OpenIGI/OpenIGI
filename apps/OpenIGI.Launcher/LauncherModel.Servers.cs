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
/// game keeps too), and joining one by starting the game at it.
/// </content>
internal sealed partial class LauncherModel
{
    private const string ServerLogCategory = "Servers";

    private readonly Stopwatch _serverClock = Stopwatch.StartNew();
    private ServerBrowser? _browser;
    private string? _browsing;

    /// <summary>Gets every server heard of for the chosen game, unordered.</summary>
    public IEnumerable<BrowserServer> Servers => _browser?.Servers ?? [];

    /// <summary>Gets why the master's list could not be had, or <see langword="null"/>.</summary>
    public string? ServerListError => _browser?.MasterError;

    /// <summary>Gets a value indicating whether the master's list is being fetched.</summary>
    public bool ListingServers => _browser?.IsFetching == true;

    /// <summary>Gets a count that moves whenever the server list changes, for a page to rebuild on.</summary>
    public int ServersRevision { get; private set; }

    /// <summary>Asks again: the master's list, this network, and the saved and recent servers.</summary>
    public void RefreshServers() => Browser()?.Refresh(_serverClock.Elapsed.TotalSeconds);

    /// <summary>Gets the picture of a map of the chosen game, as the launcher ships it; <see langword="null"/> for none.</summary>
    /// <param name="mission">The map's mission id.</param>
    /// <returns>The picture's file.</returns>
    public string? MissionPicturePath(int mission) => Current.Retail is { } title
        ? Path.Combine(AppContext.BaseDirectory, "Assets", "missions", title.Id, string.Create(CultureInfo.InvariantCulture, $"{mission}.jpg"))
        : null;

    /// <summary>Gets a map of the chosen game as the game's menus name it (Trainyard).</summary>
    /// <param name="mission">The map's mission id.</param>
    /// <returns>Its name, or <see langword="null"/> where not known.</returns>
    public string? MissionName(int mission) => Current.Retail?.MissionName(mission);

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

    // Each frame: what the master and the servers answered.
    private void TickServers()
    {
        if (_browser is { } browser && browser.Update(_serverClock.Elapsed.TotalSeconds))
        {
            ServersRevision++;
        }
    }

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

    private void DisposeServers() => _browser?.Dispose();
}
