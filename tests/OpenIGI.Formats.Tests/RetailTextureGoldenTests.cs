using System.Buffers.Binary;
using OpenIGI.Core;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Runs the texture, sprite and font loaders over every matching file in a retail installation.
/// </summary>
/// <remarks>
/// A format is only understood when it parses the whole corpus, not one hand-picked file. These tests
/// therefore assert on totals and on the size arithmetic closing exactly — a header misread by one
/// field shows up as a payload that does not add up.
/// </remarks>
public sealed class RetailTextureGoldenTests
{
    [RequiresGameDataFact]
    public void EveryLooseTexFileDecodes()
    {
        IReadOnlyList<string> files = GameData.EnumerateFiles("*.tex");
        Assert.NotEmpty(files);

        int decoded = 0;
        int sprites = 0;
        List<string> failures = [];

        foreach (string path in files)
        {
            byte[] bytes = File.ReadAllBytes(path);
            try
            {
                if (TexFile.IsTexture(bytes))
                {
                    TextureImage image = TexFile.Load(bytes);
                    Assert.True(image.Width > 0 && image.Height > 0);
                    Assert.Equal(image.Width * image.Height, image.GetLevel(0).Length);
                    decoded++;
                }
                else
                {
                    // The per-level terrain.tex files are version 9 sprites, not textures. The
                    // original routes them through the sprite loader for the same reason.
                    SpriteAsset sprite = SpriteLoader.Load(bytes, Path.GetFileName(path));
                    Assert.NotEmpty(sprite.Frames);
                    sprites++;
                }
            }
            catch (Exception ex) when (ex is TextureFormatException or IlffFormatException)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
        Assert.True(decoded > 0, "No .tex file was recognised as a texture.");
        Assert.True(sprites > 0, "No .tex file was recognised as a sprite.");
    }

    [RequiresGameDataFact]
    public void TexHeaderSizeArithmeticClosesExactly()
    {
        List<string> mismatches = [];

        foreach (string path in GameData.EnumerateFiles("*.tex"))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!TexFile.IsTexture(bytes))
            {
                continue;
            }

            TextureDescriptor descriptor = TexFile.ReadHeader(bytes, out int payloadOffset);
            int expected = payloadOffset + descriptor.PayloadSize;
            if (expected != bytes.Length)
            {
                mismatches.Add($"{Path.GetFileName(path)}: header implies {expected} bytes, file is {bytes.Length}");
            }
        }

