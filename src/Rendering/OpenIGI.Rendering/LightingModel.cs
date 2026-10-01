namespace OpenIGI.Rendering;

/// <summary>
/// Where a surface's light comes from.
/// </summary>
/// <remarks>
/// <para>
/// The original lights its world entirely with baked lightmaps — one texture per surface, multiplied
/// into the diffuse. That is beautiful, hand-authored and completely static: nothing casts a shadow,
/// nothing responds to a muzzle flash, and the sun cannot move. Replacing it is the largest single
/// change in the remaster track and the one most able to make the game look <em>worse</em>, so it is a
/// setting with a way back rather than a decision taken once.
/// </para>
/// <para>
/// The G-buffer keeps albedo and baked irradiance in <b>separate</b> targets precisely so these three
/// can be chosen at run time. Storing the product, as the original's shader does, would make
/// <see cref="Hybrid"/> and <see cref="Dynamic"/> impossible without re-rendering.
/// </para>
/// </remarks>
public enum LightingModel
{
    /// <summary>
    /// Only the baked lightmaps, exactly as the original composites them.
    /// </summary>
    /// <remarks>
    /// The deferred path reproduces the forward one here: <c>albedo × baked</c> is the same product
    /// the original's fragment shader writes.
    /// </remarks>
    Baked,

    /// <summary>
    /// The bakes as the ambient term, with the sun added on top.
    /// </summary>
    /// <remarks>
    /// The default until the probe volume in stage R7 proves dynamic lighting out. It buys real
    /// shadows and a directional sun without giving up the artists' interiors, at the cost of
    /// double-counting the sun where a bake already included it.
    /// </remarks>
    Hybrid,

    /// <summary>
    /// The bakes are ignored; the diffuse texture is albedo and everything is lit at run time.
    /// </summary>
    /// <remarks>
    /// What makes a moving sun, time of day and light that reacts to the game possible at all. Needs
    /// the ambient occlusion of stage R6 and the probe irradiance of R7 before interiors read
    /// correctly — before those, rooms with no direct sun go flat.
    /// </remarks>
    Dynamic,
}
