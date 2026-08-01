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
/// <param name="HighDynamicRange">
/// Whether colour is stored with range beyond <c>[0, 1]</c>. When <see langword="false"/> the target
/// is an ordinary 8-bit buffer and the round trip is lossless, which is what the vanilla path uses so
/// that going through the offscreen target changes nothing at all.
/// </param>
public readonly record struct SceneTargetDescription(
    int Width,
    int Height,
    int SampleCount = 1,
    bool HighDynamicRange = true)
{
    /// <summary>Gets a value indicating whether the description asks for multisampling.</summary>
    public bool IsMultisampled => SampleCount > 1;

    /// <summary>Gets a value indicating whether the description names a usable target size.</summary>
    public bool IsValid => Width > 0 && Height > 0 && SampleCount >= 1;
}
