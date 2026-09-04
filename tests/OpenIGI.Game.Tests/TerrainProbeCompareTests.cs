using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Compares the port's chunk vertices against a vertex dump the a probe took from the retail game.</summary>
/// <remarks>
/// Opt-in: point <c>OPENIGI_TERRAIN_PROBE_LOG</c> at a log written with <c>IGI_PROBE_HOOKS=terrain</c>
/// (see <c>a probe of the running original</c>); without one the test has nothing to compare and passes vacuously. The
/// dump's <c>[terrain] V</c> records are the positioned vertices <c>sub_510010</c> built, heightmap
/// included, so this is the whole vertex pipeline against the original at once.
/// </remarks>
public sealed partial class TerrainProbeCompareTests
{
    private sealed record Block(int Index, Vector3d Position, int E, int Base, int Total, List<(float X, float Y, float Z, uint W)> Vertices);

    private sealed record Node(Vector3d Centre, int Depth, int Orientation, uint Offset);

    [RequiresGameDataFact]
    public void Compare()
    {
        string? logPath = Environment.GetEnvironmentVariable("OPENIGI_TERRAIN_PROBE_LOG");
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
        {
            return;
        }

        // Parse the dump: descriptors give orientation per position, blocks give the vertices.
        Dictionary<(long, long, long, int), int> orientations = [];
        List<Block> blocks = [];
        Regex d = DescriptorLine();
        Regex b = BlockLine();
        Regex v = VertexLine();
        int frames = 0;
        foreach (string line in File.ReadLines(logPath))
        {
            if (line.Contains("[terrain] frame", StringComparison.Ordinal))
            {
                frames++;
                continue;
            }

            Match m = d.Match(line);
            if (m.Success)
            {
                orientations[(L(m, 1), L(m, 2), L(m, 3), I(m, 4))] = Convert.ToInt32(m.Groups[5].Value, 16) & 7;
                continue;
            }

            m = b.Match(line);
            if (m.Success)
            {
                blocks.Add(new Block(I(m, 1), new Vector3d(L(m, 2), L(m, 3), L(m, 4)), I(m, 5), I(m, 6), I(m, 7), []));
                continue;
            }

            m = v.Match(line);
            if (m.Success)
            {
                Block block = blocks[^1];
                Assert.Equal(block.Index, I(m, 1));
                block.Vertices.Add((F(m, 3), F(m, 4), F(m, 5), Convert.ToUInt32(m.Groups[6].Value, 16)));
            }
        }

        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"frames={frames} blocks={blocks.Count} with vertices={blocks.Count(x => x.Vertices.Count > 0)}");

        (TerrainData terrain, TerrainSurface surface, TerrainHeightBindings bindings) = Load(1);

        // Every octree node whose cube contains a dumped block, keyed by centre and depth.
        Dictionary<(long, long, long, int), Node> nodes = [];
        HashSet<(long, long, long, int)> wanted = [.. blocks.Where(x => x.Vertices.Count > 0).Select(x => ((long)x.Position.X, (long)x.Position.Y, (long)x.Position.Z, x.E))];
        Walk(terrain.Octree, TerrainOctree.RootIndex, TerrainOrientation.Identity, 0, default, nodes, wanted);
        report.AppendLine(CultureInfo.InvariantCulture, $"matched nodes={nodes.Count} of {wanted.Count}");

