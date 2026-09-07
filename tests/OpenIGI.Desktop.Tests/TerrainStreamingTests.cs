using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// What the terrain renderer draws is what the descent asked for — on the frame it asks, not several
/// frames later.
/// </summary>
/// <remarks>
/// <para>
/// A per-frame ceiling on how many chunks could be built once stood between the two. It read as terrain
/// blinking out and back while the player turned: the descent emitted the ground, the renderer refused to
/// build a fifth to a half of it, and nothing downstream could tell the difference between a chunk that
/// was over the budget and a chunk that did not exist. It survived every capture harness because the
/// deterministic paths lifted the ceiling, and it survived <c>r_terrain_occlusion 0</c> because it had
/// nothing to do with occlusion.
/// </para>
/// <para>
/// Both tests run with occlusion culling off, so the only thing that can hold a chunk back is streaming.
/// </para>
/// </remarks>
public sealed class TerrainStreamingTests
{
    private const float FieldOfView = 1.0f;
    private const float AspectRatio = 16f / 9f;
    private const int DefaultViewDistance = 450;

    /// <summary>What <c>r_terrain_cache</c> asks for at the ultra preset.</summary>
    private const int UltraCacheLimit = 24576;

    /// <summary>Standing still, the first frame drawn at a pose is the same frame as the thirtieth.</summary>
    [RequiresGameDataFact]
    public void TheFirstFrameAtAPoseDrawsEveryChunkTheDescentEmitted()
    {
        using Harness harness = Harness.Load(1);

        int first = harness.Frame(harness.Eye, 0f);
        Assert.True(first > 0, "the opening pose drew no terrain at all");

        for (int i = 0; i < 30; i++)
        {
            Assert.Equal(first, harness.Frame(harness.Eye, 0f));
        }

        // And a settled view builds nothing: everything it wants is already uploaded.
        Assert.Equal(0, harness.Terrain.LastBuildCount);
    }

    /// <summary>A full turn and back draws the opening ground again on the frame it returns.</summary>
    /// <remarks>
    /// The sweep is what evicts: a turn brings more chunks into view than the cache holds, so coming back
    /// means rebuilding what was dropped. Rebuilding it over the next several frames instead of this one
    /// is exactly the hole this fix is about.
    /// </remarks>
    [RequiresGameDataFact]
    public void TurningRightRoundAndBackDrawsTheOpeningGroundOnTheFrameItReturns()
    {
        using Harness harness = Harness.Load(1);

        int opening = harness.Frame(harness.Eye, 0f);
        Assert.True(opening > 0, "the opening pose drew no terrain at all");

        // Four degrees a frame is an ordinary mouse sweep, and a whole circle of it.
        for (int i = 1; i < 90; i++)
        {
            harness.Frame(harness.Eye, (float)(i * 4.0 * Math.PI / 180.0));
        }

        Assert.Equal(opening, harness.Frame(harness.Eye, 0f));
    }

    /// <summary>A turn at the ultra cache size evicts nothing, so turning back rebuilds nothing.</summary>
    /// <remarks>
    /// The cache used to hold 4096 chunks against a level 1 panorama of about 9000, so a sweep evicted
    /// ground the player was about to turn back onto: a hundred chunks rebuilt every frame, for ever,
    /// which is the cost the per-frame build ceiling was really rationing. Sized to the panorama, the
    /// sweep settles and the streaming stops.
    /// </remarks>
    [RequiresGameDataFact]
    public void ASweepAtTheUltraCacheSizeEvictsNothingAndSettlesToNoStreamingAtAll()
    {
        using Harness harness = Harness.Load(1);
        harness.Terrain.CacheLimit = UltraCacheLimit;

        for (int i = 0; i < 90; i++)
        {
            harness.Frame(harness.Eye, (float)(i * 4.0 * Math.PI / 180.0));
        }

        for (int i = 0; i < 90; i++)
        {
            harness.Frame(harness.Eye, (float)((360 - (i * 4.0)) * Math.PI / 180.0));
        }

        Assert.Equal(0, harness.Terrain.EvictedChunkCount);
        Assert.Equal(0, harness.Terrain.LastBuildCount);
    }

