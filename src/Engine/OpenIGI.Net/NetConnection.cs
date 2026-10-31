using System.Buffers.Binary;

namespace OpenIGI.Net;

/// <summary>A message that arrived on a connection.</summary>
/// <param name="Channel">How it was sent.</param>
/// <param name="Data">Its bytes, the receiver's to keep.</param>
public readonly record struct NetMessage(NetChannel Channel, byte[] Data);

/// <summary>One end of an established connection: packet sequencing, acknowledgement, the three channels and the line's stats.</summary>
/// <remarks>
/// <para>
/// Every payload packet carries its own sequence, the newest sequence received from the other end, and a bitfield of the
/// 32 before that. Acknowledgement is therefore redundant: one packet getting through acknowledges everything the last 33
/// did, and nothing is ever acknowledged on its own. A reliable message is resent, in a later packet, until a packet
/// that carried it is acknowledged; the receiver delivers reliable messages in order and drops the copies.
/// </para>
/// <para>
/// The server and client own their connections and call <see cref="Flush"/> once per network tick, which writes as many
/// packets as the queued messages need, or a bare one to carry acknowledgements and keep the line alive.
/// </para>
/// </remarks>
public sealed class NetConnection
{
    private const double KeepAliveInterval = 0.1;
    private const double MinimumResendDelay = 0.1;
    private const int MaxPacketsPerFlush = 64;

    private readonly INetTransport _transport;
    private readonly ulong _protocolId;
    private readonly byte[] _packet = new byte[NetConstants.MaxPacketSize];

    private readonly SequenceBuffer<SentPacket> _sent = new(NetConstants.WindowSize);
    private readonly Queue<(ushort Sequence, double Time)> _lossWindow = new();
    private readonly SequenceBuffer<bool> _received = new(NetConstants.WindowSize);

    private readonly SequenceBuffer<OutgoingReliable> _reliableOut = new(NetConstants.WindowSize);
    private readonly Queue<OutgoingReliable> _reliableBacklog = new();
    private readonly SequenceBuffer<IncomingReliable> _reliableIn = new(NetConstants.WindowSize);
    private readonly List<byte> _assembly = [];
    private readonly List<(NetChannel Channel, byte[] Data)> _unreliableOut = [];
    private readonly List<ushort> _packetReliableIds = [];

    private ushort _sequence = 1;
    private ushort _latestReceived;
    private bool _anyReceived;
    private bool _ackOwed;
    private ushort _reliableNextId;
    private ushort _reliableOldestUnacked;
    private ushort _reliableExpected;
    private ushort _latestSequenced;
    private bool _anySequenced;
    private double _lastSendTime = double.NegativeInfinity;

    internal NetConnection(INetTransport transport, NetAddress remote, ulong protocolId, uint salt, double now)
    {
        _transport = transport;
        _protocolId = protocolId;
        RemoteAddress = remote;
        Salt = salt;
        LastReceiveTime = now;
    }

    /// <summary>Gets the other end's address.</summary>
    public NetAddress RemoteAddress { get; }

    /// <summary>Gets how the line is doing.</summary>
    public NetStatistics Statistics { get; } = new();

    /// <summary>Gets when the last valid packet arrived, in seconds.</summary>
    public double LastReceiveTime { get; private set; }

    /// <summary>Gets a value indicating whether the other end broke the protocol, so the connection must be dropped.</summary>
    public bool IsFaulted { get; private set; }

    /// <summary>Gets the reliable messages queued or unacknowledged.</summary>
    public int PendingReliableCount => SequenceMath.Distance(_reliableNextId, _reliableOldestUnacked) + _reliableBacklog.Count;

    internal uint Salt { get; }

