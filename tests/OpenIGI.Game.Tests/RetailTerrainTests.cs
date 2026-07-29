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

/// <summary>
/// Loads retail terrain, descends it, and turns the chunks into geometry — the whole of
/// the original against real levels.
/// </summary>
/// <remarks>
/// The check that matters most is that the terrain lands where the objects do. Nothing in any terrain
/// file holds a coordinate: a chunk's position is accumulated from octant offsets during the descent, so
/// if any part of that accumulation is wrong the surface appears somewhere else entirely rather than
/// looking slightly off.
/// </remarks>
public sealed class RetailTerrainTests
{
    [RequiresGameDataFact]
    public void EveryLevelsTerrainLoadsAndDeclaresItsMaterialsAndTileMaps()
    {
        Harness harness = Harness.Create();

        int usable = 0;
        int unusable = 0;
        List<string> counts = [];

        for (int id = 1; id <= 14; id++)
        {
            if (!harness.Registry.TryGet(id, out MissionDescriptor? descriptor))
            {
                continue;
            }

            TerrainData? terrain = harness.Terrain.Load(descriptor.TerrainBasePath);
            Assert.NotNull(terrain);

            // Both natives are registered only for the run of the terrain script, so they must be gone
            // again afterwards — the original unregisters them for the same reason.
            Assert.False(harness.Loop.Natives.Contains(TerrainLoader.MaterialNative));
            Assert.False(harness.Loop.Natives.Contains(TerrainLoader.TileMapNative));

            counts.Add($"{id}:{terrain.Materials.Count}/{terrain.TileMaps.Count}");

            // Ids are contiguous from zero in both tables, which is what lets a band index one directly.
            for (int material = 0; material < terrain.Materials.Count; material++)
            {
                Assert.True(terrain.Materials.ContainsKey(material), $"level {id} has no material {material}");
            }

            for (int tileMap = 0; tileMap < terrain.TileMaps.Count; tileMap++)
            {
                Assert.True(terrain.TileMaps.ContainsKey(tileMap), $"level {id} has no tile map {tileMap}");
            }

            foreach (TerrainMaterial material in terrain.Materials.Values)
            {
                Assert.Equal(TerrainMaterial.BandCount, material.Bands.Count);
                foreach (TerrainMaterialBand band in material.Bands)
                {
                    Assert.True(terrain.TileMaps.ContainsKey(band.TileMap0));
                    Assert.True(terrain.TileMaps.ContainsKey(band.TileMap1));
                }
            }

            if (terrain.IsUsable)
            {
                usable++;
            }
            else
            {
                unusable++;
            }
        }

        Assert.Equal(13, usable);
        Assert.Equal(1, unusable);

        // Measured, because world-loading.md §6.3 had this wrong: it is not "materials 0..6 and tile
        // maps 0..7 everywhere". Nine levels declare six materials and eight tile maps; four declare
        // seven of each; level 14 declares seven and eight.
        Assert.Equal(
            "1:6/8 2:6/8 3:6/8 4:6/8 5:6/8 6:6/8 7:7/7 8:7/7 9:6/8 10:6/8 11:7/7 12:7/7 13:6/8 14:7/8",
            string.Join(" ", counts));
    }

