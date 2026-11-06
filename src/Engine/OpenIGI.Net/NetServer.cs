using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenIGI.Net;

/// <summary>How a <see cref="NetServer"/> runs.</summary>
public sealed class NetServerOptions
{
    /// <summary>Gets or sets the id of the protocol: the game. A client with another id is never heard.</summary>
    public ulong ProtocolId { get; set; }

    /// <summary>Gets or sets the version of the protocol: a client of another is turned down, told which this server speaks.</summary>
    public ushort ProtocolVersion { get; set; }

    /// <summary>Gets or sets the bytes a second each connection sends at most (Source's <c>rate</c>).</summary>
    public int Rate { get; set; } = NetConstants.DefaultRate;

    /// <summary>Gets or sets the most clients connected at once.</summary>
    public int MaxClients { get; set; } = 32;

    /// <summary>Gets or sets how long a silent client is kept, in seconds.</summary>
    public double Timeout { get; set; } = 10;

    /// <summary>Gets or sets who may join. Unset, everyone may while there is room.</summary>
    public Func<NetConnectRequest, NetAdmission>? Admit { get; set; }

    /// <summary>Gets or sets what the server tells a browser that asks about it, or <see langword="null"/> to stay quiet.</summary>
    public Func<NetAddress, byte[]?>? Info { get; set; }
}

/// <summary>The listening end: admits clients through a challenge, keeps a connection per client, reports what arrives.</summary>
/// <remarks>
/// <para>
/// Joining takes four packets. The client's request is padded to <see cref="NetConstants.ConnectionRequestSize"/> bytes
/// and answered with a small challenge, so the server can never be made to send more than it was sent. The challenge is a
/// keyed hash of the client's address, its nonce and the time; only a client that really receives at that address can
/// echo it back, and only then is a slot spent on it. The echo carries the application's join payload, which
/// <see cref="NetServerOptions.Admit"/> judges.
/// </para>
/// <para>
/// One thread calls <see cref="Update"/>, <see cref="Send"/> and <see cref="Flush"/>; the game's network tick does.
/// </para>
/// </remarks>
public sealed class NetServer : IDisposable
{
    private const double ChallengeBucket = 10;

    private readonly INetTransport _transport;
    private readonly NetServerOptions _options;
    private readonly Peer?[] _peers;
    private readonly Dictionary<NetAddress, int> _byAddress = [];
    private readonly Queue<NetEvent> _events = new();
    private readonly List<NetMessage> _messages = [];
    private readonly byte[] _receiveBuffer;
    private readonly byte[] _sendBuffer = new byte[NetConstants.MaxPacketSize];
    private readonly ulong _key0;
    private readonly ulong _key1;

    // The server's half of each sealed connection's key exchange, one pair for the run; null where the platform can't seal.
    private readonly System.Security.Cryptography.ECDiffieHellman? _sealKey;
    private readonly byte[] _sealPublic = new byte[PacketCipher.PublicKeySize];

