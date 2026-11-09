namespace OpenIGI.Net.Browsing;

/// <summary>Where the browser heard of a server; one server can be heard of in several ways.</summary>
[Flags]
public enum BrowserSource
{
    /// <summary>Not heard of at all.</summary>
    None = 0,

    /// <summary>The master server's list.</summary>
    Internet = 1,

    /// <summary>An answer to a broadcast on this network.</summary>
    Lan = 2,

    /// <summary>The player's favourites.</summary>
    Favourite = 4,

    /// <summary>A server the player has played on.</summary>
    History = 8,
}

/// <summary>What a server says of itself, as a browser reads it from the server's own answer or from the master's list.</summary>
/// <param name="Title">The game it runs, as <c>igi1</c>.</param>
/// <param name="Name">Its name.</param>
/// <param name="Map">The map it is playing.</param>
/// <param name="Mode">The mode it is playing.</param>
/// <param name="Players">The players on it.</param>
/// <param name="MaxPlayers">How many it takes.</param>
/// <param name="Passworded">Whether it asks a password.</param>
/// <param name="Version">The game build it runs.</param>
/// <param name="Compatible">Whether this game can join it: the same network protocol.</param>
public sealed record BrowserInfo(
    string Title, string Name, string Map, string Mode, int Players, int MaxPlayers, bool Passworded, string Version, bool Compatible);

/// <summary>One line of the server browser.</summary>
/// <param name="Key">What it is known by: <c>udp:</c> and its address, or <c>room:</c> and its code.</param>
/// <param name="Address">Its UDP address, for a desktop game to join and ping; <see langword="null"/> for a room alone.</param>
/// <param name="Room">The room a browser joins it through, or <see langword="null"/>.</param>
/// <param name="Info">What it says of itself.</param>
/// <param name="Region">The region it says it plays from (<c>eu</c>, <c>na</c>...), or empty.</param>
/// <param name="Sources">Where it was heard of.</param>
/// <param name="Ping">The round trip to it in seconds, or <see langword="null"/> before it has answered.</param>
public sealed record BrowserServer(
    string Key, NetAddress? Address, string? Room, BrowserInfo Info, string Region, BrowserSource Sources, double? Ping)
{
    /// <summary>Gets a value indicating whether it has answered this game's own question: it is there and can be reached.</summary>
    public bool Answered => Ping is not null;

    /// <summary>Gets a value indicating whether it has no room for another player.</summary>
    public bool IsFull => Info.MaxPlayers > 0 && Info.Players >= Info.MaxPlayers;

    /// <summary>Gets the key of a server by its UDP address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(NetAddress address) => $"udp:{address}";

    /// <summary>Gets the key of a room.</summary>
    /// <param name="code">The room's code.</param>
    /// <returns>The key.</returns>
    public static string KeyOfRoom(string code) => $"room:{code.ToUpperInvariant()}";
}
