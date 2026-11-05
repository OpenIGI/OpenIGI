namespace OpenIGI.Core.Numerics;

/// <summary>Fits a lens authored for the original's 4:3 display to a screen of another shape.</summary>
/// <remarks>
/// <para>
/// Both games author every field of view as the tangent of half the HORIZONTAL angle across a 4:3 display. On a
/// wider display the originals keep both tangents and spread the picture across the extra pixels, so everything
/// is drawn wider than it is tall (a square is about a third wider at 16:9). The port used to keep the horizontal
/// angle and square the pixels instead, which crops the top and bottom.
/// </para>
/// <para>
/// Hor+ keeps the authored 4:3 picture whole and adds to it: a wider screen keeps the vertical angle and sees more
/// at the sides, a narrower one keeps the horizontal angle and sees more above and below, and 4:3 is untouched.
/// Every 3D projection -- the world, the first-person rig, and the CPU-side projections the HUD, the binoculars
/// and the computer map make -- goes through here, so they all agree with the picture.
/// </para>
/// </remarks>
public static class WidescreenLens
{
    /// <summary>The display shape the originals' lenses were authored across.</summary>
    public const float AuthoredAspect = 4f / 3f;

    /// <summary>Gets or sets whether lenses are fitted Hor+; off keeps the horizontal angle at every shape.</summary>
    /// <remarks>Mirrors <c>r_widescreen_fov</c>, which the host copies here before each frame's lens is built.</remarks>
    public static bool HorPlus { get; set; } = true;

    /// <summary>The horizontal half-angle tangent a screen of this shape shows for an authored lens.</summary>
    /// <param name="authoredTangent">The lens's horizontal half-angle tangent across 4:3.</param>
    /// <param name="aspectRatio">The screen's width over its height.</param>
    /// <returns>The tangent across the screen's own width.</returns>
    public static float HorizontalTangent(float authoredTangent, float aspectRatio) =>
        HorPlus && aspectRatio > AuthoredAspect ? authoredTangent * (aspectRatio / AuthoredAspect) : authoredTangent;

    /// <summary>The horizontal half-angle tangent a screen of this size shows for an authored lens.</summary>
    /// <param name="authoredTangent">The lens's horizontal half-angle tangent across 4:3.</param>
    /// <param name="width">The screen's width.</param>
    /// <param name="height">The screen's height.</param>
    /// <returns>The tangent across the screen's own width.</returns>
    public static float HorizontalTangent(float authoredTangent, float width, float height) =>
        width > 0f && height > 0f ? HorizontalTangent(authoredTangent, width / height) : authoredTangent;

    /// <summary>The vertical half-angle tangent a screen of this shape shows for an authored lens, with square pixels.</summary>
    /// <param name="authoredTangent">The lens's horizontal half-angle tangent across 4:3.</param>
    /// <param name="aspectRatio">The screen's width over its height.</param>
    /// <returns>Three quarters of the authored tangent on a screen 4:3 or wider; more on a narrower one.</returns>
    public static float VerticalTangent(float authoredTangent, float aspectRatio) =>
        HorizontalTangent(authoredTangent, aspectRatio) / MathF.Max(aspectRatio, 0.0001f);

    /// <summary>The authored lens a projection was built from, which levels of detail are chosen by.</summary>
    /// <param name="projection">A perspective projection.</param>
    /// <returns>Its horizontal half-angle tangent across 4:3.</returns>
    /// <remarks>
    /// Detail is chosen from how big a thing is on the screen, which is the vertical scale and not how much the
    /// screen shows at its sides: reading the widened horizontal tangent would coarsen every model on a wide screen.
    /// </remarks>
    public static float AuthoredTangent(in Matrix4f projection)
    {
        if (projection.M11 == 0f)
        {
            return 1f;
        }

        float tanX = 1f / projection.M11;
        if (!HorPlus || tanX <= 0f || projection.M22 == 0f)
        {
            return tanX;
        }

        return MathF.Min(tanX, MathF.Abs(1f / projection.M22) * AuthoredAspect);
    }
}
