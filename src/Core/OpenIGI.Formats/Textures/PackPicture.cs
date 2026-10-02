using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Core.Graphics;

namespace OpenIGI.Formats.Textures;

/// <summary>
/// The one container a generated pack writes: <c>"LOOP"</c> version 12.
/// </summary>
/// <remarks>
/// <para>
/// Every picture in this game arrives in one of four shapes — a version 2 or 11 single texture, a
/// version 7 or 9 multi-frame atlas — read by three different loaders that disagree about where the
/// format code lives. Replacement art could be written back in each of those shapes, and the first plan
/// was to do exactly that. This is better, for three reasons that only appeared once the whole corpus
/// was in view.
/// </para>
/// <para>
/// <b>Size.</b> An upscale is BGRA8 at four times the width and height: 32 times the bytes of the
/// A1R5G5B5 it replaces, which turns 120 MB of art into about 3 GB. The retail containers store raw
/// rows and have nowhere to say otherwise. Here a frame is a PNG, which on this material is between
/// three and five times smaller and costs a decode that the loader was already doing.
/// </para>
/// <para>
/// <b>Layout must not follow resolution.</b> The menu sizes the logo as
/// <c>grid.Columns * Frames[0].Width</c> — the art's pixel count IS the layout — so a four-times
/// picture would draw four times too big, and nothing in the retail formats can say otherwise because
/// nothing needed to. So this records a LOGICAL size beside the real one, the loader reports the
/// logical size as the frame's own, and a sharper menu is still a menu the same shape.
/// </para>
/// <para>
/// <b>One writer instead of four.</b> A sprite is not just pixels: it carries frame advances, offsets,
/// a palette block and the tile grid that reassembles a 640x480 picture out of 150 frames of 64x32.
/// Rewriting one in place means moving every block and fixing every pointer. Writing this instead means
/// one layout to get right and one to test.
/// </para>
/// <para>
/// It is only ever written by the packer and only ever read from an overrides folder. Nothing in a
/// retail installation is version 12, so a game with no pack never meets one.
/// </para>
/// <para>
/// THE LAYOUT. Little-endian throughout.
/// </para>
/// <code>
/// header, 48 bytes
///   0x00  'LOOP'
///   0x04  version, 12
///   0x08  frame count, u32
///   0x0C  flags, u32                 bit 0: frames are PNG. bit 1: build a mip chain on load.
///                                    bit 2: frames are block-compressed chains (see below)
///   0x10  logical width, u32         what a caller lays out with, whatever the pixels turned out to be
///   0x14  logical height, u32
///   0x18  grid columns, u32          0 when the picture is not a tile grid
///   0x1C  grid rows, u32
///   0x20  grid cell offset, u32      columns*rows bytes of one-based frame numbers
///   0x24  three reserved dwords
///
/// frame table, 32 bytes each, immediately after the header
///   0x00  picture offset, u32        from the start of the file
///   0x04  picture length, u32
///   0x08  surface offset, u32        0 when the frame carries no derived map
///   0x0C  surface length, u32
///   0x10  logical width, u16         NOT the pixel width; what layout uses
///   0x12  logical height, u16
///   0x14  advance, u16
///   0x16  offset x, i16
///   0x18  offset y, i16
///   0x1A  reserved, u16 then u32
/// </code>
/// <para>
/// A BLOCK-COMPRESSED FRAME (bit 2, with bit 0 clear) holds a GPU-ready chain instead of a PNG. Its
/// picture payload is a 32-byte header -- 'BCN1', format, width, height, levels, the offset and length
/// of a small PNG proxy within the payload, and per-frame flags (bit 0: cut-out) -- then every level's
/// blocks, largest first. The surface payload is the same shape with no proxy. Nothing is built at load:
/// the chain was built, its cut-out widened and its coverage preserved when the pack was made.
/// </para>
/// <para>
/// THE SURFACE MAP IS ITS OWN SLOT, not a second frame, and that distinction is load-bearing. The
/// arrangement it replaces put the picture in layer 0 and its packed normal-roughness-occlusion map in
/// layer 1 of a multi-image container — which works only for as long as nothing else uses layer 1 for
/// something else. A level's ground atlas is eighteen tiles in eighteen layers, and a sprite is its
/// animation frames, so under that rule the second ground tile and the second frame of an explosion both
/// read as somebody's normal map. Here a frame is always a picture and a map is always a map.
/// </para>
/// </remarks>
public static class PackPicture
{
    /// <summary>The container version a pack writes.</summary>
    public const uint Version = 12;

