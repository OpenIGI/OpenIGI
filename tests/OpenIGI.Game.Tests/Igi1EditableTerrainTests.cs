using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Tests;

/// <summary>Project I.G.I.'s ground shaped through its heightmap grids: what the editor's brushes change and what the game then stands on.</summary>
public sealed class Igi1EditableTerrainTests(ITestOutputHelper output)
{
    [RequiresGameDataFact]
    public void ASampleSetByTheEditorIsTheGroundTheGameFindsThere()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        TerrainSurface surface = TerrainSurface.Build(terrain, root);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(surface);
        Assert.Equal(3, editable.Grids.Count);
        byte[] before = terrain.Heightmap!.Write();

        ITerrainHeightGrid grid = editable.Grids[0];
        (double x, double y) = grid.SampleMetres(64, 64);
        double height = grid.GetHeight(64, 64);
        Assert.InRange(Ground(terrain, x, y) - height, -0.01, 0.01);

        grid.SetHeight(64, 64, height + 1.5);
        Assert.True(grid.IsChanged);
        Assert.InRange(Ground(terrain, x, y) - (height + 1.5), -0.04, 0.04);

        // Saved, the heightmap keeps its size and the octree comes with it, the shaping baked into its vertices.
        IReadOnlyList<(string Path, byte[] Bytes)> files = editable.ChangedFiles(_ => null);
        Assert.Equal(
            [Igi1EditableTerrain.HeightmapMissionPath, Igi1EditableTerrain.OctreeMissionPath, Igi1EditableTerrain.GeometryMissionPath],
            files.Take(3).Select(static f => f.Path));
        Assert.Equal(before.Length, files[0].Bytes.Length);

