namespace OpenIGI.Formats.World;

/// <summary>A <c>terrain.hmp</c> file: up to 255 height grids, selected by id.</summary>
/// <remarks>
/// <para>
/// <b>Seven of the fourteen retail levels ship no <c>terrain.hmp</c> at all.</b> The original guards the
/// load with a file-exists test and leaves the pointer null, so a heightmap is optional and a terrain
/// without one is complete, not broken — the chunk geometry in <c>terrain.cmd</c> already carries the
/// shape, and the heightmap only refines the finest levels of detail.
/// </para>
/// <para>
/// Which grid a chunk samples, and the origin and scale it samples at, are not in this file. They come
/// from run-time bindings a level script's tasks publish (<c>0xBA2030</c>), which is why
/// <see cref="TerrainHeightGrid.Sample"/> takes grid coordinates and leaves the mapping to its caller.
/// </para>
/// <para>
/// The heightmap keeps its own copy of the file, so an editor can change samples in place
/// (<see cref="SetCorner"/>) and every <see cref="TerrainHeightGrid"/> already handed out sees the change at once;
/// <see cref="Write"/> gives the file back, byte for byte as it was read apart from the samples changed.
/// </para>
/// </remarks>
public sealed class TerrainHeightmap
{
    private byte[] _bytes;
    private TerrainGridTable _table;

    private TerrainHeightmap(byte[] bytes)
    {
        _bytes = bytes;
        _table = TerrainGridTable.Read(bytes, n => (n + 1) * (n + 1));
    }

    /// <summary>Gets a number that changes every time a sample is changed.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the underlying record table.</summary>
    public TerrainGridTable Table => _table;

    /// <summary>Gets a value indicating whether the file's blocks walk it exactly.</summary>
    public bool IsExact => _table.IsExact;

    /// <summary>Gets how many ids carry a grid.</summary>
    public int GridCount => _table.OccupiedCount;

    /// <summary>Reads a <c>terrain.hmp</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The heightmap.</returns>
    /// <remarks>
    /// A grid of <c>n</c> cells stores <c>(n + 1) * (n + 1)</c> bytes because the samples sit on the
    /// grid's <em>corners</em>. The renderer confirms the same stride from the other side: it steps a row
    /// by <c>(1 &lt;&lt; shift) + 1</c>.
    /// </remarks>
    public static TerrainHeightmap Read(ReadOnlyMemory<byte> data) => new(data.ToArray());

    /// <summary>Makes a heightmap with no grids, for a level that ships none.</summary>
    /// <returns>A heightmap of an empty table, which the original reads as a level with no grids.</returns>
    public static TerrainHeightmap Create() => new(new byte[TerrainGridTable.TableSize]);

    /// <summary>Gets the lowest id that carries no grid, which is the one the original hands a new grid.</summary>
    /// <returns>The id, or <c>-1</c> when all of them are taken.</returns>
    public int FirstFreeId()
    {
        for (int id = 0; id < TerrainGridTable.RecordCount; id++)
        {
            if (_table.SizeOf(id) == 0)
            {
                return id;
            }
        }

        return -1;
    }

