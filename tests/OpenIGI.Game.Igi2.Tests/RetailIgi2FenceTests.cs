using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Fences, as fence.c runs them and humanplayer.c is shocked by and climbs them.</summary>
public sealed class RetailIgi2FenceTests
{
    /// <summary>
    /// The fences of missions 14 and 15 have no magic vertices: their ends are both at their origin, so none can be climbed or shocks.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_fences_of_missions_14_and_15_have_no_ends()
    {
        Igi2Level level = Igi2Level.Find("location1", "level4");
        Igi2FenceTask fence = All(level.Root).OfType<Igi2FenceTask>().First();
        fence.Models = level.ModelFor;
        fence.Prepare();
        Assert.False(level.ModelFor(fence.Model)!.TryGetMagicVertex(0, out _));
    }

    /// <summary>
    /// A live fence hums and shocks the player against it: the chest takes the damage scale times its multiplier, once in 45 ticks;
    /// dead again, its hum goes.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_live_fence_shocks_the_player_against_it()
    {
        Igi2Level level = Igi2Level.Find("location1", "level6");
        Igi2FenceTask fence = All(level.Root).OfType<Igi2FenceTask>().First(static f => f.Model == "370_02_1");
        fence.Models = level.ModelFor;
        fence.ElectricExpression = "1";
        fence.Prepare();
        Igi2LevelRun run = Run();
        fence.Run(run);
        Assert.True(fence.IsElectric);
        Assert.NotNull(fence.Hum);

        (Igi2HumanPlayer player, _, _) = Igi2PlayerFakes.OnFloor();
        player.General = player.General with { ElectricFenceDamageScale = 10f };
        player.Damage.Clear([.. Enumerable.Repeat(1f, Igi2HumanDamage.PartCount)]);
        Vector3d middle = Middle(fence, level, up: 4096.0);
        fence.Notify(player, middle, 0f);
        Assert.Equal(10f, player.Damage.Damage);
        Assert.Equal(Igi2DamagePart.Chest, player.Damage.LastPart);
        Assert.Equal(Igi2HumanPlayer.ElectricTimeout, player.ElectricDamageTimeout);
        fence.Notify(player, middle, 0f);
        Assert.Equal(10f, player.Damage.Damage);

        fence.ElectricExpression = "0";
        Igi2FenceTask dead = new() { Position = fence.Position, Gamma = fence.Gamma, Model = fence.Model, Models = level.ModelFor, ElectricExpression = "0" };
        dead.Prepare();
        dead.Run(run);
        Assert.False(dead.IsElectric);
        Assert.Null(dead.Hum);
    }

    /// <summary>
    /// Before a dead fence, facing it, the player that presses activate gets onto its line and climbs over, the activate timer counting,
    /// and lands on its far side off it, collision and camera back.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_player_climbs_over_a_dead_fence()
    {
        Igi2Level level = Igi2Level.Find("location1", "level6");
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
        Igi2FenceTask fence = All(level.Root).OfType<Igi2FenceTask>().First(static f => f.IsClimbable && f.Model == "370_05_1");
        fence.Models = level.ModelFor;
        fence.Prepare();
        IReadOnlyList<float> f = fence.Frame;
        Vector3d middle = Middle(fence, level, up: 0.0);
        Vector3d at = new(middle.X - (f[1] * 2048.0), middle.Y - (f[4] * 2048.0), middle.Z + 4096.0);
        float facing = MathF.Atan2(-f[1], f[4]);

        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = level.ModelFor(point.Model)!;
        Igi2Session session = Igi2Session.Start();
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, level.Root, tuning, clips)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, tuning.General, at, facing, AiDifficulty.Easy, clips, body.BoneHierarchy, weapons);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        player.Activatables.Add(fence);
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle);
        }

        Assert.Same(fence, player.Human.EvaluateTarget);
        player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate, Pressed = PlayerAction.Activate });
        Assert.True(player.Human.IsClimbFence);
        Assert.Equal(Igi2PlayerState.ClimbFence, player.Human.Locomotion.CurrentState);
        Assert.Equal(Igi2HumanPlayer.FenceClimbTicks, player.Human.TimedActionTime);
        for (int tick = 0; tick < 600 && player.Human.IsClimbFence; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle);
        }

        Assert.False(player.Human.IsClimbFence);
        Assert.False(player.Human.IsDisableCollision);
        Assert.Equal(1, player.Human.CameraMode);
        Vector3d end = player.Human.Position;
        double across = (f[1] * (end.X - fence.Position.X)) + (f[4] * (end.Y - fence.Position.Y));
        Assert.True(across > 0.0, $"landed {across} across the fence");
    }

    // A point over the middle of a fence, between its two ends, raised.
    private static Vector3d Middle(Igi2FenceTask fence, Igi2Level level, double up)
    {
        Formats.World.MefModel model = level.ModelFor(fence.Model)!;
        Assert.True(model.TryGetMagicVertex(0, out Vector3f left));
        Assert.True(model.TryGetMagicVertex(1, out Vector3f right));
        IReadOnlyList<float> f = fence.Frame;
        double x = (left.X + right.X) * 0.5;
        return new Vector3d(fence.Position.X + (f[0] * x), fence.Position.Y + (f[3] * x), fence.Position.Z + up);
    }

    private static Igi2LevelRun Run()
    {
        TaskTypeRegistry types = new();
        TaskTree tree = new(types);
        GameTask root = tree.New(null, types.Register<ContainerTask>("Container"));
        return new(root, new NativeRegistry(), new Igi2ScriptFunctions());
    }

    private static IEnumerable<GameTask> All(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask inner in All(child))
            {
                yield return inner;
            }
        }
    }
}
