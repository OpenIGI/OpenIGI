using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>
/// A brain that keeps its human on its graph: a living human with one takes its footing from the graph's nodes instead of
/// tracing the world (<c>human.c Human_GroundCollision</c>, its HumanAI half).
/// </summary>
public interface IIgi2GraphFooting
{
    /// <summary>
    /// Finds the footing on the graph under a move: the height along the stretch between the node the human stands on and the
    /// node beside it, that node's ground material, and the light between the two nodes.
    /// </summary>
    /// <param name="from">Where the move started; the footing lies under it, not under where the move ends.</param>
    /// <param name="ground">The footing, cleared by the caller and filled when the brain has a graph.</param>
    /// <param name="light">How lit the footing is, the two nodes' light by how far along the stretch it lies.</param>
    /// <returns><see langword="false"/> when the brain has no graph (or no node to stand on), and the footing is left empty.</returns>
    bool TryGetFooting(Vector3d from, WorldIntersection ground, out float light);
}
