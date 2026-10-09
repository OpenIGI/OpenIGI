using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

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
        Igi2DynamicTasks dynamic = new();
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
        m.Run.Evaluate(new Igi2LevelExpression(expression, "test", m.Run.Natives));
}
