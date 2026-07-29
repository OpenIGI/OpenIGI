namespace OpenIGI.Game.World;

/// <summary>Three integers in world units — a point or an offset the octree descent works in.</summary>
/// <remarks>
/// World coordinates are integral: the octree indexes them bitwise, so a floating-point position is
/// floored before it enters a query. Kept separate from the rendering vectors, which are real-valued.
/// </remarks>
/// <param name="X">The X coordinate.</param>
/// <param name="Y">The Y coordinate.</param>
/// <param name="Z">The Z coordinate, which is up.</param>
public readonly record struct Vector3i(int X, int Y, int Z);
