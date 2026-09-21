using System.Globalization;
using OpenIGI.Core.Graphics;
using OpenIGI.Formats.Textures;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>Every JPEG I.G.I. 2 ships, decoded and checked against a reference.</summary>
/// <remarks>
/// <para>
/// The ground is textured entirely from these, and <b>68 of the 83 are progressive</b> — so this is the
/// reader that decides whether I.G.I. 2's terrain has a surface at all.
/// </para>
/// <para>
/// Correctness is measured against an independent decoder rather than against the game, because the
/// game has none: it hands the bytes to D3DX. Reference decodes are produced outside the test and
/// compared pixel for pixel, which is the only way a from-scratch JPEG reader is worth trusting —
/// dimensions and non-crashing say almost nothing about whether the image is right.
/// </para>
/// </remarks>
public sealed class RetailIgi2JpegTests
{
    /// <summary>Every shipped file decodes, at its declared size.</summary>
    /// <remarks>
    /// Exact counts, and both kinds separately: a reader that quietly handled only baseline would still
    /// pass a test that merely counted files.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_shipped_jpeg_decodes()
    {
        int files = 0;
        int baseline = 0;
        int progressive = 0;
        long pixels = 0;

        foreach (string path in Files())
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(JpegImage.IsJpeg(bytes), $"'{Path.GetFileName(path)}' is not a JPEG.");

            TextureImage image = JpegImage.Read(bytes);
            files++;
            pixels += image.Pixels.Length;

            Assert.Equal(image.Width * image.Height, image.Pixels.Length);
            Assert.Equal(1, image.MipLevels);
            Assert.All(image.Pixels.ToArray(), pixel => Assert.Equal(255, pixel.A));

            if (IsProgressive(bytes))
            {
                progressive++;
            }
            else
            {
                baseline++;
            }
        }

