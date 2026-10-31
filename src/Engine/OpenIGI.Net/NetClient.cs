using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenIGI.Net;

/// <summary>Where a <see cref="NetClient"/> is in joining a server.</summary>
public enum NetClientState : byte
{
    /// <summary>Not connected and not trying.</summary>
    Disconnected,

    /// <summary>Sending the connection request, waiting for a challenge.</summary>
    Requesting,

    /// <summary>Echoing the challenge with the join payload, waiting to be accepted.</summary>
    Responding,

    /// <summary>Connected.</summary>
    Connected,
}

/// <summary>The joining end: runs the handshake against one server and then carries the connection.</summary>
/// <remarks>The handshake is described on <see cref="NetServer"/>. Each step is resent until answered, so a lost packet only costs time.</remarks>
public sealed class NetClient : IDisposable
{
    private const double HandshakeResendInterval = 0.25;

    private readonly INetTransport _transport;
    private readonly ulong _protocolId;
    private readonly Queue<NetEvent> _events = new();
    private readonly List<NetMessage> _messages = [];
    private readonly byte[] _receiveBuffer;
    private readonly byte[] _sendBuffer = new byte[NetConstants.MaxPacketSize];

    private NetAddress _server;
    private byte[] _connectPayload = [];
    private ulong _nonce;
    private byte[] _challenge = [];
    private double _attemptStarted;
    private double _lastHandshakeSend;

