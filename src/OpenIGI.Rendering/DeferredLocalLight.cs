using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// One real-time local light handed to the deferred lighting for a single frame — a lamp fixture a
/// level's emissive geometry implies, treated as a punctual source.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. declares no point lights at all; these are inferred from where a level's emissive textures are
/// drawn (see the game layer's light-fixture proposer). This struct is the renderer-facing form of one:
/// the position is already in <b>view space</b>, the frame the lighting pass shades in, so the game layer
/// does the world-to-view transform in double precision — the level sits at large world coordinates and
/// subtracting the camera in float first would cost most of the mantissa.
/// </para>
/// <para>
/// The set is uploaded whole each frame and cleared after the pass, so a frame that submits none lights
/// from none. <see cref="Maximum"/> is the shader's array size; a longer list is truncated.
/// </para>
/// </remarks>
/// <param name="ViewPosition">The light's position in view space, in metres.</param>
/// <param name="Radius">How far the light reaches before its windowed falloff takes it to zero, in metres.</param>
/// <param name="Colour">The light's linear colour, its brightest channel around one.</param>
/// <param name="Intensity">The light's relative strength, already scaled by whatever global knob the caller applies.</param>
/// <param name="ViewDirection">
/// The way the fixture faces, in view space, or zero for a light that shines every way.
/// <para>
/// A lamp is a housing with an opening: it throws light out of the opening and nothing out of the
/// slab it is bolted to. Lit as a bare point instead, every ceiling fixture in the game puts a bright
/// disc on the ceiling ABOVE it — light on the one surface that is physically behind the emitter —
/// and there is nowhere in a room to stand where that is not visible.
/// </para>
/// <para>
/// Zero is a real answer and not a failure. A fixture whose geometry does not agree on a facing —
/// a lamp modelled as a closed box, whose faces cancel — is better lit as a point than as a spot
/// aimed at a guess, and the shader reads a zero vector as exactly that.
/// </para>
/// </param>
/// <param name="SourceRadius">
/// How big the fixture itself is, in metres — not how far it reaches, which is <paramref name="Radius"/>.
/// <para>
/// This is what makes a shadow a shadow rather than a stencil. Traced as a point, a lamp gives one
/// binary answer per pixel: a hard edge, aliased along it, and identical whether the thing casting it is
/// touching the wall or a room away. A real fitting is tens of centimetres across, so its shadow has a
/// penumbra that widens with the gap between occluder and receiver — sharp under a crate, soft on the
/// wall behind it. Only the source's size can say how wide.
/// </para>
/// <para>
/// The fixture's own extent, so it costs nothing to know: the extractor already measures how far the
/// emitting geometry reaches from its centroid, because it needs that for the light's reach.
/// </para>
/// </param>
public readonly record struct DeferredLocalLight(
    Vector3f ViewPosition,
    float Radius,
    Vector3f Colour,
    float Intensity,
    Vector3f ViewDirection = default,
    float SourceRadius = 0f)
{
    /// <summary>The most local lights a single frame can carry, matching the lighting shader's array size.</summary>
    /// <remarks>
    /// <para>
    /// Sixty-four rather than thirty-two, because a level has more lamps than this and the ones that do
    /// not fit are SWITCHED OFF: with a smaller budget the far end of a corridor stayed dark and lit
    /// itself a few at a time as the player walked towards it, which reads as the lighting failing rather
    /// than as a budget being spent. Doubling it is most of the fix, and it is nearly free — the shader
    /// culls a light against its own reach before doing any work for it, so a lamp the pixel is nowhere
    /// near costs one distance test.
    /// </para>
    /// <para>
    /// <b>A hundred and sixty, which the OpenGL backend cannot hold.</b> Sixty-four was its ceiling —
    /// default-block array uniforms, three <c>vec4</c> arrays deep, 768 components of a guaranteed 1024
    /// — and holding the Vulkan path to it meant lamps still went out as the player crossed a compound
    /// with more fittings than slots. That backend is on its way out and Vulkan carries these in a
    /// uniform block with room to spare, so the ceiling now follows the backend that is staying. If
    /// OpenGL is ever revived it needs its own, smaller, budget rather than this one lowered.
    /// </para>
    /// </remarks>
    public const int Maximum = 160;
}
