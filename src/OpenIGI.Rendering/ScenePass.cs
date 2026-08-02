namespace OpenIGI.Rendering;

/// <summary>
/// Which part of a frame a draw belongs to.
/// </summary>
/// <remarks>
/// The modern path shades in two stages — surfaces are recorded into a G-buffer, then lit in one pass
/// over the screen — and a translucent surface cannot take part in that, because a G-buffer holds one
/// surface per pixel. Naming the pass a draw belongs to is how the backend knows which of its two
/// programs to use, without any renderer in the game having to know the pipeline exists.
/// </remarks>
public enum ScenePass
{
    /// <summary>Not inside a scene capture; draws go straight to the back buffer as the original's do.</summary>
    None,

    /// <summary>
    /// Opaque and alpha-tested surfaces, recorded into the G-buffer to be lit afterwards.
    /// </summary>
    Geometry,

    /// <summary>
    /// Surfaces drawn after lighting: glass, foliage that blends rather than cuts, muzzle flashes,
    /// decals, and the first-person arms and weapon.
    /// </summary>
    /// <remarks>
    /// The view model is here for a reason that is not transparency: it is drawn with its own
    /// narrower projection, so its depth does not belong in the same buffer the fog and the
    /// reflections read.
    /// </remarks>
    Forward,

    /// <summary>Depth-only rendering from a light's point of view.</summary>
    Shadow,

    /// <summary>A model's surface captured into its page of the surface cache atlas.</summary>
    /// <remarks>
    /// <para>
    /// Not part of drawing a frame at all. Each model is rendered a handful of times at load, through
    /// an orthographic camera per card, into a rectangle of an atlas — so that a ray hitting that model
    /// later can read what its surface looks like without the model being on screen, or in the frustum,
    /// or drawn at all.
    /// </para>
    /// <para>
    /// It writes albedo, normal and material, and deliberately <em>not</em> motion: a card is a fixed
    /// view of a static model, so there is no previous frame for it to have moved from.
    /// </para>
    /// </remarks>
    Card,
}
