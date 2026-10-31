using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>Whole sessions over loopback: the handshake, the three channels on a bad line, and the ways a session ends.</summary>
public sealed class NetSessionTests
{
    [Fact]
    public void AClientJoinsAndBothEndsHearOfIt()
    {
        using NetSessionHarness session = new(new NetServerOptions { Admit = r => NetAdmission.Accept("welcome"u8.ToArray()) });
        NetSessionHarness.ClientEnd client = session.AddClient();
        client.Client.Connect(session.Server.LocalAddress, "Jones"u8, session.Now);

        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected));
        session.Run(0.1);

        NetEvent joined = Assert.Single(session.ServerEvents, e => e.Kind == NetEventKind.Connected);
        Assert.Equal("Jones", Encoding.UTF8.GetString(joined.Payload!));
        NetEvent accepted = Assert.Single(client.Events, e => e.Kind == NetEventKind.Connected);
        Assert.Equal("welcome", Encoding.UTF8.GetString(accepted.Payload!));
        Assert.Equal(joined.Peer, client.Client.Slot);
    }

    [Fact]
    public void TheHandshakeSurvivesABadLine()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = session.AddClient();
        session.SetLine(latency: 0.2, jitter: 0.1, loss: 0.4, duplicate: 0.2);
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);

        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected, 20));
        session.Run(1);
        Assert.Single(session.ServerEvents, e => e.Kind == NetEventKind.Connected);
    }

    [Fact]
    public void ReliableMessagesArriveOnceAndInOrderThroughLossReorderingAndDuplication()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        session.SetLine(latency: 0.15, jitter: 0.2, loss: 0.2, duplicate: 0.1);

        const int Count = 2000;
        for (int i = 0; i < Count; i++)
        {
            byte[] message = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(message, i);
            session.Server.Send(client.Client.Slot, NetChannel.Reliable, message);
            if (i % 20 == 0)
            {
                session.Tick();
            }
        }

        Assert.True(session.RunUntil(() => client.Messages(NetChannel.Reliable).Count() >= Count, 60));
        session.Run(2);

        int[] received = [.. client.Messages(NetChannel.Reliable).Select(m => BinaryPrimitives.ReadInt32LittleEndian(m))];
        Assert.Equal(Enumerable.Range(0, Count), received);
    }

    [Fact]
    public void AReliableMessageLargerThanAPacketIsCutAndPutBackTogether()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        session.SetLine(latency: 0.1, jitter: 0.05, loss: 0.1, duplicate: 0.05);

        byte[] large = new byte[100_000];
        new Random(7).NextBytes(large);
        client.Client.Send(NetChannel.Reliable, large);
        client.Client.Send(NetChannel.Reliable, "after"u8);

        Assert.True(session.RunUntil(() => session.ServerEvents.Count(e => e.Kind == NetEventKind.Message) >= 2, 30));
        byte[][] messages = [.. session.ServerEvents.Where(e => e.Kind == NetEventKind.Message).Select(e => e.Message.Data)];
        Assert.Equal(large, messages[0]);
        Assert.Equal("after"u8.ToArray(), messages[1]);
    }

    [Theory]
    [InlineData(0.15, 0.1, 30)]
    [InlineData(0.01, 0.05, 250)]
    public void SequencedMessagesNeverGoBackwards(double jitter, double loss, int atLeast)
    {
        // Heavy jitter reorders most packets, and a sequenced channel drops whatever arrives behind a newer one; a
        // realistic line loses little more than its loss.
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        session.SetLine(latency: 0.05, jitter: jitter, loss: loss, duplicate: 0.2);

        for (int tick = 0; tick < 300; tick++)
        {
            byte[] snapshot = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(snapshot, tick);
            session.Server.Send(client.Client.Slot, NetChannel.Sequenced, snapshot);
            session.Tick();
        }

        session.Run(1);
        int[] ticks = [.. client.Messages(NetChannel.Sequenced).Select(m => BinaryPrimitives.ReadInt32LittleEndian(m))];
        Assert.True(ticks.Length >= atLeast, $"only {ticks.Length} of 300 got through");
        for (int i = 1; i < ticks.Length; i++)
        {
            Assert.True(ticks[i] > ticks[i - 1], $"tick {ticks[i]} came after {ticks[i - 1]}");
        }
    }

    [Fact]
    public void TheLineIsMeasured()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        session.SetLine(latency: 0.1, jitter: 0, loss: 0, duplicate: 0);
        session.Run(5);

        NetStatistics stats = client.Client.Connection!.Statistics;
        // 100 ms each way, plus up to a tick of waiting at each end for the next flush.
        Assert.InRange(stats.RoundTripTime, 0.19, 0.25);
        Assert.True(stats.BytesSentPerSecond > 0);
        Assert.Equal(0, stats.PacketLoss, 2);
    }

    [Fact]
    public void AdmissionCanTurnAClientDownWithAReason()
    {
        using NetSessionHarness session = new(new NetServerOptions { Admit = r => NetAdmission.Reject("wrong password") });
        NetSessionHarness.ClientEnd client = session.AddClient();
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);

        Assert.True(session.RunUntil(() => client.Events.Any(e => e.Kind == NetEventKind.Rejected)));
        Assert.Equal("wrong password", client.Events.Single(e => e.Kind == NetEventKind.Rejected).Reason);
        Assert.Equal(0, session.Server.ClientCount);
    }

    [Fact]
    public void AFullServerTurnsTheNextClientDown()
    {
        using NetSessionHarness session = new(new NetServerOptions { MaxClients = 1 });
        Join(session);
        NetSessionHarness.ClientEnd second = session.AddClient(seed: 3);
        second.Client.Connect(session.Server.LocalAddress, [], session.Now);

        Assert.True(session.RunUntil(() => second.Events.Any(e => e.Kind == NetEventKind.Rejected)));
        Assert.Equal("server is full", second.Events.Single(e => e.Kind == NetEventKind.Rejected).Reason);
    }

    [Fact]
    public void AnotherProtocolIsNeverHeard()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = session.AddClient(protocolId: 0x1234);
        client.Client.Timeout = 2;
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);

        Assert.True(session.RunUntil(() => client.Events.Any(e => e.Kind == NetEventKind.Rejected)));
        Assert.Equal("no response from server", client.Events.Single(e => e.Kind == NetEventKind.Rejected).Reason);
        Assert.Empty(session.ServerEvents);
    }

    [Fact]
    public void LeavingTellsTheServerWhy()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        client.Client.Disconnect("quit", session.Now);

        Assert.True(session.RunUntil(() => session.ServerEvents.Any(e => e.Kind == NetEventKind.Disconnected)));
        Assert.Equal("quit", session.ServerEvents.Single(e => e.Kind == NetEventKind.Disconnected).Reason);
        Assert.Equal(0, session.Server.ClientCount);
    }

    [Fact]
    public void AKickReachesTheClient()
    {
        using NetSessionHarness session = new();
        NetSessionHarness.ClientEnd client = Join(session);
        session.Server.Disconnect(client.Client.Slot, "kicked by vote");

        Assert.True(session.RunUntil(() => client.Events.Any(e => e.Kind == NetEventKind.Disconnected)));
        Assert.Equal("kicked by vote", client.Events.Single(e => e.Kind == NetEventKind.Disconnected).Reason);
    }

    [Fact]
    public void ASilentClientTimesOut()
    {
        using NetSessionHarness session = new(new NetServerOptions { Timeout = 2 });
        NetSessionHarness.ClientEnd client = Join(session);
        client.Line.Send.Loss = 1;

        Assert.True(session.RunUntil(() => session.ServerEvents.Any(e => e.Kind == NetEventKind.Disconnected), 5));
        Assert.Equal("timed out", session.ServerEvents.Single(e => e.Kind == NetEventKind.Disconnected).Reason);
    }

    [Fact]
    public void ABrowserCanAskTheServerAboutItselfWithoutJoining()
    {
        LoopbackNetwork network = new();
        double now = 0;
        using NetServer server = new(network.Open(26001), new NetServerOptions
        {
            ProtocolId = NetSessionHarness.ProtocolId,
            Info = _ => "sandstorm 3/32"u8.ToArray(),
        });
        using NetQueryClient query = new(network.Open(), NetSessionHarness.ProtocolId);

        query.Query(server.LocalAddress, now);
        now += 0.05;
        server.Update(now);
        query.Update(now);

        Assert.True(query.TryReceive(out NetServerInfo info));
        Assert.Equal("sandstorm 3/32", Encoding.UTF8.GetString(info.Info));
        Assert.Equal(server.LocalAddress, info.Address);
        Assert.Equal(0, server.ClientCount);
    }

    private static NetSessionHarness.ClientEnd Join(NetSessionHarness session)
    {
        NetSessionHarness.ClientEnd client = session.AddClient();
        client.Client.Connect(session.Server.LocalAddress, [], session.Now);
        Assert.True(session.RunUntil(() => client.Client.State == NetClientState.Connected));
        session.Run(0.2);
        return client;
    }
}
