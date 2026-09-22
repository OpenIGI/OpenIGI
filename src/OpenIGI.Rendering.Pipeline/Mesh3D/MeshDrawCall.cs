using OpenIGI.Rendering.Pipeline.Meshes;

namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>One run of a mesh's indices, with the state it is drawn in.</summary>
/// <param name="Mesh">The mesh.</param>
/// <param name="IndexOffset">The first index of the run.</param>
/// <param name="IndexCount">How many indices.</param>
/// <param name="Diffuse">The diffuse texture.</param>
/// <param name="Lightmap">The baked lightmap.</param>
/// <param name="Detail">The surface detail, which is the neutral map where a diffuse has none.</param>
/// <param name="Blend">How the draw combines with what is already there.</param>
/// <param name="Depth">How it interacts with the depth buffer.</param>
/// <param name="Cull">Which facing it discards.</param>
/// <param name="AlphaReference">The cut-out threshold out of 255.</param>
/// <param name="Reflective">Whether it samples the bound environment cube.</param>
public readonly record struct MeshDrawCall(
    RhiMesh Mesh,
    int IndexOffset,
    int IndexCount,
    TextureHandle Diffuse,
    TextureHandle Lightmap,
    TextureHandle Detail,
    BlendMode Blend = BlendMode.Opaque,
    DepthState Depth = DepthState.TestAndWrite,
    CullMode Cull = CullMode.CounterClockwise,
    byte AlphaReference = Mesh3DConstants.DefaultAlphaReference,
    bool Reflective = false);