    [RequiresGameDataFact]
    public void TheDescentPutsTerrainUnderTheLevelsObjects()
    {
        Harness harness = Harness.Create();
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
        Harness harness = Harness.Create();
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
    public void TheGroundIsTexturedFromTheLevelsOwnTextureModifiers()
    {
        Harness harness = Harness.Create();
        (TerrainData terrain, TerrainSurface surface, WorldScene scene) = harness.LoadMission(1);

        // Level 1's objects.qsc declares nine TextureModifiers: three "Grass" and one more at a coarser
        // level on material 1, two "Rock" and one "Rock around base" on 5, two "Dirt inside base" on 4.
        Assert.Equal(9, surface.Layers.Count);
        Assert.Equal([1, 4, 5], surface.Layers.Select(l => l.MaterialId).Distinct().Order());

        // Sorted most specific first, which is the order the original's stack tests them in.
        for (int i = 1; i < surface.Layers.Count; i++)
        {
            Assert.True(surface.Layers[i - 1].Level >= surface.Layers[i].Level);
        }

        // Each layer's mask covers its square exactly: n cells over the 2 * extent the level implies.
        foreach (TerrainTileLayer layer in surface.Layers)
        {
            Assert.Equal(layer.Size - 1.0, 2.0 * layer.Extent * layer.Scale, 3);
            Assert.True(layer.Covers(layer.OriginX - 1.0, layer.OriginY) is false);
        }

        (double centreX, double centreY, double ground) = Medians(scene);
        Camera3D camera = new(
            new Vector3d(
                centreX * WorldScene.UnitsPerMetre,
                centreY * WorldScene.UnitsPerMetre,
                (ground + 30.0) * WorldScene.UnitsPerMetre),
            Camera3D.LookRotation(0f, -0.3f));

        List<TerrainChunk> chunks = [];
        TerrainTraversal.Collect(terrain.Octree, TerrainView.FromPerspective(camera, 1.2f, 16f / 9f), chunks);

        HashSet<int> materials = [];
        HashSet<int> tiles = [];
        int lit = 0;

        foreach (TerrainChunk chunk in chunks)
        {
            Assert.True(terrain.TryGetBlock(chunk, out TerrainCommandBlock block));
            TerrainChunkGeometry? geometry = TerrainChunkMesh.Build(chunk, block, surface, 1f);
            if (geometry is null)
            {
                continue;
            }

            lit++;
            foreach (TerrainChunkLayer layer in geometry.Layers)
            {
                materials.Add(layer.MaterialId);

                // Every pass must name a tile the level's atlas actually holds, or it would draw white.
                Assert.NotNull(terrain.Tiles);
                Assert.InRange(layer.TileIndex, 0, terrain.Tiles.Tiles.Count - 1);
                tiles.Add(layer.TileIndex);
            }
        }

        Assert.True(lit > 200, $"only {lit} chunk(s) built geometry");

        // This one view reaches the base material, the grass and the rock; the dirt patches are inside
        // the base and behind the camera, so the sweep below is what proves they resolve at all.
        Assert.Equal([0, 1, 5], materials.Order());

        // The materials resolve to a spread of tiles rather than one: the base material's own tile map
        // alternates four of them, and each overlay brings its own pair.
        Assert.True(tiles.Count >= 8, $"only {tiles.Count} distinct tile(s): {string.Join(",", tiles.Order())}");

        // Every material the level can select, over a sweep of the whole place.
        HashSet<int> everywhere = [];
        for (int i = -60; i <= 60; i++)
        {
            for (int j = -60; j <= 60; j++)
            {
                everywhere.Add(surface.MaterialAt(
                    (centreX * WorldScene.UnitsPerMetre) + (i * 10 * WorldScene.UnitsPerMetre),
                    (centreY * WorldScene.UnitsPerMetre) + (j * 10 * WorldScene.UnitsPerMetre)));
            }
        }

        Assert.Equal([0, 1, 4, 5], everywhere.Order());
    }

    [RequiresGameDataFact]
    public void EveryTileATileMapNamesExistsInTheLevelsAtlas()
    {
        Harness harness = Harness.Create();
        int checkedLevels = 0;
        List<string> overruns = [];

        for (int id = 1; id <= 14; id++)
        {
            if (!harness.Registry.TryGet(id, out MissionDescriptor? descriptor))
            {
                continue;
            }

            TerrainData? terrain = harness.Terrain.Load(descriptor.TerrainBasePath);
            Assert.NotNull(terrain);
            Assert.NotNull(terrain.Tiles);

            checkedLevels++;

            int over = 0;
            int highest = 0;
            foreach (TerrainTileMap map in terrain.TileMaps.Values)
            {
                foreach (byte tile in map.Tiles)
                {
                    highest = Math.Max(highest, tile);
                    if (tile >= terrain.Tiles.Tiles.Count)
                    {
                        over++;
                    }
                }
            }

            overruns.Add($"{id}:{over}/{highest}/{terrain.Tiles.Tiles.Count}");
        }

        Assert.Equal(14, checkedLevels);

        // The script declares the tile maps and the atlas holds the tiles, and nothing in either file
        // says they agree — so this measures it, as "out-of-range entries / highest named / atlas size".
        // Ten levels agree exactly: the highest tile a map names is the last one the atlas holds. The
        // four that ship only twelve tiles do not — 128 of their 448 tile-map entries name tiles 12 to
        // 14, which are simply not there. The original indexes past its atlas and reads whatever
        // follows; see TerrainChunkMesh for what happens here instead.
        Assert.Equal(
            "1:0/17/18 2:0/17/18 3:0/17/18 4:0/17/18 5:0/17/18 6:0/17/18 7:128/14/12 8:128/14/12 "
                + "9:0/17/18 10:0/17/18 11:128/14/12 12:128/14/12 13:0/17/18 14:0/16/18",
            string.Join(" ", overruns));
    }

    [RequiresGameDataFact]
    public void TurningOnTheSpotNeverRetessellatesTheGround()
    {
        Harness harness = Harness.Create();
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
    public void TheDeadLevelsTerrainReportsItselfUnusable()
    {
        Harness harness = Harness.Create();
        Assert.True(harness.Registry.TryGet(14, out MissionDescriptor? descriptor));

        TerrainData? terrain = harness.Terrain.Load(descriptor.TerrainBasePath);

        // Its files parse — they are the right shape and the right size — but 14400 of its 15202 nodes
        // address no block, so nothing may descend it. The original relocates those offsets regardless.
        Assert.NotNull(terrain);
        Assert.False(terrain.IsUsable);
        Assert.NotEmpty(terrain.Defects);
    }

    [RequiresGameDataFact]
    public void HeightsDisplaceOnlyTheFinestChunksAndOnlyWhereBound()
    {
        Harness harness = Harness.Create();
        Assert.True(harness.Registry.TryGet(1, out MissionDescriptor? descriptor));

        TerrainData? terrain = harness.Terrain.Load(descriptor.TerrainBasePath);
        Assert.NotNull(terrain);
        Assert.NotNull(terrain.Heightmap);

        // Level 1 is one of the six that ship a heightmap. The binding a chunk samples it through comes
        // from a level-script task that has not been read out of the binary yet, so this drives the
        // sampling directly rather than through a load.
        TerrainHeightGrid grid = default;
        for (int id = 1; id < TerrainGridTable.RecordCount && grid.Corners.IsEmpty; id++)
        {
            terrain.Heightmap.TryGetGrid(id, out grid);
        }

        Assert.False(grid.Corners.IsEmpty);
        Assert.Equal(grid.Size + 1, grid.Stride);

        // A sample outside the grid reads flat rather than wrapping into the next row.
        Assert.Equal(0.0, grid.Sample(-1.0, 0.0));
        Assert.Equal(0.0, grid.Sample(grid.Size, 0.0));

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

    /// <summary>The middle of a level, by median rather than by bounding box.</summary>
    [RequiresGameDataFact]
    public void ThePointDescentReachesRealDepthOverTheLevelsOwnGround()
    {
        // The descent 0x4C9A30 does, against the octree the renderer already walks. If the collision
        // query and the renderer disagreed about the tree, this is where it would show.
        Harness harness = Harness.Create();
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
        Harness harness = Harness.Create();
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

    [RequiresGameDataFact]
    public void TheLeafQueryLandsOnGeometryOverTheLevelsOwnGround()
    {
        // The front of 0x4CB050: descend to depth 16 and restate the point in the leaf's frame. This is
        // the step before the surface test, so what it must prove is that real ground actually resolves
        // to real triangles.
        Harness harness = Harness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);

        (double centreX, double centreY, double ground) = Medians(scene);

        int found = 0;
        int sampled = 0;
        long triangles = 0;

        for (int dx = -6; dx <= 6; dx++)
        {
            for (int dy = -6; dy <= 6; dy++)
            {
                Vector3i point = new(
                    (int)((centreX + (dx * 6.0)) * WorldScene.UnitsPerMetre),
                    (int)((centreY + (dy * 6.0)) * WorldScene.UnitsPerMetre),
                    (int)(ground * WorldScene.UnitsPerMetre));

                sampled++;
                if (!TerrainLeafQuery.TryFindLeaf(terrain, point, out TerrainLeaf leaf))
                {
                    continue;
                }

                found++;
                triangles += leaf.Block.TriangleCount;

                // A leaf that answered carries geometry, at the surface depth, with the right scale.
                Assert.Equal(TerrainLeafQuery.SurfaceDepth, leaf.Cursor.Depth);
                Assert.False(leaf.Block.IsEmpty);
                Assert.Equal(TerrainOctree.RootExtent >> TerrainLeafQuery.SurfaceDepth, leaf.Cursor.Extent);
                Assert.Equal(leaf.Cursor.Extent / 24.0, leaf.UnitScale);

                // Every packed vertex sits inside the leaf, which is what the +-24 encoding promises.
                for (int i = 0; i < leaf.Block.VertexCount; i++)
                {
                    TerrainPackedVertex vertex = leaf.Block.Vertex(i);
                    Assert.InRange(vertex.X, -24, 24);
                    Assert.InRange(vertex.Y, -24, 24);
                    Assert.InRange(vertex.Z, -24, 24);
                }

                // And every triangle indexes a vertex it has.
                for (int i = 0; i < leaf.Block.TriangleCount; i++)
                {
                    TerrainTriangle triangle = leaf.Block.Triangle(i);
                    Assert.InRange(triangle.I0, 0, leaf.Block.VertexCount - 1);
                    Assert.InRange(triangle.I1, 0, leaf.Block.VertexCount - 1);
                    Assert.InRange(triangle.I2, 0, leaf.Block.VertexCount - 1);
                }
            }
        }

        Assert.Equal(169, sampled);

        // The point of the test: over the middle of a real level, most samples find real ground.
        Assert.True(found > sampled / 2, $"only {found} of {sampled} samples reached terrain geometry");
        Assert.True(triangles > 0);
    }

    [RequiresGameDataFact]
    public void TheLeafCornerMaskAgreesWithTheLeafExtent()
    {
        // The original snaps X and Y with 0xFFFF8000 and works Z off the node centre. That mask is only
        // right if it is twice the leaf half-extent, which is worth pinning rather than assuming.
        Harness harness = Harness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);

        (double centreX, double centreY, double ground) = Medians(scene);
        Vector3i point = new(
            (int)(centreX * WorldScene.UnitsPerMetre),
            (int)(centreY * WorldScene.UnitsPerMetre),
            (int)(ground * WorldScene.UnitsPerMetre));

        Assert.True(TerrainLeafQuery.TryFindLeaf(terrain, point, out TerrainLeaf leaf));

        Assert.Equal(~TerrainLeafQuery.LeafCornerMask + 1, 2 * leaf.Cursor.Extent);
        Assert.Equal(32768, 2 * leaf.Cursor.Extent);

        // The point sits within one leaf of the origin the query reports.
        Assert.InRange(point.X, leaf.Origin.X - (2 * leaf.Cursor.Extent), leaf.Origin.X + (2 * leaf.Cursor.Extent));
        Assert.InRange(point.Y, leaf.Origin.Y - (2 * leaf.Cursor.Extent), leaf.Origin.Y + (2 * leaf.Cursor.Extent));
    }

    [RequiresGameDataFact]
    public void TheGroundQueryFindsRealTerrainUnderLevelOne()
    {
        // The whole chain end to end - descend, reach the leaf, interpolate the triangle - which is the
        // query nothing in the port could do before.
        Harness harness = Harness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);

        (double centreX, double centreY, double ground) = Medians(scene);

        // A body standing on the ground, which is what this query is for: its reach is about four
        // metres, so probing from twenty would legitimately find nothing.
        double from = (ground + 2.0) * WorldScene.UnitsPerMetre;

        int hits = 0;
        int sampled = 0;
        List<double> heights = [];

        for (int dx = -6; dx <= 6; dx++)
        {
            for (int dy = -6; dy <= 6; dy++)
            {
                double x = (centreX + (dx * 6.0)) * WorldScene.UnitsPerMetre;
                double y = (centreY + (dy * 6.0)) * WorldScene.UnitsPerMetre;
                sampled++;

                if (!TerrainHeightQuery.TryGetGround(terrain, x, y, from, out TerrainGround hit))
                {
                    continue;
                }

                hits++;
                heights.Add(hit.Height);

                Assert.True(hit.Height <= from, $"ground {hit.Height} was above the query at {from}");
                Assert.True(from - hit.Height <= TerrainHeightQuery.SearchDepthUnits + 1.0);

                double length = Math.Sqrt(
                    (hit.NormalX * hit.NormalX) + (hit.NormalY * hit.NormalY) + (hit.NormalZ * hit.NormalZ));
                Assert.Equal(1.0, length, 1e-9);
            }
        }

        Assert.Equal(169, sampled);
        Assert.True(hits > sampled / 2, $"only {hits} of {sampled} samples found ground");

        // The heights are a plausible surface rather than nonsense. They may legitimately be equal -
        // the middle of level 1 is largely flat - so the spread is asserted bounded, not non-zero.
        Assert.True(
            heights.Max() - heights.Min() < 200.0 * WorldScene.UnitsPerMetre,
            "the height spread is too large to be one surface");
    }

    [RequiresGameDataFact]
    public void TheGroundQueryIsStableAndItsReachIsBounded()
    {
        Harness harness = Harness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);
        (double centreX, double centreY, double ground) = Medians(scene);

        // Not every point over a level has terrain under it - the octree does not author empty air - so
        // find one that does before testing how it behaves.
        double x = 0.0;
        double y = centreY * WorldScene.UnitsPerMetre;
        bool located = false;
        for (int step = 0; step < 40 && !located; step++)
        {
            x = (centreX + (step * 4.0)) * WorldScene.UnitsPerMetre;
            located = TerrainHeightQuery.TryGetGround(
                terrain, x, y, (ground + 1.0) * WorldScene.UnitsPerMetre, out _);
        }

        Assert.True(located, "no sample over the middle of the level had terrain under it");

        double from = (ground + 1.0) * WorldScene.UnitsPerMetre;
        Assert.True(TerrainHeightQuery.TryGetGround(terrain, x, y, from, out TerrainGround near));
        Assert.True(TerrainHeightQuery.TryGetGround(terrain, x, y, from, out TerrainGround again));

        // Asking twice gives the same answer, which a player standing on it depends on.
        Assert.Equal(near.Height, again.Height);

        // Starting higher, but still within reach, finds the same floor.
        Assert.True(TerrainHeightQuery.TryGetGround(
            terrain, x, y, near.Height + (2.0 * WorldScene.UnitsPerMetre), out TerrainGround higher));
        Assert.Equal(near.Height, higher.Height);

        // And the reach really is bounded: past the search depth the same point finds nothing. That is
        // the query behaving as the original does - it probes for a body already near the ground, and
        // the human's own step-down budgets are all well inside it.
        Assert.False(TerrainHeightQuery.TryGetGround(
            terrain, x, y, near.Height + TerrainHeightQuery.SearchDepthUnits + 4096.0, out _));

        Assert.Equal(4.0, TerrainHeightQuery.SearchDepthUnits / WorldScene.UnitsPerMetre, 0.01);
    }

