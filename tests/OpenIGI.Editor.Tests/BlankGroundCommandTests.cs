using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Commands;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Editor.Terrain;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Editor.Tests;

/// <summary>A blank mission's ground shaped on Project I.G.I.'s level 1: a play area of rebuilt octree round the player.</summary>
public sealed class BlankGroundCommandTests(ITestOutputHelper output)
{
    /// <summary>
    /// Flat ground is the start's height across the play area, the player stands on it, the exit too, and the rebuilt octree
    /// is there to save into the mission's own terrain files.
    /// </summary>
    [RequiresGameDataFact]
    public void Flat_ground_is_level_across_the_play_area()
    {
        (TerrainData terrain, LevelTask root, MissionDocument blank) = Load(1);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(TerrainSurface.Build(terrain, root));
        MissionTask player = blank.EnumerateTasks().Single(static t => t.TypeName == "HumanPlayer");
        Vector3d start = Metres(player);
        Assert.True(editable.TryGetHeight(start.X, start.Y, out double startHeight));

        IEditorCommand? shaped = BlankGroundCommand.Shape(editable, blank, BlankGround.Flat, seed: 0);
        Assert.NotNull(shaped);
        shaped.Apply();

        // Inside the region shaped, away from the edge where it eases back into the level, the ground is the start's height.
        ITerrainHeightGrid region = editable.Grids[^1];
        (double left, double top) = region.SampleMetres(0, 0);
        (double right, double bottom) = region.SampleMetres(region.Width - 1, region.Height - 1);
        double inset = BlankGroundShape.EdgeMetres + 4;
        double worst = 0;
        int samples = 0;
        for (double x = Math.Min(left, right) + inset; x <= Math.Max(left, right) - inset; x += 15)
        {
            for (double y = Math.Min(top, bottom) + inset; y <= Math.Max(top, bottom) - inset; y += 15)
            {
                if (editable.TryGetHeight(x, y, out double z))
                {
                    worst = Math.Max(worst, Math.Abs(z - startHeight));
                    samples++;
                }
            }
        }

        output.WriteLine($"start {startHeight:0.##} m; {samples} samples, worst {worst:0.###} m off");
        output.WriteLine($"octree records: after {TerrainOctree.Read(editable.ChangedFiles(_ => null).Single(static f => f.MissionPath == Igi1EditableTerrain.OctreeMissionPath).Bytes).Count}");
        Assert.True(samples > 200, $"only {samples} samples");
        Assert.True(worst < 0.5, $"the ground is {worst:0.##} m off flat");

        // The player still stands as high above the ground as they did, and the octree is saved with the mission.
        Vector3d after = Metres(player);
        Assert.True(editable.TryGetHeight(after.X, after.Y, out double under));
        Assert.InRange(after.Z - under, start.Z - startHeight - 0.5, start.Z - startHeight + 0.5);
        IReadOnlyList<(string Path, byte[] Bytes)> files = editable.ChangedFiles(_ => null);
        Assert.Contains(files, static f => f.Path == Igi1EditableTerrain.OctreeMissionPath);
        TerrainOctree octree = TerrainOctree.Read(files.Single(static f => f.Path == Igi1EditableTerrain.OctreeMissionPath).Bytes);
        Assert.True(octree.IsUsable);

        // As the shipped levels: no child whose block has no vertices carries its bit. The original meshes every child with
        // its bit set, with a do-while over the block's vertices, and crashed on the generated ground until this held.
        byte[] geometry = files.Single(static f => f.Path == Igi1EditableTerrain.GeometryMissionPath).Bytes;
        int meshedEmpty = 0;
        for (int i = 0; i < octree.Count; i++)
        {
            for (int slot = 0; slot < 8; slot++)
            {
                if (octree[i].HasChild(slot) && Vertices(geometry, octree[octree[i].Child(slot)].CommandOffset) == 0)
                {
                    meshedEmpty++;
                }
            }
        }

        Assert.Equal(0, meshedEmpty);
    }

