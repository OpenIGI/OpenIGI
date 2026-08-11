using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Verifies the retail behaviour that grounds human character models (AI soldiers and the player body)
/// onto the terrain when a level loads. The original engine's level verifier exempts these from exact
/// position cross-reference because it terrain-snaps them at load, so a character authored a little above
/// the surface must settle onto it rather than float. Level 4 placed every soldier about a metre up in the
/// air because the snap was missing; this test locks the grounded positions in.
/// </summary>
public sealed class AiGroundingTests
{
    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void CharacterPlacementsSettleOnTheTerrain(int missionId)
    {
        (WorldScene scene, LoadedMission mission) = Build(missionId);
        TerrainData? terrain = mission.Terrain;
        if (terrain is null || !terrain.IsUsable)
        {
            return; // no drawable terrain to settle onto — nothing to assert
        }

        // A placement that is an attached child (such as a character riding a vehicle) inherits the
        // parent's world transform and sits on the parent, so it is exempt from grounding onto the terrain.

        double toleranceMetres = 0.5;
        List<string> failures = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (!IsCharacter(placement.Owner) || placement.IsAttached)
            {
                continue;
            }

            Vector3d pos = placement.Position;
            double x = pos.X * WorldScene.UnitsPerMetre;
            double y = pos.Y * WorldScene.UnitsPerMetre;
            double z = pos.Z * WorldScene.UnitsPerMetre;

            // Only placements with ground within the search depth settle onto it; a character far above
            // the terrain has none and is legitimately off it.
            if (!TerrainHeightQuery.TryGetGround(terrain, x, y, z, out TerrainGround ground))
            {
                continue;
            }

            double delta = pos.Z - (ground.Height / WorldScene.UnitsPerMetre);
            if (Math.Abs(delta) > toleranceMetres)
            {
                failures.Add(
                    $"{placement.Instance.Name} ({placement.Owner.Type.Name}) floats {delta:F2}m above ground"
                    + $" at Z={pos.Z:F2}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)));
    }

    private static bool IsCharacter(GameTask task) =>
        task is DeclaredTask declared
        && (declared.Type.Name == "HumanSoldier"
            || declared.Type.Name == "HumanSoldierFemale"
            || declared.Type.Name == "HumanPlayer");

    private static (WorldScene Scene, LoadedMission Mission) Build(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        TerrainLoader terrainLoader = new(resources, loop.Natives);
        LoadedMission mission = new MissionLoader(
            registry, fileSystem, resources, levelLoader, terrainLoader: terrainLoader).Load(missionId);
        return (WorldScene.Build(mission, levelRoot, magic), mission);
    }
}
