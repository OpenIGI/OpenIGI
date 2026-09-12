using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Round-trips the container writers through the readers the game itself uses.
/// </summary>
/// <remarks>
/// <para>
/// A writer is only correct if the reader agrees, so nothing here checks a byte offset by hand: every
/// test encodes something and then decodes it with <see cref="IresArchive"/>, <see cref="TexFile"/> or
/// <see cref="TexAtlasFile"/>. That is the same code path the renderer takes, so a header field written
/// into the wrong slot cannot pass.
/// </para>
/// <para>
/// The retail tests go further and rebuild the game's own archives from their own entries. This is the
/// strongest available statement that an overrides pack will load: the input is the shipping file, and
/// the output has to hold the same names, in the same order, with the same bytes.
/// </para>
/// </remarks>
public sealed class ContainerWriterTests
{
    [Fact]
    public void AnArchiveComesBackWithItsEntriesInOrder()
    {
        IresWriter.Entry[] entries =
        [
            new("LOCAL:common/textures/a.tex", IresEntryKind.Body, new byte[] { 1, 2, 3 }),
            new("LOCAL:common/textures/b.tex", IresEntryKind.Body, new byte[] { 4, 5, 6, 7, 8 }),
            new("LOCAL:common/textures", IresEntryKind.Path, new byte[] { 9 }),
        ];

        IresArchive archive = IresArchive.Read(IresWriter.Write(entries));

        Assert.Equal(3, archive.Entries.Count);

        // WITH the namespace prefix. The reader normalises separators but keeps "LOCAL:", because that
        // is part of the name the game looks a resource up by -- only the extractor strips it, and only
        // to turn a name into a path on disk.
        Assert.Equal("LOCAL:common/textures/a.tex", archive.Entries[0].Name);
        Assert.Equal(new byte[] { 4, 5, 6, 7, 8 }, archive.Entries[1].Data.ToArray());
        Assert.Equal(IresEntryKind.Path, archive.Entries[2].Kind);
    }

    [Fact]
    public void AnOddlySizedPayloadDoesNotDisturbTheNextEntry()
    {
        // Chunks advance by alignUp(16 + payload, 4), so a payload that is not a multiple of four is
        // where an arithmetic slip shows up: the next chunk header lands one to three bytes off and the
        // walk reads a tag out of someone else's pixels.
        IresWriter.Entry[] entries =
        [
            new("LOCAL:one", IresEntryKind.Body, new byte[] { 1 }),
            new("LOCAL:two", IresEntryKind.Body, new byte[] { 2, 3 }),
            new("LOCAL:three", IresEntryKind.Body, new byte[] { 4, 5, 6 }),
            new("LOCAL:four", IresEntryKind.Body, new byte[] { 7, 8, 9, 10 }),
        ];

        IresArchive archive = IresArchive.Read(IresWriter.Write(entries));

        Assert.Equal(4, archive.Entries.Count);
        Assert.Equal(new byte[] { 4, 5, 6 }, archive.Entries[2].Data.ToArray());
        Assert.Equal(new byte[] { 7, 8, 9, 10 }, archive.Entries[3].Data.ToArray());
    }

    [Fact]
    public void AnEntryWithoutANameIsRefused()
    {
        // The reader throws on a payload chunk that precedes any NAME, so writing one would produce an
        // archive that cannot be read back. Refusing here says so where the mistake was made.
        IresWriter.Entry[] entries = [new(string.Empty, IresEntryKind.Body, new byte[] { 1 })];

        Assert.Throws<IlffFormatException>(() => IresWriter.Write(entries));
    }

    [Theory]
    [InlineData(TexFile.Version2)]
    [InlineData(TexFile.Version11)]
    public void ATextureComesBackPixelForPixel(uint version)
    {
        TextureImage written = TexFile.Load(TexWriter.WriteTexture(Checkerboard(8, 4), version));

        Assert.Equal(8, written.Width);
        Assert.Equal(4, written.Height);
        Assert.Equal(Checkerboard(8, 4).Pixels.ToArray(), written.GetLevel(0).ToArray());
    }

