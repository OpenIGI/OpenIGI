using OpenIGI.Formats.Animation;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// The human rig, as every shipped skeleton and every skinned body actually holds it.
/// </summary>
/// <remarks>
/// This is the corpus evidence that the hit-zone table's thirty entries are the rig's thirty-three bones
/// with the three-bone weapon chain taken out.
/// </remarks>
public sealed class RetailHumanRigTests
{
    /// <summary>The hierarchy every human skeleton shares, out of <c>000.iff</c>'s own <c>PLST</c>.</summary>
    private static readonly int[] HumanParents =
    [
        -1, 0, 1, 2, 3, 4, 4, 6, 3, 8, 9, 10, 11, 12, 3, 14, 15, 16, 17, 18,
        1, 20, 21, 0, 23, 24, 25, 26, 0, 28, 29, 30, 31,
    ];

    private static readonly string[] UnusedPacks = ["common.mtp", "menusystem.mtp"];

    /// <summary>Every human skeleton in the game is the same thirty-three-bone hierarchy.</summary>
    /// <remarks>
    /// Five of the six shipped sets — <c>000</c>, <c>001</c>, <c>002</c>, <c>003</c> and <c>006</c>.
    /// <c>005</c> is the first-person view rig and is a different thing entirely at fifty-six bones. It
    /// is what lets one hit-zone table serve every human in the game.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryHumanSkeletonIsTheSameHierarchy()
    {
        int checkedSets = 0;

        foreach (BoneSkeleton skeleton in Skeletons())
        {
            if (skeleton.Id == 5)
            {
                Assert.Equal(56, skeleton.BoneCount);
                continue;
            }

            Assert.Equal(HumanParents.Length, skeleton.BoneCount);
            Assert.Equal(HumanParents, skeleton.Parents.ToArray());
            checkedSets++;
        }

        Assert.Equal(5, checkedSets);
    }

    /// <summary>
    /// The rig's limbs group as 4, 4, 6, 6, 3, 5, 5 — and taking out the three-bone weapon chain leaves
    /// 4, 4, 6, 6, 5, 5, which is the hit-zone table's grouping exactly.
    /// </summary>
    [RequiresGameDataFact]
    public void TheLimbGroupingIsTheZoneTablesGroupingOnceTheWeaponChainIsRemoved()
    {
        // Each chain, by its root's parent: spine off the root, neck and arms off the spine top, the
        // weapon chain off spine bone 1, and both legs off the root.
        Assert.Equal([-1, 0, 1, 2], Parents(0, 4));      // pelvis and spine
        Assert.Equal([3, 4, 4, 6], Parents(4, 4));       // neck and head
        Assert.Equal([3, 8, 9, 10, 11, 12], Parents(8, 6));    // one arm
        Assert.Equal([3, 14, 15, 16, 17, 18], Parents(14, 6)); // the other
        Assert.Equal([1, 20, 21], Parents(20, 3));       // the weapon chain, off spine bone 1
        Assert.Equal([0, 23, 24, 25, 26], Parents(23, 5));     // one leg
        Assert.Equal([0, 28, 29, 30, 31], Parents(28, 5));     // the other

        Assert.Equal(30, HumanParents.Length - 3);
    }

