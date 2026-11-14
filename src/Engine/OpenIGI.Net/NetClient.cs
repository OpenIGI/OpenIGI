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
    private readonly ushort _protocolVersion;

    // This attempt's half of the key exchange, and the keys agreed once the server's half came with its challenge.
    private System.Security.Cryptography.ECDiffieHellman? _sealKey;
    private PacketCipher? _cipher;

    /// <summary>Initializes a new instance of the <see cref="NetClient"/> class.</summary>
    /// <param name="transport">What it sends on; the client disposes it.</param>
    /// <param name="protocolId">The id of the protocol, which must match the server's.</param>
    /// <param name="protocolVersion">The version of the protocol; a server of another turns the client down and says which it plays.</param>
    public NetClient(INetTransport transport, ulong protocolId, ushort protocolVersion = 0)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _protocolId = protocolId;
        _protocolVersion = protocolVersion;
        _receiveBuffer = new byte[Math.Max(transport.MaxDatagramSize, NetConstants.MaxPacketSize)];
    }

    /// <summary>Gets or sets whether the client seals its packets when it can: on by default, off only for tests and diagnosis.</summary>
    /// <remarks>A transport that is encrypted already (<see cref="INetTransport.IsEncrypted"/>) is never sealed again.</remarks>
    public bool Seal { get; set; } = true;

    /// <summary>Gets or sets the bytes a second the connection sends at most (Source's <c>rate</c>).</summary>
    public int Rate { get; set; } = NetConstants.DefaultRate;

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
        ForgetKeys();
        if (Seal && !_transport.IsEncrypted && PacketCipher.IsSupported)
        {
            _sealKey = PacketCipher.CreateKey();
        }

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
            case NetClientState.Connected when Connection is { } live && now - live.LastReceiveTime > Timeout:
                EndConnection(TimedOutReason);
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
            int length = Connection!.WriteDisconnect(_sendBuffer, reason);
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
        ForgetKeys();
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

                // A server that can seal sent its key: the keys are agreed now, and the answer goes sealed. One that can't
                // sent none, and the connection runs in the clear.
                _cipher?.Dispose();
                _cipher = null;
                if (_sealKey is not null && packet.Length >= prefix + 24 + PacketCipher.PublicKeySize)
                {
                    _cipher = PacketCipher.Agree(_sealKey, packet.Slice(prefix + 24, PacketCipher.PublicKeySize), _nonce, isServer: false);
                    if (_cipher is null)
                    {
                        return;
                    }
                }

                State = NetClientState.Responding;
                SendHandshake(now);
                return;

            case PacketKind.Accepted when State is NetClientState.Requesting or NetClientState.Responding
                                          && packet.Length >= prefix + 14
                                          && BinaryPrimitives.ReadUInt64LittleEndian(packet[prefix..]) == _nonce:
            {
                uint salt = BinaryPrimitives.ReadUInt32LittleEndian(packet[(prefix + 8)..]);
                Slot = BinaryPrimitives.ReadUInt16LittleEndian(packet[(prefix + 12)..]);
                Connection = new NetConnection(_transport, _server, _protocolId, salt, now, _cipher) { Rate = Rate };
                _cipher = null;
                _sealKey?.Dispose();
                _sealKey = null;
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

            case PacketKind.Payload when State == NetClientState.Connected && Connection is { } live
                                         && packet.Length >= prefix + NetConstants.PayloadHeaderSize
                                         && BinaryPrimitives.ReadUInt32LittleEndian(packet[prefix..]) == live.Salt:
                _messages.Clear();
                live.ReceivePayload(packet, now, _messages);
                foreach (NetMessage message in _messages)
                {
                    _events.Enqueue(new NetEvent(NetEventKind.Message, Slot, message, string.Empty, null));
                }

                if (live.IsFaulted)
                {
                    Disconnect("protocol error", now);
                }

                return;

            case PacketKind.Disconnect when State == NetClientState.Connected && Connection is { } leaving
                                            && leaving.TryReadDisconnect(packet, out string left):
                EndConnection(left);
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
            BinaryPrimitives.WriteUInt16LittleEndian(request[(prefix + 8)..], _protocolVersion);
            request[prefix + 10] = (byte)(_sealKey is null ? ConnectFlags.None : ConnectFlags.Sealed);
            SendOutOfBand(PacketKind.ConnectionRequest, request);
            return;
        }

        // The answer: the challenge echoed, then -- sealed -- this side's key and the join payload under the agreed keys.
        int keySize = _cipher is null ? 0 : PacketCipher.PublicKeySize;
        int sealSize = _cipher is null ? 0 : PacketCipher.Overhead;
        Span<byte> response = _sendBuffer.AsSpan(0, prefix + 25 + keySize + _connectPayload.Length + sealSize);
        _challenge.CopyTo(response[prefix..]);
        response[prefix + 24] = (byte)(_cipher is null ? ConnectFlags.None : ConnectFlags.Sealed);
        if (_cipher is null)
        {
            _connectPayload.CopyTo(response[(prefix + 25)..]);
        }
        else
        {
            PacketCipher.WritePublicKey(_sealKey!, response[(prefix + 25)..]);
            _cipher.Seal(0, _challenge, _connectPayload, response[(prefix + 25 + keySize)..]);
        }

        SendOutOfBand(PacketKind.ChallengeResponse, response);
    }

    private void ForgetKeys()
    {
        _cipher?.Dispose();
        _cipher = null;
        _sealKey?.Dispose();
        _sealKey = null;
    }

    /// <summary>The reason a connection ends with when the server fell silent: the line, not anyone's choice.</summary>
    public const string TimedOutReason = "timed out";

    private void EndConnection(string reason)
    {
        State = NetClientState.Disconnected;
        Connection?.Dispose();
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
