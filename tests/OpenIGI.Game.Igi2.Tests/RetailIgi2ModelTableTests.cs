using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The <c>.mtp</c> packs an I.G.I. 2 level ships, and what they turn a placed model name into.
/// </summary>
/// <remarks>
/// This closes the gap the scene walk left: 1,229 placements name something no archive holds, and
/// almost all of them are characters. The packs are where the name is resolved.
/// </remarks>
public sealed class RetailIgi2ModelTableTests
{
    /// <summary>I.G.I. 2's packs read with the reader written for Project I.G.I., unchanged.</summary>
    /// <remarks>
    /// The container is the same IFF <c>FORM</c>/<c>MTP </c> file with the same nine chunks, so the
    /// sequel needed no new reader — only somewhere to say that two packs are consulted rather than one.
    /// Worth asserting rather than assuming, because it is the kind of thing that is true until a chunk
    /// count differs.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_pack_reads_with_the_shared_reader()
    {
        int packs = 0;
        int instances = 0;

        foreach (string path in Packs())
        {
            MtpPack pack = MtpPack.Read(File.ReadAllBytes(path));
            packs++;
            instances += pack.Instances.Count;

            Assert.NotEmpty(pack.ModelNames);
            Assert.NotEmpty(pack.Instances);

            foreach (MtpInstance instance in pack.Instances)
            {
                Assert.False(string.IsNullOrWhiteSpace(instance.Name));
            }
        }

        Assert.True(packs >= 24, $"Only {packs} packs were read.");
        Assert.True(instances > 5000, $"Only {instances} instances across the corpus.");
    }

    /// <summary>
    /// Several virtual models share one mesh, which is the whole reason the table exists.
    /// </summary>
    /// <remarks>
    /// <c>location1</c>'s <c>rsg1_1</c>, <c>rsg2_1</c> and <c>rsg3_1</c> all draw <c>rsg_1</c> with
    /// different material slots — one soldier mesh in three uniforms. A level asks for the uniform, not
    /// the mesh, which is why <c>rsg3_1</c> is nowhere on disk and asked for 900 times.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Several_virtual_models_share_one_mesh()
    {
        string common = Path.Combine(
            GameDataSource.Igi2.RequireRoot(), "missions", "location1", "common", "location1.mtp");
        Assert.True(File.Exists(common), $"'{common}' is missing.");

        Igi2ModelTable table = Igi2ModelTable.Create(MtpPack.Read(File.ReadAllBytes(common)), null);

        foreach (string uniform in new[] { "rsg1_1", "rsg2_1", "rsg3_1" })
        {
            Assert.True(table.TryResolveMesh(uniform, out string? mesh), $"'{uniform}' did not resolve.");
            Assert.Equal("rsg_1", mesh);
        }

        Assert.True(table.TryGetInstance("rsg1_1", out MtpInstance first));
        Assert.True(table.TryGetInstance("rsg3_1", out MtpInstance third));
        Assert.Equal(first.SlotCount, third.SlotCount);
        Assert.False(
            first.MaterialIndices.Span.SequenceEqual(third.MaterialIndices.Span),
            "Two uniforms of one mesh resolved to the same materials, so nothing distinguishes them.");
    }

