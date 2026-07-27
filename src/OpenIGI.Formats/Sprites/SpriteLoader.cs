using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;

namespace OpenIGI.Formats.Sprites;

/// <summary>
/// Loads sprites and bitmap fonts, reproducing the original's single <c>Sprite_Load</c> entry point
/// (<c>0x4B6720</c>).
/// </summary>
/// <remarks>
/// <para>
/// Four container shapes are accepted: an ILFF <c>FONT</c> container and <c>"LOOP"</c> versions 1, 7
/// and 9. Versions 5 and 10 exist but the original refuses them outright
/// (<c>"The font '%s' must be packed."</c>), and so does this loader — no retail file uses them.
/// </para>
/// <para>
/// Field offsets are transcribed from the decompiled loader; see the original
/// §5–6. Where the three <c>"LOOP"</c> versions disagree — and they disagree about where the format
/// code lives, which is the interesting part — each branch follows its own version rather than a
/// unified guess.
/// </para>
/// </remarks>
public static class SpriteLoader
{
    private const int PaletteEntryCount = 256;

    /// <summary>Loads a sprite or font.</summary>
    /// <param name="data">The container bytes.</param>
    /// <param name="name">The resource name, used in diagnostics only.</param>
    /// <returns>The loaded sprite.</returns>
    /// <exception cref="TextureFormatException">The bytes are not a container the original accepts.</exception>
    public static SpriteAsset Load(ReadOnlyMemory<byte> data, string name = "")
    {
        ReadOnlySpan<byte> span = data.Span;

        if (IlffContainer.IsIlff(span))
        {
            IlffContainer container = IlffContainer.Read(data);
            if (container.FormType == IlffTags.Font)
            {
                return LoadFont(container, name);
            }

            throw new TextureFormatException($"Invalid sprite file '{name}': ILFF form '{container.FormType}' is not a font.");
        }

        if (!TexFile.IsLoopAsset(span))
        {
            throw new TextureFormatException($"Invalid sprite file '{name}': not an ILFF container and not a 'LOOP' asset.");
        }

        uint version = TexFile.ReadVersion(span);
        return version switch
        {
            1 => LoadLoop1(span, name),
            7 => LoadLoop7(span, name),
            9 => LoadLoop9(span, name),
            5 or 10 => throw new TextureFormatException(
                $"Sprite '{name}' is a version {version} container, which the original refuses with "
                + "\"The font '%s' must be packed.\" — no retail file uses it."),
            _ => throw new TextureFormatException($"Invalid sprite file '{name}': unknown 'LOOP' version {version}."),
        };
    }

    /// <summary>Loads an ILFF <c>FONT</c> container.</summary>
    private static SpriteAsset LoadFont(IlffContainer container, string name)
    {
        IlffChunk header = RequireChunk(container, "FNTH", name);
        IlffChunk map = RequireChunk(container, "TRAN", name);
        IlffChunk glyphs = RequireChunk(container, "ANMF", name);

        ReadOnlySpan<byte> fnth = header.Payload.Span;
        if (fnth.Length < 24)
        {
            throw new TextureFormatException($"Font '{name}' has a {fnth.Length}-byte FNTH chunk; six dwords are required.");
        }

        uint firstField = BinaryPrimitives.ReadUInt32LittleEndian(fnth);
        int glyphCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(fnth[4..]);
        int defaultAdvance = (int)BinaryPrimitives.ReadUInt32LittleEndian(fnth[12..]);
        int defaultWidth = (int)BinaryPrimitives.ReadUInt32LittleEndian(fnth[16..]);
        int defaultHeight = (int)BinaryPrimitives.ReadUInt32LittleEndian(fnth[20..]);

        // Sprite_Alloc: flags 0x10000 when FNTH's first field is zero.
        uint flags = firstField != 0 ? 0u : 0x10000u;

        TextureImage[] textures = LoadTexturesFromChunks(container, name);
        SpriteFrame[] frames = ReadGlyphRecords(glyphs.Payload.Span, glyphCount, name);
        byte[] characterMap = ReadCharacterMap(map.Payload.Span, name);

        return new SpriteAsset(
            name,
            SpriteKind.Font,
            textures,
            frames,
            defaultWidth,
            defaultHeight,
            defaultAdvance,
            flags,
            palette: null,
            characterMap,
            tileLayout: null);
    }

