using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering.Mesh3D;

/// <summary>Everything a forward draw needs to know about the scene it is in.</summary>
/// <param name="Projection">The projection the frame is drawn with.</param>
/// <param name="Lighting">The scene's lighting, whose model decides how the rig is shaded.</param>
/// <param name="Fog">The frame's fog.</param>
/// <param name="FogTarget">Which target the fog colour is taken from.</param>
/// <param name="DetailFade">The terrain's level-of-detail slide: scale, offset, slope and mode.</param>
/// <param name="ModelAmbient">The level's flat model light, for a sub-mesh with no lightmap.</param>
/// <param name="VertexSun">The model-space direction towards the fixed-function sun.</param>
/// <param name="LightmapScale">What a baked lightmap's values are multiplied by.</param>
/// <param name="PoseBlend">How much of the previous pose is in the position drawn.</param>
/// <param name="Exposure">The exposure the resolve will apply, which the lit rig is divided by.</param>
/// <param name="LampLight">What the level's lamps put on the rig, in the same units as the sun.</param>
/// <param name="FirstPerson">Whether the draw is bracketed as first-person geometry.</param>
/// <param name="LinearScene">
/// Whether the draw lands in the scene's linear image rather than the frame: its shader then decodes what it
/// composited and divides it by <see cref="Exposure"/>, which the block carries in place of the lightmap
/// scale.
/// </param>
public readonly record struct ForwardDrawState(
    Matrix4f Projection,
    SceneLighting Lighting,
    SceneFog Fog,
    FogTarget FogTarget,
    DetailFade DetailFade,
    Vector3f ModelAmbient,
    Vector3f VertexSun,
    float LightmapScale,
    float PoseBlend,
    float Exposure,
    Vector3f LampLight,
    bool FirstPerson,
    bool LinearScene = false);

/// <summary>The terrain's level-of-detail slide.</summary>
/// <param name="Scale">How the ramp is scaled.</param>
/// <param name="Offset">Where it starts.</param>
/// <param name="Slope">How steeply it runs.</param>
/// <param name="Mode">Which slide is in force; zero is none.</param>
public readonly record struct DetailFade(float Scale, float Offset, float Slope, float Mode)
{
    /// <summary>Gets no slide at all, which is what everything but the terrain draws with.</summary>
    public static DetailFade None => default;

    /// <summary>Gets whether a slide is in force.</summary>
    public bool Active => Mode > 0f;
}

/// <summary>
/// The 128 bytes a forward mesh draw pushes, filled from the scene rather than from a device.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out because it is the part of the 3D path most easily got wrong and least visible when it is:
/// every float of the block is spoken for, several slots carry different things depending on what the
/// draw is, and a slot written with the wrong one shades plausibly and incorrectly. As a function of a
/// state and a draw it can simply be checked.
/// </para>
/// <para>
/// The layout, slot by slot: the model-view-projection in the first sixteen; the marker and the
/// lightmap scale or inverse exposure; the pose blend; the rig's ambient level or the sun's first
/// component. The last three rows are read three different ways — as the terrain's fade ramp and camera
/// offset, as the model-to-view rotation with the key colour in the w's for the lit rig, or as a single
/// rotation row plus the flat model light for an ordinary world draw — and the frame's fog rides in the
/// final row of the two that do not need it.
/// </para>
/// </remarks>
public static class ForwardPushBlock
{
    /// <summary>How many floats the block holds.</summary>
    public const int Floats = 32;

    /// <summary>Fills the block for one draw.</summary>
    /// <param name="block">Room for <see cref="Floats"/> floats.</param>
    /// <param name="state">The scene the draw is in.</param>
    /// <param name="modelView">The draw's object-to-view transform.</param>
    /// <param name="blend">How the draw is blended.</param>
    /// <param name="hasLightmap">Whether the draw's sub-mesh carries a baked lightmap.</param>
    /// <exception cref="ArgumentException">The block is not <see cref="Floats"/> floats long.</exception>
    public static void Write(
        Span<float> block,
        in ForwardDrawState state,
        in Matrix4f modelView,
        BlendMode blend,
        bool hasLightmap)
    {
        if (block.Length != Floats)
        {
            throw new ArgumentException($"The forward push block is exactly {Floats} floats.", nameof(block));
        }

        block.Clear();

        // The forward pipelines take the combined model-view-projection; their shader applies the
        // clip-space correction.
        Matrix4f mvp = state.Projection * modelView;
        mvp.CopyColumnMajorTo(block[..16]);

        // What this draw is: the world, the rig, or something translucent -- and, for the rig, whether
        // there is a dynamic light to key it with. The studio key is a remaster light and the baked model
        // has none, which is what used to draw the vanilla rig as a black silhouette.
        block[16] = ForwardDrawMarker.For(blend, state.FirstPerson, state.Lighting.Model);

        bool litRig = ForwardDrawMarker.IsStudioRig(block[16]);

        // The lit rig's lighting is designed in display terms (the retail look), so it is divided by the
        // exposure the resolve will apply -- written raw, the eye adaptation of a dim interior multiplied
        // the arms into pure white. A world draw has no exposure to carry and puts the lightmap scale
        // there instead: the same scale the G-buffer path pushes in its material row, which this block
        // has no room for otherwise.
        //
        // Over the scene's linear image a world draw carries the inverse exposure as well, because its colour
        // is decoded there before it lands: the lightmap scale goes with it through the curve's own power,
        // which is what scaling the encoded colour comes to once decoded.
        float inverseExposure = 1f / MathF.Max(state.Exposure, 1e-3f);
        block[17] = litRig
            ? inverseExposure
            : state.LinearScene
                ? MathF.Pow(MathF.Max(state.LightmapScale, 0f), 2.2f) * inverseExposure
                : state.LightmapScale;

        block[18] = state.PoseBlend;

        // The rig's ambient level; for a world draw, the first component of the model-space direction
        // towards the fixed-function sun, which is zero unless the vertex sun was switched on.
        float lampFill = Luminance(state.LampLight);
        block[19] = litRig
            ? state.Lighting.AmbientColour.Length + lampFill
            : state.VertexSun.X;

        if (state.DetailFade.Active && !litRig)
        {
            WriteTerrain(block, state, modelView, blend);
            return;
        }

        WriteRotation(block, state, modelView, blend, litRig, hasLightmap);
    }