    [RequiresGameDataFact]
    public void TheGroundUnderTheLevelsObjectsIsNearWhereTheySit()
    {
        // The check that says the query is in the right coordinate frame at all: a level's objects are
        // placed on its ground, so the ground under them should be close to their own height. If the
        // bias, the leaf frame or the scale were wrong this would be wildly off rather than slightly.
        Harness harness = Harness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);

        int compared = 0;
        int near = 0;

        foreach (WorldPlacement placement in scene.Placements.Take(400))
        {
            double x = placement.Position.X * WorldScene.UnitsPerMetre;
            double y = placement.Position.Y * WorldScene.UnitsPerMetre;
            double z = placement.Position.Z * WorldScene.UnitsPerMetre;

            if (!TerrainHeightQuery.TryGetGround(
                    terrain, x, y, z + (2.0 * WorldScene.UnitsPerMetre), out TerrainGround hit))
            {
                continue;
            }

            compared++;
            if (Math.Abs(hit.Height - z) < 10.0 * WorldScene.UnitsPerMetre)
            {
                near++;
            }
        }

        Assert.True(compared > 50, $"only {compared} placements resolved to ground");
        Assert.True(
            near > compared / 2,
            $"only {near} of {compared} placements sat within ten metres of the ground under them");
    }

    [RequiresGameDataFact]
    public void EveryMissionDeclaresAPlayerSpawnAndItIsOnTheGround()
    {
        // The original does not compute a start: the level script places a HumanPlayer task. So the
        // check is that every shipped mission has one, and that where it puts the player is actually
        // standing on that level's terrain.
        Harness harness = Harness.Create();

        int missions = 0;
        int withSpawn = 0;
        int onGround = 0;
        List<string> misses = [];

        foreach (MissionDescriptor descriptor in harness.Registry.Missions.OrderBy(m => m.Id))
        {
            missions++;

            (TerrainData terrain, LevelTask levelRoot) = harness.LoadMissionTree(descriptor.Id);
            if (!PlayerSpawn.TryFind(levelRoot, out PlayerSpawnPoint spawn))
            {
                misses.Add($"{descriptor.Id}: no HumanPlayer task");
                continue;
            }

            withSpawn++;

            if (terrain is not { IsUsable: true })
            {
                continue;
            }

            // A HumanPlayer task's Position is in the game's own world units, unlike a WorldScene
            // placement, which the scene builder has already divided into metres for the renderer.
            double x = spawn.Position.X;
            double y = spawn.Position.Y;
            double z = spawn.Position.Z;

            PlayerBody body = new() { Position = new Vector3d(x, y, z) };
            if (body.PlaceOnGround(terrain))
            {
                onGround++;

                // The declared spawn should be near the ground under it, not tens of metres out.
                Assert.True(
                    Math.Abs(body.Position.Z - z) < 20.0 * WorldScene.UnitsPerMetre,
                    $"mission {descriptor.Id}: spawn is {(z - body.Position.Z) / WorldScene.UnitsPerMetre:0.#} m off the ground");
            }
        }

        Assert.True(missions > 10, $"only {missions} missions were scanned");
        Assert.True(
            withSpawn == missions,
            $"{missions - withSpawn} mission(s) declared no player start: {string.Join("; ", misses)}");
        Assert.True(onGround > missions / 2, $"only {onGround} of {missions} spawns resolved to ground");
    }

    private static (double X, double Y, double Z) Medians(WorldScene scene)
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
        return (xs[xs.Length / 2], ys[ys.Length / 2], zs[zs.Length / 2]);
    }

    /// <summary>A game loop with the retail installation mounted and the mission table scanned.</summary>
    private sealed class Harness
    {
        private Harness(
            GameLoop loop,
            VirtualFileSystem fileSystem,
            ResourceRegistry resources,
            MissionRegistry registry,
            MagicObjectRegistry magic,
            TerrainLoader terrain)
        {
            Loop = loop;
            FileSystem = fileSystem;
            Resources = resources;
            Registry = registry;
            Magic = magic;
            Terrain = terrain;
        }

        public GameLoop Loop { get; }

        public VirtualFileSystem FileSystem { get; }

        public ResourceRegistry Resources { get; }

        public MissionRegistry Registry { get; }

        public MagicObjectRegistry Magic { get; }

        public TerrainLoader Terrain { get; }

        public static Harness Create()
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

            // Without these the level's HumanPlayer is never built and the tree simply has no player.
            PlayerTaskTypes.RegisterAll(loop.Types);

            return new Harness(loop, fileSystem, resources, registry, magic, new TerrainLoader(resources, loop.Natives));
        }

        public (TerrainData Terrain, LevelTask Root) LoadMissionTree(int id)
        {
            LevelTask levelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));
            LevelLoader levelLoader = new(levelRoot, Loop.TaskCommands, Loop.Natives, Resources);
            LoadedMission mission = new MissionLoader(Registry, FileSystem, Resources, levelLoader, null, Terrain)
                .Load(id);

            return (mission.Terrain!, levelRoot);
        }

        public (TerrainData Terrain, TerrainSurface Surface, WorldScene Scene) LoadMission(int id)
        {
            LevelTask levelRoot = (LevelTask)Loop.Tasks.New(Loop.Tasks.Root, Loop.Types.Get(EngineTaskTypes.Level));
            LevelLoader levelLoader = new(levelRoot, Loop.TaskCommands, Loop.Natives, Resources);
            LoadedMission mission = new MissionLoader(Registry, FileSystem, Resources, levelLoader, null, Terrain)
                .Load(id);

            Assert.NotNull(mission.Terrain);
            return (
                mission.Terrain,
                TerrainSurface.Build(mission.Terrain, levelRoot),
                WorldScene.Build(mission, levelRoot, Magic));
        }
    }
}