    /// <summary>Reads the 40-byte <c>ANMF</c> glyph records. Mirrors the font branch of <c>0x4B6720</c>.</summary>
    private static SpriteFrame[] ReadGlyphRecords(ReadOnlySpan<byte> anmf, int glyphCount, string name)
    {
        const int RecordSize = 40;

        if (glyphCount < 0 || (long)glyphCount * RecordSize > anmf.Length)
        {
            throw new TextureFormatException(
                $"Font '{name}' declares {glyphCount} glyphs but its ANMF chunk holds {anmf.Length / RecordSize}.");
        }

        SpriteFrame[] frames = new SpriteFrame[glyphCount];
        for (int i = 0; i < glyphCount; i++)
        {
            ReadOnlySpan<byte> r = anmf.Slice(i * RecordSize, RecordSize);
            frames[i] = new SpriteFrame(
                TextureIndex: BinaryPrimitives.ReadInt16LittleEndian(r[20..]),
                Width: BinaryPrimitives.ReadUInt16LittleEndian(r[22..]),
                Height: BinaryPrimitives.ReadUInt16LittleEndian(r[24..]),
                OffsetX: BinaryPrimitives.ReadUInt16LittleEndian(r[34..]),
                OffsetY: BinaryPrimitives.ReadUInt16LittleEndian(r[36..]),
                Advance: BinaryPrimitives.ReadUInt16LittleEndian(r[26..]),
                Flags: BinaryPrimitives.ReadUInt32LittleEndian(r),
                U0: BinaryPrimitives.ReadSingleLittleEndian(r[4..]),
                V0: BinaryPrimitives.ReadSingleLittleEndian(r[8..]),
                U1: BinaryPrimitives.ReadSingleLittleEndian(r[12..]),
                V1: BinaryPrimitives.ReadSingleLittleEndian(r[16..]));
        }

        return frames;
    }

