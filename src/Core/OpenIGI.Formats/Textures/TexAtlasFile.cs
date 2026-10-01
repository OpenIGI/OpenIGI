using System.Buffers.Binary;
using System.Collections.ObjectModel;
using OpenIGI.Core;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// A multi-image <c>"LOOP"</c> container — versions 7 and 9 — which stores a set of equally sized tiles
/// rather than one texture with a mip chain.
/// </summary>
/// <remarks>
/// <para>
/// A level's <c>terrain/terrain.tex</c> is one of these: eighteen 128×128 tiles that the terrain's tile
/// maps index. <see cref="TexFile"/> deliberately refuses these versions, because a caller asking for
/// "the texture" of an atlas has no sensible answer; this reads the whole set.
/// </para>
/// <para>
/// The 52-byte header is shared by both versions and the layer descriptors follow it directly. Version 7
/// gives every layer the file's own pixel mode and uses 40-byte descriptors; version 9 gives each layer
/// its own mode and uses 32-byte ones. Retail terrain atlases are all version 9, mode
/// <see cref="TextureFormatCode.A1R5G5B5"/>.
/// </para>
/// <para>
/// The layer's mode is the sixth argument the original passes to its texture-create helper
/// (<c>0x4B23B0</c>), which is the same position the <c>.lmp</c> path passes
/// <see cref="TextureFormatCode.Palette8"/> in — that is what identifies it as the format code rather
/// than the file-level word at <c>+0x12</c>, which travels alongside as the secondary format.
/// </para>
/// </remarks>
public sealed class TexAtlasFile
{
    /// <summary>The size of the header both multi-image versions share.</summary>
    public const int HeaderSize = 52;

    /// <summary>The multi-image version whose layers share the file's pixel mode.</summary>
    public const uint Version7 = 7;

    /// <summary>The multi-image version whose layers each carry their own pixel mode.</summary>
    public const uint Version9 = 9;

    /// <summary>The size of a version 7 layer descriptor.</summary>
    public const int Version7LayerSize = 40;

    /// <summary>The size of a version 9 layer descriptor.</summary>
    public const int Version9LayerSize = 32;

    private TexAtlasFile(uint version, int width, int height, IReadOnlyList<TextureImage> tiles)
    {
        Version = version;
        TileWidth = width;
        TileHeight = height;
        Tiles = tiles;
    }

    /// <summary>Gets the container version, <see cref="Version7"/> or <see cref="Version9"/>.</summary>
    public uint Version { get; }

    /// <summary>Gets the width the header declares for every tile.</summary>
    public int TileWidth { get; }

    /// <summary>Gets the height the header declares for every tile.</summary>
    public int TileHeight { get; }

    /// <summary>Gets the tiles, in file order; the index is what a tile map holds.</summary>
    public IReadOnlyList<TextureImage> Tiles { get; }

    /// <summary>Determines whether a <c>"LOOP"</c> asset is one of the multi-image versions.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns><see langword="true"/> for a version 7 or version 9 asset.</returns>
    public static bool IsAtlas(ReadOnlySpan<byte> data)
    {
        if (!TexFile.IsLoopAsset(data))
        {
            return false;
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);

        // As in TexFile: version 12 is the pack's own container, accepted so a replacement ground atlas
        // arrives through exactly the path the level's own one does.
        return version is Version7 or Version9 || PackPicture.Is(data);
    }

    /// <summary>Reads every tile of an atlas.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The atlas.</returns>
    /// <exception cref="TextureFormatException">The file is not a multi-image container, or a layer does not fit.</exception>
    public static TexAtlasFile Read(ReadOnlyMemory<byte> data)
    {
        if (PackPicture.Is(data.Span))
        {
            PackPicture.Content content = PackPicture.Read(data);
            TextureImage[] pictures = [.. content.Frames.Select(frame => frame.Image)];

            return new TexAtlasFile(
                PackPicture.Version,
                pictures[0].Width,
                pictures[0].Height,
                new ReadOnlyCollection<TextureImage>(pictures));
        }

        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < HeaderSize || FourCc.FromBytes(span) != TexFile.Magic)
        {
            throw new TextureFormatException("A texture atlas must start with the 'LOOP' magic.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
        if (version is not (Version7 or Version9))
        {
            throw new TextureFormatException($"Texture atlas version {version} is not a multi-image container.");
        }

        ushort secondaryFormat = BinaryPrimitives.ReadUInt16LittleEndian(span[0x12..]);
        int layerCount = ReadCount(span[0x20..], "layer count");
        int width = ReadCount(span[0x28..], "tile width");
        int height = ReadCount(span[0x2C..], "tile height");
        uint sharedFormat = BinaryPrimitives.ReadUInt32LittleEndian(span[0x30..]);

        int stride = version == Version7 ? Version7LayerSize : Version9LayerSize;
        long descriptorBytes = (long)stride * layerCount;
        if (descriptorBytes > span.Length - HeaderSize)
        {
            throw new TextureFormatException(
                $"A {layerCount}-layer atlas needs {descriptorBytes} descriptor bytes, which the file does not hold.");
        }

        TextureImage[] tiles = new TextureImage[layerCount];
        for (int i = 0; i < layerCount; i++)
        {
            ReadOnlySpan<byte> layer = span.Slice(HeaderSize + (i * stride), stride);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(layer);

            // Version 7 layers hold their pitch and size as dwords, version 9 as words after a mode.
            (uint format, int pitch, int layerWidth, int layerHeight) = version == Version7
                ? (sharedFormat,
                    ReadCount(layer[4..], "layer pitch"),
                    ReadCount(layer[8..], "layer width"),
                    ReadCount(layer[12..], "layer height"))
                : (BinaryPrimitives.ReadUInt32LittleEndian(layer[4..]),
                    (int)BinaryPrimitives.ReadUInt16LittleEndian(layer[8..]),
                    (int)BinaryPrimitives.ReadUInt16LittleEndian(layer[10..]),
                    (int)BinaryPrimitives.ReadUInt16LittleEndian(layer[12..]));

            TextureDescriptor descriptor = TextureDescriptor.Create(
                format,
                layerWidth,
                layerHeight,
                pitch,
                secondaryFormat);

            int payload = descriptor.PayloadSize;
            if (offset > (uint)span.Length || payload > span.Length - (int)offset)
            {
                throw new TextureFormatException(
                    $"Layer {i} needs {payload} bytes at offset {offset}, which the {span.Length}-byte file does not hold.");
            }

            tiles[i] = TextureDecoder.Decode(descriptor, span.Slice((int)offset, payload), ReadOnlySpan<Bgra32>.Empty);
        }

        return new TexAtlasFile(version, width, height, new ReadOnlyCollection<TextureImage>(tiles));
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"TEX{Version} atlas: {Tiles.Count} tile(s) of {TileWidth}x{TileHeight}";

    private static int ReadCount(ReadOnlySpan<byte> source, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        if (value > int.MaxValue)
        {
            throw new TextureFormatException($"The {what} is {value}, too large to address.");
        }

        return (int)value;
    }
}
