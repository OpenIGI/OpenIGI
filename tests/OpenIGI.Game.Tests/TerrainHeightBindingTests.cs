using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.World;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The heightmap bindings a level's <c>HeightMap</c> tasks publish, and why they matter: the fine
/// terrain is authored as the surface minus the heightmap.
/// </summary>
/// <remarks>
/// The expected origins and scale were read from the running retail game with the a probe
/// (<c>[terrain] heightmap</c> records from the table at <c>0xBA2030</c> after level 1 loaded), not
/// derived; the test pins the port to them.
/// </remarks>
public sealed class TerrainHeightBindingTests
{
    private sealed record Node(Vector3d Centre, int Depth, int Orientation, uint Offset);

    [RequiresGameDataFact]
    public void LevelOnePublishesTheThreeBindingsTheRetailGameDoes()
    {
        (TerrainData terrain, LevelTask levelRoot) = Load(1);
        TerrainHeightBindings bindings = TerrainHeightBindings.Build(terrain, levelRoot);

        Assert.Equal(0, bindings.Skipped);
        Assert.Equal(3, bindings.Entries.Count);

        // Level 11 cells are 2^20 units across; a 128-cell grid samples every 2^13 units.
        double scale = 1.0 / 8192.0;
        (int Bitmap, double OriginX, double OriginY)[] expected =
        [
            (0, 22020096.0, -57671680.0),
            (1, 22020096.0, -56623104.0),
            (2, 23068672.0, -56623104.0),
        ];

        foreach ((int bitmap, double originX, double originY) in expected)
        {
            TerrainHeightBindings.Entry entry = Assert.Single(bindings.Entries, e => e.BitmapId == bitmap);
            Assert.Equal(11, entry.Level);
            Assert.Equal(originX, entry.Binding.OriginX);
            Assert.Equal(originY, entry.Binding.OriginY);
            Assert.Equal(scale, entry.Binding.Scale);
            Assert.Equal(128, entry.Binding.Grid.Size);
            Assert.True(entry.Binding.IsBound);
        }

        // Inside a cell the binding resolves; outside every cell nothing does. The player start of
        // level 1 (24578514, -55852816) is 112 m east of the nearest cell, which the retail descriptors
        // confirm by carrying heightmap id 0 there.
        Assert.True(bindings.Resolve(new Vector3d(22544384.0, -57147392.0, 174587904.0)).IsBound);
        Assert.False(bindings.Resolve(new Vector3d(24578514.0, -55852816.0, 174419680.0)).IsBound);
    }

    [RequiresGameDataFact]
    public void TheFineBlocksAreAuthoredAsTheSurfaceMinusTheHeightmap()
    {
        (TerrainData terrain, LevelTask levelRoot) = Load(1);
        TerrainSurface surface = TerrainSurface.Build(terrain, levelRoot);
        TerrainHeightBindings bindings = surface.Heights;
        Assert.False(bindings.IsEmpty);

        List<Node> coarse = [];
        List<Node> fine = [];
        Walk(terrain.Octree, TerrainOctree.RootIndex, TerrainOrientation.Identity, 0, default, coarse, fine, bindings);
        Assert.NotEmpty(coarse);
        Assert.NotEmpty(fine);

        // The coarse level never sees the heightmap, so it is the shape the fine level must agree with
        // once its own displacement is applied and faded — and, without the displacement, the shape
        // the fine blocks pass through exactly at every shared column.
        Dictionary<(long, long), double> coarseZ = [];
        foreach (Node node in coarse)
        {
            TerrainChunk chunk = new(node.Centre, node.Depth, node.Orientation, node.Offset);
            if (!terrain.TryGetBlock(chunk, out TerrainCommandBlock block)
                || TerrainChunkMesh.Build(chunk, block, surface, 1f) is not { } geometry)
            {
                continue;
            }

            for (int i = 0; i < block.VertexCount; i++)
            {
                Vector3f v = geometry.Vertices[i].Position;
                coarseZ[Key(chunk.Position.X + v.X, chunk.Position.Y + v.Y)] = chunk.Position.Z + v.Z;
            }
        }

        int shared = 0;
        double rawMismatch = 0.0;
        double displacedDifference = 0.0;
        int rawSteep = 0;
        int displacedSteep = 0;
        int edges = 0;

        foreach (Node node in fine)
        {
            TerrainChunk chunk = new(node.Centre, node.Depth, node.Orientation, node.Offset);
            TerrainHeightBinding heights = bindings.Resolve(chunk);
            if (!heights.IsBound || !terrain.TryGetBlock(chunk, out TerrainCommandBlock block))
            {
                continue;
            }

            TerrainChunkGeometry? raw = TerrainChunkMesh.Build(chunk, block, surface, 1f);
            TerrainChunkGeometry? displaced = TerrainChunkMesh.Build(chunk, block, surface, 1f, heights);
            if (raw is null || displaced is null)
            {
                continue;
            }

            for (int i = 0; i < block.VertexCount; i++)
            {
                Vector3f a = raw.Vertices[i].Position;
                Vector3f b = displaced.Vertices[i].Position;
                if (coarseZ.TryGetValue(Key(chunk.Position.X + a.X, chunk.Position.Y + a.Y), out double z13))
                {
                    shared++;
                    rawMismatch = Math.Max(rawMismatch, Math.Abs(chunk.Position.Z + a.Z - z13));
                    displacedDifference = Math.Max(displacedDifference, Math.Abs(chunk.Position.Z + b.Z - z13));
                }
            }

            rawSteep += CountSteepEdges(raw, ref edges);
            int ignored = 0;
            displacedSteep += CountSteepEdges(displaced, ref ignored);
        }

        Assert.True(shared > 500, $"only {shared} shared columns");

        // Without the heightmap the fine blocks pass exactly through the coarse mesh...
        Assert.True(rawMismatch < 1.0, $"raw fine geometry departs from the coarse mesh by {rawMismatch} units");

        // ...and with it they depart by metres, which is the detail the coarse level lacks.
        Assert.True(displacedDifference > 2.0 * WorldScene.UnitsPerMetre, $"heightmap only moves the surface {displacedDifference} units");

        // The pits: the raw fine blocks are far steeper than the displaced ones. Measured at 1324
        // against 60 edges over 45 degrees; the bar is a tenth.
        Assert.True(rawSteep > 10 * displacedSteep, $"{rawSteep} steep edges raw, {displacedSteep} displaced, of {edges}");
    }