    /// <summary>Queues a message for the next <see cref="Flush"/>.</summary>
    /// <param name="channel">How to deliver it.</param>
    /// <param name="data">The bytes; copied.</param>
    /// <exception cref="ArgumentException">The message is larger than its channel carries.</exception>
    public void Send(NetChannel channel, ReadOnlySpan<byte> data)
    {
        if (channel == NetChannel.Reliable)
        {
            if (data.Length > NetConstants.MaxReliableMessageSize)
            {
                throw new ArgumentException($"A reliable message is at most {NetConstants.MaxReliableMessageSize} bytes.", nameof(data));
            }

            int offset = 0;
            do
            {
                int size = Math.Min(NetConstants.MaxFragmentSize, data.Length - offset);
                bool more = offset + size < data.Length;
                _reliableBacklog.Enqueue(new OutgoingReliable(data.Slice(offset, size).ToArray(), more));
                offset += size;
            }
            while (offset < data.Length);

            PromoteBacklog();
            return;
        }

        if (data.Length > NetConstants.MaxUnreliableMessageSize)
        {
            throw new ArgumentException($"An unreliable message is at most {NetConstants.MaxUnreliableMessageSize} bytes.", nameof(data));
        }

        _unreliableOut.Add((channel, data.ToArray()));
    }

    /// <summary>Writes and sends the packets the queued messages need, or one to carry acknowledgements when it is time.</summary>
    /// <param name="now">The current time in seconds.</param>
    public void Flush(double now)
    {
        PromoteBacklog();
        CountLosses(now);

        bool keepAliveDue = now - _lastSendTime >= KeepAliveInterval;
        double resendDelay = Math.Max(MinimumResendDelay, (Statistics.RoundTripTime * 1.25) + (Statistics.RoundTripJitter * 2));
        int unreliableCursor = 0;

        for (int packets = 0; packets < MaxPacketsPerFlush; packets++)
        {
            int offset = WritePayloadHeader();
            int headerEnd = offset;
            _packetReliableIds.Clear();

            for (ushort id = _reliableOldestUnacked; id != _reliableNextId; id++)
            {
                if (!_reliableOut.TryGet(id, out OutgoingReliable? message) || message.Acked || now - message.LastSent < resendDelay)
                {
                    continue;
                }

                int size = 1 + 2 + VarIntSize(message.Data.Length) + message.Data.Length;
                if (offset + size > _packet.Length)
                {
                    continue;
                }

                _packet[offset] = (byte)((byte)NetChannel.Reliable | (message.More ? 4 : 0));
                BinaryPrimitives.WriteUInt16LittleEndian(_packet.AsSpan(offset + 1), id);
                offset = WriteBody(offset + 3, message.Data);
                if (message.LastSent > double.NegativeInfinity)
                {
                    Statistics.CountResend();
                }

                message.LastSent = now;
                _packetReliableIds.Add(id);
            }

            while (unreliableCursor < _unreliableOut.Count)
            {
                (NetChannel channel, byte[] data) = _unreliableOut[unreliableCursor];
                int size = 1 + VarIntSize(data.Length) + data.Length;
                if (offset + size > _packet.Length)
                {
                    break;
                }

                _packet[offset] = (byte)channel;
                offset = WriteBody(offset + 1, data);
                unreliableCursor++;
            }

            if (offset == headerEnd && !_ackOwed && !keepAliveDue)
            {
                break;
            }

            SendPacket(offset, now);
            keepAliveDue = false;

            if (offset == headerEnd && unreliableCursor >= _unreliableOut.Count)
            {
                break;
            }
        }

        _unreliableOut.Clear();
    }

