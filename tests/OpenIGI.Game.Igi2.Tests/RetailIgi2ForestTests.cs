using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// <see cref="ForestTask"/> and <see cref="Igi2ForestData"/> against every stand of trees the retail tree
/// ships.
/// </summary>
/// <remarks>
/// The claim worth testing is the one the format cannot make on its own: a forest's placement file is
/// named by the task's level id rather than by anything the file or a parameter says, so the only proof
/// the id is read from the right place is that the names on disk are the names the tasks generate.
/// </remarks>
public sealed class RetailIgi2ForestTests
{
    /// <summary>The placement files on disk are the ones the tasks name, on all but two counts.</summary>
    /// <remarks>
    /// <para>
    /// Both directions, deliberately. A one-way check would pass if the id were read from the wrong field
    /// and happened to land on a file that exists; matching the two sets says the level's stands and the
    /// level's files are the same collection.
    /// </para>
    /// <para>
    /// They are, 85 stands out of 86. The exceptions are shipped data, not decoding: <c>location3</c>'s
    /// <c>level2</c> holds a stand whose <c>forest_3062.dat</c> is not on disk, and its <c>level3</c>
    /// ships a <c>forest_2169.dat</c> no stand names. A task's id is the slot the level script gave it,
    /// so re-saving a level after the ids shift renames every placement file — and leaves the old one
    /// behind. At run time the dangling stand simply loads no trees: <c>Forest_UpdateInternalDataHandler</c>
    /// tests the file for existence and skips the load, so the stand is there and empty.
    /// </para>
    /// <para>
    /// A third file is unreachable for a different reason and so is invisible to this sweep:
    /// <c>multiplayer/demo</c> ships <c>forest_4085.dat</c> beside an <c>objects.qsc</c> that was never
    /// compiled, so the level has no <c>objects.qvm</c> and nothing enumerates it at all — the same way
    /// that directory's heightmap archive is orphaned. 87 files on disk, 85 of them reachable.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void The_placement_files_on_disk_are_the_ones_the_tasks_name()
    {
        int stands = 0;
        List<string> dangling = [];
        List<string> orphaned = [];

        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string directory = Path.GetDirectoryName(script)!;

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            List<string> named = [];
            foreach (ForestTask stand in TasksOf<ForestTask>(harness.LevelRoot))
            {
                stands++;
                string resource = stand.PlacementResource;
                Assert.StartsWith("MISSION:forest_", resource, StringComparison.Ordinal);
                named.Add(resource["MISSION:".Length..]);
            }

            List<string> onDisk =
            [
                .. Directory.EnumerateFiles(directory, "forest_*.dat").Select(static p => Path.GetFileName(p)!),
            ];

            dangling.AddRange(named.Where(n => !onDisk.Contains(n, StringComparer.OrdinalIgnoreCase)));
            orphaned.AddRange(onDisk.Where(n => !named.Contains(n, StringComparer.OrdinalIgnoreCase)));
        }

