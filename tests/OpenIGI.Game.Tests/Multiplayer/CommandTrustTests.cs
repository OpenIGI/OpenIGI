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
    public void A_view_further_back_than_the_client_usually_draws_is_held_to_what_it_does()
    {
        // A second of honest views 8 ticks behind, under a line that explains 10. The client's own stamps run far from the
        // server's clock, as they do after a long session: they are no measure.
        UserCommandBuffer queue = new(Channels) { TargetDepth = 0, MaxRewindTicks = 10 };
        float[] frame = new float[Channels];
        long server = 100;
        for (int i = 0; i < 30; i++, server++)
        {
            Take(queue, frame, 5000 + i, server, server - 8);
        }

        Assert.Equal(0, queue.Backtracked);

        // A shot claiming a view 70 ticks back is held to the larger of the line's 10 and its usual 8 and a hitch's 6; one
        // ahead of the server, to now.
        Take(queue, frame, 5030, server, server - 70);
        Assert.Equal(server - 14, queue.LastViewTick);
        Take(queue, frame, 5031, server + 1, server + 40);
        Assert.Equal(server + 1, queue.LastViewTick);
        Assert.Equal(1, queue.Backtracked);
    }

    [Fact]
    public void A_client_whose_views_are_always_late_is_believed_but_one_shot_reaching_further_is_not()
    {
        // The line says 10 ticks, but this client honestly draws 25 behind (a slow machine, a deep buffer): believed.
        UserCommandBuffer queue = new(Channels) { TargetDepth = 0, MaxRewindTicks = 10 };
        float[] frame = new float[Channels];
        long server = 1000;
        for (int i = 0; i < 90; i++, server++)
        {
            Take(queue, frame, i + 1, server, server - 25);
            Assert.Equal(server - 25, queue.LastViewTick);
        }

        Assert.Equal(0, queue.Backtracked);

        // Then one shot claims a view a second further back: held to the usual age and a hitch.
        Take(queue, frame, 91, server, server - 55);
        Assert.Equal(1, queue.Backtracked);
        Assert.InRange(server - queue.LastViewTick, 25, 32);
    }

    [Fact]
    public void A_client_claiming_old_views_from_the_start_never_teaches_the_server_a_reach()
    {
        UserCommandBuffer queue = new(Channels) { TargetDepth = 0, MaxRewindTicks = 10 };
        float[] frame = new float[Channels];
        long server = 1000;
        for (int i = 0; i < 30; i++, server++)
        {
            Take(queue, frame, i + 1, server, server - 29);
        }

        // Learned as no more than twice the line and a hitch (26), so 29 back is held to that and a hitch again.
        Take(queue, frame, 31, server, server - 29);
        Assert.Equal(server - 29, queue.LastViewTick);
        Take(queue, frame, 32, server + 1, server - 60);
        Assert.Equal(1, queue.Backtracked);
        Assert.True(server + 1 - queue.LastViewTick <= 33, $"held to {server + 1 - queue.LastViewTick} ticks back");
    }

    private static void Take(UserCommandBuffer queue, float[] frame, long tick, long server, long view)
    {
        queue.Push(new UserCommand(tick, new float[Channels], ViewTick: view));
        queue.ServerTick = server;
        queue.Due();
        Assert.True(queue.TryTake(frame));
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
