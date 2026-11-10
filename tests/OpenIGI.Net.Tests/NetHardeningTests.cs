using System.Text;
using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>The connection layer under misuse: versions, sealing, tampering, restarts and the send rate.</summary>
public sealed class NetHardeningTests
{
    [Fact]
    public void A_client_of_another_version_is_turned_down_in_words_rather_than_left_to_time_out()
    {
        using NetSessionHarness session = new(new NetServerOptions { ProtocolVersion = 16 });
        NetSessionHarness.ClientEnd client = session.AddClient(protocolVersion: 15);
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);

        Assert.True(session.RunUntil(() => client.Events.Any(e => e.Kind == NetEventKind.Rejected), 2));
        NetEvent rejected = Assert.Single(client.Events, e => e.Kind == NetEventKind.Rejected);
        Assert.Contains("version mismatch", rejected.Reason, StringComparison.Ordinal);
        Assert.Contains("16", rejected.Reason, StringComparison.Ordinal);
        Assert.Equal(0, session.Server.ClientCount);
    }

    [Fact]
    public void A_connection_over_a_plain_line_is_sealed_at_both_ends_and_carries_messages()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);

        Assert.True(client.Client.Connection!.IsSealed);
        Assert.True(session.Server.GetConnection(client.Client.Slot)!.IsSealed);
        session.Server.Send(client.Client.Slot, NetChannel.Reliable, "hello"u8);
        client.Client.Send(NetChannel.Reliable, "back"u8);
        session.Run(0.2);

        Assert.Equal("hello", Encoding.UTF8.GetString(Assert.Single(client.Messages(NetChannel.Reliable))));
        Assert.Contains(session.ServerEvents, e => e.Kind == NetEventKind.Message && Encoding.UTF8.GetString(e.Message.Data) == "back");
    }

    [Fact]
    public void A_client_told_not_to_seal_still_plays_in_the_clear()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = session.AddClient();
        client.Client.Seal = false;
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);
        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected));

        Assert.False(client.Client.Connection!.IsSealed);
        Assert.False(session.Server.GetConnection(client.Client.Slot)!.IsSealed);
        session.Server.Send(client.Client.Slot, NetChannel.Reliable, "clear"u8);
        session.Run(0.2);
        Assert.Single(client.Messages(NetChannel.Reliable));
    }

    [Fact]
    public void The_password_never_crosses_the_line_where_it_can_be_read()
    {
        using NetSessionHarness session = new();
        TamperingTransport? tap = null;
        NetSessionHarness.ClientEnd client = session.AddClient(wrap: line => tap = new TamperingTransport(line));
        byte[] secret = "hunter2-the-password"u8.ToArray();
        client.Client.Connect(session.Server.LocalAddress, secret, session.Now);
        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected));

        Assert.NotEmpty(tap!.Sent);
        Assert.DoesNotContain(tap.Sent, packet => packet.AsSpan().IndexOf(secret) >= 0);
        NetEvent joined = Assert.Single(session.ServerEvents, e => e.Kind == NetEventKind.Connected);
        Assert.Equal(secret, joined.Payload);
    }

    [Fact]
    public void A_packet_changed_on_the_way_is_dropped_though_its_checksum_was_made_good()
    {
        using NetSessionHarness session = new();
        TamperingTransport? tap = null;
        NetSessionHarness.ClientEnd client = session.AddClient(wrap: line => tap = new TamperingTransport(line));
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);
        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected));
        session.Run(0.2);
        session.ServerEvents.Clear();

        // Every payload packet from now has a byte of its body flipped and its checksum written again, as one on the path
        // who knows the protocol would.
        tap!.Tamper = packet =>
        {
            if (packet[0] == (byte)PacketKind.Payload && packet.Length > NetConstants.PacketPrefixSize + NetConstants.PayloadHeaderSize + 1)
            {
                packet[NetConstants.PacketPrefixSize + NetConstants.PayloadHeaderSize] ^= 0x5A;
                PacketCodec.Seal(packet, PacketKind.Payload, NetSessionHarness.ProtocolId);
            }
        };

        client.Client.Send(NetChannel.Unreliable, "forged"u8);
        session.Run(0.3);
        Assert.DoesNotContain(session.ServerEvents, e => e.Kind == NetEventKind.Message);
        Assert.True(session.Server.GetConnection(client.Client.Slot)!.Statistics.Unopened > 0);

        // Untouched again, the line carries on.
        tap.Tamper = null;
        client.Client.Send(NetChannel.Reliable, "genuine"u8);
        session.Run(0.5);
        Assert.Contains(session.ServerEvents, e => e.Kind == NetEventKind.Message && Encoding.UTF8.GetString(e.Message.Data) == "genuine");
    }

    [Fact]
    public void A_game_that_restarts_on_the_same_address_is_let_straight_back_in()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        int firstSlot = client.Client.Slot;

        // A crash: the old connection never says goodbye (the line eats it), and the game joins afresh from the same port.
        client.Line.Send.Loss = 1;
        client.Client.Disconnect("crashed", session.Now);
        client.Line.Send.Loss = 0;
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);

        double started = session.Now;
        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected, 3));
        Assert.True(session.Now - started < 2, "rejoined only after the old connection timed out");
        Assert.Equal(1, session.Server.ClientCount);
        Assert.Contains(session.ServerEvents, e => e.Kind == NetEventKind.Disconnected && e.Peer == firstSlot && e.Reason == "reconnected");
        Assert.Equal(2, session.ServerEvents.Count(e => e.Kind == NetEventKind.Connected));
    }

    [Fact]
    public void A_connection_sends_no_faster_than_its_rate_and_says_what_it_dropped()
    {
        const int Rate = 24 * 1024;
        using NetSessionHarness session = new(new NetServerOptions { Rate = Rate });
        NetSessionHarness.ClientEnd client = Join(session);
        NetConnection line = session.Server.GetConnection(client.Client.Slot)!;
        long before = line.Statistics.BytesSent;

        byte[] snapshot = new byte[900];
        const double Seconds = 3;
        for (double t = 0; t < Seconds; t += NetSessionHarness.Step)
        {
            for (int i = 0; i < 4; i++)
            {
                session.Server.Send(client.Client.Slot, NetChannel.Unreliable, snapshot);
            }

            session.Tick();
        }

        double perSecond = (line.Statistics.BytesSent - before) / Seconds;
        Assert.InRange(perSecond, Rate * 0.7, Rate * 1.15);
        Assert.True(line.Statistics.Choked > 0);
        Assert.True(line.IsChokedAt(session.Now) || line.Statistics.Choked > 0);
    }

    [Fact]
    public void A_large_reliable_message_on_a_clean_line_is_never_sent_twice()
    {
        using NetSessionHarness session = new(new NetServerOptions { Rate = 64 * 1024 * 1024 });
        NetSessionHarness.ClientEnd client = Join(session);
        byte[] big = new byte[200 * 1024];
        new Random(5).NextBytes(big);

        session.Server.Send(client.Client.Slot, NetChannel.Reliable, big);
        Assert.True(session.RunUntil(() => client.Messages(NetChannel.Reliable).Any(), 10));

        Assert.Equal(big, client.Messages(NetChannel.Reliable).Single());
        Assert.Equal(0, session.Server.GetConnection(client.Client.Slot)!.Statistics.ReliableResends);
    }

    [Theory]
    [InlineData(0x0000UL, (ushort)1, 0x0001UL)]
    [InlineData(0x1FFF0UL, (ushort)0x0005, 0x20005UL)]
    [InlineData(0x20005UL, (ushort)0xFFF0, 0x1FFF0UL)]
    [InlineData(0x20005UL, (ushort)0x0003, 0x20003UL)]
    public void A_sixteen_bit_sequence_is_read_as_the_full_one_nearest_the_newest(ulong newest, ushort sequence, ulong expected) =>
        Assert.Equal(expected, PacketCipher.Extend(newest, sequence));

    [Fact]
    public void A_budget_spends_what_it_holds_and_refills_at_its_rate()
    {
        TokenBucket bucket = new(perSecond: 2, burst: 3);
        Assert.True(bucket.TrySpend(0));
        Assert.True(bucket.TrySpend(0));
        Assert.True(bucket.TrySpend(0));
        Assert.False(bucket.TrySpend(0));
        Assert.False(bucket.TrySpend(0.25));
        Assert.True(bucket.TrySpend(0.5));
        Assert.True(bucket.TrySpend(10, 3));
        Assert.Equal(2, bucket.Refused);
    }

    [Fact]
    public void Reliable_messages_all_arrive_in_order_over_a_lossy_line_at_the_lowest_rate()
    {
        // At a rate that leaves less than a packet a flush, the flushes between are bare acknowledgements. One that stood
        // for the reliable messages of the packet before it had them counted delivered when that packet was lost.
        using NetSessionHarness session = new(new NetServerOptions { Rate = 4 * NetConstants.MaxPacketSize });
        NetSessionHarness.ClientEnd client = Join(session);
        session.SetLine(0.02, 0.005, 0.2, 0);
        const int Count = 40;
        for (int i = 0; i < Count; i++)
        {
            byte[] message = new byte[900];
            message[0] = (byte)i;
            session.Server.Send(client.Client.Slot, NetChannel.Reliable, message);
        }

        Assert.True(session.RunUntil(() => client.Messages(NetChannel.Reliable).Count() == Count, 120));
        Assert.Equal(Enumerable.Range(0, Count).Select(static i => (byte)i), client.Messages(NetChannel.Reliable).Select(static m => m[0]));
    }

    [Fact]
    public void A_goodbye_not_sealed_by_the_other_end_does_not_end_a_sealed_connection()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        NetConnection line = client.Client.Connection!;
        Assert.True(line.IsSealed);

        // What anyone who saw one packet of the connection could write: its salt, and a reason in the clear.
        Span<byte> forged = stackalloc byte[NetConstants.PacketPrefixSize + 4 + 8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(forged[NetConstants.PacketPrefixSize..], line.Salt);
        Assert.False(line.TryReadDisconnect(forged, out _));

        Span<byte> real = stackalloc byte[NetConstants.MaxPacketSize];
        int length = session.Server.GetConnection(client.Client.Slot)!.WriteDisconnect(real, "bye");
        Assert.True(line.TryReadDisconnect(real[..length], out string reason));
        Assert.Equal("bye", reason);
    }

    private static NetSessionHarness.ClientEnd Join(NetSessionHarness session)
    {
        NetSessionHarness.ClientEnd client = session.AddClient();
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);
        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected));
        return client;
    }

    /// <summary>A line someone on the path can read and change.</summary>
    private sealed class TamperingTransport(INetTransport inner) : INetTransport
    {
        public List<byte[]> Sent { get; } = [];

        public Action<byte[]>? Tamper { get; set; }

        public NetAddress LocalAddress => inner.LocalAddress;

        public int MaxDatagramSize => inner.MaxDatagramSize;

        public void Send(NetAddress destination, ReadOnlySpan<byte> datagram)
        {
            byte[] copy = datagram.ToArray();
            Sent.Add(copy);
            Tamper?.Invoke(copy);
            inner.Send(destination, copy);
        }

        public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length) => inner.TryReceive(buffer, out source, out length);

        public void Dispose() => inner.Dispose();
    }
}
