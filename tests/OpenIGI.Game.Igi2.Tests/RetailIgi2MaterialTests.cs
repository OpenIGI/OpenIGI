using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.Textures;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The last link of the world chain: from a placed model's draw record to the pixels it samples.
/// </summary>
/// <remarks>
/// <para>
/// A level names a virtual model; the pack turns it into a mesh and a set of material slots; the mesh's
/// draw records name slots; <c>GTT</c> and <c>TEXF</c> turn a slot into a texture name; the level's
/// <c>textures/*.res</c> holds its pixels. Every step of that was established separately, and this walks
/// all of it end to end over the whole corpus, because a chain is exactly the kind of thing where every
/// link passes its own test and the join still fails.
/// </para>
/// <para>
/// Nothing here draws. It establishes that a renderer asking these questions gets an answer.
/// </para>
/// </remarks>
public sealed class RetailIgi2MaterialTests
{
    /// <summary>Every material slot a placed mesh names resolves to a texture the level ships.</summary>
    /// <remarks>
    /// <para>
    /// The counts are exact because the corpus is fixed, and they are the point: a regression in any of
    /// the five links moves one of them, and which one moves says which link broke.
    /// </para>
    /// <para>
    /// Records with no slot are not failures — <see cref="MefDrawRecord.NoMaterialSlot"/> means the record
    /// draws untextured, which is a thing the format says and the renderer has to honour.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_material_slot_of_every_placed_mesh_resolves_to_a_shipped_texture()
    {
        int meshes = 0;
        int records = 0;
        int untextured = 0;
        int resolved = 0;
        int missing = 0;
        HashSet<string> textures = new(StringComparer.OrdinalIgnoreCase);

        foreach (Level level in Levels())
        {
            // One mesh is placed many times and wears the same textures every time, so the walk is over
            // the distinct (virtual model, mesh) pairs rather than over all 10,569 placements.
            foreach (string virtualModel in level.Scene.Placements
                .Select(static p => p.Model)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!level.Table.TryResolveMesh(virtualModel, out string? mesh)
                    || !TryRead(level.Models, Igi2WorldScene.ModelResourceName(mesh), out ReadOnlyMemory<byte> bytes))
                {
                    continue;
                }

                meshes++;
                foreach (MefDrawRecord record in MefModel.Read(bytes, AssetFormatProfile.Igi2).DrawRecords)
                {
                    records++;
                    if (!record.HasMaterial)
                    {
                        untextured++;
                        continue;
                    }

                    if (!level.Table.TryResolveTexture(virtualModel, record.MaterialSlot, out string? texture))
                    {
                        missing++;
                        continue;
                    }

                    resolved++;
                    textures.Add(texture);
                    Assert.True(
                        TryRead(level.Textures, Igi2ModelTable.TextureResourceName(texture), out ReadOnlyMemory<byte> pixels),
                        $"'{virtualModel}' slot {record.MaterialSlot} wants '{texture}', which the level does not ship.");
                    Assert.Equal(11u, TexFile.ReadVersion(pixels.Span));
                }
            }
        }

        Assert.Equal(1_564, meshes);
        Assert.Equal(5_807, records);

        // Not one slot goes unanswered. This started at 358 and the residue was the finding: the missing
        // slots all belonged to meshes resolved against the wrong pack, so a mesh arrived wearing another
        // model's material list. Fixing the pack made the number zero rather than smaller, which is what
        // says the cause was found rather than the symptom trimmed.
        Assert.Equal(0, missing);
        Assert.Equal(records - untextured, resolved);
        Assert.Equal(2, untextured);
        Assert.Equal(918, textures.Count);
    }

    /// <summary>Two uniforms of one soldier mesh really do sample different textures.</summary>
    /// <remarks>
    /// The measurement above would pass just as well if every slot resolved to the same texture, so this
    /// pins the thing the table exists for. <c>location1</c>'s <c>rsg1_1</c> and <c>rsg3_1</c> are one
    /// mesh with slot sets <c>[2,3]</c> and <c>[2,5]</c>; they share their first texture and differ in
    /// their second, which is the uniform.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void One_mesh_in_two_uniforms_samples_two_different_textures()
    {
        string path = Path.Combine(
            GameDataSource.Igi2.RequireRoot(), "missions", "location1", "common", "location1.mtp");
        Igi2ModelTable table = Igi2ModelTable.Create(MtpPack.Read(File.ReadAllBytes(path)), null);

        Assert.True(table.TryResolveTexture("rsg1_1", 0, out string? firstShared));
        Assert.True(table.TryResolveTexture("rsg3_1", 0, out string? thirdShared));
        Assert.Equal(firstShared, thirdShared);

        Assert.True(table.TryResolveTexture("rsg1_1", 1, out string? firstUniform));
        Assert.True(table.TryResolveTexture("rsg3_1", 1, out string? thirdUniform));
        Assert.NotEqual(firstUniform, thirdUniform);
    }

    private static bool TryRead(IReadOnlyList<IresArchive> archives, string resource, out ReadOnlyMemory<byte> bytes)
    {
        foreach (IresArchive archive in archives)
        {
            if (archive.TryGetEntry(resource, out IresEntry? entry))
            {
                bytes = entry.Data;
                return true;
            }
        }

        bytes = default;
        return false;
    }

    private static IEnumerable<Level> Levels()
    {
        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string directory = Path.GetDirectoryName(script)!;
            string commonDirectory = Path.Combine(Path.GetDirectoryName(directory)!, "common");

            string? levelPack = Directory.EnumerateFiles(directory, "*.mtp").FirstOrDefault();
            if (levelPack is null)
            {
                continue;
            }

            string? commonPack = Directory.Exists(commonDirectory)
                ? Directory.EnumerateFiles(commonDirectory, "*.mtp").FirstOrDefault()
                : null;

            List<IresArchive> models = Archives(directory, commonDirectory, "models");
            List<IresArchive> textures = Archives(directory, commonDirectory, "textures");
            if (models.Count == 0 || textures.Count == 0)
            {
                continue;
            }

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            yield return new Level(
                Igi2WorldScene.Build(harness.LevelRoot),
                Igi2ModelTable.Create(
                    MtpPack.Read(File.ReadAllBytes(levelPack)),
                    commonPack is null ? null : MtpPack.Read(File.ReadAllBytes(commonPack))),
                models,
                textures);
        }
    }

    private static List<IresArchive> Archives(string directory, string commonDirectory, string kind)
    {
        List<IresArchive> archives = [];
        foreach (string root in new[] { Path.Combine(directory, kind), Path.Combine(commonDirectory, kind) })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            archives.AddRange(Directory
                .EnumerateFiles(root, "*.res")
                .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
                .Select(static p => IresArchive.Read(File.ReadAllBytes(p))));
        }

        return archives;
    }

    private sealed record Level(
        Igi2WorldScene Scene,
        Igi2ModelTable Table,
        IReadOnlyList<IresArchive> Models,
        IReadOnlyList<IresArchive> Textures);
}
