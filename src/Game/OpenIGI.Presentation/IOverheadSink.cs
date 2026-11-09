using OpenIGI.Core.Graphics;

namespace OpenIGI.Presentation;

/// <summary>Receives the ground as it is drawn, for a picture of the level seen from straight above.</summary>
/// <remarks>
/// A tool's way to draw the level outside the game -- the multiplayer layout page draws it this way -- with the ground's own
/// textures and the passes that blend them, rather than a picture taken through the renderer.
/// </remarks>
public interface IOverheadSink
{
    /// <summary>Takes one of the ground's textures, once, before any triangles that use it.</summary>
    /// <param name="texture">The number the ground's triangles name it by.</param>
    /// <param name="width">Its width in texels.</param>
    /// <param name="height">Its height in texels.</param>
    /// <param name="pixels">Its texels, row by row.</param>
    void GroundTexture(int texture, int width, int height, ReadOnlySpan<Bgra32> pixels);

    /// <summary>Takes one pass of the ground over one patch: triangles, three corners each, in one texture.</summary>
    /// <param name="texture">The texture the pass samples, as <see cref="GroundTexture"/> named it.</param>
    /// <param name="first">Whether the pass is the patch's first, drawn opaque; every later one is added over it.</param>
    /// <param name="corners">The triangles' corners, in world units.</param>
    void GroundPass(int texture, bool first, ReadOnlySpan<OverheadVertex> corners);
}

/// <summary>A corner of a ground triangle.</summary>
/// <param name="X">East, in world units.</param>
/// <param name="Y">North, in world units.</param>
/// <param name="Z">Up, in world units.</param>
/// <param name="U">The texture's across coordinate.</param>
/// <param name="V">The texture's down coordinate.</param>
/// <param name="Weight">How much of the texture this pass adds here, 0 to 1.</param>
public readonly record struct OverheadVertex(double X, double Y, double Z, float U, float V, float Weight);
