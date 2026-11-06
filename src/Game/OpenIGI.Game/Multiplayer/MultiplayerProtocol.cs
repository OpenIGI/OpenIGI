using System.Text;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>The version of the multiplayer protocol, and the protocol id each game's sessions are told apart by.</summary>
/// <remarks>
/// The id is what every packet's checksum is keyed with (see <see cref="NetServerOptions.ProtocolId"/>), so a client of
/// one game never hears a server of the other. The version is not in it: a server of another version still hears a client,
/// and turns it down in words (<see cref="NetServerOptions.ProtocolVersion"/>), and the server list still reads its line
/// and shows it as another version's.
/// </remarks>
public static class MultiplayerProtocol
{
    /// <summary>The protocol version; raised whenever any message changes shape.</summary>
    public const int Version = 25;

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
    public static ulong IdFor(string titleId) => Net.Browsing.ServerInfo.ProtocolIdFor(titleId);
}

/// <summary>What a client sends with its request to join, which the server's admission judges.</summary>
/// <param name="PlayerName">The name it plays under.</param>
/// <param name="Version">The build it runs.</param>
/// <param name="Password">The password it was given, or empty.</param>
/// <param name="Content">
/// What the client plays with beyond the game itself -- its title and the mods that change play -- as a short fingerprint,
/// or empty when it doesn't say. Two games that differ there drift apart without a word, so a host refuses a mismatch.
/// </param>
/// <param name="Skin">The look it plays in, a <see cref="PlayerSkins"/> id; empty for the default.</param>
/// <param name="UpdateRate">The snapshots a second it wants (<c>cl_updaterate</c>); 0 for every tick the server allows.</param>
/// <param name="Rate">The bytes a second its line takes (<c>rate</c>); 0 for the server's default.</param>
public sealed record JoinRequest(
    string PlayerName, string Version, string Password, string Content = "", string Skin = "", int UpdateRate = 0, int Rate = 0)
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
        writer.WriteString(Skin, 16);
        writer.WriteRanged(Math.Clamp(UpdateRate, 0, 255), 0, 255);
        writer.WriteRanged(Math.Clamp(Rate / 1024, 0, 4095), 0, 4095);
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

        JoinRequest request = new(reader.ReadString(MaxNameBytes), reader.ReadString(32), reader.ReadString(64), reader.ReadString(16), reader.ReadString(16))
        {
            UpdateRate = reader.ReadRanged(0, 255),
            Rate = reader.ReadRanged(0, 4095) * 1024,
        };
        return reader.IsOverflowed ? null : request;
    }
}
