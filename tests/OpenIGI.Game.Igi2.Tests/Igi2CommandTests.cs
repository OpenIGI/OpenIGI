using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>An I.G.I. 2 client's command: the frame, how the player stood as it began, and what it left the player with.</summary>
public sealed class Igi2CommandTests
{
    [Fact]
    public void The_start_the_end_and_the_frame_come_back_as_they_went()
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        frame[(int)Igi2HumanChannel.MoveForwards] = 1f;
        frame[(int)Igi2HumanChannel.LookX] = 0.25f;
        float[] command = new float[Igi2Command.ChannelCount];
        Igi2CommandStart start = new(1.5f, -0.2f, Unfiltered: true, SteadyTurns: true, InvertMouse: true, Igi2PlayerMovement.Igi1, Igi1Look.Retail, Igi1Weight: true, Seed: 123456);
        Igi2Command.Pack(frame, start, new Igi2CommandEnd(-1, Walk: true, 0.5f, 0.6f, Climb: 3), command);

        float[] read = new float[Igi2HumanChannels.Count];
        Igi2CommandStart? began = Igi2Command.Unpack(command, read, out Igi2CommandEnd? end);

        Assert.Equal(start, began);
        Assert.Equal(new Igi2CommandEnd(-1, true, 0.5f, 0.6f, 3), end);
        Assert.Equal(frame, read);
    }

    /// <summary>What the client's player hung on after the command comes with it, for the server's copy to be weighed against.</summary>
    /// <param name="climb">What it hung on.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void What_the_player_hangs_on_comes_with_the_command(int climb)
    {
        float[] command = new float[Igi2Command.ChannelCount];
        Igi2Command.Pack(new float[Igi2HumanChannels.Count], default, new Igi2CommandEnd(-1, Walk: true, 0.5f, 0.6f, climb), command);

        Igi2CommandStart? start = Igi2Command.Unpack(command, new float[Igi2HumanChannels.Count], out Igi2CommandEnd? end);

        Assert.Equal(default(Igi2CommandStart), start);
        Assert.Equal(new Igi2CommandEnd(-1, true, 0.5f, 0.6f, climb), end);
    }

    [Fact]
    public void A_chosen_weapon_is_the_pick_and_the_weapon_keys_are_taken_out()
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        frame[(int)Igi2HumanChannel.SelectWeapon] = -1f;
        frame[(int)Igi2HumanChannel.NextWeapon] = 1f;
        frame[(int)Igi2HumanChannel.WeaponCategory1] = 1f;
        float[] command = new float[Igi2Command.ChannelCount];
        Igi2Command.Pack(frame, default, new Igi2CommandEnd(3, false, 1f, 1f), command);

        float[] read = new float[Igi2HumanChannels.Count];
        Igi2Command.Unpack(command, read, out Igi2CommandEnd? end);

        Assert.Equal(3f, read[(int)Igi2HumanChannel.SelectWeapon]);
        Assert.Equal(0f, read[(int)Igi2HumanChannel.NextWeapon]);
        Assert.Equal(0f, read[(int)Igi2HumanChannel.WeaponCategory1]);
        Assert.Equal(3, end!.Value.Chosen);
    }

    [Fact]
    public void A_blank_command_changes_nobody_and_picks_no_weapon()
    {
        float[] read = new float[Igi2HumanChannels.Count];
        read[(int)Igi2HumanChannel.SelectWeapon] = 7f;

        Igi2CommandStart? start = Igi2Command.Unpack(new float[Igi2Command.ChannelCount], read, out Igi2CommandEnd? end);

        Assert.Null(start);
        Assert.Null(end);
        Assert.Equal(0f, read[(int)Igi2HumanChannel.SelectWeapon]);
    }

    /// <summary>
    /// Two streams that drew apart -- the client's player's since the level began, the server's copy's since the client joined
    /// -- draw alike once set from a command's seed: the same round scatters the same way on both machines.
    /// </summary>
    [Fact]
    public void Streams_set_from_one_seed_draw_alike_whatever_they_drew_before()
    {
        IgiRandom client = new(7u), server = new(99u);
        for (int i = 0; i < 37; i++)
        {
            client.NextRaw();
        }

        uint seed = Igi2Command.SeedFor(1234);
        Assert.True(seed <= Igi2Command.SeedLimit);

        // Through the command, as a float, and back.
        float[] command = new float[Igi2Command.ChannelCount];
        Igi2Command.Pack(new float[Igi2HumanChannels.Count], new Igi2CommandStart(0f, 0f, false, false, false, Igi2PlayerMovement.Igi2, Igi1Look.Linear, false, seed), new Igi2CommandEnd(-1, false, 1f, 1f), command);
        uint carried = Igi2Command.Unpack(command, new float[Igi2HumanChannels.Count], out _)!.Value.Seed;
        Assert.Equal(seed, carried);

        Igi2Command.Reseed(client, seed);
        Igi2Command.Reseed(server, carried);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(client.NextRaw(), server.NextRaw());
        }
    }
}
