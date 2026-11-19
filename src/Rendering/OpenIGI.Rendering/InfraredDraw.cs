namespace OpenIGI.Rendering;

/// <summary>Which of a body's two infrared draws <see cref="IGraphicsDevice.DrawInfrared"/> makes.</summary>
/// <remarks>
/// I.G.I. 2's <c>Direct3DRender_DrawBoneMesh</c> draws a boned mesh twice while the view is infrared: once where it stands, in
/// the heat ramp's colours alone, and once more drawn nearer the eye and blended, which is what shows it through a wall.
/// </remarks>
public enum InfraredDraw
{
    /// <summary>The body where it stands: the ramp's colour replaces what the frame drew there.</summary>
    Body,

    /// <summary>The body drawn nearer the eye: the ramp's colour blended over the frame by its alpha at half coverage.</summary>
    SeenThrough,
}
