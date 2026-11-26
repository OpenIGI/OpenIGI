using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Multiplayer;

/// <summary>
/// What a level shows of the other players of a session over the picture: each one's name over its head, and each fallen one's
/// mark where it lies with how far its revival has gone. The host's display draws them, whichever title answers.
/// </summary>
/// <remarks>
/// A title whose soldier renderer draws the other players answers there (<see cref="ISoldierBodies.NameTags"/>); a title that
/// runs its own level behind its player answers from its own level.
/// </remarks>
public interface ICoopMarks
{
    /// <summary>Gets the other players' names over their heads.</summary>
    /// <returns>The tags.</returns>
    IReadOnlyList<WorldNameTag> NameTags();

    /// <summary>Gets the fallen teammates' marks.</summary>
    /// <returns>The marks.</returns>
    IReadOnlyList<ReviveMarker> ReviveMarkers();
}
