using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Cabinets, as cabinet.c runs them and humanplayer.c searches them.</summary>
public sealed class RetailIgi2CabinetTests
{
    /// <summary>
    /// Mission 34's cabinets carry a drawer on an attach point; one searched with activate held to its end is searched, its drawer
    /// out and back on the clip's triggers, and the player stands again, the activation over.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_cabinet_held_to_the_end_of_its_search_is_searched()
    {
        Igi2Level level = Igi2Level.Find("location3", "level4");
        Igi2Session session = Igi2Session.Start();
        Igi2MagicObjTypes magic = session.Session.Services.Require<Igi2MagicObjTypes>();
        Igi2CabinetTask cabinet = All(level.Root).OfType<Igi2CabinetTask>().First();
        cabinet.Models = level.ModelFor;
        cabinet.Prepare(magic);
        Assert.NotNull(cabinet.DrawerModel);

        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner);
        (List<LoadedPack> packs, ResourceRegistry resources) = Igi2LevelScene.Packs(level);
        Igi2AnimationLibrary clips = Igi2AnimationLibrary.Load(packs, resources);
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = level.ModelFor(point.Model)!;
        Igi2PlayerTuning tuning = session.Require<Igi2PlayerTuning>();
        Igi2PlayerWeapons weapons = Igi2Gameplay.Arm(session.Session.Services, level.Root, tuning, clips)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(world, tuning.General, cabinet.Position, 0f, AiDifficulty.Easy, clips, body.BoneHierarchy, weapons);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        player.Human.AnimTriggers = session.Session.Services.Require<Igi2AnimTriggers>();
        Flow.Igi2LevelRun run = new(level.Root, new OpenIGI.Scripting.Runtime.NativeRegistry(), new Scripting.Igi2ScriptFunctions());

        player.Human.ScriptActivate(cabinet);
        Assert.Equal(Igi2PlayerState.ActivateCabinet, player.Human.Locomotion.CurrentState);
        Assert.Equal((int)(cabinet.SearchTime * 30f), player.Human.TimedActionTime);
        float widest = 0f;
        for (int tick = 0; tick < 300 && !cabinet.IsSearched; tick++)
        {
            player.Tick(RetailIgi2Mission1.Idle with { Held = PlayerAction.Activate });
            cabinet.Run(run);
            widest = Math.Max(widest, cabinet.DrawerOffset);
        }

        Assert.True(cabinet.IsSearched);
        Assert.Equal(Igi2CabinetTask.DrawerTravel, widest);
        Assert.NotEqual(Igi2PlayerState.ActivateCabinet, player.Human.Locomotion.CurrentState);
        Assert.False(player.Human.IsActivating);
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