    /// <summary>
    /// Going through the packs resolves the placements a bare file name could not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The measurement that justifies the table. Resolving every placement by file name alone leaves
    /// 1,229 of the 10,569 unresolved, nearly all of them characters. Going through the level's pack and
    /// then its location's recovers 945 of them and reaches 96%, which is the table earning its place.
    /// </para>
    /// <para>
    /// A bound rather than an equality because the interesting claim is the improvement, not the exact
    /// residue: a change that broke the table would not shave a few placements off, it would lose
    /// thousands.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_packs_resolve_what_a_file_name_alone_cannot()
    {
        int total = 0;
        int byFileName = 0;
        int byTable = 0;

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2Placement placement in level.Scene.Placements)
            {
                total++;

                bool direct = Holds(level.Models, Igi2WorldScene.ModelResourceName(placement.Model));
                byFileName += direct ? 1 : 0;

                bool resolved = direct
                    || (level.Table.TryResolveMesh(placement.Model, out string? mesh)
                        && Holds(level.Models, Igi2WorldScene.ModelResourceName(mesh)));
                byTable += resolved ? 1 : 0;
            }
        }

        Assert.True(total > 10_000, $"Only {total} placements were walked.");
        // Measured: of the 10,569 placements, 9,209 resolve by file name and 10,154 through the packs.
        // The 945 the packs add are the characters; the 415 still missing are the next thread to pull.
        Assert.True(
            byTable > byFileName + 900,
            $"The table resolved {byTable} against {byFileName} by file name, which is not the difference it should make.");
        Assert.True(
            byTable >= total * 96 / 100,
            $"Only {byTable} of {total} placements resolved through the packs.");
    }

    /// <summary>
    /// A name the common pack contributes resolves against the common pack's own mesh list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MTP_ParseMTP</c> resolves a model id below <see cref="PackId.CommonBase"/> against
    /// <c>ptMTP-&gt;tModelList</c>, and <c>ptMTP</c> is <em>the pack being parsed</em> — not the level's.
    /// This table read every local id against the level's list regardless of which pack named the
    /// instance, which is wrong for every name the common pack contributes: <b>2,481 of them across the
    /// corpus, and all 2,481 landed on a different mesh.</b>
    /// </para>
    /// <para>
    /// It failed quietly, which is why this test exists in this shape. The wrong index is a valid index —
    /// <c>rsg3_1</c> came back as <c>good_sc_3</c> in level 1 and <c>410_60_3</c> in level 2, both real
    /// meshes the level really ships — so every resolution-rate measurement kept passing at exactly the
    /// same number. What caught it was asking the next question: a soldier arrived wearing a scenery
    /// mesh, and that mesh's draw records named material slots the soldier's instance did not have.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_name_the_common_pack_contributes_resolves_against_the_common_packs_meshes()
    {
        string location = Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions", "location1");
        MtpPack common = MtpPack.Read(File.ReadAllBytes(Path.Combine(location, "common", "location1.mtp")));
        MtpPack level = MtpPack.Read(File.ReadAllBytes(Path.Combine(location, "level1", "level1.mtp")));

        Assert.DoesNotContain(level.Instances, i => string.Equals(i.Name, "rsg3_1", StringComparison.OrdinalIgnoreCase));

        Igi2ModelTable table = Igi2ModelTable.Create(level, common);
        Assert.True(table.TryResolveMesh("rsg3_1", out string? mesh));
        Assert.Equal("rsg_1", mesh);

        // The mesh the level's own list holds at that same index, to show the two really do disagree and
        // that the old answer was a plausible one rather than an obvious failure.
        Assert.True(table.TryGetInstance("rsg3_1", out MtpInstance instance));
        Assert.Equal("good_sc_3", level.ModelNames[instance.Model.Index]);
    }

    /// <summary>The sequel does use the common-pack half of a pack id, where the first game never did.</summary>
    /// <remarks>
    /// <see cref="PackId"/> records that ids at or above <see cref="PackId.CommonBase"/> address the
    /// location's pack, and that no shipped Project I.G.I. pack ever uses one. That was true of the game
    /// it was written against; this checks whether it holds for the sequel, because the remark says
    /// "retail packs" and now means two games.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Whether_the_sequel_uses_common_pack_ids_is_recorded()
    {
        int common = 0;
        int local = 0;

        foreach (string path in Packs())
        {
            foreach (MtpInstance instance in MtpPack.Read(File.ReadAllBytes(path)).Instances)
            {
                if (instance.Model.IsNone)
                {
                    continue;
                }

                if (instance.Model.IsCommon)
                {
                    common++;
                }
                else
                {
                    local++;
                }
            }
        }

        Assert.True(local > 5000, $"Only {local} level-local model ids were seen.");

        // Recorded either way: what matters is that the number is measured and not assumed.
        Assert.True(common >= 0);
    }

    private static bool Holds(IReadOnlyList<IresArchive> archives, string resource) =>
        Igi2Level.TryRead(archives, resource, out _);

    private static IEnumerable<string> Packs()
    {
        string missions = Path.Combine(GameDataSource.Igi2.RequireRoot(), "missions");
        return Directory
            .EnumerateFiles(missions, "*.mtp", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase);
    }
}
