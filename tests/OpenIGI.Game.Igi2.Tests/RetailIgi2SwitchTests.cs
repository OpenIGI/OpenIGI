using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's switches (<c>Switch_RunHandler</c>, <c>igi2 0x4DA760</c>), the player's view finding one
/// (<c>Human_UpdateActivate</c>, <c>0x500D10</c>) and pressing it (<c>Switch_Pressed</c>, <c>0x4DB3E0</c>).
/// </summary>
public sealed class RetailIgi2SwitchTests
{
    /// <summary>The shed's "Power On" button, <c>Switch_3429</c>.</summary>
    private const int PowerOn = 3429;

    /// <summary>The lift's "Elevator Up" button beside it, <c>Switch_270</c>, active only once the power is on.</summary>
    private const int ElevatorUp = 270;

    /// <summary><c>Real32_Acos</c> (<c>igi2 0x5612C0</c>): exact at its ends and the middle, and near the true arc cosine between.</summary>
    [Fact]
    public void The_arc_cosine_is_retails_polynomial()
    {
        Assert.Equal(0f, Igi2Pose.Acos(1f), 6);
        Assert.Equal(0f, Igi2Pose.Acos(1.5f), 6);
        Assert.Equal(BitConverter.Int32BitsToSingle(0x3FC90FDB), Igi2Pose.Acos(0f));
        Assert.Equal(BitConverter.Int32BitsToSingle(0x40490FDB), Igi2Pose.Acos(-1f), 6);
        foreach (float x in new[] { -0.9f, -0.5f, 0.3f, 0.70710677f, 0.8f })
        {
            Assert.Equal(MathF.Acos(x), Igi2Pose.Acos(x), 4);
        }

        Assert.Equal(0f, Igi2Pose.CalcGamma(0.0, 0.0));
        Assert.Equal(Igi2Pose.Acos(0.6f), Igi2Pose.CalcGamma(-0.8, 0.6), 6);
        Assert.Equal(-Igi2Pose.Acos(0.6f), Igi2Pose.CalcGamma(0.8, 0.6), 6);
    }

    /// <summary>
    /// The 19 switches of mission 1 are switches: each starts as its <c>Initial on</c> says, wearing that state's model, and
    /// is still in the world scene.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void Mission1_has_its_switches()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Assert.Equal(19, m.Switches.Count);
        Igi2SwitchTask power = m.Switch(PowerOn);
        Assert.Equal((false, 2, "337_12_1", "button_1"), (power.IsOn, power.ModelSlot, power.Model, power.Sound));
        Assert.Contains(m.Level.Scene.Placements, p => p.LevelId == PowerOn && p.Model == "337_12_1");
    }

    /// <summary>
    /// Pressed, a switch flips on the tick it runs held, sounds, and lets go once the level's tick is more than three past
    /// the press; its model follows. "Power On" goes inactive once on, and the lift's button wakes, as their scripts say.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_pressed_switch_flips_and_lets_go()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2SwitchTask power = m.Switch(PowerOn);
        Igi2SwitchTask lift = m.Switch(ElevatorUp);
        m.Run.Tick();
        Assert.Equal((true, false), (power.IsActive, lift.IsActive));

        power.Press(0);
        m.Run.Tick();
        Assert.Equal((true, true, 1), (power.IsOn, power.IsPressed, power.ModelSlot));
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "button_1");

        m.Run.Tick();
        Assert.Equal((false, true), (power.IsActive, lift.IsActive));

        int ticks = 1;
        while (power.IsPressed)
        {
            m.Run.Tick();
            ticks++;
        }

        Assert.Equal(4, ticks);
        Assert.Equal((true, 0, "337_13_1"), (power.IsOn, power.ModelSlot, power.Model));
    }

    /// <summary>The generator in the shed starts up the tick its power button is pressed, and loops the tick after.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_power_button_starts_the_generator()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2SoundGeneratorTask generator = m.Tree.OfType<Igi2SoundGeneratorTask>().Single(static g => g.Id == 3510);
        m.Run.Tick();
        Assert.False(generator.IsPlaying);

        m.Switch(PowerOn).Press(0);
        m.Run.Tick();
        Assert.Equal((true, "11_gen_start"), (generator.IsPlaying, generator.Sound));
        m.Run.Tick();
        Assert.Equal("11_gen_loop", generator.Sound);
    }

    /// <summary>
    /// A view finds a switch it faces from in front, but not from behind, not from far to one side, and not once the switch
    /// is inactive; the one found is the one nearest the line of sight.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_view_finds_the_switch_in_front_of_it()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2SwitchTask power = m.Switch(PowerOn);
        m.Run.Tick();
        float[] f = Igi2Pose.Rotation(power.Orientation.X, power.Orientation.Y, power.Orientation.Z);
        Vector3d across = new(f[0], f[3], f[6]);
        Vector3d ahead = new(f[1], f[4], f[7]);

        // Facing it: the view's own right and forward turned half round from the switch's.
        float[] facing = [-f[0], -f[1], f[2], -f[3], -f[4], f[5], -f[6], -f[7], f[8]];
        Assert.Same(power, Find(power, power.Position + (ahead * 2000.0), facing));
        Assert.Null(Find(power, power.Position - (ahead * 2000.0), f));
        Assert.Null(Find(power, power.Position + (ahead * 2000.0) + (across * 4000.0), facing));

        power.Press(0);
        m.Run.Tick();
        m.Run.Tick();
        Assert.Null(Find(power, power.Position + (ahead * 2000.0), facing));
    }

    /// <summary>
    /// The player, standing in front of the power button and facing it, finds it and presses it with the activate key; facing
    /// away, it finds nothing.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_presses_the_button_it_faces()
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Igi2SwitchTask power = level.Root.Children.SelectMany(RetailIgi2Mission1.All).OfType<Igi2SwitchTask>().Single(static s => s.Id == PowerOn);
        power.Models = level.ModelFor;
        float[] f = Igi2Pose.Rotation(power.Orientation.X, power.Orientation.Y, power.Orientation.Z);
        Vector3d ahead = new(f[1], f[4], f[7]);
        float facing = MathF.Atan2((float)ahead.X, (float)-ahead.Y);

        Igi2TitlePlayer player = Stand(level, power, power.Position + (ahead * 1200.0), facing);
        Assert.Same(power, player.Human.EvaluateTarget);
        Assert.False(power.IsPressed);

        player.Tick(Idle with { Held = PlayerAction.Activate, Pressed = PlayerAction.Activate });
        player.Tick(Idle with { Held = PlayerAction.Activate });
        Assert.True(power.IsPressed);
        Assert.True(player.Human.IsCheckActivateButton);

        Igi2TitlePlayer away = Stand(level, power, power.Position + (ahead * 1200.0), facing + MathF.PI);
        Assert.Null(away.Human.EvaluateTarget);
    }

    private static TitlePlayerInput Idle => RetailIgi2Mission1.Idle;

    private static Igi2TitlePlayer Stand(Igi2Level level, Igi2SwitchTask button, Vector3d at, float gamma) =>
        RetailIgi2Mission1.Stand(Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner), level, button, at, gamma);

    private static IIgi2Activatable? Find(Igi2SwitchTask target, Vector3d view, float[] orientation)
    {
        Igi2EvaluateActivate context = new(view, orientation, isPlayer: true);
        target.EvaluateActivate(context);
        return context.Best;
    }
}
