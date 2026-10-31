namespace OpenIGI.Net;

/// <summary>An in-process network: endpoints that hand datagrams to each other's queues.</summary>
/// <remarks>
/// A listen server's own client talks to it over this, and the tests run whole sessions on it without a socket. Delivery
/// is immediate and perfect; wrap an endpoint in <see cref="SimulatedLinkTransport"/> for a bad line. Thread-safe, so a
/// test may run the server and a client on different threads.
/// </remarks>
public sealed class LoopbackNetwork
{
    private readonly Dictionary<int, LoopbackTransport> _endpoints = [];
    private readonly Lock _gate = new();

    /// <summary>Opens an endpoint.</summary>
    /// <param name="port">Its number; zero picks a free one.</param>
    /// <returns>The endpoint.</returns>
    /// <exception cref="InvalidOperationException">The number is taken.</exception>
    public LoopbackTransport Open(int port = 0)
    {
        lock (_gate)
        {
            if (port == 0)
            {
                port = 50000;
                while (_endpoints.ContainsKey(port))
                {
                    port++;
                }
            }

            if (_endpoints.ContainsKey(port))
            {
                throw new InvalidOperationException($"Loopback port {port} is already open.");
            }

            LoopbackTransport endpoint = new(this, port);
            _endpoints.Add(port, endpoint);
            return endpoint;
        }
    }

    internal void Deliver(int fromPort, int toPort, ReadOnlySpan<byte> datagram)
    {
        LoopbackTransport? target;
        lock (_gate)
        {
            _endpoints.TryGetValue(toPort, out target);
        }

        target?.Enqueue(NetAddress.Loopback(fromPort), datagram.ToArray());
    }

    internal void Close(int port)
    {
        lock (_gate)
        {
            _endpoints.Remove(port);
        }
    }
}

/// <summary>One endpoint of a <see cref="LoopbackNetwork"/>.</summary>
public sealed class LoopbackTransport : INetTransport
{
    private readonly LoopbackNetwork _network;
    private readonly Queue<(NetAddress Source, byte[] Datagram)> _inbox = new();
    private readonly Lock _gate = new();
    private bool _disposed;

    internal LoopbackTransport(LoopbackNetwork network, int port)
    {
        _network = network;
        LocalAddress = NetAddress.Loopback(port);
    }

    /// <inheritdoc/>
    public NetAddress LocalAddress { get; }

    /// <inheritdoc/>
    public int MaxDatagramSize => NetConstants.MaxPacketSize;

    /// <inheritdoc/>
    public void Send(NetAddress destination, ReadOnlySpan<byte> datagram)
    {
        if (!_disposed && destination.Kind == NetAddressKind.Loopback && datagram.Length <= MaxDatagramSize)
        {
            _network.Deliver(LocalAddress.Port, destination.Port, datagram);
        }
    }

    /// <inheritdoc/>
    public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length)
    {
        lock (_gate)
        {
            if (_inbox.TryDequeue(out (NetAddress Source, byte[] Datagram) item))
            {
                item.Datagram.CopyTo(buffer);
                source = item.Source;
                length = item.Datagram.Length;
                return true;
            }
        }

        source = default;
        length = 0;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _network.Close(LocalAddress.Port);
        }
    }

    internal void Enqueue(NetAddress source, byte[] datagram)
    {
        lock (_gate)
        {
            _inbox.Enqueue((source, datagram));
        }
    }
}
