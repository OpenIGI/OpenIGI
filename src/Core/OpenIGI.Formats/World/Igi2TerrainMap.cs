using System.Buffers.Binary;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.World;

/// <summary>Which of the three per-tile terrain maps a file carries.</summary>
/// <remarks>
/// The numbers are the <c>eFormat</c> word the writer stamps at offset 32, read from
/// <c>TerrainMap_SaveHandler</c> (<c>igi2ed 0x630B60</c>): it writes <c>2</c> for the <c>.thm</c>,
/// <c>0</c> for the <c>.tmm</c> and <c>3</c> for the <c>.tlm</c>, and each of the three loaders in
/// <c>igi2.exe</c> refuses a file whose word is not its own.
/// </remarks>
public enum Igi2TerrainMapKind
{
    /// <summary>A material map, <c>.tmm</c> — one byte per sample.</summary>
    Material = 0,

    /// <summary>A height map, <c>.thm</c> — one <see langword="float"/> per sample.</summary>
    Height = 2,

    /// <summary>A terrain lightmap, <c>.tlm</c> — one <see cref="Bgra32"/> per sample.</summary>
    Light = 3,
}

/// <summary>
/// One tile of I.G.I. 2 terrain data: a <c>heightmap%03d.thm</c>, <c>.tmm</c> or <c>.tlm</c>.
/// </summary>
/// <remarks>
/// <para>
/// The three files are a set. <c>TerrainMap_Alloc</c> (<c>igi2.exe 0x575030</c>) loads all three by the
/// same tile id, from <c>heightmaps/heightmap%03d.<i>ext</i></c>, and substitutes a generated buffer for
/// any one that is missing — so a tile with no <c>.tlm</c> is complete, not broken. All three normally
/// arrive inside the ILFF archive <c>heightmaps/heightmaps.res</c> rather than as files on disk; the
/// editor writes them loose and then bundles them, which is what <c>TerrainMap_SaveHandler</c> emits its
/// temporary <c>BeginResource</c>/<c>AddResource</c> script for.
/// </para>
/// <para>
/// Each file is a fixed header followed by <c>nWidth * nHeight</c> samples in row-major order, with no
/// padding and no per-row alignment. The engine takes the payload by pointer and uses it in place —
/// <c>pvHeightmap</c> is <c>float *</c> at byte 52, <c>piMaterialmap</c> is <c>unsigned char *</c> at
/// byte 44 and <c>pbLightmap</c> is <c>unsigned int *</c> at byte 44 — which is where both the header
/// sizes and the sample strides below come from. Nothing is decoded, scaled or swizzled on the way in.
/// </para>
/// <para>
/// This is a different format from I.G.I. 1 terrain rather than a later version of it, so it is a
/// separate reader; see <see cref="TerrainHeightmap"/> and <see cref="TerrainLightmaps"/> for the first
/// game, whose grids are id-indexed records inside one file.
/// </para>
/// </remarks>
public sealed class Igi2TerrainMap
{
    /// <summary>The size of a <c>.thm</c> header, in bytes.</summary>
    /// <remarks>
    /// <c>TerrainMapTHMHeader_t</c>: <c>vVersion</c>, a 28-byte <c>QCalendar_s</c> stamp, <c>eFormat</c>,
    /// <c>vOffset</c>, <c>vScale</c>, <c>nWidth</c>, <c>nHeight</c>. The writer passes 52 to
    /// <c>File_Write</c> literally.
    /// </remarks>
    public const int HeightHeaderSize = 52;

    /// <summary>The size of a <c>.tmm</c> or <c>.tlm</c> header, in bytes.</summary>
    /// <remarks>
    /// <c>TerrainMapTMMHeader_t</c> and <c>TerrainMapTLMHeader_t</c> are the same shape as the height
    /// header without its <c>vOffset</c> and <c>vScale</c> pair.
    /// </remarks>
    public const int CommonHeaderSize = 44;

    /// <summary>The only version any of the three loaders accepts.</summary>
    /// <remarks>
    /// Stored as a <see langword="float"/>, and compared as the raw word <c>1008981770</c> because that
    /// is how the original compares it — an integer test against the constant it wrote, not a floating
    /// point one. Reading the bits rather than the value also keeps the check exact.
    /// </remarks>
    public const uint VersionWord = 1008981770;

    private readonly ReadOnlyMemory<byte> _samples;

    private Igi2TerrainMap(
        Igi2TerrainMapKind kind,
        int width,
        int height,
        float offset,
        float scale,
        ReadOnlyMemory<byte> samples,
        ReadOnlyMemory<byte> trailing)
    {
        Kind = kind;
        Width = width;
        Height = height;
        Offset = offset;
        Scale = scale;
        _samples = samples;
        Trailing = trailing;
    }

    /// <summary>Gets which of the three maps this is.</summary>
    public Igi2TerrainMapKind Kind { get; }

    /// <summary>Gets the number of samples along a row.</summary>
    public int Width { get; }

    /// <summary>Gets the number of rows.</summary>
    public int Height { get; }

