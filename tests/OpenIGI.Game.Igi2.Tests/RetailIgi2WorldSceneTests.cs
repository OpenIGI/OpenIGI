using OpenIGI.Formats;
using OpenIGI.Formats.Ilff;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// The world an I.G.I. 2 level places, walked out of its task tree and resolved against its own packs.
/// </summary>
/// <remarks>
/// The step between "the level loads" and "the level draws": every model the level names has to be one
/// it ships. Nothing here draws anything, which is the point — a scene that resolves is a scene worth
/// writing a renderer against, and one that does not is a renderer that would draw nothing and take a
/// while to say so.
/// </remarks>
public sealed class RetailIgi2WorldSceneTests
{
    /// <summary>Every level places a world, out of more task types than any one class would have covered.</summary>
    /// <remarks>
    /// Fifteen types place something across the corpus — <c>EditRigidObj</c> for most of it, then
    /// <c>Building</c>, <c>ExplodeObject</c>, <c>Fence</c>, <c>Ladder</c>, <c>Terminal</c> and the rest.
    /// Reading by parameter name rather than by class is what collects all of them without writing
    /// fifteen classes first.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_level_places_a_world()
    {
        int levels = 0;
        int placements = 0;
        HashSet<string> types = [];

        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            Igi2WorldScene scene = Igi2WorldScene.Build(harness.LevelRoot);
            levels++;
            placements += scene.Placements.Count;

            Assert.NotEmpty(scene.Placements);
            foreach (Igi2Placement placement in scene.Placements)
            {
                types.Add(placement.TypeName);
                Assert.False(string.IsNullOrWhiteSpace(placement.Model));
                Assert.InRange(placement.RenderGroup, 0, 15);
            }
        }

        Assert.Equal(24, levels);
        Assert.Equal(11_699, placements);
        // Fourteen, not fifteen: HumanPlayer became a class of its own when the sequel's player was
        // registered, and a character is not a static placement.
        Assert.Equal(20, types.Count);
    }

    /// <summary>
    /// A static prop's model is a file name, and the level or its location ships that file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The claim that makes the scene usable, and the one that caught the first version of
    /// <see cref="Igi2WorldScene.ModelResourceName"/>: level data already carries the level-of-detail
    /// suffix, so appending one resolved exactly nothing.
    /// </para>
    /// <para>
    /// A tolerance rather than an equality, because 370 of the 10,489 static placements name a model
    /// neither archive holds by file name. Those are not a decoding fault: they resolve through the
    /// level's <c>.mtp</c> pack, which this walk does not read and <c>RetailIgi2ModelTableTests</c>
    /// does — after which the whole corpus leaves only two unresolved.
    /// </para>
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_static_prop_names_a_model_file_that_ships()
    {
        int resolved = 0;
        int total = 0;

        foreach ((string _, IReadOnlyList<IresArchive> archives, Igi2WorldScene scene) in Levels())
        {
            foreach (Igi2Placement placement in scene.StaticPlacements)
            {
                total++;
                string name = Igi2WorldScene.ModelResourceName(placement.Model);
                resolved += archives.Any(a => a.TryGetEntry(name, out _)) ? 1 : 0;
            }
        }

        Assert.Equal(10_489, total);
        Assert.True(
            resolved >= total * 96 / 100,
            $"Only {resolved} of {total} static placements resolved, below the 96% the corpus reaches.");
    }

    /// <summary>A character's model is not a file name, which is why the world pass skips them.</summary>
    /// <remarks>
    /// The negative half, and the reason <see cref="Igi2WorldScene.CharacterTypes"/> exists. Level 1 asks
    /// for <c>rsg1_1</c>, <c>rsg2_1</c> and <c>rsg3_1</c>; the archives hold <c>rsg_1</c> to <c>rsg_4</c>,
    /// which are one model's levels of detail. Something between the two performs a lookup this port has
    /// not found yet, and pinning that it is needed stops a renderer being written as though it were not.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void A_character_model_does_not_resolve_as_a_file()
    {
        int resolved = 0;
        int total = 0;

        foreach ((string _, IReadOnlyList<IresArchive> archives, Igi2WorldScene scene) in Levels())
        {
            foreach (Igi2Placement placement in scene.Placements
                .Where(static p => Igi2WorldScene.CharacterTypes.Contains(p.TypeName)))
            {
                total++;
                string name = Igi2WorldScene.ModelResourceName(placement.Model);
                resolved += archives.Any(a => a.TryGetEntry(name, out _)) ? 1 : 0;
            }
        }

        Assert.True(total > 1000, $"Only {total} character placements were found.");
        Assert.True(
            resolved < total / 2,
            $"{resolved} of {total} character models resolved as files, which would make CharacterTypes wrong.");
    }

    /// <summary>Every model the world pass would draw parses as a mesh under the I.G.I. 2 profile.</summary>
    /// <remarks>
    /// The scene is worth only as much as the geometry behind it, so this reads each one rather than
    /// checking a name exists. Under the I.G.I. 1 profile every one would be refused on the version word,
    /// which is what makes the format profile a real gate rather than a formality.
    /// </remarks>
    [RequiresTitleDataFact("igi2")]
    public void Every_model_the_world_pass_draws_parses_as_a_mesh()
    {
        int meshes = 0;
        long triangles = 0;

        foreach ((string _, IReadOnlyList<IresArchive> archives, Igi2WorldScene scene) in Levels())
        {
            foreach (string model in scene.StaticPlacements
                .Select(static p => p.Model)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string name = Igi2WorldScene.ModelResourceName(model);
                IresEntry? entry = null;
                foreach (IresArchive archive in archives)
                {
                    if (archive.TryGetEntry(name, out entry))
                    {
                        break;
                    }
                }

                if (entry is null)
                {
                    continue;
                }

                MefModel mesh = MefModel.Read(entry.Data, AssetFormatProfile.Igi2);
                meshes++;
                triangles += mesh.DrawRecords.Sum(static r => r.Indices.Length / 3);
            }
        }

        Assert.True(meshes > 900, $"Only {meshes} distinct meshes were read.");
        Assert.True(triangles > 400_000, $"Only {triangles} triangles across the corpus.");
    }

    /// <summary>Each level with its model archives and its walked scene.</summary>
    /// <remarks>
    /// Two archives, because a level's own <c>models</c> holds what only it uses and its location's
    /// <c>common/models</c> holds what its neighbours share. A placement names a model without saying
    /// which, so the lookup tries both in that order — nearest first, which is also the order the
    /// original's search list resolves an alias in.
    /// </remarks>
    private static IEnumerable<(string Script, IReadOnlyList<IresArchive> Archives, Igi2WorldScene Scene)> Levels()
    {
        foreach (string script in RetailIgi2LevelLoadTests.LevelScripts())
        {
            string directory = Path.GetDirectoryName(script)!;
            List<IresArchive> archives = [];

            foreach (string candidate in new[]
                     {
                         Path.Combine(directory, "models"),
                         Path.Combine(Path.GetDirectoryName(directory)!, "common", "models"),
                     })
            {
                if (!Directory.Exists(candidate))
                {
                    continue;
                }

                archives.AddRange(Directory
                    .EnumerateFiles(candidate, "*.res")
                    .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
                    .Select(static p => IresArchive.Read(File.ReadAllBytes(p))));
            }

            if (archives.Count == 0)
            {
                continue;
            }

            RetailIgi2LevelLoadTests.Harness harness = new(script);
            harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);

            yield return (script, archives, Igi2WorldScene.Build(harness.LevelRoot));
        }
    }
}
