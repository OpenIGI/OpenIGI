namespace OpenIGI.Rendering;

/// <summary>
/// How much of the sky each surface can actually see, and what colour the surfaces around it bounce
/// onto it.
/// </summary>
/// <remarks>
/// <para>
/// A flat ambient term says every surface receives the same light from every direction. Nothing in a
/// real room does: the inside of a corner sees a sliver of sky, the underside of a crate sees none,
/// and the difference between them is most of what tells the eye that two surfaces are touching. The
/// original bought that with hand-painted lightmaps, which is why its interiors read so well and why
/// nothing it did not bake — a soldier, a door, the player's own weapon — sits in a room rather than
/// floating in it.
/// </para>
/// <para>
/// Both effects here are screen-space, and that is a real limitation rather than an implementation
/// detail: they can only account for surfaces the camera can see. A wall just outside the frame
/// neither occludes nor bounces, so turning the camera changes the shading slightly. The alternative
/// is the probe volume in the next stage, which knows about geometry off screen and is correspondingly
/// slower to update; the two are complementary and are meant to run together.
/// </para>
/// </remarks>
/// <param name="Quality">
/// How many directions each pixel searches, or 0 for no occlusion at all. The cost is very nearly
/// linear in this, and the noise it leaves is what the temporal accumulation is for — which is why
/// this can be far lower here than it could be in a renderer resolving one frame at a time.
/// </param>
/// <param name="Radius">
/// How far, in metres, a surface is considered able to occlude another. Small values darken only where
/// surfaces touch; large ones shade whole rooms but start to look like a smudge, because a screen-space
/// search cannot tell a distant wall from a near one it happens to line up with.
/// </param>
/// <param name="Intensity">
/// How strongly the result is applied, where 1 is the physical amount. Above 1 is a deliberate
/// exaggeration, and is the usual setting, because the ambient term it multiplies is itself a
/// simplification.
/// </param>
/// <param name="BounceQuality">
/// How many samples are spent gathering colour from neighbouring surfaces, or 0 for none. Reuses the
/// same horizon search as the occlusion, so it is much cheaper than the number suggests.
/// </param>
/// <param name="BounceIntensity">How strongly the gathered colour is added back.</param>
public readonly record struct AmbientOcclusionSettings(
    int Quality = 0,
    float Radius = 1.5f,
    float Intensity = 1f,
    int BounceQuality = 0,
    float BounceIntensity = 1f)
{
    /// <summary>Gets settings with neither occlusion nor bounce.</summary>
    public static AmbientOcclusionSettings Off { get; }

    /// <summary>Gets a value indicating whether the pass would do anything.</summary>
    public bool Enabled => Quality > 0 && Radius > 0f && (Intensity > 0f || WantsBounce);

    /// <summary>Gets a value indicating whether colour is gathered as well as visibility.</summary>
    public bool WantsBounce => BounceQuality > 0 && BounceIntensity > 0f;
}
