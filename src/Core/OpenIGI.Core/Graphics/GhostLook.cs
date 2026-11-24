namespace OpenIGI.Core.Graphics;

/// <summary>How a race's ghost is drawn this frame: how long it has been shown, and how hard its projection is glitching.</summary>
/// <param name="Seconds">How long the ghost has been shown, which moves its scan, grain and flicker.</param>
/// <param name="Glitch">How hard the projection is failing this frame, nought for not at all to one at its worst.</param>
/// <param name="GlitchSeed">Which glitch it is: a new seed tears the picture another way.</param>
public readonly record struct GhostLook(float Seconds, float Glitch, float GlitchSeed);
