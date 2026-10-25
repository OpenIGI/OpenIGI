using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using static OpenIGI.Game.Tests.RetailTerrainHarness;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Asks retail terrain where the ground is: the leaf query, the ground normals and heights under the
/// level's objects, and every mission's player spawn standing on it.
/// </summary>
public sealed class RetailTerrainGroundQueryTests
{
    [RequiresGameDataFact]
    public void TheLeafQueryLandsOnGeometryOverTheLevelsOwnGround()
    {
        // The front of 0x4CB050: descend to depth 16 and restate the point in the leaf's frame. This is
        // the step before the surface test, so what it must prove is that real ground actually resolves
        // to real triangles.
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
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
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
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
    public void EveryGroundNormalAcrossLevelOneFacesUp()
    {
        // The mirrored-leaf regression, level-wide. A wide grid reaches the leaves the flat centre never
        // touches — including the mirrored ones (orientation bit 2) whose winding is reversed. Every
        // ground normal a heightmap can produce faces up; a single downward one is the v105 sign
        // correction (0x4CA010) gone missing, which the slope-slide turns into an unwalkable hillside.
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (TerrainData terrain, _, WorldScene scene) = harness.LoadMission(1);

        (double centreX, double centreY, double ground) = Medians(scene);

        int hits = 0;

        // ±150 m of the centre at 5 m spacing — 3721 columns, wide enough to cross many leaves.
        for (int dx = -30; dx <= 30; dx++)
        {
            for (int dy = -30; dy <= 30; dy++)
            {
                double x = (centreX + (dx * 5.0)) * WorldScene.UnitsPerMetre;
                double y = (centreY + (dy * 5.0)) * WorldScene.UnitsPerMetre;

                // The ground query only reaches four metres, so a whole column is scanned in probe-length
                // steps from well above to well below the centre's height — the same descent a placement
                // uses to land on a raised leaf.
                double top = (ground + 200.0) * WorldScene.UnitsPerMetre;
                double floor = (ground - 200.0) * WorldScene.UnitsPerMetre;

                for (double probe = top; probe > floor; probe -= TerrainHeightQuery.SearchDepthUnits)
                {
                    if (!TerrainHeightQuery.TryGetGround(terrain, x, y, probe, out TerrainGround hit))
                    {
                        continue;
                    }

                    hits++;
                    Assert.True(hit.NormalZ > 0.0, $"ground normal faces down (z={hit.NormalZ}) at ({x}, {y})");
                    break;
                }
            }
        }

        Assert.True(hits > 500, $"only {hits} of 3721 columns found ground");
    }

    [RequiresGameDataFact]
    public void TheGroundQueryFindsRealTerrainUnderLevelOne()
    {
        // The whole chain end to end - descend, reach the leaf, interpolate the triangle - which is the
        // query nothing in the port could do before.
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
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

                // Terrain is a heightmap with no overhangs, so every ground normal faces up. A mirrored
                // leaf reverses triangle winding; without the 0x4CA010 v105 sign correction its normal
                // points DOWN, which SlopeDegrees reads as past-vertical and the slope-slide then throws
                // the player off perfectly walkable ground.
                Assert.True(hit.NormalZ > 0.0, $"ground normal faces down (z={hit.NormalZ}) at ({x}, {y})");
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
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
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
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
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
        RetailTerrainHarness harness = RetailTerrainHarness.Create();

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

    [RequiresGameDataTheory]
    [InlineData(1, 3.09598708152771f)]
    [InlineData(2, 3.6479880809783936f)]
    [InlineData(3, -3.455751895904541f)]
    [InlineData(4, 2.9321529865264893f)]
    [InlineData(5, 2.5132739543914795f)]
    [InlineData(6, 1.4660769701004028f)]
    [InlineData(7, 4.2895960807800293f)]
    [InlineData(8, 3.6651909351348877f)]
    [InlineData(9, 4.2935090065002441f)]
    [InlineData(10, 0f)]
    [InlineData(11, 1.3071240186691284f)]
    [InlineData(12, -0.31415900588035583f)]
    [InlineData(13, 3.979351043701172f)]
    [InlineData(14, -1.3799949884414673f)]
    public void HumanPlayerTaskGammaMatchesTheAuthoredObjectsFile(int missionId, float expectedGamma)
    {
        // These values are the fourth argument of Task_New(0, "HumanPlayer", ...) in each shipped
        // missions/location0/levelN/objects.qvm.  Keep the parser tied to the retail object task,
        // rather than an opening camera or a hand-maintained spawn table.
        RetailTerrainHarness harness = RetailTerrainHarness.Create();
        (_, LevelTask levelRoot) = harness.LoadMissionTree(missionId);

        Assert.True(PlayerSpawn.TryFindGameplay(levelRoot, out PlayerSpawnPoint spawn));
        Assert.Equal(expectedGamma, spawn.Gamma, precision: 6);
    }
}