        Assert.Equal(83, files);
        Assert.Equal(68, progressive);
        Assert.Equal(15, baseline);
        Assert.Equal(6_602_752L, pixels);
    }

    /// <summary>Every file matches an independent decoder's pixels.</summary>
    /// <remarks>
    /// <para>
    /// The test that actually says the decoder works, and the one that caught the bug that mattered: a
    /// progressive end-of-block run counted one short desynchronises a scan two bands later and shows up
    /// as an over-long Huffman code in an unrelated place.
    /// </para>
    /// <para>
    /// The reference decodes are nineteen megabytes, so they live outside the repository and this runs
    /// only when <c>IGI2_JPEG_REFERENCE</c> names them. Regenerating them takes one pass of any
    /// conforming decoder over the shipped files; the sibling fingerprint test runs always and pins the
    /// same output without the data.
    /// </para>
    /// <para>
    /// A small tolerance, because the inverse cosine transform is not specified to the last bit and two
    /// conforming decoders routinely differ by a level or two. The mean difference is what matters — a
    /// decoder that got a Huffman table, a scan order or a progressive refinement wrong does not drift
    /// by a level, it produces blocks in the wrong place or the wrong colour entirely.
    /// </para>
    /// </remarks>
    [RequiresDirectoryFact("IGI2_JPEG_REFERENCE", "reference decodes of the shipped JPEGs")]
    public void Every_file_matches_an_independent_decoder()
    {
        if (!GameDataSource.Igi2.IsAvailable)
        {
            return;
        }

        string reference = ReferenceDirectory();
        string[] manifest = File.ReadAllLines(Path.Combine(reference, "manifest.tsv"));
        Assert.Equal(83, manifest.Length);

        string root = GameDataSource.Igi2.RequireRoot();
        double worstMean = 0;
        int worstMax = 0;
        string worstFile = string.Empty;

        foreach (string line in manifest)
        {
            string[] parts = line.Split('\t');
            byte[] expected = File.ReadAllBytes(Path.Combine(reference, parts[0]));
            int width = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int height = int.Parse(parts[2], CultureInfo.InvariantCulture);

            TextureImage image = JpegImage.Read(File.ReadAllBytes(Path.Combine(root, parts[3])));
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);

            long total = 0;
            int max = 0;
            ReadOnlySpan<Bgra32> pixels = image.Pixels;
            for (int i = 0; i < pixels.Length; i++)
            {
                int dr = Math.Abs(pixels[i].R - expected[(i * 3) + 0]);
                int dg = Math.Abs(pixels[i].G - expected[(i * 3) + 1]);
                int db = Math.Abs(pixels[i].B - expected[(i * 3) + 2]);

                total += dr + dg + db;
                max = Math.Max(max, Math.Max(dr, Math.Max(dg, db)));
            }

            double mean = total / (double)(pixels.Length * 3);
            if (mean > worstMean)
            {
                worstMean = mean;
                worstMax = max;
                worstFile = parts[3];
            }
        }

        // Sub-level agreement across every file. A structural mistake would put this in the tens.
        Assert.True(
            worstMean < 1.0,
            $"'{worstFile}' differs from the reference by a mean of {worstMean:F3} per channel, worst {worstMax}.");
    }

    /// <summary>Every file decodes to exactly the pixels it decoded to when the reader was verified.</summary>
    /// <remarks>
    /// <para>
    /// The regression half, and it runs whenever the game data is present rather than only when the
    /// reference decodes are. Each line is a file, its size and a hash of its decoded pixels, taken from
    /// the reader on the day it was checked against an independent decoder pixel for pixel.
    /// </para>
    /// <para>
    /// A hash cannot say the decoder is <em>right</em> — that is the sibling test's job, and it needed
    /// nineteen megabytes to do it. What a hash says is that nothing has changed since, which is the
    /// question a suite is actually asked most of the time, and it says it in eight kilobytes.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_file_decodes_to_the_pixels_it_was_verified_with()
    {
        string root = GameDataSource.Igi2.RequireRoot();
        string[] expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "JpegFingerprints.tsv"));
        Assert.Equal(83, expected.Length);

        int checkedFiles = 0;
        foreach (string line in expected)
        {
            string[] parts = line.Split('	');
            string path = Path.Combine(root, parts[0].Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"'{parts[0]}' is not in the installation.");

            TextureImage image = JpegImage.Read(File.ReadAllBytes(path));
            Assert.Equal(int.Parse(parts[1], CultureInfo.InvariantCulture), image.Width);
            Assert.Equal(int.Parse(parts[2], CultureInfo.InvariantCulture), image.Height);
            Assert.Equal(parts[3], Fingerprint(image));
            checkedFiles++;
        }

        Assert.Equal(83, checkedFiles);
    }

    private static string Fingerprint(TextureImage image)
    {
        byte[] bytes = new byte[image.Pixels.Length * 4];
        for (int i = 0; i < image.Pixels.Length; i++)
        {
            Bgra32 pixel = image.Pixels[i];
            bytes[(i * 4) + 0] = pixel.B;
            bytes[(i * 4) + 1] = pixel.G;
            bytes[(i * 4) + 2] = pixel.R;
            bytes[(i * 4) + 3] = pixel.A;
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
    }

    /// <summary>Walks the segment chain to find the frame marker.</summary>
    /// <remarks>
    /// Segment by segment rather than scanning for the bytes: a quantisation or Huffman table can
    /// contain <c>FF C0</c> in its payload, and a scan that took the first one it saw counted one of the
    /// corpus's progressive files as baseline.
    /// </remarks>
    private static bool IsProgressive(ReadOnlySpan<byte> data)
    {
        int offset = 2;
        while (offset + 3 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            int marker = data[offset + 1];
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }

            if (marker is 0xC0 or 0xC1)
            {
                return false;
            }

            if (marker == 0xC2)
            {
                return true;
            }

            offset += 2 + System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
        }

        return false;
    }

    private static IEnumerable<string> Files() =>
        Directory
            .EnumerateFiles(GameDataSource.Igi2.RequireRoot(), "*.jpg", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase);

    private static string ReferenceDirectory() =>
        Environment.GetEnvironmentVariable("IGI2_JPEG_REFERENCE") ?? string.Empty;
}
