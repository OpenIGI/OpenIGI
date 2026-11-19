using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>One copy of a mesh range in <see cref="IGraphicsDevice.DrawMeshInstances"/>: what differs from copy to copy.</summary>
/// <param name="ModelView">The copy's model-to-view transform.</param>
/// <param name="Lightmap">Its lightmap texture, or <see cref="TextureHandle.None"/> for none.</param>
/// <param name="VertexSun">
/// The sun it is lit by, as <see cref="IGraphicsDevice.SetVertexSun"/> takes it; read only when the call says the copies carry
/// their own.
/// </param>
public readonly record struct MeshInstance(Matrix4f ModelView, TextureHandle Lightmap, Vector3f VertexSun = default);