    /// <summary>A batch page is created with room for what a page holds, not for a page's worth.</summary>
    /// <remarks>
    /// The floor was 4096 vertices apiece against a median page of eleven, so the panorama above
    /// allocated forty-five times the geometry it held — some 918 MB of device buffers for 10 MB of
    /// ground. The bound here is loose on purpose: it is guarding the order of magnitude, not the
    /// measurement, which sits near two and a half.
    /// </remarks>
    [RequiresGameDataFact]
    public void BatchPagesAreNotAllocatedManyTimesTheGeometryTheyHold()
    {
        using Harness harness = Harness.Load(1);
        harness.Terrain.CacheLimit = UltraCacheLimit;

        for (int i = 0; i < 90; i++)
        {
            harness.Frame(harness.Eye, (float)(i * 4.0 * Math.PI / 180.0));
        }

        (int pages, long vertices, long capacity) = harness.Terrain.PageOccupancy;
        Assert.True(pages > 0, "the sweep opened no batch pages");
        Assert.True(
            capacity < vertices * 6,
            $"{pages} pages allocated {capacity} vertices for the {vertices} they hold");
    }

    /// <summary>The cache is never sized below what one frame's descent can ask for.</summary>
    /// <remarks>
    /// A cache smaller than the descent's own chunk buffer would evict, every frame, chunks the next
    /// frame is about to ask for again — the thrash the setting exists to end, arrived at from below.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheCacheWillNotBeSizedBelowOneFramesWorthOfChunks()
    {
        using Harness harness = Harness.Load(1);

        harness.Terrain.CacheLimit = 1;

        Assert.Equal(TerrainTraversal.MaximumChunks, harness.Terrain.CacheLimit);
    }

    /// <summary>A retail level, its terrain renderer, and a camera to point at it.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly Matrix4f _projection = Matrix4f.CreatePerspective(FieldOfView, AspectRatio, 0.1f, 4000f);

        private Harness(TerrainRenderer terrain, Vector3d eye)
        {
            Terrain = terrain;
            Eye = eye;
        }

        public TerrainRenderer Terrain { get; }

        /// <summary>Gets a viewpoint over the level's own placed objects, which is where its terrain is.</summary>
        public Vector3d Eye { get; }

        /// <summary>Gets or sets how far the descent resolves ground, as <c>r_terrain_detail</c> sets it.</summary>
        public int ViewDistance { get; set; } = DefaultViewDistance;

        public static Harness Load(int mission)
        {
            VirtualFileSystem fileSystem = new();
            fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
            ResourceRegistry resources = new(fileSystem);
            GameLoop loop = new(new ManualTimeSource());

            MissionRegistry registry = new();
            registry.Register(loop.Natives);
            registry.Scan(fileSystem, resources, loop.Natives);

            MagicObjectRegistry magic = new();
            magic.Register(loop.Natives);
            magic.Load(resources, loop.Natives);
            PlayerTaskTypes.RegisterAll(loop.Types);

            LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
            LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
            LoadedMission loaded = new MissionLoader(registry, fileSystem, resources, levelLoader, null, new TerrainLoader(resources, loop.Natives))
                .Load(mission);

            Assert.NotNull(loaded.Terrain);
            TerrainSurface surface = TerrainSurface.Build(loaded.Terrain, levelRoot);
            WorldScene scene = WorldScene.Build(loaded, levelRoot, magic);

            return new Harness(new TerrainRenderer(new RecordingGraphicsDevice(), surface), Median(scene));
        }

        /// <summary>Draws one frame and returns how many terrain triangles it submitted.</summary>
        /// <param name="eye">The camera position in metres.</param>
        /// <param name="yaw">The heading in radians; zero looks along <c>+Y</c>.</param>
        public int Frame(Vector3d eye, float yaw)
        {
            Terrain.Draw(
                new Camera3D(eye, Camera3D.LookRotation(yaw, 0f)),
                _projection,
                FieldOfView,
                AspectRatio,
                ViewDistance,
                batching: true,
                occlusionCulling: false);
            return Terrain.LastTriangleCount;
        }

        public void Dispose() => Terrain.Dispose();

        /// <summary>The median placed object, which is over the part of the level the mission plays in.</summary>
        private static Vector3d Median(WorldScene scene)
        {
            double[] xs = new double[scene.Placements.Count];
            double[] ys = new double[scene.Placements.Count];
            double[] zs = new double[scene.Placements.Count];
            for (int i = 0; i < scene.Placements.Count; i++)
            {
                xs[i] = scene.Placements[i].Position.X;
                ys[i] = scene.Placements[i].Position.Y;
                zs[i] = scene.Placements[i].Position.Z;
            }

            Array.Sort(xs);
            Array.Sort(ys);
            Array.Sort(zs);
            return new Vector3d(xs[xs.Length / 2], ys[ys.Length / 2], zs[zs.Length / 2] + 3.0);
        }
    }
}
