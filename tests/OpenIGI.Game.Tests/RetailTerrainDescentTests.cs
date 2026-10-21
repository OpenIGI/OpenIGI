using OpenIGI.Core.Graphics;
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
using static OpenIGI.Game.Tests.RetailTerrainHarness;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Descends retail terrain the way the renderer and the collision query do: where the chunks land, how
/// detail follows the camera, and how the point descent agrees with the traversal.
/// </summary>
public sealed class RetailTerrainDescentTests
{
    [RequiresGameDataFact]
    public void TheDescentPutsTerrainUnderTheLevelsObjects()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, TerrainSurface surface, WorldScene scene) = harness.LoadMission(1);
        Assert.True(terrain.IsUsable);

        // Aim at the middle of what the level places, the way the fly-through camera does.
        (double centreX, double centreY, double ground) = Medians(scene);

        Camera3D camera = new(
            new Vector3d(
                centreX * WorldScene.UnitsPerMetre,
                centreY * WorldScene.UnitsPerMetre,
                (ground + 30.0) * WorldScene.UnitsPerMetre),
            Camera3D.LookRotation(0f, -0.3f));

        List<TerrainChunk> chunks = [];
        TerrainTraversalResult result = TerrainTraversal.Collect(
            terrain.Octree,
            TerrainView.FromPerspective(camera, 1.2f, 16f / 9f),
            chunks);

        Assert.Equal(chunks.Count, result.Emitted);
        Assert.Equal(0, result.Dropped);
        Assert.True(chunks.Count > 50, $"the descent found only {chunks.Count} chunk(s)");

        double minZ = double.MaxValue;
        double maxZ = double.MinValue;
        double nearestGap = double.MaxValue;

        foreach (TerrainChunk chunk in chunks)
        {
            // A chunk is only ever emitted between these, and its extent follows from the exponent.
            Assert.InRange(chunk.LodExponent, TerrainTraversal.MinimumExponent, TerrainTraversal.MaximumExponent);
            Assert.InRange(chunk.Orientation, 0, TerrainOrientation.Count - 1);
            Assert.Equal(TerrainOctree.RootExtent >> chunk.LodExponent, chunk.Extent);

            // The packed vertex unit is the extent over 24, which is what makes ±24 span the cube.
            Assert.Equal(chunk.Extent / (double)TerrainPackedVertex.UnitsPerExtent, chunk.UnitScale, 6);

            Assert.True(terrain.TryGetBlock(chunk, out TerrainCommandBlock block));
            TerrainChunkGeometry? geometry = TerrainChunkMesh.Build(
                chunk,
                block,
                surface,
                (float)(1.0 / WorldScene.UnitsPerMetre));

            if (geometry is null)
            {
                continue;
            }

            Assert.NotEmpty(geometry.Layers);
            Assert.True(geometry.Layers[0].IsBase);

            double chunkZ = chunk.Position.Z / WorldScene.UnitsPerMetre;
            foreach (Vertex3D vertex in geometry.Vertices)
            {
                // Positions are chunk-relative, so a vertex may not leave its own cube.
                float extent = (float)(chunk.Extent / WorldScene.UnitsPerMetre);
                Assert.InRange(vertex.Position.X, -extent, extent);
                Assert.InRange(vertex.Position.Y, -extent, extent);
                Assert.InRange(vertex.Position.Z, -extent, extent);

                minZ = Math.Min(minZ, chunkZ + vertex.Position.Z);
                maxZ = Math.Max(maxZ, chunkZ + vertex.Position.Z);
            }

            double dx = (chunk.Position.X / WorldScene.UnitsPerMetre) - centreX;
            double dy = (chunk.Position.Y / WorldScene.UnitsPerMetre) - centreY;
            nearestGap = Math.Min(nearestGap, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        // Terrain has to be where the level is. The chunk nearest the middle of the objects should be
        // within a chunk or two of them, and the surface should span the objects' own height.
        Assert.InRange(nearestGap, 0.0, 200.0);
        Assert.InRange(ground, minZ - 50.0, maxZ + 50.0);
    }

    [RequiresGameDataFact]
    public void DetailFollowsTheCameraAndTheFrustumRejectsWhatIsBehindIt()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);
        (double centreX, double centreY, double ground) = Medians(scene);

        Vector3d eye = new(
            centreX * WorldScene.UnitsPerMetre,
            centreY * WorldScene.UnitsPerMetre,
            (ground + 30.0) * WorldScene.UnitsPerMetre);

        List<TerrainChunk> facing = [];
        TerrainTraversal.Collect(
            terrain.Octree,
            TerrainView.FromPerspective(new Camera3D(eye, Camera3D.LookRotation(0f, -0.3f)), 1.2f, 16f / 9f),
            facing);

        // The finest chunks are the ones nearest the camera and the coarsest are the furthest — that is
        // the whole point of the distance metric, and it is easy to get inverted.
        double nearestFine = double.MaxValue;
        double nearestCoarse = double.MaxValue;
        foreach (TerrainChunk chunk in facing)
        {
            double distance = Distance(chunk, eye);
            if (chunk.LodExponent >= TerrainTraversal.MaximumExponent)
            {
                nearestFine = Math.Min(nearestFine, distance);
            }
            else if (chunk.LodExponent <= TerrainTraversal.MinimumExponent + 2)
            {
                nearestCoarse = Math.Min(nearestCoarse, distance);
            }
        }

        Assert.True(nearestFine < nearestCoarse, $"finest at {nearestFine}, coarsest at {nearestCoarse}");

        // A narrower field of view sees less of the world, so it can only ever select fewer chunks.
        List<TerrainChunk> narrow = [];
        TerrainTraversal.Collect(
            terrain.Octree,
            new TerrainView(
                new Camera3D(eye, Camera3D.LookRotation(0f, -0.3f)),
                0.1f,
                0.1f,
                TerrainView.DefaultViewDistance,
                TerrainView.DefaultFloorZ),
            narrow);

        Assert.True(narrow.Count < facing.Count, $"{narrow.Count} chunks through a keyhole, {facing.Count} wide open");
        Assert.NotEmpty(narrow);
    }

    [RequiresGameDataFact]
    public void APanoramicDescentKeepsTheGroundBehindTheCameraAtTheSameDetail()
    {
        // The ground the traced sun is shadowed by. Built from the frustum alone, a hill behind the camera
        // left the traced world whenever the player looked away from it, and the valley in front of them
        // flicked between its shadow and sunlight as they turned.
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);
        (double centreX, double centreY, double ground) = Medians(scene);

        Vector3d eye = new(
            centreX * WorldScene.UnitsPerMetre,
            centreY * WorldScene.UnitsPerMetre,
            (ground + 30.0) * WorldScene.UnitsPerMetre);
        TerrainView view = new(
            new Camera3D(eye, Camera3D.LookRotation(0f, -0.3f)),
            1f,
            0.6f,
            TerrainView.DefaultViewDistance,
            TerrainView.DefaultFloorZ);

        List<TerrainChunk> facing = [];
        List<TerrainChunk> turnedAway = [];
        List<TerrainChunk> panoramic = [];
        TerrainTraversal.Collect(terrain.Octree, view, facing, maximumChunks: 1 << 16);
        TerrainTraversal.Collect(
            terrain.Octree,
            view with { Camera = new Camera3D(eye, Camera3D.LookRotation(MathF.PI, -0.3f)) },
            turnedAway,
            maximumChunks: 1 << 16);
        TerrainTraversal.Collect(terrain.Octree, view with { Panoramic = true }, panoramic, maximumChunks: 1 << 16);

        HashSet<(Vector3d, int)> all = [.. panoramic.Select(chunk => (chunk.Position, chunk.LodExponent))];
        Assert.All(facing, chunk => Assert.Contains((chunk.Position, chunk.LodExponent), all));
        Assert.All(turnedAway, chunk => Assert.Contains((chunk.Position, chunk.LodExponent), all));
        Assert.True(panoramic.Count > facing.Count, $"{panoramic.Count} all round, {facing.Count} in view");

        // And the same no matter which way the camera faces: it is a set of ground, not of what is seen.
        List<TerrainChunk> panoramicTurned = [];
        TerrainTraversal.Collect(
            terrain.Octree,
            view with { Camera = new Camera3D(eye, Camera3D.LookRotation(MathF.PI, -0.3f)), Panoramic = true },
            panoramicTurned,
            maximumChunks: 1 << 16);
        Assert.Equal(all, [.. panoramicTurned.Select(chunk => (chunk.Position, chunk.LodExponent))]);
    }

    [RequiresGameDataFact]
    public void TurningOnTheSpotNeverRetessellatesTheGround()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, TerrainSurface surface, WorldScene scene) = harness.LoadMission(1);
        (double centreX, double centreY, double ground) = Medians(scene);

        Vector3d eye = new(
            centreX * WorldScene.UnitsPerMetre,
            centreY * WorldScene.UnitsPerMetre,
            (ground + 30.0) * WorldScene.UnitsPerMetre);

        // A chunk's level of detail comes from its distance, and its position is accumulated from
        // integer octant offsets — neither has anything to do with where the camera is pointed. So
        // turning on the spot may only ever add and remove chunks at the edges of the view; a chunk that
        // survives a turn must be the same chunk, at the same detail, textured the same way. If that
        // ever stops holding, the ground will visibly tear itself apart as the player looks around.
        Dictionary<(long X, long Y, long Z), (int Lod, int Orientation, int Tile)> seen = [];
        List<TerrainChunk> chunks = [];
        int turns = 0;

        for (float yaw = 0f; yaw < MathF.Tau; yaw += MathF.Tau / 24f)
        {
            for (float pitch = -0.6f; pitch <= 0.6f; pitch += 0.6f)
            {
                turns++;
                TerrainTraversal.Collect(
                    terrain.Octree,
                    TerrainView.FromPerspective(
                        new Camera3D(eye, Camera3D.LookRotation(yaw, pitch)),
                        1.2f,
                        16f / 9f),
                    chunks);

                foreach (TerrainChunk chunk in chunks)
                {
                    Assert.True(terrain.TryGetBlock(chunk, out TerrainCommandBlock block));
                    TerrainChunkGeometry? geometry = TerrainChunkMesh.Build(chunk, block, surface, 1f);

                    (long, long, long) key = ((long)chunk.Position.X, (long)chunk.Position.Y, (long)chunk.Position.Z);
                    (int, int, int) now = (chunk.LodExponent, chunk.Orientation, geometry?.Layers[0].TileIndex ?? -1);

                    if (seen.TryGetValue(key, out (int Lod, int Orientation, int Tile) before))
                    {
                        Assert.Equal(before, now);
                    }
                    else
                    {
                        seen[key] = now;
                    }
                }
            }
        }

        Assert.Equal(75, turns);
        Assert.True(seen.Count > 500, $"the sweep only ever saw {seen.Count} distinct chunk(s)");
    }

    [RequiresGameDataFact]
    public void HeightsDisplaceOnlyTheFinestChunksAndOnlyWhereBound()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        Assert.True(harness.Registry.TryGet(1, out MissionDescriptor? descriptor));

        TerrainData? terrain = harness.Terrain.Load(descriptor.TerrainBasePath);
        Assert.NotNull(terrain);
        Assert.NotNull(terrain.Heightmap);

        // Level 1 is one of the six that ship a heightmap. This drives the sampling directly with a
        // binding of its own; TerrainHeightBindingTests covers the ones the level's tasks publish.
        TerrainHeightGrid grid = default;
        for (int id = 0; id < TerrainGridTable.RecordCount && grid.Corners.IsEmpty; id++)
        {
            terrain.Heightmap.TryGetGrid(id, out grid);
        }

        Assert.False(grid.Corners.IsEmpty);
        Assert.Equal(grid.Size + 1, grid.Stride);

        // A sample outside the grid reads flat rather than wrapping into the next row; the far edge is
        // inside it and reads the last corner, which is what keeps two cells' grids continuous.
        Assert.Equal(0.0, grid.Sample(-1.0, 0.0));
        Assert.Equal(0.0, grid.Sample(grid.Size + 1.0, 0.0));
        Assert.Equal(
            (grid.Corner(grid.Size, 0) - TerrainHeightGrid.Bias) * TerrainHeightGrid.UnitsPerSample,
            grid.Sample(grid.Size, 0.0),
            6);
        Assert.Equal(
            (grid.Corner(grid.Size, grid.Size) - TerrainHeightGrid.Bias) * TerrainHeightGrid.UnitsPerSample,
            grid.Sample(grid.Size, grid.Size),
            6);

        // Interpolating between two corners has to land between their two heights.
        double left = (grid.Corner(0, 0) - TerrainHeightGrid.Bias) * TerrainHeightGrid.UnitsPerSample;
        double right = (grid.Corner(1, 0) - TerrainHeightGrid.Bias) * TerrainHeightGrid.UnitsPerSample;
        double middle = grid.Sample(0.5, 0.0);
        Assert.InRange(middle, Math.Min(left, right), Math.Max(left, right));
        Assert.Equal(left, grid.Sample(0.0, 0.0), 6);

        TerrainHeightBinding binding = new(grid, 0.0, 0.0, 1.0 / 4096.0);
        Assert.True(binding.IsBound);

        // A chunk coarser than the heightmap band is not displaced at all, however the binding is set.
        TerrainChunk coarse = new(default, TerrainTraversal.HeightmapExponent - 1, 0, 0);
        TerrainChunk fine = new(default, TerrainTraversal.MaximumExponent, 0, 0);
        Assert.False(coarse.AppliesHeightmap);
        Assert.True(fine.AppliesHeightmap);
    }

    private static double Distance(in TerrainChunk chunk, in Vector3d eye)
    {
        double dx = chunk.Position.X - eye.X;
        double dy = chunk.Position.Y - eye.Y;
        double dz = chunk.Position.Z - eye.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    [RequiresGameDataFact]
    public void ThePointDescentReachesRealDepthOverTheLevelsOwnGround()
    {
        // The descent 0x4C9A30 does, against the octree the renderer already walks. If the collision
        // query and the renderer disagreed about the tree, this is where it would show.
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);
        Assert.True(terrain.IsUsable);

        (double centreX, double centreY, double ground) = Medians(scene);

        int deepest = 0;
        int sampled = 0;

        // A grid over the middle of the level, at the height its objects sit at.
        for (int dx = -4; dx <= 4; dx++)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                Vector3i point = new(
                    (int)((centreX + (dx * 8.0)) * WorldScene.UnitsPerMetre),
                    (int)((centreY + (dy * 8.0)) * WorldScene.UnitsPerMetre),
                    (int)(ground * WorldScene.UnitsPerMetre));

                TerrainDescentCursor cursor = TerrainPointDescent.Descend(terrain.Octree, point);
                sampled++;
                deepest = Math.Max(deepest, cursor.Depth);

                // Whatever depth it reached, the cube it reports contains the point.
                Assert.InRange(point.X, cursor.Centre.X - cursor.Extent, cursor.Centre.X + cursor.Extent);
                Assert.InRange(point.Y, cursor.Centre.Y - cursor.Extent, cursor.Centre.Y + cursor.Extent);
                Assert.InRange(point.Z, cursor.Centre.Z - cursor.Extent, cursor.Centre.Z + cursor.Extent);

                // And it names a node that exists, at a legal orientation.
                Assert.InRange(cursor.NodeIndex, 0, terrain.Octree.Count - 1);
                Assert.InRange(cursor.Orientation, 0, TerrainOrientation.Count - 1);
                Assert.InRange(cursor.Depth, 0, TerrainPointDescent.MaximumDepth);
                Assert.Equal(TerrainOctree.RootExtent >> cursor.Depth, cursor.Extent);
            }
        }

        Assert.Equal(81, sampled);

        // The real check: over authored ground the tree is deep, not a root stub. Retail level 1 reaches
        // the traversal's own minimum chunk exponent, so the descent must get at least that far.
        Assert.True(
            deepest >= TerrainTraversal.MinimumExponent,
            $"deepest descent was {deepest}, which is shallower than the authored terrain");
    }

    [RequiresGameDataFact]
    public void ThePointDescentAgreesWithTheChunksTheTraversalEmits()
    {
        // Two independent walks of one tree: the renderer's view-driven collect, and the collision
        // module's point descent. Descending to a chunk's own centre must land on that chunk's cube.
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);

        (double centreX, double centreY, double ground) = Medians(scene);
        Camera3D camera = new(
            new Vector3d(
                centreX * WorldScene.UnitsPerMetre,
                centreY * WorldScene.UnitsPerMetre,
                (ground + 30.0) * WorldScene.UnitsPerMetre),
            Camera3D.LookRotation(0f, -0.3f));

        List<TerrainChunk> chunks = [];
        TerrainTraversal.Collect(terrain.Octree, TerrainView.FromPerspective(camera, 1.2f, 16f / 9f), chunks);
        Assert.NotEmpty(chunks);

        int checkedChunks = 0;
        foreach (TerrainChunk chunk in chunks.Take(64))
        {
            Vector3i centre = new((int)chunk.Position.X, (int)chunk.Position.Y, (int)chunk.Position.Z);
            TerrainDescentCursor cursor = TerrainPointDescent.Descend(terrain.Octree, centre, chunk.LodExponent);

            // The descent got at least as deep as the chunk sits, and its cube covers the chunk's origin.
            Assert.InRange(centre.X, cursor.Centre.X - cursor.Extent, cursor.Centre.X + cursor.Extent);
            Assert.InRange(centre.Y, cursor.Centre.Y - cursor.Extent, cursor.Centre.Y + cursor.Extent);
            checkedChunks++;
        }

        Assert.True(checkedChunks > 0);
    }
}
