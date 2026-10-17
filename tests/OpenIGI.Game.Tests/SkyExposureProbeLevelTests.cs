using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Runs the sky-exposure probe over a retail level, to check that what it measures on a shed built out
/// of planes is also what it measures on the buildings the game actually ships.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SkyExposureProbeTests"/> pins the rule; this pins that the rule survives contact with
/// real geometry — meshes at angles to the world axes, roofs of every thickness including none,
/// buildings whose walls do not meet, and the open terrain between them. The claim is a population
/// one, because any single column of any level might be a doorway, a hole in a roof or a mistake in
/// the original data: <b>ground under a roof must measure much less open sky than ground that has
/// none</b>, and the two distributions must barely overlap.
/// </para>
/// <para>
/// Skipped without a retail installation, like every other test that needs one.
/// </para>
/// </remarks>
public sealed class SkyExposureProbeLevelTests
{
    private const double Units = WorldScene.UnitsPerMetre;

    /// <summary>How high above the floor the ear stands, in metres.</summary>
    private const double EarMetres = 1.7;

    [RequiresGameDataFact]
    public void GroundUnderARoofMeasuresFarLessOpenSkyThanGroundWithout()
    {
        WorldScene scene = Build(1);
        ShelterHeightField field = ShelterHeightField.Build(scene.Placements);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);
        Assert.False(field.IsEmpty);

        WorldRayCast cast = solids.TryCastWithDoorsShut;

        List<float> indoors = [];
        List<float> outdoors = [];

        // A coarse sweep of the built-up part of the level. Every twenty-third cell on both axes keeps
        // this to a few hundred probes -- each of which is sixty-four traces -- rather than millions.
        //
        // Each column is measured TWICE: once standing on top of whatever it holds, and once standing
        // underneath it where there is room to. Comparing a roof's two sides is the sharpest contrast
        // the level offers and needs no terrain, which the placed-object collision does not carry.
        for (int x = 0; x < field.Width && (indoors.Count < 60 || outdoors.Count < 60); x += 23)
        {
            for (int y = 0; y < field.Depth && (indoors.Count < 60 || outdoors.Count < 60); y += 23)
            {
                double worldX = field.OriginX + ((x + 0.5) * field.CellMetres);
                double worldY = field.OriginY + ((y + 0.5) * field.CellMetres);

                float surface = field.SurfaceOver(worldX, worldY);
                if (float.IsNegativeInfinity(surface))
                {
                    continue;
                }

                // On top of the column: a roof, a road or a container lid, and open sky either way.
                if (outdoors.Count < 60)
                {
                    outdoors.Add(Measure(field, cast, new Vector3d(worldX, worldY, surface + EarMetres)));
                }

                if (indoors.Count >= 60
                    || !TryRoofedSpot(solids, field, worldX, worldY, surface, out Vector3d sheltered))
                {
                    continue;
                }

                indoors.Add(Measure(field, cast, sheltered));
            }
        }

        Assert.True(indoors.Count >= 20, $"only {indoors.Count} roofed spots were found to stand in");
        Assert.True(outdoors.Count >= 20, $"only {outdoors.Count} open spots were found to stand in");

        float indoorMedian = Median(indoors);
        float outdoorMedian = Median(outdoors);

        // Open ground hears nearly the whole sky. Anything much below this would mean the terrain the
        // player walks on is being read as a roof over their head.
        Assert.True(outdoorMedian > 0.8f, $"open ground measured only {outdoorMedian:0.000} of the sky");

        // Under a roof it must be a small fraction of that -- not merely lower. This is the number the
        // whole mix is derived from: a bed that only halved indoors would sound like a quieter
        // outdoors, which is exactly the failure the system exists to avoid.
        Assert.True(
            indoorMedian < outdoorMedian * 0.35f,
            $"under a roof measured {indoorMedian:0.000} against {outdoorMedian:0.000} in the open");

        // And the two populations must be genuinely separable, not merely different on average.
        int overlap = indoors.Count(value => value > outdoorMedian * 0.6f);
        Assert.True(
            overlap <= indoors.Count * 0.2,
            $"{overlap} of {indoors.Count} roofed spots measured as open as the open ground does");
    }

    /// <summary>Measures one spot with every ray, in one call.</summary>
    private static float Measure(ShelterHeightField field, WorldRayCast cast, Vector3d ear)
    {
        SkyExposureProbe probe = new(SkyExposureProbe.DefaultRayCount)
        {
            RaysPerUpdate = SkyExposureProbe.DefaultRayCount,
            SmoothingSeconds = 0f,
        };

        probe.Update(ear, field, cast, Units, 1f / 60f);
        return probe.Exposure;
    }

    /// <summary>Finds a place under the column's own roof that a person could stand in.</summary>
    /// <remarks>
    /// The floor is the first surface a cast down from just under the roof meets. Two metres of
    /// clearance between the two is a room, a shed or a walkway; less is a kerb, a crate or a solid
    /// block, none of which anybody is standing inside.
    /// </remarks>
    private static bool TryRoofedSpot(
        ObjectCollisionIndex solids,
        ShelterHeightField field,
        double worldX,
        double worldY,
        float surface,
        out Vector3d ear)
    {
        ear = default;

        if (!solids.TryCastWithDoorsShut(
            new Vector3d(worldX * Units, worldY * Units, (surface - 0.3) * Units),
            new Vector3d(worldX * Units, worldY * Units, (field.LowestZ - 5.0) * Units),
            out WorldRayHit floor))
        {
            return false;
        }

        double floorZ = floor.Position.Z / Units;
        if (surface - floorZ < 2.0)
        {
            return false;
        }

        ear = new Vector3d(worldX, worldY, floorZ + EarMetres);
        return true;
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static WorldScene Build(int missionId)
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);

        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));

        MissionRegistry registry = new();
        registry.Register(loop.Natives);
        registry.Scan(fileSystem, resources, loop.Natives);

        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(missionId);

        return WorldScene.Build(mission, levelRoot, magic: null);
    }
}
