using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Every TGA I.G.I. 2 ships, read.</summary>
/// <remarks>
/// The sky, the water and the effect sprites are TGA rather than the engine's own <c>.tex</c>, because
/// the game hands them to D3DX rather than decoding them itself
/// (<c>Direct3DTexture_DownloadFromFile</c>, <c>igi2ed 0x662FE0</c>).
/// </remarks>
public sealed class RetailIgi2TgaTests
{
    /// <summary>Every shipped file decodes to exactly the pixels its header describes.</summary>
    /// <remarks>
    /// <para>
    /// Exact counts, because a reader that quietly produced a short or misaligned image would still
    /// return something drawable. The corpus is four shapes and no run-length: 162 uncompressed 24-bit,
    /// 31 uncompressed 32-bit, 3 colour-mapped 8-bit and 1 greyscale.
    /// </para>
    /// <para>
    /// Alpha is the interesting column. A 24-bit file has none and must come out fully opaque; a 32-bit
    /// file carries one and is where the sky's cut-outs live, so at least one has to be partly
    /// transparent or the alpha is being dropped.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_tga_decodes()
    {
        int files = 0;
        int trueColour24 = 0;
        int trueColour32 = 0;
        int mapped = 0;
        int grey = 0;
        int withTransparency = 0;
        long pixels = 0;

        foreach (string path in Directory
            .EnumerateFiles(GameDataSource.Igi2.RequireRoot(), "*.tga", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(TgaImage.IsTga(bytes), $"'{Path.GetFileName(path)}' does not look like a TGA.");

            TextureImage image = TgaImage.Read(bytes);
            files++;
            pixels += image.Pixels.Length;

            Assert.Equal(image.Width * image.Height, image.Pixels.Length);
            Assert.Equal(1, image.MipLevels);

            int depth = bytes[16];
            int type = bytes[2];
            trueColour24 += type == 2 && depth == 24 ? 1 : 0;
            trueColour32 += type == 2 && depth == 32 ? 1 : 0;
            mapped += type == 1 ? 1 : 0;
            grey += type == 3 ? 1 : 0;

            bool opaque = true;
            foreach (Bgra32 pixel in image.Pixels)
            {
                if (pixel.A != 255)
                {
                    opaque = false;
                    break;
                }
            }

            withTransparency += opaque ? 0 : 1;

            // A file without an alpha channel cannot produce anything but opaque pixels.
            if (depth == 24 || type == 3)
            {
                Assert.True(opaque, $"'{Path.GetFileName(path)}' has no alpha channel but decoded transparent.");
            }
        }

        Assert.Equal(197, files);
        Assert.Equal(162, trueColour24);
        Assert.Equal(31, trueColour32);
        Assert.Equal(3, mapped);
        Assert.Equal(1, grey);
        Assert.Equal(10_538_560L, pixels);

        // The 32-bit files are the ones with cut-outs; if none of them decoded transparent, the alpha
        // channel would be being read and thrown away.
        Assert.True(withTransparency > 0, "No shipped TGA decoded with any transparency.");
    }

    /// <summary>The textures a level's scripts name really are the files on disk beside it.</summary>
    /// <remarks>
    /// The sequel's sky and water name their textures as full paths with their own extension —
    /// <c>MISSION:textures/clouds1.tga</c> — resolving to loose files rather than archive entries. This
    /// is the check that the naming and the tree agree, on the four the level scripts name most.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_sky_and_water_textures_a_level_names_are_on_disk()
    {
        string level = Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions", "location1", "level1", "textures");
        Assert.True(Directory.Exists(level), $"'{level}' is missing.");

        foreach (string name in new[] { "clouds1.tga", "k_stars2.tga" })
        {
            string path = Path.Combine(level, name);
            Assert.True(File.Exists(path), $"'{name}' is not beside the level.");

            TextureImage image = TgaImage.Read(File.ReadAllBytes(path));
            Assert.True(image.Width > 0 && image.Height > 0);
        }
    }
}
