using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Runs the pack, model and object-lightmap loaders over every matching file in a retail installation.
/// </summary>
/// <remarks>
/// <para>
/// These assert the invariants that the M6 reverse-engineering pass established over the whole corpus —
/// 15 packs and 6795 models — rather than over a sample. Every one of them caught something: the
/// byte-exact <c>INST</c> walk, the vertex stride derived from the D3D vertex format, the draw-record walk
/// filling its chunk exactly, and above all the material slot bound to the <em>instance</em> rather than
/// positionally to the model, which an earlier reading got wrong on 1625 models.
/// </para>
/// </remarks>
public sealed class RetailWorldGoldenTests
{
    /// <summary>The packs that have no sibling models archive and that <c>Mission_Load</c> never opens.</summary>
    private static readonly string[] UnusedPacks = ["common.mtp", "menusystem.mtp"];

    [RequiresGameDataFact]
    public void EveryPackDecodes()
    {
        IReadOnlyList<string> packs = GameData.EnumerateFiles("*.mtp");
        Assert.NotEmpty(packs);

        int instances = 0;
        int materials = 0;
        List<string> failures = [];

        foreach (string path in packs)
        {
            try
            {
                MtpPack pack = MtpPack.Read(File.ReadAllBytes(path));

                // The material table is indexed by the geometry ids in INST and has one entry per texture.
                Assert.Equal(pack.TextureNames.Count, pack.Materials.Count);

                foreach (MtpInstance instance in pack.Instances)
                {
                    Assert.False(instance.Model.IsNone);
                    Assert.InRange(instance.Model.Index, 0, pack.ModelNames.Count - 1);
                    Assert.False(instance.Model.IsCommon, "no retail pack reaches into the common id space");
                }

                foreach (MtpMaterial material in pack.Materials)
                {
                    Assert.False(material.Texture.IsNone);
                    Assert.InRange(material.Texture.Index, 0, pack.TextureNames.Count - 1);

                    // Every GTT entry in every retail pack says "no lightmap"; object lightmaps come from
                    // lightmaps.res instead. If this ever fails, re-read world-loading.md §3.3 first.
                    Assert.True(material.Lightmap.IsNone, $"{Path.GetFileName(path)} has a non-null lightmap id");
                }

                // Retail packs leave these three empty; the loader still has to handle them.
                Assert.Empty(pack.SoundNames);
                Assert.Empty(pack.PaletteNames);
                Assert.Empty(pack.VolumeNames);

                instances += pack.Instances.Count;
                materials += pack.Materials.Count;
            }
            catch (PackFormatException ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
        Assert.True(instances > 3000, $"expected thousands of instances, decoded {instances}");
        Assert.True(materials > 3000, $"expected thousands of materials, decoded {materials}");
    }

    [RequiresGameDataFact]
    public void EveryModelDecodes()
    {
        int models = 0;
        int emptyModels = 0;
        Dictionary<MefMeshFormat, int> formats = [];
        List<string> failures = [];

        foreach ((string packPath, string archivePath) in PacksWithModels())
        {
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(archivePath));
            foreach (IresEntry entry in archive.Entries)
            {
                try
                {
                    MefModel model = MefModel.Read(entry.Data);
                    models++;
                    formats[model.Format] = formats.GetValueOrDefault(model.Format) + 1;

                    if (model.IsEmpty)
                    {
                        emptyModels++;
                        continue;
                    }

                    // Human_SetModel copies MESH+0x78 into Human+0xA0, which the retail binocular
                    // detector uses as its projected marker half-extent. Keep this field finite and
                    // positive across the full retail model corpus so rendering cannot silently fall
                    // back to a guessed geometry radius.
                    Assert.True(float.IsFinite(model.NativeMarkerBound));
                    Assert.True(model.NativeMarkerBound > 0f);

                    // The stride check lives in the loader; this asserts the result is usable geometry.
                    Assert.Equal(model.TriangleCount, model.DrawRecords.Sum(r => r.TriangleCount));
                    foreach (MefDrawRecord record in model.DrawRecords)
                    {
                        foreach (ushort index in record.Indices.Span)
                        {
                            Assert.InRange(record.ToVertexIndex(index), 0, model.Vertices.Length - 1);
                        }
                    }

                    // A lightmapped model's records tile its vertex array end to end.
                    if (model.Format == MefMeshFormat.Lightmapped)
                    {
                        // The page table, the render header's page count and the largest page any draw
                        // record names all agree, for every lightmapped model in the corpus. That triple
                        // agreement is what identifies the record field at offset 18 as a page index.
                        int highestPage = -1;
                        foreach (MefDrawRecord record in model.DrawRecords)
                        {
                            highestPage = Math.Max(highestPage, record.LightmapPage);
                        }

                        if (model.DrawRecords.Count > 0)
                        {
                            Assert.Equal(model.LightmapPages.Count, highestPage + 1);
                        }

                        foreach (MefLightmapPage page in model.LightmapPages)
                        {
                            Assert.InRange(page.Width, 1, 256);
                            Assert.InRange(page.Height, 1, 256);
                        }

                        int running = 0;
                        foreach (MefDrawRecord record in model.DrawRecords)
                        {
                            Assert.Equal(running, record.BaseVertex);
                            running += record.VertexSpan;
                        }

                        Assert.Equal(model.Vertices.Length, running);
                    }
                    else
                    {
                        Assert.Empty(model.LightmapPages);
                    }
                }
                catch (Exception ex) when (ex is PackFormatException or IlffFormatException)
                {
                    failures.Add($"{Path.GetFileName(archivePath)}:{entry.Name}: {ex.Message}");
                }
            }
        }

        Assert.Empty(failures);
        Assert.True(models > 6000, $"expected the whole model corpus, decoded {models}");
        Assert.Equal(3, formats.Count);
        Assert.True(formats[MefMeshFormat.Lightmapped] > formats[MefMeshFormat.Diffuse]);
        Assert.True(emptyModels < models / 10, $"{emptyModels} of {models} models are empty, which is too many");
    }

    [RequiresGameDataFact]
    public void EveryDrawRecordMaterialSlotAddressesItsInstance()
    {
        int checkedRecords = 0;
        List<string> failures = [];

        foreach ((string packPath, string archivePath) in PacksWithModels())
        {
            MtpPack pack = MtpPack.Read(File.ReadAllBytes(packPath));
            Dictionary<string, MefModel> byName = LoadModelsByName(archivePath);

            foreach (MtpInstance instance in pack.Instances)
            {
                string modelName = pack.ModelNames[instance.Model.Index];
                if (!byName.TryGetValue(modelName, out MefModel? model))
                {
                    failures.Add($"{Path.GetFileName(packPath)}: no model '{modelName}' in the archive");
                    continue;
                }

                foreach (MefDrawRecord record in model.DrawRecords)
                {
                    if (!record.HasMaterial)
                    {
                        continue;
                    }

                    if (!pack.TryGetMaterial(instance, record.MaterialSlot, out MtpMaterial material))
                    {
                        failures.Add(
                            $"{Path.GetFileName(packPath)}: {instance.Name} slot {record.MaterialSlot} of {instance.SlotCount}");
                        continue;
                    }

                    Assert.InRange(material.Texture.Index, 0, pack.TextureNames.Count - 1);
                    checkedRecords++;
                }
            }
        }

        Assert.Empty(failures);
        Assert.True(checkedRecords > 10000, $"expected the whole corpus of draw records, checked {checkedRecords}");
    }

    [RequiresGameDataFact]
    public void EveryObjectLightmapDecodes()
    {
        IReadOnlyList<string> archives = GameData.EnumerateFiles("lightmaps.res");
        Assert.NotEmpty(archives);

        int maps = 0;
        int multiImage = 0;
        List<string> failures = [];

        foreach (string path in archives)
        {
            IresArchive archive = IresArchive.Read(File.ReadAllBytes(path));
            foreach (IresEntry entry in archive.Entries)
            {
                try
                {
                    ObjectLightmap map = ObjectLightmap.Read(entry.Data);
                    Assert.Equal(0.12f, map.Version, 3);
                    Assert.NotEmpty(map.Images);
                    foreach (ObjectLightmapImage image in map.Images)
                    {
                        Assert.Equal(4 * image.Width * image.Height, image.Pixels.Length);
                        Assert.True(image.Width > 0 && image.Height > 0);
                    }

                    multiImage += map.Images.Count > 1 ? 1 : 0;

                    Assert.True(
                        ObjectLightmap.TryParseName(entry.Name, out string objectName, out int recordIndex),
                        $"could not split '{entry.Name}'");
                    Assert.NotEmpty(objectName);
                    Assert.True(recordIndex >= 0);
                    maps++;
                }
                catch (PackFormatException ex)
                {
                    failures.Add($"{Path.GetFileName(path)}:{entry.Name}: {ex.Message}");
                }
            }
        }

        Assert.Empty(failures);
        Assert.True(maps > 25000, $"expected the whole lightmap corpus, decoded {maps}");

        // Thirteen entries across the corpus hold more than one image. They are the reason the header is
        // 60 + 44*count rather than the flat 104 bytes the single-image majority suggests.
        Assert.Equal(13, multiImage);
    }

    [RequiresGameDataFact]
    public void EveryDatIndexAgreesWithItsPack()
    {
        int entries = 0;
        List<string> failures = [];

        foreach (string datPath in GameData.EnumerateFiles("*.dat"))
        {
            string packPath = Path.ChangeExtension(datPath, ".mtp");
            if (!File.Exists(packPath))
            {
                // graphs/graphN.dat are AI navigation graphs, a different format entirely.
                continue;
            }

            DatIndex index = DatIndex.Parse(File.ReadAllText(datPath));
            MtpPack pack = MtpPack.Read(File.ReadAllBytes(packPath));

            // The .dat is build output the runtime ignores, so this is a cross-check of our MODS decode.
            if (index.Entries.Count != pack.ModelNames.Count)
            {
                failures.Add($"{Path.GetFileName(datPath)}: {index.Entries.Count} entries vs {pack.ModelNames.Count} models");
                continue;
            }

            for (int i = 0; i < index.Entries.Count; i++)
            {
                Assert.Equal(pack.ModelNames[i], index.Entries[i].Model);
            }

            entries += index.Entries.Count;
        }

        Assert.Empty(failures);
        Assert.True(entries > 3000, $"expected the whole model corpus, cross-checked {entries}");
    }

    private static Dictionary<string, MefModel> LoadModelsByName(string archivePath)
    {
        Dictionary<string, MefModel> byName = new(StringComparer.OrdinalIgnoreCase);
        IresArchive archive = IresArchive.Read(File.ReadAllBytes(archivePath));
        foreach (IresEntry entry in archive.Entries)
        {
            string name = Path.GetFileNameWithoutExtension(entry.Name.Replace('\\', '/'));
            byName[name] = MefModel.Read(entry.Data);
        }

        return byName;
    }

    private static IEnumerable<(string PackPath, string ArchivePath)> PacksWithModels()
    {
        foreach (string packPath in GameData.EnumerateFiles("*.mtp"))
        {
            string name = Path.GetFileName(packPath);
            if (UnusedPacks.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string archivePath = Path.Combine(
                Path.GetDirectoryName(packPath)!,
                "models",
                Path.GetFileNameWithoutExtension(packPath) + ".res");

            Assert.True(File.Exists(archivePath), $"{name} has no models archive at {archivePath}");
            yield return (packPath, archivePath);
        }
    }
}