        Assert.Equal<string[]>(["forest_3062.dat"], [.. dangling]);
        Assert.Equal<string[]>(["forest_2169.dat"], [.. orphaned]);
        Assert.Equal(86, stands);
    }

    /// <summary>Every placement file parses, and every stand gets at least as many trees as it asks for.</summary>
    /// <remarks>
    /// The clamp in <c>Forest_LoadData</c> only ever lowers the count, so a file holding fewer records
    /// than its task declares is legal and silently shrinks the stand. Asserting it never happens in the
    /// retail tree makes the clamp a safety net rather than something the shipped data leans on.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_stand_has_at_least_the_trees_it_declares()
    {
        long declared = 0;
        long available = 0;

        foreach ((ForestTask stand, Igi2ForestData placement) in Stands())
        {
            declared += stand.TreeCount;
            available += placement.Count;

            Assert.True(stand.TreeCount > 0, $"'{stand.PlacementResource}' declares no trees.");
            Assert.True(
                placement.Count >= stand.TreeCount,
                $"'{stand.PlacementResource}' holds {placement.Count} trees for a task declaring {stand.TreeCount}.");
        }

        // The 85 reachable files. The corpus holds 58,293 trees across all 87; the difference is the two
        // that no stand of a compiled level names.
        Assert.Equal(54_537, available);
        Assert.True(declared > 0, "No stand declared any trees.");
    }

    /// <summary>Every stand names one model, and a record carries none.</summary>
    /// <remarks>
    /// A forest is one model drawn many times. Worth pinning because it is what makes a 40-byte record
    /// enough to place a tree — the corpus reuses 20-odd tree models across 86 stands.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_stand_names_one_model()
    {
        HashSet<string> models = [];

        foreach ((ForestTask stand, Igi2ForestData _) in Stands())
        {
            Assert.False(string.IsNullOrWhiteSpace(stand.Model), $"'{stand.PlacementResource}' names no model.");
            models.Add(stand.Model);
        }

        Assert.True(models.Count < 40, $"{models.Count} distinct models is more reuse than a forest implies.");
    }

    /// <summary>
    /// A tree is tilted off the vertical exactly when its stand is set to normalise trees to the ground.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strongest claim available here, because it joins two things read from different files. The
    /// third angle is the turn about the vertical that every tree gets; the first two are a tilt, and they
    /// are non-zero on precisely the nine stands whose <c>Normalize objects to ground</c> is set — 14,675
    /// trees, a quarter of the corpus, with no stand disagreeing in either direction.
    /// </para>
    /// <para>
    /// Reading the angle triple in the wrong order, or reading the flag from the wrong parameter slot,
    /// breaks the correlation immediately. It would take a coincidence across 85 stands to survive.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Trees_are_tilted_exactly_when_their_stand_normalises_to_the_ground()
    {
        int normalised = 0;
        bool sawRotation = false;
        bool sawAnisotropic = false;

        foreach ((ForestTask stand, Igi2ForestData placement) in Stands())
        {
            bool tilted = false;

            foreach (Igi2ForestTree tree in placement.Trees())
            {
                tilted |= Math.Abs(tree.Angles.X) > 1e-5f || Math.Abs(tree.Angles.Y) > 1e-5f;
                sawRotation |= tree.Angles.Z > 0.01f;

                Assert.InRange(tree.Scale.X, 0.1f, 10f);
                Assert.InRange(tree.Scale.Y, 0.1f, 10f);
                Assert.InRange(tree.Scale.Z, 0.1f, 10f);
                sawAnisotropic |= tree.Scale.X != tree.Scale.Y || tree.Scale.Y != tree.Scale.Z;
            }

            Assert.True(
                tilted == stand.NormalizeToGround,
                $"'{stand.PlacementResource}' is tilted={tilted} but declares NormalizeToGround={stand.NormalizeToGround}.");

            normalised += stand.NormalizeToGround ? 1 : 0;
        }

        Assert.Equal(9, normalised);
        Assert.True(sawRotation, "Every tree was unrotated, so the angle triple proves nothing.");
        Assert.True(sawAnisotropic, "Every tree was uniformly scaled, so the scale triple proves nothing.");
    }

    /// <summary>Sixty-six shipped trees carry uninitialised memory where their baked light should be.</summary>
    /// <remarks>
    /// <para>
    /// Every light in the corpus has a zero fourth byte except 66, which are <c>0xABABABAB</c> exactly —
    /// the fill a Windows debug heap leaves in a block nobody wrote. Those trees were saved before
    /// <c>Calculate light</c> ever ran over them.
    /// </para>
    /// <para>
    /// Harmless, and the reason is the point: <c>ForestRender_Draw</c> (<c>igi2ed 0x57CA90</c>) draws
    /// foliage with the stored dword under <c>| 0xFF000000</c> and the trunk with the average of the low
    /// three bytes, so the fourth byte never reaches the renderer and <c>0xABABABAB</c> shows up as a
    /// neutral grey rather than as a transparent tree. That is also the evidence for reading the field as
    /// 24 bits of colour rather than as a colour with alpha.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Only_a_handful_of_trees_carry_an_unbaked_light()
    {
        int unbaked = 0;
        long total = 0;

        foreach ((ForestTask _, Igi2ForestData placement) in Stands())
        {
            foreach (Igi2ForestTree tree in placement.Trees())
            {
                total++;
                if (tree.Light.A == 0)
                {
                    continue;
                }

                unbaked++;
                Assert.Equal(new Bgra32(0xAB, 0xAB, 0xAB, 0xAB), tree.Light);
            }
        }

        Assert.Equal(54_537, total);
        Assert.Equal(66, unbaked);
    }

    /// <summary>Every stand whose file exists, paired with it.</summary>
    /// <remarks>
    /// The one dangling stand is skipped rather than made to throw: the original skips it too, and a
    /// helper that refused to enumerate would make every test here fail for one level's stale filename.
    /// <see cref="The_placement_files_on_disk_are_the_ones_the_tasks_name"/> is what holds that fact.
    /// </remarks>
    private static IEnumerable<(ForestTask Stand, Igi2ForestData Placement)> Stands()
    {
        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string directory = Path.GetDirectoryName(script)!;

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            foreach (ForestTask stand in TasksOf<ForestTask>(harness.LevelRoot))
            {
                string path = Path.Combine(directory, stand.PlacementResource["MISSION:".Length..]);
                if (File.Exists(path))
                {
                    yield return (stand, Igi2ForestData.Read(File.ReadAllBytes(path)));
                }
            }
        }
    }

    private static IEnumerable<T> TasksOf<T>(GameTask root)
        where T : GameTask
    {
        foreach (GameTask child in root.Children)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in TasksOf<T>(child))
            {
                yield return nested;
            }
        }
    }
}
