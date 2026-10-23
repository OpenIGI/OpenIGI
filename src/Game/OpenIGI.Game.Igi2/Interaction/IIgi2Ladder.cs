using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>A ladder, as I.G.I. 2's player finds it, gets on it and climbs it (<see cref="Player.Igi2HumanPlayer"/>).</summary>
/// <remarks>
/// The climb reads its model's magic vertices: the second its top as the view finds it, the third its foot and the fourth its top
/// as the climber stands on it (<see cref="Igi2LadderTask"/>); its steps; and its frame, which the climber faces and rides.
/// </remarks>
public interface IIgi2Ladder : IIgi2Activatable, IAttachParent
{
    /// <summary>Gets its steps, one to every 1,228.8 units of its height.</summary>
    int Steps { get; }

    /// <summary>Gets its frame, row by row: its Y the way it faces.</summary>
    IReadOnlyList<float> Frame { get; }

    /// <summary>A magic vertex of its model, where it stands in the world.</summary>
    /// <param name="index">The vertex.</param>
    /// <returns>The vertex's position.</returns>
    Vector3d MagicVertex(int index);
}