    [Fact]
    public void AMipChainSurvivesVersionEleven()
    {
        // The flag and the payload have to agree. A chain written without the flag reads back as one
        // level; the flag set without a chain sends the reader past the end of the file.
        TextureImage source = Checkerboard(8, 8).WithMipChain();
        Assert.True(source.MipLevels > 1);

        TextureImage written = TexFile.Load(TexWriter.WriteTexture(source, TexFile.Version11));

        Assert.Equal(source.MipLevels, written.MipLevels);
        Assert.Equal(source.GetLevel(source.MipLevels - 1).ToArray(), written.GetLevel(written.MipLevels - 1).ToArray());
    }

    [Theory]
    [InlineData(TexAtlasFile.Version7)]
    [InlineData(TexAtlasFile.Version9)]
    public void AnAtlasKeepsItsLayersInOrder(uint version)
    {
        // Order is the whole point of the test. Terrain names its ground by index into this atlas, so
        // layers that come back transposed repaint a level without failing anything.
        TextureImage[] tiles = [Solid(4, 4, 10), Solid(4, 4, 20), Solid(4, 4, 30)];

        TexAtlasFile atlas = TexAtlasFile.Read(TexWriter.WriteAtlas(tiles, version));

        Assert.Equal(3, atlas.Tiles.Count);
        Assert.Equal(10, atlas.Tiles[0].GetLevel(0)[0].R);
        Assert.Equal(20, atlas.Tiles[1].GetLevel(0)[0].R);
        Assert.Equal(30, atlas.Tiles[2].GetLevel(0)[0].R);
    }

    [Fact]
    public void AnEmptyAtlasIsRefused() =>
        Assert.Throws<TextureFormatException>(() => TexWriter.WriteAtlas([], TexAtlasFile.Version9));

    [Fact]
    public void AVersionTheOriginalDoesNotLoadIsRefused() =>
        Assert.Throws<TextureFormatException>(() => TexWriter.WriteTexture(Checkerboard(2, 2), 5));

    [RequiresGameDataFact]
    public void EveryRetailArchiveRebuildsWithTheSameContents()
    {
        IReadOnlyList<string> files = GameData.EnumerateFiles("*.res");
        Assert.NotEmpty(files);

        int rebuilt = 0;
        List<string> failures = [];

        foreach (string path in files)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (!IlffContainer.IsIlff(bytes) || IlffContainer.Read(bytes).FormType != IlffTags.Ires)
            {
                continue;
            }

            IresArchive original = IresArchive.Read(bytes);

            // Nested archives contribute their entries to the parent's namespace, so a flat rebuild of
            // the walk's output is not the same file. Those are left for the packer to copy through
            // untouched rather than silently flattened here.
            if (original.Container.Chunks.Any(chunk => chunk.IsContainer))
            {
                continue;
            }

            IresWriter.Entry[] entries = [.. original.Entries.Select(entry =>
                new IresWriter.Entry(entry.Name, entry.Kind, entry.Data))];

            IresArchive round = IresArchive.Read(IresWriter.Write(entries));

            if (round.Entries.Count != original.Entries.Count)
            {
                failures.Add($"{Path.GetFileName(path)}: {original.Entries.Count} entries in, {round.Entries.Count} out");
                continue;
            }

            for (int i = 0; i < original.Entries.Count; i++)
            {
                if (round.Entries[i].Name != original.Entries[i].Name
                    || round.Entries[i].Kind != original.Entries[i].Kind
                    || !round.Entries[i].Data.Span.SequenceEqual(original.Entries[i].Data.Span))
                {
                    failures.Add($"{Path.GetFileName(path)}: entry {i} '{original.Entries[i].Name}' differs");
                    break;
                }
            }

            rebuilt++;
        }

