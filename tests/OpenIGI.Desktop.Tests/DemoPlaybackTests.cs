using OpenIGI.Game.Multiplayer;
using OpenIGI.Host.Multiplayer;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>A demo played back to an ordinary client: its messages at their times, the viewer a ghost.</summary>
public sealed class DemoPlaybackTests
{
    [Fact]
    public void A_demo_is_delivered_to_a_client_at_its_times_with_the_recorder_made_one_of_the_others()
    {
        BitWriter writer = new();
        writer.WriteBits((uint)SessionMessage.Welcome, 8);
        writer.WriteVarUInt(3);
        writer.WriteVarUInt(30);
        writer.WriteString("4", 64);
        writer.WriteBits(0, 8);
        writer.WriteString("tdm", 16);
        byte[] welcome = writer.AsSpan().ToArray();
        byte[] Chat(string text)
        {
            writer.Reset();
            SessionTalk.WriteChat(writer, 7, text);
            return writer.AsSpan().ToArray();
        }

        DemoMessage[] messages =
        [
            new(0, NetChannel.Reliable, welcome),
            new(1, NetChannel.Reliable, Chat("one second in")),
            new(5, NetChannel.Reliable, Chat("five seconds in")),
        ];

        // Through a file and back, as a saved demo is played.
        MemoryStream file = new();
        DemoFile.Write(file, new DemoHeader(MultiplayerProtocol.Version, "igi1", "4", "tdm", "Anya"), messages);
        file.Position = 0;
        (DemoHeader header, IReadOnlyList<DemoMessage> read) = DemoFile.Read(file);
        Assert.Equal(("igi1", "4", "Anya", 3), (header.Title, header.Map, header.Player, read.Count));

        double now = 0;
        using DemoPlayback playback = new(header, read, () => now);
        ClientSession client = new(new NetClient(playback, MultiplayerProtocol.IdFor("igi1"), MultiplayerProtocol.Version), 4);
        client.Connect(playback.Server, new JoinRequest("viewer", "test", string.Empty), now);
        List<string> heard = [];
        for (; now < 3; now += 1 / 30.0)
        {
            client.Receive(now);
            client.Flush(now);
            heard.AddRange(client.TakeChat().Select(static l => l.Text));
        }

        Assert.Null(client.EndReason);
        Assert.True(playback.Seconds > 0, "the stand-in never let the client in");
        // Welcomed as nobody in the recording, on its map; the first line heard, the second not yet.
        Assert.Equal(DemoPlayback.GhostEntity, client.EntityId);
        Assert.Equal(("4", "tdm"), (client.Map, client.ServerMode));
        Assert.Equal(["one second in"], heard);
        Assert.False(playback.IsFinished);

        for (; now < 7; now += 1 / 30.0)
        {
            client.Receive(now);
            client.Flush(now);
            heard.AddRange(client.TakeChat().Select(static l => l.Text));
        }

        Assert.Equal(["one second in", "five seconds in"], heard);
        Assert.True(playback.IsFinished);
    }

    [Fact]
    public void A_demo_plays_faster_or_holds_where_it_is_and_a_travel_takes_the_viewer_on_as_nobody()
    {
        BitWriter writer = new();
        writer.WriteBits((uint)SessionMessage.Welcome, 8);
        writer.WriteVarUInt(3);
        writer.WriteVarUInt(30);
        writer.WriteString("4", 64);
        writer.WriteBits(0, 8);
        writer.WriteString("tdm", 16);
        byte[] welcome = writer.AsSpan().ToArray();
        writer.Reset();
        writer.WriteBits((uint)SessionMessage.Travel, 8);
        writer.WriteBits(1, 8);
        writer.WriteVarUInt(3);
        writer.WriteString("5", 64);
        byte[] travel = writer.AsSpan().ToArray();
        writer.Reset();
        SessionTalk.WriteChat(writer, 7, "ten seconds in");
        byte[] chat = writer.AsSpan().ToArray();

        double now = 0;
        using DemoPlayback playback = new(
            new DemoHeader(MultiplayerProtocol.Version, "igi1", "4", "tdm", "Anya"),
            [new(0, NetChannel.Reliable, welcome), new(2, NetChannel.Reliable, travel), new(10, NetChannel.Reliable, chat)],
            () => now)
        { Speed = 4 };
        ClientSession client = new(new NetClient(playback, MultiplayerProtocol.IdFor("igi1"), MultiplayerProtocol.Version), 4);
        client.Connect(playback.Server, new JoinRequest("viewer", "test", string.Empty), now);
        List<string> heard = [];
        void Run(double seconds)
        {
            for (double end = now + seconds; now < end; now += 1 / 30.0)
            {
                client.Receive(now);
                client.Flush(now);
                heard.AddRange(client.TakeChat().Select(static l => l.Text));
            }
        }

        // At four times, two seconds of the wall play eight of the recording: the travel taken, as nobody, the chat not yet.
        Run(2);
        Assert.Equal(("5", DemoPlayback.GhostEntity), (client.Map, client.EntityId));
        Assert.Empty(heard);

        // Paused, nothing more comes however long the wall runs.
        playback.Paused = true;
        double held = playback.Seconds;
        Run(5);
        Assert.Equal(held, playback.Seconds, 6);
        Assert.Empty(heard);

        playback.Paused = false;
        Run(1);
        Assert.Equal(["ten seconds in"], heard);
    }

    [Fact]
    public void A_demo_from_another_protocol_or_not_a_demo_at_all_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => new DemoPlayback(new DemoHeader(MultiplayerProtocol.Version - 1, "igi1", "1", "tdm", "x"), []));
        Assert.Throws<InvalidDataException>(() => DemoFile.Read(new MemoryStream("not a demo at all"u8.ToArray())));
    }
}