    /// <summary>
    /// Reads the trailing <c>"LOOP"</c> version 6 tile-grid block a picture's header points at.
    /// Mirrors <c>TileGrid_Load</c> (<c>0x4B75C0</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Block layout: the magic and version, two words at +8 and +12, the column and row counts at +16
    /// and +20, then a 16-byte record per cell holding <c>{ reserved, x, y, frameNumber }</c>.
    /// </para>
    /// <para>
    /// The original reads exactly two things — the column and row counts, and the low byte of each
    /// record's frame number — and derives every tile's position from its grid cell. So the per-record
    /// x and y are redundant, and the two words at +8/+12 are not read at all: in the large pictures
    /// they happen to hold the picture's size, but in <c>pointer.spr</c> and the window-frame sprites
    /// they hold stale pointer values. Reading them would work on the files that made the theory look
    /// right and produce nonsense on the rest.
    /// </para>
    /// </remarks>
    private static SpriteTileLayout ReadTileLayout(ReadOnlySpan<byte> data, int offset, int frameCount, string name)
    {
        const int BlockHeaderSize = 24;
        const int RecordSize = 16;

        if (offset < 0 || offset + BlockHeaderSize > data.Length)
        {
            throw new TextureFormatException(
                $"Sprite '{name}' points at a tile grid at offset {offset}, outside its {data.Length} bytes.");
        }

        ReadOnlySpan<byte> block = data[offset..];
        if (!TexFile.IsLoopAsset(block))
        {
            throw new TextureFormatException($"Sprite '{name}' has a tile grid without the 'LOOP' magic.");
        }

        int columns = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[16..]);
        int rows = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[20..]);

        if (columns <= 0 || rows <= 0)
        {
            throw new TextureFormatException($"Sprite '{name}' has a {columns}x{rows} tile grid.");
        }

        int cellCount = columns * rows;
        if (BlockHeaderSize + ((long)cellCount * RecordSize) > block.Length)
        {
            throw new TextureFormatException(
                $"Sprite '{name}' declares a {columns}x{rows} tile grid that does not fit in its remaining {block.Length} bytes.");
        }

        byte[] cells = new byte[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            ReadOnlySpan<byte> record = block.Slice(BlockHeaderSize + (i * RecordSize), RecordSize);
            byte frameNumber = record[12];
            if (frameNumber > frameCount)
            {
                throw new TextureFormatException(
                    $"Sprite '{name}' tile {i} names frame {frameNumber}, past its {frameCount} frames.");
            }

            cells[i] = frameNumber;
        }

        return new SpriteTileLayout(columns, rows, cells);
    }

    /// <summary>Reads the <c>TRAN</c> character map: 256 words of which only the low byte is used.</summary>
    private static byte[] ReadCharacterMap(ReadOnlySpan<byte> tran, string name)
    {
        if (tran.Length < PaletteEntryCount * sizeof(ushort))
        {
            throw new TextureFormatException(
                $"Font '{name}' has a {tran.Length}-byte TRAN chunk; {PaletteEntryCount * sizeof(ushort)} are required.");
        }

        byte[] map = new byte[PaletteEntryCount];
        for (int i = 0; i < PaletteEntryCount; i++)
        {
            map[i] = tran[i * 2];
        }

        return map;
    }

    /// <summary>Decodes every <c>TEXH</c>/<c>BODY</c> pair in a container. Mirrors <c>Texture_LoadFromIlff</c> (<c>0x4B2070</c>).</summary>
    private static TextureImage[] LoadTexturesFromChunks(IlffContainer container, string name)
    {
        FourCc texh = FourCc.FromString("TEXH");
        List<TextureImage> textures = [];

        IReadOnlyList<IlffChunk> chunks = container.Chunks;
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Id != texh)
            {
                continue;
            }

            if (i + 1 >= chunks.Count || chunks[i + 1].Id != IlffTags.Body)
            {
                throw new TextureFormatException($"Sprite '{name}' has a TEXH chunk that is not followed by a BODY chunk.");
            }

            TextureDescriptor descriptor = TextureDescriptor.ReadTexh(chunks[i].Payload.Span);
            textures.Add(TextureDecoder.Decode(descriptor, chunks[i + 1].Payload.Span));
        }

        if (textures.Count == 0)
        {
            throw new TextureFormatException($"Sprite '{name}' holds no textures.");
        }

        return [.. textures];
    }

    /// <summary>Loads a <c>"LOOP"</c> version 1 sprite: one frame, header fields inline.</summary>
    private static SpriteAsset LoadLoop1(ReadOnlySpan<byte> data, string name)
    {
        Require(data, 28, name);

        int paletteOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        Bgra32[]? palette = null;
        uint format;
        if (paletteOffset != 0)
        {
            (palette, format) = ReadPalette(data, paletteOffset, name);
        }
        else
        {
            // Sprite_Load's version 1 branch defaults the format to 2 when there is no palette chunk.
            format = 2;
        }

        int pitch = BinaryPrimitives.ReadUInt16LittleEndian(data[22..]);
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[24..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[26..]);

        TextureDescriptor descriptor = TextureDescriptor.Create(format, width, height, pitch);
        TextureImage texture = TextureDecoder.Decode(descriptor, data[28..], palette);

        SpriteFrame[] frames =
        [
            new SpriteFrame(0, width, height, 0, 0, 0, 0, 0.0f, 0.0f, 1.0f, 1.0f),
        ];

        return new SpriteAsset(name, SpriteKind.Loop1, [texture], frames, width, height, 0, 0x10000, palette, null, null);
    }

    /// <summary>Loads a <c>"LOOP"</c> version 7 sprite: 40-byte frame records from +52, one shared format.</summary>
    private static SpriteAsset LoadLoop7(ReadOnlySpan<byte> data, string name) =>
        LoadMultiFrame(data, name, SpriteKind.Loop7, recordSize: 40);

    /// <summary>Loads a <c>"LOOP"</c> version 9 sprite: 32-byte frame records from +52, per-frame formats.</summary>
    private static SpriteAsset LoadLoop9(ReadOnlySpan<byte> data, string name) =>
        LoadMultiFrame(data, name, SpriteKind.Loop9, recordSize: 32);

    /// <summary>
    /// The shared body of the version 7 and 9 branches. They agree on the header and differ only in
    /// their frame-record layout and in where the format code comes from.
    /// </summary>
    private static SpriteAsset LoadMultiFrame(ReadOnlySpan<byte> data, string name, SpriteKind kind, int recordSize)
    {
        const int FrameTableOffset = 52;

        Require(data, FrameTableOffset, name);

        int paletteOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
        int frameCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[32..]);
        int defaultWidth = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[40..]);
        int defaultHeight = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[44..]);
        ushort secondaryFormat = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);

        Bgra32[]? palette = null;
        uint sharedFormat;
        if (paletteOffset != 0)
        {
            (palette, sharedFormat) = ReadPalette(data, paletteOffset, name);
        }
        else
        {
            sharedFormat = BinaryPrimitives.ReadUInt32LittleEndian(data[48..]);
        }

        if (frameCount < 0 || (long)FrameTableOffset + ((long)frameCount * recordSize) > data.Length)
        {
            throw new TextureFormatException(
                $"Sprite '{name}' declares {frameCount} frames, which do not fit in {data.Length} bytes.");
        }

        // Sprite_Alloc sets 0x10000 only for a single-frame sprite.
        uint flags = frameCount == 1 ? 0x10000u : 0u;

        TextureImage[] textures = new TextureImage[frameCount];
        SpriteFrame[] frames = new SpriteFrame[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            ReadOnlySpan<byte> r = data.Slice(FrameTableOffset + (i * recordSize), recordSize);

            int pixelOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(r);
            int pitch;
            int width;
            int height;
            int advance;
            int offsetX;
            int offsetY;
            uint format;
            uint formatParameter;

            if (kind == SpriteKind.Loop9)
            {
                format = BinaryPrimitives.ReadUInt32LittleEndian(r[4..]);
                pitch = BinaryPrimitives.ReadUInt16LittleEndian(r[8..]);
                width = BinaryPrimitives.ReadUInt16LittleEndian(r[10..]);
                height = BinaryPrimitives.ReadUInt16LittleEndian(r[12..]);
                advance = BinaryPrimitives.ReadUInt16LittleEndian(r[14..]);
                offsetX = BinaryPrimitives.ReadUInt16LittleEndian(r[22..]);
                offsetY = BinaryPrimitives.ReadUInt16LittleEndian(r[24..]);
                formatParameter = BinaryPrimitives.ReadUInt32LittleEndian(r[28..]);
            }
            else
            {
                format = sharedFormat;
                pitch = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[4..]);
                width = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[8..]);
                height = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[12..]);
                advance = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[16..]);
                offsetX = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[32..]);
                offsetY = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[36..]);
                formatParameter = 0;
            }

            if (pixelOffset < 0 || pixelOffset > data.Length)
            {
                throw new TextureFormatException(
                    $"Sprite '{name}' frame {i} points at offset {pixelOffset}, outside its {data.Length} bytes.");
            }

            TextureDescriptor descriptor = TextureDescriptor.Create(
                format,
                width,
                height,
                pitch,
                secondaryFormat,
                formatParameter);

            textures[i] = TextureDecoder.Decode(descriptor, data[pixelOffset..], palette);
            frames[i] = new SpriteFrame(i, width, height, offsetX, offsetY, advance, formatParameter, 0.0f, 0.0f, 1.0f, 1.0f);
        }

        // A picture stored as tiles points at a trailing grid block that says how to reassemble it.
        int tileGridOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[28..]);
        SpriteTileLayout? layout = tileGridOffset != 0
            ? ReadTileLayout(data, tileGridOffset, frameCount, name)
            : null;

        return new SpriteAsset(
            name,
            kind,
            textures,
            frames,
            defaultWidth,
            defaultHeight,
            0,
            flags,
            palette,
            characterMap: null,
            layout);
    }

    /// <summary>
    /// Reads a palette chunk: a format code at +12 and 256 four-byte entries at +16.
    /// </summary>
    /// <remarks>
    /// Entries are stored blue-first, exactly like <see cref="Bgra32"/> — the paletted converter
    /// (<c>0x517FD0</c>) copies palette bytes 0, 1 and 2 straight into destination bytes 0, 1 and 2.
    /// Alpha is not stored: the converter writes <c>0xFF</c> for every index except zero, which is the
    /// transparent index.
    /// </remarks>
    private static (Bgra32[] Palette, uint Format) ReadPalette(ReadOnlySpan<byte> data, int offset, string name)
    {
        const int EntriesOffset = 16;
        int required = offset + EntriesOffset + (PaletteEntryCount * 4);
        if (offset < 0 || required > data.Length)
        {
            throw new TextureFormatException(
                $"Sprite '{name}' has a palette at offset {offset} that does not fit in {data.Length} bytes.");
        }

        uint format = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 12)..]);
        ReadOnlySpan<byte> entries = data.Slice(offset + EntriesOffset, PaletteEntryCount * 4);

        Bgra32[] palette = new Bgra32[PaletteEntryCount];
        for (int i = 0; i < PaletteEntryCount; i++)
        {
            ReadOnlySpan<byte> entry = entries.Slice(i * 4, 4);
            palette[i] = new Bgra32(entry[0], entry[1], entry[2], i == 0 ? (byte)0 : (byte)0xFF);
        }

        return (palette, format);
    }

    private static IlffChunk RequireChunk(IlffContainer container, string tag, string name)
    {
        if (!container.TryFindChunk(FourCc.FromString(tag), out IlffChunk chunk))
        {
            throw new TextureFormatException($"ILF file corrupt: font '{name}' has no '{tag}' chunk.");
        }

        return chunk;
    }

    private static void Require(ReadOnlySpan<byte> data, int size, string name)
    {
        if (data.Length < size)
        {
            throw new TextureFormatException($"Sprite '{name}' is {data.Length} bytes; its header needs {size}.");
        }
    }
}
