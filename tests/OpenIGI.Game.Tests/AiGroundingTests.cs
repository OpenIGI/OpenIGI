using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
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
    [Fact]
    public void DeadBodyRetainsItsLastSupportedHeightWhenAnUpperFloorHasNoCollision()
    {
        Vector3d initial = new(100.0, 200.0, 300.0);
        AiSoldier soldier = new(graph: null, initial, facing: 0f, phase: 0, seed: 1);
        soldier.SetGroundQuery((double _, double _, double _, out double groundZ) =>
        {
            groundZ = 0.0;
            return false;
        });
        soldier.Kill();

        soldier.TickDeadBody(new Vector3f(20f, 30f, -80f), deathClipEnded: false);

        Assert.Equal(initial.Z, soldier.Position.Z);
        Assert.Equal(initial.X + 20.0, soldier.Position.X);
        Assert.Equal(initial.Y + 30.0, soldier.Position.Y);
    }

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
        (WorldScene scene, LoadedMission mission, _) = Build(missionId);
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

    /// <summary>
    /// The visible AI are drawn by the soldier renderer, which grounds a live soldier through its own
    /// probe (<c>SoldierRenderer.Stand</c>/<c>SnapToGround</c>). Its acceptance window must be at least
    /// as deep as the terrain query's own search (<see cref="TerrainHeightQuery.SearchDepthUnits"/>),
    /// or a soldier the query finds ground for is still left floating in the air. Level 4 authors several
    /// soldiers 2.3-3.1m above the terrain, larger than the old 2m window, which made them float; this
    /// locks in the invariant that every soldier the terrain query can settle actually settles.
    /// </summary>
    [RequiresGameDataTheory]
    [InlineData(4)]
    public void SoldierTasksTheTerrainQueryFindsAreSettleable(int missionId)
    {
        (_, LoadedMission mission, LevelTask levelRoot) = Build(missionId);
        TerrainData? terrain = mission.Terrain;
        if (terrain is null || !terrain.IsUsable)
        {
            return;
        }

        // Mirrors SoldierRenderer.Stand's probe: it starts just above the feet and accepts any surface
        // the terrain query can find, so the acceptance window must be no shallower than the search.
        const double probeAbove = 2048.0;
        double window = TerrainHeightQuery.SearchDepthUnits;
        List<string> floating = [];
        foreach (HumanSoldierTask soldier in Walk(levelRoot))
        {
            Vector3d p = soldier.Position;
            double probeTop = p.Z + probeAbove;
            if (!TerrainHeightQuery.TryGetGround(terrain, p.X, p.Y, probeTop, out TerrainGround ground))
            {
                continue; // no ground within the search depth — a rooftop or airborne actor, left alone
            }

            if (ground.Height < p.Z - window || ground.Height > probeTop)
            {
                floating.Add(
                    $"'{soldier.Name}' ground {ground.Height - p.Z:F1} units below feet at "
                    + $"({p.X:F0},{p.Y:F0},{p.Z:F0}) is outside the renderer's acceptance window.");
            }
        }

        Assert.True(floating.Count == 0, string.Join("\n", floating));
    }

    private static IEnumerable<HumanSoldierTask> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is HumanSoldierTask soldier)
            {
                yield return soldier;
            }

            foreach (HumanSoldierTask deeper in Walk(child))
            {
                yield return deeper;
            }
        }
    }

    private static bool IsCharacter(GameTask task) =>
        task is DeclaredTask declared
        && (declared.Type.Name == "HumanSoldier"
            || declared.Type.Name == "HumanSoldierFemale"
            || declared.Type.Name == "HumanPlayer");

    private static (WorldScene Scene, LoadedMission Mission, LevelTask Level) Build(int missionId)
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
        return (WorldScene.Build(mission, levelRoot, magic), mission, levelRoot);
    }
}
