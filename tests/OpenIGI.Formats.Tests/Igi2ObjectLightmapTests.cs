using System.Buffers.Binary;
using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// I.G.I. 2's object lightmaps: the same shape as I.G.I. 1's with a wider descriptor, and a different
/// naming scheme.
/// </summary>
public sealed class Igi2ObjectLightmapTests
{
    /// <summary>The descriptor stride is what the two games disagree about, and only that.</summary>
    [Fact]
    public void The_profiles_differ_only_in_the_descriptor_stride()
    {
        Assert.Equal(44, AssetFormatProfile.Igi1.ObjectLightmapDescriptorStride);
        Assert.Equal(52, AssetFormatProfile.Igi2.ObjectLightmapDescriptorStride);
        Assert.Equal(ObjectLightmap.DescriptorSize, AssetFormatProfile.Igi1.ObjectLightmapDescriptorStride);
    }

    /// <summary>An entry built to the sequel's stride decodes under the sequel's profile.</summary>
    [Fact]
    public void A_wider_descriptor_decodes_under_the_igi2_profile()
    {
        byte[] entry = Build(52, [(3, 2), (6, 5)]);

        ObjectLightmap lightmap = ObjectLightmap.Read(entry, AssetFormatProfile.Igi2);

        Assert.Equal(2, lightmap.Images.Count);
        Assert.Equal(3, lightmap.Images[0].Width);
        Assert.Equal(2, lightmap.Images[0].Height);
        Assert.Equal(3 * 2 * 4, lightmap.Images[0].Pixels.Length);
        Assert.Equal(6, lightmap.Images[1].Width);
        Assert.Equal(5, lightmap.Images[1].Height);
    }

    /// <summary>The same entry read at the first game's stride is refused, not silently mis-decoded.</summary>
    /// <remarks>
    /// The stride decides where the pixel blocks begin, so a wrong one does not degrade the image — it
    /// starts the first block eight bytes early and every image after it is noise. The closing
    /// arithmetic is what turns that into a refusal.
    /// </remarks>
    [Fact]
    public void The_wrong_stride_is_refused_rather_than_mis_decoded()
    {
        byte[] entry = Build(52, [(3, 2), (6, 5)]);

        Assert.Throws<PackFormatException>(() => ObjectLightmap.Read(entry, AssetFormatProfile.Igi1));
    }

    /// <summary>Neither game is the default, because there is no default.</summary>
    /// <remarks>
    /// This used to assert that a caller who said nothing got the first game. It does not any more, and
    /// the reason is the bug that change fixed: the shared world renderer said nothing, so every I.G.I. 2
    /// level drew unlit while its lightmap archive sat there readable. A stride cannot be guessed from
    /// the bytes -- both readings parse -- so the caller has to know, and now it has to say.
    /// </remarks>
    [Fact]
    public void A_caller_has_to_name_the_title_it_is_reading()
    {
        byte[] entry = Build(44, [(4, 4)]);

        ObjectLightmap lightmap = ObjectLightmap.Read(entry, AssetFormatProfile.Igi1);

        Assert.Single(lightmap.Images);
        Assert.Equal(4, lightmap.Images[0].Width);
        Assert.Throws<ArgumentNullException>(() => ObjectLightmap.Read(entry, null!));
    }

    /// <summary>An I.G.I. 2 entry name splits into a task id, a model index and a level of detail.</summary>
    /// <param name="name">The entry name.</param>
    /// <param name="taskId">The task id it should yield.</param>
    /// <param name="variant">The group letter as a number, zero when there is none.</param>
    /// <param name="modelIndex">The model index it should yield.</param>
    /// <param name="lod">The level of detail it should yield.</param>
    [Theory]
    [InlineData("4010_0000_1.olm", 4010, 0, 0, 1)]
    [InlineData("LOCAL:lightmaps/3724_0012_5.olm", 3724, 0, 12, 5)]
    [InlineData("0112a_0003_2.olm", 112, 1, 3, 2)]
    [InlineData("0112e_0003_2.olm", 112, 5, 3, 2)]
    public void An_igi2_name_splits_into_its_four_parts(string name, int taskId, int variant, int modelIndex, int lod)
    {
        Assert.True(ObjectLightmap.TryParseIgi2Name(name, out int id, out int group, out int model, out int detail));

        Assert.Equal(taskId, id);
        Assert.Equal(variant, group);
        Assert.Equal(modelIndex, model);
        Assert.Equal(lod, detail);
    }

    /// <summary>
    /// The first game's parser reads a sequel name without complaint, and gets the wrong number.
    /// </summary>
    /// <remarks>
    /// Pinned because it is the trap: <c>TryParseName</c> splits on the last underscore, so
    /// <c>4010_0000_1</c> yields a draw-record index of 1 that is really a level of detail. Nothing about
    /// that fails at the call site, so the separate parser has to be the visible reason it exists.
    /// </remarks>
    [Fact]
    public void The_igi1_parser_misreads_an_igi2_name_without_failing()
    {
        Assert.True(ObjectLightmap.TryParseName("4010_0007_3.olm", out string objectName, out int recordIndex));

        Assert.Equal("4010_0007", objectName);
        Assert.Equal(3, recordIndex);

        // What it actually is.
        Assert.True(ObjectLightmap.TryParseIgi2Name("4010_0007_3.olm", out int taskId, out _, out int model, out int lod));
        Assert.Equal(4010, taskId);
        Assert.Equal(7, model);
        Assert.Equal(3, lod);
    }

