using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
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

    /// <summary>
    /// A round breaks a Destructable switch (<c>Switch_WeaponHitHandler</c>), whichever game fired it, and nothing finds it
    /// again. Its run copies <c>isDestroyed</c> to <c>isLastDestroyed</c> before <c>Switch_SetModel</c> compares them, so the
    /// break alone changes no model. One that is not Destructable takes the round and carries on.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_round_breaks_a_destructable_switch()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2SwitchTask power = m.Switch(PowerOn);
        Igi2SwitchTask lift = m.Switch(ElevatorUp);
        m.Run.Tick();

        WeaponHit round = new(power.Position, Vector3f.UnitZ, 1f, Vector3f.UnitX, null, null);
        lift.WeaponHit(round);
        power.Destructable = true;
        power.WeaponHit(round);
        m.Run.Tick();

        Assert.Equal((true, 2), (power.IsDestroyed, power.ModelSlot));
        Assert.False(lift.IsDestroyed);
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
        float[] f = EngineEuler.Rotation(power.Orientation.X, power.Orientation.Y, power.Orientation.Z);
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
        float[] f = EngineEuler.Rotation(power.Orientation.X, power.Orientation.Y, power.Orientation.Z);
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

    /// <summary>
    /// A switch its container has taken out of the world is not found: <c>Human_UpdateActivate</c> searches the world's stationary
    /// tree, which a container's remove handler takes its children off. The last mission keeps two rocket ignition buttons at one
    /// spot -- <c>Switch_3593</c> under "Launch Button - Game" and <c>Switch_1405</c>, always active, under "Launch Button - CutScene"
    /// -- and the outro's answered the player's press, so the launch never went.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_switch_out_of_the_world_is_not_found()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true, location: "location3", levelName: "LEVEL6");
        m.Tree.OfType<Game.Flow.EditVariableTask>().Single(static t => t.Id == 2112).Value = 1;
        for (int tick = 0; tick < 30; tick++)
        {
            m.Step();
        }

        Igi2SwitchTask game = m.Switch(3593), outro = m.Switch(1405);
        Assert.Equal(game.Position, outro.Position);
        Assert.False(OpenIGI.Game.World.ConditionalContainerGate.IsTaskActive(outro));

        float[] f = EngineEuler.Rotation(game.Orientation.X, game.Orientation.Y, game.Orientation.Z);
        Vector3d ahead = new(f[1], f[4], f[7]);
        Igi2TitlePlayer player = RetailIgi2Mission1.Stand(m.Collision!, m.Level, game, game.Position + (ahead * 1200.0), MathF.Atan2((float)ahead.X, (float)-ahead.Y));
        player.Activatables.Add(outro);

        IIgi2Activatable[] found = [.. player.Human.Surroundings.FindActivatables(game.Position, 2048f)];
        Assert.Contains(game, found);
        Assert.DoesNotContain(outro, found);
    }

    /// <summary>
    /// The last mission's launch, step by step as its script chains it: with the first three objectives done the outer code pad
    /// opens the bunker; the inner one seals it, and only once both doors have shut (6.7 s for the slower) does the shutter switch
    /// wake; the shutter open, the launch computer; then the two ignition buttons in turn, the second under the game's own
    /// container; and the third press starts the outro.
    /// </summary>
    /// <remarks>
    /// A player reported the shutter switch and the ignition buttons dead; their objective marker still stood on the inner pad.
    /// The chain is the script's own: no switch answers before the step ahead of it is done.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_last_missions_launch_goes_step_by_step()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true, location: "location3", levelName: "LEVEL6");
        Game.Flow.EditVariableTask Var(int id) => m.Tree.OfType<Game.Flow.EditVariableTask>().Single(t => t.Id == id);
        Igi2GenericTbaTask Pad(int id) => m.Tree.OfType<Igi2GenericTbaTask>().Single(t => t.Id == id);
        void Run(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                m.Step();
            }
        }

        Igi2SwitchTask shutter = m.Switch(1407), computer = m.Switch(2541), ignition = m.Switch(3444), launch = m.Switch(3593);
        Var(2112).Value = 1;
        Var(3897).Value = 1;
        Run(30);
        Assert.Equal((true, true), (Pad(3924).IsActive, m.Door(2708).IsClosed));

        Pad(3924).Finish();
        Run(250);
        Assert.Equal((true, true, false), (m.Door(2708).IsOpen, m.Door(2709).IsOpen, shutter.IsActive));

        Pad(4062).Finish();
        Run(150);
        Assert.False(shutter.IsActive);
        Run(100);
        Assert.Equal((true, true, true), (m.Door(2708).IsClosed, m.Door(2709).IsClosed, shutter.IsActive));

        shutter.Press(0);
        Run(250);
        Assert.Equal((true, true), (m.Door(1708).IsOpen, computer.IsActive));

        computer.Press(0);
        Run(10);
        Assert.Equal((1, true), (Var(3995).Value, ignition.IsActive));

        ignition.Press(0);
        Run(10);
        Assert.Equal((2, true, true), (Var(3995).Value, launch.IsActive, OpenIGI.Game.World.ConditionalContainerGate.IsTaskActive(launch)));

        launch.Press(0);
        Run(10);
        Assert.Equal(3, Var(3995).Value);
        Assert.True(OpenIGI.Game.World.ConditionalContainerGate.IsTaskActive(m.Tree.Single(static t => t.Id == 3914)));
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
