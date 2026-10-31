using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>What detectors see, the level's water, and the world's other parts that are made ready as the level loads.</summary>
internal static class Igi2WorldSystems
{
    /// <summary>What detectors see, in the order the tree makes them (<c>AIDetection_ptDetectionQTaskList</c>): the cameras and the player.</summary>
    public static void Detectors(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Igi2LevelRun level = b.Level;
        if (!b.Services.TryGet(out Igi2GameMaterials? sightMaterials))
        {
            return;
        }

        Detection.Igi2DetectionWorld detection = new(player.CollisionWorld, sightMaterials)
        {
            FlowTicks = () => level.Ticks,
            LevelTick = () => level.LevelFlow?.Tick ?? 0,
        };

        // The level's alarm, for a camera's first sight: the last alarm control made (AlarmControl_CreateHandler's global).
        Interaction.Igi2AlarmControlTask? levelAlarm = b.Root.Descendants().OfType<Interaction.Igi2AlarmControlTask>().LastOrDefault();
        foreach (GameTask node in b.Root.Descendants())
        {
            if (node is Interaction.Igi2SecurityCameraTask camera)
            {
                camera.AiEvents = e => player.Ai?.SendEvent(e);
                camera.IsAlarmSounding = levelAlarm is null ? null : () => levelAlarm.IsAlarm;
                camera.Difficulty = player.Human.Difficulty;
                camera.Prepare();
                camera.Bind(detection, player.CollisionWorld.SolidsOf(Interaction.Igi2SecurityCameraTask.TypeName, camera.Id));
                player.Explosions?.AddTarget(camera);
                detection.Detectables.Add(camera);
            }
            else if (ReferenceEquals(node, b.PlayerTask))
            {
                detection.Detectables.Add(player.Human);
            }
            else if (node is Ai.Igi2HumanSoldierTask { Body: { } soldier })
            {
                detection.Detectables.Add(soldier);
                Igi2Gameplay.SeeThrough(soldier, detection);
            }
        }

        // HumanSoldier_UpdateInternalDataAfterLoadHandler: a living soldier of team 1 is no business of the detectors, but in
        // missions 21 and 22; it is listed again as it dies.
        int missionId = b.Request.World.Mission.Descriptor.Id;
        if (missionId is not (21 or 22))
        {
            foreach (Ai.Igi2HumanSoldierTask soldierTask in b.Root.Descendants().OfType<Ai.Igi2HumanSoldierTask>())
            {
                if (soldierTask.Body is { Team: 1, IsDead: false } living)
                {
                    detection.Remove(living);
                }
            }
        }

        b.Sight = detection;
        player.Explosions?.FlashSight = detection;
    }

    /// <summary>
    /// The level's water, in the order it is made (<c>Water_ptQTaskList</c>): its surface in the collision world, its depth asked by
    /// the humans' surroundings, its splashes made by bullets and blasts. The module outlives the level, as its stream does.
    /// </summary>
    public static void LevelWater(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Igi2LevelRun level = b.Level;
        Igi2Water water = b.Services.TryGet(out Igi2Water? sessionWater) ? sessionWater : new Igi2Water();
        water.Clear();
        water.FlowTicks = () => level.Ticks;
        foreach (WaterTask waterTask in b.Root.Descendants().OfType<WaterTask>())
        {
            Igi2WaterBody body = water.Add(waterTask);
            waterTask.Body = body;
            player.CollisionWorld.AddWater(body);
        }

        foreach (Igi2Human human in b.LevelHumans())
        {
            if (human.Surroundings is Igi2LevelSurroundings wet)
            {
                wet.Water = water;
            }
        }

        if (player.Explosions is { } underwater)
        {
            underwater.Water = water;
        }
    }

    /// <summary>A level's model LOD settings over the game's (<c>LocalModelLODSettingsContainer_UpdateInternalDataAfterLoadHandler</c>).</summary>
    public static void ModelLod(Igi2LevelBuild b)
    {
        if (b.Services.TryGet(out OpenIGI.Game.World.ModelLodTable? lodTable))
        {
            lodTable.BeginLevel();
            foreach (Igi2LocalModelLodSettingsContainerTask container in b.Root.Descendants().OfType<Igi2LocalModelLodSettingsContainerTask>())
            {
                lodTable.Load(container, container.Path, b.Request.World.Resources, b.Natives);
            }
        }
    }

