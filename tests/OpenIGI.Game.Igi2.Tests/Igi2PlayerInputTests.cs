using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The player's channel reader and signal handler, against what the retail functions do.</summary>
public sealed class Igi2PlayerInputTests
{
    /// <summary>A held action lands on its channel; the remap table and the reader agree.</summary>
    [Theory]
    [InlineData(Igi2PlayerAction.MoveForwards, Igi2HumanChannel.MoveForwards)]
    [InlineData(Igi2PlayerAction.ZoomIn, Igi2HumanChannel.ZoomIn)]
    [InlineData(Igi2PlayerAction.LowerStance, Igi2HumanChannel.LowerStance)]
    [InlineData(Igi2PlayerAction.ModifyWalkRun, Igi2HumanChannel.ModifyWalkRun)]
    [InlineData(Igi2PlayerAction.ComputerMap, Igi2HumanChannel.ComputerMap)]
    [InlineData(Igi2PlayerAction.ComputerObjectives, Igi2HumanChannel.ComputerObjectives)]
    [InlineData(Igi2PlayerAction.ToggleStandLie, Igi2HumanChannel.ToggleStandLie)]
    public void A_held_action_sets_its_channel(Igi2PlayerAction action, Igi2HumanChannel channel)
    {
        float[] frame = Read(new Igi2PlayerInputState(0f, 0f, action, Igi2PlayerAction.None));
        Assert.Equal(1f, frame[(int)channel]);
    }

    /// <summary>Weapon categories select on the press, not while held.</summary>
    [Fact]
    public void Categories_read_the_press()
    {
        float[] held = Read(new Igi2PlayerInputState(0f, 0f, Igi2PlayerAction.WeaponCategory3, Igi2PlayerAction.None));
        float[] pressed = Read(new Igi2PlayerInputState(0f, 0f, Igi2PlayerAction.None, Igi2PlayerAction.WeaponCategory3));

        Assert.Equal(0f, held[(int)Igi2HumanChannel.WeaponCategory1 + 2]);
        Assert.Equal(1f, pressed[(int)Igi2HumanChannel.WeaponCategory1 + 2]);
        Assert.Equal(1f, Read(new Igi2PlayerInputState(0f, 0f, 0, Igi2PlayerAction.WeaponCategory9))[(int)Igi2HumanChannel.WeaponCategory9]);
    }

    /// <summary>Sensitivity is clamped to [0.05, 1], the turn keys nudge by 0.05, and inversion flips Y.</summary>
    [Fact]
    public void Look_channels_scale_clamp_and_nudge()
    {
        Igi2PlayerInputReader reader = new() { MouseSensitivity = 0f, InvertMouse = true };
        float[] frame = new float[Igi2HumanChannels.Count];
        reader.Read(new Igi2PlayerInputState(2f, 4f, Igi2PlayerAction.TurnLeft, 0), frame);

        Assert.Equal((0.050000001f * 2f) - 0.050000001f, frame[(int)Igi2HumanChannel.LookX]);
        Assert.Equal(-0.050000001f * 4f, frame[(int)Igi2HumanChannel.LookY]);
        Assert.Equal(-1f, frame[(int)Igi2HumanChannel.Unused13]);
    }

    /// <summary>A frozen trigger reads zero until it is released, then frees itself.</summary>
    [Fact]
    public void A_frozen_trigger_waits_for_release()
    {
        Igi2PlayerInputReader reader = new() { FreezeFire = true };
        float[] frame = new float[Igi2HumanChannels.Count];

        reader.Read(new Igi2PlayerInputState(0f, 0f, Igi2PlayerAction.Fire, 0), frame);
        Assert.Equal(0f, frame[(int)Igi2HumanChannel.Fire]);
        Assert.True(reader.FreezeFire);

        reader.Read(new Igi2PlayerInputState(0f, 0f, 0, 0), frame);
        Assert.False(reader.FreezeFire);

        reader.Read(new Igi2PlayerInputState(0f, 0f, Igi2PlayerAction.Fire, 0), frame);
        Assert.Equal(1f, frame[(int)Igi2HumanChannel.Fire]);
    }

    /// <summary>Walking is the walk toggle, inverted while modify-walk-run is held.</summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public void Running_is_the_toggle_inverted_by_the_modifier(bool walkToggle, bool modifier, bool running)
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        player.IsWalk = walkToggle;
        Igi2PlayerFakes.Feed(player, modifier ? Igi2PlayerAction.ModifyWalkRun : 0);

        player.PackSignal();

        Assert.Equal(running, player.IsRunning);
        Assert.Equal(!running, player.Signal.HasFlag(Igi2PlayerSignal.Walking));
    }

    /// <summary>Carried bits survive; channel bits are rebuilt each tick; the stand/lie toggle depends on posture.</summary>
    [Fact]
    public void The_handler_keeps_physics_bits_and_rebuilds_input_bits()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        player.Signal = Igi2PlayerSignal.Grounded | Igi2PlayerSignal.Forward;
        player.Pose = Igi2HumanPose.LyingFlat;
        player.PerfectShotTimer = 9;

        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.ToggleStandLie);
        player.PackSignal();

        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Grounded));
        Assert.False(player.Signal.HasFlag(Igi2PlayerSignal.Forward));
        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.StandUp));
        Assert.Equal(0, player.PerfectShotTimer);
    }

    /// <summary>Holding crouch keeps the bit but only the press and the release reset the perfect-shot timer.</summary>
    [Fact]
    public void Crouch_resets_the_timer_on_change_only()
    {
        Igi2HumanPlayer player = Igi2PlayerFakes.Player().Player;
        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.Crouch);
        player.PackSignal();

        player.PerfectShotTimer = 5;
        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.Crouch);
        player.PackSignal();

        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Crouch));
        Assert.Equal(5, player.PerfectShotTimer);
    }

    /// <summary>The laser designator raises its zoom on fire and clears the trigger.</summary>
    [Fact]
    public void Fire_on_the_designator_raises_its_zoom()
    {
        Igi2PlayerFakes.RecordingWeapon weapon = new() { ActiveCategory = Igi2HumanPlayer.LaserDesignatorCategory };
        Igi2HumanPlayer player = Igi2PlayerFakes.Player(weapon).Player;
        Igi2PlayerFakes.Feed(player, Igi2PlayerAction.Fire);

        player.PackSignal();

        Assert.Equal(["Fire", "ClearTrigger"], weapon.Calls);
        Assert.True(player.Signal.HasFlag(Igi2PlayerSignal.Fire));
    }

    private static float[] Read(Igi2PlayerInputState input)
    {
        float[] frame = new float[Igi2HumanChannels.Count];
        new Igi2PlayerInputReader().Read(input, frame);
        return frame;
    }
}
