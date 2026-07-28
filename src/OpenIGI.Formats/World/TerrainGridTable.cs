using System.Buffers.Binary;

namespace OpenIGI.Formats.World;

/// <summary>
/// The table both <c>terrain.bit</c> and <c>terrain.hmp</c> open with: 256 fixed records, then one
/// variable-size block per record that declares a non-zero size.
/// </summary>
/// <remarks>
/// <para>
/// Each record is <c>{u32 data, u32 flags, u32 n}</c>. <c>data</c> is a file offset the original
/// back-patches into a pointer as it walks — it is meaningless on disk, and the blocks simply follow the
/// table in record order. Only <c>n</c> and the ordering matter for reading, and the arithmetic closes
/// byte-exactly for every retail file.
/// </para>
/// <para>
/// The record index is the id the renderer selects a grid by, and <b>id 0 means "none"</b>: the original
/// guards every lookup with a non-zero test, and its runtime expansion of these tables reuses record
/// zero's storage for unrelated globals.
/// </para>
/// <para>
/// A file shorter than the table is not an error. The original allocates a zeroed table instead, so a
/// level with a stub file behaves as though every record were empty.
/// </para>
/// </remarks>
public sealed class TerrainGridTable
{
    /// <summary>How many records the fixed table holds.</summary>
    public const int RecordCount = 256;

    /// <summary>The size of one record in bytes.</summary>
    public const int RecordStride = 12;

    /// <summary>The size of the fixed table in bytes.</summary>
    public const int TableSize = RecordCount * RecordStride;

    private readonly int[] _sizes;
    private readonly ReadOnlyMemory<byte>[] _blocks;

    private TerrainGridTable(int[] sizes, ReadOnlyMemory<byte>[] blocks, uint[] flags, int consumed, int length)
    {
        _sizes = sizes;
        _blocks = blocks;
        Flags = flags;
        BytesConsumed = consumed;
        Length = length;
    }

    /// <summary>Gets the record flags, in record order.</summary>
    /// <remarks>Carried through undecoded; no consumer of either file needs them yet.</remarks>
    public IReadOnlyList<uint> Flags { get; }

    /// <summary>Gets how many bytes the walk consumed, which equals <see cref="Length"/> for a sound file.</summary>
    public int BytesConsumed { get; }

    /// <summary>Gets the file's size in bytes.</summary>
    public int Length { get; }

    /// <summary>Gets a value indicating whether the walk consumed the file exactly.</summary>
    public bool IsExact => BytesConsumed == Length;

    /// <summary>Gets the grid size a record declares, zero when the record is empty.</summary>
    /// <param name="id">The record index, <c>0</c> to <c>255</c>.</param>
    /// <returns>The declared <c>n</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not a record index.</exception>
    public int SizeOf(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, RecordCount);
        return _sizes[id];
    }

    /// <summary>Gets a record's block.</summary>
    /// <param name="id">The record index, <c>0</c> to <c>255</c>.</param>
    /// <returns>The block, empty when the record declares no size or its block ran off the end.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not a record index.</exception>
    public ReadOnlyMemory<byte> BlockOf(int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(id, RecordCount);
        return _blocks[id];
    }

    /// <summary>Gets how many records declare a block.</summary>
    public int OccupiedCount
    {
        get
        {
            int n = 0;
            foreach (int size in _sizes)
            {
                if (size != 0)
                {
                    n++;
                }
            }

            return n;
        }
    }

    /// <summary>Walks a file of this shape.</summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="blockSize">
    /// The size in bytes of a record's block, given its declared <c>n</c>. <c>n * n / 8</c> for
    /// <c>terrain.bit</c>, <c>(n + 1) * (n + 1)</c> for <c>terrain.hmp</c>.
    /// </param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="blockSize"/> is <see langword="null"/>.</exception>
    public static TerrainGridTable Read(ReadOnlyMemory<byte> data, Func<int, int> blockSize)
    {
        ArgumentNullException.ThrowIfNull(blockSize);

        int[] sizes = new int[RecordCount];
        uint[] flags = new uint[RecordCount];
        ReadOnlyMemory<byte>[] blocks = new ReadOnlyMemory<byte>[RecordCount];

        if (data.Length < TableSize)
        {
            return new TerrainGridTable(sizes, blocks, flags, 0, data.Length);
        }

        ReadOnlySpan<byte> table = data.Span[..TableSize];
        for (int i = 0; i < RecordCount; i++)
        {
            ReadOnlySpan<byte> record = table.Slice(i * RecordStride, RecordStride);
            flags[i] = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            uint n = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            sizes[i] = n <= int.MaxValue ? (int)n : 0;
        }

        int cursor = TableSize;
        for (int i = 0; i < RecordCount; i++)
        {
            if (sizes[i] == 0)
            {
                continue;
            }

            int size = blockSize(sizes[i]);
            if (size <= 0 || size > data.Length - cursor)
            {
                // Stop at the first block that does not fit rather than reading past it. IsExact then
                // reports the file as unsound, which is how a caller tells a stub apart from real data.
                break;
            }

            blocks[i] = data.Slice(cursor, size);
            cursor += size;
        }

        return new TerrainGridTable(sizes, blocks, flags, cursor, data.Length);
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{OccupiedCount} of {RecordCount} records, {BytesConsumed} of {Length} bytes consumed";
}
