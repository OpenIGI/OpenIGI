using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The container a generated pack writes, and the three loaders that have to accept it.
/// </summary>
/// <remarks>
/// A pack picture exists to stand in for any of the game's four picture containers, so most of what is
/// worth testing is not the layout — it is that <see cref="TexFile"/>, <see cref="TexAtlasFile"/> and
/// <see cref="SpriteLoader"/> each take one and hand back what their own callers expect. A container
/// only one of the three accepts replaces only some of the art, and which art goes wrong depends on
/// which loader the game happened to route that asset through.
/// </remarks>
public sealed class PackPictureTests
{
    [Fact]
    public void OneFrameComesBackPixelForPixel()
    {
        TextureImage source = Ramp(9, 5);

        PackPicture.Content content = PackPicture.Read(
            PackPicture.Write(Single(source, 9, 5), buildMips: false));

        Assert.Single(content.Frames);
        Assert.Equal(source.GetLevel(0).ToArray(), content.Frames[0].Image.GetLevel(0).ToArray());
    }

    [Fact]
    public void TheLogicalSizeIsKeptSeparateFromThePixels()
    {
        // The reason the field exists. A four-times picture has to keep answering with the size the
        // layout was built around, or a sharper menu is a differently shaped menu.
        PackPicture.Content content = PackPicture.Read(
            PackPicture.Write(
                new PackPicture.Content([new PackPicture.Frame(Ramp(64, 32), 16, 8)], 16, 8, 0, 0, default),
                buildMips: false));

        Assert.Equal(64, content.Frames[0].Image.Width);
        Assert.Equal(16, content.Frames[0].LogicalWidth);
        Assert.Equal(8, content.Frames[0].LogicalHeight);
        Assert.Equal(16, content.LogicalWidth);
    }

    [Fact]
    public void FramesKeepTheirOrder()
    {
        // Terrain names its ground by index, so a reordered atlas repaints a level and nothing notices.
        PackPicture.Frame[] frames =
        [
            new(Solid(4, 4, 10), 4, 4),
            new(Solid(4, 4, 20), 4, 4),
            new(Solid(4, 4, 30), 4, 4),
        ];

        PackPicture.Content content = PackPicture.Read(
            PackPicture.Write(new PackPicture.Content(frames, 4, 4, 0, 0, default), buildMips: false));

        Assert.Equal(10, content.Frames[0].Image.GetLevel(0)[0].R);
        Assert.Equal(20, content.Frames[1].Image.GetLevel(0)[0].R);
        Assert.Equal(30, content.Frames[2].Image.GetLevel(0)[0].R);
    }

    [Fact]
    public void ATileGridSurvives()
    {
        // What reassembles a 640x480 loading screen out of 150 frames of 64x32. Cells are one-based,
        // and an off-by-one here scrambles the picture rather than failing.
        byte[] cells = [1, 2, 3, 4, 0, 1];

        PackPicture.Content content = PackPicture.Read(
            PackPicture.Write(
                new PackPicture.Content(
                    [new(Solid(2, 2, 1), 2, 2), new(Solid(2, 2, 2), 2, 2), new(Solid(2, 2, 3), 2, 2), new(Solid(2, 2, 4), 2, 2)],
                    6,
                    4,
                    3,
                    2,
                    cells),
                buildMips: false));

        Assert.Equal(3, content.GridColumns);
        Assert.Equal(2, content.GridRows);
        Assert.Equal(cells, content.GridCells.ToArray());
    }

    [Fact]
    public void AGridThatDoesNotMatchItsCellsIsRefused() =>
        Assert.Throws<TextureFormatException>(() => PackPicture.Write(
            new PackPicture.Content([new(Solid(2, 2, 1), 2, 2)], 2, 2, 4, 4, new byte[3])));

