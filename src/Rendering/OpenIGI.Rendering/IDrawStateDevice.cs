using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>Device state the following draws are made under: depth, culling, projection, the terrain fade, the material and how cut-outs are tested.</summary>
/// <remarks>Each member spans a run of draws rather than one, which is why it is state and not a draw argument. Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IDrawStateDevice
{
    /// <summary>The cut-out threshold a device starts at, and returns to.</summary>
    /// <remarks>
    /// 128, which is the half this port has always tested at to within a texel value — the shader's
    /// literal was <c>0.5</c> and <c>128 / 255</c> is <c>0.502</c>. Chosen so that making the threshold
    /// settable changes no picture until somebody asks it to. It is <em>not</em> either of I.G.I. 2's
    /// numbers; see <see cref="SetAlphaReference"/>.
    /// </remarks>
    const byte DefaultAlphaReference = 128;

    /// <summary>Sets how subsequent draws interact with the depth buffer.</summary>
    /// <param name="state">The depth behaviour.</param>
    void SetDepthState(DepthState state);

    /// <summary>Sets which triangle facings subsequent draws discard.</summary>
    /// <param name="mode">The culling rule.</param>
    void SetCullMode(CullMode mode);

    /// <summary>Sets the projection applied to subsequent <see cref="IMeshDrawDevice.DrawMesh"/> calls.</summary>
    /// <param name="projection">The view-to-clip transform.</param>
    /// <remarks>Set once per frame; the per-object transform travels with each draw instead.</remarks>
    void SetProjection(in Matrix4f projection);

    /// <summary>Applies the terrain LOD fade to subsequent <see cref="IMeshDrawDevice.DrawMesh"/> calls.</summary>
    /// <param name="scale">What one unit of Chebyshev camera distance is worth.</param>
    /// <param name="offset">Subtracted from the scaled distance.</param>
    /// <param name="slope">Scales the offset distance into the fade's <c>[0, 1]</c>.</param>
    /// <param name="fadeAlpha">
    /// Whether the draw's alpha also fades out with the factor — the detail pass does, the base pass
    /// only morphs.
    /// </param>
    /// <remarks>
    /// The fade is <c>t = clamp((chebyshev * scale - offset) * slope, 0, 1)</c> per vertex, the distance
    /// taken along the world axes, exactly as <c>sub_512C30</c> computes it. While set, a vertex slides
    /// onto the morph target its normal channel carries as <c>t</c> rises — the seam morph — and with
    /// <paramref name="fadeAlpha"/> the alpha is additionally multiplied by <c>1 - t</c>. Cleared by
    /// <see cref="ClearDetailFade"/>; draws between the two calls are affected, everything else is not.
    /// </remarks>
    void SetDetailFade(float scale, float offset, float slope, bool fadeAlpha);

    /// <summary>
    /// Sets the terrain's level-of-detail slide for a pass that covers the chunk once and takes no share of
    /// the material cross-fade: the seam morph applies, the weights do not.
    /// </summary>
    /// <param name="scale">As <see cref="SetDetailFade(float, float, float, bool)"/>.</param>
    /// <param name="offset">As <see cref="SetDetailFade(float, float, float, bool)"/>.</param>
    /// <param name="slope">As <see cref="SetDetailFade(float, float, float, bool)"/>.</param>
    /// <remarks>Project I.G.I.'s terrain lightmap pass, which multiplies the whole chunk after its material passes.</remarks>
    void SetDetailFadeUnweighted(float scale, float offset, float slope) => SetDetailFade(scale, offset, slope, fadeAlpha: false);

    /// <summary>Stops the terrain fade; subsequent <see cref="IMeshDrawDevice.DrawMesh"/> calls draw unmodified.</summary>
    void ClearDetailFade();

    /// <summary>Sets the flat colour every following model with no lightmap is multiplied by.</summary>
    /// <param name="ambient">The level's ambient, or <see cref="Vector3f.One"/> to leave models alone.</param>
    /// <remarks>
    /// The original's model lighting, and nearly all of it: a model sub-mesh with no lightmap is drawn
    /// with Direct3D lighting on (<c>sub_49F1C0</c>), whose whole contribution is the global
    /// <c>D3DRENDERSTATE_AMBIENT</c> against a material that reflects it entirely — the directional
    /// lights the state block sets alongside it arrive scaled by the world's own <c>0.001</c> and are
    /// worth a quarter of one 8-bit level. A sub-mesh WITH a lightmap is drawn with lighting off and the
    /// lightmap in the second texture stage, which is why this applies to one and not the other.
    /// </remarks>
    void SetModelAmbient(Vector3f ambient);

    /// <summary>Gets the flat model light the last <see cref="SetModelAmbient"/> set.</summary>
    /// <remarks>So that a renderer which must draw untinted — the sky — can put back what it found.</remarks>
    Vector3f ModelAmbient { get; }

    /// <summary>Sets the colour the following draws fog towards, until it is set again.</summary>
    /// <param name="target">The colour: the scene's fog, half of it, or white.</param>
    /// <remarks>
    /// For I.G.I. 2's ground, whose three multiplied passes each fog to a different colour so that the
    /// product lands on the scene's fog at distance — see <see cref="FogTarget"/> for the state blocks.
    /// Device state, like <see cref="IGraphicsDevice.SetLightmapModulates"/>: it spans a run of draws and must be put
    /// back to <see cref="FogTarget.Scene"/> after them. A title that never calls this fogs everything
    /// to the scene's colour, which is what the forward path always did. It reaches a draw through
    /// <see cref="SceneFog.ColourFor(BlendMode, FogTarget)"/>, alongside the colour itself.
    /// </remarks>
    void SetFogTarget(FogTarget target)
    {
        _ = target;
    }

    /// <summary>Sets the fixed-function sun a title's unlightmapped objects are lit by, for every draw until it is set again.</summary>
    /// <param name="ambient">The flat light every such vertex receives, linear.</param>
    /// <param name="colour">The directional light's diffuse colour, linear.</param>
    /// <remarks>
    /// <para>
    /// The colours are a LEVEL'S CONSTANTS, like the fog: I.G.I. 2 reads them off its <c>Dirlight</c> and
    /// <c>GlobalLight</c> when the mission loads (<c>DirlightQTask_DrawCallback</c>, <c>igi2ed 0x657680</c>;
    /// <c>GlobalLightQTask_DrawCallback</c>, <c>0x6534E0</c>) and the backend is free to bake them in.
    /// The direction is not, because it is applied in each object's own space: see
    /// <see cref="IGraphicsDevice.SetVertexSun"/>, which is what switches the lighting on for a run of draws at all.
    /// </para>
    /// <para>
    /// A title that has no such light never calls this and both stay black, which lights nothing.
    /// </para>
    /// </remarks>
    void SetSceneSun(Vector3f ambient, Vector3f colour)
    {
        _ = ambient;
        _ = colour;
    }

    /// <summary>Sets how the following draws respond to light.</summary>
    /// <param name="material">The surface properties, from the port's own material table.</param>
    /// <remarks>
    /// Only the deferred path reads this; on the original's path nothing does. It is device state
    /// rather than a draw argument because a material spans a run of sub-meshes.
    /// </remarks>
    void SetMaterialOverride(in SurfaceMaterial material);

    /// <summary>Narrows the following draws to a part of the view, or lets them cover all of it again.</summary>
    /// <param name="clip">The part of the view, or <see langword="null"/> for all of it.</param>
    /// <remarks>
    /// A scissor over the current viewport, kept until it is changed: the sector cull's window onto the outside, which the
    /// original clips the outside's geometry to. A pass opened while it is set is opened narrowed. Ignored by a device that
    /// cannot scissor, which then draws the whole view as it always did.
    /// </remarks>
    void SetDrawClip(in ViewClip? clip)
    {
        _ = clip;
    }

    /// <summary>Sets the headroom the following draws' lightmap pages were encoded with.</summary>
    /// <param name="scale">
    /// The value each sampled lightmap is multiplied by after it is decoded. One for the shipped LDR pages;
    /// a path-traced HDR page stores the radiance divided by this before its sRGB encode, so scaling it back
    /// up recovers a lightmap that keeps its bright values above one instead of clipping.
    /// </param>
    /// <remarks>Device state, like <see cref="SetMaterialOverride"/>: it spans a run of draws.</remarks>
    void SetLightmapScale(float scale);

    /// <summary>Marks the draws that follow as geometry that exists only in front of the eye.</summary>
    /// <param name="firstPerson">Whether the following draws are the view model rather than the world.</param>
    /// <remarks>
    /// <para>
    /// The arms and the held weapon are drawn into the same lit colour as the world, and two passes read
    /// that colour back as though everything in it were world: the eye adaptation meters it, and the
    /// screen-space indirect gather traces against it. Neither is true of a view model. A white knife
    /// held up to the camera raises the metered luminance and darkens the whole frame, and bounces light
    /// off itself onto the room -- so pressing the free-camera key, which simply stops drawing it,
    /// visibly changes the lighting of a scene that has not moved.
    /// </para>
    /// <para>
    /// Device state, like <see cref="SetLightmapScale"/>: it spans a run of draws and must be cleared
    /// after them. A backend that does not separate the two may ignore it.
    /// </para>
    /// </remarks>
    void SetFirstPersonGeometry(bool firstPerson);

    /// <summary>Marks the draws that follow as moving under their own power.</summary>
    /// <param name="moving">Whether the object being drawn has its own motion this frame.</param>
    /// <param name="identity">
    /// Which object this is, the same number every frame, or nought to pair by the mesh alone. A renderer that
    /// skips some of its objects on some frames -- occlusion, a frustum -- changes which draw of a shared mesh
    /// comes n-th, and paired by the mesh alone the n-th draw of a crate is a different crate from one frame
    /// to the next.
    /// </param>
    /// <remarks>
    /// <para>
    /// The velocity the temporal passes read is derived from depth and the two cameras, so it describes only
    /// what the camera did. A car, a jet, a train or a swinging door drawn without this is reprojected as if it
    /// were the scenery behind it, and its history trails behind it as a ghost. With it set, each geometry draw
    /// is paired with its own transform from the frame before and stamps its true motion.
    /// </para>
    /// <para>
    /// Device state, like <see cref="SetFirstPersonGeometry"/>: set it around a run of draws and clear it after.
    /// A backend without per-object motion may ignore it.
    /// </para>
    /// </remarks>
    void SetObjectMotion(bool moving, long identity = 0);

    /// <summary>Says that the title draws a sky of its own, which the frame should keep.</summary>
    /// <param name="painted">Whether the sky about to be drawn is the game's own painted one.</param>
    /// <remarks>
    /// <para>
    /// A deferred frame has to decide what to put where nothing was drawn, and a remaster's answer is
    /// its own atmosphere — which is the right answer for a game whose sky is a flat painted gradient
    /// and whose missions are daylight. It is the wrong answer for a game that authors a sky per
    /// mission: an atmosphere knows night only as a sun below the horizon, and I.G.I. 2's night
    /// missions leave the sun up and paint it black, so its night skies came back as daylight blue.
    /// </para>
    /// <para>
    /// Setting this says two things at once: keep what the dome drew, and treat it as a picture rather
    /// than a radiance — divide it by the exposure the resolve will multiply back, the same
    /// compensation <see cref="SetFirstPersonGeometry"/> already earns for the view model. Defaulted to
    /// nothing, so a backend that never exposes anything ignores it and a title that has not asked
    /// keeps the picture it had.
    /// </para>
    /// </remarks>
    void SetPaintedSky(bool painted)
    {
        _ = painted;
    }

    /// <summary>Sets the seed for the following draws' hashed alpha test, zero for the hard cut-out.</summary>
    /// <param name="seed">
    /// Zero draws cut-outs with a hard per-pixel test. A value above zero turns on the hashed test, whose
    /// dithered coverage the temporal resolve averages into a smooth edge; vary it each frame so successive
    /// frames dither differently and the average converges.
    /// </param>
    /// <remarks>Device state, like <see cref="SetLightmapScale"/>: it spans a run of draws.</remarks>
    void SetAlphaHash(float seed);

    /// <summary>Sets how much texture alpha a cut-out draw needs to survive, for the following draws.</summary>
    /// <param name="reference">
    /// The threshold, out of 255. A fragment whose texture alpha is <b>below</b> it is discarded;
    /// <see cref="DefaultAlphaReference"/> is the port's long-standing half and zero turns the test off.
    /// </param>
    /// <remarks>
    /// <para>
    /// A byte rather than a fraction because that is the original's own type:
    /// <c>D3DRS_ALPHAREF</c> takes an integer out of 255, and I.G.I. 2 sets it to exactly two values —
    /// <b>64</b> for cut-outs generally (<c>Direct3DRender_SetBlendStateAlphaTest</c>,
    /// <c>igi2ed 0x56C960</c>) and <b>32</b> for foliage, which lowers it so the thin edges of a leaf
    /// sprite survive (<c>ForestRender_SetStates_Foliage</c>, <c>0x57BDF0</c>). The comparison there is
    /// <c>D3DCMP_GREATEREQUAL</c>, so the test is "alpha at least the reference" and this discards below
    /// it, which is the same rule.
    /// </para>
    /// <para>
    /// It matters more than a threshold usually does. The port had it hard coded at a half, which is
    /// neither of the original's numbers: on I.G.I. 1's foliage, whose texture alpha is strictly nought
    /// or 255, any threshold in between gives the same picture and the constant was harmless. I.G.I. 2's
    /// is not — a leaf sprite's alpha ramps, and testing at a half erodes it to a fraction of its
    /// authored shape.
    /// </para>
    /// <para>
    /// Device state, like <see cref="SetAlphaHash"/>: it spans a run of draws and must be set back
    /// after them. A backend may treat it as part of the pipeline, so callers should change it in runs
    /// rather than per draw.
    /// </para>
    /// </remarks>
    void SetAlphaReference(byte reference);

    /// <summary>Sets how strongly the derived detail maps are applied.</summary>
    /// <param name="strength">
    /// Zero ignores them entirely — which is what every preset below <c>high</c> asks for — one is the
    /// amount they were derived at, and above that exaggerates.
    /// </param>
    void SetMaterialDetail(float strength);

    /// <summary>Sets how an up-facing surface's roughness differs from a wall's.</summary>
    /// <param name="gloss">
    /// A multiplier on an up-facing surface's roughness, up to two: below 1 smooths floors so they reflect,
    /// above 1 roughens them so they shine less than the walls; 1 leaves every surface as its material said.
    /// </param>
    void SetFloorGloss(float gloss);

    /// <summary>Sets how the next meshes drawn blend between their two poses.</summary>
    /// <param name="currentBlend">
    /// How much of the <em>previous-pose</em> vertex stream is mixed into the rasterised position, 0
    /// to 1. Zero draws the current pose exactly — the value every static mesh is implicitly drawn
    /// with, since its previous stream is itself.
    /// </param>
    /// <param name="previousBlend">
    /// The same mix for the position reported as <em>last frame's</em>. The difference between the
    /// two blends is precisely the pose
    /// motion the frame reports: a fixed-tick simulation renders between two known poses, so last
    /// frame's vertex is a different mix of the same two streams, not a third buffer.
    /// </param>
    /// <remarks>
    /// Meaningful only for meshes from <see cref="IMeshDevice.CreateAnimatedMesh"/>; everywhere else both streams
    /// are one buffer and any blend of a value with itself is that value. Cleared by
    /// <see cref="ClearPoseInterpolation"/>.
    /// </remarks>
    void SetPoseInterpolation(float currentBlend, float previousBlend);

    /// <summary>Stops blending poses; subsequent meshes draw their current vertices exactly.</summary>
    void ClearPoseInterpolation();
}
