using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>One 2-D draw as it was asked for.</summary>
    /// <param name="Vertices">The vertices.</param>
    /// <param name="Indices">The indices.</param>
    /// <param name="Texture">The texture.</param>
    /// <param name="Blend">The blend.</param>
    public sealed record DrawCall(Vertex2D[] Vertices, ushort[] Indices, TextureHandle Texture, BlendMode Blend)
    {
        /// <summary>Gets how many quads the draw's vertices make.</summary>
        public int QuadCount => Vertices.Length / 4;
    }

    /// <summary>One mesh draw as it was asked for, with the state it was drawn under.</summary>
    /// <param name="Mesh">The mesh.</param>
    /// <param name="IndexOffset">The first index.</param>
    /// <param name="IndexCount">The indices drawn.</param>
    /// <param name="ModelView">The model-view transform.</param>
    /// <param name="Diffuse">The diffuse texture.</param>
    /// <param name="Lightmap">The lightmap.</param>
    /// <param name="Blend">The blend.</param>
    /// <param name="DepthState">The depth state.</param>
    /// <param name="CullMode">The cull mode.</param>
    /// <param name="Fog">The fog.</param>
    /// <param name="ModelAmbient">The model ambient.</param>
    /// <param name="LightmapScale">The lightmap scale.</param>
    /// <param name="VertexSun">The vertex sun.</param>
    /// <param name="FogTarget">The fog target.</param>
    public sealed record MeshDrawCall(
        MeshHandle Mesh,
        int IndexOffset,
        int IndexCount,
        Matrix4f ModelView,
        TextureHandle Diffuse,
        TextureHandle Lightmap,
        BlendMode Blend,
        DepthState DepthState,
        CullMode CullMode,
        SceneFog Fog = default,
        Vector3f ModelAmbient = default,
        float LightmapScale = 1f,
        Vector3f VertexSun = default,
        FogTarget FogTarget = FogTarget.Scene)
    {
        /// <summary>Gets how many triangles the draw covers.</summary>
        public int TriangleCount => IndexCount / 3;
    }
}
