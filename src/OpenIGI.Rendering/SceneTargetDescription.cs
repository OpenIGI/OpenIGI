namespace OpenIGI.Rendering;

/// <summary>
/// What the offscreen scene is rendered into.
/// </summary>
/// <remarks>
/// <para>
/// The original renders straight to the back buffer and keeps nothing, which is why it can have no
/// shadows, no ambient occlusion, no reflections and no volumetrics: every one of those needs the
/// depth buffer readable as a texture and the frame available after it has been drawn. This describes
/// the target that makes them possible.
/// </para>
/// <para>
/// The size is the <em>render</em> size, which is not the window size when the render scale is not
/// one. Presentation scales to the window at the end of <see cref="IGraphicsDevice.ResolveScene"/>.
/// </para>
/// </remarks>
/// <param name="Width">The render width in pixels.</param>
/// <param name="Height">The render height in pixels.</param>
/// <param name="SampleCount">
/// How many samples per pixel the geometry is rasterised with; 1 disables multisampling. Resolved
/// down before anything reads the target, because a post-process cannot sample a multisampled depth
/// buffer usefully.
/// </param>
/// <param name="Deferred">
/// Whether surfaces are recorded into a G-buffer and lit afterwards, rather than shaded as they are
/// drawn. Required by everything from stage R3 on, because a shadow, an occlusion term or a
/// reflection needs the surface's normal and roughness to still be there once the frame is complete.
/// </param>
/// <param name="HighDynamicRange">
/// Whether colour is stored with range beyond <c>[0, 1]</c>. When <see langword="false"/> the target
/// is an ordinary 8-bit buffer and the round trip is lossless, which is what the vanilla path uses so
/// that going through the offscreen target changes nothing at all.
/// </param>
public readonly record struct SceneTargetDescription(
    int Width,
    int Height,
    int SampleCount = 1,
    bool Deferred = false,
    bool HighDynamicRange = true)
{
    /// <summary>Gets a value indicating whether the description asks for multisampling.</summary>
    /// <remarks>
    /// <b>Never true while <see cref="Deferred"/> is</b>. Multisampling a G-buffer means multisampling
    /// every one of its targets and resolving each — and resolving a <em>normal</em> by averaging it is
    /// not meaningful, because the average of two unit vectors is not a direction either surface has.
    /// Deferred renderers anti-alias temporally instead, which is what stage R5 brings; until then the
    /// render scale is the dial.
    /// </remarks>
    public bool IsMultisampled => SampleCount > 1 && !Deferred;

    /// <summary>Gets a value indicating whether the description names a usable target size.</summary>
    public bool IsValid => Width > 0 && Height > 0 && SampleCount >= 1;
}
