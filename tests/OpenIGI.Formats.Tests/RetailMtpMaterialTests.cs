using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The <c>GTT</c> table both games ship, and why resolving a material through it changes no retail answer.
/// </summary>
/// <remarks>
/// <para>
/// <c>MTP_ParseMTP</c> (<c>igi2.exe 0x4F5DE0</c>) reads a material slot as a <c>GTT</c> index and takes
/// both halves from <c>gtt[2 * slot]</c> and <c>gtt[2 * slot + 1]</c>.
/// <see cref="MtpPack.TryGetMaterial"/> used to take the slot as a <c>TEXF</c> index directly while still
/// indexing <c>GTT</c> for the second half — self-inconsistent, and contradicted by the binary.
/// </para>
/// <para>
/// It produced the right answer anyway, and these tests are why: in every pack either game ships, the
/// texture half of <c>GTT</c> is the identity mapping. Correcting the read was therefore free, and this
/// keeps that a measurement rather than something the correction assumed.
/// </para>
/// </remarks>
public sealed class RetailMtpMaterialTests
{
    /// <summary>Project I.G.I.'s 17 packs map every slot to the texture of the same index.</summary>
    [RequiresGameDataFact]
    public void The_first_games_material_table_is_the_identity_mapping() =>
        AssertIdentity(GameDataSource.Igi1, expectedPacks: 17, expectedEntries: 7187);

    /// <summary>I.G.I. 2's 33 packs do the same.</summary>
    /// <remarks>
    /// Measured rather than inherited. The sequel ships roughly twice the packs and a quarter fewer
    /// entries, and nothing about the first game's answer would have implied this one.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_sequels_material_table_is_the_identity_mapping() =>
        AssertIdentity(GameDataSource.Igi2, expectedPacks: 33, expectedEntries: 5336);

    /// <summary>
    /// Asserts that every <c>GTT</c> entry in a corpus names the texture of its own index and no palette.
    /// </summary>
    /// <param name="source">The installation to walk.</param>
    /// <param name="expectedPacks">How many packs it holds.</param>
    /// <param name="expectedEntries">How many material entries those packs hold between them.</param>
    /// <remarks>
    /// <para>
    /// Exact counts rather than bounds. The claim is about a fixed corpus that cannot grow, so a bound
    /// would let a reader that silently dropped packs or entries keep passing — which is the failure this
    /// exists to catch.
    /// </para>
    /// <para>
    /// The palette half is <see cref="PackId.None"/> throughout both corpora. The field is read and
    /// carried because the format has it; nothing consults it, and a renderer that palettised a surface
    /// on retail data would be inventing behaviour.
    /// </para>
    /// </remarks>
    private static void AssertIdentity(GameDataSource source, int expectedPacks, int expectedEntries)
    {
        int packs = 0;
        int entries = 0;

        foreach (string path in Directory
            .EnumerateFiles(source.RequireRoot(), "*.mtp", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            MtpPack pack = MtpPack.Read(File.ReadAllBytes(path));
            packs++;

            // MTP_ParseMTP gives GTT and TEXF the same id space by construction; every retail pack gives
            // them the same length too, which is what makes an identity mapping expressible at all.
            Assert.Equal(pack.TextureNames.Count, pack.Materials.Count);

            for (int i = 0; i < pack.Materials.Count; i++)
            {
                entries++;
                MtpMaterial material = pack.Materials[i];
                string fileName = Path.GetFileName(path);

                Assert.False(material.Texture.IsNone, $"{fileName} material {i} names no texture.");
                Assert.False(material.Texture.IsCommon, $"{fileName} material {i} is {material.Texture}.");
                Assert.Equal(i, material.Texture.Index);
                Assert.True(material.Palette.IsNone, $"{fileName} material {i} carries palette {material.Palette}.");
            }
        }

        Assert.Equal(expectedPacks, packs);
        Assert.Equal(expectedEntries, entries);
    }
}
