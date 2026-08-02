using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenIGI.UI.Text;

/// <summary>
/// Persists a rasterised <see cref="FontAtlasData"/> to disk so the <c>stb_truetype</c> pass is paid
/// once rather than on every launch.
/// </summary>
/// <remarks>
/// <para>
/// The cache key is a hash of everything the atlas depends on — the font bytes, the pixel height, the
/// atlas kind, the SDF parameters and the exact code-point set — so a change to any of them produces a
/// different file and the old one is simply never read again. A format-version byte in the header does
/// the same job for changes to this serialiser itself.
/// </para>
/// <para>
/// The file is a small custom binary blob rather than, say, a PNG plus a JSON sidecar: the pixels are a
/// single channel that PNG would either bloat to RGBA or need a greyscale path for, and the metrics are
/// a flat table that serialises in a few lines. Load is defensive — a truncated or corrupt file is
/// treated as a miss and rebuilt, never thrown out of a constructor.
/// </para>
/// </remarks>
public static class FontAtlasCache
{
    private const uint Magic = 0x49474641; // "IGFA"
    private const byte FormatVersion = 1;

    /// <summary>Loads a cached atlas if one matches, otherwise builds it and caches the result.</summary>
    /// <param name="cacheDirectory">The directory cache files live in. Created if it does not exist.</param>
    /// <param name="fontData">The TrueType file's bytes.</param>
    /// <param name="pixelHeight">The base pixel height to rasterise at.</param>
    /// <param name="kind">Whether to produce a coverage or a signed-distance-field atlas.</param>
    /// <param name="codepoints">The code points to include, or <see langword="null"/> for printable ASCII.</param>
    /// <param name="atlasWidth">The atlas width in texels.</param>
    /// <param name="sdfPadding">The distance an SDF extends beyond a glyph, in texels.</param>
    /// <param name="sdfOnEdge">The texel value on a glyph edge for an SDF.</param>
    /// <returns>The atlas, loaded from cache when possible.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cacheDirectory"/> or <paramref name="fontData"/> is <see langword="null"/>.</exception>
    public static FontAtlasData LoadOrBuild(
        string cacheDirectory,
        byte[] fontData,
        float pixelHeight,
        FontAtlasKind kind = FontAtlasKind.Coverage,
        IReadOnlyList<int>? codepoints = null,
        int atlasWidth = 512,
        int sdfPadding = 4,
        byte sdfOnEdge = 128)
    {
        ArgumentNullException.ThrowIfNull(cacheDirectory);
        ArgumentNullException.ThrowIfNull(fontData);

        codepoints ??= FontAtlasBuilder.AsciiPrintable;
        string key = ComputeKey(fontData, pixelHeight, kind, codepoints, atlasWidth, sdfPadding, sdfOnEdge);
        string path = Path.Combine(cacheDirectory, $"font-{key}.atlas");

        if (TryLoad(path, out FontAtlasData? cached))
        {
            return cached;
        }

        FontAtlasData built = FontAtlasBuilder.Build(
            fontData, pixelHeight, kind, codepoints, atlasWidth, sdfPadding, sdfOnEdge);

        TrySave(path, built);
        return built;
    }

    /// <summary>Computes the cache key an atlas would be stored under.</summary>
    /// <param name="fontData">The TrueType file's bytes.</param>
    /// <param name="pixelHeight">The base pixel height.</param>
    /// <param name="kind">The atlas kind.</param>
    /// <param name="codepoints">The code-point set.</param>
    /// <param name="atlasWidth">The atlas width in texels.</param>
    /// <param name="sdfPadding">The SDF padding in texels.</param>
    /// <param name="sdfOnEdge">The SDF on-edge value.</param>
    /// <returns>A hex string identifying the atlas.</returns>
    public static string ComputeKey(
        byte[] fontData,
        float pixelHeight,
        FontAtlasKind kind,
        IReadOnlyList<int> codepoints,
        int atlasWidth,
        int sdfPadding,
        byte sdfOnEdge)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentNullException.ThrowIfNull(codepoints);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> scratch = stackalloc byte[8];