    /// <summary>Gets the header's <c>vOffset</c>; zero for a map that is not a height map.</summary>
    /// <remarks>
    /// Carried because the header carries it, not because it means anything at run time: the editor
    /// writes a literal <c>0</c> here and a literal <c>1</c> to <see cref="Scale"/> on every save, and
    /// the engine never reads either — it hands the payload straight on as world-space heights. All 50
    /// height maps in the retail tree carry exactly that pair. It belongs to the digital elevation model
    /// import path (<c>TerrainMap_LoadDEM</c>, <c>igi2ed 0x62FE80</c>), which normalises before it
    /// writes.
    /// </remarks>
    public float Offset { get; }

    /// <summary>Gets the header's <c>vScale</c>; zero for a map that is not a height map.</summary>
    /// <remarks>See <see cref="Offset"/> — recorded, never applied.</remarks>
    public float Scale { get; }

    /// <summary>Gets the sample payload, row-major, <see cref="SampleSize"/> bytes per sample.</summary>
    public ReadOnlyMemory<byte> Samples => _samples;

    /// <summary>Gets whatever follows the declared extent; empty for all but two retail files.</summary>
    /// <remarks>
    /// <para>
    /// The original reads exactly <c>nWidth * nHeight</c> samples and never checks the file length, so a
    /// longer file loads and its tail is simply never addressed. Two shipped height maps are longer:
    /// <c>location3/LEVEL4</c>'s <c>heightmap000.thm</c> and <c>heightmap001.thm</c> each carry 87,380
    /// bytes past their 256×256 base — <c>128² + 64² + … + 1²</c> samples, a complete mip pyramid.
    /// Comparing the first of those levels against a two-by-two box average of the base agrees to a mean
    /// absolute error of 3.8 over a value spread of 3.8 million, so it is a box-filtered mip chain and
    /// not coincidence.
    /// </para>
    /// <para>
    /// It is exposed rather than dropped so that the bytes are visibly unused instead of invisibly
    /// discarded, but nothing reads it: the engine builds its own min and max height mip maps at run time
    /// into separate allocations (<c>TerrainMap_AllocateMinMaxHeightMipMaps</c>,
    /// <c>igi2ed 0x6296B0</c>), eight bytes per sample rather than four, so this tail is not what feeds
    /// them.
    /// </para>
    /// </remarks>
    public ReadOnlyMemory<byte> Trailing { get; }

    /// <summary>Gets how many bytes one sample of this map occupies.</summary>
    public int SampleSize => SampleSizeOf(Kind);

    /// <summary>Gets how many samples the map holds.</summary>
    public int SampleCount => Width * Height;