    /// <summary>Seeded ground rolls, the same seed the same way, and is level where the player starts.</summary>
    [RequiresGameDataFact]
    public void Seeded_ground_rolls_and_is_level_at_the_start()
    {
        (TerrainData terrain, LevelTask root, MissionDocument blank) = Load(1);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(TerrainSurface.Build(terrain, root));
        Vector3d start = Metres(blank.EnumerateTasks().Single(static t => t.TypeName == "HumanPlayer"));
        Assert.True(editable.TryGetHeight(start.X, start.Y, out double startHeight));
        Assert.NotNull(BlankGroundCommand.Shape(editable, blank, BlankGround.Seeded, seed: 4242));

        Assert.True(editable.TryGetHeight(start.X + 5, start.Y, out double near));
        Assert.InRange(near, startHeight - 0.5, startHeight + 0.5);
        double low = double.MaxValue, high = double.MinValue;
        for (double d = -200; d <= 200; d += 20)
        {
            if (editable.TryGetHeight(start.X + d, start.Y + 120, out double z))
            {
                low = Math.Min(low, z);
                high = Math.Max(high, z);
            }
        }

        output.WriteLine($"seeded: {low:0.#} to {high:0.#} m round a start at {startHeight:0.#} m");
        Assert.True(high - low > 5, "the seeded ground hardly rolls");

        // A cube fully slid is where the depth above draws at rest, as in the shipped levels, so the two meet without a crack.
        // Each depth rounding its own slide targets, they parted by up to 1.3 m and showed the sky.
        IReadOnlyList<(string Path, byte[] Bytes)> files = editable.ChangedFiles(_ => null);
        TerrainOctree octree = TerrainOctree.Read(files.Single(static f => f.Path == Igi1EditableTerrain.OctreeMissionPath).Bytes);
        byte[] geometry = files.Single(static f => f.Path == Igi1EditableTerrain.GeometryMissionPath).Bytes;
        TerrainCommands blocks = TerrainCommands.Read(geometry);
        TerrainCommands slidBlocks = TerrainCommands.Read(Slid(octree, geometry));
        ITerrainHeightGrid region = editable.Grids[^1];
        (double minX, double minY) = region.SampleMetres(0, 0);
        double worstStep = 0;
        int compared = 0;
        for (int depth = 11; depth <= 15; depth++)
        {
            double spacing = (TerrainOctree.RootExtent >> depth) / 4096.0;
            for (double x = minX + spacing; x < minX + 512; x += spacing * 3)
            {
                for (double y = minY + spacing; y < minY + 512; y += spacing * 3)
                {
                    if (!editable.TryGetHeight(x, y, out double z))
                    {
                        continue;
                    }

                    long ux = (long)(x * 4096) + 1, uy = (long)(y * 4096) + 1;
                    if (TerrainRegionBuild.DrawnAt(octree, blocks, depth, ux, uy, z * 4096) is { } coarse
                        && TerrainRegionBuild.DrawnAt(octree, slidBlocks, depth + 1, ux, uy, z * 4096) is { } fine)
                    {
                        worstStep = Math.Max(worstStep, Math.Abs(coarse - fine) / 4096);
                        compared++;
                    }
                }
            }
        }

        output.WriteLine($"depths meet within {worstStep:0.###} m over {compared} grid points");
        Assert.True(compared > 500, $"only {compared} grid points compared");
        Assert.True(worstStep < 0.05, $"a depth slides {worstStep:0.##} m off the depth above");
    }

