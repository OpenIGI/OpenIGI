using OpenIGI.Core.Numerics;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Rendering.Tests;

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
        Assert.False(RenderCVars.TerrainOcclusion.DefaultValue);
    }
}
