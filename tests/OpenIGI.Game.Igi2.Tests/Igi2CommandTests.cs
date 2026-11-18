using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Multiplayer;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>An I.G.I. 2 client's command: the frame, where it looked as the frame began, and the weapon it chose.</summary>
public sealed class Igi2CommandTests
{
    [Fact]
    public void The_look_and_the_frame_come_back_as_they_went()
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        frame[(int)Igi2HumanChannel.MoveForwards] = 1f;
        frame[(int)Igi2HumanChannel.LookX] = 0.25f;
        float[] command = new float[Igi2Command.ChannelCount];
        Igi2Command.Pack(frame, new Igi2CommandLook(1.5f, -0.2f, Unfiltered: true), chosen: -1, command);

        float[] read = new float[Igi2HumanChannels.Count];
        Igi2CommandLook? look = Igi2Command.Unpack(command, read);

        Assert.Equal(new Igi2CommandLook(1.5f, -0.2f, true), look);
        Assert.Equal(frame, read);
    }

    [Fact]
    public void A_chosen_weapon_is_the_pick_and_the_weapon_keys_are_taken_out()
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        frame[(int)Igi2HumanChannel.SelectWeapon] = -1f;
        frame[(int)Igi2HumanChannel.NextWeapon] = 1f;
        frame[(int)Igi2HumanChannel.WeaponCategory1] = 1f;
        float[] command = new float[Igi2Command.ChannelCount];
        Igi2Command.Pack(frame, new Igi2CommandLook(0f, 0f, false), chosen: 3, command);

        float[] read = new float[Igi2HumanChannels.Count];
        Igi2Command.Unpack(command, read);

        Assert.Equal(3f, read[(int)Igi2HumanChannel.SelectWeapon]);
        Assert.Equal(0f, read[(int)Igi2HumanChannel.NextWeapon]);
        Assert.Equal(0f, read[(int)Igi2HumanChannel.WeaponCategory1]);
    }

    [Fact]
    public void A_blank_command_turns_nobody_and_picks_no_weapon()
    {
        float[] read = new float[Igi2HumanChannels.Count];
        read[(int)Igi2HumanChannel.SelectWeapon] = 7f;

        Igi2CommandLook? look = Igi2Command.Unpack(new float[Igi2Command.ChannelCount], read);

        Assert.Null(look);
        Assert.Equal(0f, read[(int)Igi2HumanChannel.SelectWeapon]);
    }
}
