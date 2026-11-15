namespace OpenIGI.Presentation;

/// <summary>Receives the ground as it is drawn, for a picture of the level seen from straight above.</summary>
/// <remarks>
/// A tool's way to draw the level outside the game -- the editor's map and the multiplayer layout page draw it this way -- with
/// the ground's own textures and the passes that blend them, rather than a picture taken through the renderer. The textures
/// are the tiles of the ground's atlas, which the tool reads for itself.
/// </remarks>
public interface IOverheadSink
{
    /// <summary>Takes one pass of the ground over one patch: triangles, three corners each, in one texture.</summary>
    /// <param name="tile">The tile of the ground's atlas the pass samples.</param>
    /// <param name="first">Whether the pass is the patch's first, drawn opaque; every later one is added over it.</param>
    /// <param name="corners">The triangles' corners, in world units.</param>
    void GroundPass(int tile, bool first, ReadOnlySpan<OverheadVertex> corners);
}

/// <summary>A corner of a ground triangle.</summary>
/// <param name="X">East, in world units.</param>
/// <param name="Y">North, in world units.</param>
/// <param name="Z">Up, in world units.</param>
/// <param name="U">The texture's across coordinate.</param>
/// <param name="V">The texture's down coordinate.</param>
/// <param name="Weight">How much of the texture this pass adds here, 0 to 1.</param>
public readonly record struct OverheadVertex(double X, double Y, double Z, float U, float V, float Weight);
