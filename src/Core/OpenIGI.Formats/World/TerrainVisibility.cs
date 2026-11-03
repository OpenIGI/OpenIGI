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
    private readonly byte[] _bytes;
    private readonly TerrainGridTable _table;

    // The file is held as an owned copy, so a painted bit changes the masks every grid already handed out views on.
    private TerrainVisibility(byte[] bytes)
    {
        _bytes = bytes;
        _table = TerrainGridTable.Read(bytes, static n => n * n / 8);
    }

    /// <summary>Gets a count that goes up with every painted cell, for a caller that keeps what it drew from the masks.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the underlying record table.</summary>
    public TerrainGridTable Table => _table;

    /// <summary>Gets a value indicating whether the file's blocks walk it exactly.</summary>
    public bool IsExact => _table.IsExact;

    /// <summary>Gets how many ids carry a mask.</summary>
    public int GridCount => _table.OccupiedCount;

    /// <summary>Reads a <c>terrain.bit</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The masks.</returns>
    public static TerrainVisibility Read(ReadOnlyMemory<byte> data) => new(data.ToArray());

    /// <summary>Sets or clears one cell of a mask.</summary>
    /// <param name="id">The mask id.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="set">Whether the cell is set.</param>
    /// <returns>Whether the cell changed; a cell outside the mask, or of an id with none, never does.</returns>
    public bool SetCell(int id, int x, int y, bool set)
    {
        if (!TryGetGrid(id, out TerrainVisibilityGrid grid) || (uint)x >= (uint)grid.Size || (uint)y >= (uint)grid.Size)
        {
            return false;
        }

        int index = (y * grid.Size) + x;
        int at = _table.OffsetOf(id) + (index >> 3);
        byte bit = (byte)(1 << (index & 7));
        byte before = _bytes[at];
        _bytes[at] = set ? (byte)(before | bit) : (byte)(before & ~bit);
        if (_bytes[at] == before)
        {
            return false;
        }

        Revision++;
        return true;
    }

    /// <summary>Writes the file as it stands.</summary>
    /// <returns>A copy of its bytes.</returns>
    public byte[] Write() => (byte[])_bytes.Clone();

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