    /// <summary>Reads a payload packet whose prefix and salt the owner has already checked.</summary>
    /// <param name="packet">The whole packet.</param>
    /// <param name="now">The current time in seconds.</param>
    /// <param name="output">Where the messages it completes go.</param>
    internal void ReceivePayload(ReadOnlySpan<byte> packet, double now, List<NetMessage> output)
    {
        int start = NetConstants.PacketPrefixSize + 4;
        if (packet.Length < NetConstants.PacketPrefixSize + NetConstants.PayloadHeaderSize)
        {
            return;
        }

        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(packet[start..]);
        ushort ack = BinaryPrimitives.ReadUInt16LittleEndian(packet[(start + 2)..]);
        uint ackBits = BinaryPrimitives.ReadUInt32LittleEndian(packet[(start + 4)..]);

        if (_received.Contains(sequence)
            || (_anyReceived && SequenceMath.Distance(sequence, _latestReceived) <= -NetConstants.WindowSize / 2))
        {
            return;
        }

        _received.Insert(sequence, true);
        if (!_anyReceived || SequenceMath.IsNewer(sequence, _latestReceived))
        {
            _latestReceived = sequence;
        }

        _anyReceived = true;
        _ackOwed = true;
        LastReceiveTime = now;
        Statistics.CountReceived(packet.Length, now);
        ReceiveAcks(ack, ackBits, now);

        bool sequencedFresh = !_anySequenced || SequenceMath.IsNewer(sequence, _latestSequenced);
        bool anySequenced = false;
        int offset = NetConstants.PacketPrefixSize + NetConstants.PayloadHeaderSize;
        while (offset < packet.Length)
        {
            byte header = packet[offset++];
            var channel = (NetChannel)(header & 3);
            bool more = (header & 4) != 0;
            ushort id = 0;
            if (channel == NetChannel.Reliable)
            {
                if (offset + 2 > packet.Length)
                {
                    IsFaulted = true;
                    return;
                }

                id = BinaryPrimitives.ReadUInt16LittleEndian(packet[offset..]);
                offset += 2;
            }

            if (!TryReadVarInt(packet, ref offset, out int length) || length < 0 || offset + length > packet.Length || channel > NetChannel.Reliable)
            {
                IsFaulted = true;
                return;
            }

            ReadOnlySpan<byte> data = packet.Slice(offset, length);
            offset += length;
            switch (channel)
            {
                case NetChannel.Unreliable:
                    output.Add(new NetMessage(channel, data.ToArray()));
                    break;
                case NetChannel.Sequenced when sequencedFresh:
                    output.Add(new NetMessage(channel, data.ToArray()));
                    anySequenced = true;
                    break;
                case NetChannel.Reliable:
                    ReceiveReliable(id, more, data, output);
                    break;
            }
        }

        if (anySequenced)
        {
            _latestSequenced = sequence;
            _anySequenced = true;
        }
    }

    private void ReceiveAcks(ushort ack, uint ackBits, double now)
    {
        for (int i = 0; i <= 32; i++)
        {
            if (i > 0 && ((ackBits >> (i - 1)) & 1) == 0)
            {
                continue;
            }

            ushort sequence = (ushort)(ack - i);
            if (!_sent.TryGet(sequence, out SentPacket? packet) || packet.Acked)
            {
                continue;
            }

            packet.Acked = true;
            if (i == 0)
            {
                Statistics.AddRoundTripSample(now - packet.Time);
            }

            foreach (ushort id in packet.ReliableIds)
            {
                if (_reliableOut.TryGet(id, out OutgoingReliable? message))
                {
                    message.Acked = true;
                }
            }
        }

        while (_reliableOldestUnacked != _reliableNextId
               && (!_reliableOut.TryGet(_reliableOldestUnacked, out OutgoingReliable? oldest) || oldest.Acked))
        {
            _reliableOut.Remove(_reliableOldestUnacked);
            _reliableOldestUnacked++;
        }

        PromoteBacklog();
    }

