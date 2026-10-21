using OpenIGI.Core.Numerics;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Rendering.Tests;

[Collection(SharedPresetRegistry.Name)]
public sealed class MeshBatchAndOcclusionTests
{
    [Fact]
    public void DrawMeshRanges_SubmitsEveryRangeAsOneBatch()
    {
        RecordingGraphicsDevice device = new();
        MeshDrawRange[] ranges = [new(0, 6), new(12, 3), new(30, 9)];

        device.DrawMeshRanges(
            new MeshHandle(7),
            ranges,
            Matrix4f.Identity,
            new TextureHandle(3),
            TextureHandle.None,
            BlendMode.Opaque);

        Assert.Equal(1, device.MeshBatchDrawCount);
        Assert.Equal(ranges, device.MeshDraws.Select(draw => new MeshDrawRange(draw.IndexOffset, draw.IndexCount)));
    }

    [Fact]
    public void RecordingOcclusionQueries_AreConservativeAndNeverHideGeometry()
    {
        RecordingGraphicsDevice device = new();
        OcclusionQueryHandle query = device.CreateOcclusionQuery();

        device.IssueOcclusionQuery(
            query,
            Matrix4f.Identity,
            new OcclusionBounds(new Vector3f(-1f), new Vector3f(1f)));

        Assert.True(query.IsValid);
        Assert.True(device.OcclusionVisible(query));
    }

    [Fact]
    public void TerrainOcclusion_DefaultsOffUntilAViewProvesItProfitable()
    {
        Assert.False(GeometryCVars.TerrainOcclusion.DefaultValue);
    }

    /// <summary>Each tier holds more ground than it draws, because a player turns round.</summary>
    /// <remarks>
    /// A cache sized to the frustum evicts what is behind the player and rebuilds it as they turn back.
    /// A whole turn from one spot on level 1 touches 2363 chunks at the enhanced view distance, 4681 at
    /// high and 8979 at ultra, so each tier's figure has to clear its own. The
    /// tiers are set to between two and three times these, because a player who walks while they turn
    /// sees more ground than a fixed-spot measurement does; what is pinned here is the floor, not that
    /// margin.
    /// </remarks>
    [Theory]
    [InlineData(GraphicsPreset.Vanilla, 4096)]
    [InlineData(GraphicsPreset.Enhanced, 2363)]
    [InlineData(GraphicsPreset.High, 4681)]
    [InlineData(GraphicsPreset.Ultra, 8979)]
    public void TerrainCache_HoldsAWholeTurnAtEveryTierThatDrawsOne(GraphicsPreset preset, int atLeast)
    {
        _ = GeometryCVars.TerrainCache;

        GraphicsPresetRegistry.Default.Apply(preset);

        Assert.True(
            GeometryCVars.TerrainCache.Value >= atLeast,
            $"{preset} keeps {GeometryCVars.TerrainCache.Value} chunks, under the {atLeast} a turn wants");
    }
}
