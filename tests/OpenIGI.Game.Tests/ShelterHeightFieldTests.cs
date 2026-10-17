using OpenIGI.Core.Graphics;
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
/// Holds the top-down roof raster against the collision geometry it is made of.
/// </summary>
/// <remarks>
/// <para>
/// The thing being pinned is that "is there a roof over this point" is now answered by the level's
/// geometry rather than by which tasks happen to be named after buildings. The old test was that a
/// building's bounding box contained a point; measured against the collision index on mission one, that
/// test called a quarter of the ground inside those boxes indoors when it has open sky over it — which
/// is what stood the fog down and stopped the rain in the middle of an apron — and missed two fifths of
/// the ground that genuinely is under a roof, which is what put puddles on warehouse floors.
/// </para>
/// <para>
/// So the assertions here are against the ray-cast truth and not against a previous implementation: a
/// column of the level is roofed when a cast down it finds a surface with room to stand under, and the
/// raster has to agree.
/// </para>
/// </remarks>
public sealed class ShelterHeightFieldTests
{
    private const double Units = WorldScene.UnitsPerMetre;

    [RequiresGameDataFact]
    public void HoldsTheHeightTheCollisionIndexTracesAtTheSamePlace()
    {
        WorldScene scene = Build(1);
        ShelterHeightField field = ShelterHeightField.Build(scene.Placements);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

        Assert.False(field.IsEmpty);

        int compared = 0;
        int agreed = 0;
        int missing = 0;

        // Every seventh cell on both axes: a few thousand columns, which is enough to catch a transform
        // that disagrees with the collision index without casting a ray for every texel in the level.
        for (int x = 0; x < field.Width; x += 7)
        {
            for (int y = 0; y < field.Depth; y += 7)
            {
                double worldX = field.OriginX + ((x + 0.5) * field.CellMetres);
                double worldY = field.OriginY + ((y + 0.5) * field.CellMetres);

                if (!solids.TryCastWithDoorsShut(
                    new Vector3d(worldX * Units, worldY * Units, (field.HighestZ + 5.0) * Units),
                    new Vector3d(worldX * Units, worldY * Units, (field.LowestZ - 5.0) * Units),
                    out WorldRayHit contact))
                {
                    continue;
                }

                float held = field.SurfaceOver(worldX, worldY);
                if (float.IsNegativeInfinity(held))
                {
                    missing++;
                    continue;
                }

                compared++;
                if (Math.Abs(held - (contact.Position.Z / Units)) <= 1.0)
                {
                    agreed++;
                }
            }
        }

        Assert.True(compared > 500, $"only {compared} columns had geometry to compare");

        // A cell the cast found something in and the raster has nothing for is a hole in the cover, and
        // a hole in a roof is a column of rain in the middle of a room. There should be none.
        Assert.True(missing == 0, $"{missing} traced columns are empty in the raster");
        Assert.True(
            agreed >= compared * 0.98,
            $"only {agreed} of {compared} columns agree with the collision index to within a metre");
    }

    [RequiresGameDataFact]
    public void AgreesWithTheTracedTruthAboutWhatIsUnderARoof()
    {
        WorldScene scene = Build(1);
        ShelterHeightField field = ShelterHeightField.Build(scene.Placements);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(scene.Placements);

        int roofed = 0;
        int roofedAndSheltered = 0;
        int open = 0;
        int openButSheltered = 0;

        for (int x = 0; x < field.Width; x += 5)
        {
            for (int y = 0; y < field.Depth; y += 5)
            {
                double worldX = field.OriginX + ((x + 0.5) * field.CellMetres);
                double worldY = field.OriginY + ((y + 0.5) * field.CellMetres);
                double top = (field.HighestZ + 5.0) * Units;
                double bottom = (field.LowestZ - 5.0) * Units;

                // The truth: the topmost surface here, and the next one below it. A person can stand
                // between them when the gap is a couple of metres, and then this ground is under a roof.
                if (!solids.TryCastWithDoorsShut(
                    new Vector3d(worldX * Units, worldY * Units, top),
                    new Vector3d(worldX * Units, worldY * Units, bottom),
                    out WorldRayHit ceiling))
                {
                    continue;
                }

                double ceilingZ = ceiling.Position.Z / Units;
                if (!solids.TryCastWithDoorsShut(
                    new Vector3d(worldX * Units, worldY * Units, (ceilingZ - 0.3) * Units),
                    new Vector3d(worldX * Units, worldY * Units, bottom),
                    out WorldRayHit floor))
                {
                    continue;
                }

                double floorZ = floor.Position.Z / Units;
                double headroom = ceilingZ - floorZ;
                bool sheltered = field.IsSheltered(worldX, worldY, floorZ);

                // Two metres of clearance is a roof by anyone's reckoning and must shelter; less than
                // the field's own headroom is open ground and must not. The band between the two is a
                // kerb or a crate, where "sheltered" is a judgement rather than a fact, and neither
                // answer is asserted.
                if (headroom >= 2.0)
                {
                    roofed++;
                    if (sheltered)
                    {
                        roofedAndSheltered++;
                    }
                }
                else if (headroom < ShelterHeightField.SurfaceHeadroomMetres)
                {
                    open++;
                    if (sheltered)
                    {
                        openButSheltered++;
                    }
                }
            }
        }

        Assert.True(roofed > 200 && open > 200, $"the sample was one-sided: {roofed} roofed, {open} open");

        // The building boxes this replaced managed 58% of the first, on this level, and called a quarter
        // of the ground they covered indoors when it was not.
        Assert.True(
            roofedAndSheltered >= roofed * 0.9,
            $"only {roofedAndSheltered} of {roofed} roofed columns are sheltered");
        Assert.True(
            openButSheltered <= open * 0.02,
            $"{openButSheltered} of {open} open columns are wrongly sheltered");
    }