    /// <summary>The size of the file header in bytes.</summary>
    public const int HeaderSize = 48;

    /// <summary>The size of one frame record in bytes.</summary>
    public const int FrameSize = 32;

    /// <summary>Set when every frame payload is a PNG rather than raw BGRA8 rows.</summary>
    public const uint PngFlag = 1;

    /// <summary>Set when the loader should build a mip chain, because the file does not carry one.</summary>
    /// <remarks>
    /// The retail version 11 textures store their chain, and things downstream rely on having one. Mips
    /// are a third again in size and are pure arithmetic over pixels that are already here, so this asks
    /// for them to be built at load rather than paying for them on disk.
    /// </remarks>
    public const uint MipChainFlag = 2;

    /// <summary>Set when every frame is a stored block-compressed chain rather than a PNG.</summary>
    /// <remarks>
    /// Written without <see cref="PngFlag"/>, so a build that predates it refuses the file outright
    /// rather than reading block data as a PNG.
    /// </remarks>
    public const uint BlockCompressedFlag = 4;

    /// <summary>The magic at the start of a block-compressed payload.</summary>
    private const uint BlockMagic = 0x314E4342; // 'BCN1'

    /// <summary>The size of a block-compressed payload's header.</summary>
    private const int BlockHeaderSize = 32;

    /// <summary>One picture going into, or coming out of, a pack container.</summary>
    /// <param name="Image">The pixels, at whatever resolution they ended up.</param>
    /// <param name="LogicalWidth">The width a caller should lay out with.</param>
    /// <param name="LogicalHeight">The height a caller should lay out with.</param>
    /// <param name="Advance">The sprite advance, in logical units; zero for anything that is not a glyph.</param>
    /// <param name="OffsetX">The sprite's draw offset, in logical units.</param>
    /// <param name="OffsetY">The sprite's draw offset, in logical units.</param>
    /// <param name="Surface">
    /// The packed normal, roughness and occlusion map derived for this picture, or <see langword="null"/>
    /// when none was. Derived data, never the game's: nothing shipped with a map.
    /// </param>
    /// <param name="Compressed">
    /// The picture as a stored block-compressed chain, or <see langword="null"/> for a PNG frame. When set,
    /// <see cref="Image"/> is only its small proxy.
    /// </param>
    /// <param name="CompressedSurface">The surface map as a stored block-compressed chain, or <see langword="null"/>.</param>
    public readonly record struct Frame(
        TextureImage Image,
        int LogicalWidth,
        int LogicalHeight,
        int Advance = 0,
        int OffsetX = 0,
        int OffsetY = 0,
        TextureImage? Surface = null,
        CompressedTextureImage? Compressed = null,
        CompressedTextureImage? CompressedSurface = null);

    /// <summary>Everything a pack container holds.</summary>
    /// <param name="Frames">The pictures, in the order anything that indexes them expects.</param>
    /// <param name="LogicalWidth">The whole picture's width in layout units.</param>
    /// <param name="LogicalHeight">The whole picture's height in layout units.</param>
    /// <param name="GridColumns">The tile grid's width, or zero when the picture is not tiled.</param>
    /// <param name="GridRows">The tile grid's height, or zero when the picture is not tiled.</param>
    /// <param name="GridCells">One-based frame numbers, row-major; zero means an empty cell.</param>
    public readonly record struct Content(
        IReadOnlyList<Frame> Frames,
        int LogicalWidth,
        int LogicalHeight,
        int GridColumns,
        int GridRows,
        ReadOnlyMemory<byte> GridCells);

