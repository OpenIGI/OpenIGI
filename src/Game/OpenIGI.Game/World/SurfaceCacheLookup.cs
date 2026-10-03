using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.World;

/// <summary>
/// Finds which card page covers a point in the world, so that a ray which hits a surface can read what
/// that surface is lit by.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what makes a traced bounce possible.</b> Sphere tracing against the distance field
/// answers "how far to the nearest surface" and nothing else — it returns a distance, not a thing. A
/// ray that stops somewhere therefore knows it hit, but not what it hit, and a bounce needs the
/// radiance leaving whatever was struck. The surface cache holds that radiance per card page; this is
/// the index that gets from a world position to the right page of it.
/// </para>
/// <para>
/// <b>A uniform grid, because the pages never move.</b> A level's geometry is static, so the structure
/// is built once at load and never rebuilt — which rules out the usual objection to uniform grids,
/// that they are expensive to keep current. What is left is their one real advantage: a constant-time
/// lookup with no traversal, which matters when every ray of every probe does one.
/// </para>
/// <para>
/// <b>Its cells are hashed rather than laid out in a box</b>, which is what lets the cell be a fixed
/// eight metres whatever the level is shaped like. A box grid has to divide the level's own extent, so
/// its cell is decided by the geometry furthest out — and a mission is not one compact box: level 8 is
/// three bases with open ground between them, and most missions leave a handful of props parked
/// kilometres away from all of it. See <see cref="CellMetres"/> for what that cost.
/// </para>
/// <para>
/// Cells hold page indices rather than placements. A placement's pages face different ways and sit in
/// different parts of its bounding box, so going through the placement would mean testing every card
/// of a large object to find the one facing the ray — whereas a page's own bounds are tight enough
/// that the grid does most of the rejecting.
/// </para>
/// </remarks>
public sealed class SurfaceCacheLookup : IDisposable
{
    private const string LogCategory = "SurfaceCache";

    /// <summary>How many buckets the grid's cells are hashed into.</summary>
    /// <remarks>
    /// Two million, which is sixteen megabytes of ranges, built once and never rebuilt — the same table
    /// the grid had when it was a hundred and twenty-eight cells cubed and addressed directly. A power
    /// of two, so the hash is masked rather than divided.
    /// </remarks>
    public const int Buckets = 1 << 21;

    /// <summary>How large one cell is, in metres, on every axis of every level.</summary>
    /// <remarks>
    /// <para>
    /// <b>Fixed, which is the whole point.</b> The cell used to be the level's longest span divided by a
    /// hundred and twenty-eight, so a level's own shape decided how finely it was indexed — and what
    /// actually decided it was the geometry FURTHEST from the level. Level 4 leaves twenty-seven objects
    /// at the world origin, thirty kilometres from the base; level 1 keeps twenty-seven buildings three
    /// kilometres underground; level 8 keeps a room of props a hundred kilometres straight up. Level 8 is
    /// also the first mission built as three separate bases with open ground between them, so its span
    /// was large before the strays even counted. Measured to those, its cell came out at 795 metres: the
    /// entire playable level fell into six cells of the two million, and the busiest held 4932 of the
    /// 6042 page references. A lookup walks its cell, so every ray that hit anything walked most of the
    /// level — 29 ms a frame of global illumination on level 8 against 4 ms on level 1, whose cache is
    /// only a third smaller. That was the one mission-specific frame-rate cliff in the game.
    /// </para>
    /// <para>
    /// Hashing the cells instead of laying them out in a box is what lets the size be fixed: a level can
    /// be any size, in any number of pieces, anywhere, and the cost of a lookup follows how densely the
    /// geometry is packed rather than how far apart its far corners are. Nothing has to be judged as
    /// belonging to the level or not, which matters for a game whose missions really do have several
    /// bases kilometres apart.
    /// </para>
    /// <para>
    /// Eight metres because that is the scale of what a card is: a wall, a hut, a container. Measured
    /// across the fourteen missions it leaves the busiest bucket between 60 and 250 pages, where the
    /// spread used to run from 72 to 4932.
    /// </para>
    /// </remarks>
    public const float CellMetres = 8f;

    /// <summary>The most cells one card page may be filed under before it is left out as malformed.</summary>
    /// <remarks>
    /// Forty cells cubed, or a card three hundred metres across. The widest card in the retail missions
    /// reaches a hundred and twenty-two metres from its model's origin, so this leaves about a factor of
    /// two in hand. It exists because hashing removed the bound the box grid used to impose for free.
    /// </remarks>
    private const long MaximumCellsPerPage = 64L * 1024L;