    /// <summary>Adds a flat grid: every sample at <see cref="TerrainHeightGrid.Bias"/>, displacing nothing.</summary>
    /// <param name="id">The id to give it, one that carries no grid.</param>
    /// <param name="size">The cells along an edge: a power of two from 8 to 256, as the original accepts.</param>
    /// <exception cref="ArgumentOutOfRangeException">The id is taken or not an id, or the size is not one the original accepts.</exception>
    /// <exception cref="InvalidOperationException">The file's blocks do not walk it exactly, so it cannot be laid out again.</exception>
    /// <remarks>
    /// The blocks follow the table in id order, so the file is laid out again; every grid handed out before refers to the
    /// old layout and has to be fetched again with <see cref="TryGetGrid"/>.
    /// </remarks>
    public void AddGrid(int id, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, TerrainGridTable.RecordCount);
        if (_table.SizeOf(id) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "The id already carries a grid.");
        }

        if (size is < 8 or > 256 || !int.IsPow2(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A grid is 8 to 256 cells across, a power of two.");
        }

        byte[] block = new byte[(size + 1) * (size + 1)];
        Array.Fill(block, (byte)TerrainHeightGrid.Bias);
        LayOut(id, size, block);
    }

    /// <summary>Takes a grid out of the file.</summary>
    /// <param name="id">The grid's id.</param>
    /// <exception cref="ArgumentOutOfRangeException">The id carries no grid.</exception>
    /// <exception cref="InvalidOperationException">The file's blocks do not walk it exactly.</exception>
    public void RemoveGrid(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, TerrainGridTable.RecordCount);
        if (_table.SizeOf(id) == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "The id carries no grid.");
        }

        LayOut(id, 0, []);
    }

    /// <summary>Changes one corner sample of a grid.</summary>
    /// <param name="id">The grid's record index.</param>
    /// <param name="x">The column, <c>0</c> to the grid's size.</param>
    /// <param name="y">The row, <c>0</c> to the grid's size.</param>
    /// <param name="value">The raw signed sample; clamped to a signed byte.</param>
    /// <exception cref="ArgumentOutOfRangeException">The id carries no grid, or the corner is outside it.</exception>
    public void SetCorner(int id, int x, int y, int value)
    {
        int size = _table.SizeOf(id);
        int offset = _table.OffsetOf(id);
        if (size == 0 || offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "The id carries no grid.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(x, size);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(y, size);

        byte raw = (byte)(sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
        ref byte at = ref _bytes[offset + (y * (size + 1)) + x];
        if (at != raw)
        {
            at = raw;
            Revision++;
        }
    }

    // The file again with one record's block replaced: the table as it was but for that record, then every block in id order.
    private void LayOut(int id, int size, byte[] block)
    {
        if (!_table.IsExact)
        {
            throw new InvalidOperationException("The heightmap's blocks do not walk the file exactly, so it cannot be laid out again.");
        }

        int length = TerrainGridTable.TableSize;
        for (int i = 0; i < TerrainGridTable.RecordCount; i++)
        {
            length += i == id ? block.Length : _table.BlockOf(i).Length;
        }

        byte[] bytes = new byte[length];
        _bytes.AsSpan(0, Math.Min(_bytes.Length, TerrainGridTable.TableSize)).CopyTo(bytes);
        Span<byte> record = bytes.AsSpan(id * TerrainGridTable.RecordStride, TerrainGridTable.RecordStride);
        record.Clear();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(record[8..], (uint)size);
        int cursor = TerrainGridTable.TableSize;
        for (int i = 0; i < TerrainGridTable.RecordCount; i++)
        {
            ReadOnlySpan<byte> body = i == id ? block : _table.BlockOf(i).Span;
            body.CopyTo(bytes.AsSpan(cursor));
            cursor += body.Length;
        }

        _bytes = bytes;
        _table = TerrainGridTable.Read(bytes, n => (n + 1) * (n + 1));
        Revision++;
    }

    /// <summary>Gives the file back as it stands now.</summary>
    /// <returns>A copy of the file's bytes, with every changed sample in it.</returns>
    public byte[] Write() => (byte[])_bytes.Clone();

    /// <summary>Gets the grid an id selects.</summary>
    /// <param name="id">The grid's record index — a <c>HeightMap</c> task's <c>Bitmap ID</c>.</param>
    /// <param name="grid">Receives the grid.</param>
    /// <returns><see langword="false"/> when the id carries no block.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not a record index.</exception>
    /// <remarks>
    /// Zero is a valid record: <c>sub_507410</c> indexes the table straight by the task's <c>Bitmap ID</c>,
    /// and level 1's first grid and level 3's only grid are both id 0. The zero that means "none" is the
    /// run-time binding id a chunk descriptor carries, which is a texture-modifier slot plus one — a
    /// different number, which an earlier reading of this method conflated with the record index.
    /// </remarks>
    public bool TryGetGrid(int id, out TerrainHeightGrid grid)
    {
        grid = default;

        int size = _table.SizeOf(id);
        ReadOnlyMemory<byte> block = _table.BlockOf(id);
        if (size == 0 || block.Length != (size + 1) * (size + 1))
        {
            return false;
        }

        grid = new TerrainHeightGrid(size, block);
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"terrain heightmaps: {_table}";
}

/// <summary>One height grid: a square of signed corner samples.</summary>
/// <param name="Size">The number of cells along an edge; the grid stores one more sample than that.</param>
/// <param name="Corners">The samples, row-major, <see cref="Stride"/> per row.</param>
public readonly record struct TerrainHeightGrid(int Size, ReadOnlyMemory<byte> Corners)
{
    /// <summary>The sample value that means "no displacement".</summary>
    /// <remarks>The renderer subtracts this before scaling, so a grid of this value is flat.</remarks>
    public const double Bias = 64.0;

    /// <summary>The engine units one unit of a sample is worth.</summary>
    public const double UnitsPerSample = 256.0;

    /// <summary>Gets how many samples a row holds.</summary>
    public int Stride => Size + 1;

    /// <summary>Reads one corner sample, unscaled.</summary>
    /// <param name="x">The column, <c>0</c> to <see cref="Size"/>.</param>
    /// <param name="y">The row, <c>0</c> to <see cref="Size"/>.</param>
    /// <returns>The sample, which is a <em>signed</em> byte.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is outside the grid.</exception>
    public int Corner(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(x, Size);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(y, Size);

        return (sbyte)Corners.Span[(y * Stride) + x];
    }

    /// <summary>Samples the grid bilinearly and converts to engine units.</summary>
    /// <param name="x">The column in grid coordinates; the fractional part interpolates.</param>
    /// <param name="y">The row in grid coordinates.</param>
    /// <returns>The height in engine units, zero outside the grid.</returns>
    /// <remarks>
    /// <para>
    /// The original truncates towards zero to reach the cell and interpolates in <c>double</c>, and this
    /// does the same.
    /// </para>
    /// <para>
    /// The far edge — <paramref name="x"/> or <paramref name="y"/> exactly <see cref="Size"/> — is
    /// <em>inside</em> the grid: the row holds <c>Size + 1</c> samples so that the cell's last column
    /// is covered, and the original reads it (its wrap-around neighbour is multiplied by a zero
    /// fraction). Treating it as outside opened a crack of the full height along every boundary
    /// between two cells, where the chunk on one side sampled its grid's last column as flat while the
    /// chunk on the other side sampled its own grid's first. Beyond that, a sample outside the grid
    /// reads as flat rather than wrapping: the original would read its neighbour's row, which is an
    /// artefact of the pointer arithmetic rather than intent, and no bound chunk reaches it.
    /// </para>
    /// </remarks>
    public double Sample(double x, double y)
    {
        if (Corners.IsEmpty || double.IsNaN(x) || double.IsNaN(y))
        {
            return 0.0;
        }

        int ix = (int)x;
        int iy = (int)y;
        if (ix < 0 || iy < 0 || ix > Size || iy > Size)
        {
            return 0.0;
        }

        double tx = x - ix;
        double ty = y - iy;

        // On the far edge the sample is the last corner itself: step back one cell and interpolate all
        // the way across it, which lands on that corner without reading past the row.
        if (ix == Size)
        {
            ix--;
            tx = 1.0;
        }

        if (iy == Size)
        {
            iy--;
            ty = 1.0;
        }

        ReadOnlySpan<byte> corners = Corners.Span;
        int row0 = iy * Stride;
        int row1 = row0 + Stride;

        double h00 = (sbyte)corners[row0 + ix];
        double h10 = (sbyte)corners[row0 + ix + 1];
        double h01 = (sbyte)corners[row1 + ix];
        double h11 = (sbyte)corners[row1 + ix + 1];

        double lower = h00 + ((h10 - h00) * tx);
        double upper = h01 + ((h11 - h01) * tx);
        return (lower + ((upper - lower) * ty) - Bias) * UnitsPerSample;
    }
}
