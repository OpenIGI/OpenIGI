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
/// <param name="Debug">
/// A buffer to show instead of the scene, or <see cref="DebugView.None"/> for the scene itself. It
/// short-circuits everything else here, because the point of it is to see a buffer's contents rather
/// than a graded interpretation of them.
/// </param>
public readonly record struct PostProcessSettings(
    ToneMapOperator ToneMap = ToneMapOperator.Neutral,
    float Exposure = 1.0f,
    float BloomIntensity = 0.0f,
    float BloomThreshold = 1.0f,
    float WhitePoint = 1.6f,
    bool SceneIsDisplayEncoded = true,
    DebugView Debug = DebugView.None)
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
        ToneMap == ToneMapOperator.None && Exposure == 1.0f && !WantsBloom && Debug == DebugView.None;
}
