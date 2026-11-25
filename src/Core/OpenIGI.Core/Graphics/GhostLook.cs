namespace OpenIGI.Core.Graphics;

/// <summary>
/// How a race's ghost is drawn this frame. It is a runner under an optical cloak: how long it has been shown, how hard the cloak
/// is failing, how much of it has materialised, and how far a re-cloak after a failure has run.
/// </summary>
/// <param name="Seconds">How long the ghost has been shown, which moves its sweep and its shimmer.</param>
/// <param name="Glitch">How hard the cloak is failing this frame, nought for not at all to one at its worst.</param>
/// <param name="GlitchSeed">Which failure it is: a new seed breaks the cloak in other places.</param>
/// <param name="Presence">How much of the ghost is there: nought before it materialises or after it dissolves, one when whole.</param>
/// <param name="Recloak">
/// How far the cloak has closed again after a failure, from nought just after it to one once it has. One while no failure has
/// been seen.
/// </param>
public readonly record struct GhostLook(float Seconds, float Glitch, float GlitchSeed, float Presence = 1f, float Recloak = 1f);