    /// <summary>
    /// <b>Nothing in the game has geometry on the two bones that reach out in front of the body.</b>
    /// Of 535 skinned bodies on the thirty-three-bone rig, not one references bone 21 or bone 22, and
    /// only one model family references bone 20 — the chain's root, which sits at the sternum.
    /// </summary>
    /// <remarks>
    /// This is what lets a body-part numbering leave the weapon chain out while the rig still has it.
    /// The leaf bones — 5, 7, 13, 19, 27 and 32, the fingertips, toes and second head branch — are
    /// unreferenced too, but those are still body parts; the weapon chain is the only run whose removal
    /// makes the zone table's groups line up.
    /// </remarks>
    [RequiresGameDataFact]
    public void NothingHasGeometryOnTheWeaponChainsForwardBones()
    {
        SortedSet<int> referenced = [];
        int bodies = 0;
        int touchingTheChainRoot = 0;

        foreach (MefModel model in Models())
        {
            if (model.Format != MefMeshFormat.Skinned || model.SkinInfluences.Length == 0)
            {
                continue;
            }

            SortedSet<int> bones = [];
            foreach (MefSkinInfluence influence in model.SkinInfluences)
            {
                bones.Add(influence.BoneIndex);
            }

            // Only bodies on the 33-bone rig; the view arms reach bone 48 and are a different skeleton.
            if (bones.Max > 32)
            {
                continue;
            }

            bodies++;
            referenced.UnionWith(bones);
            if (bones.Contains(20))
            {
                touchingTheChainRoot++;
            }
        }

        Assert.Equal(535, bodies);
        Assert.DoesNotContain(21, referenced);
        Assert.DoesNotContain(22, referenced);
        Assert.Equal(6, touchingTheChainRoot);

        // And the rest of the rig is used, so the absence is about those bones and not about sparseness.
        Assert.Contains(0, referenced);
        Assert.Contains(18, referenced);
        Assert.Contains(23, referenced);
        Assert.Contains(31, referenced);

        // The rig's leaves carry no vertices either, so "unreferenced" alone does not make a bone a
        // non-body-part -- which is why the argument for the weapon chain rests on the zone table's
        // grouping and not on this.
        Assert.DoesNotContain(5, referenced);
        Assert.DoesNotContain(13, referenced);
        Assert.DoesNotContain(19, referenced);
        Assert.DoesNotContain(27, referenced);
        Assert.DoesNotContain(32, referenced);
    }

    /// <summary>The weapon chain runs a metre out in front of the chest, which is not a body part.</summary>
    /// <remarks>
    /// Its three rest translations sum to about +4444 units of forward travel — 1.08 m — off spine bone
    /// 1. Nothing anatomical does that, and bone 22 is exactly where <c>0x4600C0</c> hangs a held
    /// weapon.
    /// </remarks>
    [RequiresGameDataFact]
    public void TheWeaponChainReachesForwardOfTheBody()
    {
        BoneSkeleton rig = Skeletons().First(s => s.Id == 0);

        float forward = 0f;
        for (int bone = 20; bone <= 22; bone++)
        {
            forward += rig.RestTranslations[bone].Y;
        }

        Assert.True(forward > 4000f, $"the weapon chain only reaches {forward} units forward");

        // A whole arm, by contrast, reaches barely half a metre sideways.
        float sideways = 0f;
        for (int bone = 8; bone <= 13; bone++)
        {
            sideways += Math.Abs(rig.RestTranslations[bone].X);
        }

        Assert.True(sideways < 2500f, $"an arm reaches {sideways} units sideways");
    }

    private static int[] Parents(int first, int count) => HumanParents[first..(first + count)];

    private static IEnumerable<BoneSkeleton> Skeletons()
    {
        foreach (string path in Directory.EnumerateFiles(
            Path.Combine(GameData.RequireRoot(), "common", "anims"), "*.iff").Order(StringComparer.Ordinal))
        {
            yield return BoneAnimationSet.Read(File.ReadAllBytes(path)).Skeleton;
        }
    }

    private static IEnumerable<MefModel> Models()
    {
        foreach (string packPath in GameData.EnumerateFiles("*.mtp"))
        {
            if (UnusedPacks.Contains(Path.GetFileName(packPath), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string archivePath = Path.Combine(
                Path.GetDirectoryName(packPath)!,
                "models",
                Path.GetFileNameWithoutExtension(packPath) + ".res");
            if (!File.Exists(archivePath))
            {
                continue;
            }

            IresArchive archive = IresArchive.Read(File.ReadAllBytes(archivePath));
            foreach (IresEntry entry in archive.Entries)
            {
                if (entry.Kind != IresEntryKind.Body)
                {
                    continue;
                }

                MefModel model;
                try
                {
                    model = MefModel.Read(entry.Data);
                }
                catch (Exception ex) when (ex is PackFormatException or IlffFormatException)
                {
                    continue;
                }

                yield return model;
            }
        }
    }
}