        hash.AppendData(fontData);

        BinaryPrimitives.WriteSingleLittleEndian(scratch, pixelHeight);
        hash.AppendData(scratch[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(scratch, (int)kind);
        hash.AppendData(scratch[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(scratch, atlasWidth);
        hash.AppendData(scratch[..4]);
        BinaryPrimitives.WriteInt32LittleEndian(scratch, sdfPadding);
        hash.AppendData(scratch[..4]);
        scratch[0] = sdfOnEdge;
        hash.AppendData(scratch[..1]);

        foreach (int codepoint in codepoints)
        {
            BinaryPrimitives.WriteInt32LittleEndian(scratch, codepoint);
            hash.AppendData(scratch[..4]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Reads a cached atlas from a file.</summary>
    /// <param name="path">The cache file's path.</param>
    /// <param name="atlas">Receives the atlas when the file is present and valid.</param>
    /// <returns><see langword="true"/> when the file was read; <see langword="false"/> on a miss or corruption.</returns>
    public static bool TryLoad(string path, out FontAtlasData atlas)
    {
        atlas = null!;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (reader.ReadUInt32() != Magic || reader.ReadByte() != FormatVersion)
            {
                return false;
            }

            var kind = (FontAtlasKind)reader.ReadInt32();
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            float basePixelHeight = reader.ReadSingle();
            float ascent = reader.ReadSingle();
            float descent = reader.ReadSingle();
            float lineGap = reader.ReadSingle();
            int sdfPadding = reader.ReadInt32();
            byte sdfOnEdge = reader.ReadByte();
            float sdfPixelDistScale = reader.ReadSingle();

            int glyphCount = reader.ReadInt32();
            if (width <= 0 || height <= 0 || glyphCount < 0)
            {
                return false;
            }

            var glyphs = new GlyphInfo[glyphCount];
            for (int i = 0; i < glyphCount; i++)
            {
                glyphs[i] = new GlyphInfo(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle());
            }

            byte[] pixels = reader.ReadBytes(width * height);
            if (pixels.Length != width * height)
            {
                return false;
            }

            atlas = new FontAtlasData(
                kind, width, height, pixels, basePixelHeight, ascent, descent, lineGap,
                glyphs, sdfPadding, sdfOnEdge, sdfPixelDistScale);
            return true;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException)
        {
            // A truncated or malformed cache file is a miss, not a fault: it is rebuilt and rewritten.
            return false;
        }
    }

    /// <summary>Writes an atlas to a cache file, ignoring write failures.</summary>
    /// <param name="path">The cache file's path.</param>
    /// <param name="atlas">The atlas to write.</param>
    /// <returns><see langword="true"/> when the file was written.</returns>
    public static bool TrySave(string path, FontAtlasData atlas)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(atlas);

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write((int)atlas.Kind);
            writer.Write(atlas.Width);
            writer.Write(atlas.Height);
            writer.Write(atlas.BasePixelHeight);
            writer.Write(atlas.Ascent);
            writer.Write(atlas.Descent);
            writer.Write(atlas.LineGap);
            writer.Write(atlas.SdfPadding);
            writer.Write(atlas.SdfOnEdge);
            writer.Write(atlas.SdfPixelDistScale);

            writer.Write(atlas.Glyphs.Count);
            foreach (GlyphInfo glyph in atlas.Glyphs)
            {
                writer.Write(glyph.Codepoint);
                writer.Write(glyph.AtlasX);
                writer.Write(glyph.AtlasY);
                writer.Write(glyph.Width);
                writer.Write(glyph.Height);
                writer.Write(glyph.OffsetX);
                writer.Write(glyph.OffsetY);
                writer.Write(glyph.Advance);
            }

            writer.Write(atlas.Pixels);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only install directory or a full disk must not stop the game starting; the atlas is
            // simply rebuilt next launch.
            return false;
        }
    }
}