    /// <summary>Gets how many bytes one map of the given kind spends per sample.</summary>
    /// <param name="kind">The map kind.</param>
    /// <returns>The stride in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a known kind.</exception>
    public static int SampleSizeOf(Igi2TerrainMapKind kind) => kind switch
    {
        Igi2TerrainMapKind.Material => 1,
        Igi2TerrainMapKind.Height => 4,
        Igi2TerrainMapKind.Light => Bgra32.Size,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Gets how many bytes of header a map of the given kind carries.</summary>
    /// <param name="kind">The map kind.</param>
    /// <returns>The header size in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a known kind.</exception>
    public static int HeaderSizeOf(Igi2TerrainMapKind kind) => kind switch
    {
        Igi2TerrainMapKind.Height => HeightHeaderSize,
        Igi2TerrainMapKind.Material or Igi2TerrainMapKind.Light => CommonHeaderSize,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Reads a terrain map, taking its kind from the header.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The map.</returns>
    /// <exception cref="PackFormatException">
    /// The bytes are too short, carry an unknown version or kind, declare a non-positive extent, or hold
    /// fewer samples than the extent calls for.
    /// </exception>
    /// <remarks>
    /// The kind is read rather than demanded because the file says which it is and all three headers
    /// agree on their first 36 bytes. A caller that knows what it asked the file system for should still
    /// say so, with <see cref="Read(ReadOnlyMemory{byte}, Igi2TerrainMapKind)"/>, which is what the
    /// original does: each of its three loaders rejects the other two kinds outright.
    /// </remarks>
    public static Igi2TerrainMap Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < CommonHeaderSize)
        {
            throw new PackFormatException(
                $"A terrain map is {span.Length} bytes, too short for a {CommonHeaderSize}-byte header.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(span);
        if (version != VersionWord)
        {
            throw new PackFormatException(
                $"A terrain map declares version word 0x{version:X8}, not 0x{VersionWord:X8}.");
        }

        int format = BinaryPrimitives.ReadInt32LittleEndian(span[32..]);
        if (!Enum.IsDefined(typeof(Igi2TerrainMapKind), format))
        {
            throw new PackFormatException($"A terrain map declares unknown format {format}.");
        }

        Igi2TerrainMapKind kind = (Igi2TerrainMapKind)format;
        int headerSize = HeaderSizeOf(kind);
        if (span.Length < headerSize)
        {
            throw new PackFormatException(
                $"A {kind} terrain map is {span.Length} bytes, too short for its {headerSize}-byte header.");
        }

        float offset = 0f;
        float scale = 0f;
        int extents = CommonHeaderSize - 8;
        if (kind == Igi2TerrainMapKind.Height)
        {
            offset = BinaryPrimitives.ReadSingleLittleEndian(span[36..]);
            scale = BinaryPrimitives.ReadSingleLittleEndian(span[40..]);
            extents = HeightHeaderSize - 8;
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(span[extents..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(span[(extents + 4)..]);
        if (width <= 0 || height <= 0)
        {
            throw new PackFormatException($"A {kind} terrain map declares an extent of {width}x{height}.");
        }

        // Short is fatal and long is not, because that is the original's own tolerance: it reads exactly
        // nWidth * nHeight samples through a pointer into the file and never consults the length, so a
        // long file works and a short one walks off the end. See Trailing for the two that are long.
        long expected = (long)width * height * SampleSizeOf(kind);
        long actual = span.Length - headerSize;
        if (actual < expected)
        {
            throw new PackFormatException(
                $"A {width}x{height} {kind} terrain map carries {actual} sample bytes, needs {expected}.");
        }

        int payload = headerSize + (int)expected;
        return new Igi2TerrainMap(kind, width, height, offset, scale, data[headerSize..payload], data[payload..]);
    }

    /// <summary>Reads a terrain map and requires it to be of a particular kind.</summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="kind">The kind the caller asked the file system for.</param>
    /// <returns>The map.</returns>
    /// <exception cref="PackFormatException">The bytes are malformed, or carry a different kind.</exception>
    public static Igi2TerrainMap Read(ReadOnlyMemory<byte> data, Igi2TerrainMapKind kind)
    {
        Igi2TerrainMap map = Read(data);
        if (map.Kind != kind)
        {
            throw new PackFormatException($"A terrain map is a {map.Kind} map, not a {kind} map.");
        }

        return map;
    }

    /// <summary>Gets one height sample, in world units.</summary>
    /// <param name="x">The column, from zero.</param>
    /// <param name="y">The row, from zero.</param>
    /// <returns>The height.</returns>
    /// <exception cref="InvalidOperationException">This is not a height map.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates fall outside the map.</exception>
    /// <remarks>
    /// The value is used exactly as stored: <c>TerrainMap_Alloc</c> points a <c>float *</c> at the
    /// payload, and neither <see cref="Offset"/> nor <see cref="Scale"/> is applied anywhere.
    /// </remarks>
    public float GetHeight(int x, int y) =>
        BinaryPrimitives.ReadSingleLittleEndian(SampleAt(x, y, Igi2TerrainMapKind.Height));

    /// <summary>Gets one lightmap texel.</summary>
    /// <param name="x">The column, from zero.</param>
    /// <param name="y">The row, from zero.</param>
    /// <returns>The texel.</returns>
    /// <exception cref="InvalidOperationException">This is not a lightmap.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates fall outside the map.</exception>
    /// <remarks>
    /// The channel order is settled, not assumed. <c>TerrainMap_CalculateLightmaps</c>
    /// (<c>igi2ed 0x62BFE0</c>) composes each texel as
    /// <c>(0xFF &lt;&lt; 24) | (red &lt;&lt; 16) | (green &lt;&lt; 8) | blue</c> and stores the dword, so
    /// on disk the bytes run blue, green, red, alpha — the engine's own <see cref="Bgra32"/> layout. The
    /// fallback buffer for a missing <c>.tlm</c> is <c>memset 0xFF</c>, which is opaque white in the same
    /// layout.
    /// </remarks>
    public Bgra32 GetLight(int x, int y)
    {
        ReadOnlySpan<byte> texel = SampleAt(x, y, Igi2TerrainMapKind.Light);
        return new Bgra32(texel[0], texel[1], texel[2], texel[3]);
    }

    /// <summary>Gets one material index.</summary>
    /// <param name="x">The column, from zero.</param>
    /// <param name="y">The row, from zero.</param>
    /// <returns>The index, selecting a <c>TerrainMaterial</c> the level declares.</returns>
    /// <exception cref="InvalidOperationException">This is not a material map.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The coordinates fall outside the map.</exception>
    /// <remarks>The fallback buffer for a missing <c>.tmm</c> is zeroed, so index zero is the default.</remarks>
    public byte GetMaterial(int x, int y) => SampleAt(x, y, Igi2TerrainMapKind.Material)[0];

    /// <inheritdoc/>
    public override string ToString() => $"{Kind} terrain map {Width}x{Height}";

    private ReadOnlySpan<byte> SampleAt(int x, int y, Igi2TerrainMapKind expected)
    {
        if (Kind != expected)
        {
            throw new InvalidOperationException($"This is a {Kind} terrain map, not a {expected} map.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        int size = SampleSizeOf(Kind);
        return _samples.Span.Slice(((y * Width) + x) * size, size);
    }
}
