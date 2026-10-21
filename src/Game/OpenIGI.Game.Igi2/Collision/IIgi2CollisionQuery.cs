namespace OpenIGI.Game.Igi2.Collision;

/// <summary>The world-level line queries a body puts to the collision engine.</summary>
public interface IIgi2CollisionQuery
{
    /// <summary>
    /// Vertical lines from each start by its distance, against the objects and, when asked, the ground:
    /// <c>World_GetNextZSpecificMesh</c> (<c>igi2 0x5512C0</c>).
    /// </summary>
    /// <param name="trace">The lines: their starts are read, their ends written, their results cleared and filled.</param>
    /// <param name="distances">How far each line reaches along Z: negative down, positive up.</param>
    /// <param name="groups">The query groups; the top bit asks for the ground.</param>
    void NextZ(Igi2LineTrace trace, ReadOnlySpan<double> distances, uint groups);

    /// <summary>Lines in any direction against the objects of some groups: <c>World_TraceLinesSpecificMesh</c> (<c>igi2 0x5519C0</c>).</summary>
    /// <param name="trace">The lines: their starts and ends are read, their results reset to the ends and filled.</param>
    /// <param name="groups">The query groups.</param>
    void TraceLines(Igi2LineTrace trace, uint groups);
}