    /// <summary>Initializes a new instance of the <see cref="NetClient"/> class.</summary>
    /// <param name="transport">What it sends on; the client disposes it.</param>
    /// <param name="protocolId">The id of the protocol, which must match the server's.</param>
    public NetClient(INetTransport transport, ulong protocolId)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _protocolId = protocolId;
        _receiveBuffer = new byte[Math.Max(transport.MaxDatagramSize, NetConstants.MaxPacketSize)];
    }

    /// <summary>Gets where the client is in joining.</summary>
    public NetClientState State { get; private set; }

    /// <summary>Gets the slot the server gave this client, or -1 before it is connected.</summary>
    public int Slot { get; private set; } = -1;

    /// <summary>Gets the connection once established.</summary>
    public NetConnection? Connection { get; private set; }

    /// <summary>Gets or sets how long to wait for the server before giving up, in seconds.</summary>
    public double Timeout { get; set; } = 10;

    /// <summary>Starts joining a server, dropping any connection held.</summary>
    /// <param name="server">The server's address.</param>
    /// <param name="payload">What the server's admission judges: name, game, map, password.</param>
    /// <param name="now">The current time in seconds.</param>
    /// <exception cref="ArgumentException">The payload is larger than a request carries.</exception>
    public void Connect(NetAddress server, ReadOnlySpan<byte> payload, double now)
    {
        if (payload.Length > NetConstants.MaxConnectPayloadSize)
        {
            throw new ArgumentException($"A join payload is at most {NetConstants.MaxConnectPayloadSize} bytes.", nameof(payload));
        }

        if (State != NetClientState.Disconnected)
        {
            Disconnect("reconnecting", now);
        }

        _server = server;
        _connectPayload = payload.ToArray();
        Span<byte> nonce = stackalloc byte[8];
        RandomNumberGenerator.Fill(nonce);
        _nonce = BinaryPrimitives.ReadUInt64LittleEndian(nonce);
        _attemptStarted = now;
        _lastHandshakeSend = double.NegativeInfinity;
        State = NetClientState.Requesting;
    }

    /// <summary>Reads what has arrived, advances the handshake and queues what happened.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Update(double now)
    {
        while (_transport.TryReceive(_receiveBuffer, out NetAddress source, out int length))
        {
            ReadOnlySpan<byte> packet = _receiveBuffer.AsSpan(0, length);
            if (source == _server && PacketCodec.TryOpen(packet, _protocolId, out PacketKind kind))
            {
                Handle(kind, packet, now);
            }
        }

        switch (State)
        {
            case NetClientState.Requesting or NetClientState.Responding when now - _attemptStarted > Timeout:
                State = NetClientState.Disconnected;
                _events.Enqueue(new NetEvent(NetEventKind.Rejected, -1, default, "no response from server", null));
                break;
            case NetClientState.Requesting or NetClientState.Responding when now - _lastHandshakeSend >= HandshakeResendInterval:
                SendHandshake(now);
                break;
            case NetClientState.Connected when now - Connection!.LastReceiveTime > Timeout:
                EndConnection("timed out");
                break;
        }
    }

    /// <summary>Takes the next thing that happened.</summary>
    /// <param name="netEvent">What happened.</param>
    /// <returns><see langword="true"/> when there was one.</returns>
    public bool TryReceive(out NetEvent netEvent) => _events.TryDequeue(out netEvent);

    /// <summary>Queues a message to the server; dropped unless connected.</summary>
    /// <param name="channel">How to deliver it.</param>
    /// <param name="data">The bytes.</param>
    public void Send(NetChannel channel, ReadOnlySpan<byte> data) => Connection?.Send(channel, data);

    /// <summary>Sends the server what is queued for it.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Flush(double now) => Connection?.Flush(now);

    /// <summary>Leaves the server, telling it why.</summary>
    /// <param name="reason">Why.</param>
    /// <param name="now">The current time in seconds.</param>
    public void Disconnect(string reason, double now)
    {
        _ = now;
        if (State == NetClientState.Connected)
        {
            int prefix = NetConstants.PacketPrefixSize;
            BinaryPrimitives.WriteUInt32LittleEndian(_sendBuffer.AsSpan(prefix), Connection!.Salt);
            int length = prefix + 4 + PacketCodec.WriteString(_sendBuffer.AsSpan(prefix + 4), reason);
            for (int i = 0; i < 3; i++)
            {
                SendOutOfBand(PacketKind.Disconnect, _sendBuffer.AsSpan(0, length));
            }

            EndConnection(reason);
            return;
        }

        State = NetClientState.Disconnected;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Disconnect("client closed", 0);
        _transport.Dispose();
    }

    private void Handle(PacketKind kind, ReadOnlySpan<byte> packet, double now)
    {
        int prefix = NetConstants.PacketPrefixSize;
        switch (kind)
        {
            case PacketKind.Challenge when State == NetClientState.Requesting && packet.Length >= prefix + 24
                                           && BinaryPrimitives.ReadUInt64LittleEndian(packet[prefix..]) == _nonce:
                _challenge = packet.Slice(prefix, 24).ToArray();
                State = NetClientState.Responding;
                SendHandshake(now);
                return;

            case PacketKind.Accepted when State is NetClientState.Requesting or NetClientState.Responding
                                          && packet.Length >= prefix + 14
                                          && BinaryPrimitives.ReadUInt64LittleEndian(packet[prefix..]) == _nonce:
            {
                uint salt = BinaryPrimitives.ReadUInt32LittleEndian(packet[(prefix + 8)..]);
                Slot = BinaryPrimitives.ReadUInt16LittleEndian(packet[(prefix + 12)..]);
                Connection = new NetConnection(_transport, _server, _protocolId, salt, now);
                State = NetClientState.Connected;
                _events.Enqueue(new NetEvent(NetEventKind.Connected, Slot, default, string.Empty, packet[(prefix + 14)..].ToArray()));
                return;
            }

            case PacketKind.Rejected when State is NetClientState.Requesting or NetClientState.Responding
                                          && packet.Length >= prefix + 8
                                          && BinaryPrimitives.ReadUInt64LittleEndian(packet[prefix..]) == _nonce:
                State = NetClientState.Disconnected;
                _events.Enqueue(new NetEvent(NetEventKind.Rejected, -1, default, PacketCodec.ReadString(packet[(prefix + 8)..]), null));
                return;

            case PacketKind.Payload when State == NetClientState.Connected
                                         && packet.Length >= prefix + NetConstants.PayloadHeaderSize
                                         && BinaryPrimitives.ReadUInt32LittleEndian(packet[prefix..]) == Connection!.Salt:
                _messages.Clear();
                Connection.ReceivePayload(packet, now, _messages);
                foreach (NetMessage message in _messages)
                {
                    _events.Enqueue(new NetEvent(NetEventKind.Message, Slot, message, string.Empty, null));
                }

                if (Connection.IsFaulted)
                {
                    Disconnect("protocol error", now);
                }

                return;

            case PacketKind.Disconnect when State == NetClientState.Connected
                                            && packet.Length >= prefix + 4
                                            && BinaryPrimitives.ReadUInt32LittleEndian(packet[prefix..]) == Connection!.Salt:
                EndConnection(PacketCodec.ReadString(packet[(prefix + 4)..]));
                return;
        }
    }

    private void SendHandshake(double now)
    {
        _lastHandshakeSend = now;
        int prefix = NetConstants.PacketPrefixSize;
        if (State == NetClientState.Requesting)
        {
            Span<byte> request = _sendBuffer.AsSpan(0, NetConstants.ConnectionRequestSize);
            request.Clear();
            BinaryPrimitives.WriteUInt64LittleEndian(request[prefix..], _nonce);
            SendOutOfBand(PacketKind.ConnectionRequest, request);
            return;
        }

        Span<byte> response = _sendBuffer.AsSpan(0, prefix + 24 + _connectPayload.Length);
        _challenge.CopyTo(response[prefix..]);
        _connectPayload.CopyTo(response[(prefix + 24)..]);
        SendOutOfBand(PacketKind.ChallengeResponse, response);
    }

    private void EndConnection(string reason)
    {
        State = NetClientState.Disconnected;
        Connection = null;
        Slot = -1;
        _events.Enqueue(new NetEvent(NetEventKind.Disconnected, -1, default, reason, null));
    }

    private void SendOutOfBand(PacketKind kind, Span<byte> packet)
    {
        PacketCodec.Seal(packet, kind, _protocolId);
        _transport.Send(_server, packet);
    }
}
