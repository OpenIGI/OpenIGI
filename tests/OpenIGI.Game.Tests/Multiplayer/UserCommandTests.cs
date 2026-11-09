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
    public void Codec_carries_the_fraction_of_a_tick_the_client_drew_at_to_a_256th()
    {
        UserCommandCodec codec = new(Channels);
        UserCommand[] sent =
        [
            new(100, new float[Channels], ViewTick: 94, ViewFraction: 0.37f),
            new(101, new float[Channels], ViewTick: 95, ViewFraction: 0.999f),
            new(102, new float[Channels]),
        ];

        BitWriter writer = new();
        codec.Write(writer, sent);
        BitReader reader = new(writer.AsSpan());
        List<UserCommand> read = [];

        Assert.True(codec.Read(ref reader, read));
        Assert.Equal((94L, 95L, long.MinValue), (read[0].ViewTick, read[1].ViewTick, read[2].ViewTick));
        Assert.Equal(0.37f, read[0].ViewFraction, 1f / 256);
        Assert.Equal(255f / 256, read[1].ViewFraction, 1e-6f);
        Assert.Equal(0f, read[2].ViewFraction);
    }

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
    public void Queue_RunsEachCommandOnceInTheClientsOrderWhateverOrderItArrivedIn()
    {
        UserCommandBuffer queue = new(Channels);
        queue.Push(Command(12, look: 0.12f));
        queue.Push(Command(10, look: 0.10f));
        queue.Push(Command(11, look: 0.11f));
        queue.Push(Command(11, look: 9f));

        float[] frame = new float[Channels];
        for (long tick = 10; tick <= 12; tick++)
        {
            Assert.Equal(1, queue.Due());
            Assert.True(queue.TryTake(frame));
            Assert.Equal(tick / 100f, frame[0], 5);
            Assert.Equal(tick, queue.LastTakenTick);
        }

        Assert.Equal(0, queue.Missed);
    }

    [Fact]
    public void Queue_WaitsForItsBufferAndRunsNothingWhenDryRatherThanRepeat()
    {
        UserCommandBuffer queue = new(Channels) { TargetDepth = 2 };
        float[] frame = new float[Channels];
        queue.Push(Command(1, look: 0.5f, fire: 1));
        Assert.Equal(0, queue.Due());

        queue.Push(Command(2, look: 0.6f));
        Assert.Equal(1, queue.Due());
        Assert.True(queue.TryTake(frame));
        Assert.Equal(1, queue.Due());
        Assert.True(queue.TryTake(frame));

        // Dry: nothing to run, and the client's next command is run when it comes, not skipped over.
        Assert.Equal(0, queue.Due());
        Assert.Equal(1, queue.Missed);
        queue.Push(Command(3, look: 0.7f));
        queue.Push(Command(4, look: 0.8f));
        Assert.Equal(1, queue.Due());
        Assert.True(queue.TryTake(frame));
        Assert.Equal(3, queue.LastTakenTick);
    }

    [Fact]
    public void Queue_CatchesUpTwoATickWhenTheClientRunsAhead()
    {
        UserCommandBuffer queue = new(Channels) { TargetDepth = 1, MaxDepth = 3 };

        // Two ticks with nothing to run: the client's line was held up, and it saved the runs it missed.
        queue.Due();
        queue.Due();
        for (long tick = 1; tick <= 5; tick++)
        {
            queue.Push(Command(tick));
        }

        Assert.Equal(2, queue.Due());
    }

    [Fact]
    public void Queue_DropsARepeatOfACommandAlreadyRun()
    {
        UserCommandBuffer queue = new(Channels) { TargetDepth = 1 };
        float[] frame = new float[Channels];
        queue.Push(Command(20, look: 1f));
        queue.Due();
        queue.TryTake(frame);
        queue.Push(Command(20, look: 1f));
        queue.Push(Command(19, look: 1f));

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