    /// <summary>How many pages of moving placements the lookup can list.</summary>
    /// <remarks>
    /// The movers' list is one more bucket after the hashed ones -- index <see cref="Buckets"/> -- which
    /// every lookup walks as well as its own cell. A page that moves cannot be filed by where it is: the
    /// grid is built once. A truck is a few dozen pages; this is room for a level's worth of vehicles,
    /// doors and lifts, and the buffer is sized for it up front so its handle never changes.
    /// </remarks>
    public const int MaximumMoverPages = 4096;

    private readonly IGraphicsDevice _device;
    private readonly int[] _ranges;
    private readonly int[] _references;
    private bool _disposed;

    private SurfaceCacheLookup(
        IGraphicsDevice device,
        StorageBufferHandle cells,
        StorageBufferHandle pages,
        Vector3f minimum,
        int[] ranges,
        int[] references)
    {
        _device = device;
        Cells = cells;
        Pages = pages;
        Minimum = minimum;
        References = references.Length;
        _ranges = ranges;
        _references = references;
    }

    /// <summary>Gets the buffer holding each cell's range into <see cref="Pages"/>.</summary>
    public StorageBufferHandle Cells { get; }

    /// <summary>Gets the buffer holding the page indices the cells point into.</summary>
    public StorageBufferHandle Pages { get; }

    /// <summary>Gets the point cell indices are measured from, relative to the cache's origin.</summary>
    /// <remarks>
    /// The hash does not need an origin -- a cell index may be negative -- but the subtraction keeps the
    /// numbers small enough that a float divides them exactly, and the levels sit kilometres from the
    /// cache's own origin. The shader is given this same value, so a query point and the page whose box
    /// contains it land on the same cell.
    /// </remarks>
    public Vector3f Minimum { get; }

    /// <summary>Gets how many page references the cells hold in total.</summary>
    public int References { get; }

    /// <summary>Builds the grid over a level's card pages.</summary>
    /// <param name="device">The device to allocate the buffers on.</param>
    /// <param name="capture">The captured cards, for each card's rectangle and depth.</param>
    /// <param name="instances">The placements, for where each page's card stands in the world.</param>
    /// <param name="origin">The point the cache measures its floats from.</param>
    /// <param name="logger">Where to report what was built.</param>
    /// <returns>The grid, or <see langword="null"/> when there is nothing to index.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static SurfaceCacheLookup? Build(
        IGraphicsDevice device,
        SurfaceCacheCapture.Result capture,
        SurfaceCacheInstances instances,
        Vector3d origin,
        IOpenIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(instances);

        IOpenIgiLogger log = logger ?? NullLogger.Instance;
        SurfaceCacheLayout layout = instances.Layout;

        int pageCount = layout.Pages.Length;

        if (pageCount == 0)
        {
            return null;
        }

        Vector3f[] lows = new Vector3f[pageCount];
        Vector3f[] highs = new Vector3f[pageCount];
        bool[] real = new bool[pageCount];

        Vector3f worldLow = new(float.MaxValue);
        Vector3f worldHigh = new(float.MinValue);

        foreach (SurfaceCacheInstances.Placed placed in instances.Instances)
        {
            for (int slot = 0; slot < placed.PageCount; slot++)
            {
                int page = placed.FirstPage + slot;

                if (!layout.Pages[page].IsAllocated)
                {
                    continue;
                }

                MeshCard card = capture.Cards[instances.CardForPage[page]];

                if (!TryBounds(card, placed, origin, out Vector3f low, out Vector3f high))
                {
                    continue;
                }

                lows[page] = low;
                highs[page] = high;
                real[page] = true;

                worldLow = Lower(worldLow, low);
                worldHigh = Upper(worldHigh, high);
            }
        }

        if (worldLow.X > worldHigh.X)
        {
            log.Information(LogCategory, "No card page has usable bounds; nothing was indexed.");
            return null;
        }

        // Snapped to a whole cell, so the grid's phase does not shift with the outermost card and the
        // cells a page is filed under are the cells a lookup at the same point computes.
        Vector3f minimum = new(
            MathF.Floor(worldLow.X / CellMetres) * CellMetres,
            MathF.Floor(worldLow.Y / CellMetres) * CellMetres,
            MathF.Floor(worldLow.Z / CellMetres) * CellMetres);

