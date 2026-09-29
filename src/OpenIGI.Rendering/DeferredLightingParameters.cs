using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>How a frame's volumetric fog divides the distance in front of the eye into slices.</summary>
/// <param name="B">The distribution's base.</param>
/// <param name="O">Its offset.</param>
/// <param name="Scale">Its scale; nought says there is no volume this frame.</param>
/// <param name="InverseSlices">One over the slice count.</param>
public readonly record struct FogSliceDistribution(float B, float O, float Scale, float InverseSlices)
{
    /// <summary>Gets the distribution that says there is no fog.</summary>
    public static FogSliceDistribution None => default;
}

/// <summary>
/// Everything the deferred shading's parameter block holds, gathered where a frame knows it.
/// </summary>
/// <param name="Unprojection">How a depth becomes a view position.</param>
/// <param name="ProbeOrigin">Where the probe field's grid starts.</param>
/// <param name="ProbeEnabled">Whether there is a field to read at all.</param>
/// <param name="ProbeInverseCount">One over the grid's count on each axis.</param>
/// <param name="ProbeSpacing">How far apart its probes stand, in metres.</param>
/// <param name="ProbeBlend">How much of the field is blended in.</param>
/// <param name="LightScale">What the frame's lighting is multiplied by.</param>
/// <param name="SunShadowTraced">Whether the sun's shadow comes from a ray rather than the cascades.</param>
/// <param name="SkyColour">The sky colour the field's grey visibility is tinted by.</param>
/// <param name="SkyIntensity">How brightly that tint is applied.</param>
/// <param name="CameraPosition">The camera in world space, added to the rotated view position.</param>
/// <param name="ScreenGiStrength">How strongly the near-field screen atlas contributes; nought is off.</param>
/// <param name="ScreenGiFullResolution">Whether that atlas is at the frame's own resolution.</param>
/// <param name="OcclusionEnabled">Whether the ambient occlusion image is worth reading.</param>
/// <param name="ViewHeightKm">The camera's height from the planet's centre, as the sky tables were built for.</param>
/// <param name="ViewToWorld">The view-to-world rotation, which every world-space lookup goes through.</param>
/// <param name="CascadeCount">How many shadow cascades the frame divided the view into.</param>
/// <param name="ShadowsReady">
/// Whether the cascades have anything in them yet. Kept apart from the count because the splits and the
/// matrices describe how the frame was divided either way, and only the count the shading reads is
/// stood down — so a level's first frames light unshadowed rather than shadowed by an empty map.
/// </param>
/// <param name="DepthBias">The cascades' depth bias.</param>
/// <param name="NormalBias">Their normal bias.</param>
/// <param name="ShadowTexelSize">One over the shadow map's resolution.</param>
/// <param name="Wetness">How wet the world is and how its water moves.</param>
/// <param name="HasShelterMap">Whether the level has a top-down roof raster.</param>
/// <param name="Shelter">Where that raster sits in the world and how a texel decodes.</param>
/// <param name="Fog">How the fog volume's slices are distributed.</param>
/// <param name="Flashlight">The player's flashlight, or <see cref="FlashlightLight.Off"/>.</param>
/// <param name="FlashlightShadowed">Whether this frame made the flashlight a shadow mask to be read through.</param>
/// <param name="FrameIndex">Which frame this is, for anything that jitters.</param>
/// <remarks>
/// One record rather than a method of twenty arguments, and in this project rather than in a backend,
/// because both paths fill the same block for the same shader: a second copy of this layout is a second
/// thing to keep in step, and the way it fails is a frame that shades plausibly from the wrong slot.
/// </remarks>
public readonly record struct DeferredLightingState(
    DepthUnprojection Unprojection,
    Vector3f ProbeOrigin = default,
    bool ProbeEnabled = false,
    Vector3f ProbeInverseCount = default,
    float ProbeSpacing = 0f,
    float ProbeBlend = 0f,
    float LightScale = 1f,
    bool SunShadowTraced = false,
    Vector3f SkyColour = default,
    float SkyIntensity = 0f,
    Vector3f CameraPosition = default,
    float ScreenGiStrength = 0f,
    bool ScreenGiFullResolution = false,
    bool OcclusionEnabled = false,
    float ViewHeightKm = 0f,
    Matrix3f ViewToWorld = default,
    int CascadeCount = 0,
    bool ShadowsReady = false,
    float DepthBias = 0f,
    float NormalBias = 0f,
    float ShadowTexelSize = 0f,
    WetnessSettings Wetness = default,
    bool HasShelterMap = false,
    ShelterMapTransform Shelter = default,
    FogSliceDistribution Fog = default,
    FlashlightLight Flashlight = default,
    bool FlashlightShadowed = false,
    long FrameIndex = 0);

