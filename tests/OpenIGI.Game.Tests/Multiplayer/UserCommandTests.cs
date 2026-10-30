using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using OpenIGI.Net;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>The user command's journey: packed with its predecessors, unpacked, queued and run on its tick.</summary>
public sealed class UserCommandTests
{
    private const int Channels = 63;

    [Fact]
    public void Codec_RoundTripsConsecutiveCommandsExactly()
    {
        UserCommandCodec codec = new(Channels);
        UserCommand[] sent = [Command(100, look: 0.0371f, fire: 1), Command(101, look: -0.5f, fire: 1), Command(102, look: 0f, fire: 0)];

        BitWriter writer = new();
        codec.Write(writer, sent);
        BitReader reader = new(writer.AsSpan());
        List<UserCommand> read = [];

        Assert.True(codec.Read(ref reader, read));
        Assert.Equal(sent.Length, read.Count);
        for (int i = 0; i < sent.Length; i++)
        {
            Assert.Equal(sent[i].Tick, read[i].Tick);
            Assert.Equal(sent[i].Channels, read[i].Channels);
        }
    }

    [Fact]
    public void Codec_CarriesEachCommandsViewTick()
    {
        UserCommandCodec codec = new(Channels);
        UserCommand[] sent = [Command(41), Command(42), Command(43)];
        sent[0] = sent[0] with { ViewTick = 35 };
        sent[2] = sent[2] with { ViewTick = 37 };
        BitWriter writer = new(256);
        codec.Write(writer, sent);
        BitReader reader = new(writer.AsSpan().ToArray());
        List<UserCommand> read = [];
        Assert.True(codec.Read(ref reader, read));
        Assert.Equal([35L, long.MinValue, 37L], read.Select(static c => c.ViewTick));
    }
    [Fact]
    public void Codec_SpendsLittleOnAnUnchangedFrame()
    {
        UserCommandCodec codec = new(Channels);
        UserCommand a = Command(5, look: 0.25f, fire: 1);
        UserCommand b = new(6, (float[])a.Channels.Clone());

        BitWriter one = new();
        codec.Write(one, [a]);
        BitWriter two = new();
        codec.Write(two, [a, b]);

        // The repeat costs a bit a channel, and a byte for the tick it was drawn at.
        Assert.Equal(Channels + 8, two.BitCount - one.BitCount);
    }

    [Fact]
    public void Codec_RefusesCommandsThatAreNotConsecutive()
    {
        UserCommandCodec codec = new(Channels);
        Assert.Throws<ArgumentException>(() => codec.Write(new BitWriter(), [Command(1), Command(3)]));
    }

    [Fact]
    public void Codec_RejectsATruncatedPacket()
    {
        UserCommandCodec codec = new(Channels);
        BitWriter writer = new();
        codec.Write(writer, [Command(9, look: 0.3f)]);
        BitReader reader = new(writer.AsSpan()[..6]);
        List<UserCommand> read = [];

        Assert.False(codec.Read(ref reader, read));
        Assert.Empty(read);
    }

    [Fact]
    public void Queue_RunsEachCommandOnItsOwnTickWhateverOrderItArrivedIn()
    {
        UserCommandBuffer queue = new(Channels);
        queue.Push(Command(12, look: 0.12f), serverTick: 10);
        queue.Push(Command(10, look: 0.10f), serverTick: 10);
        queue.Push(Command(11, look: 0.11f), serverTick: 10);
        queue.Push(Command(11, look: 9f), serverTick: 10);

        float[] frame = new float[Channels];
        for (long tick = 10; tick <= 12; tick++)
        {
            Assert.True(queue.Take(tick, frame));
            Assert.Equal(tick / 100f, frame[0], 5);
        }

        Assert.Equal(0, queue.Missed);
    }

    [Fact]
    public void Queue_HoldsTheLastCommandThroughAGapAndCountsIt()
    {
        UserCommandBuffer queue = new(Channels);
        queue.Push(Command(1, look: 0.5f, fire: 1), serverTick: 1);
        float[] frame = new float[Channels];
        queue.Take(1, frame);

        Assert.False(queue.Take(2, frame));
        Assert.Equal(0.5f, frame[0]);
        Assert.Equal(1f, frame[1]);
        Assert.Equal(1, queue.Missed);
    }

    [Fact]
    public void Queue_DropsACommandForATickAlreadyRun()
    {
        UserCommandBuffer queue = new(Channels);
        float[] frame = new float[Channels];
        queue.Take(20, frame);
        queue.Push(Command(19, look: 1f), serverTick: 21);

        Assert.Equal(1, queue.Late);
        Assert.Equal(0, queue.Buffered);
    }

    [Fact]
    public void Roster_OfOneAnswersAsTheOriginalsPointerDid()
    {
        PlayerRoster<string> roster = new();
        Assert.Null(roster.Primary);

        roster.Set("jones");
        Assert.Equal("jones", roster.Primary);
        Assert.Equal(0, roster.IndexOf("jones"));
        Assert.Equal("jones", roster.Nearest(_ => 1));
        Assert.Null(roster.Nearest(_ => null));

        roster.Set(null);
        Assert.Empty(roster);
    }

    [Fact]
    public void Roster_NearestPicksTheLowestRankAndTheEarliestOnATie()
    {
        PlayerRoster<string> roster = new();
        roster.Add("a");
        roster.Add("b");
        roster.Add("c");
        roster.Add("b");

        Assert.Equal(3, roster.Count);
        Assert.Equal("b", roster.Nearest(p => p == "a" ? 5 : 2));
        Assert.True(roster.Remove("a"));
        Assert.Equal("b", roster.Primary);
    }

    private static UserCommand Command(long tick, float look = 0f, float fire = 0f)
    {
        float[] channels = new float[Channels];
        channels[0] = look;
        channels[1] = fire;
        return new UserCommand(tick, channels);
    }
}
