using System.Buffers.Binary;

namespace OpenIGI.Formats.World;

/// <summary>One chunk's block in <c>terrain.cmd</c>: a triangle strip, then the vertices it indexes.</summary>
/// <remarks>
/// <para>
/// The 8-byte header is <c>{u16 triangleCount, s16 triangleBytes, u16 baseVertices, u16 extraVertices}</c>
/// and the block's size derives from it exactly — validated over 67907 blocks and 748303 triangles across
/// thirteen levels, with no mismatches. <c>sub_510010</c> reaches the vertices by adding
/// <c>triangleBytes</c> to <c>block + 8</c>, which is what identifies the two trailing sections as the
/// vertex stream rather than the per-pass attribute blocks an earlier reading assumed.
/// </para>
/// <para>
/// The block is a view over the file's bytes; nothing is copied until <see cref="Vertex"/> or
/// <see cref="Triangle"/> is called.
/// </para>
/// </remarks>
public readonly struct TerrainCommandBlock
{
    /// <summary>The size of a block header in bytes.</summary>
    public const int HeaderSize = 8;

    private readonly ReadOnlyMemory<byte> _triangles;
    private readonly ReadOnlyMemory<byte> _vertices;

    internal TerrainCommandBlock(
        int triangleCount,
        int baseVertexCount,
        int extraVertexCount,
        ReadOnlyMemory<byte> triangles,
        ReadOnlyMemory<byte> vertices)
    {
        TriangleCount = triangleCount;
        BaseVertexCount = baseVertexCount;
        ExtraVertexCount = extraVertexCount;
        _triangles = triangles;
        _vertices = vertices;
    }

    /// <summary>Gets how many triangles the strip encodes.</summary>
    public int TriangleCount { get; }

    /// <summary>Gets how many vertices carry a position of their own.</summary>
    public int BaseVertexCount { get; }

    /// <summary>Gets how many vertices are interpolated from two others to close a level-of-detail seam.</summary>
    public int ExtraVertexCount { get; }

    /// <summary>Gets the total vertex count, which is what the triangle indices address.</summary>
    public int VertexCount => BaseVertexCount + ExtraVertexCount;

    /// <summary>Gets a value indicating whether the block has anything to draw.</summary>
    public bool IsEmpty => TriangleCount == 0 || VertexCount == 0;

    /// <summary>Reads one triangle of the strip.</summary>
    /// <param name="index">The triangle's position in the strip.</param>
    /// <returns>The triangle.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a triangle of this block.</exception>
    public TerrainTriangle Triangle(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, TriangleCount);
        return new TerrainTriangle(BinaryPrimitives.ReadUInt32LittleEndian(_triangles.Span[(index * 4)..]));
    }

    /// <summary>Reads one vertex.</summary>
    /// <param name="index">The vertex index, as the triangles address it.</param>
    /// <returns>The vertex.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a vertex of this block.</exception>
    public TerrainPackedVertex Vertex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, VertexCount);
        return new TerrainPackedVertex(BinaryPrimitives.ReadUInt32LittleEndian(_vertices.Span[(index * 4)..]));
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{TriangleCount} triangles, {BaseVertexCount}+{ExtraVertexCount} vertices";
}

/// <summary>One command of a chunk's triangle strip.</summary>
/// <remarks>
/// The strip is written as explicit index triples rather than as a running strip, and the winding
/// alternates from triangle to triangle within a run. <see cref="RestartsRun"/> marks the last triangle
/// of a run: the next one starts the winding over. The flag byte is only <c>0xE0</c> (continue) or
/// <c>0xF0</c> (restart) in retail data — 463876 and 284427 occurrences.
/// </remarks>
/// <param name="Packed">The raw command dword.</param>
public readonly record struct TerrainTriangle(uint Packed)
{
    /// <summary>The bit <c>sub_512C30</c> tests to end a run.</summary>
    public const uint RestartFlag = 0x10000000;

    /// <summary>Gets the first vertex index.</summary>
    public int I0 => (int)(Packed & 0xFF);

    /// <summary>Gets the second vertex index.</summary>
    public int I1 => (int)((Packed >> 8) & 0xFF);

    /// <summary>Gets the third vertex index.</summary>
    public int I2 => (int)((Packed >> 16) & 0xFF);

    /// <summary>Gets a value indicating whether this triangle ends its run.</summary>
    public bool RestartsRun => (Packed & RestartFlag) != 0;
}

/// <summary>One vertex of a chunk, packed into a single dword of 6-bit fields.</summary>
/// <remarks>
/// <para>
/// The three coordinates are offsets from the chunk's centre in units of a twenty-fourth of the chunk's
/// extent: <c>±24</c> spans the chunk's cube exactly, which is the check that pins the bias. No retail
/// vertex exceeds that range.
/// </para>
/// <para>
/// A vertex past <see cref="TerrainCommandBlock.BaseVertexCount"/> additionally carries the vertex it
/// morphs from and two edge selectors, in the bits the base vertices leave unused. That is the seam
/// morphing which lets two levels of detail meet without a crack.
/// </para>
/// </remarks>
/// <param name="Packed">The raw vertex dword.</param>
public readonly record struct TerrainPackedVertex(uint Packed)
{
    /// <summary>The coordinate value that corresponds to one chunk extent.</summary>
    public const int UnitsPerExtent = 24;

    /// <summary>Gets the X offset from the chunk centre, in twenty-fourths of its extent.</summary>
    public int X => (int)((Packed >> 26) & 0x3F) - UnitsPerExtent;

    /// <summary>Gets the Y offset from the chunk centre.</summary>
    public int Y => (int)((Packed >> 20) & 0x3F) - UnitsPerExtent;

    /// <summary>Gets the Z offset from the chunk centre.</summary>
    public int Z => (int)((Packed >> 14) & 0x3F) - UnitsPerExtent;

    /// <summary>Gets the vertex this one morphs from; meaningful only on an interpolated vertex.</summary>
    public int MorphSource => (int)(Packed & 0x3F);

    /// <summary>Gets the texture-projection axis selector for the base texture pass.</summary>
    /// <remarks>
    /// Read for <b>every</b> vertex, not only the interpolated ones — <c>sub_512C30</c> tests these two
    /// bits per vertex when it builds the pass's texture coordinates. Zero projects the texture across
    /// the ground (U from X, V from Y); any other value marks a steep face, whose V runs down the
    /// vertical instead (V from Z) and whose U follows X or Y by the selector's low bit, flipped when
    /// the chunk's orientation swaps the axes.
    /// </remarks>
    public int BaseAxis => (int)((Packed >> 6) & 3);

    /// <summary>Gets the texture-projection axis selector for the detail texture pass.</summary>
    /// <remarks>Bits 8–9, read the same way as <see cref="BaseAxis"/> for the double-density pass.</remarks>
    public int DetailAxis => (int)((Packed >> 8) & 3);
}
