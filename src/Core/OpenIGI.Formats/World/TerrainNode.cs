using System.Buffers.Binary;

namespace OpenIGI.Formats.World;

/// <summary>One 32-byte record of a <c>terrain.ctr</c> file: a node of the terrain octree.</summary>
/// <remarks>
/// <para>
/// The layout, from the descent in <c>sub_4D14E0</c>:
/// </para>
/// <list type="table">
/// <item><term><c>+0</c></term><description><c>u16 × 8</c> — the child record indices, one per slot.</description></item>
/// <item><term><c>+16</c></term><description><c>u8 × 8</c> — the child orientations; see <see cref="TerrainOrientation"/>.</description></item>
/// <item><term><c>+24</c></term><description><c>u8</c> — which of the eight slots are occupied.</description></item>
/// <item><term><c>+25</c>, <c>+26</c></term><description>constant across a whole level; meaning open.</description></item>
/// <item><term><c>+27</c></term><description>always zero.</description></item>
/// <item><term><c>+28</c></term><description><c>u32</c> — the byte offset of this node's block in <c>terrain.cmd</c>.</description></item>
/// </list>
/// <para>
/// A node carries geometry whether or not it has children: the octree is a level-of-detail hierarchy, so
/// a coarse node draws a coarse version of everything beneath it. That is why the descent stops as soon
/// as a node is detailed enough rather than always running to a leaf.
/// </para>
/// </remarks>
public readonly struct TerrainNode : IEquatable<TerrainNode>
{
    /// <summary>The size of one record in bytes.</summary>
    public const int Stride = 32;

    /// <summary>The child index that means "no child".</summary>
    public const ushort NoChild = 0xFFFF;

    private readonly ulong _children0;
    private readonly ulong _children1;
    private readonly ulong _orientations;

    /// <summary>Initializes a new instance of the <see cref="TerrainNode"/> struct from its record bytes.</summary>
    /// <param name="record">Exactly <see cref="Stride"/> bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="record"/> is not <see cref="Stride"/> bytes.</exception>
    public TerrainNode(ReadOnlySpan<byte> record)
    {
        if (record.Length != Stride)
        {
            throw new ArgumentException($"A terrain node is {Stride} bytes, got {record.Length}.", nameof(record));
        }

        _children0 = BinaryPrimitives.ReadUInt64LittleEndian(record);
        _children1 = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
        _orientations = BinaryPrimitives.ReadUInt64LittleEndian(record[16..]);
        ChildMask = record[24];
        CommandOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[28..]);
    }

    /// <summary>Gets the bitmask of which of the eight slots hold a child.</summary>
    /// <remarks>Zero on a leaf. Every retail node is either a leaf or has all eight.</remarks>
    public byte ChildMask { get; }

    /// <summary>Gets the byte offset of this node's block in <c>terrain.cmd</c>.</summary>
    /// <remarks>
    /// The original relocates this to a pointer once, at the end of <c>Terrain_Load</c>, and does so
    /// unconditionally — which is how level 14's out-of-range offsets become wild pointers there.
    /// </remarks>
    public uint CommandOffset { get; }

    /// <summary>Gets a value indicating whether the node has no children.</summary>
    public bool IsLeaf => ChildMask == 0;

    /// <summary>Gets the record index of a child slot.</summary>
    /// <param name="slot">The slot, <c>0</c> to <c>7</c>; see <see cref="TerrainOrientation.ChildSlot"/>.</param>
    /// <returns>The index into the file's records, or <see cref="NoChild"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside <c>0</c> to <c>7</c>.</exception>
    public ushort Child(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, TerrainOrientation.Count);

        ulong packed = slot < 4 ? _children0 : _children1;
        return (ushort)(packed >> (16 * (slot & 3)));
    }

    /// <summary>Gets the orientation byte of a child slot.</summary>
    /// <param name="slot">The slot, <c>0</c> to <c>7</c>.</param>
    /// <returns>The orientation to fold into the parent's; see <see cref="TerrainOrientation.Compose"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is outside <c>0</c> to <c>7</c>.</exception>
    public byte Orientation(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, TerrainOrientation.Count);

        return (byte)(_orientations >> (8 * slot));
    }

    /// <summary>Determines whether a slot holds a child.</summary>
    /// <param name="slot">The slot, <c>0</c> to <c>7</c>.</param>
    /// <returns><see langword="true"/> when <see cref="ChildMask"/> has the slot's bit set.</returns>
    public bool HasChild(int slot) => (uint)slot < TerrainOrientation.Count && ((ChildMask >> slot) & 1) != 0;

    /// <inheritdoc/>
    public bool Equals(TerrainNode other) =>
        _children0 == other._children0
        && _children1 == other._children1
        && _orientations == other._orientations
        && ChildMask == other.ChildMask
        && CommandOffset == other.CommandOffset;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is TerrainNode other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(_children0, _children1, _orientations, ChildMask, CommandOffset);

    /// <inheritdoc/>
    public override string ToString() =>
        IsLeaf ? $"leaf, block +{CommandOffset}" : $"mask 0x{ChildMask:X2}, block +{CommandOffset}";

    /// <summary>Determines whether two nodes are equal.</summary>
    /// <param name="left">The first node.</param>
    /// <param name="right">The second node.</param>
    /// <returns><see langword="true"/> when the records are identical.</returns>
    public static bool operator ==(TerrainNode left, TerrainNode right) => left.Equals(right);

    /// <summary>Determines whether two nodes differ.</summary>
    /// <param name="left">The first node.</param>
    /// <param name="right">The second node.</param>
    /// <returns><see langword="true"/> when the records differ.</returns>
    public static bool operator !=(TerrainNode left, TerrainNode right) => !left.Equals(right);
}
