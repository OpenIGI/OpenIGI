using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Player;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>What the level's scripts reach, and the minds of its soldiers.</summary>
internal static class Igi2ScriptSystems
{
    /// <summary>The level's tasks by id, a human standing for its task: what a script or a minefield names.</summary>
    public static void PlacedTasks(Igi2LevelBuild b)
    {
        foreach (GameTask node in b.Root.Descendants())
        {
            if (node.Id != GameTask.NoId)
            {
                b.PlacedById.TryAdd(node.Id, node switch
                {
                    Ai.Igi2HumanSoldierTask { Body: { } body } => body,
                    Igi2HumanPlayerTask => b.Player.Human,
                    _ => node,
                });
            }
        }
    }

    /// <summary>The natives scripts reach the player and the humans through answer for this level.</summary>
    public static void LevelNatives(Igi2LevelBuild b)
    {
        if (b.Services.TryGet(out Igi2LevelNatives? levelNatives))
        {
            Dictionary<int, object> placed = b.PlacedById;
            levelNatives.Bind(b.Player.Human, id => placed.GetValueOrDefault(id), b.Root.Children.OfType<Igi2LevelFlowTask>().FirstOrDefault());

            // The player's designator marks its target where the level's scripts read it (LaserDesignator_GetTicksHeldOnTask).
            b.Player.Weapons?.Firing.LaserDesignator = levelNatives.LaserDesignator;
        }
    }

    /// <summary>
    /// The level's AI over its soldiers' minds; what the humans do that the AI hears reaches the minds from now on, the cars' and
    /// trains' kill boxes are tested against everyone, and each car's driver finds its car and its graph.
    /// </summary>
    public static void Ai(Igi2LevelBuild b)
    {
        Igi2TitlePlayer player = b.Player;
        Igi2LevelRun level = b.Level;
        if (player.Soldiers is null)
        {
            return;
        }

        b.Services.TryGet(out OpenIGI.Game.Igi2.Ai.Igi2HumanAiConfig? config);
        b.Services.TryGet(out Weapons.Igi2HumanWeapons? armoury);
        List<Igi2Human> everyone = b.LevelHumans();
        List<Audio.Igi2AmbientAreaTask> ambience = b.Ambience;
        Func<Vector3d, float> noise = point =>
        {
            // AmbientArea_GetNoiseLevelAtPosition: the loudest area whose box and falloff hold the point, at most one.
            float loudest = 0f;
            foreach (Audio.Igi2AmbientAreaTask area in ambience)
            {
                float heard = area.NoiseAt(point);
                if (loudest < heard)
                {
                    loudest = heard >= 1f ? 1f : heard;
                }
            }

            return loudest;
        };
        player.Ai = OpenIGI.Game.Igi2.Ai.Runtime.Igi2LevelAi.Build(new OpenIGI.Game.Igi2.Ai.Runtime.Igi2LevelAiSetup(
            b.Root,
            b.Request.World.Resources,
            b.Natives,
            b.Functions,
            player.Human,
            everyone,
            player.Human.Random,
            () => level.LevelFlow?.Tick ?? 0)
        {
            Detection = b.Sight,
            FireTaskTypes = armoury?.FireTaskTypes,
            Config = config,
            Difficulty = (int)player.Human.Difficulty,
            Logger = b.Request.World.Logger,
            World = b.SessionAi,
            NoiseAt = noise,

            // Graph_UpdateInternalDataAfterLoadHandler: each node's room, asked as a human asks, with tolerance.
            SectorAt = b.SectorLocator is { } locator ? point => locator.SectorAt(point, lift: true) : null,
        });

        Igi2Gameplay.HearAiEvents(everyone, () => player.Ai);

        foreach (Vehicles.Igi2CarTask car in player.Cars)
        {
            car.Humans = everyone;
        }

        foreach (Vehicles.Igi2TrainTask train in b.Root.Descendants().OfType<Vehicles.Igi2TrainTask>())
        {
            train.Humans = everyone;
        }

        // CarAI_UpdateInternalDataAfterLoadHandler: each driver finds its car and its graph, and sets the car back to its start.
        foreach (Vehicles.Igi2CarAiTask driver in b.Root.Descendants().OfType<Vehicles.Igi2CarAiTask>())
        {
            driver.Prepare(player.Ai);
        }
    }
}