    private static int CountSteepEdges(TerrainChunkGeometry geometry, ref int edges)
    {
        TerrainChunkLayer layer = geometry.Layers[0];
        int steep = 0;
        for (int i = layer.IndexOffset; i + 2 < layer.IndexOffset + layer.IndexCount; i += 3)
        {
            steep += Steep(geometry.Vertices[geometry.Indices[i]].Position, geometry.Vertices[geometry.Indices[i + 1]].Position, ref edges);
            steep += Steep(geometry.Vertices[geometry.Indices[i + 1]].Position, geometry.Vertices[geometry.Indices[i + 2]].Position, ref edges);
            steep += Steep(geometry.Vertices[geometry.Indices[i + 2]].Position, geometry.Vertices[geometry.Indices[i]].Position, ref edges);
        }

        return steep;

        static int Steep(Vector3f a, Vector3f b, ref int edges)
        {
            double run = Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
            if (run < 1.0)
            {
                return 0;
            }

            edges++;
            return Math.Abs(a.Z - b.Z) / run > 1.0 ? 1 : 0;
        }
    }

    private static (long, long) Key(double x, double y) => ((long)Math.Round(x / 64.0), (long)Math.Round(y / 64.0));

    private static (TerrainData Terrain, LevelTask Root) Load(int id)
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
        TerrainLoader terrainLoader = new(resources, loop.Natives);
        LevelTask levelRoot = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        LevelLoader levelLoader = new(levelRoot, loop.TaskCommands, loop.Natives, resources);
        LoadedMission mission = new MissionLoader(registry, fileSystem, resources, levelLoader, null, terrainLoader).Load(id);
        Assert.NotNull(mission.Terrain);
        return (mission.Terrain, levelRoot);
    }

    /// <summary>Every node at depths 13 and 16 whose cube touches a heightmap cell, placed as the descent places them.</summary>
    /// <remarks>The tree is a DAG, so it is only walked where a binding could apply.</remarks>
    private static void Walk(
        TerrainOctree tree,
        int index,
        int orientation,
        int depth,
        Vector3d centre,
        List<Node> coarse,
        List<Node> fine,
        TerrainHeightBindings bindings)
    {
        TerrainNode node = tree[index];
        if (node.IsLeaf || depth >= 16)
        {
            return;
        }

        long extent = TerrainOctree.RootExtent >> depth;
        bool touches = false;
        foreach (TerrainHeightBindings.Entry entry in bindings.Entries)
        {
            if (Math.Abs(centre.X - entry.Centre.X) <= extent + entry.Extent
                && Math.Abs(centre.Y - entry.Centre.Y) <= extent + entry.Extent)
            {
                touches = true;
                break;
            }
        }

        if (!touches)
        {
            return;
        }

        int childDepth = depth + 1;
        for (int octant = 0; octant < TerrainOrientation.Count; octant++)
        {
            int slot = TerrainOrientation.ChildSlot(orientation, octant);
            if (!node.HasChild(slot))
            {
                continue;
            }

            ushort child = node.Child(slot);
            if (child >= tree.Count)
            {
                continue;
            }

            int ox = (octant & 1) != 0 ? TerrainOctree.RootExtent : -TerrainOctree.RootExtent;
            int oy = (octant & 2) != 0 ? TerrainOctree.RootExtent : -TerrainOctree.RootExtent;
            int oz = (octant & 4) != 0 ? TerrainOctree.RootExtent : -TerrainOctree.RootExtent;
            Vector3d childCentre = new(centre.X + (ox >> childDepth), centre.Y + (oy >> childDepth), centre.Z + (oz >> childDepth));
            int childOrientation = TerrainOrientation.Compose(orientation, node.Orientation(slot) & 7);

            Node n = new(childCentre, childDepth, childOrientation, tree[child].CommandOffset);
            if (childDepth == 13)
            {
                coarse.Add(n);
            }
            else if (childDepth == 16)
            {
                fine.Add(n);
            }

            Walk(tree, child, childOrientation, childDepth, childCentre, coarse, fine, bindings);
        }
    }
}
