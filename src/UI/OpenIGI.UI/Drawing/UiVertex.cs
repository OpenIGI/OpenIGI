using OpenIGI.Core.Graphics;

namespace OpenIGI.UI.Drawing;

/// <summary>One corner of a drawn quad: a device-space position, a texture coordinate and a colour.</summary>
/// <param name="X">The horizontal position in device pixels.</param>
/// <param name="Y">The vertical position in device pixels.</param>
/// <param name="U">The horizontal texture coordinate, 0 to 1 across the image.</param>
/// <param name="V">The vertical texture coordinate, 0 to 1 down the image.</param>
/// <param name="Color">The colour modulated with the sampled texel, straight alpha.</param>
public readonly record struct UiVertex(float X, float Y, float U, float V, Bgra32 Color);
