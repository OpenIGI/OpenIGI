namespace OpenIGI.Net;

/// <summary>Sends and receives whole datagrams, with no promise that any arrive, arrive once or arrive in order.</summary>
/// <remarks>
/// UDP, an in-process loopback and a WebSocket all look like this. The connection layer above adds sequencing, acks,
/// reliability and the handshake, so a transport only has to move bytes. Calls come from the one thread that runs the
/// network; a transport that receives on another thread queues what it gets.
/// </remarks>
public interface INetTransport : IDisposable
{
    /// <summary>Gets the address this transport receives on.</summary>
    NetAddress LocalAddress { get; }

    /// <summary>Gets the largest datagram the transport carries.</summary>
    int MaxDatagramSize { get; }

    /// <summary>Sends a datagram.</summary>
    /// <param name="destination">Where to.</param>
    /// <param name="datagram">The bytes.</param>
    void Send(NetAddress destination, ReadOnlySpan<byte> datagram);

    /// <summary>Takes the next datagram that has arrived, if any.</summary>
    /// <param name="buffer">Where to copy it; at least <see cref="MaxDatagramSize"/> bytes.</param>
    /// <param name="source">Where it came from.</param>
    /// <param name="length">How many bytes it holds.</param>
    /// <returns><see langword="true"/> when a datagram was taken.</returns>
    bool TryReceive(Span<byte> buffer, out NetAddress source, out int length);

    /// <summary>Gets the bytes sent to a destination that the transport still holds, not yet on the wire.</summary>
    /// <param name="destination">Where to.</param>
    /// <returns>The bytes; nought for a transport that hands everything straight to the network.</returns>
    int Backlog(NetAddress destination) => 0;
}

/// <summary>A transport that can also send to every host on the local network, for LAN server discovery.</summary>
public interface IBroadcastTransport : INetTransport
{
    /// <summary>Sends a datagram to every host on the local network at a port.</summary>
    /// <param name="port">The port the servers listen on.</param>
    /// <param name="datagram">The bytes.</param>
    void Broadcast(int port, ReadOnlySpan<byte> datagram);
}
