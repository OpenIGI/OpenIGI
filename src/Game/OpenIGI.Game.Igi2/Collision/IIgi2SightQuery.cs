namespace OpenIGI.Game.Igi2.Collision;

/// <summary>What answers a line of sight, <c>World_LineOfSight</c> (<c>igi2 0x551F60</c>).</summary>
public interface IIgi2SightQuery
{
    /// <summary>Runs a line of sight through the objects of some groups and, when asked, the ground.</summary>
    /// <param name="sight">The line, what it may meet, and its running results.</param>
    /// <param name="groups">The query groups; <see cref="Igi2CollisionWorld.TerrainGroup"/> asks for the ground.</param>
    /// <returns><see langword="true"/> when the line crossed no face.</returns>
    bool LineOfSight(Igi2LineOfSight sight, uint groups);
}
