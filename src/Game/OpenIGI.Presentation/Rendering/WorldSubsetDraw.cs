using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>One drawable subset: its index range and the states it binds.</summary>
/// <remarks>
/// <c>IsLamp</c> says the sub-mesh glows because the lamp detection called it a fixture, as opposed
/// to because a material file said the surface emits. Only the first kind answers to a light switch:
/// an authored emissive is a screen or a sign or the sky, and nobody is switching those off.
/// </remarks>
/// <param name="IndexOffset">The first index of the subset's range.</param>
/// <param name="IndexCount">How many indices it draws.</param>
/// <param name="TriangleCount">How many triangles that is, for the tallies.</param>
/// <param name="Diffuse">The texture it is drawn with.</param>
/// <param name="Material">What the surface is made of.</param>
/// <param name="Lightmap">The baked lightmap page it samples, or none.</param>
/// <param name="IsLamp">Whether the lamp detection called it a fixture.</param>
internal readonly record struct WorldSubsetDraw(
    int IndexOffset,
    int IndexCount,
    int TriangleCount,
    TextureHandle Diffuse,
    SurfaceMaterial Material,
    TextureHandle Lightmap,
    bool IsLamp = false);