    /// <summary>Names that are not of the sequel's shape are rejected.</summary>
    /// <param name="name">The name to reject.</param>
    [Theory]
    [InlineData("obj00014_00003.olm")]
    [InlineData("4010_0000.olm")]
    [InlineData("4010_zzzz_1.olm")]
    [InlineData("4010!_0000_1.olm")]
    public void A_name_of_another_shape_is_rejected(string name) =>
        Assert.False(ObjectLightmap.TryParseIgi2Name(name, out _, out _, out _, out _));

    /// <summary>Every object lightmap in the retail tree decodes under the I.G.I. 2 profile.</summary>
    /// <remarks>
    /// The whole corpus rather than a sample, because the closing arithmetic makes each entry a check on
    /// the stride: 60 bytes of prefix, one descriptor each, then four bytes a texel, landing exactly on
    /// the end of the entry.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_lightmap_decodes()
    {
        int entries = 0;
        int images = 0;
        long texels = 0;
        int multiImage = 0;

        foreach (string path in Archives())
        {
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));

            foreach (IresEntry entry in archive.Entries)
            {
                Assert.EndsWith(ObjectLightmap.Extension, entry.Name, StringComparison.OrdinalIgnoreCase);

                ObjectLightmap lightmap = ObjectLightmap.Read(entry.Data, AssetFormatProfile.Igi2);
                entries++;
                images += lightmap.Images.Count;
                multiImage += lightmap.Images.Count > 1 ? 1 : 0;

                foreach (ObjectLightmapImage image in lightmap.Images)
                {
                    Assert.True(image.Width > 0 && image.Height > 0, $"'{entry.Name}' has an empty image.");
                    Assert.Equal(image.Width * image.Height * 4, image.Pixels.Length);
                    texels += image.Width * image.Height;
                }
            }
        }

        Assert.Equal(31_542, entries);
        Assert.Equal(31_546, images);
        Assert.Equal(2, multiImage);
        Assert.True(texels > 0);
    }

    /// <summary>
    /// Every entry name parses, and its parts stay inside the ranges the writer's format strings allow.
    /// </summary>
    /// <remarks>
    /// The level of detail is the tell: it is written with <c>%1d</c> and counted from one, so a name
    /// carrying a zero or a two-digit value would mean the parts had been split in the wrong places.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_name_parses_into_plausible_parts()
    {
        int lettered = 0;
        HashSet<int> lods = [];

        foreach (string path in Archives())
        {
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));

            foreach (IresEntry entry in archive.Entries)
            {
                Assert.True(
                    ObjectLightmap.TryParseIgi2Name(entry.Name, out int taskId, out int variant, out int model, out int lod),
                    $"'{entry.Name}' does not parse.");

                Assert.InRange(taskId, 0, 4095);
                Assert.InRange(variant, 0, ObjectLightmap.VariantLetters.Length - 1);
                Assert.InRange(model, 0, 9999);
                Assert.InRange(lod, 1, 9);

                lods.Add(lod);
                lettered += variant > 0 ? 1 : 0;
            }
        }

        Assert.Equal<int[]>([1, 2, 3, 4, 5], [.. lods.Order()]);
        Assert.Equal(746, lettered);
    }

    /// <summary>Every retail lightmap declares the two version words the writer stamps.</summary>
    /// <remarks>
    /// The first is <c>0.12</c>, the same as I.G.I. 1's, which is why a wrong stride is not caught by a
    /// version check. The second is <c>0.16</c> — the sequel's mesh version, since a lightmap belongs to
    /// a model — and it is the field that actually separates the two games' entries.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_retail_lightmap_carries_the_same_version_pair()
    {
        foreach (string path in Archives())
        {
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));

            foreach (IresEntry entry in archive.Entries)
            {
                ReadOnlySpan<byte> span = entry.Data.Span;

                Assert.Equal(0.12f, BinaryPrimitives.ReadSingleLittleEndian(span), 4);
                Assert.Equal(0.16f, BinaryPrimitives.ReadSingleLittleEndian(span[4..]), 4);
            }
        }
    }

    private static IReadOnlyList<string> Archives()
    {
        string root = Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions");
        return
        [
            .. Directory
                .EnumerateFiles(root, "lightmaps.res", SearchOption.AllDirectories)
                .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Builds an entry the way the writer does: prefix, descriptors, then the pixel blocks.</summary>
    private static byte[] Build(int descriptorStride, (int Width, int Height)[] images)
    {
        int pixels = images.Sum(static i => i.Width * i.Height * 4);
        byte[] entry = new byte[ObjectLightmap.PrefixSize + (images.Length * descriptorStride) + pixels];

        BinaryPrimitives.WriteSingleLittleEndian(entry, 0.12f);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(44), images.Length);

        for (int i = 0; i < images.Length; i++)
        {
            // The dimensions are the descriptor's last dword in both games.
            int dims = ObjectLightmap.PrefixSize + (i * descriptorStride) + descriptorStride - 4;
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(dims), (ushort)images[i].Width);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(dims + 2), (ushort)images[i].Height);
        }

        return entry;
    }
}
