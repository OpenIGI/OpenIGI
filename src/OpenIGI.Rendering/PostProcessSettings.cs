namespace OpenIGI.Rendering;

/// <summary>
/// What happens to the scene between it being drawn and it reaching the screen.
/// </summary>
/// <remarks>
/// <para>
/// Passed to <see cref="IGraphicsDevice.ResolveScene"/> once per frame. Everything here is a property
/// of the frame rather than of any one draw, which is why it travels as a single value rather than as
/// device state that could be left set.
/// </para>
/// <para>
/// <see cref="Identity"/> is the settings under which the offscreen path must produce exactly what the
/// original's direct-to-backbuffer path produces. It is the regression gate for the whole remaster
/// track, so it is named here rather than assembled by a caller who might get one field wrong.
/// </para>
/// </remarks>
/// <param name="ToneMap">How out-of-range light is brought back into what a display can show.</param>
/// <param name="Exposure">
/// A linear multiplier applied before tone mapping. One leaves the scene's own brightness alone.
/// </param>
/// <param name="BloomIntensity">
/// How much of the bloom is mixed back in, or zero to skip the pass. Bloom is what makes a bright fog
/// bank or a muzzle flash read as light rather than as a pale patch.
/// </param>
/// <param name="BloomThreshold">The luminance above which a pixel contributes to the bloom.</param>
/// <param name="WhitePoint">
/// The linear value that maps to display white. This is the amount of headroom the tone map reserves
/// above white, and it has to match what the scene actually produces: reserving four stops for content
/// that never exceeds one spends all of it compressing a range that was not there, which shows up as a
/// washed-out picture rather than as a setting being wrong. One disables the roll-off entirely.
/// </param>
/// <param name="SceneIsDisplayEncoded">
/// Whether the captured scene holds display-encoded colour rather than linear light.
/// </param>
/// <param name="Contrast">
/// How far colour is pushed away from mid-grey, where 1 leaves it alone. Applied in the display space
/// after the tone map, because contrast is a statement about the picture rather than about the light,
/// and doing it in linear light crushes the shadows before the curve has had a chance to shape them.
/// </param>
/// <param name="Saturation">
/// How far colour is pushed away from its own luminance, where 1 leaves it alone. Zero is greyscale.
/// </param>
/// <param name="Vignette">
/// How much the corners are darkened, from 0 to 1. Every lens does this and every photograph has it;
/// its absence is one of the quiet reasons rendered images read as rendered.
/// </param>
/// <param name="Grain">
/// How much film grain is added. Subtle by default and animated per frame, because static grain is
/// dirt on the lens rather than grain — and because a little of it hides the banding that an 8-bit
/// display gives a smooth gradient.
/// </param>
/// <param name="ChromaticAberration">
/// How far the red and blue channels are separated towards the edges of the frame, in pixels at the
/// corner. A real lens cannot focus every wavelength to the same point, and the error grows with
/// distance from the axis — which is why this is zero in the middle however high it is set.
/// </param>
/// <param name="SpatialAa">
/// How strongly edges in the finished frame are smoothed, from 0 to 1, or 0 for none. Spatial rather
/// than temporal: it works from the displayed picture alone, so it cannot ghost — which is why it is
/// here at all, the temporal path being unusable on this game's skinned soldiers.
/// </param>
/// <param name="SpatialAaThreshold">
/// The luminance contrast below which a pixel is left untouched. Most of a frame is below it, which is
/// what makes the pass nearly free and what stops it softening texture detail that is not an edge.
/// </param>
/// <param name="FocalDistance">
/// How far, in metres, the camera is focused. Zero switches depth of field off entirely, which is the
/// right default for a shooter: the player's eye picks what to look at, and a renderer that decides for
/// them fights that. It earns its place on the menu camera and down a scope.
/// </param>
/// <param name="FocalRange">Over how many metres a surface goes from sharp to fully defocused.</param>
/// <param name="FocalBlur">The widest the defocus gets, in pixels.</param>
/// <param name="MotionBlur">
/// How much of a frame's movement is smeared, where 1 is the whole of it. A real shutter is open for
/// part of a frame rather than all of it, so values below 1 are the physical range.
/// </param>
/// <param name="CameraTaps">
/// How many samples the defocus and motion gather takes. The cost of both effects together, and their
/// only quality dial.
/// </param>
/// <param name="Sharpen">
/// How much local contrast is restored after the temporal accumulation and any upsampling have
/// softened the frame. Distinct from the sharpening inside the temporal resolve: that one fights the
/// history's own resampling blur, and this one is a look.
/// </param>

public readonly record struct PostProcessSettings(
    ToneMapOperator ToneMap = ToneMapOperator.Neutral,
    float Exposure = 1.0f,
    float BloomIntensity = 0.0f,
    float BloomThreshold = 1.0f,
    float WhitePoint = 1.6f,
    bool SceneIsDisplayEncoded = true,
    float Contrast = 1f,
    float Saturation = 1f,
    float Vignette = 0f,
    float Grain = 0f,
    float ChromaticAberration = 0f,
    float Sharpen = 0f,
    float FocalDistance = 0f,
    float FocalRange = 12f,
    float FocalBlur = 8f,
    float MotionBlur = 0f,
    int CameraTaps = 6,
    float SpatialAa = 0f,
    float SpatialAaThreshold = 0.05f)
{
    /// <summary>
    /// Gets the settings that pass the scene through untouched.
    /// </summary>
    /// <remarks>
    /// No tone map, unit exposure, no bloom. With an 8-bit target this makes the whole offscreen
    /// detour a copy, which is what lets the vanilla preset stay pixel-identical to the build that
    /// had no offscreen path at all.
    /// </remarks>
    public static PostProcessSettings Identity { get; } = new(ToneMapOperator.None, 1.0f, 0.0f, 1.0f, 1.6f);

    /// <summary>Gets a value indicating whether the bloom pass would do anything.</summary>
    public bool WantsBloom => BloomIntensity > 0.0f;

    /// <summary>Gets a value indicating whether resolving is a straight copy.</summary>
    /// <remarks>
    /// When this holds, a backend may present the scene target directly instead of running any pass
    /// over it — and, more usefully, a test can assert that the vanilla path asked for nothing.
    /// </remarks>
    public bool IsIdentity =>
        ToneMap == ToneMapOperator.None
        && Exposure == 1.0f
        && !WantsBloom
        && !WantsGrading
        && !WantsCameraEffects
        && !WantsSpatialAa;

    /// <summary>Gets a value indicating whether edges are smoothed after the tone map.</summary>
    public bool WantsSpatialAa => SpatialAa > 0f;

    /// <summary>Gets a value indicating whether the camera gather would do anything.</summary>
    public bool WantsCameraEffects =>
        CameraTaps > 0 && ((FocalDistance > 0f && FocalBlur > 0f) || MotionBlur > 0f);

    /// <summary>Gets a value indicating whether anything after the tone map would change the picture.</summary>
    public bool WantsGrading =>
        Contrast != 1f
        || Saturation != 1f
        || Vignette > 0f
        || Grain > 0f
        || ChromaticAberration > 0f
        || Sharpen > 0f;
}
