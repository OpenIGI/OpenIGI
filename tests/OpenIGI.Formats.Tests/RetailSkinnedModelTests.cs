using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Checks the skinned mesh format over every model in the retail corpus.
/// </summary>
/// <remarks>
/// Mesh format 1 was previously read as "the lightmapped layout, just never batched". It is the
/// <b>animated</b> format: the draw path takes the bone branch on exactly this format, and the last
/// eight bytes of a vertex carry a skin binding rather than a lightmap UV. The evidence is arithmetic —
/// the render header's two counts sum to the vertex array, and the weights sum to one per vertex — and
/// neither holds under the old reading.
/// </remarks>
public sealed class RetailSkinnedModelTests
{
    private static readonly string[] UnusedPacks = ["common.mtp", "menusystem.mtp"];

    [RequiresGameDataFact]
    public void TheCorpusSplitsIntoTheThreeFormatsTheOriginalAccepts()
    {
        Dictionary<MefMeshFormat, int> counts = [];
        foreach (MefModel model in Models())
        {
            counts[model.Format] = counts.GetValueOrDefault(model.Format) + 1;
        }

        // The census M6 established, unchanged — reading format 1 differently does not move a model
        // between formats, it only changes what the last eight bytes of its vertices mean.
        Assert.Equal(630, counts[MefMeshFormat.Diffuse]);
        Assert.Equal(537, counts[MefMeshFormat.Skinned]);
        Assert.Equal(5628, counts[MefMeshFormat.Lightmapped]);
    }

    [RequiresGameDataFact]
    public void EverySkinnedModelSplitsItsArrayIntoBaseVerticesAndExtraInfluences()
    {
        int skinned = 0;
        foreach (MefModel model in Models())
        {
            if (model.Format != MefMeshFormat.Skinned)
            {
                // Every other format is all base vertices and carries no bindings at all.
                Assert.Equal(model.Vertices.Length, model.BaseVertexCount);
                Assert.Equal(0, model.ExtraInfluenceCount);
                Assert.Empty(model.SkinInfluences);
                continue;
            }

            skinned++;
            Assert.Equal(model.Vertices.Length, model.BaseVertexCount + model.ExtraInfluenceCount);
            Assert.Equal(model.Vertices.Length, model.SkinInfluences.Count);
            Assert.True(model.ExtraInfluenceCount > 0);
        }

        Assert.Equal(537, skinned);
    }

    [RequiresGameDataFact]
    public void EveryVertexsInfluencesSumToOne()
    {
        // The decisive test. A base vertex's own weight plus every extra influence naming it comes to
        // exactly 1.0 — which is meaningless if those eight bytes were a lightmap UV, and is the whole
        // reason for reading them as a skin binding.
        int models = 0;
        foreach (MefModel model in Models())
        {
            if (model.Format != MefMeshFormat.Skinned)
            {
                continue;
            }

            models++;
            double[] total = new double[model.BaseVertexCount];
            for (int i = 0; i < model.SkinInfluences.Count; i++)
            {
                MefSkinInfluence influence = model.SkinInfluences[i];
                int target = i < model.BaseVertexCount ? i : influence.TargetVertex;

                Assert.InRange(target, 0, model.BaseVertexCount - 1);
                total[target] += influence.Weight;
            }

            Assert.All(total, t => Assert.Equal(1.0, t, 2));
        }

        Assert.Equal(537, models);
    }

    [RequiresGameDataFact]
    public void EveryBoneIndexFitsAShippedSkeleton()
    {
        // The largest skeleton the game ships has 56 bones, and no model reaches past 48. That is the
        // only constraint a model places on its skeleton: the .mef does not name one.
        int highest = -1;
        foreach (MefModel model in Models())
        {
            if (model.Format != MefMeshFormat.Skinned)
            {
                continue;
            }

            foreach (MefSkinInfluence influence in model.SkinInfluences)
            {
                Assert.InRange(influence.BoneIndex, 0, 55);
                highest = Math.Max(highest, influence.BoneIndex);
            }
        }

        Assert.Equal(48, highest);
    }

    [RequiresGameDataFact]
    public void OnlyTwoModelsNeedTheFiftySixBoneSkeleton()
    {
        // 005.iff is the odd skeleton out at 56 bones; everything else is 33. Exactly two models carry
        // an index past 32, and they are the pair the engine spawns against skeleton 5.
        List<string> wide = [];
        foreach ((string name, MefModel model) in NamedModels())
        {
            if (model.Format != MefMeshFormat.Skinned)
            {
                continue;
            }

            foreach (MefSkinInfluence influence in model.SkinInfluences)
            {
                if (influence.BoneIndex >= 33)
                {
                    wide.Add(name);
                    break;
                }
            }
        }

        Assert.Equal(2, wide.Count);
        Assert.All(wide, n => Assert.Contains("005_01_", n, StringComparison.Ordinal));
    }

    private static IEnumerable<MefModel> Models() => NamedModels().Select(m => m.Model);

    private static IEnumerable<(string Name, MefModel Model)> NamedModels()
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

                yield return (entry.Name, model);
            }
        }
    }
}