        editable.MarkSaved();
        Assert.False(grid.IsChanged);
        Assert.Empty(editable.ChangedFiles(_ => null));
    }

    [RequiresGameDataFact]
    public void TheGroundGoesNoHigherThanASampleCanHold()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(TerrainSurface.Build(terrain, root));
        ITerrainHeightGrid grid = editable.Grids[1];
        (double x, double y) = grid.SampleMetres(40, 90);
        double height = grid.GetHeight(40, 90);
        double original = height - ((terrain.Heightmap!.Table.BlockOf(1).Span[(90 * grid.Width) + 40] is var raw ? (sbyte)raw : 0) - 64) / 16.0;

        grid.SetHeight(40, 90, height + 100.0);
        Assert.Equal(1, editable.TakeClampCount());
        Assert.Equal(0, editable.TakeClampCount());
        Assert.InRange(Ground(terrain, x, y) - (original + Igi1EditableTerrain.MostRaise), -0.04, 0.04);
    }

    [RequiresGameDataFact]
    public void AnAreaAddedWhereThereIsNoneCanBeShapedAndTakenAwayAgain()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        TerrainSurface surface = TerrainSurface.Build(terrain, root);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(surface);
        byte[] shipped = terrain.Heightmap!.Write();

        // Level 1's player start, 112 m east of the nearest shipped grid.
        double x = 24578514.0 / 4096.0;
        double y = -55852816.0 / 4096.0;
        double ground = Ground(terrain, x, y);
        double shippedGround = Ground(terrain, 5470.0, -13990.0);
        Assert.Null(editable.AddArea(new Vector3d(5504.0, -13952.0, 42601.0)));

        TerrainArea? area = editable.AddArea(new Vector3d(x, y, ground));
        Assert.NotNull(area);
        Assert.Equal(3, area.Id);
        Assert.Equal("HeightMap", area.TaskType);
        Assert.Contains(new KeyValuePair<string, int>("Bitmap ID", 3), area.Values);
        Assert.Equal(4, editable.Grids.Count);
        Assert.Equal(4, surface.Heights.Entries.Count);
        Assert.InRange(Ground(terrain, x, y) - ground, -0.01, 0.01);

        // The shipped grids still read as they did once the file is laid out again.
        Assert.Equal(shippedGround, Ground(terrain, 5470.0, -13990.0));

        ITerrainHeightGrid grid = editable.Grids[3];
        (double column, double row) = grid.ToGrid(x, y);
        int cx = (int)Math.Round(column);
        int cy = (int)Math.Round(row);
        (double sx, double sy) = grid.SampleMetres(cx, cy);
        double before = grid.GetHeight(cx, cy);
        grid.SetHeight(cx, cy, before + 2.0);
        Assert.InRange(Ground(terrain, sx, sy) - (before + 2.0), -0.04, 0.04);

        byte[] saved = editable.ChangedFiles(_ => null).Single(f => f.MissionPath == Igi1EditableTerrain.HeightmapMissionPath).Bytes;
        Assert.Equal(shipped.Length + (129 * 129), saved.Length);

        editable.RemoveArea(area);
        Assert.Equal(3, editable.Grids.Count);
        Assert.InRange(Ground(terrain, sx, sy) - before, -0.04, 0.04);
        Assert.Equal(shipped, terrain.Heightmap.Write());
    }

    [RequiresGameDataFact]
    public void SavedShapingIsInTheOctreeAtEveryDepthAndTheGroundIsUnchanged()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(TerrainSurface.Build(terrain, root));
        ITerrainHeightGrid grid = editable.Grids[0];

        // A 3 m mound, 12 m across at the top, in the middle of grid 0.
        for (int y = 52; y <= 76; y++)
        {
            for (int x = 52; x <= 76; x++)
            {
                double r = Math.Sqrt(((x - 64) * (x - 64)) + ((y - 64) * (y - 64))) / 12.0;
                grid.SetHeight(x, y, grid.GetHeight(x, y) + (3.0 * Math.Max(0.0, 1.0 - (r * r))));
            }
        }

        List<(double X, double Y, double Ground)> probes = [];
        for (int i = 0; i < 9; i++)
        {
            (double px, double py) = grid.SampleMetres(56 + (i * 2), 60 + (i % 3 * 3));
            probes.Add((px, py, Ground(terrain, px, py)));
        }

        IReadOnlyList<(string Path, byte[] Bytes)> files = editable.ChangedFiles(_ => null);
        Assert.Equal(4, files.Count);
        byte[] lmp = files.Single(f => f.Path == Igi1EditableTerrain.LightmapMissionPath).Bytes;
        byte[] shippedLight = Shipped(1, "terrain/terrain.lmp");
        Assert.Equal(shippedLight.Length, lmp.Length);
        int brighter = shippedLight.Zip(lmp).Count(static p => p.Second > p.First);
        int darker = shippedLight.Zip(lmp).Count(static p => p.Second < p.First);
        Assert.True(brighter > 10 && darker > 10, $"{brighter} texels brighter, {darker} darker");
        byte[] ctr = files.Single(f => f.Path == Igi1EditableTerrain.OctreeMissionPath).Bytes;
        byte[] cmd = files.Single(f => f.Path == Igi1EditableTerrain.GeometryMissionPath).Bytes;
        byte[] hmp = files.Single(f => f.Path == Igi1EditableTerrain.HeightmapMissionPath).Bytes;
        Assert.True(ctr.Length > terrain.Octree.Data.Length);
        Assert.True(terrain.Octree.Data.Span.Slice(64).SequenceEqual(ctr.AsSpan(64, terrain.Octree.Data.Length - 64)));

        // Loaded again from the saved files, the ground is where it was shaped.
        TerrainData saved = new(
            TerrainOctree.Read(ctr), TerrainCommands.Read(cmd), terrain.Visibility, TerrainHeightmap.Read(hmp),
            null, null, terrain.Materials, terrain.TileMaps, []);
        TerrainHeightBindings.Bind(saved, root);
        foreach ((double px, double py, double ground) in probes)
        {
            Assert.InRange(Ground(saved, px, py) - ground, -0.12, 0.12);
        }

        // And the coarse levels, which never see the heightmap, rose with it: the depth-13 node over the mound.
        (double cx, double cy) = grid.SampleMetres(64, 64);
        Assert.InRange(CoarseTop(saved, cx, cy) - CoarseTop(terrain, cx, cy), 1.5, 4.0);
    }

    [RequiresGameDataFact]
    public void TheGridsEdgeStaysWhereItWasSoNoCrackOpensBeyondIt()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(TerrainSurface.Build(terrain, root));
        ITerrainHeightGrid grid = editable.Grids[0];

        double edge = grid.GetHeight(0, 64);
        grid.SetHeight(0, 64, edge - 10.0);
        Assert.Equal(edge, grid.GetHeight(0, 64), 6);

        double near = grid.GetHeight(2, 64);
        grid.SetHeight(2, 64, near - 10.0);
        Assert.Equal(near - 2.0, grid.GetHeight(2, 64), 6);
        Assert.Equal(2, editable.TakeClampCount());
    }

    [RequiresGameDataFact]
    public void EveryRetailHeightmapTaskStillBinds()
    {
        foreach (int level in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 })
        {
            (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(level);
            TerrainHeightBindings bindings = TerrainHeightBindings.Build(terrain, root);
            Assert.Equal(0, bindings.Skipped);
            foreach (TerrainHeightBindings.Entry entry in bindings.Entries)
            {
                output.WriteLine($"level {level}: grid {entry.BitmapId} level {entry.Level} centre z {entry.Centre.Z / 4096.0:0.0} m, below {entry.CubesBelow}, above {entry.CubesAbove}");
            }
        }
    }

    // The highest vertex of the depth-13 nodes within 8 m of a point, in metres.
    private static double CoarseTop(TerrainData terrain, double xMetres, double yMetres)
    {
        double x = xMetres * 4096.0;
        double y = yMetres * 4096.0;
        double top = double.MinValue;
        TerrainOctreeRewriter.Rewrite(
            terrain.Octree.Data,
            terrain.Commands.Data,
            place => place.Exponent <= 13 && Math.Abs(place.CentreX - x) <= place.Extent && Math.Abs(place.CentreY - y) <= place.Extent,
            (place, block) =>
            {
                if (place.Exponent == 13)
                {
                    for (int i = 0; i < block.BaseVertexCount; i++)
                    {
                        TerrainPackedVertex v = block.Vertex(i);
                        (int lx, int ly) = TerrainOrientation.Transform(place.Orientation, v.X, v.Y);
                        if (Math.Abs(place.CentreX + (lx * place.Unit) - x) <= 8.0 * 4096.0 && Math.Abs(place.CentreY + (ly * place.Unit) - y) <= 8.0 * 4096.0)
                        {
                            top = Math.Max(top, (place.CentreZ + (v.Z * place.Unit)) / 4096.0);
                        }
                    }
                }

                return null;
            });
        Assert.NotEqual(double.MinValue, top);
        return top;
    }

    private static byte[] Shipped(int level, string path) =>
        File.ReadAllBytes(GameData.Resolve($"missions/location0/level{level}/{path}"));

    private static double Ground(TerrainData terrain, double xMetres, double yMetres)
    {
        Assert.True(terrain.TryDrop(xMetres * 4096.0, yMetres * 4096.0, 44000.0 * 4096.0, 4000.0 * 4096.0, upward: false, out TerrainGround ground));
        return ground.Height / 4096.0;
    }

    /// <summary>
    /// A ray meets the ground where marching it does, on the octree and on a sculpted grid alike (the grid's new shape, not
    /// the octree under it), and a ray into the sky meets nothing.
    /// </summary>
    [RequiresGameDataFact]
    public void RaysMeetTheGroundWhereAMarchDoes()
    {
        (TerrainData terrain, LevelTask root) = TerrainHeightBindingTests.Load(1);
        Igi1EditableTerrain editable = Igi1EditableTerrain.For(TerrainSurface.Build(terrain, root));
        ITerrainHeightGrid grid = editable.Grids[0];
        for (int y = 52; y <= 76; y++)
        {
            for (int x = 52; x <= 76; x++)
            {
                grid.SetHeight(x, y, grid.GetHeight(x, y) + 6.0);
            }
        }

        editable.Refresh(grid, 52, 52, 76, 76);
        (double gx, double gy) = grid.SampleMetres(64, 64);
        Vector3d overGrid = new(gx - 30.0, gy, grid.GetHeight(64, 64) + 25.0);
        Vector3d overOctree = new(6000.0, -13636.0, 42620.0);
        foreach ((Vector3d eye, Vector3d direction) in (ValueTuple<Vector3d, Vector3d>[])
            [(overGrid, new Vector3d(1, 0, -0.8)), (overGrid, new Vector3d(1, 0.2, -0.5)), (overOctree, new Vector3d(1, 0, -1)), (overOctree, new Vector3d(0.3, 1, -0.4))])
        {
            Assert.Equal(TerrainHeightGrids.March(editable.TryGetHeight, eye, direction, 5000.0, out Vector3d marched), editable.TryRaycast(eye, direction, 5000.0, out Vector3d cast));
            Assert.True((cast - marched).Length < 0.05, $"{cast} against {marched}");
        }

        Assert.False(editable.TryRaycast(overOctree, new Vector3d(1, 0, 0.2), 5000.0, out _));
    }
}