        Assert.Empty(failures);
        Assert.True(rebuilt > 0, "No IRES archive was rebuilt.");
    }

    [RequiresGameDataFact]
    public void RetailArchivesComeBackByteForByte()
    {
        // The strongest statement available about the layout, and it costs one comparison: rebuild the
        // game's own archive from its own entries and diff the bytes.
        //
        // Restricted to the archives that already end on their own alignment, because the FINAL chunk is
        // the one thing the retail files disagree with each other about -- 61 pad it out and 31 stop at
        // the last payload byte. Where that question does not arise, nothing at all is allowed to differ:
        // not the container alignment, not a chunk's own alignment, not one advance.
        int identical = 0;
        int skipped = 0;
        int widelyAligned = 0;
        List<string> failures = [];

        foreach (string path in GameData.EnumerateFiles("*.res"))
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (!IlffContainer.IsIlff(bytes))
            {
                continue;
            }

            IlffContainer container = IlffContainer.Read(bytes);

            if (container.FormType != IlffTags.Ires || container.Chunks.Any(chunk => chunk.IsContainer))
            {
                continue;
            }

            if (container.Size % container.Align != 0)
            {
                skipped++;
                continue;
            }

            IresArchive original = IresArchive.FromContainer(container);
            byte[] rebuilt = IresWriter.Write(
                [.. original.Entries.Select(entry => new IresWriter.Entry(entry.Name, entry.Kind, entry.Data))],
                container.Align);

            if (!rebuilt.AsSpan().SequenceEqual(bytes))
            {
                failures.Add($"{Path.GetFileName(path)}: {bytes.Length} bytes in, {rebuilt.Length} out");
                continue;
            }

            identical++;

            if (container.Align > IresWriter.DefaultAlignment)
            {
                widelyAligned++;
            }
        }

        Assert.Empty(failures);
        Assert.True(identical > 0, "No retail archive was compared byte for byte.");

        // Without this the test passes on an installation that happens to hold only 4-aligned archives,
        // and the per-chunk alignment rule goes back to being untested. One file in the game exercises
        // it, so say so rather than hoping it turns up.
        Assert.True(
            widelyAligned > 0,
            $"No archive aligned past {IresWriter.DefaultAlignment} was compared ({skipped} were skipped for "
                + "ending mid-alignment), so nothing here tested that a chunk's alignment comes from the "
                + "chunk and not from its container.");
    }

    [RequiresGameDataFact]
    public void EveryRetailTextureSurvivesTheRoundTrip()
    {
        IReadOnlyList<string> files = GameData.EnumerateFiles("*.tex");
        Assert.NotEmpty(files);

        int textures = 0;
        int atlases = 0;
        List<string> failures = [];

        foreach (string path in files)
        {
            byte[] bytes = File.ReadAllBytes(path);

            try
            {
                if (TexFile.IsTexture(bytes))
                {
                    uint version = TexFile.ReadVersion(bytes);
                    TextureImage source = TexFile.Load(bytes);
                    TextureImage round = TexFile.Load(TexWriter.WriteTexture(source, version));

                    if (!round.GetLevel(0).SequenceEqual(source.GetLevel(0)))
                    {
                        failures.Add($"{Path.GetFileName(path)}: pixels differ after a version {version} round trip");
                    }

                    textures++;
                }
                else if (TexAtlasFile.IsAtlas(bytes))
                {
                    TexAtlasFile source = TexAtlasFile.Read(bytes);
                    TexAtlasFile round = TexAtlasFile.Read(
                        TexWriter.WriteAtlas([.. source.Tiles], source.Version));

                    if (round.Tiles.Count != source.Tiles.Count)
                    {
                        failures.Add($"{Path.GetFileName(path)}: {source.Tiles.Count} tiles in, {round.Tiles.Count} out");
                        continue;
                    }

                    for (int i = 0; i < source.Tiles.Count; i++)
                    {
                        if (!round.Tiles[i].GetLevel(0).SequenceEqual(source.Tiles[i].GetLevel(0)))
                        {
                            failures.Add($"{Path.GetFileName(path)}: tile {i} differs");
                            break;
                        }
                    }

                    atlases++;
                }
            }
            catch (TextureFormatException ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
        Assert.True(textures > 0, "No loose texture was round-tripped.");
        Assert.True(atlases > 0, "No terrain atlas was round-tripped; those are the ground of every level.");
    }

    [Fact]
    public void TheIndexFindsTheSameEntriesAsTheReader()
    {
        IresWriter.Entry[] entries =
        [
            new("LOCAL:a.tex", IresEntryKind.Body, new byte[] { 1, 2, 3 }),
            new("LOCAL:b.tex", IresEntryKind.Body, new byte[] { 4, 5 }),
            new("LOCAL:dir", IresEntryKind.Path, new byte[] { 6 }),
        ];

        byte[] archive = IresWriter.Write(entries);
        using MemoryStream stream = new(archive);

        IReadOnlyList<IresIndex.Located> located = IresIndex.Read(stream);
        IresArchive read = IresArchive.Read(archive);

        Assert.Equal(read.Entries.Count, located.Count);

        for (int i = 0; i < located.Count; i++)
        {
            Assert.Equal(read.Entries[i].Name, located[i].Name);
            Assert.Equal(read.Entries[i].Kind, located[i].Kind);
            Assert.Equal(
                read.Entries[i].Data.ToArray(),
                archive.AsSpan((int)located[i].Offset, located[i].Length).ToArray());
        }
    }

    [RequiresGameDataFact]
    public void TheIndexAgreesWithTheReaderOnEveryRetailArchive()
    {
        // The index exists so the override folder can be scanned without loading a quarter-gigabyte
        // archive per level. It walks the chunk list itself rather than reusing the reader, which means
        // it is a second implementation of the same layout -- so it is worth checking the two agree
        // across the whole corpus rather than on a file made by the writer they both trust.
        int walked = 0;
        List<string> failures = [];

        foreach (string path in GameData.EnumerateFiles("*.res"))
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (!IlffContainer.IsIlff(bytes))
            {
                continue;
            }

            IlffContainer container = IlffContainer.Read(bytes);

            if (container.FormType != IlffTags.Ires || container.Chunks.Any(chunk => chunk.IsContainer))
            {
                continue;
            }

            IresArchive read = IresArchive.FromContainer(container);
            using MemoryStream stream = new(bytes);
            IReadOnlyList<IresIndex.Located> located = IresIndex.Read(stream);

            if (located.Count != read.Entries.Count)
            {
                failures.Add($"{Path.GetFileName(path)}: reader saw {read.Entries.Count}, index saw {located.Count}");
                continue;
            }

            for (int i = 0; i < located.Count; i++)
            {
                if (located[i].Name != read.Entries[i].Name
                    || located[i].Length != read.Entries[i].Data.Length
                    || !bytes.AsSpan((int)located[i].Offset, located[i].Length).SequenceEqual(read.Entries[i].Data.Span))
                {
                    failures.Add($"{Path.GetFileName(path)}: entry {i} '{read.Entries[i].Name}' differs");
                    break;
                }
            }

            walked++;
        }

        Assert.Empty(failures);
        Assert.True(walked > 0, "No retail archive was indexed.");
    }

    private static TextureImage Checkerboard(int width, int height)
    {
        Bgra32[] pixels = new Bgra32[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = (byte)(((i + (i / width)) % 2 == 0) ? 255 : 0);
            pixels[i] = new Bgra32(value, (byte)(255 - value), (byte)i, 255);
        }

        return TextureImage.FromPixels(width, height, pixels);
    }

    private static TextureImage Solid(int width, int height, byte red)
    {
        Bgra32[] pixels = new Bgra32[width * height];
        Array.Fill(pixels, new Bgra32(0, 0, red, 255));
        return TextureImage.FromPixels(width, height, pixels);
    }
}