        int cells = Buckets;
        int[] counts = new int[cells];
        int oversized = 0;

        // Counted first, then filled. Two passes over the pages costs a few milliseconds once and saves
        // holding a quarter of a million growable lists, which is the part that would actually hurt.
        for (int pass = 0; pass < 2; pass++)
        {
            int[]? offsets = null;
            int[]? references = null;

            if (pass == 1)
            {
                offsets = new int[cells + 1];

                for (int i = 0; i < cells; i++)
                {
                    offsets[i + 1] = offsets[i] + counts[i];
                }

                references = new int[offsets[cells]];
                Array.Clear(counts);
            }

            for (int page = 0; page < pageCount; page++)
            {
                if (!real[page])
                {
                    continue;
                }

                CellRange(lows[page], highs[page], minimum,
                    out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);

                // A card whose box is larger than any card can be. Nothing in the retail missions comes
                // near this -- the widest is about a quarter of the limit -- but the cells a page is
                // filed under used to be bounded by the grid being a box, and without that bound a
                // single malformed card in a mod could ask the loader for gigabytes. Left out and
                // counted rather than clamped: a page filed in the wrong cells is worse than one the
                // bounce cannot find.
                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1) > MaximumCellsPerPage)
                {
                    if (pass == 0)
                    {
                        oversized++;
                    }

                    continue;
                }

