using System.Buffers.Binary;
using System.Security.Cryptography;

namespace OpenIGI.Net;

/// <summary>A server's answer to a query: what it says about itself and how long it took to say it.</summary>
/// <param name="Address">The server.</param>
/// <param name="Info">What its <see cref="NetServerOptions.Info"/> returned.</param>
/// <param name="Ping">The round trip in seconds.</param>
public readonly record struct NetServerInfo(NetAddress Address, byte[] Info, double Ping);

/// <summary>Asks servers about themselves without joining: the server browser's pings, and LAN discovery by broadcast.</summary>
/// <remarks>
/// Retail used GameSpy for the list and its own query packets for pings; this is the query half, over any transport. A
/// request is padded so a server's answer is never much larger than what provoked it.
/// </remarks>
public sealed class NetQueryClient : IDisposable
{
    private const int RequestSize = 64;

    private readonly INetTransport _transport;
    private readonly ulong _protocolId;
    private readonly Dictionary<ulong, double> _sent = [];
    private readonly Queue<NetServerInfo> _answers = new();
    private readonly byte[] _buffer;

    /// <summary>Initializes a new instance of the <see cref="NetQueryClient"/> class.</summary>
    /// <param name="transport">What it sends on; the query client disposes it.</param>
    /// <param name="protocolId">The id of the protocol; servers of another protocol stay silent.</param>
    public NetQueryClient(INetTransport transport, ulong protocolId)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _protocolId = protocolId;
        _buffer = new byte[Math.Max(transport.MaxDatagramSize, NetConstants.MaxPacketSize)];
    }

    /// <summary>Asks one server about itself.</summary>
    /// <param name="server">The server.</param>
    /// <param name="now">The current time in seconds.</param>
    public void Query(NetAddress server, double now) => _transport.Send(server, Request(now));

    /// <summary>Asks every server on the local network, when the transport can broadcast.</summary>
    /// <param name="port">The port servers listen on.</param>
    /// <param name="now">The current time in seconds.</param>
    /// <returns><see langword="false"/> when the transport cannot broadcast.</returns>
    public bool QueryLocalNetwork(int port, double now)
    {
        if (_transport is not IBroadcastTransport broadcast)
        {
            return false;
        }

        broadcast.Broadcast(port, Request(now));
        return true;
    }

    /// <summary>Reads the answers that have arrived.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Update(double now)
    {
        int prefix = NetConstants.PacketPrefixSize;
        while (_transport.TryReceive(_buffer, out NetAddress source, out int length))
        {
            ReadOnlySpan<byte> packet = _buffer.AsSpan(0, length);
            if (!PacketCodec.TryOpen(packet, _protocolId, out PacketKind kind) || kind != PacketKind.InfoResponse || length < prefix + 8)
            {
                continue;
            }

            ulong nonce = BinaryPrimitives.ReadUInt64LittleEndian(packet[prefix..]);
            if (_sent.TryGetValue(nonce, out double sentAt))
            {
                _answers.Enqueue(new NetServerInfo(source, packet[(prefix + 8)..].ToArray(), now - sentAt));
            }
        }

        // A broadcast is answered by every server, so a nonce stays live a few seconds rather than until its first answer.
        foreach (KeyValuePair<ulong, double> entry in _sent)
        {
            if (now - entry.Value > 5)
            {
                _sent.Remove(entry.Key);
            }
        }
    }

    /// <summary>Takes the next answer.</summary>
    /// <param name="info">The answer.</param>
    /// <returns><see langword="true"/> when there was one.</returns>
    public bool TryReceive(out NetServerInfo info) => _answers.TryDequeue(out info);

    /// <inheritdoc/>
    public void Dispose() => _transport.Dispose();

    private byte[] Request(double now)
    {
        byte[] request = new byte[RequestSize];
        ulong nonce = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8));
        BinaryPrimitives.WriteUInt64LittleEndian(request.AsSpan(NetConstants.PacketPrefixSize), nonce);
        PacketCodec.Seal(request, PacketKind.InfoRequest, _protocolId);
        _sent[nonce] = now;
        return request;
    }
}
