using System.Globalization;

namespace OpenIGI.Net;

/// <summary>What kind of transport an address belongs to.</summary>
public enum NetAddressKind : byte
{
    /// <summary>No address.</summary>
    None,

    /// <summary>An in-process loopback endpoint; <see cref="NetAddress.Port"/> names it.</summary>
    Loopback,

    /// <summary>An IP endpoint: <see cref="NetAddress.Host"/> holds the IPv6 (or IPv4-mapped) address.</summary>
    Ip,

    /// <summary>A stream connection a server accepted, such as a WebSocket; <see cref="NetAddress.Host"/> is its id.</summary>
    Stream,

    /// <summary>A WebRTC peer met through a room; <see cref="NetAddress.Host"/> is its number in the room, the host's zero.</summary>
    Peer,
}

/// <summary>Where a datagram came from or goes to, on any transport, as a value the connection tables can key on.</summary>
/// <param name="Kind">The transport kind.</param>
/// <param name="Host">The host: an IPv6 address (IPv4 mapped), a stream id, or zero.</param>
/// <param name="Port">The port, or the loopback endpoint's number.</param>
public readonly record struct NetAddress(NetAddressKind Kind, UInt128 Host, int Port)
{
    /// <summary>Gets a loopback address.</summary>
    /// <param name="port">The endpoint's number.</param>
    /// <returns>The address.</returns>
    public static NetAddress Loopback(int port) => new(NetAddressKind.Loopback, UInt128.Zero, port);

    /// <summary>Gets a room peer's address.</summary>
    /// <param name="id">The peer's number in its room; the room's host is zero.</param>
    /// <returns>The address.</returns>
    public static NetAddress Peer(int id) => new(NetAddressKind.Peer, (UInt128)(uint)id, 0);

    /// <summary>Gets an IPv4 address, stored IPv4-mapped.</summary>
    /// <param name="a">The first octet.</param>
    /// <param name="b">The second octet.</param>
    /// <param name="c">The third octet.</param>
    /// <param name="d">The fourth octet.</param>
    /// <param name="port">The port.</param>
    /// <returns>The address.</returns>
    public static NetAddress IPv4(byte a, byte b, byte c, byte d, int port) =>
        new(NetAddressKind.Ip, new UInt128(0, 0xFFFF_0000_0000UL | ((ulong)a << 24) | ((ulong)b << 16) | ((ulong)c << 8) | d), port);

    /// <summary>Gets whether this is an IPv4-mapped IP address.</summary>
    public bool IsIPv4 => Kind == NetAddressKind.Ip && (Host >> 32) == 0xFFFF;

    /// <summary>Writes the address into 19 bytes, for hashing: kind, host, port.</summary>
    /// <param name="destination">At least 19 bytes.</param>
    public void WriteTo(Span<byte> destination)
    {
        destination[0] = (byte)Kind;
        ulong upper = (ulong)(Host >> 64);
        ulong lower = (ulong)Host;
        for (int i = 0; i < 8; i++)
        {
            destination[1 + i] = (byte)(upper >> (56 - (8 * i)));
            destination[9 + i] = (byte)(lower >> (56 - (8 * i)));
        }

        destination[17] = (byte)(Port >> 8);
        destination[18] = (byte)Port;
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        switch (Kind)
        {
            case NetAddressKind.Loopback:
                return string.Create(CultureInfo.InvariantCulture, $"loopback:{Port}");
            case NetAddressKind.Peer:
                return string.Create(CultureInfo.InvariantCulture, $"peer:{(uint)Host}");
            case NetAddressKind.Stream:
                return string.Create(CultureInfo.InvariantCulture, $"stream:{(ulong)Host}");
            case NetAddressKind.Ip when IsIPv4:
                uint v4 = (uint)Host;
                return string.Create(
                    CultureInfo.InvariantCulture, $"{v4 >> 24}.{(v4 >> 16) & 0xFF}.{(v4 >> 8) & 0xFF}.{v4 & 0xFF}:{Port}");
            case NetAddressKind.Ip:
                return string.Create(CultureInfo.InvariantCulture, $"[{(ulong)(Host >> 64):x16}{(ulong)Host:x16}]:{Port}");
            default:
                return "none";
        }
    }
}