                for (int z = z0; z <= z1; z++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int cell = Bucket(x, y, z);

                            if (pass == 0)
                            {
                                counts[cell]++;
                            }
                            else
                            {
                                references![offsets![cell] + counts[cell]++] = page;
                            }
                        }
                    }
                }
            }

            if (pass == 1)
            {
                if (oversized > 0)
                {
                    log.Warning(
                        LogCategory,
                        $"{oversized} card page(s) claim a box too large to index and were left out; "
                        + "a ray that hits one takes no bounce off it.");
                }

                return Upload(device, log, offsets!, counts, references!, minimum);
            }
        }

        return null;
    }

    /// <summary>Lists the pages of placements that move, for every lookup to walk beside its own cell.</summary>
    /// <param name="pages">The moving pages; beyond <see cref="MaximumMoverPages"/> the rest are left out.</param>
    /// <remarks>Rare -- once each time something starts moving for the first time -- so it re-uploads both.</remarks>
    public void SetMovers(IReadOnlyList<int> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int count = Math.Min(pages.Count, MaximumMoverPages);
        int[] all = new int[_references.Length + MaximumMoverPages];
        _references.CopyTo(all, 0);
        for (int i = 0; i < count; i++)
        {
            all[_references.Length + i] = pages[i];
        }

        _ranges[Buckets * 2] = _references.Length;
        _ranges[(Buckets * 2) + 1] = count;

        _device.UpdateStorageBuffer<int>(Pages, all);
        _device.UpdateStorageBuffer<int>(Cells, _ranges);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.DestroyStorageBuffer(Cells);
        _device.DestroyStorageBuffer(Pages);
    }

    private static SurfaceCacheLookup? Upload(
        IGraphicsDevice device,
        IOpenIgiLogger log,
        int[] offsets,
        int[] counts,
        int[] references,
        Vector3f minimum)
    {
        int cells = counts.Length;

        // One range past the hashed buckets: the movers' list, empty until something moves. See SetMovers.
        int[] ranges = new int[(cells + 1) * 2];
        ranges[cells * 2] = references.Length;
        int occupied = 0;
        int busiest = 0;

        for (int i = 0; i < cells; i++)
        {
            ranges[i * 2] = offsets[i];
            ranges[(i * 2) + 1] = counts[i];

            if (counts[i] > 0)
            {
                occupied++;
                busiest = Math.Max(busiest, counts[i]);
            }
        }

        try
        {
            StorageBufferHandle cellBuffer = device.CreateStorageBuffer(
                ranges.Length * sizeof(int), "cards.lookup.cells");
            device.UpdateStorageBuffer<int>(cellBuffer, ranges);

            StorageBufferHandle pageBuffer = device.CreateStorageBuffer(
                (references.Length + MaximumMoverPages) * sizeof(int), "cards.lookup.pages");

            if (references.Length > 0)
            {
                device.UpdateStorageBuffer<int>(pageBuffer, references);
            }

            log.Information(
                LogCategory,
                $"Card lookup: {references.Length} reference(s) over {occupied} occupied bucket(s) of "
                + $"{cells}, busiest {busiest}, cell {CellMetres:0.##} m.");

            return new SurfaceCacheLookup(
                device, cellBuffer, pageBuffer, minimum, ranges, references);
        }
        catch (GraphicsDeviceException error)
        {
            log.Error(LogCategory, $"The card lookup could not be built: {error.Message}");
            return null;
        }
    }

    /// <summary>Works out where a card page stands in the world.</summary>
    /// <remarks>
    /// The card's rectangle swept back by its own captured depth, which is the volume its texels can
    /// describe: a card is a rectangle with a heightfield behind it, so the box has to include the
    /// deepest surface it recorded or the pages of anything with relief would not be found where their
    /// geometry actually is.
    /// </remarks>
    private static bool TryBounds(
        MeshCard card,
        SurfaceCacheInstances.Placed placed,
        Vector3d origin,
        out Vector3f low,
        out Vector3f high)
    {
        low = new Vector3f(float.MaxValue);
        high = new Vector3f(float.MinValue);

        if (!float.IsFinite(card.HalfWidth) || !float.IsFinite(card.HalfHeight) || !float.IsFinite(card.Depth))
        {
            return false;
        }

        for (int corner = 0; corner < 8; corner++)
        {
            float u = (corner & 1) == 0 ? -card.HalfWidth : card.HalfWidth;
            float v = (corner & 2) == 0 ? -card.HalfHeight : card.HalfHeight;
            float w = (corner & 4) == 0 ? 0f : -card.Depth;

            Vector3f local = new(
                card.Centre.X + (card.Right.X * u) + (card.Up.X * v) + (card.Direction.X * w),
                card.Centre.Y + (card.Right.Y * u) + (card.Up.Y * v) + (card.Direction.Y * w),
                card.Centre.Z + (card.Right.Z * u) + (card.Up.Z * v) + (card.Direction.Z * w));

            // Transposed, matching the lighting shader and the distance field. The three must agree or
            // a page is indexed somewhere its own geometry is not.
            Matrix3f rotation = placed.Orientation;

            Vector3f world = new(
                (float)(placed.Position.X - origin.X)
                    + (rotation.M11 * local.X) + (rotation.M21 * local.Y) + (rotation.M31 * local.Z),
                (float)(placed.Position.Y - origin.Y)
                    + (rotation.M12 * local.X) + (rotation.M22 * local.Y) + (rotation.M32 * local.Z),
                (float)(placed.Position.Z - origin.Z)
                    + (rotation.M13 * local.X) + (rotation.M23 * local.Y) + (rotation.M33 * local.Z));

            low = Lower(low, world);
            high = Upper(high, world);
        }

        return float.IsFinite(low.X) && float.IsFinite(high.X);
    }

    /// <summary>The span of cells a page's box covers, as signed indices off <paramref name="minimum"/>.</summary>
    private static void CellRange(
        Vector3f low,
        Vector3f high,
        Vector3f minimum,
        out int x0, out int y0, out int z0,
        out int x1, out int y1, out int z1)
    {
        x0 = CellAt(low.X - minimum.X);
        y0 = CellAt(low.Y - minimum.Y);
        z0 = CellAt(low.Z - minimum.Z);
        x1 = CellAt(high.X - minimum.X);
        y1 = CellAt(high.Y - minimum.Y);
        z1 = CellAt(high.Z - minimum.Z);
    }

    private static int CellAt(float at) => (int)MathF.Floor(at / CellMetres);

    /// <summary>Which bucket a cell is filed under. Must match the shader's hash exactly.</summary>
    /// <remarks>
    /// Teschner's three large primes, one per axis, exclusive-ored and masked down to the table. Chosen
    /// because it is short enough to be written twice -- once here and once in GLSL -- with no doubt that
    /// the two agree, which is the only property that matters: a page filed under a bucket the lookup
    /// does not compute is a page the bounce cannot find. Unsigned multiplication wraps identically in
    /// both languages, and a negative index converts to the same bit pattern in both.
    /// </remarks>
    private static int Bucket(int x, int y, int z)
    {
        unchecked
        {
            uint hash = ((uint)x * 0x8DA6B343u) ^ ((uint)y * 0xD8163841u) ^ ((uint)z * 0xCB1AB31Fu);
            return (int)(hash & (Buckets - 1));
        }
    }

    private static Vector3f Lower(Vector3f a, Vector3f b) =>
        new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));

    private static Vector3f Upper(Vector3f a, Vector3f b) =>
        new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
}
