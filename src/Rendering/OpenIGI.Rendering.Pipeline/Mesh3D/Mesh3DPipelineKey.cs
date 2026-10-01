namespace OpenIGI.Rendering.Pipeline.Mesh3D;

/// <summary>The render state that identifies one world-pipeline variant.</summary>
/// <remarks>
/// Depth behaviour, culling and blending are baked into a pipeline on both backends, so together they
/// name the variant a draw needs. Used as a dictionary key, which is why it is a value with structural
/// equality.
/// </remarks>
/// <param name="Depth">How the draw interacts with the depth buffer.</param>
/// <param name="Cull">Which triangle facings the draw discards.</param>
/// <param name="Blend">How fragments combine with the target.</param>
/// <param name="AlphaReference">
/// How much texture alpha a cut-out fragment needs to survive, out of 255. Part of the key because it
/// is a specialisation constant rather than a push value: both mesh push blocks are already the 128
/// bytes the hardware guarantees, and every float in them is spoken for. A byte keeps the variant space
/// finite — a level uses two or three of the 256 values, and the pipelines are built on demand.
/// </param>
/// <param name="Reflective">
/// Whether the variant samples the bound environment cube. Also a constant, and it buys more than the
/// threshold does: a cube lookup that every draw in the world computed and threw away would be a fetch
/// per fragment for the one surface in the level that reflects.
/// </param>
public readonly record struct Mesh3DPipelineKey(
    DepthState Depth,
    CullMode Cull,
    BlendMode Blend,
    byte AlphaReference,
    bool Reflective);
