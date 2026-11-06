namespace OpenIGI.Net.Browsing;

/// <summary>How the browser's lines are ordered.</summary>
public enum BrowserSort
{
    /// <summary>The nearest first; those not yet answering last.</summary>
    Ping,

    /// <summary>The fullest first.</summary>
    Players,

    /// <summary>By name.</summary>
    Name,

    /// <summary>By map, then name.</summary>
    Map,
}

/// <summary>What the player has asked the browser to show.</summary>
public sealed record BrowserFilter
{
    /// <summary>Gets text a line's name, map or mode must hold; empty for any.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets the mode a line must play, or empty for any.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>Gets the region a line must play from, or empty for any.</summary>
    public string Region { get; init; } = string.Empty;

    /// <summary>Gets the sources a line must have been heard from, any one of them; <see cref="BrowserSource.None"/> for any.</summary>
    public BrowserSource Sources { get; init; }

    /// <summary>Gets a value indicating whether full servers are left out.</summary>
    public bool HideFull { get; init; }

    /// <summary>Gets a value indicating whether empty servers are left out.</summary>
    public bool HideEmpty { get; init; }

    /// <summary>Gets a value indicating whether servers asking a password are left out.</summary>
    public bool HidePassworded { get; init; }

    /// <summary>Gets a value indicating whether servers of another build's protocol are left out.</summary>
    public bool HideIncompatible { get; init; }

    /// <summary>Gets the most ping a line may have, in seconds; 0 for any. One that has not answered is kept until it is known.</summary>
    public double MaxPing { get; init; }

    /// <summary>Gets how the lines are ordered.</summary>
    public BrowserSort Sort { get; init; } = BrowserSort.Ping;

    /// <summary>Picks and orders the lines this filter shows.</summary>
    /// <param name="servers">Every line.</param>
    /// <returns>The lines shown, in order.</returns>
    public IReadOnlyList<BrowserServer> Apply(IEnumerable<BrowserServer> servers)
    {
        ArgumentNullException.ThrowIfNull(servers);
        IEnumerable<BrowserServer> shown = servers.Where(Shows);
        IOrderedEnumerable<BrowserServer> ordered = Sort switch
        {
            BrowserSort.Players => shown.OrderByDescending(static s => s.Info.Players),
            BrowserSort.Name => shown.OrderBy(static s => s.Info.Name, StringComparer.OrdinalIgnoreCase),
            BrowserSort.Map => shown.OrderBy(static s => s.Info.Map, StringComparer.OrdinalIgnoreCase),
            _ => shown.OrderBy(static s => s.Ping is null).ThenBy(static s => s.Ping ?? 0),
        };
        return [.. ordered.ThenBy(static s => s.Info.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static s => s.Key, StringComparer.Ordinal)];
    }

    /// <summary>Gets whether this filter shows a line.</summary>
    /// <param name="server">The line.</param>
    /// <returns><see langword="true"/> when it is shown.</returns>
    public bool Shows(BrowserServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        BrowserInfo info = server.Info;
        return (Sources == BrowserSource.None || (server.Sources & Sources) != 0)
            && (Mode.Length == 0 || string.Equals(info.Mode, Mode, StringComparison.OrdinalIgnoreCase))
            && (Region.Length == 0 || string.Equals(server.Region, Region, StringComparison.OrdinalIgnoreCase))
            && (!HideFull || !server.IsFull)
            && (!HideEmpty || info.Players > 0)
            && (!HidePassworded || !info.Passworded)
            && (!HideIncompatible || info.Compatible)
            && (MaxPing <= 0 || server.Ping is not { } ping || ping <= MaxPing)
            && (Text.Length == 0 || Holds(info.Name) || Holds(info.Map) || Holds(info.Mode));
    }

    /// <summary>
    /// Picks the server quick play joins: one this build can join with room for another player and no password, of the mode
    /// asked for if any; then the best of them -- near and already playing, but not one about to fill.
    /// </summary>
    /// <param name="servers">Every line.</param>
    /// <param name="mode">The mode wanted, or empty for any.</param>
    /// <returns>The server, or <see langword="null"/> when none will do.</returns>
    /// <remarks>
    /// The score is the ping in milliseconds (a line that has not answered counts as 150), less 25 for each player already on it
    /// up to three quarters full: a full-ish game nearby beats an empty one a little nearer, as people want someone to play.
    /// </remarks>
    public static BrowserServer? QuickPlay(IEnumerable<BrowserServer> servers, string mode = "")
    {
        ArgumentNullException.ThrowIfNull(servers);
        BrowserFilter filter = new() { HideFull = true, HidePassworded = true, HideIncompatible = true, Mode = mode ?? string.Empty };
        return servers.Where(filter.Shows)
            .OrderBy(static s =>
            {
                double ping = s.Ping is { } p ? p * 1000 : 150;
                int room = s.Info.MaxPlayers > 0 ? (int)(s.Info.MaxPlayers * 0.75) : s.Info.Players;
                return ping - (25 * Math.Min(s.Info.Players, room));
            })
            .ThenBy(static s => s.Key, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private bool Holds(string field) => field.Contains(Text, StringComparison.OrdinalIgnoreCase);
}