    /// <summary>Determines whether these bytes are a pack container.</summary>
    /// <param name="data">The bytes to test.</param>
    /// <returns><see langword="true"/> for a version 12 <c>"LOOP"</c> asset.</returns>
    public static bool Is(ReadOnlySpan<byte> data) =>
        TexFile.IsLoopAsset(data)
        && data.Length >= HeaderSize
        && BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) == Version;

    /// <summary>Encodes a pack container.</summary>
    /// <param name="content">What to write.</param>
    /// <param name="buildMips">Whether the loader should build a mip chain for each frame.</param>
    /// <returns>The complete file bytes.</returns>
    /// <exception cref="TextureFormatException">There are no frames, or the grid does not match them.</exception>
    public static byte[] Write(in Content content, bool buildMips = true)
    {
        IReadOnlyList<Frame> frames = content.Frames
            ?? throw new TextureFormatException("A pack picture needs frames.");

        if (frames.Count == 0)
        {
            throw new TextureFormatException("A pack picture needs at least one frame.");
        }

        int cells = content.GridColumns * content.GridRows;

        if (cells != content.GridCells.Length)
        {
            throw new TextureFormatException(
                $"A {content.GridColumns}x{content.GridRows} grid needs {cells} cells, got {content.GridCells.Length}.");
        }

        byte[][] payloads = new byte[frames.Count][];
        byte[]?[] surfaces = new byte[frames.Count][];

        // All or nothing: the flag is the file's, not a frame's.
        bool compressed = frames[0].Compressed is not null;
        if (frames.Any(frame => frame.Compressed is not null != compressed))
        {
            throw new TextureFormatException("A pack picture's frames must be all block-compressed or all PNG.");
        }

        for (int i = 0; i < frames.Count; i++)
        {
            if (compressed)
            {
                payloads[i] = EncodeBlocks(frames[i].Compressed!);
                surfaces[i] = frames[i].CompressedSurface is { } blockSurface ? EncodeBlocks(blockSurface) : null;
                continue;
            }

            TextureImage image = frames[i].Image
                ?? throw new TextureFormatException($"Frame {i} has no image.");

            payloads[i] = Encode(image);

            if (frames[i].Surface is { } surface)
            {
                surfaces[i] = Encode(surface);
            }
        }

        int table = HeaderSize + (FrameSize * frames.Count);
        int body = table + cells;
        long total = body
            + payloads.Sum(payload => (long)payload.Length)
            + surfaces.Sum(surface => (long)(surface?.Length ?? 0));

        if (total > int.MaxValue)
        {
            throw new TextureFormatException($"A pack picture of {total} bytes cannot be addressed by its own offsets.");
        }

        byte[] file = new byte[total];
        Span<byte> span = file;

        uint flags = compressed ? BlockCompressedFlag : PngFlag | (buildMips ? MipChainFlag : 0);

        TexFile.Magic.WriteTo(span);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)frames.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], flags);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)content.LogicalWidth);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)content.LogicalHeight);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)content.GridColumns);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], (uint)content.GridRows);
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], cells == 0 ? 0u : (uint)table);

        content.GridCells.Span.CopyTo(span[table..]);

        int at = body;

        for (int i = 0; i < frames.Count; i++)
        {
            Frame frame = frames[i];
            Span<byte> record = span.Slice(HeaderSize + (i * FrameSize), FrameSize);

            BinaryPrimitives.WriteUInt32LittleEndian(record, (uint)at);
            BinaryPrimitives.WriteUInt32LittleEndian(record[4..], (uint)payloads[i].Length);
            BinaryPrimitives.WriteUInt16LittleEndian(record[16..], Checked(frame.LogicalWidth, "logical width"));
            BinaryPrimitives.WriteUInt16LittleEndian(record[18..], Checked(frame.LogicalHeight, "logical height"));
            BinaryPrimitives.WriteUInt16LittleEndian(record[20..], Checked(frame.Advance, "advance"));
            BinaryPrimitives.WriteInt16LittleEndian(record[22..], (short)frame.OffsetX);
            BinaryPrimitives.WriteInt16LittleEndian(record[24..], (short)frame.OffsetY);

            payloads[i].CopyTo(span[at..]);
            at += payloads[i].Length;

            if (surfaces[i] is { } surface)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(record[8..], (uint)at);
                BinaryPrimitives.WriteUInt32LittleEndian(record[12..], (uint)surface.Length);
                surface.CopyTo(span[at..]);
                at += surface.Length;
            }
        }

        return file;
    }

    /// <summary>Decodes a pack container.</summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>Its frames and the layout information that travels with them.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a usable version 12 container.</exception>
    public static Content Read(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;

        if (!Is(span))
        {
            throw new TextureFormatException($"Not a version {Version} pack picture.");
        }

        int count = ReadBounded(span[8..], "frame count");
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
        int logicalWidth = ReadBounded(span[16..], "logical width");
        int logicalHeight = ReadBounded(span[20..], "logical height");
        int columns = ReadBounded(span[24..], "grid columns");
        int rows = ReadBounded(span[28..], "grid rows");
        int cellOffset = ReadBounded(span[32..], "grid cell offset");

        if (count == 0)
        {
            throw new TextureFormatException("A pack picture holds no frames.");
        }

        long table = (long)HeaderSize + ((long)count * FrameSize);

        if (table > span.Length)
        {
            throw new TextureFormatException(
                $"A {count}-frame pack picture needs {table} header bytes, which the {span.Length}-byte file does not hold.");
        }

        long cells = (long)columns * rows;

        if (cells > 0 && (cellOffset <= 0 || cellOffset + cells > span.Length))
        {
            throw new TextureFormatException(
                $"A {columns}x{rows} grid at offset {cellOffset} does not fit in {span.Length} bytes.");
        }

        Frame[] frames = new Frame[count];
        bool mips = (flags & MipChainFlag) != 0;

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> record = span.Slice(HeaderSize + (i * FrameSize), FrameSize);
            frames[i] = (flags & BlockCompressedFlag) != 0
                ? ReadCompressedFrame(data, record, i)
                : ReadFrame(span, record, i, mips, flags);
        }

        return new Content(
            frames,
            logicalWidth,
            logicalHeight,
            columns,
            rows,
            cells == 0 ? default : data.Slice(cellOffset, (int)cells));
    }

    /// <summary>Decodes only the first frame of a pack container, straight from a span.</summary>
    /// <param name="data">The file bytes.</param>
    /// <param name="buildMips">Whether to build the chain the mip flag asks for; false for a caller that only measures level zero.</param>
    /// <returns>The first frame.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a usable version 12 container.</exception>
    /// <remarks>
    /// For the readers that want one texture: <see cref="Read"/> needs the bytes as memory for the grid
    /// it hands back, which cost every such caller a copy of the whole file, and it decoded every frame
    /// to return the first.
    /// </remarks>
    public static Frame ReadFirstFrame(ReadOnlySpan<byte> data, bool buildMips = true)
    {
        if (!Is(data))
        {
            throw new TextureFormatException($"Not a version {Version} pack picture.");
        }

        int count = ReadBounded(data[8..], "frame count");
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);

        if (count == 0)
        {
            throw new TextureFormatException("A pack picture holds no frames.");
        }

        if ((long)HeaderSize + FrameSize > data.Length)
        {
            throw new TextureFormatException("A pack picture's first frame record runs past the end of the file.");
        }

        if ((flags & BlockCompressedFlag) != 0)
        {
            // The chain is a slice of the file, and a span cannot be sliced into something that outlives
            // it: copied once, here. A caller holding the bytes as memory uses the overload that does not.
            return ReadFirstFrame((ReadOnlyMemory<byte>)data.ToArray(), buildMips);
        }

        return ReadFrame(data, data.Slice(HeaderSize, FrameSize), 0, buildMips && (flags & MipChainFlag) != 0, flags);
    }

    /// <summary>Decodes only the first frame of a pack container, from memory the result may keep.</summary>
    /// <param name="data">The file bytes. A block-compressed frame's chain is a slice of them, not a copy.</param>
    /// <param name="buildMips">Whether to build the chain the mip flag asks for, for a PNG frame.</param>
    /// <returns>The first frame.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a usable version 12 container.</exception>
    public static Frame ReadFirstFrame(ReadOnlyMemory<byte> data, bool buildMips = true)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (!Is(span))
        {
            throw new TextureFormatException($"Not a version {Version} pack picture.");
        }

        int count = ReadBounded(span[8..], "frame count");
        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);

        if (count == 0)
        {
            throw new TextureFormatException("A pack picture holds no frames.");
        }

        if ((long)HeaderSize + FrameSize > span.Length)
        {
            throw new TextureFormatException("A pack picture's first frame record runs past the end of the file.");
        }

        ReadOnlySpan<byte> record = span.Slice(HeaderSize, FrameSize);
        return (flags & BlockCompressedFlag) != 0
            ? ReadCompressedFrame(data, record, 0)
            : ReadFrame(span, record, 0, buildMips && (flags & MipChainFlag) != 0, flags);
    }

    /// <summary>Reads a block-compressed frame, its chains slices of the file's own bytes.</summary>
    private static Frame ReadCompressedFrame(ReadOnlyMemory<byte> data, ReadOnlySpan<byte> record, int frame)
    {
        CompressedTextureImage picture = ReadBlocks(data, record, frame, "picture", withProxy: true)
            ?? throw new TextureFormatException($"Frame {frame} has no picture.");
        CompressedTextureImage? surface = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]) == 0
            ? null
            : ReadBlocks(data, record[8..], frame, "surface map", withProxy: false);

        // The proxy stands in as the frame's image for anything that only lays the frame out or measures
        // it; a reader that needs every texel asks the chain to decode itself.
        TextureImage image = picture.Proxy ?? picture.Decode();

        return new Frame(
            image,
            BinaryPrimitives.ReadUInt16LittleEndian(record[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(record[18..]),
            BinaryPrimitives.ReadUInt16LittleEndian(record[20..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[22..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[24..]),
            Surface: null,
            Compressed: picture,
            CompressedSurface: surface);
    }

    private static CompressedTextureImage? ReadBlocks(
        ReadOnlyMemory<byte> data, ReadOnlySpan<byte> record, int frame, string what, bool withProxy)
    {
        int offset = ReadBounded(record, $"frame {what} offset");
        int length = ReadBounded(record[4..], $"frame {what} length");

        if (offset < 0 || length < BlockHeaderSize || (long)offset + length > data.Length)
        {
            throw new TextureFormatException(
                $"Frame {frame}'s {what} needs {length} bytes at offset {offset}, past the {data.Length}-byte file.");
        }

        ReadOnlyMemory<byte> payload = data.Slice(offset, length);
        ReadOnlySpan<byte> header = payload.Span;

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != BlockMagic)
        {
            throw new TextureFormatException($"Frame {frame}'s {what} is not a block-compressed chain.");
        }

        BlockFormat format = (BlockFormat)BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (format is not (BlockFormat.Bc1 or BlockFormat.Bc7))
        {
            throw new TextureFormatException($"Frame {frame}'s {what} names unknown block format {(int)format}.");
        }

        int width = ReadBounded(header[8..], $"frame {what} width");
        int height = ReadBounded(header[12..], $"frame {what} height");
        int levels = ReadBounded(header[16..], $"frame {what} levels");
        int proxyOffset = ReadBounded(header[20..], $"frame {what} proxy offset");
        int proxyLength = ReadBounded(header[24..], $"frame {what} proxy length");
        uint frameFlags = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);

        if (width == 0 || height == 0 || levels == 0 || levels > MipChainBuilder.LevelCount(width, height))
        {
            throw new TextureFormatException($"Frame {frame}'s {what} describes an impossible {width}x{height} chain of {levels}.");
        }

        long chain = BlockCompression.ChainBytes(format, width, height, levels);
        if (BlockHeaderSize + chain > length)
        {
            throw new TextureFormatException($"Frame {frame}'s {what} needs {chain} bytes of blocks, the payload holds {length - BlockHeaderSize}.");
        }

        TextureImage? proxy = null;
        if (withProxy && proxyLength > 0)
        {
            if ((long)proxyOffset + proxyLength > length)
            {
                throw new TextureFormatException($"Frame {frame}'s proxy runs past its payload.");
            }

            (Bgra32[] pixels, int proxyWidth, int proxyHeight) = PngReader.Read(header.Slice(proxyOffset, proxyLength));
            proxy = TextureImage.FromPixels(proxyWidth, proxyHeight, pixels);
        }

        return new CompressedTextureImage(
            format, width, height, levels, payload.Slice(BlockHeaderSize, (int)chain), (frameFlags & 1) != 0, proxy);
    }

    /// <summary>Lays a block-compressed chain out as a frame payload.</summary>
    private static byte[] EncodeBlocks(CompressedTextureImage image)
    {
        byte[]? proxy = image.Proxy is { } small ? Encode(small) : null;
        int chain = image.Levels.Length;
        byte[] payload = new byte[BlockHeaderSize + chain + (proxy?.Length ?? 0)];
        Span<byte> span = payload;

        BinaryPrimitives.WriteUInt32LittleEndian(span, BlockMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)image.Format);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)image.Width);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)image.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)image.MipLevels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], proxy is null ? 0u : (uint)(BlockHeaderSize + chain));
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)(proxy?.Length ?? 0));
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], image.IsCutout ? 1u : 0u);

        image.Levels.Span.CopyTo(span[BlockHeaderSize..]);
        proxy?.CopyTo(span[(BlockHeaderSize + chain)..]);
        return payload;
    }

    private static Frame ReadFrame(ReadOnlySpan<byte> span, ReadOnlySpan<byte> record, int i, bool mips, uint flags)
    {
        if ((flags & PngFlag) == 0)
        {
            throw new TextureFormatException("A pack picture with no PNG flag has no other encoding defined.");
        }

        // Decoded with room for its chain when one is to be built, so the chain is built where level
        // zero lies instead of in a second array it is copied into.
        TextureImage image = Decode(span, record, i, "picture", withChainRoom: mips);
        TextureImage? surface = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]) == 0
            ? null
            : Decode(span, record[8..], i, "surface map", withChainRoom: false);

        return new Frame(
            mips ? image.WithMipChain() : image,
            BinaryPrimitives.ReadUInt16LittleEndian(record[16..]),
            BinaryPrimitives.ReadUInt16LittleEndian(record[18..]),
            BinaryPrimitives.ReadUInt16LittleEndian(record[20..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[22..]),
            BinaryPrimitives.ReadInt16LittleEndian(record[24..]),

            // Never mipped. A surface map is sampled at one resolution by the shader that reads it,
            // and averaging normals down a chain is how a bumpy surface becomes a flat one.
            surface);
    }

    private static byte[] Encode(TextureImage image)
    {
        using MemoryStream encoded = new();
        PngWriter.Write(encoded, image.GetLevel(0), image.Width, image.Height);
        return encoded.ToArray();
    }

    private static TextureImage Decode(
        ReadOnlySpan<byte> span, ReadOnlySpan<byte> record, int frame, string what, bool withChainRoom)
    {
        int offset = ReadBounded(record, $"frame {what} offset");
        int length = ReadBounded(record[4..], $"frame {what} length");

        if (offset < 0 || length < 0 || (long)offset + length > span.Length)
        {
            throw new TextureFormatException(
                $"Frame {frame}'s {what} needs {length} bytes at offset {offset}, past the {span.Length}-byte file.");
        }

        (Bgra32[] pixels, int width, int height) = PngReader.Read(
            span.Slice(offset, length), withChainRoom ? MipChainBuilder.ChainLength : null);
        return TextureImage.FromPixels(width, height, pixels);
    }

    private static ushort Checked(int value, string what)
    {
        if (value is < 0 or > ushort.MaxValue)
        {
            throw new TextureFormatException($"A frame's {what} of {value} does not fit the field that records it.");
        }

        return (ushort)value;
    }

    private static int ReadBounded(ReadOnlySpan<byte> source, string what)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(source);

        if (value > int.MaxValue)
        {
            throw new TextureFormatException($"The {what} {value} is too large to address.");
        }

        return (int)value;
    }
}
