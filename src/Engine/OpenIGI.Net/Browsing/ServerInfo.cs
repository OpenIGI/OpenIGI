using System.Text;

namespace OpenIGI.Net.Browsing;

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
    /// <summary>Gets the protocol version the server plays; a game of another can't join it.</summary>
    public required int Protocol { get; init; }

    /// <summary>Gets the protocol id of a game's sessions: what every packet's checksum is keyed with, so a client of one game
    /// never hears a server of the other.</summary>
    /// <param name="titleId">The game's id, as <c>igi1</c> or <c>igi2</c>.</param>
    /// <returns>The id.</returns>
    public static ulong ProtocolIdFor(string titleId)
    {
        ArgumentNullException.ThrowIfNull(titleId);
        byte[] name = Encoding.UTF8.GetBytes($"OpenIGI/{titleId}");
        return SipHash.Compute(0x4F70656E49474931UL, 0x4D756C7469706C61UL, name);
    }

    /// <summary>Gets whether a game playing a protocol version can join the server.</summary>
    /// <param name="protocol">The game's protocol version.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public bool IsCompatibleWith(int protocol) => Protocol == protocol;

    /// <summary>Writes the line.</summary>
    /// <returns>Its bytes.</returns>
    public byte[] Encode()
    {
        BitWriter writer = new();
        writer.WriteRanged(Protocol, 0, 255);
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
    /// <returns>The line, or <see langword="null"/> when it is malformed.</returns>
    /// <remarks>
    /// The line's shape is fixed from protocol 16 on, later versions adding only at its end, so a server of another version
    /// is still listed -- as one this game can't join (<see cref="IsCompatibleWith"/>).
    /// </remarks>
    public static ServerInfo? Decode(ReadOnlySpan<byte> data)
    {
        BitReader reader = new(data);
        int protocol = reader.ReadRanged(0, 255);
        if (protocol < 16)
        {
            return null;
        }

        ServerInfo info = new(
            reader.ReadString(16), reader.ReadString(64), reader.ReadString(64), reader.ReadString(32),
            reader.ReadRanged(0, 255), reader.ReadRanged(0, 255), reader.ReadBool(), reader.ReadString(32))
        {
            Protocol = protocol,
        };
        return reader.IsOverflowed ? null : info;
    }
}