    [RequiresGameDataFact]
    public void TheGroundDoesNotShelterItselfAndARoofDoesNotEither()
    {
        WorldScene scene = Build(1);
        ShelterHeightField field = ShelterHeightField.Build(scene.Placements);

        int selfSheltering = 0;
        int cells = 0;

        for (int x = 0; x < field.Width; x += 3)
        {
            for (int y = 0; y < field.Depth; y += 3)
            {
                double worldX = field.OriginX + ((x + 0.5) * field.CellMetres);
                double worldY = field.OriginY + ((y + 0.5) * field.CellMetres);
                float surface = field.SurfaceOver(worldX, worldY);
                if (float.IsNegativeInfinity(surface))
                {
                    continue;
                }

                // Standing ON the highest surface of a column is standing in the open, whether that
                // surface is a road or the roof of the warehouse beside it.
                cells++;
                if (field.IsSheltered(worldX, worldY, surface))
                {
                    selfSheltering++;
                }
            }
        }

        Assert.True(cells > 0);
        Assert.Equal(0, selfSheltering);
    }

    [RequiresGameDataFact]
    public void TheMissionStartsOutdoors()
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
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader).Load(1);
        WorldScene scene = WorldScene.Build(mission, levelRoot, magic: null);

        Assert.True(PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn));

        ShelterHeightField field = ShelterHeightField.Build(scene.Placements);
        Vector3d eye = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 1.7);

        Assert.False(field.IsUnderCover(eye));
    }

    [RequiresGameDataFact]
    public void ThePackedTextureDecodesToTheHeightsItWasBuiltFrom()
    {
        WorldScene scene = Build(1);
        ShelterHeightField field = ShelterHeightField.Build(scene.Placements);
        Bgra32[] texels = field.Encode(out float scale);

        Assert.Equal(field.Width * field.Depth, texels.Length);

        // One step of the packed value across the level's whole vertical span. Anything coarser than a
        // hand's breadth would put the shelter edge in a different place from the one the CPU sees.
        Assert.True(scale < 0.1f, $"one packed step is {scale:0.000} m");

        double worst = 0.0;
        int checkedCells = 0;
        for (int x = 0; x < field.Width; x += 3)
        {
            for (int y = 0; y < field.Depth; y += 3)
            {
                double worldX = field.OriginX + ((x + 0.5) * field.CellMetres);
                double worldY = field.OriginY + ((y + 0.5) * field.CellMetres);
                float held = field.SurfaceOver(worldX, worldY);
                float read = ShelterHeightField.Decode(texels[(y * field.Width) + x], field.LowestZ, scale);

                if (float.IsNegativeInfinity(held))
                {
                    Assert.True(float.IsNegativeInfinity(read), $"open sky at {x},{y} decoded to {read}");
                    continue;
                }

                checkedCells++;
                worst = Math.Max(worst, Math.Abs(held - read));
            }
        }

        Assert.True(checkedCells > 0);
        Assert.True(worst <= scale, $"a texel is {worst:0.000} m off, which is more than one step of {scale:0.000} m");
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
