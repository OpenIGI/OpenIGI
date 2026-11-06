using System.Diagnostics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// A demo played back: a stand-in server, in this process, that delivers the recorded messages to an ordinary client at the
/// times they first came, so the client draws the session again exactly as it drew it then. It is the client's transport: each
/// read the client makes runs the stand-in first.
/// </summary>
/// <remarks>
/// The recording player is played back as one of the others: the server's welcome is given a player nobody is, so the viewer
/// walks the level as a ghost while the session plays out round it. Whatever the client sends -- commands, acknowledgements --
/// is read and dropped.
/// </remarks>
public sealed class DemoPlayback : INetTransport
{
    /// <summary>The entity the viewer is welcomed as: one no session gives out.</summary>
    public const int GhostEntity = 65000;

    private readonly NetServer _server;
    private readonly LoopbackTransport _client;
    private readonly IReadOnlyList<DemoMessage> _messages;
    private readonly Func<double> _now;
    private readonly BitWriter _writer = new();
    private int? _peer;
    private double _startedAt;
    private int _next;

    /// <summary>Initializes a new instance of the <see cref="DemoPlayback"/> class.</summary>
    /// <param name="header">What the demo is of.</param>
    /// <param name="messages">Its messages.</param>
    /// <param name="clock">The clock it plays by, in seconds; the wall's by default.</param>
    /// <exception cref="InvalidDataException">It was recorded with another protocol, whose messages this game cannot read.</exception>
    public DemoPlayback(DemoHeader header, IReadOnlyList<DemoMessage> messages, Func<double>? clock = null)
    {
        Stopwatch wall = Stopwatch.StartNew();
        _now = clock ?? (() => wall.Elapsed.TotalSeconds);
        ArgumentNullException.ThrowIfNull(header);
        if (header.Protocol != MultiplayerProtocol.Version)
        {
            throw new InvalidDataException($"recorded with multiplayer protocol {header.Protocol}; this game speaks {MultiplayerProtocol.Version}");
        }

        Header = header;
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        LoopbackNetwork network = new();
        _server = new NetServer(network.Open(26001), new NetServerOptions { ProtocolId = MultiplayerProtocol.IdFor(header.Title), ProtocolVersion = MultiplayerProtocol.Version, MaxClients = 1 });
        _client = network.Open();
    }

    /// <summary>Gets what the demo is of.</summary>
    public DemoHeader Header { get; }

    /// <summary>Gets where the client joins the stand-in.</summary>
    public NetAddress Server => _server.LocalAddress;

    /// <summary>Gets a value indicating whether every message has been delivered.</summary>
    public bool IsFinished => _next >= _messages.Count;

    /// <summary>Gets how far in the demo has played, in seconds.</summary>
    public double Seconds => _peer is null ? 0 : _now() - _startedAt;

    /// <summary>Gets the demo's length, in seconds.</summary>
    public double Length => _messages.Count == 0 ? 0 : _messages[^1].Seconds;

    /// <inheritdoc/>
    public NetAddress LocalAddress => _client.LocalAddress;

    /// <inheritdoc/>
    public int MaxDatagramSize => _client.MaxDatagramSize;

    /// <inheritdoc/>
    public void Send(NetAddress destination, ReadOnlySpan<byte> datagram) => _client.Send(destination, datagram);

    /// <inheritdoc/>
    public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length)
    {
        Pump();
        return _client.TryReceive(buffer, out source, out length);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _server.Dispose();
        _client.Dispose();
    }

    // The stand-in's turn: the client let in, and every message now due delivered.
    private void Pump()
    {
        double now = _now();
        _server.Update(now);
        while (_server.TryReceive(out NetEvent e))
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    _peer = e.Peer;
                    _startedAt = now;
                    _next = 0;
                    break;
                case NetEventKind.Disconnected when e.Peer == _peer:
                    _peer = null;
                    break;
            }
        }

        if (_peer is not { } peer)
        {
            return;
        }

        double at = now - _startedAt;
        while (_next < _messages.Count && _messages[_next].Seconds <= at)
        {
            DemoMessage message = _messages[_next++];
            _server.Send(peer, message.Channel, Viewed(message.Data));
        }

        _server.Flush(now);
    }

    // A message as the viewer is given it: the welcome to a player nobody is, the rest as they came.
    private byte[] Viewed(byte[] data)
    {
        BitReader reader = new(data);
        if (data.Length == 0 || (SessionMessage)reader.ReadBits(8) != SessionMessage.Welcome)
        {
            return data;
        }

        _ = reader.ReadVarUInt();
        uint tickRate = reader.ReadVarUInt();
        string map = reader.ReadString(64);
        uint epoch = reader.ReadBits(8);
        string mode = reader.ReadString(16);
        _writer.Reset();
        _writer.WriteBits((uint)SessionMessage.Welcome, 8);
        _writer.WriteVarUInt(GhostEntity);
        _writer.WriteVarUInt(tickRate);
        _writer.WriteString(map, 64);
        _writer.WriteBits(epoch, 8);
        _writer.WriteString(mode, 16);
        return _writer.AsSpan().ToArray();
    }
}
