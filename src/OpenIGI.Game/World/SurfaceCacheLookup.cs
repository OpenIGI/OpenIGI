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
/// Cells hold page indices rather than placements. A placement's pages face different ways and sit in
/// different parts of its bounding box, so going through the placement would mean testing every card
/// of a large object to find the one facing the ray — whereas a page's own bounds are tight enough
/// that the grid does most of the rejecting.
/// </para>
/// </remarks>
public sealed class SurfaceCacheLookup : IDisposable
{
    private const string LogCategory = "SurfaceCache";

    /// <summary>How many cells the grid gets along each axis.</summary>
    /// <remarks>
    /// A hundred and twenty-eight cubed is two million cells and sixteen megabytes of ranges, built
    /// once and never rebuilt. It started at sixty-four, which put a cell at twelve metres and left the
    /// busiest holding 196 pages — and the lookup walks every page in a cell, so that figure multiplied
    /// by eight rays by a million texels is what the gather actually costs. Halving the cell is the
    /// cheapest thing that moves it.
    /// </remarks>
    public const int Resolution = 128;

    private readonly IGraphicsDevice _device;
    private bool _disposed;

    private SurfaceCacheLookup(
        IGraphicsDevice device,
        StorageBufferHandle cells,
        StorageBufferHandle pages,
        Vector3f minimum,
        float cellSize,
        int references)
    {
        _device = device;
        Cells = cells;
        Pages = pages;
        Minimum = minimum;
        CellSize = cellSize;
        References = references;
    }

    /// <summary>Gets the buffer holding each cell's range into <see cref="Pages"/>.</summary>
    public StorageBufferHandle Cells { get; }

    /// <summary>Gets the buffer holding the page indices the cells point into.</summary>
    public StorageBufferHandle Pages { get; }

    /// <summary>Gets the grid's minimum corner, relative to the cache's origin.</summary>
    public Vector3f Minimum { get; }

    /// <summary>Gets the size of one cell, in metres.</summary>
    public float CellSize { get; }

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
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(instances);

        IIgiLogger log = logger ?? NullLogger.Instance;
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

        // A margin, so a hit that lands a touch outside the outermost card -- which the trace's own
        // error makes routine -- still falls in a cell rather than off the grid entirely.
        float span = MathF.Max(
            MathF.Max(worldHigh.X - worldLow.X, worldHigh.Y - worldLow.Y),
            MathF.Max(worldHigh.Z - worldLow.Z, 1f));

        float cellSize = span * 1.02f / Resolution;
        Vector3f minimum = new(
            worldLow.X - cellSize, worldLow.Y - cellSize, worldLow.Z - cellSize);

        int cells = Resolution * Resolution * Resolution;
        int[] counts = new int[cells];

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

                CellRange(lows[page], highs[page], minimum, cellSize,
                    out int x0, out int y0, out int z0, out int x1, out int y1, out int z1);

                for (int z = z0; z <= z1; z++)
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int cell = (((z * Resolution) + y) * Resolution) + x;

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
                return Upload(device, log, offsets!, counts, references!, minimum, cellSize);
            }
        }

        return null;
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
        IIgiLogger log,
        int[] offsets,
        int[] counts,
        int[] references,
        Vector3f minimum,
        float cellSize)
    {
        int cells = counts.Length;
        int[] ranges = new int[cells * 2];
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
                Math.Max(references.Length, 1) * sizeof(int), "cards.lookup.pages");

            if (references.Length > 0)
            {
                device.UpdateStorageBuffer<int>(pageBuffer, references);
            }

            log.Information(
                LogCategory,
                $"Card lookup: {references.Length} reference(s) over {occupied} occupied cell(s) of "
                + $"{cells}, busiest {busiest}, cell {cellSize:0.##} m.");

            return new SurfaceCacheLookup(
                device, cellBuffer, pageBuffer, minimum, cellSize, references.Length);
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

    private static void CellRange(
        Vector3f low,
        Vector3f high,
        Vector3f minimum,
        float cellSize,
        out int x0, out int y0, out int z0,
        out int x1, out int y1, out int z1)
    {
        x0 = Clamp((low.X - minimum.X) / cellSize);
        y0 = Clamp((low.Y - minimum.Y) / cellSize);
        z0 = Clamp((low.Z - minimum.Z) / cellSize);
        x1 = Clamp((high.X - minimum.X) / cellSize);
        y1 = Clamp((high.Y - minimum.Y) / cellSize);
        z1 = Clamp((high.Z - minimum.Z) / cellSize);
    }

    private static int Clamp(float at) =>
        Math.Clamp((int)MathF.Floor(at), 0, Resolution - 1);

    private static Vector3f Lower(Vector3f a, Vector3f b) =>
        new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));

    private static Vector3f Upper(Vector3f a, Vector3f b) =>
        new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));
}
