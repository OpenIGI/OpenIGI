using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1's seven timed actions, its code pads and padlocks (<c>GenericTBA_RunHandler</c>, <c>igi2 0x4C2200</c>), and the
/// player working one (<c>HumanPlayer_InitStateActivateGenericTBA</c>, <c>0x4AE910</c>).
/// </summary>
public sealed class RetailIgi2GenericTbaTests
{
    /// <summary>The code pad of the large warehouse door, <c>GenericTBA_161</c>, active until it is finished.</summary>
    private const int WarehousePad = 161;

    /// <summary>A code pad always active, <c>GenericTBA_160</c>.</summary>
    private const int Pad = 160;

    /// <summary>An outer padlock, <c>GenericTBA_260</c>, explodable, which its script blows once it is finished.</summary>
    private const int Padlock = 260;

    /// <summary>The gate the outer padlock hangs on, <c>Door_3955</c>, which opens once the padlock is blown.</summary>
    private const int PadlockGate = 3955;

    /// <summary>All seven are typed and read with their authored parameters, active as they are made.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_timed_actions_are_read()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2GenericTbaTask[] actions = [.. m.Tree.OfType<Igi2GenericTbaTask>()];
        Assert.Equal([160, 161, 260, 261, 262, 3400, 3421], actions.Select(static a => a.Id).Order());

        Igi2GenericTbaTask pad = actions.Single(static a => a.Id == WarehousePad);
        Assert.Equal(("360_16_1", "360_17_1", 100f, false), (pad.Model, pad.DestroyedModel, pad.DamageScale, pad.IsExplodable));
        Assert.Equal(("!GenericTBA_161.isFinished", "tba_hack_code_pad", 4f), (pad.ActiveScript, pad.ActivateAnimation, pad.ActivateTime));
        Assert.Equal(("STATUSSCREEN:a_codepad.spr", "tba_codepad"), (pad.Sprite, pad.ActivateSound));
        Assert.Equal((true, false, false), (pad.IsActive, pad.IsFinished, pad.IsInUse));