/// <summary>The blocks the deferred shading reads, written from what a frame holds.</summary>
/// <remarks>
/// Backend-neutral for the reason every other lifted block here is: the layout belongs to the shader,
/// not to a device, and the two paths that fill it have to fill it identically or one of them shades
/// from a slot the other wrote something else into.
/// </remarks>
public static class DeferredLightingParameters
{
    /// <summary>How many floats the GI block holds.</summary>
    public const int GiFloats = DeferredLightingBlocks.GiBytes / sizeof(float);

    /// <summary>How many floats the per-draw light block holds.</summary>
    public const int LightFloats = 24;

    /// <summary>How many cascades the block has room for.</summary>
    public const int Cascades = 4;

    /// <summary>Writes the GI parameter block.</summary>
    /// <param name="block">Room for <see cref="GiFloats"/> floats.</param>
    /// <param name="state">What the frame holds.</param>
    /// <param name="cascadeSplits">The view depth each cascade stops at.</param>
    /// <param name="cascadeMatrices">Each cascade's camera-relative-world to light-clip transform.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void WriteGi(
        Span<float> block,
        in DeferredLightingState state,
        ReadOnlySpan<float> cascadeSplits,
        ReadOnlySpan<Matrix4f> cascadeMatrices)
    {
        if (block.Length != GiFloats)
        {
            throw new ArgumentException($"The GI block is {GiFloats} floats, not {block.Length}.", nameof(block));
        }

        block.Clear();

        // unproject: how a depth becomes a view position.
        block[0] = state.Unprojection.XScale;
        block[1] = state.Unprojection.YScale;
        block[2] = state.Unprojection.Near;
        block[3] = state.Unprojection.Far;

        // probeOrigin: xyz origin, w whether the field is on.
        block[4] = state.ProbeOrigin.X;
        block[5] = state.ProbeOrigin.Y;
        block[6] = state.ProbeOrigin.Z;
        block[7] = state.ProbeEnabled ? 1f : 0f;

        // probeInvCount: xyz one-over-count, w spacing.
        block[8] = state.ProbeInverseCount.X;
        block[9] = state.ProbeInverseCount.Y;
        block[10] = state.ProbeInverseCount.Z;
        block[11] = state.ProbeSpacing;

        // probeMisc: the half-spacing normal offset, the blend, the light scale, and whether the sun's
        // shadow comes from a ray rather than from the cascades.
        block[12] = state.ProbeSpacing * 0.5f;
        block[13] = Math.Clamp(state.ProbeBlend, 0f, 1f);
        block[14] = MathF.Max(state.LightScale, 0f);
        block[15] = state.SunShadowTraced ? 1f : 0f;

        // probeSky: the sky colour the grey visibility is tinted by, times its intensity.
        float intensity = MathF.Max(state.SkyIntensity, 0f);
        block[16] = state.SkyColour.X * intensity;
        block[17] = state.SkyColour.Y * intensity;
        block[18] = state.SkyColour.Z * intensity;

        // camera: the camera's world position, added to the rotated view position.
        block[20] = state.CameraPosition.X;
        block[21] = state.CameraPosition.Y;
        block[22] = state.CameraPosition.Z;

        // giParams: the screen-GI strength (nought disables it), whether its atlas is full resolution,
        // whether ambient occlusion is on, and the height the sky tables were built for.
        block[24] = MathF.Max(state.ScreenGiStrength, 0f);
        block[25] = state.ScreenGiStrength > 0f && state.ScreenGiFullResolution ? 1f : 0f;
        block[26] = state.OcclusionEnabled ? 1f : 0f;
        block[27] = state.ViewHeightKm;

        // viewToWorld: the row-major rotation, written column-major as a mat4.
        Matrix3f m = state.ViewToWorld;
        block[28] = m.M11; block[29] = m.M21; block[30] = m.M31;
        block[32] = m.M12; block[33] = m.M22; block[34] = m.M32;
        block[36] = m.M13; block[37] = m.M23; block[38] = m.M33;
        block[43] = 1f;

        // cascadeSplits: the view depth each cascade stops at.
        for (int i = 0; i < Cascades; i++)
        {
            block[44 + i] = i < state.CascadeCount && i < cascadeSplits.Length ? cascadeSplits[i] : 0f;
        }

        // shadowConfig: the cascade count (nought disables), the two biases, and the texel size.
        block[48] = state.ShadowsReady ? state.CascadeCount : 0f;
        block[49] = state.DepthBias;
        block[50] = state.NormalBias;
        block[51] = state.ShadowTexelSize;

        // cascadeMatrix[4]: each cascade's camera-relative-world to light-clip, column-major.
        for (int i = 0; i < Cascades; i++)
        {
            Matrix4f cascade = i < state.CascadeCount && i < cascadeMatrices.Length
                ? cascadeMatrices[i]
                : Matrix4f.Identity;

            cascade.CopyColumnMajorTo(block.Slice(52 + (i * 16), 16));
        }

        // wetness0: how wet, how much of the ground stands under water, how hard the rain strikes it,
        // and the clock the ripples run on.
        block[116] = MathF.Max(state.Wetness.Amount, 0f);
        block[117] = Math.Clamp(state.Wetness.PuddleCoverage, 0f, 1f);
        block[118] = Math.Clamp(state.Wetness.RippleStrength, 0f, 1f);
        block[119] = state.Wetness.Seconds;

        // wetness1: the two lattice spacings in metres, how much the wind textures the water, and
        // whether there is a roof map to read.
        block[120] = MathF.Max(state.Wetness.PuddleSizeMetres, 0.05f);
        block[121] = MathF.Max(state.Wetness.RippleSizeMetres, 0.02f);
        block[122] = Math.Clamp(state.Wetness.WindRippleStrength, 0f, 1f);
        block[123] = state.HasShelterMap ? 1f : 0f;

        // shelterMap*: where the level's top-down roof raster sits in the world and how a texel of it
        // decodes. In world metres and not camera-relative, unlike everything around it: the map does
        // not move with the eye.
        ShelterMapTransform shelter = state.HasShelterMap ? state.Shelter : ShelterMapTransform.None;

        block[124] = shelter.OriginX;
        block[125] = shelter.OriginY;
        block[126] = shelter.CellMetres > 0f ? 1f / shelter.CellMetres : 0f;
        block[127] = shelter.LowestZ;

        block[128] = shelter.HeightScale;
        block[129] = shelter.HeadroomMetres;
        block[130] = shelter.ReachMetres;

        block[132] = shelter.Width;
        block[133] = shelter.Height;

        // fog: the slice distribution the shading looks a pixel up with. A scale of nought says there is
        // no volume this frame, which is how the shader knows to leave the colour alone rather than
        // sample whatever was bound in the volume's place.
        block[136] = state.Fog.B;
        block[137] = state.Fog.O;
        block[138] = state.Fog.Scale;
        block[139] = state.Fog.InverseSlices;

        // flashlight: the player's beam, laid out as the fog's block lays it out.
        FlashlightBlock.Write(
            block.Slice(FlashlightOffset, FlashlightBlock.Floats),
            state.Flashlight,
            state.FlashlightShadowed,
            state.FrameIndex);
    }

    /// <summary>Where in the GI block the flashlight starts, in floats.</summary>
    public const int FlashlightOffset = 140;

    /// <summary>Writes the block the shading is handed per draw.</summary>
    /// <param name="block">Room for <see cref="LightFloats"/> floats.</param>
    /// <param name="sunDirection">The view-space direction towards the sun.</param>
    /// <param name="model">The lighting model: nought baked, one hybrid, two dynamic.</param>
    /// <param name="sunColour">The sun's colour.</param>
    /// <param name="lightScale">What the frame's lighting is multiplied by.</param>
    /// <param name="ambient">The flat ambient a surface falls back to.</param>
    /// <param name="debugView">Which debug view is in force, or nought for the picture.</param>
    /// <param name="paintedSky">Whether the title draws its own sky rather than the marched one.</param>
    /// <param name="bakesWhole">Whether the level's baked light is complete.</param>
    /// <param name="night">The stars, the moon's disc and the cloud cover the sky is drawn with.</param>
    /// <exception cref="ArgumentException">The block is the wrong length.</exception>
    public static void WriteLight(
        Span<float> block,
        in Vector3f sunDirection,
        float model,
        in Vector3f sunColour,
        float lightScale,
        in Vector3f ambient,
        float debugView = 0f,
        bool paintedSky = false,
        bool bakesWhole = true,
        in NightSky night = default)
    {
        if (block.Length != LightFloats)
        {
            throw new ArgumentException($"The light block is {LightFloats} floats, not {block.Length}.", nameof(block));
        }

        block.Clear();

        block[0] = sunDirection.X;
        block[1] = sunDirection.Y;
        block[2] = sunDirection.Z;
        block[3] = model;

        block[4] = sunColour.X;
        block[5] = sunColour.Y;
        block[6] = sunColour.Z;
        block[7] = lightScale;

        block[8] = ambient.X;
        block[9] = ambient.Y;
        block[10] = ambient.Z;
        block[11] = debugView;

        block[12] = paintedSky ? 1f : 0f;
        block[14] = bakesWhole ? 1f : 0f;

        block[16] = night.MoonDirection.X;
        block[17] = night.MoonDirection.Y;
        block[18] = night.MoonDirection.Z;
        block[19] = MathF.Max(night.MoonRadiance, 0f);

        block[20] = MathF.Max(night.StarRadiance, 0f);
        block[21] = Math.Clamp(night.CloudCover, 0f, 1f);

        // Wrapped at an hour: the twinkle only wants a phase, and a float counting up a whole session
        // loses the fraction a slow sine needs.
        block[22] = night.Seconds % 3600f;
    }
}
