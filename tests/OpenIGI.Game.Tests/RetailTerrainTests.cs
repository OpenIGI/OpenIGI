using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Game.Tests.RetailTerrainHarness;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Loads retail terrain, descends it, and turns the chunks into geometry — the whole terrain path against
/// real levels.
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
        RetailTerrainHarness harness = RetailTerrainHarness.Create();

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

        // Measured, because an earlier reading had this wrong: it is not "materials 0..6 and tile
        // maps 0..7 everywhere". Nine levels declare six materials and eight tile maps; four declare
        // seven of each; level 14 declares seven and eight.
        Assert.Equal(
            "1:6/8 2:6/8 3:6/8 4:6/8 5:6/8 6:6/8 7:7/7 8:7/7 9:6/8 10:6/8 11:7/7 12:7/7 13:6/8 14:7/8",
            string.Join(" ", counts));
    }

    [RequiresGameDataFact]
    public void TheGroundIsTexturedFromTheLevelsOwnTextureModifiers()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, TerrainSurface surface, WorldScene scene) = harness.LoadMission(1);

        // Level 1's objects.qsc declares nine TextureModifiers: three "Grass" and one more at a coarser
        // level on material 1, two "Rock" and one "Rock around base" on 5, two "Dirt inside base" on 4.
        Assert.Equal(9, surface.Layers.Length);
        Assert.Equal([1, 4, 5], surface.Layers.Select(l => l.MaterialId).Distinct().Order());

        // Sorted most specific first, which is the order the original's stack tests them in.
        for (int i = 1; i < surface.Layers.Length; i++)
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

        // This one view, over the base, reaches every material the level declares: the base ground,
        // the grass on the surrounding hills, and the dirt and rock patches in and around the base.
        Assert.Equal([0, 1, 4, 5], materials.Order());

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
    public void MissionThreeRemovesTerrainOverTheAmmunitionBuildingRamp()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, TerrainSurface surface, _) = harness.LoadMission(3);

        // Mission 3's TerrainDiscards container declares fourteen cubes. This level-15 one contains the
        // ammunition building's entrance and is the sheet of ground that otherwise covers its ramp.
        Assert.Equal(14, surface.Discards.Length);
        TerrainDiscardRegion ramp = Assert.Single(
            surface.Discards,
            discard => discard.Contains(23749052, -55320144, 174413136));
        Assert.Equal(15, ramp.Level);

        Vector3d centre = new(
            (ramp.MinX + ramp.MaxX) * 0.5,
            (ramp.MinY + ramp.MaxY) * 0.5,
            (ramp.MinZ + ramp.MaxZ) * 0.5);
        // Use a low-detail view from far away to get a coarse cube that would intersect the ramp.
        Camera3D distantCamera = new(
            new Vector3d(
                centre.X,
                centre.Y - (500.0 * WorldScene.UnitsPerMetre),
                centre.Z + (300.0 * WorldScene.UnitsPerMetre)),
            Camera3D.LookRotation(0f, -0.45f));
        TerrainView coarseView = TerrainView.FromPerspective(distantCamera, 1.2f, 16f / 9f);

        List<TerrainChunk> unmasked = [];
        TerrainTraversal.Collect(terrain.Octree, coarseView, unmasked);
        Assert.Contains(unmasked, chunk => ramp.IntersectsCube(chunk.Position, chunk.Extent));

        // Find a chunk that is face-adjacent to the ramp region but does not intersect it.
        TerrainChunk? adjacentChunk = unmasked.FirstOrDefault(chunk =>
            !ramp.IntersectsCube(chunk.Position, chunk.Extent)
            && (Math.Abs(chunk.Position.X - ramp.MinX) == chunk.Extent * 2
                || Math.Abs(chunk.Position.X - ramp.MaxX) == chunk.Extent * 2
                || Math.Abs(chunk.Position.Y - ramp.MinY) == chunk.Extent * 2
                || Math.Abs(chunk.Position.Y - ramp.MaxY) == chunk.Extent * 2
                || Math.Abs(chunk.Position.Z - ramp.MinZ) == chunk.Extent * 2
                || Math.Abs(chunk.Position.Z - ramp.MaxZ) == chunk.Extent * 2));

        List<TerrainChunk> masked = [];
        TerrainTraversal.Collect(terrain.Octree, coarseView, masked, surface.Discards);
        Assert.DoesNotContain(masked, chunk => ramp.IntersectsCube(chunk.Position, chunk.Extent));

        // Verify discard-driven refinement: an adjacent chunk remains even when a ramp-intersecting one is removed.
        if (adjacentChunk is not null)
        {
            Assert.Contains(masked, chunk =>
                chunk.Position.X == adjacentChunk.Value.Position.X
                && chunk.Position.Y == adjacentChunk.Value.Position.Y
                && chunk.Position.Z == adjacentChunk.Value.Position.Z);
        }

        // The same authored cube must remove collision, not merely the draw descriptor. The raw CTR/CMD
        // leaf is present at the building entrance—that is the invisible surface the player used to
        // stand on—but the task binding filters it before either exact terrain collision path sees it.
        const int entranceX = 23749052;
        const int entranceY = -55320144;
        const int entranceZ = 174413136;
        Vector3i entrance = new(entranceX, entranceY, entranceZ);
        TerrainDescentCursor raw = TerrainPointDescent.Descend(
            terrain.Octree,
            entrance,
            TerrainLeafQuery.SurfaceDepth);
        Assert.Equal(TerrainLeafQuery.SurfaceDepth, raw.Depth);
        Assert.True(terrain.Commands.TryGetBlock(
            terrain.Octree[raw.NodeIndex].CommandOffset,
            out TerrainCommandBlock rawBlock));
        Assert.False(rawBlock.IsEmpty);
        Assert.True(TerrainDiscardBinding.For(terrain).Discards(raw));
        Assert.False(TerrainLeafQuery.TryFindLeaf(terrain, entrance, out _));

        Assert.False(TerrainHeightQuery.TryGetGround(
            terrain,
            entranceX,
            entranceY,
            entranceZ + 8192.0,
            out _));
        Assert.False(TerrainRayTrace.TryTrace(
            terrain,
            new WorldTraceRequest(
                new Vector3d(entranceX, entranceY, ramp.MaxZ - 1.0),
                new Vector3d(entranceX, entranceY, ramp.MinZ + 1.0),
                WorldTraceMask.Terrain),
            out _));
    }

    [RequiresGameDataFact]
    public void EveryTileATileMapNamesExistsInTheLevelsAtlas()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
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
    public void TheDeadLevelsTerrainReportsItselfUnusable()
    {
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        Assert.True(harness.Registry.TryGet(14, out MissionDescriptor? descriptor));

        TerrainData? terrain = harness.Terrain.Load(descriptor.TerrainBasePath);

        // Its files parse — they are the right shape and the right size — but 14400 of its 15202 nodes
        // address no block, so nothing may descend it. The original relocates those offsets regardless.
        Assert.NotNull(terrain);
        Assert.False(terrain.IsUsable);
        Assert.NotEmpty(terrain.Defects);
    }
}