        Igi2GenericTbaTask padlock = actions.Single(static a => a.Id == Padlock);
        Assert.Equal((true, 0.4f, 5, 0), (padlock.IsExplodable, padlock.DamageScale, padlock.ExplosionFragments, padlock.ExplosionFireballs));
        Assert.Equal(("pick_lock", 4f, "tba_picklock"), (padlock.ActivateAnimation, padlock.ActivateTime, padlock.ActivateSound));
        Assert.Equal("GenericTBA_260.isFinished", padlock.ExplosionScript);
    }

    /// <summary>
    /// A finish is made on the next tick, and is finished this tick for that tick only. One that is not explodable then wears
    /// its destroyed model, and exploded it is no longer active.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_finish_stands_for_a_tick_and_explodes_a_pad()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load();
        Igi2GenericTbaTask pad = Arm(m, WarehousePad, null);
        m.Run.Tick();
        Assert.True(pad.IsActive);

        pad.Finish();
        Assert.False(pad.IsFinished);

        // The level's clock ticks after the pads in mission 1's tree, so a finish is stamped with the tick before.
        int tick = m.Run.LevelFlow!.Tick;
        m.Run.Tick();
        Assert.Equal((true, true, true), (pad.IsFinished, pad.IsFinishedThisTick, pad.Explosive.IsExploded));
        Assert.Equal(("360_17_1", false), (pad.Explosive.Model, pad.IsActive));
        Assert.Equal(tick, pad.FinishedTick);
        Assert.Equal(1.0, Read(m, $"GenericTBA_{WarehousePad}.isFinishedThisTick"));

        m.Run.Tick();
        Assert.Equal((true, false), (pad.IsFinished, pad.IsFinishedThisTick));
        Assert.Equal((1.0, 0.0, 1.0), (Read(m, $"GenericTBA_{WarehousePad}.isFinished"), Read(m, $"GenericTBA_{WarehousePad}.isActive"), Read(m, $"GenericTBA_{WarehousePad}.isExploded")));
    }

    /// <summary>
    /// An explodable padlock ticks as an <c>ExplodeObject</c>: its finish is what its script blows it on, and with no fireballs it
    /// blows without a blast.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_finished_padlock_blows()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        DynamicTasks dynamic = new();
        Igi2GenericTbaTask padlock = Arm(m, Padlock, new Igi2ExplosionWorld(new IgiRandom(0x1234567u), m.Collision!, m.Materials, dynamic));
        m.Run.Tick();
        Assert.False(padlock.Explosive.IsExploded);

        padlock.Finish();
        m.Run.Tick();
        Assert.Equal((true, true, false), (padlock.IsFinished, padlock.Explosive.IsExploded, padlock.IsActive));
        Assert.Equal("306_03_1", padlock.Explosive.Model);
        Assert.Empty(dynamic.Tasks);
    }

    /// <summary>
    /// The outer padlock is a child of the gate it locks, and goes with it (<c>Door_SetPosition</c>'s
    /// <c>RigidDynCubeObj_UpdateAttachments</c>, then <c>ExplodeObject_UpdateAttachmentHandler</c>): blown, the gate swings open
    /// and the padlock stays where it sat on the gate, its solid with it and drawn there, rather than hanging where the gate was.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_padlock_goes_with_the_gate_it_locks()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2GenericTbaTask padlock = Arm(m, Padlock, null);
        Igi2DoorTask gate = m.Door(PadlockGate);
        Assert.Same(gate, padlock.Parent);
        Igi2Attachment.AttachChildren(gate, gate);
        Vector3d placed = padlock.Position;
        Vector3d onGate = OnGate(gate, placed);

        m.Run.Tick();
        padlock.Finish();
        for (int tick = 0; tick < 200 && !gate.IsOpen; tick++)
        {
            m.Run.Tick();
        }

        Assert.True(gate.IsOpen);
        Assert.True(padlock.Explosive.IsExploded);
        Assert.True(Distance(placed, padlock.Position) > 4096.0, $"the padlock stayed at {padlock.Position}");
        Assert.True(Distance(onGate, OnGate(gate, padlock.Position)) < 1.0);
        Assert.Equal(padlock.Position, padlock.CurrentPosition);
        Assert.True(Distance(padlock.Position, m.Collision!.SolidsOf(Igi2GenericTbaTask.TypeName, Padlock)[0].Position) < 1.0);

        // Drawn where the world scene placed it, as built, and moved from there to where the gate holds it now.
        WorldPlacement drawn = new(
            WorldScene.ToMetres(padlock.Built.Position), padlock.Built.ToMatrix(), default, null!, null!, Igi2GenericTbaTask.TypeName, null!)
        {
            LevelId = Padlock,
        };
        Assert.True(new Igi2MoverMotions([gate, padlock]).TryGetTransform(drawn, 1f, out Vector3d metres, out _));
        Assert.True(Distance(padlock.Position, WorldScene.ToUnits(metres)) < 1.0);
    }

    // A point in the gate's frame as it stands, as Igi2Attachment holds what the gate carries.
    private static Vector3d OnGate(Igi2DoorTask gate, Vector3d point)
    {
        IReadOnlyList<float> m = gate.CurrentOrientation;
        Vector3d d = point - gate.CurrentPosition;
        return new Vector3d(
            (m[0] * d.X) + (m[3] * d.Y) + (m[6] * d.Z),
            (m[1] * d.X) + (m[4] * d.Y) + (m[7] * d.Z),
            (m[2] * d.X) + (m[5] * d.Y) + (m[8] * d.Z));
    }

    private static double Distance(Vector3d a, Vector3d b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));

    /// <summary>
    /// The player facing an active code pad works it while the key is held: it kneels at the pad's second vertex facing it, the
    /// pad sounds and is in use, and once its four seconds are out the pad is finished on the next tick and the player stands.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_works_a_code_pad()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2GenericTbaTask pad = Arm(m, Pad, null);
        m.Run.Tick();
        Igi2TitlePlayer player = Before(m, pad);
        Assert.Same(pad, player.Human.EvaluateTarget);

        Hold(player, pressed: true);
        Igi2HumanPlayer human = player.Human;
        Assert.Equal(Igi2PlayerState.ActivateGenericTba, human.Locomotion.CurrentState);
        Assert.Equal((4 * 30, 3, true, true), (human.TimedActionTime, human.CameraMode, human.IsProgressBar, human.IsActivating));
        Assert.Contains(m.Sounds.Playing, static s => s.Name == "tba_codepad");
        Vector3d vertex = pad.MagicVertex;
        double reach = Math.Sqrt(Math.Pow(human.Position.X - vertex.X, 2) + Math.Pow(human.Position.Y - vertex.Y, 2));
        Assert.Equal(Igi2HumanPlayer.PickReach, reach, 1.0);

        for (int tick = 1; tick < 4 * 30; tick++)
        {
            Hold(player);
        }

        Assert.Equal(Igi2PlayerState.ActivateGenericTba, human.Locomotion.CurrentState);
        Hold(player);
        Assert.Equal(Igi2PlayerState.Stand, human.Locomotion.CurrentState);
        Assert.Equal((false, false, 1), (human.IsActivating, human.IsProgressBar, human.CameraMode));
        Assert.DoesNotContain(m.Sounds.Playing, static s => s.Name == "tba_codepad" && s.IsAlive);

        Assert.False(pad.IsFinished);
        m.Run.Tick();
        Assert.Equal((true, true), (pad.IsFinished, pad.IsInUse));
    }

    /// <summary>
    /// A player popped out of its work by a script stands free again: <c>HumanPlayer_GoToDefaultState</c> lets go of everything the
    /// timed action locked -- the action, both orientation locks, the interaction camera, the progress bar and the work's sound.
    /// location2/level6 and location3/level4 pop the player this way around the cutscenes that begin as it hacks a computer.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_script_pop_frees_the_player_from_its_work()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2GenericTbaTask pad = Arm(m, Pad, null);
        m.Run.Tick();
        Igi2TitlePlayer player = Before(m, pad);
        Hold(player, pressed: true);
        Igi2HumanPlayer human = player.Human;
        Assert.Equal((3, true, true), (human.CameraMode, human.IsLockAction, human.View.IsLockOrientation));

        human.GoToDefaultState();

        Assert.Equal(Igi2PlayerState.Stand, human.Locomotion.CurrentState);
        Assert.Equal(
            (1, false, false, false, false, false),
            (human.CameraMode, human.IsLockAction, human.View.IsLockOrientation, human.IsLockOrientation, human.IsProgressBar, human.IsActivating));
        Assert.DoesNotContain(m.Sounds.Playing, static s => s.Name == "tba_codepad" && s.IsAlive);
    }

    /// <summary>The key held over from the tick before does not start the work; the activation simply ends.</summary>
    [RequiresTitleDataFact("igi2")]
    public void A_held_key_does_not_start_the_work()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Igi2GenericTbaTask pad = Arm(m, Pad, null);
        m.Run.Tick();
        Igi2TitlePlayer player = Before(m, pad);

        // Held where there is nothing to work, and still held once the pad is found.
        player.Activatables.Remove(pad);
        Hold(player, pressed: true);
        player.Activatables.Add(pad);
        Hold(player);
        Assert.Same(pad, player.Human.EvaluateTarget);
        Hold(player);
        Assert.NotEqual(Igi2PlayerState.ActivateGenericTba, player.Human.Locomotion.CurrentState);
        Assert.False(player.Human.IsActivating);
    }

    private static Igi2GenericTbaTask Arm(RetailIgi2Mission1 m, int id, Igi2ExplosionWorld? blasts)
    {
        Igi2GenericTbaTask action = m.Tree.OfType<Igi2GenericTbaTask>().Single(a => a.Id == id);
        action.Explosive.Models = m.Level.ModelFor;
        action.Explosive.Explosions = blasts;
        if (m.Collision is { } world)
        {
            action.Explosive.Bind(world.SolidsOf(Igi2GenericTbaTask.TypeName, id));
        }

        blasts?.AddTarget(action);
        return action;
    }

    // The player 2400 units out in front of an action's vertex along its Y, facing it.
    private static Igi2TitlePlayer Before(RetailIgi2Mission1 m, Igi2GenericTbaTask action)
    {
        IReadOnlyList<float> f = action.CurrentOrientation;
        Vector3d ahead = new(f[1], f[4], f[7]);
        Vector3d at = action.MagicVertex + (ahead * 2400.0);
        float facing = MathF.Atan2((float)ahead.X, -(float)ahead.Y);
        return RetailIgi2Mission1.Stand(m.Collision!, m.Level, action, new Vector3d(at.X, at.Y, action.Position.Z), facing, m.Sounds);
    }

    private static void Hold(Igi2TitlePlayer player, bool pressed = false) =>
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate, Pressed = pressed ? PlayerAction.Activate : default });

    private static double Read(RetailIgi2Mission1 m, string expression) =>
        m.Run.Evaluate(Igi2LevelExpressions.Create(expression, "test", m.Run.Natives));
}
