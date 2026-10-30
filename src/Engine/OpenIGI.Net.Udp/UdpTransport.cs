using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace OpenIGI.Net.Udp;

/// <summary>The internet transport: one non-blocking UDP socket, dual-stack where the machine allows it.</summary>
/// <remarks>
/// <para>
/// Retail used UDP alone, the server on port 26001 and clients on 26015 (<see cref="DefaultServerPort"/>,
/// <see cref="DefaultClientPort"/>); a firewall opened for the original works for this. The socket never blocks: the
/// network tick drains whatever has arrived and moves on.
/// </para>
/// <para>
/// An IPv4 peer of a dual-stack socket appears with its IPv4-mapped IPv6 address, which is exactly how
/// <see cref="NetAddress.IPv4"/> stores one, so the same peer always keys the connection tables the same way.
/// </para>
/// </remarks>
public sealed class UdpTransport : IBroadcastTransport
{
    /// <summary>The port a server listens on unless told otherwise, as retail's <c>svport</c>.</summary>
    public const int DefaultServerPort = 26001;

    /// <summary>The port a client sends from unless told otherwise, as retail's <c>clport</c>.</summary>
    public const int DefaultClientPort = 26015;

    private const int SioUdpConnReset = -1744830452;

    private readonly Socket _socket;
    private readonly SocketAddress _receiveAddress;
    private readonly Dictionary<NetAddress, SocketAddress> _sendAddresses = [];
    private readonly bool _isIPv6;

    private UdpTransport(Socket socket, bool isIPv6)
    {
        _socket = socket;
        _isIPv6 = isIPv6;
        _receiveAddress = new SocketAddress(isIPv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
        LocalAddress = FromEndPoint((IPEndPoint)socket.LocalEndPoint!);
    }

    /// <inheritdoc/>
    public NetAddress LocalAddress { get; }

    /// <inheritdoc/>
    public int MaxDatagramSize => NetConstants.MaxPacketSize;

    /// <summary>Opens a socket.</summary>
    /// <param name="port">The port to bind; zero lets the system pick, the way a client usually wants.</param>
    /// <param name="ipv4Only">Whether to stay on IPv4, as LAN discovery does so its broadcasts go out everywhere.</param>
    /// <returns>The transport.</returns>
    /// <exception cref="SocketException">The port is taken or the socket cannot be made.</exception>
    public static UdpTransport Open(int port, bool ipv4Only = false)
    {
        if (!ipv4Only && Socket.OSSupportsIPv6)
        {
            try
            {
                Socket dual = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp) { DualMode = true };
                return Configure(dual, new IPEndPoint(IPAddress.IPv6Any, port), isIPv6: true);
            }
            catch (SocketException)
            {
                // IPv6 can be compiled in yet switched off; the IPv4 socket below still works there.
            }
        }

        Socket v4 = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        return Configure(v4, new IPEndPoint(IPAddress.Any, port), isIPv6: false);
    }

    /// <summary>Gets the address of an endpoint.</summary>
    /// <param name="endPoint">An IP endpoint.</param>
    /// <returns>The address, IPv4 stored mapped.</returns>
    public static NetAddress FromEndPoint(IPEndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        IPAddress address = endPoint.Address.AddressFamily == AddressFamily.InterNetwork
            ? endPoint.Address.MapToIPv6()
            : endPoint.Address;
        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        return new NetAddress(NetAddressKind.Ip, BinaryPrimitives.ReadUInt128BigEndian(bytes), endPoint.Port);
    }

    /// <summary>Gets the endpoint an address names.</summary>
    /// <param name="address">An IP address.</param>
    /// <returns>The endpoint, IPv4 unmapped.</returns>
    /// <exception cref="ArgumentException">The address is not an IP address.</exception>
    public static IPEndPoint ToEndPoint(NetAddress address)
    {
        if (address.Kind != NetAddressKind.Ip)
        {
            throw new ArgumentException("Not an IP address.", nameof(address));
        }

        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(bytes, address.Host);
        IPAddress ip = new(bytes);
        return new IPEndPoint(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip, address.Port);
    }

