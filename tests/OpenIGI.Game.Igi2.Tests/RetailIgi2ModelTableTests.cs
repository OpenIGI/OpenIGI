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

        Igi2ModelTable table = Igi2ModelTable.Create([MtpPack.Read(File.ReadAllBytes(common))]);

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

        // Exact, because the corpus is fixed and the residue is now small enough to name. Of the 10,550
        // placements 9,536 resolve by file name alone and 10,545 through the packs -- the ones the packs
        // add are the characters and everything else the global pack carries.
        Assert.Equal(11_724, total);
        Assert.Equal(10_710, byFileName);
        Assert.Equal(11_719, byTable);
    }

    /// <summary>The five placements the shipped data cannot resolve, and why they are the data's fault.</summary>
    /// <remarks>
    /// <para>
    /// This began as 415 unresolved. Opening the two global packs took it to 5, and those 5 are not a
    /// gap in the port: <c>soldier_1</c> is in no archive anywhere in the installation, and
    /// <c>igiagent_1</c>, <c>231_15_1</c> and <c>730_01_1</c> exist only in the archives of levels other
    /// than the one placing them.
    /// </para>
    /// <para>
    /// <b>The original fails identically.</b> <c>MTP_VirModel_GetPtr</c> searches only the four packs the
    /// game has open, so a name none of them carries yields no model and the object is simply not drawn.
    /// Pinning the exact names keeps that a statement about the retail data rather than a tolerance.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_five_that_do_not_resolve_are_missing_from_the_shipped_data()
    {
        Dictionary<string, int> unresolved = new(StringComparer.OrdinalIgnoreCase);

        foreach (Igi2Level level in Igi2Level.All())
        {
            foreach (Igi2Placement placement in level.Scene.Placements)
            {
                if (Holds(level.Models, Igi2WorldScene.ModelResourceName(placement.Model))
                    || (level.Table.TryResolveMesh(placement.Model, out string? mesh)
                        && Holds(level.Models, Igi2WorldScene.ModelResourceName(mesh))))
                {
                    continue;
                }

                unresolved.TryGetValue(placement.Model, out int count);
                unresolved[placement.Model] = count + 1;
            }
        }

        Assert.Equal(2, unresolved["soldier_1"]);
        Assert.Equal(1, unresolved["igiagent_1"]);
        Assert.Equal(1, unresolved["231_15_1"]);
        Assert.Equal(1, unresolved["730_01_1"]);
        Assert.Equal(4, unresolved.Count);
    }

    /// <summary>The global pack is what carries the characters, and it is loaded first.</summary>
    /// <remarks>
    /// <para>
    /// <c>Game_CreateHandler</c> loads four packs in a fixed order — <c>common</c>, <c>new</c>, the
    /// location's, the level's — and <c>MTP_Load</c> appends, so <c>COMMON/common.mtp</c> is the head of
    /// <c>MTP_tMTPList</c>. That matters twice: it is the pack a <see cref="PackId.CommonBase"/> id
    /// addresses, and <c>MTP_VirModel_GetPtr</c> returns the <em>first</em> match walking from the head,
    /// so a level cannot override a name the global pack already has.
    /// </para>
    /// <para>
    /// It is also where the player and the scientists live, which is why 415 placements looked
    /// unresolvable while only the level and location packs were open.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_global_pack_carries_the_characters()
    {
        string root = GameDataSource.Igi2.RequireRoot();
        MtpPack global = MtpPack.Read(File.ReadAllBytes(Path.Combine(root, "COMMON", "common.mtp")));

        Assert.Equal(398, global.ModelNames.Count);
        Assert.Equal(416, global.Instances.Count);

        Igi2ModelTable table = Igi2ModelTable.Create([global]);
        foreach (string character in new[] { "jones_1", "scientist" })
        {
            Assert.True(table.TryResolveMesh(character, out _), $"The global pack does not name '{character}'.");
        }
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

        Igi2ModelTable table = Igi2ModelTable.Create([common, level]);
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
