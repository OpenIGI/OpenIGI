using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// The light I.G.I. 2 shines on an object that has no baked lightmap: the level's <c>Dirlight</c> as a
/// Direct3D directional light, over the <c>GlobalLight</c>'s object ambient.
/// </summary>
/// <remarks>
/// <para>
/// <b>An unlightmapped object is lit by the fixed-function pipeline, and by nothing else.</b>
/// <c>Direct3DRender_SetGenericStateRigid</c> (<c>igi2ed 0x56CA90</c>) sets a material of pure white
/// diffuse and ambient, <c>D3DRS_LIGHTING = 1</c> and <c>D3DRS_COLORVERTEX = 0</c>;
/// <c>Direct3DRender_SetTextureStateRigid</c> (<c>0x56CE10</c>) makes stage 0
/// <c>MODULATE(TEXTURE, DIFFUSE)</c>. The model's vertex format carries no colour
/// (<c>XYZ|NORMAL|TEX</c>), so <c>DIFFUSE</c> there is what Direct3D computes per vertex:
/// <c>saturate(D3DRS_AMBIENT + Σ light.Diffuse · max(N · L, 0))</c>. The lightmapped state block
/// (<c>0x56CFD0</c>) turns lighting off; a bake replaces all of this.
/// </para>
/// <para>
/// <b>The one light.</b> <c>DirlightQTask_DrawCallback</c> (<c>0x657680</c>) hands each <c>Dirlight</c>
/// to <c>Lightsource_NewDirlight</c> (<c>0x41C860</c>), whose renderer half
/// <c>Direct3DRender_AddLightsource</c> (<c>0x56C750</c>) builds a <c>D3DLIGHT_DIRECTIONAL</c> with
/// <c>Diffuse</c> = the keyframe's front colour, no specular, no ambient, and
/// <c>Direction</c> = the negated sun vector — so the vector the keyframe stores points <em>towards</em>
/// the sun, and a face whose normal agrees with it is the lit one. The back colour is stored on the
/// light and never reaches Direct3D. The <c>Affects objects</c> flag is stored beside it and, at run
/// time, not consulted either: the light is added unconditionally.
/// </para>
/// <para>
/// <b>The ambient.</b> <c>GlobalLightQTask_DrawCallback</c> (<c>0x6534E0</c>) passes the object
/// category's ambient to <c>Lightsource_SetAmbientlight</c> (<c>0x41C990</c>), which the same renderer
/// half writes as <c>D3DRS_AMBIENT</c>. See <see cref="Igi2SceneAtmosphere.ObjectAmbient"/> for which
/// category.
/// </para>
/// <para>
/// <b>The direction, from the binary rather than from plausibility.</b>
/// <c>DirlightKeyframeQTask_UpdateInternalDataHandler</c> (<c>0x6573C0</c>) builds
/// <c>Real32x9_Create(0, 2π − Beta, 2π − Gamma)</c> and rotates <c>(1, 0, 0)</c> by it.
/// <c>Real32x9_Create</c> (<c>0x605F40</c>) composes <c>Gamma · Beta · Alpha</c> from
/// <c>Real32x9_CreateGamma</c> (<c>0x6061F0</c>, a rotation about Z) and <c>Real32x9_CreateBeta</c>
/// (<c>0x6061B0</c>, about Y), applied to a column vector, which works out to
/// <see cref="DirectionFrom"/>. The elevation is <c>Beta</c> itself and <c>Gamma</c> is the azimuth.
/// This differs from the shared <see cref="DirectionalLightSettings.DirectionFrom"/>, which was fitted
/// to Project I.G.I.'s data and yields <c>(−sin β, 0, cos β)</c>; that reading is left alone here
/// because it is Project I.G.I.'s, and the sequel computes its own.
/// </para>
/// </remarks>
/// <param name="Ambient">The flat light every unlightmapped vertex receives, linear.</param>
/// <param name="Colour">The sun's diffuse colour, from the keyframe's <c>Front Color</c>, linear.</param>
/// <param name="Direction">The unit vector towards the sun, in the world's Z-up frame.</param>
public readonly record struct Igi2FixedFunctionSun(Vector3f Ambient, Vector3f Colour, Vector3f Direction)
{
    /// <summary>Reads the level's fixed-function sun, if it declares one.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="sun">The sun, when the level declares a <c>Dirlight</c>.</param>
    /// <returns><see langword="true"/> when one was read.</returns>
    /// <remarks>
    /// A level with a <c>Dirlight</c> and no <c>GlobalLight</c> is lit by the sun over a black ambient,
    /// which is what Direct3D's default <c>D3DRS_AMBIENT</c> of zero gives the original.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="levelRoot"/> is <see langword="null"/>.</exception>
    public static bool TryRead(GameTask levelRoot, out Igi2FixedFunctionSun sun)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        sun = default;
        if (!DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings light))
        {
            return false;
        }

        Vector3f ambient = Igi2SceneAtmosphere.TryRead(levelRoot, out Igi2SceneAtmosphere air)
            ? air.ObjectAmbient
            : Vector3f.Zero;

        sun = new Igi2FixedFunctionSun(ambient, light.Colour, DirectionFrom(light.Beta, light.Gamma));
        return true;
    }

    /// <summary>Turns a <c>DirlightKeyframe</c>'s two angles into the vector towards the sun.</summary>
    /// <param name="beta">The keyframe's <c>Beta</c> in radians: the elevation above the horizon.</param>
    /// <param name="gamma">The keyframe's <c>Gamma</c> in radians: the azimuth, clockwise from +X.</param>
    /// <returns>The unit vector towards the sun, Z up.</returns>
    /// <remarks>
    /// <c>RotZ(−γ) · RotY(−β) · (1, 0, 0)</c>, as <c>0x6573C0</c> computes it. <c>RotY(−β)</c> takes the
    /// X axis to <c>(cos β, 0, sin β)</c>; <c>RotZ(−γ)</c> then swings it to
    /// <c>(cos β cos γ, −cos β sin γ, sin β)</c>. location1/level1 authors <c>Beta = 1 − 2π</c>, which
    /// is a sun 57° up, and <c>Gamma = 0.409</c>.
    /// </remarks>
    public static Vector3f DirectionFrom(float beta, float gamma)
    {
        float cosBeta = MathF.Cos(beta);
        return new Vector3f(cosBeta * MathF.Cos(gamma), -cosBeta * MathF.Sin(gamma), MathF.Sin(beta));
    }
}