    /// <summary>Parses <c>host:port</c>, <c>[v6]:port</c> or a bare host, resolving names.</summary>
    /// <param name="text">The text.</param>
    /// <param name="defaultPort">The port when the text names none.</param>
    /// <returns>The address, or <see langword="null"/> when it does not parse or resolve.</returns>
    public static NetAddress? Parse(string text, int defaultPort = DefaultServerPort)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (IPEndPoint.TryParse(text, out IPEndPoint? endPoint))
        {
            return FromEndPoint(endPoint.Port == 0 ? new IPEndPoint(endPoint.Address, defaultPort) : endPoint);
        }

        string host = text;
        int port = defaultPort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text.AsSpan(colon + 1), out int parsed))
        {
            host = text[..colon];
            port = parsed;
        }

        try
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);
            IPAddress? pick = Array.Find(addresses, a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
            return pick is null ? null : FromEndPoint(new IPEndPoint(pick, port));
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public void Send(NetAddress destination, ReadOnlySpan<byte> datagram)
    {
        if (destination.Kind != NetAddressKind.Ip || datagram.Length > MaxDatagramSize || (!_isIPv6 && !destination.IsIPv4))
        {
            return;
        }

        if (!_sendAddresses.TryGetValue(destination, out SocketAddress? socketAddress))
        {
            IPEndPoint endPoint = ToEndPoint(destination);
            if (_isIPv6 && endPoint.AddressFamily == AddressFamily.InterNetwork)
            {
                endPoint = new IPEndPoint(endPoint.Address.MapToIPv6(), endPoint.Port);
            }

            socketAddress = endPoint.Serialize();
            if (_sendAddresses.Count > 4096)
            {
                _sendAddresses.Clear();
            }

            _sendAddresses[destination] = socketAddress;
        }

        try
        {
            _socket.SendTo(datagram, SocketFlags.None, socketAddress);
        }
        catch (SocketException)
        {
            // A full buffer or an unreachable host loses this datagram, which the layer above already allows for.
        }
    }

    /// <inheritdoc/>
    public void Broadcast(int port, ReadOnlySpan<byte> datagram)
    {
        NetAddress everyone = NetAddress.IPv4(255, 255, 255, 255, port);
        Send(everyone, datagram);
    }

    /// <inheritdoc/>
    public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length)
    {
        while (_socket.Available > 0)
        {
            try
            {
                length = _socket.ReceiveFrom(buffer, SocketFlags.None, _receiveAddress);
            }
            catch (SocketException)
            {
                continue;
            }

            source = Decode(_receiveAddress);
            return true;
        }

        source = default;
        length = 0;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose() => _socket.Dispose();

    private static UdpTransport Configure(Socket socket, IPEndPoint bindTo, bool isIPv6)
    {
        try
        {
            socket.Blocking = false;
            socket.EnableBroadcast = true;
            socket.ReceiveBufferSize = 1 << 20;
            socket.SendBufferSize = 1 << 20;
            if (OperatingSystem.IsWindows())
            {
                // Windows reports an ICMP port-unreachable from an earlier send as an error on the next receive, which
                // would make one departed client break the whole server's receive loop.
                socket.IOControl(SioUdpConnReset, [0, 0, 0, 0], null);
            }

            socket.Bind(bindTo);
            return new UdpTransport(socket, isIPv6);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static NetAddress Decode(SocketAddress address)
    {
        Span<byte> raw = address.Buffer.Span;
        int port = (raw[2] << 8) | raw[3];
        if (address.Family == AddressFamily.InterNetworkV6)
        {
            return new NetAddress(NetAddressKind.Ip, BinaryPrimitives.ReadUInt128BigEndian(raw.Slice(8, 16)), port);
        }

        return NetAddress.IPv4(raw[4], raw[5], raw[6], raw[7], port);
    }
}