    /// <summary>Initializes a new instance of the <see cref="NetServer"/> class.</summary>
    /// <param name="transport">What it listens on; the server disposes it.</param>
    /// <param name="options">How it runs.</param>
    public NetServer(INetTransport transport, NetServerOptions options)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxClients, 1);
        _peers = new Peer?[options.MaxClients];
        _receiveBuffer = new byte[Math.Max(transport.MaxDatagramSize, NetConstants.MaxPacketSize)];
        Span<byte> key = stackalloc byte[16];
        RandomNumberGenerator.Fill(key);
        _key0 = BinaryPrimitives.ReadUInt64LittleEndian(key);
        _key1 = BinaryPrimitives.ReadUInt64LittleEndian(key[8..]);
        if (PacketCipher.IsSupported)
        {
            _sealKey = PacketCipher.CreateKey();
            PacketCipher.WritePublicKey(_sealKey, _sealPublic);
        }
    }

    /// <summary>Gets the address the server listens on.</summary>
    public NetAddress LocalAddress => _transport.LocalAddress;

    /// <summary>Gets how many clients are connected.</summary>
    public int ClientCount => _byAddress.Count;

    /// <summary>Gets the most clients connected at once.</summary>
    public int MaxClients => _peers.Length;

    /// <summary>Gets whether a slot holds a client.</summary>
    /// <param name="peer">The slot.</param>
    /// <returns><see langword="true"/> when connected.</returns>
    public bool IsConnected(int peer) => (uint)peer < (uint)_peers.Length && _peers[peer] is not null;

    /// <summary>Gets a connected client's connection, for its address and stats.</summary>
    /// <param name="peer">The slot.</param>
    /// <returns>The connection, or <see langword="null"/>.</returns>
    public NetConnection? GetConnection(int peer) => IsConnected(peer) ? _peers[peer]!.Connection : null;

    /// <summary>Reads everything that has arrived, admits and drops clients, and queues what happened.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Update(double now)
    {
        while (_transport.TryReceive(_receiveBuffer, out NetAddress source, out int length))
        {
            ReadOnlySpan<byte> packet = _receiveBuffer.AsSpan(0, length);
            if (PacketCodec.TryOpen(packet, _options.ProtocolId, out PacketKind kind))
            {
                Handle(kind, source, packet, now);
            }
        }

        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i] is { } peer && now - peer.Connection.LastReceiveTime > _options.Timeout)
            {
                Drop(i, "timed out", notify: false);
            }
        }
    }

    /// <summary>Takes the next thing that happened.</summary>
    /// <param name="netEvent">What happened.</param>
    /// <returns><see langword="true"/> when there was one.</returns>
    public bool TryReceive(out NetEvent netEvent) => _events.TryDequeue(out netEvent);

    /// <summary>Queues a message to a client.</summary>
    /// <param name="peer">The client's slot.</param>
    /// <param name="channel">How to deliver it.</param>
    /// <param name="data">The bytes.</param>
    public void Send(int peer, NetChannel channel, ReadOnlySpan<byte> data)
    {
        if (IsConnected(peer))
        {
            _peers[peer]!.Connection.Send(channel, data);
        }
    }

    /// <summary>Queues a message to every client.</summary>
    /// <param name="channel">How to deliver it.</param>
    /// <param name="data">The bytes.</param>
    public void Broadcast(NetChannel channel, ReadOnlySpan<byte> data)
    {
        foreach (Peer? peer in _peers)
        {
            peer?.Connection.Send(channel, data);
        }
    }

    /// <summary>Sends every client what is queued for it.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Flush(double now)
    {
        foreach (Peer? peer in _peers)
        {
            peer?.Connection.Flush(now);
        }
    }

    /// <summary>Drops a client, telling it why.</summary>
    /// <param name="peer">The client's slot.</param>
    /// <param name="reason">Why, shown to the player.</param>
    public void Disconnect(int peer, string reason)
    {
        if (IsConnected(peer))
        {
            Drop(peer, reason, notify: true);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        for (int i = 0; i < _peers.Length; i++)
        {
            if (_peers[i] is not null)
            {
                Drop(i, "server shut down", notify: true);
            }
        }

        _sealKey?.Dispose();
        _transport.Dispose();
    }

    private void Handle(PacketKind kind, NetAddress source, ReadOnlySpan<byte> packet, double now)
    {
        int prefix = NetConstants.PacketPrefixSize;
        switch (kind)
        {
            case PacketKind.ConnectionRequest when packet.Length >= NetConstants.ConnectionRequestSize:
            {
                ulong nonce = BinaryPrimitives.ReadUInt64LittleEndian(packet[prefix..]);
                if (FindPeer(source) is { } existing && existing.Nonce == nonce)
                {
                    SendAccepted(existing);
                    return;
                }

                // Another version is told so at once, in words, rather than left to wait out its timeout.
                ushort version = BinaryPrimitives.ReadUInt16LittleEndian(packet[(prefix + 8)..]);
                if (version != _options.ProtocolVersion)
                {
                    SendRejected(source, nonce, string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"version mismatch: the server plays protocol {_options.ProtocolVersion}, this game {version}"));
                    return;
                }

                // A client that can seal is sent the server's key, and its answer comes sealed.
                bool sealing = _sealKey is not null && ((ConnectFlags)packet[prefix + 10] & ConnectFlags.Sealed) != 0;
                ulong bucket = (ulong)Math.Floor(now / ChallengeBucket);
                Span<byte> challenge = _sendBuffer.AsSpan(0, prefix + 24 + (sealing ? PacketCipher.PublicKeySize : 0));
                BinaryPrimitives.WriteUInt64LittleEndian(challenge[prefix..], nonce);
                BinaryPrimitives.WriteUInt64LittleEndian(challenge[(prefix + 8)..], bucket);
                BinaryPrimitives.WriteUInt64LittleEndian(challenge[(prefix + 16)..], Token(source, nonce, bucket));
                if (sealing)
                {
                    _sealPublic.CopyTo(challenge[(prefix + 24)..]);
                }

                SendOutOfBand(source, PacketKind.Challenge, challenge);
                return;
            }

            case PacketKind.ChallengeResponse when packet.Length >= prefix + 24:
                HandleChallengeResponse(source, packet, now);
                return;

            case PacketKind.Payload when packet.Length >= prefix + NetConstants.PayloadHeaderSize:
            {
                if (!_byAddress.TryGetValue(source, out int slot)
                    || _peers[slot] is not { } peer
                    || BinaryPrimitives.ReadUInt32LittleEndian(packet[prefix..]) != peer.Connection.Salt)
                {
                    return;
                }

                _messages.Clear();
                peer.Connection.ReceivePayload(packet, now, _messages);
                foreach (NetMessage message in _messages)
                {
                    _events.Enqueue(new NetEvent(NetEventKind.Message, slot, message, string.Empty, null));
                }

                if (peer.Connection.IsFaulted)
                {
                    Drop(slot, "protocol error", notify: true);
                }

                return;
            }

            case PacketKind.Disconnect when packet.Length >= prefix + 4:
            {
                if (_byAddress.TryGetValue(source, out int slot)
                    && _peers[slot] is { } peer
                    && BinaryPrimitives.ReadUInt32LittleEndian(packet[prefix..]) == peer.Connection.Salt)
                {
                    Drop(slot, PacketCodec.ReadString(packet[(prefix + 4)..]), notify: false);
                }

                return;
            }

            case PacketKind.InfoRequest when packet.Length >= prefix + 8 && _options.Info is { } info:
            {
                byte[]? blob = info(source);
                if (blob is null || blob.Length > NetConstants.MaxPacketSize - prefix - 8)
                {
                    return;
                }

                Span<byte> response = _sendBuffer.AsSpan(0, prefix + 8 + blob.Length);
                packet.Slice(prefix, 8).CopyTo(response[prefix..]);
                blob.CopyTo(response[(prefix + 8)..]);
                SendOutOfBand(source, PacketKind.InfoResponse, response);
                return;
            }
        }
    }

    private void HandleChallengeResponse(NetAddress source, ReadOnlySpan<byte> packet, double now)
    {
        int prefix = NetConstants.PacketPrefixSize;
        ulong nonce = BinaryPrimitives.ReadUInt64LittleEndian(packet[prefix..]);
        ulong bucket = BinaryPrimitives.ReadUInt64LittleEndian(packet[(prefix + 8)..]);
        ulong token = BinaryPrimitives.ReadUInt64LittleEndian(packet[(prefix + 16)..]);
        ulong nowBucket = (ulong)Math.Floor(now / ChallengeBucket);
        if (token != Token(source, nonce, bucket) || nowBucket - bucket > 1)
        {
            return;
        }

        if (FindPeer(source) is { } existing)
        {
            if (existing.Nonce == nonce)
            {
                SendAccepted(existing);
                return;
            }

            // The same address with a new attempt, its challenge answered: the game there started again (a crash, a restart)
            // and its old connection is dead. It is let in now rather than after the old one times out.
            Drop(existing.Slot, "reconnected", notify: false);
        }

        if (packet.Length < prefix + 25)
        {
            return;
        }

        ConnectFlags flags = (ConnectFlags)packet[prefix + 24];
        ReadOnlySpan<byte> body = packet[(prefix + 25)..];
        PacketCipher? cipher = null;
        byte[] payload;
        if ((flags & ConnectFlags.Sealed) != 0)
        {
            // The answer is sealed under the keys the two halves agree: one the server can't open is no answer at all.
            if (_sealKey is null || body.Length < PacketCipher.PublicKeySize + PacketCipher.Overhead
                || PacketCipher.Agree(_sealKey, body[..PacketCipher.PublicKeySize], nonce, isServer: true) is not { } agreed)
            {
                return;
            }

            ReadOnlySpan<byte> sealedPayload = body[PacketCipher.PublicKeySize..];
            payload = new byte[sealedPayload.Length - PacketCipher.Overhead];
            if (!agreed.TryOpen(0, packet.Slice(prefix, 24), sealedPayload, payload))
            {
                agreed.Dispose();
                return;
            }

            cipher = agreed;
        }
        else
        {
            payload = body.ToArray();
        }

        int slot = Array.IndexOf(_peers, null);
        if (slot < 0)
        {
            cipher?.Dispose();
            SendRejected(source, nonce, "server is full");
            return;
        }

        NetAdmission admission = _options.Admit?.Invoke(new NetConnectRequest(source, payload, slot)) ?? NetAdmission.Accept();
        if (!admission.IsAccepted)
        {
            cipher?.Dispose();
            SendRejected(source, nonce, admission.Reason);
            return;
        }

        uint salt = (uint)RandomNumberGenerator.GetInt32(int.MaxValue) | 1u;
        Peer peer = new(slot, nonce, admission.Payload, new NetConnection(_transport, source, _options.ProtocolId, salt, now, cipher) { Rate = _options.Rate });
        _peers[slot] = peer;
        _byAddress[source] = slot;
        SendAccepted(peer);
        _events.Enqueue(new NetEvent(NetEventKind.Connected, slot, default, string.Empty, payload));
    }

    private void SendAccepted(Peer peer)
    {
        int prefix = NetConstants.PacketPrefixSize;
        int length = prefix + 14 + peer.AcceptPayload.Length;
        if (length > _sendBuffer.Length)
        {
            return;
        }

        Span<byte> packet = _sendBuffer.AsSpan(0, length);
        BinaryPrimitives.WriteUInt64LittleEndian(packet[prefix..], peer.Nonce);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[(prefix + 8)..], peer.Connection.Salt);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[(prefix + 12)..], (ushort)peer.Slot);
        peer.AcceptPayload.CopyTo(packet[(prefix + 14)..]);
        SendOutOfBand(peer.Connection.RemoteAddress, PacketKind.Accepted, packet);
    }

    private void SendRejected(NetAddress destination, ulong nonce, string reason)
    {
        int prefix = NetConstants.PacketPrefixSize;
        BinaryPrimitives.WriteUInt64LittleEndian(_sendBuffer.AsSpan(prefix), nonce);
        int length = prefix + 8 + PacketCodec.WriteString(_sendBuffer.AsSpan(prefix + 8), reason);
        SendOutOfBand(destination, PacketKind.Rejected, _sendBuffer.AsSpan(0, length));
    }

    private void Drop(int slot, string reason, bool notify)
    {
        Peer peer = _peers[slot]!;
        if (notify)
        {
            int prefix = NetConstants.PacketPrefixSize;
            BinaryPrimitives.WriteUInt32LittleEndian(_sendBuffer.AsSpan(prefix), peer.Connection.Salt);
            int length = prefix + 4 + PacketCodec.WriteString(_sendBuffer.AsSpan(prefix + 4), reason);
            for (int i = 0; i < 3; i++)
            {
                SendOutOfBand(peer.Connection.RemoteAddress, PacketKind.Disconnect, _sendBuffer.AsSpan(0, length));
            }
        }

        _peers[slot] = null;
        _byAddress.Remove(peer.Connection.RemoteAddress);
        peer.Connection.Dispose();
        _events.Enqueue(new NetEvent(NetEventKind.Disconnected, slot, default, reason, null));
    }

    private Peer? FindPeer(NetAddress address) => _byAddress.TryGetValue(address, out int slot) ? _peers[slot] : null;

    private ulong Token(NetAddress address, ulong nonce, ulong bucket)
    {
        Span<byte> data = stackalloc byte[19 + 16];
        address.WriteTo(data);
        BinaryPrimitives.WriteUInt64LittleEndian(data[19..], nonce);
        BinaryPrimitives.WriteUInt64LittleEndian(data[27..], bucket);
        return SipHash.Compute(_key0, _key1, data);
    }

    private void SendOutOfBand(NetAddress destination, PacketKind kind, Span<byte> packet)
    {
        PacketCodec.Seal(packet, kind, _options.ProtocolId);
        _transport.Send(destination, packet);
    }

    private sealed record Peer(int Slot, ulong Nonce, byte[] AcceptPayload, NetConnection Connection);
}