    /// <summary>Flocks start their birds' swings from the stream every flock draws from.</summary>
    public static void Flocks(Igi2LevelBuild b)
    {
        if (b.Services.TryGet(out Igi2FlockRandom? flockRandom))
        {
            foreach (Igi2BirdFlockTask flock in b.Root.Descendants().OfType<Igi2BirdFlockTask>())
            {
                flock.Prepare(flockRandom);
            }
        }
    }

    /// <summary>Each linked cutscene camera notes where its link stands as the level is made (<c>EditCamera_UpdateInternalDataHandler</c>).</summary>
    public static void CutsceneCameraLinks(Igi2LevelBuild b)
    {
        Dictionary<int, GameTask> linkable = [];
        foreach (GameTask placed in b.Root.Descendants())
        {
            if (placed.Id != GameTask.NoId)
            {
                linkable.TryAdd(placed.Id, placed);
            }
        }

        foreach (Igi2EditCameraTask camera in b.Root.Descendants().OfType<Igi2EditCameraTask>().Where(static c => c.LinkTaskId >= 0))
        {
            camera.NoteLink(linkable.GetValueOrDefault(camera.LinkTaskId));
        }
    }

    /// <summary>Scripted overlays go up on the game screen.</summary>
    public static void ScreenOverlay(Igi2LevelBuild b)
    {
        if (b.Services.TryGet(out Igi2ScreenOverlay? screenOverlay))
        {
            screenOverlay.Hide();
            b.Player.ScreenOverlay = screenOverlay;
            foreach (Igi2OverlayHolderTask holder in b.Root.Descendants().OfType<Igi2OverlayHolderTask>())
            {
                holder.Screen = screenOverlay;
            }
        }
    }

    /// <summary>Spline path guides connect to the models they move once the level is loaded; those models move, and are drawn moving.</summary>
    public static void SplineGuides(Igi2LevelBuild b)
    {
        foreach (Paths.Igi2SplinePathGuideTask guide in b.Root.Descendants().OfType<Paths.Igi2SplinePathGuideTask>())
        {
            guide.Connect();
        }
    }

    /// <summary>The animations start from their first frame.</summary>
    public static void Animations(Igi2LevelBuild b)
    {
        foreach (Igi2AnimTask animation in b.Root.Descendants().OfType<Igi2AnimTask>())
        {
            animation.Begin();
        }
    }

    /// <summary>Smokes make their particle systems as they are made, each setting the module's stream back to its seed.</summary>
    public static void Smoke(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        if (player.Human.Surroundings is Igi2LevelSurroundings { Particles: { } particles })
        {
            Igi2SmokeRandom smokeRandom = player.Explosions?.SmokeRandom ?? new();
            foreach (Igi2SmokeTask smoke in b.Root.Descendants().OfType<Igi2SmokeTask>())
            {
                smoke.Particles = particles;
                smoke.Stream = smokeRandom;
                smoke.Players.Set(player.Human);
                player.SmokeRosters.Add(smoke.Players);
                smoke.Prepare();
            }
        }
    }

    /// <summary>The conveyor belts, by id, which the player rides.</summary>
    public static void Conveyors(Igi2LevelBuild b)
    {
        foreach (Interaction.Igi2ConveyorBeltTask belt in b.Root.Descendants().OfType<Interaction.Igi2ConveyorBeltTask>())
        {
            b.Player.Conveyors[belt.Id] = belt;
        }
    }

    /// <summary>The player's materials, the session's table.</summary>
    public static void PlayerMaterials(Igi2LevelBuild b)
    {
        if (b.Services.TryGet(out Igi2GameMaterials? materials))
        {
            b.Player.Materials = materials;
        }
    }

    /// <summary>
    /// The game is played at the active profile's difficulty (<c>Config_GetActiveDifficultyLevel</c>); here the difficulty is
    /// chosen before the level, so the profile takes it, and the squads' scripts read it back.
    /// </summary>
    public static void Difficulty(Igi2LevelBuild b)
    {
        if (b.Services.TryGet(out Config.Igi2GameConfig? gameConfig) && gameConfig.Active is { } profile)
        {
            profile.Difficulty = (int)b.Player.Human.Difficulty;
        }
    }
}