    // The geometry with every block fully slid: each extra vertex's position moved onto its source's, its own bits kept.
    private static byte[] Slid(TerrainOctree octree, byte[] geometry)
    {
        byte[] slid = (byte[])geometry.Clone();
        TerrainCommands blocks = TerrainCommands.Read(geometry);
        HashSet<uint> done = [];
        for (int i = 0; i < octree.Count; i++)
        {
            uint offset = octree[i].CommandOffset;
            if (!done.Add(offset) || !blocks.TryGetBlock(offset, out TerrainCommandBlock block) || block.IsEmpty)
            {
                continue;
            }

            int vertices = (int)offset + TerrainCommandBlock.HeaderSize + (block.TriangleCount * 4);
            for (int v = block.BaseVertexCount; v < block.VertexCount; v++)
            {
                int source = block.Vertex(v).MorphSource;
                if (source < block.BaseVertexCount)
                {
                    uint moved = (block.Vertex(source).Packed & 0xFFFFC000u) | (block.Vertex(v).Packed & 0x3FFFu);
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(slid.AsSpan(vertices + (v * 4)), moved);
                }
            }
        }

        return slid;
    }

    /// <summary>
    /// An AI graph built over seeded ground is a graph file the game reads: nodes on the ground, routes between every pair,
    /// and an AIGraph task whose Graphdata counts what the file holds.
    /// </summary>
    [RequiresGameDataFact]
    public void An_ai_graph_over_seeded_ground_reads_back_routed()
    {
        (TerrainData terrain, LevelTask root, MissionDocument blank) = Load(1);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(TerrainSurface.Build(terrain, root));
        Vector3d start = Metres(blank.EnumerateTasks().Single(static t => t.TypeName == "HumanPlayer"));
        IEditorCommand? shaped = BlankGroundCommand.Shape(editable, blank, BlankGround.Seeded, seed: 4242);
        Assert.NotNull(shaped);
        shaped.Apply();

        NavGraphBuilt? built = NavGraphBuild.Build(blank, editable, new NavGraphRequest(start));
        Assert.NotNull(built);
        Formats.Ai.GraphFile graph = Formats.Ai.GraphFile.Read(built.File);
        output.WriteLine($"graph {built.Task.Id}: {graph.Nodes.Count} nodes, {graph.Links.Count} links over {graph.MaxNodes} slots, {built.File.Length} bytes");
        Assert.True(graph.Nodes.Count > 100, $"only {graph.Nodes.Count} nodes");
        Assert.Equal([graph.Nodes.Count, graph.MaxNodes, graph.Links.Count], built.Task.FindParameter("Graphdata")!.TryGetNumbers(out double[] data) ? data.Select(static d => (int)d) : []);

        // On the ground where it is placed: the task's position plus each node, as the game reads them.
        Assert.True(built.Task.FindParameter("Graph position")!.TryGetNumbers(out double[] origin));
        foreach (Formats.Ai.GraphNode node in graph.Nodes)
        {
            Vector3d world = (new Vector3d(origin[0], origin[1], origin[2]) + node.Position) / 4096.0;
            Assert.True(editable.TryGetHeight(world.X, world.Y, out double z));
            Assert.InRange(world.Z - z, -0.05, 0.05);
        }

        // One piece: every node routes to every other.
        int first = graph.Nodes[0].Id;
        Assert.All(graph.Nodes.Skip(1), n => Assert.NotEmpty(graph.RouteTable!.EnumerateRoute(first, n.Id)));
    }

    // A block's vertex count, base and extra, from its header (words 2 and 3, as the original reads them).
    private static int Vertices(byte[] geometry, uint offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(geometry.AsSpan((int)offset + 4))
        + System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(geometry.AsSpan((int)offset + 6));

    private static Vector3d Metres(MissionTask task)
    {
        Assert.True(task.FindParameter("Position")!.TryGetNumbers(out double[] values));
        return new Vector3d(values[0] / 4096.0, values[1] / 4096.0, values[2] / 4096.0);
    }

    // The level's terrain as the game loads it, and its blank mission's document.
    private static (TerrainData Terrain, LevelTask Root, MissionDocument Blank) Load(int id)
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
        string objects = Path.Combine(GameData.RequireRoot(), mission.Descriptor.MissionDirectory, "objects.qvm");
        return (mission.Terrain, levelRoot, BlankMission.Make(File.ReadAllBytes(objects)));
    }
}