    private void ReceiveReliable(ushort id, bool more, ReadOnlySpan<byte> data, List<NetMessage> output)
    {
        int distance = SequenceMath.Distance(id, _reliableExpected);
        if (distance < 0 || distance >= NetConstants.WindowSize)
        {
            return;
        }

        if (!_reliableIn.Contains(id))
        {
            _reliableIn.Insert(id, new IncomingReliable(data.ToArray(), more));
        }

        while (_reliableIn.TryGet(_reliableExpected, out IncomingReliable next))
        {
            _reliableIn.Remove(_reliableExpected);
            _reliableExpected++;
            _assembly.AddRange(next.Data);
            if (_assembly.Count > NetConstants.MaxReliableMessageSize)
            {
                IsFaulted = true;
                _assembly.Clear();
                return;
            }

            if (!next.More)
            {
                output.Add(new NetMessage(NetChannel.Reliable, [.. _assembly]));
                _assembly.Clear();
            }
        }
    }

    private void PromoteBacklog()
    {
        while (_reliableBacklog.Count > 0
               && SequenceMath.Distance(_reliableNextId, _reliableOldestUnacked) < NetConstants.MaxReliableInFlight)
        {
            _reliableOut.Insert(_reliableNextId, _reliableBacklog.Dequeue());
            _reliableNextId++;
        }
    }

    private void CountLosses(double now)
    {
        double horizon = Math.Max(1.0, (Statistics.RoundTripTime * 2) + 0.25);
        while (_lossWindow.TryPeek(out (ushort Sequence, double Time) oldest) && now - oldest.Time > horizon)
        {
            _lossWindow.Dequeue();
            bool acked = _sent.TryGet(oldest.Sequence, out SentPacket? packet) && packet.Acked;
            Statistics.AddLossSample(!acked);
        }
    }

    private int WritePayloadHeader()
    {
        Span<byte> span = _packet.AsSpan(NetConstants.PacketPrefixSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span, Salt);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], _sequence);
        uint bits = 0;
        if (_anyReceived)
        {
            for (int i = 1; i <= 32; i++)
            {
                if (_received.Contains((ushort)(_latestReceived - i)))
                {
                    bits |= 1u << (i - 1);
                }
            }
        }

        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], _anyReceived ? _latestReceived : (ushort)0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], bits);
        return NetConstants.PacketPrefixSize + NetConstants.PayloadHeaderSize;
    }

    private int WriteBody(int offset, byte[] data)
    {
        offset = WriteVarInt(_packet, offset, data.Length);
        data.CopyTo(_packet, offset);
        return offset + data.Length;
    }

    private void SendPacket(int length, double now)
    {
        PacketCodec.Seal(_packet.AsSpan(0, length), PacketKind.Payload, _protocolId);
        _transport.Send(RemoteAddress, _packet.AsSpan(0, length));
        _sent.Insert(_sequence, new SentPacket(now, [.. _packetReliableIds]));
        _lossWindow.Enqueue((_sequence, now));
        Statistics.CountSent(length, now);
        _sequence++;
        _ackOwed = false;
        _lastSendTime = now;
    }

    private static int VarIntSize(int value)
    {
        int size = 1;
        for (uint v = (uint)value; v >= 0x80; v >>= 7)
        {
            size++;
        }

        return size;
    }

    private static int WriteVarInt(byte[] buffer, int offset, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            buffer[offset++] = (byte)(v | 0x80);
            v >>= 7;
        }

        buffer[offset++] = (byte)v;
        return offset;
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> buffer, ref int offset, out int value)
    {
        uint result = 0;
        for (int shift = 0; shift < 28 && offset < buffer.Length; shift += 7)
        {
            byte b = buffer[offset++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                value = (int)result;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private sealed class SentPacket(double time, ushort[] reliableIds)
    {
        public double Time { get; } = time;

        public ushort[] ReliableIds { get; } = reliableIds;

        public bool Acked { get; set; }
    }

    private sealed class OutgoingReliable(byte[] data, bool more)
    {
        public byte[] Data { get; } = data;

        public bool More { get; } = more;

        public double LastSent { get; set; } = double.NegativeInfinity;

        public bool Acked { get; set; }
    }

    private readonly record struct IncomingReliable(byte[] Data, bool More);
}