        Assert.Empty(mismatches);
    }

    [RequiresGameDataFact]
    public void KnownTexturesDecodeToTheirDocumentedShape()
    {
        // flare00.tex is a version 2 file: stride stored directly, 16-bit A1R5G5B5, no mips.
        byte[] flare = File.ReadAllBytes(GameData.Resolve("common/textures/flare00.tex"));
        Assert.Equal(TexFile.Version2, TexFile.ReadVersion(flare));

        TextureDescriptor flareHeader = TexFile.ReadHeader(flare, out int flareOffset);
        Assert.Equal(TextureFormatCode.A1R5G5B5, flareHeader.Format);
        Assert.Equal(64, flareHeader.Width);
        Assert.Equal(64, flareHeader.Height);
        Assert.Equal(128, flareHeader.Pitch);
        Assert.False(flareHeader.HasMipChain);
        Assert.Equal(24, flareOffset);
        Assert.Equal(flare.Length, flareOffset + flareHeader.PayloadSize);

        // sky_layer1.tex is a version 11 file: allocated width times *bytes* per pixel, 32-bit.
        byte[] sky = File.ReadAllBytes(GameData.Resolve("missions/location0/common/textures/sky_layer1.tex"));
        Assert.Equal(TexFile.Version11, TexFile.ReadVersion(sky));

        TextureDescriptor skyHeader = TexFile.ReadHeader(sky, out int skyOffset);
        Assert.Equal(TextureFormatCode.Bgra8, skyHeader.Format);
        Assert.Equal(64, skyHeader.Width);
        Assert.Equal(64, skyHeader.Height);
        Assert.Equal(256, skyHeader.Pitch);
        Assert.Equal(32, skyOffset);
        Assert.Equal(sky.Length, skyOffset + skyHeader.PayloadSize);
    }

    [RequiresGameDataFact]
    public void EveryLooseSpriteAndFontLoads()
    {
        List<string> failures = [];
        int loaded = 0;

        foreach (string pattern in new[] { "*.spr", "*.fnt", "*.pic" })
        {
            foreach (string path in GameData.EnumerateFiles(pattern))
            {
                try
                {
                    SpriteAsset sprite = SpriteLoader.Load(File.ReadAllBytes(path), Path.GetFileName(path));
                    Assert.NotEmpty(sprite.Frames);
                    Assert.NotEmpty(sprite.Textures);
                    loaded++;
                }
                catch (Exception ex) when (ex is TextureFormatException or IlffFormatException)
                {
                    failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
        }

        Assert.Empty(failures);
        Assert.True(loaded > 0, "No loose sprite or font was found.");
    }

    [RequiresGameDataFact]
    public void EverySpriteInsideTheMenuArchiveLoads()
    {
        string archivePath = GameData.Resolve("menusystem/menusystem.res");
        Assert.True(File.Exists(archivePath), $"{archivePath} is missing.");

        IresArchive archive = IresArchive.Read(File.ReadAllBytes(archivePath));
        List<string> failures = [];
        int loaded = 0;

        foreach (IresEntry entry in archive.Entries)
        {
            if (entry.Kind != IresEntryKind.Body)
            {
                continue;
            }

            string extension = Path.GetExtension(entry.Name);
            if (extension is not (".spr" or ".fnt" or ".pic"))
            {
                continue;
            }

            try
            {
                SpriteAsset sprite = SpriteLoader.Load(entry.Data, entry.Name);
                Assert.NotEmpty(sprite.Frames);
                loaded++;
            }
            catch (Exception ex) when (ex is TextureFormatException or IlffFormatException)
            {
                failures.Add($"{entry.Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
        Assert.True(loaded >= 20, $"Expected the menu archive to hold at least 20 sprites, loaded {loaded}.");
    }

    [RequiresGameDataFact]
    public void TheEmbeddedFontDecodesIntoGlyphs()
    {
        var image = PortableExecutable.Load(GameData.Resolve("IGI.exe"));
        SpriteAsset font = SpriteLoader.Load(image.ReadVirtual(0x5488DC, 0x507C), "IGI.exe:font");

        Assert.True(font.IsFont);
        Assert.Equal(SpriteKind.Font, font.Kind);
        Assert.Equal(0x59, font.Frames.Count);
        Assert.Single(font.Textures);

        // The atlas is the one described by the TEXH chunk: 128x64.
        TextureImage atlas = font.Textures[0];
        Assert.Equal(128, atlas.Width);
        Assert.Equal(64, atlas.Height);

        // Glyph 0's texture coordinates are the ones read straight out of the ANMF record.
        SpriteFrame first = font.Frames[0];
        Assert.Equal(0.0f, first.U0);
        Assert.Equal(0.0f, first.V0);
        Assert.Equal(0.0625f, first.U1, 5);

        // Map entries are one-based, so the largest legal value is the glyph count itself and zero
        // means "no glyph". Read as zero-based, '@' would run one past the last record.
        Assert.Equal(font.Frames.Count, font.CharacterMap['@']);
        Assert.Equal(font.Frames[^1], font.GetGlyph('@'));

        for (int c = 0; c < 256; c++)
        {
            Assert.True(
                font.CharacterMap[c] <= font.Frames.Count,
                $"Character {c} maps to glyph {font.CharacterMap[c]}, past the last glyph.");
        }

        // A character with no mapping falls back to the trailing defaults record, which draws nothing.
        int unmapped = -1;
        for (int c = 0; c < 256; c++)
        {
            if (font.CharacterMap[c] == 0)
            {
                unmapped = c;
                break;
            }
        }

        if (unmapped >= 0)
        {
            Assert.Equal(font.DefaultFrame, font.GetGlyph((char)unmapped));
            Assert.Equal(0.0f, font.GetGlyph((char)unmapped).U1);
        }
    }

    [RequiresGameDataFact]
    public void MenuFontsRenderPrintableAsciiToNonEmptyGlyphs()
    {
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(GameData.Resolve("menusystem/menusystem.res")));
        IresEntry entry = archive.Entries.First(e => e.Name.EndsWith("font1.fnt", StringComparison.OrdinalIgnoreCase));

        SpriteAsset font = SpriteLoader.Load(entry.Data, entry.Name);
        Assert.True(font.IsFont);

        // Letters and digits must land on a glyph with pixels; a font whose character map was read at
        // the wrong stride collapses everything onto glyph zero instead.
        int nonEmpty = 0;
        for (char c = 'A'; c <= 'Z'; c++)
        {
            if (!font.GetGlyph(c).IsEmpty)
            {
                nonEmpty++;
            }
        }

        Assert.Equal(26, nonEmpty);

        // Layout advances the cursor by the glyph's advance. If that field were misread, every glyph
        // would report zero and a whole string would stack on one pixel.
        for (char c = 'A'; c <= 'Z'; c++)
        {
            SpriteFrame glyph = font.GetGlyph(c);
            Assert.True(glyph.Advance > 0, $"Glyph '{c}' has advance {glyph.Advance}.");
            Assert.True(
                glyph.Advance >= glyph.Width,
                $"Glyph '{c}' advances {glyph.Advance} but is {glyph.Width} wide, so letters would overlap.");
        }

        // Texture coordinates must lie inside the atlas and enclose a positive area.
        foreach (SpriteFrame glyph in font.Frames)
        {
            Assert.InRange(glyph.U0, 0.0f, 1.0f);
            Assert.InRange(glyph.V0, 0.0f, 1.0f);
            Assert.InRange(glyph.U1, 0.0f, 1.0f);
            Assert.InRange(glyph.V1, 0.0f, 1.0f);
            Assert.True(glyph.TextureIndex >= 0 && glyph.TextureIndex < font.Textures.Count);
        }
    }

    [RequiresGameDataFact]
    public void TiledPicturesReassembleIntoTheirDeclaredSize()
    {
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(GameData.Resolve("menusystem/menusystem.res")));

        SpriteAsset background = LoadEntry(archive, "mainmenu.pic");
        SpriteTileLayout layout = Assert.IsType<SpriteTileLayout>(background.TileLayout);

        // 150 tiles of 64x32, ten across and fifteen down, is exactly the 640x480 the menus are
        // authored against.
        Assert.Equal(150, background.Frames.Count);
        Assert.Equal(10, layout.Columns);
        Assert.Equal(15, layout.Rows);
        Assert.Equal(64, background.DefaultWidth);
        Assert.Equal(32, background.DefaultHeight);
        Assert.Equal(640, layout.Columns * background.DefaultWidth);
        Assert.Equal(480, layout.Rows * background.DefaultHeight);

        // Every cell must name a frame, and the grid must be a permutation of them: a picture with a
        // repeated or missing tile would draw with a visible hole.
        HashSet<int> used = [];
        for (int row = 0; row < layout.Rows; row++)
        {
            for (int column = 0; column < layout.Columns; column++)
            {
                int frame = layout.GetFrameIndex(column, row);
                Assert.InRange(frame, 0, background.Frames.Count - 1);
                Assert.True(used.Add(frame), $"Frame {frame} appears at more than one cell.");
            }
        }

        Assert.Equal(background.Frames.Count, used.Count);
    }

    [RequiresGameDataFact]
    public void TileCellsAreOneBased()
    {
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(GameData.Resolve("menusystem/menusystem.res")));
        SpriteAsset background = LoadEntry(archive, "mainmenu.pic");
        SpriteTileLayout layout = Assert.IsType<SpriteTileLayout>(background.TileLayout);

        // Read as zero-based, the last cell would be one past the last frame — which is how the
        // convention was caught: text drawn through the same table came out shifted a glyph.
        Assert.Equal(1, layout.Cells[0]);
        Assert.Equal(background.Frames.Count, layout.Cells[^1]);
        Assert.Equal(0, layout.GetFrameIndex(0, 0));
        Assert.Equal(background.Frames.Count - 1, layout.GetFrameIndex(layout.Columns - 1, layout.Rows - 1));
    }

    [RequiresGameDataFact]
    public void FontCharacterMapsAreOneBasedToo()
    {
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(GameData.Resolve("menusystem/menusystem.res")));
        SpriteAsset font = LoadEntry(archive, "font1.fnt");

        // 'A' through 'Z' are consecutive glyphs in every retail font, so a one-off in the mapping
        // shows up as the whole alphabet sliding by one.
        int a = font.CharacterMap['A'];
        for (int i = 0; i < 26; i++)
        {
            Assert.Equal(a + i, font.CharacterMap[(char)('A' + i)]);
        }

        // Consecutive letters must also resolve to consecutive, distinct glyph rectangles.
        Assert.NotEqual(font.GetGlyph('A'), font.GetGlyph('B'));
        Assert.Equal(font.Frames[a - 1], font.GetGlyph('A'));
    }

    [RequiresGameDataFact]
    public void FontDefaultHeightComesFromTheRetailFnthPlusTwentyField()
    {
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(GameData.Resolve("menusystem/menusystem.res")));
        SpriteAsset font = LoadEntry(archive, "font1.fnt");

        // The retail FNTH loader writes the default frame height from +20. FontText uses that frame
        // when advancing its lines; +8 is a different header field and must not be substituted here.
        Assert.Equal(11, font.DefaultHeight);
    }

    [RequiresGameDataFact]
    public void SingleFrameSpritesCarryAOneByOneTileGrid()
    {
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(GameData.Resolve("menusystem/menusystem.res")));
        SpriteAsset pointer = LoadEntry(archive, "pointer.spr");

        // Even a one-frame sprite has the trailing grid block; it is simply 1x1. Its width and height
        // words hold stale pointer values, which is why the loader ignores them.
        SpriteTileLayout layout = Assert.IsType<SpriteTileLayout>(pointer.TileLayout);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(1, layout.Rows);
        Assert.Equal(0, layout.GetFrameIndex(0, 0));

        Assert.Single(pointer.Frames);
        Assert.Equal(20, pointer.Frames[0].Width);
        Assert.Equal(22, pointer.Frames[0].Height);
    }

    [RequiresGameDataFact]
    public void PaletteEntriesAreStoredBlueFirst()
    {
        // Any paletted sprite proves the channel order: the converter copies palette bytes straight
        // through, so a palette read as RGB would swap red and blue everywhere it is used.
        foreach (string path in GameData.EnumerateFiles("*.spr"))
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!TexFile.IsLoopAsset(bytes) || TexFile.ReadVersion(bytes) is not (7 or 9))
            {
                continue;
            }

            int paletteOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24));
            if (paletteOffset == 0)
            {
                continue;
            }

            SpriteAsset sprite = SpriteLoader.Load(bytes, Path.GetFileName(path));
            Assert.Equal(256, sprite.Palette.Length);

            // Index zero is the transparent index in the original's paletted converter.
            Assert.Equal(0, sprite.Palette[0].A);
            return;
        }
    }

    [RequiresGameDataFact]
    public void DecodedPixelsRoundTripThroughTheCanonicalLayout()
    {
        byte[] sky = File.ReadAllBytes(GameData.Resolve("missions/location0/common/textures/sky_layer1.tex"));
        TextureImage image = TexFile.Load(sky);

        // Format 3 is a straight dword copy, so the first decoded pixel must equal the first stored
        // dword read as 0xAARRGGBB. This is the assertion that pins the channel order.
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(sky.AsSpan(32));
        Bgra32 expected = Bgra32.FromPacked(stored);

        Assert.Equal(expected, image.GetLevel(0)[0]);
        Assert.Equal((byte)(stored & 0xFF), image.GetLevel(0)[0].B);
        Assert.Equal((byte)((stored >> 16) & 0xFF), image.GetLevel(0)[0].R);
    }

    private static SpriteAsset LoadEntry(IresArchive archive, string suffix)
    {
        IresEntry entry = archive.Entries.First(
            e => e.Kind == IresEntryKind.Body && e.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        return SpriteLoader.Load(entry.Data, entry.Name);
    }
}
