using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.TestSupport;

public sealed partial class RecordingGraphicsDevice
{
    public sealed record DrawCall(Vertex2D[] Vertices, ushort[] Indices, TextureHandle Texture, BlendMode Blend)
    {
        public int QuadCount => Vertices.Length / 4;
    }

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
        public int TriangleCount => IndexCount / 3;
    }
}
