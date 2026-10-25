namespace OpenIGI.Game.World;

/// <summary>The broad phase: a sparse 2D grid over the entries' build-time bounding spheres.</summary>
/// <remarks>
/// <para>
/// The flat scan this replaces touched every placed object's sphere for every ray — the AI's
/// lines of sight, every soldier's ground snap, every dropped gun's contact probe, all against
/// the whole level. A ray now touches the entries of the cells it passes, which for the vertical
/// ground probe is one cell.
/// </para>
/// <para>
/// Two-dimensional on purpose: a level is kilometres across and metres tall, so a Z axis would
/// cut nothing while tripling the buckets. An entry whose sphere spans an unreasonable number of
/// cells goes to the <see cref="Oversized"/> list and is tested every query instead of being
/// written into hundreds of buckets.
/// </para>
/// </remarks>
internal sealed class CollisionStaticGrid
{
    /// <summary>The cell edge, chosen against typical building footprints.</summary>
    public static readonly double CellUnits = 32.0 * WorldScene.UnitsPerMetre;

    /// <summary>A sphere spanning more cells than this per axis is not worth bucketing.</summary>
    private const int OversizedCellSpan = 32;

    private readonly Dictionary<long, int[]> _cells = [];
    private readonly int[] _stamps;
    private int _stamp;

    public CollisionStaticGrid(ObjectCollisionEntry[] entries)
    {
        _stamps = new int[entries.Length];
        Dictionary<long, List<int>> cells = [];
        List<int> oversized = [];

        for (int i = 0; i < entries.Length; i++)
        {
            ObjectCollisionEntry entry = entries[i];
            int minX = CellOf(entry.Centre.X - entry.Radius);
            int maxX = CellOf(entry.Centre.X + entry.Radius);
            int minY = CellOf(entry.Centre.Y - entry.Radius);
            int maxY = CellOf(entry.Centre.Y + entry.Radius);

            if (maxX - minX >= OversizedCellSpan || maxY - minY >= OversizedCellSpan)
            {
                oversized.Add(i);
                continue;
            }

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    long key = Key(x, y);
                    if (!cells.TryGetValue(key, out List<int>? bucket))
                    {
                        bucket = [];
                        cells.Add(key, bucket);
                    }

                    bucket.Add(i);
                }
            }
        }

        foreach ((long key, List<int> bucket) in cells)
        {
            _cells.Add(key, [.. bucket]);
        }

        Oversized = [.. oversized];
    }

    /// <summary>Gets the entries too large to bucket, tested on every query.</summary>
    public int[] Oversized { get; }

    public static int CellOf(double coordinate) => (int)Math.Floor(coordinate / CellUnits);

    /// <summary>Starts a query, invalidating the previous one's visit marks in O(1).</summary>
    public void BeginQuery() => _stamp++;

    /// <summary>Marks an entry visited this query; true when it already was.</summary>
    /// <remarks>
    /// An entry sits in every cell its sphere touches, so a ray crossing several of them would
    /// test its triangles once per cell without this.
    /// </remarks>
    public bool AlreadyVisited(int index)
    {
        if (_stamps[index] == _stamp)
        {
            return true;
        }

        _stamps[index] = _stamp;
        return false;
    }

    public bool TryGetCell(int x, int y, out int[] indices) =>
        _cells.TryGetValue(Key(x, y), out indices!);

    private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
}
