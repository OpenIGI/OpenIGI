namespace OpenIGI.Game.Igi2.World;

/// <summary>One frame's worth of ground for a tile, and what the quadtree did to produce it.</summary>
/// <param name="Mesh">The geometry, in the same shape a full-detail build produces.</param>
/// <param name="SquaresDrawn">How many quadtree leaves were emitted.</param>
/// <param name="SquaresCulled">How many squares the clipping planes rejected, subtrees included.</param>
/// <param name="SquaresVisited">How many squares came off the traversal stack.</param>
/// <param name="CoveredArea">
/// The emitted leaves' areas added up, in samples squared. The traversal only ever divides a square
/// into four equal quadrants, so this plus <paramref name="SupersededArea"/> equalling the root's own
/// area is a proof that the leaves tile it
/// exactly — no gap, no overlap — which is the property a level-of-detail mesh most easily loses.
/// </param>
/// <param name="SupersededArea">
/// The area a finer tile draws instead, which this one left alone. Zero for a level whose tiles do not
/// nest, and for the finest tile of one whose tiles do.
/// </param>
public readonly record struct Igi2TerrainTessellation(
    Igi2TerrainMesh Mesh,
    int SquaresDrawn,
    int SquaresCulled,
    int SquaresVisited,
    long CoveredArea,
    long SupersededArea);
