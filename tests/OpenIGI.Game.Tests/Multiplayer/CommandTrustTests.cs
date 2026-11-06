using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests.Multiplayer;

/// <summary>What a server takes on trust from a client's commands, and what it doesn't: their pace, their numbers, their view.</summary>
public sealed class CommandTrustTests
{
    private const int Channels = 8;

    [Fact]
    public void A_client_sending_commands_twice_as_fast_as_time_runs_no_faster_than_the_server()
    {
        UserCommandBuffer queue = new(Channels);
        float[] frame = new float[Channels];
        long next = 1;
        int ran = 0;
        const int Ticks = 300;
        for (int tick = 0; tick < Ticks; tick++)
        {
            queue.Push(Command(next++));
            queue.Push(Command(next++));
            for (int due = queue.Due(); due > 0 && queue.TryTake(frame); due--)
            {
                ran++;
            }
        }

        Assert.InRange(ran, Ticks - queue.TargetDepth, Ticks + queue.MaxCredit);
        Assert.True(queue.Throttled > 0);
    }

    [Fact]
    public void A_client_whose_packets_were_held_up_catches_up_on_the_runs_it_missed()
    {
        UserCommandBuffer queue = new(Channels);
        float[] frame = new float[Channels];
        long next = 1;
        int ran = 0;

        // Steady, then eight ticks with nothing arriving, then the eight held commands at once with the next.
        for (int tick = 0; tick < 60; tick++)
        {
            bool held = tick is >= 20 and < 28;
            if (!held)
            {
                int arriving = tick == 28 ? 9 : 1;
                for (int i = 0; i < arriving; i++)
                {
                    queue.Push(Command(next++));
                }
            }

            for (int due = queue.Due(); due > 0 && queue.TryTake(frame); due--)
            {
                ran++;
            }
        }

        Assert.True(queue.Buffered <= queue.MaxDepth, $"still {queue.Buffered} queued");
        Assert.Equal(next - 1 - queue.Buffered, ran);
    }

    [Fact]
    public void A_value_that_is_no_number_never_reaches_the_player()
    {
        UserCommandBuffer queue = new(Channels) { TargetDepth = 1 };
        float[] channels = new float[Channels];
        channels[0] = float.NaN;
        channels[1] = float.PositiveInfinity;
        channels[2] = 0.5f;
        queue.Push(new UserCommand(1, channels));

        float[] frame = new float[Channels];
        Assert.Equal(1, queue.Due());
        Assert.True(queue.TryTake(frame));
        Assert.Equal([0f, 0f, 0.5f], frame[..3]);
        Assert.Equal(1, queue.Unfit);
    }

    [Fact]
    public void A_view_further_back_than_the_clients_line_explains_is_held_to_what_it_does()
    {
        // The client's own stamps run far from the server's clock, as they do after a long session: they are no measure.
        UserCommandBuffer queue = new(Channels) { TargetDepth = 1, MaxRewindTicks = 10 };
        float[] frame = new float[Channels];
        queue.Push(new UserCommand(5000, new float[Channels], ViewTick: 70));
        queue.Push(new UserCommand(5001, new float[Channels], ViewTick: 95));
        queue.Push(new UserCommand(5002, new float[Channels], ViewTick: 140));

        // Taken with the server at 100: the line's 10 ticks and a hitch's 6, and the other two still waiting count too.
        queue.ServerTick = 100;
        queue.Due();
        Assert.True(queue.TryTake(frame));
        Assert.Equal(82, queue.LastViewTick);
        queue.ServerTick = 101;
        queue.Due();
        Assert.True(queue.TryTake(frame));
        Assert.Equal(95, queue.LastViewTick);
        queue.ServerTick = 102;
        queue.Due();
        Assert.True(queue.TryTake(frame));
        Assert.Equal(102, queue.LastViewTick);
        Assert.Equal(1, queue.Backtracked);
    }

    [Fact]
    public void A_client_whose_views_are_always_late_is_believed_but_one_shot_reaching_further_is_not()
    {
        // The line says 10 ticks, but this client honestly draws 25 behind (a slow machine, a deep buffer): believed once
        // the server has seen it do so steadily for a couple of seconds.
        UserCommandBuffer queue = new(Channels) { TargetDepth = 0, MaxRewindTicks = 10 };
        float[] frame = new float[Channels];
        long server = 1000;
        for (int i = 0; i < 90; i++, server++)
        {
            queue.Push(new UserCommand(i + 1, new float[Channels], ViewTick: server - 25));
            queue.ServerTick = server;
            queue.Due();
            Assert.True(queue.TryTake(frame));
            if (i >= 60)
            {
                Assert.Equal(server - 25, queue.LastViewTick);
            }
        }

        // Then one shot claims a view a second further back: held to the usual age and a hitch.
        long held = queue.Backtracked;
        queue.Push(new UserCommand(91, new float[Channels], ViewTick: server - 55));
        queue.ServerTick = server;
        queue.Due();
        Assert.True(queue.TryTake(frame));
        Assert.Equal(held + 1, queue.Backtracked);
        Assert.InRange(server - queue.LastViewTick, 25, 32);
    }

    [Fact]
    public void A_frame_from_another_machine_never_drives_the_developer_chords()
    {
        PlayerInputFrame frame = default;
        frame[(int)PlayerInputSlot.DebugFlyPressed] = 1f;
        frame[(int)PlayerInputSlot.DebugNumpadPlusHeld] = 1f;
        frame[(int)PlayerInputSlot.Fire] = 1f;

        PlayerInputFrame.ClearDeveloperSlots(frame.AsSpan());

        Assert.False(frame.IsSet(PlayerInputSlot.DebugFlyPressed));
        Assert.False(frame.IsSet(PlayerInputSlot.DebugNumpadPlusHeld));
        Assert.True(frame.IsSet(PlayerInputSlot.Fire));
    }

    private static UserCommand Command(long tick) => new(tick, new float[Channels]);
}
