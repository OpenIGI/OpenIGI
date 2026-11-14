using Xunit;

namespace OpenIGI.Net.Tests;

public sealed class MultiTransportTests
{
    [Fact]
    public void Each_datagram_goes_back_through_the_part_its_address_belongs_to_and_both_parts_are_heard()
    {
        Part udp = new(NetAddress.IPv4(10, 0, 0, 1, 26001));
        Part room = new(NetAddress.Peer(0));
        using MultiTransport both = new(udp, room);
        NetAddress desktop = NetAddress.IPv4(10, 0, 0, 2, 50000);
        NetAddress browser = NetAddress.Peer(3);

        both.Send(desktop, [1]);
        both.Send(browser, [2]);
        Assert.Equal([desktop], udp.Sent);
        Assert.Equal([browser], room.Sent);
        Assert.Equal(udp.LocalAddress, both.LocalAddress);

        udp.Inbox.Enqueue((desktop, [7]));
        room.Inbox.Enqueue((browser, [8]));
        byte[] buffer = new byte[1200];
        HashSet<NetAddress> heard = [];
        while (both.TryReceive(buffer, out NetAddress from, out _))
        {
            heard.Add(from);
        }

        Assert.Equal(2, heard.Count);
        Assert.Contains(desktop, heard);
        Assert.Contains(browser, heard);
    }

    private sealed class Part(NetAddress local) : INetTransport
    {
        public Queue<(NetAddress From, byte[] Data)> Inbox { get; } = new();

        public List<NetAddress> Sent { get; } = [];

        public NetAddress LocalAddress => local;

        public int MaxDatagramSize => 1200;

        public void Send(NetAddress destination, ReadOnlySpan<byte> datagram) => Sent.Add(destination);

        public bool TryReceive(Span<byte> buffer, out NetAddress source, out int length)
        {
            if (Inbox.TryDequeue(out (NetAddress From, byte[] Data) next))
            {
                next.Data.CopyTo(buffer);
                source = next.From;
                length = next.Data.Length;
                return true;
            }

            source = default;
            length = 0;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
