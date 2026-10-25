using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using Xunit;
using Fpu = OpenIGI.Core.Numerics.X87Single;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The player's tick, <c>HumanPlayer_Update</c> (<c>igi2 0x4A1EC0</c>): the parts in the order they run.</summary>
public sealed class Igi2PlayerTickTests
{
    /// <summary>
    /// How well detectors see the player follows the light it stands in, halved, and its stance: four fifths of it crouched, three
    /// fifths lying; a shot that has just lit it shows it fully (<c>HumanPlayer_UpdateVisibilityFactor</c>).
    /// </summary>
    [Fact]
    public void The_player_is_seen_by_its_light_and_its_stance()
    {
        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.Player();
        player.Lightmap = 1.2f;
        player.RunDelayed();
        Assert.Equal(0.6f, player.VisibilityFactor);

        player.Pose = Igi2HumanPose.Crouching;
        player.RunDelayed();
        Assert.Equal(((Fpu)0.6f * 0.800000011920929).F, player.VisibilityFactor);

        player.Pose = Igi2HumanPose.LyingFlat;
        player.IgnoreLightEffectOnVisibilityTimer = 5;
        player.RunDelayed();
        Assert.Equal(((Fpu)1f * 0.6000000238418579).F, player.VisibilityFactor);
    }

    /// <summary>
    /// Running rattles the kit and builds fatigue a six-hundredth a tick; past seven tenths the player pants, and once it stops
    /// the pant ends on a last breath (<c>HumanPlayer_MaintainRunningSound</c>, <c>HumanPlayer_MaintainFatigue</c>).
    /// </summary>
    [Fact]
    public void A_long_run_leaves_the_player_panting()
    {
        (Igi2HumanPlayer player, _, Igi2PlayerFakes.FakeSurroundings surroundings) = OnFloor();
        for (int tick = 0; tick < 500; tick++)
        {
            Tick(player, Igi2PlayerAction.MoveForwards);
            player.RunDelayed();
        }

        Assert.Contains(surroundings.Started, static s => s.Name == Igi2HumanPlayer.RunningSound && s.IsAlive);
        Assert.InRange(player.Fatigue, 0.7f, 0.85f);
        Igi2PlayerFakes.FakeSound pant = Assert.Single(surroundings.Started, static s => s.Name == Igi2HumanPlayer.BreatheLoopSound);

        for (int tick = 0; tick < 30; tick++)
        {
            Tick(player, Igi2PlayerAction.None);
            player.RunDelayed();
        }

        Assert.True(pant.Deleted);
        Assert.DoesNotContain(surroundings.Started, static s => s.Name == Igi2HumanPlayer.RunningSound && s.IsAlive);
        Assert.Contains(surroundings.Started, static s => s.Name == Igi2HumanPlayer.BreatheStopSound);
        Assert.True(player.Fatigue < 0.7f);
    }

    /// <summary>
    /// Holding the peek key locks the body and leans the view across with the mouse, no further than the peek's reach; letting go
    /// takes the lean back and returns to standing and to idle (<c>HumanPlayer_RunStatePeek</c>, <c>HumanPlayer_RunActionPeek</c>).
    /// </summary>
    [Fact]
    public void The_peek_key_leans_the_view_out_and_back()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();
        player.General = player.General with { PeekOffsetX = 2000f, PeekOffsetZ = 1500f, PeekTime = 10 };
        Tick(player, Igi2PlayerAction.None);
        for (int tick = 0; tick < 4; tick++)
        {
            Tick(player, Igi2PlayerAction.Peek, mouseX: 0.02f);
        }

        Assert.Equal(Igi2PlayerState.Peek, player.Locomotion.CurrentState);
        Assert.Equal(Igi2PlayerActionState.Peek, player.Action.CurrentState);
        Assert.True(player.View.PeekOffset.X > 0f);
        Assert.True(player.View.PeekOffset.X <= 2000f);
        Assert.Equal(0f, player.Gamma);

        for (int tick = 0; tick < 40; tick++)
        {
            Tick(player, Igi2PlayerAction.Peek, mouseX: 0.02f);
        }

        Assert.Equal(2000f, player.View.PeekOffset.X);

        Tick(player, Igi2PlayerAction.None);
        Assert.Equal(default, player.View.PeekOffset);
        Assert.Equal(Igi2PlayerActionState.Idle, player.Action.CurrentState);
        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
    }

    /// <summary>
    /// The map key opens the computer on the map with the player held still; the objectives' key turns to them; that key again
    /// closes it, and the player stands and idles again (<c>HumanPlayer_RunStateComputer</c>).
    /// </summary>
    [Fact]
    public void A_computer_key_opens_switches_and_closes_the_computer()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();
        Igi2Computer computer = new();
        player.Computer = computer;
        Tick(player, Igi2PlayerAction.None);
        Tick(player, Igi2PlayerAction.ComputerMap);
        Tick(player, Igi2PlayerAction.None);
        computer.Run();

        Assert.True(computer.IsOpen);
        Assert.Equal(Igi2ComputerMenu.Map, computer.ActiveMenu);
        Assert.Equal(Igi2PlayerState.Computer, player.Locomotion.CurrentState);
        Assert.Equal(Igi2PlayerActionState.Computer, player.Action.CurrentState);
        Assert.True(player.View.IsLockOrientation);

        Tick(player, Igi2PlayerAction.ComputerObjectives);
        computer.Run();
        Assert.True(computer.IsOpen);
        Assert.Equal(Igi2ComputerMenu.Objectives, computer.ActiveMenu);

        Tick(player, Igi2PlayerAction.None);
        Tick(player, Igi2PlayerAction.ComputerObjectives);
        Assert.False(computer.IsOpen);
        Assert.Equal(Igi2PlayerState.Stand, player.Locomotion.CurrentState);
        Assert.False(player.View.IsLockOrientation);

        Tick(player, Igi2PlayerAction.None);
        Assert.Equal(Igi2PlayerActionState.Idle, player.Action.CurrentState);
    }

    /// <summary>
    /// The mission score's tally: a hit counts on anything but a human of the player's own side, a kill only of a human of another
    /// (<c>Human_HitByYouHandler</c>, <c>Human_KilledByYouHandler</c>).
    /// </summary>
    [Fact]
    public void Hits_and_kills_count_against_other_sides()
    {
        (Igi2HumanPlayer player, _, _) = OnFloor();
        (Igi2HumanPlayer friend, _, _) = OnFloor();
        (Igi2HumanPlayer enemy, _, _) = OnFloor();
        player.Team = 0;
        friend.Team = 0;
        enemy.Team = 1;

        player.HitByYou(new object());
        player.HitByYou(friend);
        player.HitByYou(enemy);
        player.KilledByYou(friend);
        player.KilledByYou(enemy);
        player.KilledByYou(new object());

        Assert.Equal(2, player.MissionHits);
        Assert.Equal(1, player.MissionKills);
    }

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
