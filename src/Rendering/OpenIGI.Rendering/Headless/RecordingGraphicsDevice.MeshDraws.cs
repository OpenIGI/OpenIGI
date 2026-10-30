using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>Gets the mesh draws, in order.</summary>
    public List<MeshDrawCall> MeshDraws { get; } = [];

    /// <summary>Gets every <see cref="DrawWater"/>: the mesh, its transform, the cube and what it was lit by.</summary>
    public List<(MeshHandle Mesh, int IndexCount, Matrix4f ModelView, TextureHandle Cube, WaterShading Shading)> WaterDraws { get; } = [];

    /// <inheritdoc/>
    public void DrawMesh(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        if (ObjectMotion)
        {
            Record(MovingDraws, mesh);
        }

        Record(MeshDraws, new MeshDrawCall(
            mesh,
            indexOffset,
            indexCount,
            modelView,
            diffuse,
            lightmap,
            blend,
            DepthState,
            CullMode,
            Fog,
            ModelAmbient,
            LightmapScale,
            VertexSun,
            FogTarget));
    }

    /// <inheritdoc/>
    public void DrawWater(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle cube,
        in WaterShading shading) =>
        Record(WaterDraws, (mesh, indexCount, modelView, cube, shading));

    /// <inheritdoc/>
    public void DrawMeshRanges(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        TextureHandle diffuse,
        TextureHandle lightmap,
        BlendMode blend)
    {
        MeshBatchDrawCount++;
        foreach (MeshDrawRange range in ranges)
        {
            DrawMesh(mesh, range.IndexOffset, range.IndexCount, modelView, diffuse, lightmap, blend);
        }
    }

    /// <summary>Gets how many silhouette draws have been submitted.</summary>
    public int OutlineDrawCount { get; private set; }

    /// <summary>Gets the last outline's colour.</summary>
    public Bgra32 LastOutlineColour { get; private set; }

    /// <summary>Gets whether the last outline ignored the depth buffer.</summary>
    public bool LastOutlineThroughWalls { get; private set; }

    /// <inheritdoc/>
    public void DrawOutline(
        MeshHandle mesh,
        ReadOnlySpan<MeshDrawRange> ranges,
        in Matrix4f modelView,
        Bgra32 colour,
        float radius,
        bool throughWalls)
    {
        OutlineDrawCount++;
        LastOutlineColour = colour;
        LastOutlineThroughWalls = throughWalls;
    }

    /// <summary>Gets how many silhouette marks have been drawn.</summary>
    public int SilhouetteDrawCount { get; private set; }

    /// <summary>Gets the last silhouette mark's group.</summary>
    public SilhouetteGroup LastSilhouetteGroup { get; private set; }

    /// <inheritdoc/>
    public void DrawSilhouette(MeshHandle mesh, ReadOnlySpan<MeshDrawRange> ranges, in Matrix4f modelView, SilhouetteGroup group)
    {
        SilhouetteDrawCount++;
        LastSilhouetteGroup = group;
    }

    /// <summary>Gets how many mesh ranges were drawn in batches.</summary>
    public int MeshBatchDrawCount { get; private set; }
}
