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

        // The repeat costs a bit a channel, and one more to say it carries no tick it was drawn at.
        Assert.Equal(Channels + 1, two.BitCount - one.BitCount);
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

    /// <summary>
    /// A command is run on the tick it is found waiting, and two that came together both are, as Source runs a player's
    /// (<c>CBasePlayer::PhysicsSimulate</c>); a tick with none runs nothing rather than repeat the last. Given one a tick, two
    /// that came together left one queued for the rest of the session, and every command after ran a tick late for each.
    /// </summary>
    [Fact]
    public void Queue_RunsWhatHasArrivedAndNothingWhenDryRatherThanRepeat()
    {
        UserCommandBuffer queue = new(Channels);
        float[] frame = new float[Channels];
        queue.Push(Command(1, look: 0.5f, fire: 1));
        Assert.Equal(1, queue.Due());
        Assert.True(queue.TryTake(frame));

        // Dry: nothing to run, and the client's next commands are run when they come, both of them.
        Assert.Equal(0, queue.Due());
        Assert.Equal(1, queue.Missed);
        queue.Push(Command(2, look: 0.6f));
        queue.Push(Command(3, look: 0.7f));
        Assert.Equal(2, queue.Due());
        Assert.True(queue.TryTake(frame));
        Assert.True(queue.TryTake(frame));
        Assert.Equal(3, queue.LastTakenTick);
        Assert.Equal(0, queue.Buffered);
    }

    /// <summary>
    /// A line that brings its commands unevenly -- none this tick, two the next -- has the newest held back a tick, so the ticks
    /// it brings none still have one to run and the player moves evenly for those watching. Once the line is steady for a while
    /// the hold is let go, and nothing stays queued.
    /// </summary>
    [Fact]
    public void Queue_HoldsOneBackWhileTheLineIsUnevenAndLetsItGoWhenItSteadies()
    {
        UserCommandBuffer queue = new(Channels);
        float[] frame = new float[Channels];
        long next = 1;
        int Run(int arriving)
        {
            for (int i = 0; i < arriving; i++)
            {
                queue.Push(Command(next++));
            }

            int ran = 0;
            for (int due = queue.Due(); due > 0 && queue.TryTake(frame); due--)
            {
                ran++;
            }

            return ran;
        }

        // Steady: one arrives, one runs, none waits.
        for (int tick = 0; tick < 120; tick++)
        {
            Assert.Equal(1, Run(1));
        }

        Assert.Equal(0, queue.Held);

        // Uneven: none, then two, over and over. Soon one is held back and every tick runs one.
        for (int tick = 0; tick < 200; tick++)
        {
            Run(tick % 2 == 0 ? 0 : 2);
        }

        Assert.Equal(1, queue.Held);
        long dry = queue.Missed;
        for (int tick = 0; tick < 100; tick++)
        {
            Run(tick % 2 == 0 ? 0 : 2);
        }

        Assert.Equal(dry, queue.Missed);

        // Steady again: after a while the hold is let go and the queue runs empty.
        for (int tick = 0; tick < 400; tick++)
        {
            Run(1);
        }

        Assert.Equal(0, queue.Held);
        Assert.Equal(0, queue.Buffered);
    }

    /// <summary>A stall is no uneven line: a client that sends nothing for a second and then everything has nothing held back for it after.</summary>
    [Fact]
    public void Queue_HoldsNothingBackForAStall()
    {
        UserCommandBuffer queue = new(Channels);
        float[] frame = new float[Channels];
        long next = 1;
        for (int tick = 0; tick < 400; tick++)
        {
            int arriving = tick % 100 == 99 ? 31 : tick % 100 >= 69 ? 0 : 1;
            for (int i = 0; i < arriving; i++)
            {
                queue.Push(Command(next++));
            }

            for (int due = queue.Due(); due > 0 && queue.TryTake(frame); due--)
            {
                // The loop's condition does the work.
            }
        }

        Assert.Equal(0, queue.Held);
    }

    [Fact]
    public void Queue_CatchesUpOnWhatItSavedWhenTheClientRunsAhead()
    {
        UserCommandBuffer queue = new(Channels) { MaxDepth = 3 };

        // Two ticks with nothing to run: the client's line was held up, and it saved the runs it missed.
        queue.Due();
        queue.Due();
        for (long tick = 1; tick <= 5; tick++)
        {
            queue.Push(Command(tick));
        }

        // Three runs saved -- the two dry ticks' and this one's -- and five waiting: all three are given.
        Assert.Equal(3, queue.Due());
    }

    [Fact]
    public void Queue_DropsARepeatOfACommandAlreadyRun()
    {
        UserCommandBuffer queue = new(Channels);
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

    [Fact]
    public void Codec_CarriesViewTicksFromThePacketsBase_HoweverFarTheCommandNumbersHaveFallenBehind()
    {
        // Commands numbered 100 on, drawn at server ticks 5000 on: a client whose clock gave up on a hitch's ticks.
        UserCommandCodec codec = new(Channels);
        UserCommand[] sent = [Command(100) with { ViewTick = 5000, ViewFraction = 0.5f }, Command(101) with { ViewTick = 5001 }];
        BitWriter writer = new();
        codec.Write(writer, sent, viewBase: 5003);
        BitReader reader = new(writer.AsSpan());
        List<UserCommand> read = [];

        Assert.True(codec.Read(ref reader, read, viewBase: 5003));
        Assert.Equal([5000L, 5001L], read.Select(static c => c.ViewTick));
        Assert.Equal([100L, 101L], read.Select(static c => c.Tick));
    }

    private static UserCommand Command(long tick, float look = 0f, float fire = 0f)
    {
        float[] channels = new float[Channels];
        channels[0] = look;
        channels[1] = fire;
        return new UserCommand(tick, channels);
    }
}
