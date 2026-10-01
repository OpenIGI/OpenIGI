using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One solid world triangle, in world units.</summary>
/// <param name="A">The first corner.</param>
/// <param name="B">The second corner.</param>
/// <param name="C">The third corner.</param>
/// <remarks>
/// The winding carries no meaning: level collision meshes wind into the solid and terrain winds out
/// of it, so a consumer decides which side it is on from where it stands, not from the order.
/// </remarks>
public readonly record struct CollisionTriangle(Vector3d A, Vector3d B, Vector3d C)
{
    /// <summary>Tests the triangle's bounds against a box.</summary>
    /// <param name="min">The box's low corner.</param>
    /// <param name="max">The box's high corner.</param>
    /// <returns><see langword="true"/> when the two overlap.</returns>
    public bool Overlaps(Vector3d min, Vector3d max) =>
        Math.Max(A.X, Math.Max(B.X, C.X)) >= min.X && Math.Min(A.X, Math.Min(B.X, C.X)) <= max.X
        && Math.Max(A.Y, Math.Max(B.Y, C.Y)) >= min.Y && Math.Min(A.Y, Math.Min(B.Y, C.Y)) <= max.Y
        && Math.Max(A.Z, Math.Max(B.Z, C.Z)) >= min.Z && Math.Min(A.Z, Math.Min(B.Z, C.Z)) <= max.Z;
}

/// <summary>Collects every solid world triangle that touches a box.</summary>
/// <param name="min">The box's low corner, in world units.</param>
/// <param name="max">The box's high corner.</param>
/// <param name="into">Receives the triangles; not cleared first.</param>
/// <remarks>
/// The query a rigid body's narrow phase runs once per tick over the space it can reach, so its
/// contacts are measured against the real surfaces -- slopes, ledges and corners -- rather than
/// sampled through rays.
/// </remarks>
public delegate void CollisionTriangleGather(Vector3d min, Vector3d max, List<CollisionTriangle> into);
