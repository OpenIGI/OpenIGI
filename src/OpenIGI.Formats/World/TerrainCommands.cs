using System.Buffers.Binary;

namespace OpenIGI.Formats.World;

/// <summary>
/// A <c>terrain.cmd</c> file: the geometry of every octree node, addressed by the byte offset each
/// <see cref="TerrainNode.CommandOffset"/> holds.
/// </summary>
/// <remarks>
/// <para>
/// There is no directory and no block count — the file is only ever entered through an octree node's
/// offset, which is why <see cref="TryGetBlock"/> is the whole interface. The original relocates each
/// offset to a pointer once at load; keeping them as offsets means a malformed file yields a failed
/// lookup instead of a wild pointer, which is exactly the case level 14 presents.
/// </para>
/// </remarks>
public sealed class TerrainCommands
{
    private readonly ReadOnlyMemory<byte> _data;

    private TerrainCommands(ReadOnlyMemory<byte> data) => _data = data;

    /// <summary>Gets the file's size in bytes.</summary>
    public int Length => _data.Length;

    /// <summary>Wraps a <c>terrain.cmd</c> file.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The blob, ready to be indexed by node offsets.</returns>
    public static TerrainCommands Read(ReadOnlyMemory<byte> data) => new(data);

    /// <summary>Reads the block at a node's offset.</summary>
    /// <param name="offset">The node's <see cref="TerrainNode.CommandOffset"/>.</param>
    /// <param name="block">Receives the block when the offset addresses a well-formed one.</param>
    /// <returns><see langword="false"/> when the offset or the header does not fit the file.</returns>
    /// <remarks>
    /// The header is self-describing, so a block that fits is almost certainly a real one: the declared
    /// triangle byte count has to equal four times the triangle count, and the three sections together
    /// have to land inside the file. Level 14 fails both tests on every one of its offsets.
    /// </remarks>
    public bool TryGetBlock(uint offset, out TerrainCommandBlock block)
    {
        block = default;

        if (offset > (uint)_data.Length || _data.Length - (int)offset < TerrainCommandBlock.HeaderSize)
        {
            return false;
        }

        int start = (int)offset;
        ReadOnlySpan<byte> header = _data.Span.Slice(start, TerrainCommandBlock.HeaderSize);
        int triangleCount = BinaryPrimitives.ReadUInt16LittleEndian(header);
        int triangleBytes = BinaryPrimitives.ReadInt16LittleEndian(header[2..]);
        int baseVertices = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
        int extraVertices = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);

        if (triangleBytes != triangleCount * 4)
        {
            return false;
        }

        int vertexBytes = (baseVertices + extraVertices) * 4;
        int total = TerrainCommandBlock.HeaderSize + triangleBytes + vertexBytes;
        if (total > _data.Length - start)
        {
            return false;
        }

        block = new TerrainCommandBlock(
            triangleCount,
            baseVertices,
            extraVertices,
            _data.Slice(start + TerrainCommandBlock.HeaderSize, triangleBytes),
            _data.Slice(start + TerrainCommandBlock.HeaderSize + triangleBytes, vertexBytes));
        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => $"terrain commands, {Length} bytes";
}
