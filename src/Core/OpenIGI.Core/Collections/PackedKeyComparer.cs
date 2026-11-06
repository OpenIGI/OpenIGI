namespace OpenIGI.Core.Collections;

/// <summary>
/// Compares <see cref="long"/> keys that pack two numbers -- a grid cell's column and row, an edge's two vertex ids -- and
/// hashes them so that packed pairs spread over a table's buckets.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="long.GetHashCode"/> is the two halves XORed together, so the key <c>(a &lt;&lt; 32) | b</c> hashes to
/// <c>a ^ b</c>: every edge between neighbouring vertices, and every grid cell on a diagonal, lands on a handful of hash
/// values. A dictionary of those keys degrades into long chains. I.G.I. 2's terrain tessellation spent half of every frame
/// walking them.
/// </para>
/// <para>
/// The hash here is SplitMix64's finalizer, which lets every input bit reach every output bit.
/// </para>
/// </remarks>
public sealed class PackedKeyComparer : IEqualityComparer<long>
{
    /// <summary>Gets the comparer.</summary>
    public static PackedKeyComparer Instance { get; } = new();

    private PackedKeyComparer()
    {
    }

    /// <inheritdoc/>
    public bool Equals(long x, long y) => x == y;

    /// <inheritdoc/>
    public int GetHashCode(long obj)
    {
        ulong z = unchecked((ulong)obj + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return unchecked((int)(z ^ (z >> 31)));
    }
}
