namespace OpenIGI.Rendering;

/// <summary>
/// Turns the resolved frame into the picture seen through the remastered binocular optics: a glass
/// lens display transform plus the fade that raises it.
/// </summary>
/// <remarks>
/// <para>
/// This is a <em>display transform</em>, not an overlay. The world under it has to stay readable --
/// the whole point of the optics is to study the ground -- so the tint, the highlight lift and the
/// vignette are derived from the resolved picture and the terrain, the buildings and the enemies all
/// survive it.
/// </para>
/// <para>
/// It is separate from <see cref="TacticalDisplaySettings"/> rather than a generalisation of it. That
/// one is the map computer's tube; this is the first-person binocular's glass. The two are never on
/// together -- the computer replaces the first-person view -- so they can coexist as independent
/// uniforms without either changing what the other puts on screen.
/// </para>
/// <para>
/// Everything is continuous because the raise is the point: the glass bends into existence as the
/// device comes up rather than snapping on. A boolean cannot express that.
/// </para>
/// </remarks>
/// <param name="Amount">
/// How much of the glass transform is applied, 0 for the ordinary picture to 1 for full optics. Scales
/// the bezel vignette, the tint and the edge falloff so the lens fades in as the binoculars are raised.
/// </param>
/// <param name="Aberration">
/// Extra chromatic fringing toward the lens edge, on top of the ordinary post-process value. Reserved
/// for a bespoke edge split; 0 leaves the global chromatic-aberration pass to carry the fringing.
/// </param>
/// <param name="Seconds">
/// A continuously rising wall-clock time in seconds, for the animated grain and any breathing. Wall
/// clock rather than a frame counter so the glass does not shimmer faster on a faster machine.
/// </param>
public readonly record struct BinocularDisplaySettings(
    float Amount = 0f,
    float Aberration = 0f,
    float Seconds = 0f)
{
    /// <summary>Gets a value indicating whether any of this would change the picture.</summary>
    public bool IsActive => Amount > 0f;
}
