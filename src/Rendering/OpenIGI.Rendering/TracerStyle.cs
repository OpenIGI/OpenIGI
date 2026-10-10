namespace OpenIGI.Rendering;

/// <summary>How a round's tracer is drawn by <see cref="IGraphicsDevice.DrawTracers"/>.</summary>
public enum TracerStyle
{
    /// <summary>
    /// The original's line: flat, unlit, unfogged, source-alpha blended over the scene, its vertex alpha the
    /// coverage (<c>0x4E0E20</c>, <c>0x50D4C0</c>, <c>0x50DCC0</c>).
    /// </summary>
    Retail,

    /// <summary>The remaster's streak: a hot core in a soft halo of the tracer's colour, added as light.</summary>
    Remastered,
}