        int compared = 0, missing = 0, mismatchedOrientation = 0;
        double maxDxy = 0, maxDz = 0, maxDw = 0;
        int overZ = 0;
        int withHeights = 0;
        List<string> worst = [];
        foreach (Block block in blocks)
        {
            if (block.Vertices.Count == 0)
            {
                continue;
            }

            var key = ((long)block.Position.X, (long)block.Position.Y, (long)block.Position.Z, block.E);
            if (!nodes.TryGetValue(key, out Node? node))
            {
                missing++;
                continue;
            }

            int orientation = orientations.TryGetValue(key, out int o) ? o : node.Orientation;
            if (orientation != node.Orientation)
            {
                mismatchedOrientation++;
            }

            TerrainChunk chunk = new(block.Position, block.E, orientation, node.Offset);
            Assert.True(terrain.TryGetBlock(chunk, out TerrainCommandBlock cmd));
            TerrainHeightBinding heights = bindings.Resolve(chunk);
            if (heights.IsBound)
            {
                withHeights++;
            }

            TerrainChunkGeometry? g = TerrainChunkMesh.Build(chunk, cmd, surface, 1f, heights);
            Assert.NotNull(g);
            Assert.Equal(block.Total, cmd.VertexCount);
            Assert.Equal(block.Base, cmd.BaseVertexCount);

            for (int i = 0; i < block.Total; i++)
            {
                Vertex3D ours = g.Vertices[i];
                (float x, float y, float z, uint w) = block.Vertices[i];
                double dx = Math.Abs(chunk.Position.X + ours.Position.X - x);
                double dy = Math.Abs(chunk.Position.Y + ours.Position.Y - y);
                double dz = Math.Abs(chunk.Position.Z + ours.Position.Z - z);
                compared++;
                maxDxy = Math.Max(maxDxy, Math.Max(dx, dy));
                maxDz = Math.Max(maxDz, dz);
                if (dz > 64)
                {
                    overZ++;
                    if (worst.Count < 12)
                    {
                        worst.Add(string.Create(CultureInfo.InvariantCulture, $"    block {block.Index} e={block.E} o={orientation} v{i}: theirs=({x:F1},{y:F1},{z:F1}) ours=({chunk.Position.X + ours.Position.X:F1},{chunk.Position.Y + ours.Position.Y:F1},{chunk.Position.Z + ours.Position.Z:F1}) bound={heights.IsBound}"));
                    }
                }

                // The fourth float: the faded height channel of a base vertex at e == 14, which the port
                // carries negated in the morph; the morph source index of an interpolated vertex.
                if (i < block.Base)
                {
                    float theirW = BitConverter.Int32BitsToSingle((int)w);
                    maxDw = Math.Max(maxDw, Math.Abs(-ours.Normal.Z - theirW));
                }
                else
                {
                    Assert.Equal((int)(w & 0x3F), cmd.Vertex(i).MorphSource);
                }
            }
        }

        report.AppendLine(CultureInfo.InvariantCulture, $"compared={compared} vertices, blocks missing={missing}, orientation mismatches={mismatchedOrientation}, blocks with heights bound={withHeights}");
        report.AppendLine(CultureInfo.InvariantCulture, $"max |dx,dy|={maxDxy:F2} units, max |dz|={maxDz:F2} units ({maxDz / 4096:F3} m), vertices with |dz|>64 units: {overZ}, max |dw|={maxDw:F2}");
        foreach (string line in worst)
        {
            report.AppendLine(line);
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "openigi-probe-compare.txt"), report.ToString());
        Assert.True(overZ == 0 && missing == 0, report.ToString());
    }

    private static (TerrainData, TerrainSurface, TerrainHeightBindings) Load(int id)
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
        TerrainData terrain = mission.Terrain!;
        return (terrain, TerrainSurface.Build(terrain, levelRoot), TerrainHeightBindings.Build(terrain, levelRoot));
    }

    private static void Walk(TerrainOctree tree, int index, int orientation, int depth, Vector3d centre, Dictionary<(long, long, long, int), Node> nodes, HashSet<(long, long, long, int)> wanted)
    {
        TerrainNode node = tree[index];
        if (node.IsLeaf || depth >= 16)
        {
            return;
        }

        int childDepth = depth + 1;
        long childExtent = TerrainOctree.RootExtent >> childDepth;
        for (int octant = 0; octant < 8; octant++)
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

            // Only cubes that contain a wanted block are worth entering: the tree is a DAG.
            bool contains = false;
            foreach ((long x, long y, long z, int e) in wanted)
            {
                if (e >= childDepth
                    && Math.Abs(x - childCentre.X) <= childExtent
                    && Math.Abs(y - childCentre.Y) <= childExtent
                    && Math.Abs(z - childCentre.Z) <= childExtent)
                {
                    contains = true;
                    break;
                }
            }

            if (!contains)
            {
                continue;
            }

            int childOrientation = TerrainOrientation.Compose(orientation, node.Orientation(slot) & 7);
            var key = ((long)childCentre.X, (long)childCentre.Y, (long)childCentre.Z, childDepth);
            if (wanted.Contains(key))
            {
                nodes[key] = new Node(childCentre, childDepth, childOrientation, tree[child].CommandOffset);
            }

            Walk(tree, child, childOrientation, childDepth, childCentre, nodes, wanted);
        }
    }

    private static long L(Match m, int g) => (long)double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);

    private static int I(Match m, int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);

    private static float F(Match m, int g) => float.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\[terrain\] D \d+ pos=\(([-\d.]+),([-\d.]+),([-\d.]+)\) e=(\d+) flags=0x([0-9A-Fa-f]+)")]
    private static partial Regex DescriptorLine();

    [GeneratedRegex(@"\[terrain\] B (\d+) pos=\(([-\d.]+),([-\d.]+),([-\d.]+)\) e=(\d+) base=(\d+) total=(\d+)")]
    private static partial Regex BlockLine();

    [GeneratedRegex(@"\[terrain\] V (\d+) (\d+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([0-9A-Fa-f]{8})")]
    private static partial Regex VertexLine();
}
