using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// The bounding box a batch of rays is broad-phased with, and the terrain octree depth it descends to.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x4CDB20</c>, the multi-ray cast both collision passes are built on. It takes several rays
/// at once, builds <b>one box over every endpoint of all of them</b>, pads it, and uses that single box
/// for the broad phase — so casting three probe rays costs one traversal, not three.
/// </para>
/// </remarks>
public static class RayCastBounds
{
    /// <summary>The horizontal padding added to the batch's box, in engine units.</summary>
    public const double HorizontalPadding = 2.0;

    /// <summary>The vertical padding added to the batch's box, in engine units — just over 4 m.</summary>
    public const double VerticalPadding = 16387.0;

    /// <summary>The deepest the terrain octree is descended for a cast.</summary>
    /// <remarks>
    /// The same cap the terrain renderer's traversal uses (<c>world-loading.md</c> §6.5.5, which emits
    /// while <c>e &lt; 16</c>) — so collision and rendering agree on the octree's finest level, reached
    /// from two independent readings.
    /// </remarks>
    public const int MaximumTerrainDepth = 16;

    /// <summary>The octree's root depth, from which a level is subtracted.</summary>
    public const int RootDepth = 30;

    /// <summary>Builds the padded box that broad-phases a batch of rays.</summary>
    /// <param name="points">Every endpoint of every ray in the batch.</param>
    /// <param name="minimum">Receives the padded lower corner.</param>
    /// <param name="maximum">Receives the padded upper corner.</param>
    /// <exception cref="ArgumentNullException"><paramref name="points"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    /// <remarks>
    /// The vertical padding is far larger than the horizontal — over four metres against two units —
    /// because a cast that is nearly horizontal still has to catch the ground and ceiling around it.
    /// </remarks>
    public static void Build(IReadOnlyList<Vector3d> points, out Vector3d minimum, out Vector3d maximum)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("A cast needs at least one point.", nameof(points));
        }

        Vector3d low = points[0];
        Vector3d high = points[0];
        foreach (Vector3d p in points)
        {
            low = new Vector3d(Math.Min(low.X, p.X), Math.Min(low.Y, p.Y), Math.Min(low.Z, p.Z));
            high = new Vector3d(Math.Max(high.X, p.X), Math.Max(high.Y, p.Y), Math.Max(high.Z, p.Z));
        }

        minimum = new Vector3d(
            low.X - HorizontalPadding, low.Y - HorizontalPadding, low.Z - VerticalPadding);
        maximum = new Vector3d(
            high.X + HorizontalPadding, high.Y + HorizontalPadding, high.Z + VerticalPadding);
    }

    /// <summary>Reports whether a batch of rays is purely vertical.</summary>
    /// <param name="origins">Each ray's start.</param>
    /// <param name="ends">Each ray's end.</param>
    /// <returns><see langword="true"/> when every ray keeps the same X and Y.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The two lists differ in length.</exception>
    /// <remarks>
    /// The original tracks this while building the box and takes a different broad-phase routine when
    /// it holds. It is not a micro-optimisation for its own sake: the ground query always casts
    /// straight down, so the common case gets the cheaper traversal.
    /// </remarks>
    public static bool IsVertical(IReadOnlyList<Vector3d> origins, IReadOnlyList<Vector3d> ends)
    {
        ArgumentNullException.ThrowIfNull(origins);
        ArgumentNullException.ThrowIfNull(ends);
        if (origins.Count != ends.Count)
        {
            throw new ArgumentException(
                $"{origins.Count} origins against {ends.Count} ends.", nameof(ends));
        }

        for (int i = 0; i < origins.Count; i++)
        {
            if (origins[i].X != ends[i].X || origins[i].Y != ends[i].Y)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Computes the terrain octree depth a box is tested at.</summary>
    /// <param name="minX">The box's lower X, offset by the world origin.</param>
    /// <param name="minY">The lower Y.</param>
    /// <param name="minZ">The lower Z.</param>
    /// <param name="maxX">The upper X.</param>
    /// <param name="maxY">The upper Y.</param>
    /// <param name="maxZ">The upper Z.</param>
    /// <returns>The depth, capped at <see cref="MaximumTerrainDepth"/>.</returns>
    /// <remarks>
    /// The same "highest differing bit" trick as <see cref="WorldCell"/>, but expressed as a depth
    /// below the root rather than as a cell size: <c>30 - bsr(differing)</c>. A box that spans the
    /// world gives depth 0; a tiny one is capped at 16 rather than descending forever.
    /// </remarks>
    public static int TerrainDepth(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        uint differing = (uint)(maxX ^ minX) | (uint)(maxY ^ minY) | (uint)(maxZ ^ minZ);

        // bsr of zero is undefined on x86 and the original does not guard it; a box with identical
        // corners can only come from a zero-length cast, and the deepest level is the right answer.
        if (differing == 0)
        {
            return MaximumTerrainDepth;
        }

        int highest = 31 - System.Numerics.BitOperations.LeadingZeroCount(differing);
        int depth = RootDepth - highest;
        return depth > MaximumTerrainDepth ? MaximumTerrainDepth : depth;
    }
}
