using OpenIGI.Rendering;

namespace OpenIGI.Rendering.Vulkan.Pipelines;

/// <summary>The render state that identifies one world-pipeline variant.</summary>
/// <remarks>
/// Depth behaviour, culling and blending are the three pieces of state baked into a Vulkan pipeline that
/// the world draws vary, so together they name the variant a draw needs. Used as a dictionary key, which
/// is why it is a value with structural equality.
/// </remarks>
/// <param name="Depth">How the draw interacts with the depth buffer.</param>
/// <param name="Cull">Which triangle facings the draw discards.</param>
/// <param name="Blend">How fragments combine with the target.</param>
/// <param name="AlphaReference">
/// How much texture alpha a cut-out fragment needs to survive, out of 255. Part of the key because it is
/// a specialization constant rather than a push value — see <see cref="AlphaReferenceSpecialization"/>
/// for why it has to be, and why a byte keeps the variant space finite.
/// </param>
/// <param name="Reflective">
/// Whether the variant samples the bound environment cube. Also a specialization constant, so that the
/// lookup is compiled out of every pipeline the rest of the world draws through.
/// </param>
/// <param name="FogTarget">
/// The colour the variant fogs towards. A specialization constant for the same reason: three values,
/// a property of a run of draws, and no push room. See <see cref="FogTarget"/>.
/// </param>
internal readonly record struct Mesh3DPipelineKey(
    DepthState Depth,
    CullMode Cull,
    BlendMode Blend,
    byte AlphaReference,
    bool Reflective,
    FogTarget FogTarget = FogTarget.Scene);
