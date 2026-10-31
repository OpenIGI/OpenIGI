namespace OpenIGI.Net;

/// <summary>How bad a <see cref="SimulatedLinkTransport"/> makes the line, in each direction.</summary>
public sealed class SimulatedLinkSettings
{
    /// <summary>Gets or sets the one-way delay added to every datagram, in seconds (<c>net_fakelag</c>).</summary>
    public double Latency { get; set; }

    /// <summary>Gets or sets the most random delay added on top, in seconds (<c>net_fakejitter</c>).</summary>
    public double Jitter { get; set; }

    /// <summary>Gets or sets the chance a datagram is dropped, 0 to 1 (<c>net_fakeloss</c>).</summary>
    public double Loss { get; set; }

    /// <summary>Gets or sets the chance a datagram is delivered twice, 0 to 1 (<c>net_fakedup</c>).</summary>
    public double Duplicate { get; set; }

    /// <summary>Gets or sets the bytes a second the line carries to each destination, or nought for no limit (<c>net_fakebandwidth</c>).</summary>
    /// <remarks>
    /// Only the sending side applies it. What is sent faster waits its turn, as on a WebRTC channel, which queues without
    /// end rather than dropping; <see cref="SimulatedLinkTransport.Backlog"/> reports the bytes waiting.
    /// </remarks>
    public double Bandwidth { get; set; }

    /// <summary>Gets a value indicating whether the line is perfect, so nothing need be queued.</summary>
    public bool IsPerfect => Latency <= 0 && Jitter <= 0 && Loss <= 0 && Duplicate <= 0 && Bandwidth <= 0;
}

/// <summary>Wraps a transport in a bad line: delay, jitter (and so reordering), loss and duplication on both directions.</summary>
/// <remarks>
/// Source's <c>net_fakelag</c> family, and what the retail network layer's own packet-loss switch was for. The tests run
/// the reliability layer through it; a developer turns it on from the console to feel a session at 150 ms.
/// </remarks>
public sealed class SimulatedLinkTransport : INetTransport
{
    private readonly INetTransport _inner;
    private readonly Func<double> _clock;
    private readonly Random _random;
    private readonly PriorityQueue<Pending, double> _outgoing = new();
    private readonly PriorityQueue<Pending, double> _incoming = new();
    private readonly byte[] _scratch;

    // Per destination, under a bandwidth limit: when the line is next free, and the datagrams still waiting for it.
    private readonly Dictionary<NetAddress, Wire> _wires = [];

    /// <summary>Initializes a new instance of the <see cref="SimulatedLinkTransport"/> class.</summary>
    /// <param name="inner">The real transport.</param>
    /// <param name="clock">The current time in seconds.</param>
    /// <param name="seed">The seed of the random line, so a failing test repeats.</param>
    public SimulatedLinkTransport(INetTransport inner, Func<double> clock, int seed = 1)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _random = new Random(seed);
        _scratch = new byte[inner.MaxDatagramSize];
    }

    /// <summary>Gets what happens to datagrams this side sends.</summary>
    public SimulatedLinkSettings Send { get; } = new();

    /// <summary>Gets what happens to datagrams this side receives.</summary>
    public SimulatedLinkSettings Receive { get; } = new();

    /// <inheritdoc/>
    public NetAddress LocalAddress => _inner.LocalAddress;

    /// <inheritdoc/>
    public int MaxDatagramSize => _inner.MaxDatagramSize;

    /// <inheritdoc/>
    public int Backlog(NetAddress destination)
    {
        int waiting = 0;
        if (_wires.TryGetValue(destination, out Wire? wire))
        {
            double now = _clock();
            while (wire.Waiting.TryPeek(out (double Leaves, int Size) first) && first.Leaves <= now)
            {
                wire.Waiting.Dequeue();
            }

            waiting = wire.Waiting.Sum(static w => w.Size);
        }

        return waiting + _inner.Backlog(destination);
    }

    /// <inheritdoc/>
    void INetTransport.Send(NetAddress destination, ReadOnlySpan<byte> datagram)
    {
        FlushOutgoing();
        if (Send.IsPerfect && _outgoing.Count == 0)
        {
            _inner.Send(destination, datagram);
            return;
        }

        double leaves = _clock();
        if (Send.Bandwidth > 0)
        {
            if (!_wires.TryGetValue(destination, out Wire? wire))
            {
                _wires[destination] = wire = new Wire();
            }

            wire.FreeAt = Math.Max(wire.FreeAt, leaves) + (datagram.Length / Send.Bandwidth);
            leaves = wire.FreeAt;
            wire.Waiting.Enqueue((leaves, datagram.Length));
        }

        Schedule(_outgoing, Send, destination, datagram, leaves);
        FlushOutgoing();
    }

    /// <inheritdoc/>
    public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length)
    {
        FlushOutgoing();
        while (_inner.TryReceive(_scratch, out NetAddress from, out int size))
        {
            Schedule(_incoming, Receive, from, _scratch.AsSpan(0, size), _clock());
        }

        if (_incoming.TryPeek(out Pending next, out double due) && due <= _clock())
        {
            _incoming.Dequeue();
            next.Datagram.CopyTo(buffer);
            source = next.Address;
            length = next.Datagram.Length;
            return true;
        }

        source = default;
        length = 0;
        return false;
    }

    /// <inheritdoc/>
    public void Dispose() => _inner.Dispose();

    private void Schedule(PriorityQueue<Pending, double> queue, SimulatedLinkSettings line, NetAddress address, ReadOnlySpan<byte> datagram, double leaves)
    {
        if (line.Loss > 0 && _random.NextDouble() < line.Loss)
        {
            return;
        }

        int copies = line.Duplicate > 0 && _random.NextDouble() < line.Duplicate ? 2 : 1;
        for (int i = 0; i < copies; i++)
        {
            double due = leaves + line.Latency + (line.Jitter * _random.NextDouble());
            queue.Enqueue(new Pending(address, datagram.ToArray()), due);
        }
    }

    private void FlushOutgoing()
    {
        double now = _clock();
        while (_outgoing.TryPeek(out Pending next, out double due) && due <= now)
        {
            _outgoing.Dequeue();
            _inner.Send(next.Address, next.Datagram);
        }
    }

    private readonly record struct Pending(NetAddress Address, byte[] Datagram);

    private sealed class Wire
    {
        public double FreeAt { get; set; } = double.NegativeInfinity;

        public Queue<(double Leaves, int Size)> Waiting { get; } = new();
    }
}
