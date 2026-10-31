using System.Diagnostics;
using OpenIGI.Net.Udp;
using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>The real socket, on the loopback interface.</summary>
public sealed class UdpTransportTests
{
    [Fact]
    public void AClientJoinsOverUdpAndExchangesMessages()
    {
        using NetServer server = new(UdpTransport.Open(0), new NetServerOptions { ProtocolId = NetSessionHarness.ProtocolId });
        using NetClient client = new(UdpTransport.Open(0), NetSessionHarness.ProtocolId);
        NetAddress target = NetAddress.IPv4(127, 0, 0, 1, server.LocalAddress.Port);

        Stopwatch clock = Stopwatch.StartNew();
        client.Connect(target, [], clock.Elapsed.TotalSeconds);

        bool serverHeard = false;
        bool clientHeard = false;
        while (clock.Elapsed.TotalSeconds < 5 && !(serverHeard && clientHeard))
        {
            double now = clock.Elapsed.TotalSeconds;
            server.Update(now);
            while (server.TryReceive(out NetEvent e))
            {
                if (e.Kind == NetEventKind.Connected)
                {
                    server.Send(e.Peer, NetChannel.Reliable, "hello client"u8);
                }
                else if (e.Kind == NetEventKind.Message)
                {
                    serverHeard = true;
                }
            }

            client.Update(now);
            while (client.TryReceive(out NetEvent e))
            {
                if (e.Kind == NetEventKind.Connected)
                {
                    client.Send(NetChannel.Reliable, "hello server"u8);
                }
                else if (e.Kind == NetEventKind.Message)
                {
                    clientHeard = true;
                }
            }

            client.Flush(now);
            server.Flush(now);
            Thread.Sleep(1);
        }

        Assert.True(serverHeard && clientHeard);
    }

    [Theory]
    [InlineData("127.0.0.1:26001", "127.0.0.1:26001")]
    [InlineData("10.0.0.5", "10.0.0.5:26001")]
    public void ParseReadsHostAndPort(string text, string expected) =>
        Assert.Equal(expected, UdpTransport.Parse(text)!.Value.ToString());
}
