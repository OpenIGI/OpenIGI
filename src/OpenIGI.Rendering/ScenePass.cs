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

    /// <summary>
    /// The front layer of the level's glazing: which pane is nearest at each pixel, and which way it
    /// faces, recorded before anything translucent is blended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pane's reflection is computed once per pixel, from one surface — so where two windows overlap,
    /// something has to decide WHICH. Writing each pane's normal into an image from the translucency pass
    /// does not decide it: blending is ordered by the rasteriser, but an image store is not ordered at
    /// all, between triangles or between draws, so whichever fragment landed last won and the answer
    /// changed from frame to frame. That is a reflection that fizzes with per-pixel noise.
    /// </para>
    /// <para>
    /// So the question is asked in a pass with a DEPTH BUFFER OF ITS OWN, where the depth unit decides it
    /// — nearest wins, by construction, with no ordering to get right. The reference does exactly this and
    /// for exactly this reason. It has to be a separate pass rather than depth writes in the translucency
    /// subpass, because the glazing there shares its depth buffer with the tracers, the muzzle flame and
    /// the smoke, and glass that wrote depth would occlude all three.
    /// </para>
    /// </remarks>
    GlassLayer,

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
