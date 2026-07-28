namespace OpenIGI.Formats.World;

/// <summary>A <c>terrain.bit</c> file: up to 255 one-bit-per-cell masks, selected by id.</summary>
/// <remarks>
/// <para>
/// It shares its 256-record table with <see cref="TerrainHeightmap"/>; only the block size differs, at
/// one bit rather than one byte per cell. Every retail level ships one and all fourteen walk exactly.
/// </para>
/// <para>
/// The renderer samples these to decide which terrain material covers a vertex — a chunk is drawn once
/// per material its vertices select, which is how two ground types blend across one chunk. The mapping
/// from world position to cell comes from a run-time binding a level script publishes
/// (<c>0xBA5050</c>), not from this file, so this type exposes the grids and leaves that to its caller.
/// </para>
/// </remarks>
public sealed class TerrainVisibility
{
    private readonly TerrainGridTable _table;

    private TerrainVisibility(TerrainGridTable table) => _table = table;

    /// <summary>Gets the underlying record table.</summary>
    public TerrainGridTable Table => _table;

    /// <summary>Gets a value indicating whether the file's blocks walk it exactly.</summary>
    public bool IsExact => _table.IsExact;

    /// <summary>Gets how many ids carry a mask.</summary>
    public int GridCount => _table.OccupiedCount;

    /// <summary>Reads a <c>terrain.bit</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The masks.</returns>
    public static TerrainVisibility Read(ReadOnlyMemory<byte> data) =>
        new(TerrainGridTable.Read(data, n => n * n / 8));

    /// <summary>Gets the mask an id selects.</summary>
    /// <param name="id">The mask id; <c>0</c> means "none".</param>
    /// <param name="grid">Receives the mask.</param>
    /// <returns><see langword="false"/> when the id is zero or carries no block.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not a record index.</exception>
    public bool TryGetGrid(int id, out TerrainVisibilityGrid grid)
    {
        grid = default;
        if (id == 0)
        {
            return false;
        }

        int size = _table.SizeOf(id);
        ReadOnlyMemory<byte> block = _table.BlockOf(id);
        if (size == 0 || block.Length != size * size / 8)
        {
            return false;
        }

        grid = new TerrainVisibilityGrid(size, block);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"terrain bit masks: {_table}";
}

/// <summary>One bit mask: a square of one-bit cells.</summary>
/// <param name="Size">The number of cells along an edge.</param>
/// <param name="Cells">The bits, row-major, least significant bit first within a byte.</param>
public readonly record struct TerrainVisibilityGrid(int Size, ReadOnlyMemory<byte> Cells)
{
    /// <summary>Tests one cell.</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns><see langword="false"/> outside the grid, matching a cell that is clear.</returns>
    /// <remarks>
    /// The bit order is the renderer's: it forms a linear cell index, takes <c>index &gt;&gt; 3</c> as the
    /// byte and <c>index &amp; 7</c> as the shift.
    /// </remarks>
    public bool IsSet(int x, int y)
    {
        if (Cells.IsEmpty || (uint)x >= (uint)Size || (uint)y >= (uint)Size)
        {
            return false;
        }

        int index = (y * Size) + x;
        return ((Cells.Span[index >> 3] >> (index & 7)) & 1) != 0;
    }
}
