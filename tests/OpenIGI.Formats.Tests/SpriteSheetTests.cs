using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Sprites;
using OpenIGI.Formats.Textures;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Sprites, pictures and fonts laid out as one picture to look at.</summary>
public sealed class SpriteSheetTests
{
    /// <summary>Every sprite, picture and font both games pack composes into a picture with something drawn in it.</summary>
    [RequiresTitlesDataFact("igi1", "igi2")]
    public void Every_retail_sprite_and_font_composes()
    {
        int sprites = 0, fonts = 0;
        foreach (GameDataSource title in GameDataSource.All)
        {
            foreach (string pack in title.EnumerateFiles("*.res"))
            {
                IresArchive archive;
                try
                {
                    archive = IresArchive.Read(File.ReadAllBytes(pack));
                }
                catch (IlffFormatException)
                {
                    continue;
                }

                foreach (IresEntry entry in archive.Entries)
                {
                    string extension = Path.GetExtension(entry.Name).ToLowerInvariant();
                    if (extension is not (".spr" or ".pic" or ".fnt"))
                    {
                        continue;
                    }

                    SpriteAsset sprite = SpriteLoader.Load(entry.Data, entry.Name);

                    // Offsets are signed: IGI 2's font1 nudges some glyphs up by two pixels, which read unsigned was 65534.
                    Assert.All(sprite.Frames, frame => Assert.InRange(frame.OffsetY, -1024, 1024));
                    TextureImage sheet = SpriteSheet.Compose(sprite) ?? throw new Xunit.Sdk.XunitException($"{entry.Name} composed to nothing");
                    Assert.Contains(sheet.Pixels.ToArray(), static p => p.A > 0);
                    if (sprite.IsFont)
                    {
                        fonts++;
                        Assert.True(SpriteSheet.GlyphCount(sprite) > 26, entry.Name);
                    }
                    else
                    {
                        sprites++;
                    }
                }
            }
        }

        Assert.True(sprites > 100 && fonts > 10, $"{sprites} sprites, {fonts} fonts");
    }
}
