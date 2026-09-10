using OpenIGI.Core.Numerics;

namespace OpenIGI.Presentation;

/// <summary>
/// The one light a title's fixed-function pipeline shone on an object that had no baked lightmap: a
/// flat ambient plus one directional colour, evaluated per vertex.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. 2 draws an unlightmapped object with <c>D3DRS_LIGHTING = 1</c> and a white material
/// (<c>Direct3DRender_SetGenericStateRigid</c>, <c>igi2ed 0x56CA90</c>), its stage 0 set to
/// <c>MODULATE(TEXTURE, DIFFUSE)</c> (<c>Direct3DRender_SetTextureStateRigid</c>, <c>0x56CE10</c>), so
/// the whole of its lighting is what Direct3D's vertex pipeline puts in <c>DIFFUSE</c>: the global
/// ambient plus each enabled light's diffuse times the cosine of its angle to the vertex normal,
/// saturated to one. The level supplies exactly one such light — a <c>D3DLIGHT_DIRECTIONAL</c> whose
/// diffuse is the <c>Dirlight</c> keyframe's front colour and whose direction is the negated sun
/// vector (<c>Direct3DRender_AddLightsource</c>, <c>0x56C750</c>) — and the ambient is the
/// <c>GlobalLight</c> keyframe's first object category (<c>GlobalLightQTask_DrawCallback</c>,
/// <c>0x6534E0</c>, through <c>Lightsource_SetAmbientlight</c>, <c>0x41C990</c>).
/// </para>
/// <para>
/// So a vertex facing the sun is lit <c>saturate(Ambient + Colour)</c> and one facing away is lit
/// <c>Ambient</c>, and a title that authors night by colouring the sun dark, as the sequel does, gets
/// dark objects out of it. A port that draws those objects at their texture alone draws them in full
/// daylight.
/// </para>
/// </remarks>
/// <param name="Ambient">The flat light every unlightmapped vertex receives, linear.</param>
/// <param name="Colour">The directional light's diffuse colour, linear.</param>
/// <param name="Direction">The unit vector <em>towards</em> the light, in world space.</param>
public readonly record struct FixedFunctionSun(Vector3f Ambient, Vector3f Colour, Vector3f Direction);

/// <summary>How a title composites the objects its world pass draws.</summary>
/// <remarks>
/// <para>
/// <b>A title seam about compositing, kept apart from the one about air.</b>
/// <see cref="SceneAtmosphere"/> says what colour the distance goes; this says how a baked lightmap
/// is applied and what lights an object that has none. They are read off the same level but they
/// reach different places — the atmosphere is baked into every pipeline, this is applied per draw —
/// and a change to one has no business reaching the other.
/// </para>
/// <para>
/// <see cref="Default"/> is the port's long-standing behaviour: a lightmap multiplies at one and an
/// unlightmapped object is drawn unshaded. Project I.G.I. answers nothing at the seam and gets exactly
/// that, which is how its picture is left alone.
/// </para>
/// </remarks>
/// <param name="LightmapGain">
/// What a sampled lightmap is multiplied by before it modulates the texture. One for a plain
/// modulate; two for a title whose lightmap stage is <c>D3DTOP_MODULATE2X</c>.
/// </param>
/// <param name="Sun">
/// The fixed-function light shone on an object with no lightmap, or <see langword="null"/> when the
/// title draws such objects unshaded.
/// </param>
public readonly record struct ObjectShading(float LightmapGain, FixedFunctionSun? Sun)
{
    /// <summary>A plain modulate and no vertex lighting: the port's original compositing.</summary>
    public static ObjectShading Default => new(1f, null);
}
