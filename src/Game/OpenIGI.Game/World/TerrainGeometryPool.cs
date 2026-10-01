using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>Recycles the vertex and index arrays a <see cref="TerrainChunkGeometry"/> keeps.</summary>
/// <remarks>
/// <para>
/// A chunk's geometry outlives its build — the renderer re-reads it to re-append a rebuilt page — so it
/// cannot be scratch. But it dies with the chunk, and a moving camera evicts chunks at the same rate it
/// builds them, so the arrays one chunk gives up are the arrays the next one needs. At forty-eight bytes
/// a vertex a chunk's vertices are routinely over the eighty-five kilobyte threshold, which put every
/// build straight on the large object heap and every eviction's worth of them in front of a gen2
/// collection.
/// </para>
/// <para>
/// Buckets are keyed by exact length, never by "at least this long". Chunks at one level of detail share
/// a vertex count, so exact keys still hit almost every time, and it means a recycled array is
/// indistinguishable from a fresh one: <c>Vertices.Length</c> keeps meaning what it says, which is what
/// the renderer divides by the layer count to recover a chunk's vertex count.
/// </para>
/// <para>
/// Nothing here is thread-safe; it belongs to the renderer that builds and evicts, on its own thread.
/// </para>
/// </remarks>
public sealed class TerrainGeometryPool
{
    /// <summary>How many arrays one length keeps before the rest are left to the collector.</summary>
    /// <remarks>
    /// A bound rather than a cache: the point is to serve the churn of a camera crossing the level, not
    /// to hold every chunk the player has ever walked past. Past this the surplus is simply dropped.
    /// </remarks>
    private const int MaximumPerBucket = 32;

    private readonly Dictionary<int, Stack<Vertex3D[]>> _vertices = [];
    private readonly Dictionary<int, Stack<ushort[]>> _indices = [];

    /// <summary>Takes a vertex array of exactly this length, recycled if one is free.</summary>
    /// <param name="length">The length required.</param>
    /// <returns>An array of exactly <paramref name="length"/>, its contents undefined.</returns>
    public Vertex3D[] RentVertices(int length) => Rent(_vertices, length);

    /// <summary>Takes an index array of exactly this length, recycled if one is free.</summary>
    /// <param name="length">The length required.</param>
    /// <returns>An array of exactly <paramref name="length"/>, its contents undefined.</returns>
    public ushort[] RentIndices(int length) => Rent(_indices, length);

    /// <summary>Offers a finished chunk's arrays back for the next chunk to use.</summary>
    /// <param name="geometry">The geometry of a chunk that has been evicted, or <see langword="null"/>.</param>
    /// <remarks>
    /// Only ever called with geometry nothing refers to any more. The renderer copies a chunk's vertices
    /// into its batch page and uploads them to the device on the way in, so once the chunk leaves the
    /// cache its arrays are unreachable.
    /// </remarks>
    public void Return(TerrainChunkGeometry? geometry)
    {
        if (geometry is null)
        {
            return;
        }

        Return(_vertices, geometry.Vertices);
        Return(_indices, geometry.Indices);
    }

    /// <summary>Drops everything held, for a level change.</summary>
    public void Clear()
    {
        _vertices.Clear();
        _indices.Clear();
    }

    private static T[] Rent<T>(Dictionary<int, Stack<T[]>> buckets, int length)
    {
        if (length > 0 && buckets.TryGetValue(length, out Stack<T[]>? free) && free.Count > 0)
        {
            return free.Pop();
        }

        return new T[length];
    }

    private static void Return<T>(Dictionary<int, Stack<T[]>> buckets, T[]? array)
    {
        if (array is null || array.Length == 0)
        {
            return;
        }

        if (!buckets.TryGetValue(array.Length, out Stack<T[]>? free))
        {
            free = new Stack<T[]>();
            buckets[array.Length] = free;
        }

        if (free.Count < MaximumPerBucket)
        {
            free.Push(array);
        }
    }
}
