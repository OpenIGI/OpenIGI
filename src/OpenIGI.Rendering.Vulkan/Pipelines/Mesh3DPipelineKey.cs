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
internal readonly record struct Mesh3DPipelineKey(DepthState Depth, CullMode Cull, BlendMode Blend);
