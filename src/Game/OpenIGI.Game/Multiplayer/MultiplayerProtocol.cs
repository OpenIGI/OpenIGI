using System.Text;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>The version of the multiplayer protocol, and the protocol id each game's sessions are told apart by.</summary>
/// <remarks>
/// The id is what every packet's checksum is keyed with (see <see cref="NetServerOptions.ProtocolId"/>), so a client of
/// one game never hears a server of the other, nor a server built with another protocol version.
/// </remarks>
public static class MultiplayerProtocol
{
    /// <summary>The protocol version; raised whenever any message changes shape.</summary>
    public const int Version = 5;

    /// <summary>A fingerprint of what a game plays with beyond the game itself: its title and the mods that change play.</summary>
    /// <param name="titleId">The game's id.</param>
    /// <param name="mods">The mods that change play, as <c>id@version</c>.</param>
    /// <returns>Sixteen hex digits; the same for two games that will play the same.</returns>
    public static string ContentFingerprint(string titleId, IEnumerable<string> mods)
    {
        ArgumentNullException.ThrowIfNull(titleId);
        ArgumentNullException.ThrowIfNull(mods);
        string played = string.Join('\n', mods.Order(StringComparer.Ordinal));
        byte[] bytes = Encoding.UTF8.GetBytes($"{titleId}\n{played}");
        return SipHash.Compute(0x4F70656E49474931UL, 0x436F6E74656E7473UL, bytes).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the protocol id of a game's sessions.</summary>
    /// <param name="titleId">The game's id, as <c>igi1</c> or <c>igi2</c>.</param>
    /// <returns>The id.</returns>
    public static ulong IdFor(string titleId)
    {
        ArgumentNullException.ThrowIfNull(titleId);
        byte[] name = Encoding.UTF8.GetBytes($"OpenIGI/{titleId}/{Version}");
        return SipHash.Compute(0x4F70656E49474931UL, 0x4D756C7469706C61UL, name);
    }
}

/// <summary>What a server tells a browser about itself: the line in the server list.</summary>
/// <param name="TitleId">The game it runs, as <c>igi2</c>.</param>
/// <param name="Name">Its name.</param>
/// <param name="Map">The map it is playing.</param>
/// <param name="Mode">The game mode.</param>
/// <param name="Players">How many players are connected.</param>
/// <param name="MaxPlayers">How many may be.</param>
/// <param name="HasPassword">Whether joining takes a password.</param>
/// <param name="Version">The build it runs.</param>
public sealed record ServerInfo(
    string TitleId, string Name, string Map, string Mode, int Players, int MaxPlayers, bool HasPassword, string Version)
{
    /// <summary>Writes the line.</summary>
    /// <returns>Its bytes.</returns>
    public byte[] Encode()
    {
        BitWriter writer = new();
        writer.WriteRanged(MultiplayerProtocol.Version, 0, 255);
        writer.WriteString(TitleId, 16);
        writer.WriteString(Name, 64);
        writer.WriteString(Map, 64);
        writer.WriteString(Mode, 32);
        writer.WriteRanged(Players, 0, 255);
        writer.WriteRanged(MaxPlayers, 0, 255);
        writer.WriteBool(HasPassword);
        writer.WriteString(Version, 32);
        return writer.ToArray();
    }

    /// <summary>Reads a line a server sent.</summary>
    /// <param name="data">Its bytes.</param>
    /// <returns>The line, or <see langword="null"/> when it is malformed or of another protocol version.</returns>
    public static ServerInfo? Decode(ReadOnlySpan<byte> data)
    {
        BitReader reader = new(data);
        if (reader.ReadRanged(0, 255) != MultiplayerProtocol.Version)
        {
            return null;
        }

        ServerInfo info = new(
            reader.ReadString(16), reader.ReadString(64), reader.ReadString(64), reader.ReadString(32),
            reader.ReadRanged(0, 255), reader.ReadRanged(0, 255), reader.ReadBool(), reader.ReadString(32));
        return reader.IsOverflowed ? null : info;
    }
}

/// <summary>What a client sends with its request to join, which the server's admission judges.</summary>
/// <param name="PlayerName">The name it plays under.</param>
/// <param name="Version">The build it runs.</param>
/// <param name="Password">The password it was given, or empty.</param>
/// <param name="Content">
/// What the client plays with beyond the game itself -- its title and the mods that change play -- as a short fingerprint,
/// or empty when it doesn't say. Two games that differ there drift apart without a word, so a host refuses a mismatch.
/// </param>
public sealed record JoinRequest(string PlayerName, string Version, string Password, string Content = "")
{
    /// <summary>The longest player name kept.</summary>
    public const int MaxNameBytes = 32;

    /// <summary>Writes the request.</summary>
    /// <returns>Its bytes.</returns>
    public byte[] Encode()
    {
        BitWriter writer = new();
        writer.WriteRanged(MultiplayerProtocol.Version, 0, 255);
        writer.WriteString(PlayerName, MaxNameBytes);
        writer.WriteString(Version, 32);
        writer.WriteString(Password, 64);
        writer.WriteString(Content, 16);
        return writer.ToArray();
    }

    /// <summary>Reads a request a client sent.</summary>
    /// <param name="data">Its bytes.</param>
    /// <returns>The request, or <see langword="null"/> when it is malformed or of another protocol version.</returns>
    public static JoinRequest? Decode(ReadOnlySpan<byte> data)
    {
        BitReader reader = new(data);
        if (reader.ReadRanged(0, 255) != MultiplayerProtocol.Version)
        {
            return null;
        }

        JoinRequest request = new(reader.ReadString(MaxNameBytes), reader.ReadString(32), reader.ReadString(64), reader.ReadString(16));
        return reader.IsOverflowed ? null : request;
    }
}
