using System.Diagnostics;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Net.Rooms;
using OpenIGI.Net.WebRtc;
using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>Skipped where libdatachannel isn't built (tools/native/build-datachannel.ps1).</summary>
public sealed class DataChannelFactAttribute : FactAttribute
{
    public DataChannelFactAttribute()
    {
        if (!DataChannelPeerFactory.IsAvailable())
        {
            Skip = "libdatachannel is not built here: run tools/native/build-datachannel.ps1.";
        }
    }
}

/// <summary>Through the live master server and relay: run only when OPENIGI_LIVE_ROOMS=1, never in the suite.</summary>
public sealed class LiveRoomFactAttribute : FactAttribute
{
    public LiveRoomFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPENIGI_LIVE_ROOMS") != "1")
        {
            Skip = "Reaches master.openigi.com and turn.openigi.com: set OPENIGI_LIVE_ROOMS=1.";
        }
        else if (!DataChannelPeerFactory.IsAvailable())
        {
            Skip = "libdatachannel is not built here: run tools/native/build-datachannel.ps1.";
        }
    }
}

public sealed class WebRtcTests
{
    [DataChannelFact]
    public void TwoPeersCarryDatagramsBothWays()
    {
        DataChannelPeerFactory factory = new(NullLogger.Instance);
        using IRtcPeer offerer = factory.Create([], offerer: true);
        using IRtcPeer answerer = factory.Create([], offerer: false);

        Assert.True(
            Pump(() => offerer.State == RtcPeerState.Open && answerer.State == RtcPeerState.Open, offerer, answerer),
            $"never opened: {offerer.State}/{answerer.State} {offerer.Failure}{answerer.Failure}");

        byte[] there = [1, 2, 3, 4];
        byte[] back = new byte[NetConstants.MaxPacketSize];
        Random.Shared.NextBytes(back);
        offerer.Send(there);
        answerer.Send(back);

        byte[] buffer = new byte[NetConstants.MaxPacketSize];
        int length = 0;
        Assert.True(Pump(() => answerer.TryReceive(buffer, out length), offerer, answerer));
        Assert.Equal(there, buffer[..length]);
        Assert.True(Pump(() => offerer.TryReceive(buffer, out length), offerer, answerer));
        Assert.Equal(back, buffer[..length]);
    }

    [LiveRoomFact]
    public void ARoomConnectsAClientToItsServer() => Session(relayOnly: false);

    [LiveRoomFact]
    public void ARoomConnectsThroughTheRelay() => Session(relayOnly: true);

    private static void Session(bool relayOnly)
    {
        DataChannelPeerFactory factory = new(NullLogger.Instance) { RelayOnly = relayOnly };
        RoomRequest request = new("test", "test", "WebRtcTests", "1", "coop", "0.0.0", 4, Public: false, Passworded: false);
        using RoomTransport host = RoomTransport.Host(RoomTransport.DefaultMaster, request, factory, NullLogger.Instance);
        using NetServer server = new(host, new NetServerOptions { ProtocolId = NetSessionHarness.ProtocolId });
        Stopwatch clock = Stopwatch.StartNew();

        Assert.True(Wait(() => { server.Update(clock.Elapsed.TotalSeconds); return host.Status != RoomStatus.Connecting; }), "room never opened");
        Assert.Equal(RoomStatus.Open, host.Status);

        using RoomTransport joiner = RoomTransport.Join(RoomTransport.DefaultMaster, host.Code!, factory, NullLogger.Instance);
        using NetClient client = new(joiner, NetSessionHarness.ProtocolId) { Timeout = 20 };
        client.Connect(RoomTransport.HostAddress, [], clock.Elapsed.TotalSeconds);

        bool connected = Wait(() =>
        {
            double now = clock.Elapsed.TotalSeconds;
            server.Update(now);
            while (server.TryReceive(out _))
            {
            }

            client.Update(now);
            while (client.TryReceive(out _))
            {
            }

            client.Flush(now);
            server.Flush(now);
            return client.State == NetClientState.Connected;
        });

        Assert.True(connected, $"client {client.State}; joiner {joiner.Status} {joiner.Error}");
    }

    private static bool Pump(Func<bool> done, params IRtcPeer[] pair)
    {
        return Wait(() =>
        {
            Relay(pair[0], pair[1]);
            Relay(pair[1], pair[0]);
            return done();
        });
    }

    private static void Relay(IRtcPeer from, IRtcPeer to)
    {
        while (from.TryTakeSignal(out RtcSignal signal))
        {
            if (signal.Kind == "candidate")
            {
                to.AddRemoteCandidate(signal.Value, signal.Mid);
            }
            else
            {
                to.SetRemoteDescription(signal.Kind, signal.Value);
            }
        }
    }

    private static bool Wait(Func<bool> done, double seconds = 20)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (done())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return false;
    }
}
