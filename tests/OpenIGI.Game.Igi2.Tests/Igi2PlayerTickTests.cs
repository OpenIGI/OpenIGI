using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The player's tick, <c>HumanPlayer_Update</c> (<c>igi2 0x4A1EC0</c>): the parts in the order they run.</summary>
public sealed class Igi2PlayerTickTests
{
    /// <summary>Standing still on a floor, the player stays put and grounded, and the jump wait counts down.</summary>
    [Fact]
    public void Standing_still_stays_put()
    {
        (Igi2HumanPlayer player, Igi2PlayerFakes.FakeAnimation animation, _) = OnFloor();

        for (int tick = 0; tick < 10; tick++)
        {
            Tick(player, Igi2PlayerAction.None);
        }

        Assert.Equal(default, player.Position);
        Assert.True((player.Signal & Igi2PlayerSignal.Grounded) != 0);
        Assert.Equal(Igi2HumanPlayer.JumpWait - 9, player.JumpWaitTimer);
        Assert.Equal(10, animation.Steps);
    }

    /// <summary>Holding forward walks the player along its heading.</summary>
    [Fact]
    public void Holding_forward_moves_along_the_heading()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();

        for (int tick = 0; tick < 30; tick++)
        {
            Tick(player, Igi2PlayerAction.MoveForwards);
        }

        Assert.True(player.Position.Y > 1000.0, $"the player only reached {player.Position}.");
        Assert.InRange(player.Position.X, -1.0, 1.0);
        Assert.Equal(0.0, player.Position.Z);
    }

    /// <summary>The mouse turns the body on the spot.</summary>
    [Fact]
    public void The_mouse_turns_the_body()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();

        Tick(player, Igi2PlayerAction.None, mouseX: 0.02f);

        Assert.True(player.Gamma != 0f);
        Assert.Equal(default, player.Position);
    }

    /// <summary>The eyes follow the body each tick.</summary>
    [Fact]
    public void The_eyes_follow_the_body()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();

        Tick(player, Igi2PlayerAction.None);

        Assert.Equal(Igi2Human.EyesAt(player.Position, Igi2HumanPose.Standing), player.View.Position);
    }

    /// <summary>Deep water raises the swimming bit and forbids lying and crouching; it is stirred while wading.</summary>
    [Fact]
    public void Deep_water_swims_and_forbids_lying_and_crouching()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = OnFloor();
        surroundings.Water = 6000f;

        Tick(player, Igi2PlayerAction.None);

        Assert.True((player.Signal & Igi2PlayerSignal.Swimming) != 0);
        Assert.Equal(6000f, player.WaterDepth);
        Assert.Equal(Igi2AllowedStance.None, player.AllowedStance & (Igi2AllowedStance.Lie | Igi2AllowedStance.Crouch));
    }

    /// <summary>A missile launcher in hand forbids lying.</summary>
    [Fact]
    public void A_missile_launcher_forbids_lying()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor(new Igi2PlayerFakes.RecordingWeapon { ActiveIsMissileLauncher = true });

        Tick(player, Igi2PlayerAction.None);

        Assert.Equal(Igi2AllowedStance.None, player.AllowedStance & Igi2AllowedStance.Lie);
    }

    /// <summary>A computer page open blocks fire.</summary>
    [Fact]
    public void A_computer_page_blocks_fire()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();

        Tick(player, Igi2PlayerAction.Fire | Igi2PlayerAction.ComputerMap);

        Assert.Equal(0f, player.Channels[Igi2HumanChannel.Fire]);
    }

    /// <summary>The view's kick dies away by its damping each tick.</summary>
    [Fact]
    public void A_view_kick_dies_away()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();
        player.View.PitchOffset = 0.5f;
        player.View.OffsetDamping = 0.25f;

        Tick(player, Igi2PlayerAction.None);

        Assert.Equal(0.375f, player.View.PitchOffset);
    }

    private static (Igi2HumanPlayer Player, Igi2PlayerFakes.FakeAnimation Animation, Igi2PlayerFakes.FakeSurroundings Surroundings) OnFloor(
        IIgi2PlayerWeaponControl? weapon = null) => Igi2PlayerFakes.OnFloor(weapon);

    private static void Tick(Igi2HumanPlayer player, Igi2PlayerAction held, float mouseX = 0f)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        new Igi2PlayerInputReader().Read(new Igi2PlayerInputState(mouseX, 0f, held, 0), frame);
        player.Update(frame);
    }
}
