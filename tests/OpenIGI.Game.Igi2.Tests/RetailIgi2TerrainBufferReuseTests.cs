using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>Frame terrain storage is reused without changing geometry for a moving camera.</summary>
/// <param name="output">The measured allocation results.</param>
public sealed class RetailIgi2TerrainBufferReuseTests(ITestOutputHelper output)
{
    /// <summary>Repeated views and detail changes produce exactly the same vertices and material passes as owned tessellations.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Reused_buffers_match_owned_geometry_across_view_and_detail_changes()
    {
        Igi2TerrainSurface surface = Igi2Level.Find("location1", "level1").Terrain();
        Igi2TerrainTile tile = surface.Tiles[0];
        Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);
        Igi2TerrainLodSettings settings = new(surface.Terrain!.Detail, surface.Terrain.Adaption);
        Vector3d origin = tile.PositionOf(tile.Width / 2, tile.Height / 2);
        Igi2TerrainView initial = new(origin + new Vector3d(0, 0, 8192), []);
        Igi2TerrainMesh retained = lod.Tessellate(initial, settings).Mesh;
        Vector3d[] saved = retained.Positions.ToArray();
        for (int frame = 0; frame < 5; frame++)
        {
            Igi2TerrainView view = new(initial.Eye + new Vector3d(frame * 4096, frame * -2048, frame * 512), []);
            Igi2TerrainLodSettings detail = frame % 2 == 0 ? settings : settings with { Detail = settings.Detail * 2f };
            Igi2TerrainTessellation expected = lod.Tessellate(view, detail);
            Igi2TerrainTessellation actual = lod.TessellateFrame(view, detail);
            Assert.Equal(expected.Mesh.Positions, actual.Mesh.Positions);
            Assert.Equal(expected.Mesh.Normals, actual.Mesh.Normals);
            Assert.Equal(expected.Mesh.Lights, actual.Mesh.Lights);
            Assert.Equal(expected.Mesh.GroundIndices.ToArray(), actual.Mesh.GroundIndices.ToArray());
            Assert.Equal(expected.Mesh.Passes.Count, actual.Mesh.Passes.Count);
            for (int pass = 0; pass < expected.Mesh.Passes.Count; pass++)
            {
                Igi2TerrainPass a = expected.Mesh.Passes[pass], b = actual.Mesh.Passes[pass];
                Assert.Equal(a.MaterialId, b.MaterialId);
                Assert.Equal(a.BaseTriangleCount, b.BaseTriangleCount);
                Assert.Equal(a.Indices.ToArray(), b.Indices.ToArray());
                Assert.Equal(a.Weights.ToArray(), b.Weights.ToArray());
            }
        }
        Assert.Equal(saved, retained.Positions);
    }

    /// <summary>After warming the tile, traversal no longer allocates fresh vertex, edge, weight and triangle arrays.</summary>
    [RequiresTitleDataFact("igi2")]
    public void Warm_frame_tessellation_uses_less_than_one_percent_of_owned_allocations()
    {
        Igi2TerrainSurface surface = Igi2Level.Find("location1", "level1").Terrain();
        Igi2TerrainTile tile = surface.Tiles[0];
        Igi2TerrainLod lod = new(tile, surface.Materials, surface.Discards);
        Igi2TerrainLodSettings settings = new(surface.Terrain!.Detail, surface.Terrain.Adaption);
        Igi2TerrainView view = new(tile.PositionOf(tile.Width / 2, tile.Height / 2) + new Vector3d(0, 0, 8192), []);
        lod.TessellateFrame(view, settings);
        lod.TessellateFrame(view, settings);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 10; frame++) { lod.TessellateFrame(view, settings); }
        long reused = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 10; frame++) { lod.Tessellate(view, settings); }
        long owned = GC.GetAllocatedBytesForCurrentThread() - start;
        output.WriteLine(FormattableString.Invariant($"Ten terrain frames: reused {reused} bytes; owned {owned} bytes."));
        Assert.True(reused * 100 < owned, $"Reused {reused} bytes; owned {owned} bytes for ten frames.");
    }
}
