namespace OpenIGI.Net;

/// <summary>
/// Several transports heard as one: a dedicated server's UDP port and its WebRTC room together, so a desktop joins it by address
/// and a browser by the room, into the one session.
/// </summary>
/// <remarks>
/// The parts' addresses never overlap -- a room's peers are <see cref="NetAddressKind.Peer"/> addresses, UDP's are IP ones -- so
/// a datagram goes back through the part whose kind of address it is for. Receiving takes from each part in turn, so one busy
/// part does not starve another.
/// </remarks>
public sealed class MultiTransport : INetTransport
{
    private readonly INetTransport[] _parts;

    // Which part each address was last heard on: answered there, whatever kind of address it is.
    private readonly Dictionary<NetAddress, INetTransport> _heardOn = [];
    private int _next;

    /// <summary>Initialises a new instance of the <see cref="MultiTransport"/> class.</summary>
    /// <param name="parts">The transports, the first the one whose address is this one's; disposed with it.</param>
    public MultiTransport(params INetTransport[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
        {
            throw new ArgumentException("A transport needs at least one part.", nameof(parts));
        }

        _parts = [.. parts];
        MaxDatagramSize = _parts.Min(p => p.MaxDatagramSize);
    }

    // How many addresses are remembered before the memory is let go and learnt again: far more than a server's players.
    private const int MaxRemembered = 4096;

    /// <summary>Gets the transports.</summary>
    public IReadOnlyList<INetTransport> Parts => _parts;

    /// <inheritdoc/>
    public NetAddress LocalAddress => _parts[0].LocalAddress;

    /// <inheritdoc/>
    public int MaxDatagramSize { get; }

    /// <summary>Gets the part of a kind, or <see langword="null"/>.</summary>
    /// <typeparam name="T">The kind.</typeparam>
    /// <returns>The part.</returns>
    public T? Part<T>()
        where T : class, INetTransport => _parts.OfType<T>().FirstOrDefault();

    /// <inheritdoc/>
    public void Send(NetAddress destination, ReadOnlySpan<byte> datagram) => For(destination).Send(destination, datagram);

    /// <inheritdoc/>
    public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length)
    {
        for (int i = 0; i < _parts.Length; i++)
        {
            INetTransport part = _parts[(_next + i) % _parts.Length];
            if (part.TryReceive(buffer, out source, out length))
            {
                if (_heardOn.Count > MaxRemembered)
                {
                    _heardOn.Clear();
                }

                _heardOn[source] = part;
                _next = (_next + i + 1) % _parts.Length;
                return true;
            }
        }

        source = default;
        length = 0;
        return false;
    }

    /// <inheritdoc/>
    public int Backlog(NetAddress destination) => For(destination).Backlog(destination);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (INetTransport part in _parts)
        {
            part.Dispose();
        }
    }

    // The part a destination's kind of address belongs to: a peer to the part that hands out peers, anything else to the first
    // part that does not.
    private INetTransport For(NetAddress destination)
    {
        // The part it was heard on; else a part of its own kind, as an in-process loopback part beside a UDP one.
        if (_heardOn.TryGetValue(destination, out INetTransport? heard))
        {
            return heard;
        }

        foreach (INetTransport part in _parts)
        {
            if (part.LocalAddress.Kind == destination.Kind)
            {
                return part;
            }
        }

        bool peer = destination.Kind == NetAddressKind.Peer;
        foreach (INetTransport part in _parts)
        {
            if ((part.LocalAddress.Kind == NetAddressKind.Peer) == peer)
            {
                return part;
            }
        }

        return _parts[0];
    }
}