    [Fact]
    public void AMipChainIsBuiltWhenAsked()
    {
        // Mips are a third again in size and are pure arithmetic over pixels already in the file, so the
        // container asks for them rather than carrying them. Callers downstream expect a chain, so the
        // flag has to actually produce one.
        PackPicture.Content built = PackPicture.Read(PackPicture.Write(Single(Ramp(16, 16), 16, 16)));
        PackPicture.Content flat = PackPicture.Read(
            PackPicture.Write(Single(Ramp(16, 16), 16, 16), buildMips: false));

        Assert.True(built.Frames[0].Image.MipLevels > 1);
        Assert.Equal(1, flat.Frames[0].Image.MipLevels);
    }

    [Fact]
    public void TheTextureLoaderTakesOne()
    {
        byte[] file = PackPicture.Write(Single(Ramp(8, 8), 8, 8), buildMips: false);

        Assert.True(TexFile.IsTexture(file));
        Assert.Equal(Ramp(8, 8).GetLevel(0).ToArray(), TexFile.Load(file).GetLevel(0).ToArray());
    }

    [Fact]
    public void TheAtlasLoaderTakesOne()
    {
        byte[] file = PackPicture.Write(
            new PackPicture.Content([new(Solid(4, 4, 7), 4, 4), new(Solid(4, 4, 8), 4, 4)], 4, 4, 0, 0, default),
            buildMips: false);

        Assert.True(TexAtlasFile.IsAtlas(file));

        TexAtlasFile atlas = TexAtlasFile.Read(file);

        Assert.Equal(2, atlas.Tiles.Count);
        Assert.Equal(8, atlas.Tiles[1].GetLevel(0)[0].R);
    }

    [Fact]
    public void TheSpriteLoaderTakesOneAndReportsLogicalSizes()
    {
        byte[] file = PackPicture.Write(
            new PackPicture.Content(
                [new(Solid(64, 64, 1), 16, 16, Advance: 20, OffsetX: 3, OffsetY: -4)],
                16,
                16,
                0,
                0,
                default),
            buildMips: false);

        SpriteAsset sprite = SpriteLoader.Load(file, "pack.spr");

        Assert.Single(sprite.Frames);
        Assert.Equal(16, sprite.Frames[0].Width);
        Assert.Equal(20, sprite.Frames[0].Advance);
        Assert.Equal(3, sprite.Frames[0].OffsetX);
        Assert.Equal(-4, sprite.Frames[0].OffsetY);
        Assert.Equal(16, sprite.DefaultWidth);

        // The pixels are still the big ones; only what layout asks for was rescaled.
        Assert.Equal(64, sprite.GetTexture(sprite.DefaultFrame).Width);
    }

    [Fact]
    public void TheSpriteLoaderRebuildsTheTileGrid()
    {
        byte[] file = PackPicture.Write(
            new PackPicture.Content(
                [new(Solid(8, 8, 1), 2, 2), new(Solid(8, 8, 2), 2, 2)],
                4,
                2,
                2,
                1,
                new byte[] { 2, 1 }),
            buildMips: false);

        SpriteAsset sprite = SpriteLoader.Load(file, "pack.pic");

        Assert.NotNull(sprite.TileLayout);
        Assert.Equal(2, sprite.TileLayout!.Columns);
        Assert.Equal(1, sprite.TileLayout.GetFrameIndex(0, 0));
        Assert.Equal(0, sprite.TileLayout.GetFrameIndex(1, 0));
    }

    [Fact]
    public void NothingInARetailInstallationIsAPackPicture()
    {
        // The container borrows the game's magic, so it is worth stating that the version is free. If a
        // retail file ever answered to this, every loader above would route it here and decode nothing.
        Assert.False(PackPicture.Is([]));
        Assert.False(PackPicture.Is("LOOP"u8));
    }

    private static PackPicture.Content Single(TextureImage image, int width, int height) =>
        new([new PackPicture.Frame(image, width, height)], width, height, 0, 0, default);

    private static TextureImage Ramp(int width, int height)
    {
        Bgra32[] pixels = new Bgra32[width * height];

        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Bgra32((byte)i, (byte)(i * 3), (byte)(i * 7), (byte)(255 - (i % 256)));
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
