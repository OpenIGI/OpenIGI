using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The broad-phase cell an object occupies — the loose-octree level its bounding box fits into.
/// </summary>
/// <remarks>
/// <para>
/// From <c>0x4C67E0</c>, which the position commit (<c>0x4C6940</c>) calls with an object's world AABB.
/// A box fits in a cell of size <c>2^k</c> exactly when its two corners agree in every bit above
/// <c>k</c>, so the cell size is <b>the highest bit at which the corners differ</b> — one
/// <c>bsr</c> over the OR of the three per-axis XORs, and no comparisons at all.
/// </para>
/// <para>
/// <see cref="MaximumSize"/> is <c>0x40000000</c>, which is the same ±2^30 world cube the terrain
/// octree spans — the collision index and the terrain share their
/// bounds. <see cref="MinimumSize"/> is 8192 units, i.e. <b>two metres</b>, the finest the broad phase
/// ever goes.
/// </para>
/// </remarks>
public static class WorldCell
{
    /// <summary>The smallest cell — 8192 units, two metres.</summary>
    public const int MinimumSize = 0x2000;

    /// <summary>The largest cell — the half-extent of the world cube the terrain octree also uses.</summary>
    public const int MaximumSize = 0x40000000;

    /// <summary>Computes the cell size an axis-aligned box belongs in.</summary>
    /// <param name="minX">The box's lower X, in engine units, already offset by the world origin.</param>
    /// <param name="minY">The lower Y.</param>
    /// <param name="minZ">The lower Z.</param>
    /// <param name="maxX">The upper X.</param>
    /// <param name="maxY">The upper Y.</param>
    /// <param name="maxZ">The upper Z.</param>
    /// <returns>A power of two clamped to <see cref="MinimumSize"/>..<see cref="MaximumSize"/>.</returns>
    /// <remarks>
    /// The XORs are taken on the raw integers, so this works on the two's-complement bit patterns
    /// rather than on magnitudes — a box straddling the origin differs in the sign bit and lands in the
    /// largest cell, which is the behaviour the original relies on.
    /// </remarks>
    public static int SizeOf(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        uint differing = (uint)(maxX ^ minX) | (uint)(maxY ^ minY) | (uint)(maxZ ^ minZ);
        if (differing == 0)
        {
            return MinimumSize;
        }

        int highest = 31 - System.Numerics.BitOperations.LeadingZeroCount(differing);
        long size = 1L << highest;

        if (size < MinimumSize)
        {
            return MinimumSize;
        }

        return size > MaximumSize ? MaximumSize : (int)size;
    }
}