    /// <summary>The terrain's rows: the fade ramp, and where the chunk's origin sits from the eye.</summary>
    /// <remarks>
    /// Terrain slides each vertex between levels of detail, and the slide is measured along the world
    /// axes from the eye, so the vertex stage needs the ramp and the offset. A chunk is drawn unrotated,
    /// so the model-view's rotation is the view's own and its transpose undoes it: the offset is that
    /// transpose applied to the model-view's translation. The rows this takes carry the rotation and the
    /// key colour for the lit rig, and a chunk is never the rig — the first row's w, zero for every
    /// other draw, is what says so.
    /// </remarks>
    private static void WriteTerrain(
        Span<float> block,
        in ForwardDrawState state,
        in Matrix4f modelView,
        BlendMode blend)
    {
        block[20] = state.DetailFade.Scale;
        block[21] = state.DetailFade.Offset;
        block[22] = state.DetailFade.Slope;
        block[23] = state.DetailFade.Mode;

        block[24] = (modelView.M11 * modelView.M14) + (modelView.M21 * modelView.M24) + (modelView.M31 * modelView.M34);
        block[25] = (modelView.M12 * modelView.M14) + (modelView.M22 * modelView.M24) + (modelView.M32 * modelView.M34);
        block[26] = (modelView.M13 * modelView.M14) + (modelView.M23 * modelView.M24) + (modelView.M33 * modelView.M34);
        block[27] = 0f;

        WriteFog(block, state, blend);
    }

    /// <summary>The rotation rows, each w carrying a channel of the key colour or something else.</summary>
    /// <remarks>
    /// The key is read by the lit rig alone, so every other draw leaves the first w at zero: that is the
    /// terrain flag the vertex stage reads, and a sun tint there would morph the world. The other two
    /// w's are a world draw's remaining components of the direction towards the fixed-function sun.
    /// </remarks>
    private static void WriteRotation(
        Span<float> block,
        in ForwardDrawState state,
        in Matrix4f modelView,
        BlendMode blend,
        bool litRig,
        bool hasLightmap)
    {
        block[20] = modelView.M11;
        block[21] = modelView.M12;
        block[22] = modelView.M13;
        block[23] = litRig ? state.Lighting.SunColour.X + state.LampLight.X : 0f;

        if (litRig)
        {
            block[24] = modelView.M21;
            block[25] = modelView.M22;
            block[26] = modelView.M23;
            block[27] = state.Lighting.SunColour.Y + state.LampLight.Y;
            block[28] = modelView.M31;
            block[29] = modelView.M32;
            block[30] = modelView.M33;
            block[31] = state.Lighting.SunColour.Z + state.LampLight.Z;
            return;
        }

        // The second normal row carries the level's flat model light instead. A sub-mesh with a lightmap
        // does not take it -- the original draws that one with Direct3D lighting off and the lightmap in
        // the second stage, and this one with lighting on and the ambient as its only term (sub_49F1C0).
        // White is the identity either way, so the shader multiplies unconditionally and nothing
        // branches.
        Vector3f tint = hasLightmap ? Vector3f.One : state.ModelAmbient;
        block[24] = tint.X;
        block[25] = tint.Y;
        block[26] = tint.Z;
        block[27] = 0f;

        // The third row and the key's blue are read by the lit rig and by nothing else -- every other
        // draw returns out of the fragment stage before it touches either -- so this is where the
        // frame's fog rides in. The rig itself is never fogged, which is the original's arrangement too:
        // it is drawn a hand's length from the eye, where the exponential has not begun.
        WriteFog(block, state, blend);
    }

    /// <summary>Writes the frame's fog into the last row of the block.</summary>
    /// <remarks>
    /// The colour follows the blend the way <c>sub_5144E0</c> sets <c>FOGCOLOR</c> per blend mode: an
    /// additive pass fogs to black, so that a surface drawn as several passes is fogged once and not
    /// once per pass. The density is per metre of view depth, and zero means the vertex stage leaves the
    /// draw alone.
    /// </remarks>
    private static void WriteFog(Span<float> block, in ForwardDrawState state, BlendMode blend)
    {
        Vector3f colour = state.Fog.ColourFor(blend, state.FogTarget);

        block[28] = colour.X;
        block[29] = colour.Y;
        block[30] = colour.Z;
        block[31] = state.Fog.DensityPerMetre;
    }

    /// <summary>How bright a light is, by the same weights the rest of the renderer meters with.</summary>
    private static float Luminance(Vector3f colour) =>
        (colour.X * 0.212639f) + (colour.Y * 0.715169f) + (colour.Z * 0.072192f);
}
