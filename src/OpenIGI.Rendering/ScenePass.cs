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
}
